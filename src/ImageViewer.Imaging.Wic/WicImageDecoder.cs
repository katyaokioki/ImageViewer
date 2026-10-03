using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Imaging.Wic;

/// <summary>
/// Декодер на базе Windows Imaging Component: JPEG, PNG, GIF, BMP, TIFF, ICO, JPEG XR,
/// а также WebP/HEIC при наличии кодеков в системе (в Windows 10/11 ставятся из Microsoft Store).
/// Учитывает EXIF-ориентацию фотографий.
/// </summary>
public sealed class WicImageDecoder : ImageDecoderBase
{
    public WicImageDecoder() : base("Windows Imaging Component", ImageFormats.SupportedExtensions)
    {
    }

    public override RawImage DecodeStream(Stream stream, CancellationToken cancellationToken = default)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);

        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("Файл не содержит кадров изображения.");

        cancellationToken.ThrowIfCancellationRequested();

        BitmapSource frame = decoder.Frames[0];
        frame = ApplyExifOrientation(frame);

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        var pixels = new byte[(long)width * height * RawImage.BytesPerPixel];
        converted.CopyPixels(pixels, width * RawImage.BytesPerPixel, 0);

        return new RawImage(width, height, pixels);
    }

    /// <summary>Поворачивает кадр согласно тегу EXIF Orientation (1, 3, 6, 8).</summary>
    private static BitmapSource ApplyExifOrientation(BitmapSource frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata metadata &&
                metadata.ContainsQuery("System.Photo.Orientation") &&
                metadata.GetQuery("System.Photo.Orientation") is ushort orientation)
            {
                double angle = orientation switch
                {
                    3 => 180,
                    6 => 90,
                    8 => 270,
                    _ => 0
                };
                if (angle != 0)
                    return new TransformedBitmap(frame, new RotateTransform(angle));
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Формат без метаданных — показываем как есть
        }
        return frame;
    }
}

/// <summary>Преобразования между <see cref="RawImage"/> и типами WPF.</summary>
public static class WpfImageExtensions
{
    /// <summary>Создаёт замороженный (потокобезопасный) BitmapSource из пикселей BGRA32.</summary>
    public static BitmapSource ToBitmapSource(this RawImage image)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null,
            image.Pixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }
}
