using System.Buffers.Binary;
using System.Text;
using ImageViewer.Core.Files;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Core.Tests;

public class ImageHeaderReaderTests
{
    private static ImageHeader Read(byte[] data) => ImageHeaderReader.Read(new MemoryStream(data));

    [Fact]
    public void Png_ReadsSizeAndDepth()
    {
        var data = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 13);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(data, 12);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 1920);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 1080);
        data[24] = 8; // бит на канал
        data[25] = 6; // RGBA

        Assert.Equal(new ImageHeader(ImageFormatKind.Png, 1920, 1080, 32), Read(data));
    }

    [Fact]
    public void Gif_ReadsSize()
    {
        var data = new byte[13];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 320);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 240);
        data[10] = 0xF7; // глобальная палитра 256 цветов

        Assert.Equal(new ImageHeader(ImageFormatKind.Gif, 320, 240, 8), Read(data));
    }

    [Fact]
    public void Jpeg_FindsStartOfFrame()
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);                           // SOI
        ms.Write([0xFF, 0xE0, 0x00, 0x10]);               // APP0, длина 16
        ms.Write(new byte[14]);
        ms.Write([0xFF, 0xC0, 0x00, 0x11, 0x08]);          // SOF0, длина 17, 8 бит
        ms.Write([0x02, 0xD0]);                           // высота 720
        ms.Write([0x05, 0x00]);                           // ширина 1280
        ms.Write([0x03]);                                 // 3 компонента
        ms.Write(new byte[9]);

        Assert.Equal(new ImageHeader(ImageFormatKind.Jpeg, 1280, 720, 24), Read(ms.ToArray()));
    }

    [Fact]
    public void Bmp_ReadsSize()
    {
        var ms = new MemoryStream();
        BmpEncoder.Save(new RawImage(7, 5), ms);
        Assert.Equal(new ImageHeader(ImageFormatKind.Bmp, 7, 5, 24), Read(ms.ToArray()));
    }

    [Fact]
    public void WebP_Extended_ReadsSize()
    {
        var data = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(data, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(data, 12);
        data[20] = 0x10; // есть альфа-канал
        // ширина-1 и высота-1 — 24-битные little-endian
        data[24] = (byte)(799 & 0xFF); data[25] = (byte)(799 >> 8);
        data[27] = (byte)(599 & 0xFF); data[28] = (byte)(599 >> 8);

        Assert.Equal(new ImageHeader(ImageFormatKind.WebP, 800, 600, 32), Read(data));
    }

    [Fact]
    public void Tiff_LittleEndian_ReadsSize()
    {
        var data = new byte[8 + 2 + 2 * 12 + 4];
        data[0] = (byte)'I'; data[1] = (byte)'I'; data[2] = 42;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 2);
        WriteEntry(10, 256, 640);
        WriteEntry(22, 257, 480);

        var header = Read(data);
        Assert.Equal(ImageFormatKind.Tiff, header.Format);
        Assert.Equal(640, header.Width);
        Assert.Equal(480, header.Height);

        void WriteEntry(int offset, ushort tag, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), 3); // SHORT
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 8), value);
        }
    }

    [Fact]
    public void TruncatedPng_ReturnsFormatWithoutSize()
    {
        var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
        var header = Read(data);
        Assert.Equal(ImageFormatKind.Png, header.Format);
        Assert.False(header.HasSize);
    }

    [Fact]
    public void UnknownData_ReturnsUnknown()
    {
        Assert.Equal(ImageFormatKind.Unknown, Read(Encoding.ASCII.GetBytes("Hello, world!")).Format);
        Assert.Equal(ImageFormatKind.Unknown, Read([]).Format);
    }

    [Theory]
    [InlineData("photo.JPG", ImageFormatKind.Jpeg)]
    [InlineData("a.png", ImageFormatKind.Png)]
    [InlineData("scan.tiff", ImageFormatKind.Tiff)]
    [InlineData("x.webp", ImageFormatKind.WebP)]
    [InlineData("doc.txt", ImageFormatKind.Unknown)]
    public void FromExtension_IsCaseInsensitive(string file, ImageFormatKind expected)
    {
        Assert.Equal(expected, ImageFormats.FromExtension(file));
        Assert.Equal(expected != ImageFormatKind.Unknown, ImageFormats.IsSupported(file));
    }
}

public class BmpCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTripsPixels()
    {
        // Нечётная ширина — проверяем выравнивание строк до 4 байт
        var image = new RawImage(5, 3);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 5; x++)
                image.SetPixel(x, y, new PixelColor((byte)(x * 50), (byte)(y * 100), (byte)(x + y)));

        var ms = new MemoryStream();
        BmpEncoder.Save(image, ms);
        ms.Position = 0;
        var decoded = new BmpDecoder().DecodeStream(ms);

        Assert.Equal(5, decoded.Width);
        Assert.Equal(3, decoded.Height);
        Assert.Equal(image.Pixels, decoded.Pixels);
    }

    [Fact]
    public void Decode_FromFile_UsesExtension()
    {
        using var folder = new TempFolder();
        var path = folder.Bmp("a.bmp", 4, 4, new PixelColor(255, 0, 0));
        var decoder = new BmpDecoder();

        Assert.True(decoder.CanDecode(path));
        Assert.False(decoder.CanDecode("a.png"));
        Assert.Equal(new PixelColor(255, 0, 0), decoder.Decode(path).GetPixel(3, 3));
    }

    [Fact]
    public void Decode_InvalidData_Throws()
    {
        Assert.Throws<InvalidDataException>(() => new BmpDecoder().DecodeStream(new MemoryStream(new byte[100])));
    }

    [Fact]
    public void CompositeDecoder_PicksFirstCapable()
    {
        var composite = new CompositeImageDecoder(new BmpDecoder());
        Assert.True(composite.CanDecode("x.bmp"));
        Assert.False(composite.CanDecode("x.png"));
        Assert.Throws<NotSupportedException>(() => composite.Decode("x.png"));
    }
}

public class HistogramTests
{
    [Fact]
    public void SolidRed_FillsExpectedBins()
    {
        var image = TestImages.Solid(10, 10, new PixelColor(255, 0, 0));
        var h = Histogram.Compute(image);

        Assert.Equal(100, h.SampleCount);
        Assert.Equal(100, h.Red[255]);
        Assert.Equal(100, h.Green[0]);
        Assert.Equal(100, h.Blue[0]);
        Assert.Equal(100, h.Luminance[76]); // 0.299 × 255 ≈ 76
        Assert.Equal(76, h.MeanLuminance, 3);
        Assert.Equal(100, h.MaxValue);
    }

    [Fact]
    public void LargeImage_IsSampled()
    {
        var image = new RawImage(2000, 2000);
        var h = Histogram.Compute(image, maxSamples: 1_000_000);

        Assert.Equal(2, h.SampleStep);
        Assert.Equal(1_000_000, h.SampleCount);
    }

    [Fact]
    public void Percentile_SplitsDarkAndLight()
    {
        var image = new RawImage(10, 2);
        for (int x = 0; x < 10; x++)
        {
            image.SetPixel(x, 0, new PixelColor(0, 0, 0));
            image.SetPixel(x, 1, new PixelColor(255, 255, 255));
        }
        var h = Histogram.Compute(image);

        Assert.Equal(0, h.Percentile(HistogramChannel.Luminance, 0.25));
        Assert.Equal(255, h.Percentile(HistogramChannel.Luminance, 0.75));
    }

    [Fact]
    public void AsciiChart_HasRequestedSize()
    {
        var h = Histogram.Compute(TestImages.Solid(4, 4, new PixelColor(128, 128, 128)));
        var lines = h.ToAsciiChart(HistogramChannel.Luminance, width: 32, height: 5).Split(Environment.NewLine);

        Assert.Equal(5 + 2, lines.Length);
        Assert.Contains('█', lines[0]);
    }

    [Fact]
    public void Compute_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Histogram.Compute(new RawImage(10, 10), cancellationToken: cts.Token));
    }
}

public class ImageFileInfoTests
{
    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1023, "1023 Б")]
    [InlineData(1536, "1.5 КБ")]
    [InlineData(1048576, "1 МБ")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 ГБ")]
    public void FileSize_IsHumanReadable(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes));
    }

    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1024, 768, "4:3")]
    [InlineData(100, 100, "1:1")]
    [InlineData(1366, 768, "1.78:1")]
    public void AspectRatio_IsSimplified(int w, int h, string expected)
    {
        var info = new ImageFileInfo { FullPath = "x.png", Width = w, Height = h };
        Assert.Equal(expected, info.AspectRatioText);
    }

    [Fact]
    public void Reader_CombinesFileSystemAndHeader()
    {
        using var folder = new TempFolder();
        var path = folder.Bmp("pic.bmp", 64, 48);

        var info = ImageInfoReader.Read(path);

        Assert.Equal("pic.bmp", info.FileName);
        Assert.Equal(ImageFormatKind.Bmp, info.Format);
        Assert.Equal(64, info.Width);
        Assert.Equal(48, info.Height);
        Assert.Equal(24, info.BitsPerPixel);
        Assert.Equal(new FileInfo(path).Length, info.FileSize);
        Assert.Equal("image/bmp", info.MimeType);
        Assert.Contains(info.ToDisplayPairs(), p => p.Key == "Разрешение" && p.Value == "64 × 48");
    }
}
