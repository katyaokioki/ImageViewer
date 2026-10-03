using ImageViewer.Core.Navigation;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Settings;

/// <summary>Русские названия значений перечислений для отображения в интерфейсе.</summary>
public static class DisplayNames
{
    public static string Get(object? value) => value switch
    {
        AppTheme.Dark => "Тёмная",
        AppTheme.Light => "Светлая",

        BackgroundMode.Theme => "Цвет темы",
        BackgroundMode.SolidColor => "Свой цвет",
        BackgroundMode.Checkerboard => "Шахматная доска",

        ScalingQuality.HighQuality => "Сглаживание",
        ScalingQuality.NearestNeighbor => "Пиксели (без сглаживания)",

        WindowStartMode.Fullscreen => "Полный экран",
        WindowStartMode.Maximized => "Развёрнутое окно",
        WindowStartMode.Windowed => "Обычное окно",

        SortMode.Name => "По имени",
        SortMode.DateModified => "По дате изменения",
        SortMode.Size => "По размеру",
        SortMode.Extension => "По типу",

        ZoomMode mode => ZoomStrategy.GetDisplayName(mode),

        Rotation r => r == Rotation.None ? "нет" : $"{(int)r}°",

        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };
}
