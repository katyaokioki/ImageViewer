using ImageViewer.Core.Imaging;
using SkiaSharp;

namespace ImageViewer.Imaging.Skia;

/// <summary>
/// Декодер на базе SkiaSharp (та же графическая библиотека, что внутри Avalonia и Chrome).
/// Поддерживает JPEG, PNG, GIF (первый кадр), BMP, WebP, ICO на любой ОС. Учитывает EXIF-ориентацию.
/// </summary>
public sealed class SkiaImageDecoder : ImageDecoderBase
{
    private static readonly string[] SkiaExtensions =
        [".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".webp", ".ico"];

    public SkiaImageDecoder() : base("SkiaSharp", SkiaExtensions)
    {
    }

    public override RawImage DecodeStream(Stream stream, CancellationToken cancellationToken = default)
    {
        // SKCodec требует поток с поиском — при необходимости копируем в память
        Stream source = stream;
        if (!stream.CanSeek)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            source = ms;
        }

        using var codec = SKCodec.Create(source)
            ?? throw new InvalidDataException("Формат файла не распознан или файл повреждён.");

        cancellationToken.ThrowIfCancellationRequested();

        int width = codec.Info.Width, height = codec.Info.Height;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);

        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
            throw new InvalidDataException($"Не удалось декодировать изображение: {result}.");

        cancellationToken.ThrowIfCancellationRequested();

        // Копируем построчно: RowBytes может быть больше width × 4
        var pixels = new byte[(long)width * height * RawImage.BytesPerPixel];
        var span = bitmap.GetPixelSpan();
        int stride = width * RawImage.BytesPerPixel;
        for (int y = 0; y < height; y++)
            span.Slice(y * bitmap.RowBytes, stride).CopyTo(pixels.AsSpan(y * stride, stride));

        var image = new RawImage(width, height, pixels);
        return ApplyOrientation(image, codec.EncodedOrigin);
    }

    /// <summary>Поворот по EXIF: 3 — 180°, 6 — 90° по часовой, 8 — 270°.</summary>
    private static RawImage ApplyOrientation(RawImage image, SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.BottomRight => Rotate(image, 180),
        SKEncodedOrigin.RightTop => Rotate(image, 90),
        SKEncodedOrigin.LeftBottom => Rotate(image, 270),
        _ => image
    };

    /// <summary>Поворот пикселей на 90/180/270° по часовой стрелке.</summary>
    internal static RawImage Rotate(RawImage src, int degrees)
    {
        int w = src.Width, h = src.Height;
        bool swap = degrees is 90 or 270;
        var dst = new RawImage(swap ? h : w, swap ? w : h);
        var s = src.Pixels;
        var d = dst.Pixels;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                (int nx, int ny) = degrees switch
                {
                    90 => (h - 1 - y, x),
                    180 => (w - 1 - x, h - 1 - y),
                    _ => (y, w - 1 - x) // 270
                };
                Buffer.BlockCopy(s, (y * w + x) * 4, d, (ny * dst.Width + nx) * 4, 4);
            }
        }
        return dst;
    }
}
