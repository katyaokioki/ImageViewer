using System.Text;

namespace ImageViewer.Core.Imaging;

/// <summary>Известные форматы изображений.</summary>
public enum ImageFormatKind
{
    Unknown,
    Jpeg,
    Png,
    Gif,
    Bmp,
    Tiff,
    WebP,
    Ico,
    Heic,
    JpegXr
}

/// <summary>
/// Справочник форматов: расширения, MIME-типы, человекочитаемые названия
/// и определение формата по «магическим байтам» в начале файла.
/// </summary>
public static class ImageFormats
{
    private static readonly Dictionary<string, ImageFormatKind> ByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ImageFormatKind.Jpeg,
            [".jpeg"] = ImageFormatKind.Jpeg,
            [".jpe"] = ImageFormatKind.Jpeg,
            [".jfif"] = ImageFormatKind.Jpeg,
            [".png"] = ImageFormatKind.Png,
            [".gif"] = ImageFormatKind.Gif,
            [".bmp"] = ImageFormatKind.Bmp,
            [".dib"] = ImageFormatKind.Bmp,
            [".tif"] = ImageFormatKind.Tiff,
            [".tiff"] = ImageFormatKind.Tiff,
            [".webp"] = ImageFormatKind.WebP,
            [".ico"] = ImageFormatKind.Ico,
            [".heic"] = ImageFormatKind.Heic,
            [".heif"] = ImageFormatKind.Heic,
            [".jxr"] = ImageFormatKind.JpegXr,
            [".wdp"] = ImageFormatKind.JpegXr,
        };

    /// <summary>Все поддерживаемые расширения (с точкой, в нижнем регистре).</summary>
    public static IReadOnlyCollection<string> SupportedExtensions => ByExtension.Keys;

    /// <summary>Является ли файл изображением поддерживаемого типа (по расширению).</summary>
    public static bool IsSupported(string path) =>
        !string.IsNullOrEmpty(path) && ByExtension.ContainsKey(Path.GetExtension(path));

    public static ImageFormatKind FromExtension(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var kind) ? kind : ImageFormatKind.Unknown;

    public static string GetMimeType(ImageFormatKind kind) => kind switch
    {
        ImageFormatKind.Jpeg => "image/jpeg",
        ImageFormatKind.Png => "image/png",
        ImageFormatKind.Gif => "image/gif",
        ImageFormatKind.Bmp => "image/bmp",
        ImageFormatKind.Tiff => "image/tiff",
        ImageFormatKind.WebP => "image/webp",
        ImageFormatKind.Ico => "image/x-icon",
        ImageFormatKind.Heic => "image/heic",
        ImageFormatKind.JpegXr => "image/jxr",
        _ => "application/octet-stream"
    };

    public static string GetDisplayName(ImageFormatKind kind) => kind switch
    {
        ImageFormatKind.Jpeg => "JPEG",
        ImageFormatKind.Png => "PNG",
        ImageFormatKind.Gif => "GIF",
        ImageFormatKind.Bmp => "BMP (Windows Bitmap)",
        ImageFormatKind.Tiff => "TIFF",
        ImageFormatKind.WebP => "WebP",
        ImageFormatKind.Ico => "ICO (значок)",
        ImageFormatKind.Heic => "HEIC / HEIF",
        ImageFormatKind.JpegXr => "JPEG XR",
        _ => "Неизвестный"
    };

    /// <summary>Строка-фильтр для диалогов открытия файла (WinForms / WPF).</summary>
    public static string BuildDialogFilter()
    {
        var patterns = string.Join(";", SupportedExtensions.Select(e => "*" + e));
        return $"Изображения|{patterns}|Все файлы|*.*";
    }

    /// <summary>Определяет формат по первым байтам файла (сигнатуре).</summary>
    public static ImageFormatKind DetectFromSignature(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47 &&
            h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
            return ImageFormatKind.Png;

        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
            return ImageFormatKind.Jpeg;

        if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8' &&
            (h[4] == '7' || h[4] == '9') && h[5] == 'a')
            return ImageFormatKind.Gif;

        if (h.Length >= 2 && h[0] == 'B' && h[1] == 'M')
            return ImageFormatKind.Bmp;

        if (h.Length >= 4 && ((h[0] == 'I' && h[1] == 'I' && h[2] == 42 && h[3] == 0) ||
                              (h[0] == 'M' && h[1] == 'M' && h[2] == 0 && h[3] == 42)))
            return ImageFormatKind.Tiff;

        if (h.Length >= 3 && h[0] == 'I' && h[1] == 'I' && h[2] == 0xBC)
            return ImageFormatKind.JpegXr;

        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' &&
            h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
            return ImageFormatKind.WebP;

        if (h.Length >= 4 && h[0] == 0 && h[1] == 0 && h[2] == 1 && h[3] == 0)
            return ImageFormatKind.Ico;

        if (h.Length >= 12 && h[4] == 'f' && h[5] == 't' && h[6] == 'y' && h[7] == 'p')
        {
            var brand = Encoding.ASCII.GetString(h.Slice(8, 4));
            if (brand is "heic" or "heix" or "hevc" or "heim" or "heis" or "mif1" or "msf1")
                return ImageFormatKind.Heic;
        }

        return ImageFormatKind.Unknown;
    }
}
