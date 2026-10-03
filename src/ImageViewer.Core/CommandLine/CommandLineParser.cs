using System.Globalization;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.CommandLine;

/// <summary>
/// Параметры запуска графического просмотрщика. Значения null означают «взять из настроек».
/// </summary>
public sealed class CommandLineOptions
{
    /// <summary>Файл или папка для открытия.</summary>
    public string? Path { get; set; }

    public ZoomMode? ZoomMode { get; set; }
    public WindowStartMode? WindowMode { get; set; }
    public AppTheme? Theme { get; set; }
    public BackgroundMode? BackgroundMode { get; set; }
    public string? BackgroundColor { get; set; }
    public SortMode? SortMode { get; set; }
    public bool? SortDescending { get; set; }
    public bool? WrapAround { get; set; }
    public bool? ShowInfoPanel { get; set; }
    public bool? ShowHistogram { get; set; }
    public ScalingQuality? ScalingQuality { get; set; }

    /// <summary>Запустить слайд-шоу сразу после открытия.</summary>
    public bool StartSlideshow { get; set; }
    public double? SlideshowIntervalSeconds { get; set; }

    /// <summary>Номер изображения в папке (с единицы).</summary>
    public int? Index { get; set; }

    public string? SettingsPath { get; set; }
    public bool ResetSettings { get; set; }
    public bool ShowHelp { get; set; }

    /// <summary>Есть ли параметры, переопределяющие сохранённые настройки.</summary>
    public bool HasOverrides =>
        ZoomMode.HasValue || WindowMode.HasValue || Theme.HasValue || BackgroundMode.HasValue ||
        SortMode.HasValue || SortDescending.HasValue || WrapAround.HasValue || ShowInfoPanel.HasValue ||
        ShowHistogram.HasValue || ScalingQuality.HasValue || SlideshowIntervalSeconds.HasValue;

    /// <summary>Применить переопределения к настройкам (обычно — к копии сохранённых).</summary>
    public void ApplyTo(ViewerSettings settings)
    {
        if (ZoomMode is { } zoom) settings.DefaultZoomMode = zoom;
        if (WindowMode is { } window) settings.WindowStartMode = window;
        if (Theme is { } theme) settings.Theme = theme;
        if (BackgroundMode is { } bg) settings.BackgroundMode = bg;
        if (BackgroundColor is { } color) settings.BackgroundColor = color;
        if (SortMode is { } sort) settings.SortMode = sort;
        if (SortDescending is { } desc) settings.SortDescending = desc;
        if (WrapAround is { } wrap) settings.WrapAround = wrap;
        if (ShowInfoPanel is { } info) settings.ShowInfoPanel = info;
        if (ShowHistogram is { } hist) settings.ShowHistogram = hist;
        if (ScalingQuality is { } quality) settings.ScalingQuality = quality;
        if (SlideshowIntervalSeconds is { } sec) settings.SlideshowIntervalSeconds = sec;
        settings.Normalize();
    }
}

/// <summary>Результат разбора: параметры и список ошибок.</summary>
public sealed record CommandLineParseResult(CommandLineOptions Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Разбор командной строки просмотрщика. Поддерживает формы «--zoom fit», «--zoom=fit», «-z fit»
/// и позиционный аргумент — путь к файлу или папке.
/// </summary>
public static class CommandLineParser
{
    public static CommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        var o = new CommandLineOptions();
        var errors = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg)) continue;

            if (!IsOption(arg))
            {
                if (o.Path is null) o.Path = arg;
                else errors.Add($"Лишний аргумент: «{arg}» (путь уже задан: «{o.Path}»).");
                continue;
            }

            // Поддержка формы --name=value
            string name = arg;
            string? inlineValue = null;
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                name = arg[..eq];
                inlineValue = arg[(eq + 1)..];
            }

            string? RequireValue()
            {
                if (inlineValue is not null) return inlineValue;
                if (i + 1 < args.Count && !IsOption(args[i + 1])) return args[++i];
                errors.Add($"Параметр {name} требует значение.");
                return null;
            }

            switch (name.ToLowerInvariant())
            {
                case "-h" or "-?" or "/?" or "--help":
                    o.ShowHelp = true;
                    break;

                case "-z" or "--zoom":
                    if (RequireValue() is { } z)
                    {
                        if (TryParseZoomMode(z, out var mode)) o.ZoomMode = mode;
                        else errors.Add($"Неизвестный режим масштаба «{z}». Допустимо: 100, fit, shrink, width, height, fill.");
                    }
                    break;

                case "-f" or "--fullscreen":
                    o.WindowMode = WindowStartMode.Fullscreen;
                    break;
                case "-w" or "--windowed":
                    o.WindowMode = WindowStartMode.Windowed;
                    break;
                case "-m" or "--maximized":
                    o.WindowMode = WindowStartMode.Maximized;
                    break;

                case "-t" or "--theme":
                    if (RequireValue() is { } t)
                    {
                        if (t.Equals("dark", StringComparison.OrdinalIgnoreCase)) o.Theme = AppTheme.Dark;
                        else if (t.Equals("light", StringComparison.OrdinalIgnoreCase)) o.Theme = AppTheme.Light;
                        else errors.Add($"Неизвестная тема «{t}». Допустимо: dark, light.");
                    }
                    break;

                case "-b" or "--background":
                    if (RequireValue() is { } b)
                    {
                        if (b.Equals("checker", StringComparison.OrdinalIgnoreCase) || b.Equals("checkerboard", StringComparison.OrdinalIgnoreCase))
                            o.BackgroundMode = BackgroundMode.Checkerboard;
                        else if (b.Equals("theme", StringComparison.OrdinalIgnoreCase))
                            o.BackgroundMode = BackgroundMode.Theme;
                        else if (ViewerSettings.IsValidHexColor(b))
                        {
                            o.BackgroundMode = BackgroundMode.SolidColor;
                            o.BackgroundColor = b;
                        }
                        else errors.Add($"Неверный фон «{b}». Допустимо: theme, checker, #RRGGBB.");
                    }
                    break;

                case "-s" or "--slideshow":
                    o.StartSlideshow = true;
                    // Необязательное значение: интервал в секундах
                    string? sValue = inlineValue;
                    if (sValue is null && i + 1 < args.Count && TryParseDouble(args[i + 1], out _))
                        sValue = args[++i];
                    if (sValue is not null)
                    {
                        if (TryParseDouble(sValue, out double sec) && sec > 0) o.SlideshowIntervalSeconds = sec;
                        else errors.Add($"Неверный интервал слайд-шоу «{sValue}».");
                    }
                    break;

                case "--sort":
                    if (RequireValue() is { } sort)
                    {
                        if (TryParseSortMode(sort, out var sm)) o.SortMode = sm;
                        else errors.Add($"Неизвестная сортировка «{sort}». Допустимо: name, date, size, type.");
                    }
                    break;
                case "--desc":
                    o.SortDescending = true;
                    break;
                case "--asc":
                    o.SortDescending = false;
                    break;

                case "--wrap":
                    o.WrapAround = true;
                    break;
                case "--no-wrap":
                    o.WrapAround = false;
                    break;

                case "-i" or "--info":
                    o.ShowInfoPanel = true;
                    break;
                case "--histogram":
                    o.ShowHistogram = true;
                    break;
                case "--no-histogram":
                    o.ShowHistogram = false;
                    break;
                case "--pixelated":
                    o.ScalingQuality = ScalingQuality.NearestNeighbor;
                    break;
                case "--smooth":
                    o.ScalingQuality = ScalingQuality.HighQuality;
                    break;

                case "-n" or "--index":
                    if (RequireValue() is { } n)
                    {
                        if (int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) && idx >= 1)
                            o.Index = idx;
                        else errors.Add($"Неверный номер изображения «{n}» (ожидается целое число ≥ 1).");
                    }
                    break;

                case "--settings":
                    if (RequireValue() is { } sp) o.SettingsPath = sp;
                    break;
                case "--reset-settings":
                    o.ResetSettings = true;
                    break;

                default:
                    errors.Add($"Неизвестный параметр: {arg}");
                    break;
            }
        }

        return new CommandLineParseResult(o, errors);
    }

    public static bool TryParseZoomMode(string? value, out ZoomMode mode)
    {
        mode = ZoomMode.ShrinkToFit;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "100" or "100%" or "actual" or "1:1" or "original":
                mode = ZoomMode.ActualSize; return true;
            case "fit" or "screen" or "fit-screen":
                mode = ZoomMode.FitToScreen; return true;
            case "shrink" or "auto":
                mode = ZoomMode.ShrinkToFit; return true;
            case "width" or "fit-width":
                mode = ZoomMode.FitWidth; return true;
            case "height" or "fit-height":
                mode = ZoomMode.FitHeight; return true;
            case "fill":
                mode = ZoomMode.Fill; return true;
            default:
                return false;
        }
    }

    public static bool TryParseSortMode(string? value, out SortMode mode)
    {
        mode = SortMode.Name;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "name": mode = SortMode.Name; return true;
            case "date" or "modified": mode = SortMode.DateModified; return true;
            case "size": mode = SortMode.Size; return true;
            case "type" or "ext" or "extension": mode = SortMode.Extension; return true;
            default: return false;
        }
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // «-5» — не параметр, а отрицательное число; путь в Unix начинается с «/», поэтому «/?» обрабатываем отдельно
    private static bool IsOption(string arg) =>
        arg == "/?" || (arg.Length > 1 && arg[0] == '-' && !char.IsAsciiDigit(arg[1]));
}
