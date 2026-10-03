using ImageViewer.Core.Navigation;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Settings;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>Чем заполнять фон под изображением.</summary>
public enum BackgroundMode
{
    /// <summary>Цвет темы оформления.</summary>
    Theme,
    /// <summary>Заданный пользователем цвет.</summary>
    SolidColor,
    /// <summary>«Шахматка» — удобно для изображений с прозрачностью.</summary>
    Checkerboard
}

/// <summary>Качество масштабирования.</summary>
public enum ScalingQuality
{
    /// <summary>Сглаживание (для фото).</summary>
    HighQuality,
    /// <summary>Без сглаживания — видно отдельные пиксели (для пиксель-арта и анализа).</summary>
    NearestNeighbor
}

/// <summary>Режим окна при запуске.</summary>
public enum WindowStartMode
{
    Fullscreen,
    Maximized,
    Windowed
}

/// <summary>
/// Пользовательские настройки. Простой POCO-класс — легко сериализуется в JSON
/// и привязывается к элементам управления в любом UI-фреймворке.
/// </summary>
public sealed class ViewerSettings
{
    public const double MinSlideshowSeconds = 0.5;
    public const double MaxSlideshowSeconds = 600;

    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Theme;

    /// <summary>Цвет фона в формате #RRGGBB (для режима SolidColor).</summary>
    public string BackgroundColor { get; set; } = "#202020";

    public ZoomMode DefaultZoomMode { get; set; } = ZoomMode.ShrinkToFit;
    public double ZoomStep { get; set; } = 1.25;

    /// <summary>При переходе к другому изображению снова применять режим по умолчанию.</summary>
    public bool ResetZoomOnNavigate { get; set; } = true;

    /// <summary>Переход по кругу: после последнего — первое.</summary>
    public bool WrapAround { get; set; } = true;

    public SortMode SortMode { get; set; } = SortMode.Name;
    public bool SortDescending { get; set; }

    public ScalingQuality ScalingQuality { get; set; } = ScalingQuality.HighQuality;
    public WindowStartMode WindowStartMode { get; set; } = WindowStartMode.Fullscreen;

    public bool ShowStatusBar { get; set; } = true;
    public bool ShowInfoPanel { get; set; }
    public bool ShowHistogram { get; set; } = true;

    public double SlideshowIntervalSeconds { get; set; } = 3;

    /// <summary>Последний открытый файл (чтобы открыть его при запуске без параметров).</summary>
    public string? LastOpenedPath { get; set; }
    public bool RestoreLastFile { get; set; } = true;

    public ViewerSettings Clone() => (ViewerSettings)MemberwiseClone();

    /// <summary>Приводит значения к допустимым диапазонам (на случай ручной правки JSON).</summary>
    public ViewerSettings Normalize()
    {
        if (!double.IsFinite(ZoomStep) || ZoomStep <= 1.01 || ZoomStep > 4) ZoomStep = 1.25;
        if (!double.IsFinite(SlideshowIntervalSeconds)) SlideshowIntervalSeconds = 3;
        SlideshowIntervalSeconds = Math.Clamp(SlideshowIntervalSeconds, MinSlideshowSeconds, MaxSlideshowSeconds);
        if (!IsValidHexColor(BackgroundColor)) BackgroundColor = "#202020";
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Dark;
        if (!Enum.IsDefined(DefaultZoomMode) || DefaultZoomMode == ZoomMode.Manual) DefaultZoomMode = ZoomMode.ShrinkToFit;
        return this;
    }

    /// <summary>Проверка цвета вида #RGB, #RRGGBB или #AARRGGBB.</summary>
    public static bool IsValidHexColor(string? value) =>
        value is { Length: 4 or 7 or 9 } && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);
}
