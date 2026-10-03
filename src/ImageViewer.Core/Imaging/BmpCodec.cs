using System.Buffers.Binary;

namespace ImageViewer.Core.Imaging;

/// <summary>
/// Простой кроссплатформенный декодер BMP на чистом C# (8, 24 и 32 бита, без сжатия).
/// Нужен, чтобы библиотека и её тесты работали без внешних зависимостей.
/// </summary>
public sealed class BmpDecoder : ImageDecoderBase
{
    private const int BI_RGB = 0;
    private const int BI_BITFIELDS = 3;

    public BmpDecoder() : base("Встроенный BMP-декодер", [".bmp", ".dib"])
    {
    }

    public override RawImage DecodeStream(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] data;
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            data = ms.ToArray();
        }

        var span = data.AsSpan();
        if (span.Length < 54 || span[0] != 'B' || span[1] != 'M')
            throw new InvalidDataException("Файл не является BMP.");

        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(span[10..]);
        int dibSize = BinaryPrimitives.ReadInt32LittleEndian(span[14..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(span[18..]);
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(span[22..]);
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(span[30..]);
        int colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(span[46..]);

        if (dibSize < 40)
            throw new NotSupportedException("Устаревший формат заголовка BMP не поддерживается.");
        if (width <= 0 || rawHeight == 0)
            throw new InvalidDataException("Некорректные размеры BMP.");

        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);
        bool supported = (bpp is 8 or 24 && compression == BI_RGB) ||
                         (bpp == 32 && compression is BI_RGB or BI_BITFIELDS);
        if (!supported)
            throw new NotSupportedException($"BMP {bpp} бит, сжатие {compression} не поддерживается встроенным декодером.");

        // Палитра для 8-битных изображений
        byte[]? palette = null;
        if (bpp == 8)
        {
            int entries = colorsUsed > 0 ? Math.Min(colorsUsed, 256) : 256;
            palette = span.Slice(14 + dibSize, entries * 4).ToArray();
        }

        int srcStride = (width * bpp + 31) / 32 * 4;
        if ((long)pixelOffset + (long)srcStride * height > span.Length)
            throw new InvalidDataException("Файл BMP усечён.");

        var image = new RawImage(width, height);
        var dst = image.Pixels;

        for (int y = 0; y < height; y++)
        {
            if ((y & 63) == 0) cancellationToken.ThrowIfCancellationRequested();

            int srcRow = topDown ? y : height - 1 - y;
            var row = span.Slice(pixelOffset + srcRow * srcStride, srcStride);
            int d = y * image.Stride;

            for (int x = 0; x < width; x++, d += 4)
            {
                switch (bpp)
                {
                    case 8:
                        int p = row[x] * 4;
                        if (p + 2 < palette!.Length)
                        {
                            dst[d] = palette[p];
                            dst[d + 1] = palette[p + 1];
                            dst[d + 2] = palette[p + 2];
                        }
                        dst[d + 3] = 255;
                        break;
                    case 24:
                        dst[d] = row[x * 3];
                        dst[d + 1] = row[x * 3 + 1];
                        dst[d + 2] = row[x * 3 + 2];
                        dst[d + 3] = 255;
                        break;
                    default: // 32
                        dst[d] = row[x * 4];
                        dst[d + 1] = row[x * 4 + 1];
                        dst[d + 2] = row[x * 4 + 2];
                        // В BI_RGB альфа-канал официально не используется — считаем пиксель непрозрачным
                        dst[d + 3] = compression == BI_BITFIELDS ? row[x * 4 + 3] : (byte)255;
                        break;
                }
            }
        }

        return image;
    }
}

/// <summary>Запись <see cref="RawImage"/> в 24-битный BMP (используется для генерации демо-изображений и в тестах).</summary>
public static class BmpEncoder
{
    public static void Save(RawImage image, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(image, fs);
    }

    public static void Save(RawImage image, Stream stream)
    {
        int stride = (image.Width * 3 + 3) / 4 * 4;
        int pixelBytes = stride * image.Height;
        const int headerSize = 14 + 40;

        var header = new byte[headerSize];
        var h = header.AsSpan();
        h[0] = (byte)'B';
        h[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(h[2..], headerSize + pixelBytes); // размер файла
        BinaryPrimitives.WriteInt32LittleEndian(h[10..], headerSize);             // смещение пикселей
        BinaryPrimitives.WriteInt32LittleEndian(h[14..], 40);                     // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(h[18..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(h[22..], image.Height);           // bottom-up
        BinaryPrimitives.WriteUInt16LittleEndian(h[26..], 1);                     // planes
        BinaryPrimitives.WriteUInt16LittleEndian(h[28..], 24);                    // bpp
        BinaryPrimitives.WriteInt32LittleEndian(h[34..], pixelBytes);
        BinaryPrimitives.WriteInt32LittleEndian(h[38..], 2835);                   // 72 DPI
        BinaryPrimitives.WriteInt32LittleEndian(h[42..], 2835);
        stream.Write(header);

        var row = new byte[stride];
        var src = image.Pixels;
        for (int y = image.Height - 1; y >= 0; y--)
        {
            int s = y * image.Stride;
            for (int x = 0; x < image.Width; x++, s += 4)
            {
                row[x * 3] = src[s];
                row[x * 3 + 1] = src[s + 1];
                row[x * 3 + 2] = src[s + 2];
            }
            stream.Write(row);
        }
    }
}
