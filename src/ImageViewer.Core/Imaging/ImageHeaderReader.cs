using System.Buffers.Binary;
using System.Text;

namespace ImageViewer.Core.Imaging;

/// <summary>Сведения, извлечённые из заголовка файла без полного декодирования.</summary>
public readonly record struct ImageHeader(ImageFormatKind Format, int Width, int Height, int BitsPerPixel)
{
    public bool HasSize => Width > 0 && Height > 0;

    public static ImageHeader Unknown { get; } = new(ImageFormatKind.Unknown, 0, 0, 0);
}

/// <summary>
/// Быстрое чтение размеров и глубины цвета из заголовков PNG, JPEG, GIF, BMP, TIFF, WebP, ICO.
/// Не требует внешних библиотек и работает на любой платформе.
/// </summary>
public static class ImageHeaderReader
{
    private const int PrefixSize = 64;

    public static ImageHeader Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Read(stream);
    }

    /// <summary>Читает заголовок из потока. Для JPEG и TIFF поток должен поддерживать Seek.</summary>
    public static ImageHeader Read(Stream stream)
    {
        var buffer = new byte[PrefixSize];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var h = buffer.AsSpan(0, read);
        var format = ImageFormats.DetectFromSignature(h);

        try
        {
            return format switch
            {
                ImageFormatKind.Png => ReadPng(h),
                ImageFormatKind.Gif => ReadGif(h),
                ImageFormatKind.Bmp => ReadBmp(h),
                ImageFormatKind.WebP => ReadWebP(h),
                ImageFormatKind.Ico => ReadIco(h),
                ImageFormatKind.Jpeg when stream.CanSeek => ReadJpeg(stream),
                ImageFormatKind.Tiff when stream.CanSeek => ReadTiff(stream),
                _ => new ImageHeader(format, 0, 0, 0)
            };
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentOutOfRangeException
                                       or IndexOutOfRangeException or IOException)
        {
            // Повреждённый или усечённый файл: формат известен, размеры — нет.
            return new ImageHeader(format, 0, 0, 0);
        }
    }

    private static ImageHeader ReadPng(ReadOnlySpan<byte> h)
    {
        // 8 байт сигнатуры, затем чанк IHDR: длина(4) "IHDR"(4) width(4) height(4) depth(1) colorType(1)
        int width = BinaryPrimitives.ReadInt32BigEndian(h[16..]);
        int height = BinaryPrimitives.ReadInt32BigEndian(h[20..]);
        int bitDepth = h[24];
        int channels = h[25] switch
        {
            0 => 1, // grayscale
            2 => 3, // RGB
            3 => 1, // palette
            4 => 2, // grayscale + alpha
            6 => 4, // RGBA
            _ => 0
        };
        return new ImageHeader(ImageFormatKind.Png, width, height, channels * bitDepth);
    }

    private static ImageHeader ReadGif(ReadOnlySpan<byte> h)
    {
        int width = BinaryPrimitives.ReadUInt16LittleEndian(h[6..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(h[8..]);
        int bpp = (h[10] & 0x07) + 1;
        return new ImageHeader(ImageFormatKind.Gif, width, height, bpp);
    }

    private static ImageHeader ReadBmp(ReadOnlySpan<byte> h)
    {
        int dibSize = BinaryPrimitives.ReadInt32LittleEndian(h[14..]);
        if (dibSize == 12) // старый BITMAPCOREHEADER (OS/2)
        {
            return new ImageHeader(ImageFormatKind.Bmp,
                BinaryPrimitives.ReadUInt16LittleEndian(h[18..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[20..]),
                BinaryPrimitives.ReadUInt16LittleEndian(h[24..]));
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(h[18..]);
        int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(h[22..])); // < 0 — top-down
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(h[28..]);
        return new ImageHeader(ImageFormatKind.Bmp, width, height, bpp);
    }

    private static ImageHeader ReadIco(ReadOnlySpan<byte> h)
    {
        // ICONDIR (6 байт) + первая запись ICONDIRENTRY (16 байт)
        int width = h[6] == 0 ? 256 : h[6];
        int height = h[7] == 0 ? 256 : h[7];
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(h[12..]);
        return new ImageHeader(ImageFormatKind.Ico, width, height, bpp);
    }

    private static ImageHeader ReadWebP(ReadOnlySpan<byte> h)
    {
        var chunk = Encoding.ASCII.GetString(h.Slice(12, 4));
        switch (chunk)
        {
            case "VP8 ": // lossy
            {
                int width = BinaryPrimitives.ReadUInt16LittleEndian(h[26..]) & 0x3FFF;
                int height = BinaryPrimitives.ReadUInt16LittleEndian(h[28..]) & 0x3FFF;
                return new ImageHeader(ImageFormatKind.WebP, width, height, 24);
            }
            case "VP8L": // lossless
            {
                byte b0 = h[21], b1 = h[22], b2 = h[23], b3 = h[24];
                int width = 1 + (b0 | ((b1 & 0x3F) << 8));
                int height = 1 + ((b1 >> 6) | (b2 << 2) | ((b3 & 0x0F) << 10));
                return new ImageHeader(ImageFormatKind.WebP, width, height, 32);
            }
            case "VP8X": // extended
            {
                bool alpha = (h[20] & 0x10) != 0;
                int width = 1 + (h[24] | (h[25] << 8) | (h[26] << 16));
                int height = 1 + (h[27] | (h[28] << 8) | (h[29] << 16));
                return new ImageHeader(ImageFormatKind.WebP, width, height, alpha ? 32 : 24);
            }
            default:
                return new ImageHeader(ImageFormatKind.WebP, 0, 0, 0);
        }
    }

    private static ImageHeader ReadJpeg(Stream s)
    {
        // Последовательно проходим маркеры до SOFn (Start Of Frame), где лежат размеры.
        s.Position = 2;
        while (true)
        {
            int b = s.ReadByte();
            if (b == -1) break;
            if (b != 0xFF) continue;

            int marker;
            do { marker = s.ReadByte(); } while (marker == 0xFF);
            if (marker == -1) break;

            // Маркеры без поля длины
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) continue;
            if (marker is 0xD9 or 0xDA) break; // конец изображения / начало сканов

            int length = ReadUInt16BigEndian(s);
            bool isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                int precision = ReadByteOrThrow(s);
                int height = ReadUInt16BigEndian(s);
                int width = ReadUInt16BigEndian(s);
                int components = ReadByteOrThrow(s);
                return new ImageHeader(ImageFormatKind.Jpeg, width, height, precision * components);
            }

            s.Seek(length - 2, SeekOrigin.Current);
        }

        return new ImageHeader(ImageFormatKind.Jpeg, 0, 0, 0);
    }

    private static ImageHeader ReadTiff(Stream s)
    {
        s.Position = 0;
        var head = new byte[8];
        s.ReadExactly(head);
        bool le = head[0] == 'I';

        uint ifdOffset = U32(head.AsSpan(4), le);
        s.Position = ifdOffset;
        var countBytes = new byte[2];
        s.ReadExactly(countBytes);
        int count = U16(countBytes, le);

        var entries = new byte[count * 12];
        s.ReadExactly(entries);

        int width = 0, height = 0, bitsPerSample = 1, samples = 1;
        for (int i = 0; i < count; i++)
        {
            var e = entries.AsSpan(i * 12, 12);
            int tag = U16(e, le);
            int type = U16(e[2..], le);
            uint n = U32(e[4..], le);
            var value = e[8..];
            // SHORT (type 3) лежит в первых двух байтах поля значения, LONG — во всех четырёх
            int inline = type == 3 ? U16(value, le) : (int)U32(value, le);

            switch (tag)
            {
                case 256: width = inline; break;
                case 257: height = inline; break;
                case 277: samples = inline; break;
                case 258:
                    if (n <= 2) bitsPerSample = U16(value, le);
                    else
                    {
                        // Массив значений лежит по смещению; берём первое (обычно все одинаковые).
                        long back = s.Position;
                        s.Position = U32(value, le);
                        var tmp = new byte[2];
                        s.ReadExactly(tmp);
                        bitsPerSample = U16(tmp, le);
                        s.Position = back;
                    }
                    break;
            }
        }

        return new ImageHeader(ImageFormatKind.Tiff, width, height, bitsPerSample * samples);
    }

    private static ushort U16(ReadOnlySpan<byte> b, bool le) =>
        le ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);

    private static uint U32(ReadOnlySpan<byte> b, bool le) =>
        le ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);

    private static int ReadByteOrThrow(Stream s)
    {
        int b = s.ReadByte();
        return b == -1 ? throw new EndOfStreamException() : b;
    }

    private static int ReadUInt16BigEndian(Stream s) => (ReadByteOrThrow(s) << 8) | ReadByteOrThrow(s);
}
