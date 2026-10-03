using System.Globalization;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Core.Files;

/// <summary>Форматирование размера файла: 1536 → «1.5 КБ».</summary>
public static class FileSizeFormatter
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    public static string Format(long bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string number = unit == 0
            ? bytes.ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{number} {Units[unit]}";
    }
}

/// <summary>Неизменяемое описание файла изображения (record).</summary>
public sealed record ImageFileInfo
{
    public required string FullPath { get; init; }
    public long FileSize { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
    public ImageFormatKind Format { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int BitsPerPixel { get; init; }

    public string FileName => Path.GetFileName(FullPath);
    public string DirectoryPath => Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string Extension => Path.GetExtension(FullPath).ToLowerInvariant();
    public string FormatName => ImageFormats.GetDisplayName(Format);
    public string MimeType => ImageFormats.GetMimeType(Format);
    public string FileSizeText => FileSizeFormatter.Format(FileSize);
    public bool HasSize => Width > 0 && Height > 0;
    public double Megapixels => (double)Width * Height / 1_000_000;
    public string DimensionsText => HasSize ? $"{Width} × {Height}" : "—";

    /// <summary>Соотношение сторон, например «4:3» или «1.78:1».</summary>
    public string AspectRatioText
    {
        get
        {
            if (!HasSize) return "—";
            int g = Gcd(Width, Height);
            int a = Width / g, b = Height / g;
            return a <= 50 && b <= 50
                ? $"{a}:{b}"
                : ((double)Width / Height).ToString("0.##", CultureInfo.InvariantCulture) + ":1";
        }
    }

    /// <summary>Пары «название — значение» для панели информации или вывода в консоль.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ToDisplayPairs()
    {
        var list = new List<KeyValuePair<string, string>>
        {
            new("Файл", FileName),
            new("Папка", DirectoryPath),
            new("Размер файла", $"{FileSizeText} ({FileSize.ToString("N0", CultureInfo.InvariantCulture)} байт)"),
            new("Тип", $"{FormatName} ({Extension})"),
            new("MIME", MimeType),
            new("Разрешение", DimensionsText),
        };
        if (HasSize)
        {
            list.Add(new("Мегапиксели", Megapixels.ToString("0.##", CultureInfo.InvariantCulture) + " Мп"));
            list.Add(new("Пропорции", AspectRatioText));
        }
        if (BitsPerPixel > 0) list.Add(new("Глубина цвета", $"{BitsPerPixel} бит/пиксель"));
        list.Add(new("Изменён", Modified.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        list.Add(new("Создан", Created.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        return list;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Max(a, 1);
    }
}

/// <summary>Сбор сведений о файле: файловая система + заголовок изображения.</summary>
public static class ImageInfoReader
{
    public static ImageFileInfo Read(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("Файл не найден.", path);

        ImageHeader header;
        try
        {
            header = ImageHeaderReader.Read(fi.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            header = ImageHeader.Unknown;
        }

        var format = header.Format != ImageFormatKind.Unknown ? header.Format : ImageFormats.FromExtension(path);

        return new ImageFileInfo
        {
            FullPath = fi.FullName,
            FileSize = fi.Length,
            Created = fi.CreationTime,
            Modified = fi.LastWriteTime,
            Format = format,
            Width = header.Width,
            Height = header.Height,
            BitsPerPixel = header.BitsPerPixel
        };
    }

    /// <summary>Дополняет сведения размерами декодированного изображения, если из заголовка их получить не удалось.</summary>
    public static ImageFileInfo WithDecodedSize(this ImageFileInfo info, RawImage image) =>
        info.Width == image.Width && info.Height == image.Height
            ? info
            : info with { Width = image.Width, Height = image.Height };
}
