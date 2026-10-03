using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImageViewer.Core.Settings;

namespace ImageViewer.Wpf.Services;

/// <summary>Переключение темы: заменяет кисти в ресурсах приложения (элементы используют DynamicResource).</summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, Color> Dark = new()
    {
        ["WindowBackgroundBrush"] = Color.FromRgb(0x10, 0x10, 0x10),
        ["PanelBrush"] = Color.FromArgb(0xD9, 0x20, 0x20, 0x20),
        ["PanelBorderBrush"] = Color.FromRgb(0x3A, 0x3A, 0x3A),
        ["ForegroundBrush"] = Color.FromRgb(0xED, 0xED, 0xED),
        ["SecondaryForegroundBrush"] = Color.FromRgb(0xA0, 0xA0, 0xA0),
        ["AccentBrush"] = Color.FromRgb(0x4F, 0xA3, 0xFF),
        ["DialogBackgroundBrush"] = Color.FromRgb(0x1C, 0x1C, 0x1C),
        ["InputBackgroundBrush"] = Color.FromRgb(0x2A, 0x2A, 0x2A),
    };

    private static readonly Dictionary<string, Color> Light = new()
    {
        ["WindowBackgroundBrush"] = Color.FromRgb(0xF2, 0xF2, 0xF2),
        ["PanelBrush"] = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
        ["PanelBorderBrush"] = Color.FromRgb(0xD0, 0xD0, 0xD0),
        ["ForegroundBrush"] = Color.FromRgb(0x1A, 0x1A, 0x1A),
        ["SecondaryForegroundBrush"] = Color.FromRgb(0x60, 0x60, 0x60),
        ["AccentBrush"] = Color.FromRgb(0x00, 0x67, 0xC0),
        ["DialogBackgroundBrush"] = Color.FromRgb(0xFA, 0xFA, 0xFA),
        ["InputBackgroundBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    public static void Apply(AppTheme theme)
    {
        var palette = theme == AppTheme.Light ? Light : Dark;
        var resources = Application.Current.Resources;
        foreach (var (key, color) in palette)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }
    }
}

/// <summary>Кисти для фона под изображением.</summary>
public static class BackgroundBrushFactory
{
    public static Brush Create(ViewerSettings settings) => settings.BackgroundMode switch
    {
        BackgroundMode.SolidColor => ParseColor(settings.BackgroundColor),
        BackgroundMode.Checkerboard => CreateCheckerboard(settings.Theme),
        _ => Brushes.Transparent // виден фон окна из темы
    };

    public static Brush ParseColor(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.Black;
        }
    }

    /// <summary>«Шахматка» 16×16 — стандартный способ показать прозрачность.</summary>
    public static Brush CreateCheckerboard(AppTheme theme)
    {
        var (a, b) = theme == AppTheme.Light
            ? (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD6, 0xD6, 0xD6))
            : (Color.FromRgb(0x3A, 0x3A, 0x3A), Color.FromRgb(0x2A, 0x2A, 0x2A));

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(a), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        dark.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(b), null, dark));

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Перевод клавиш WPF в независимые от платформы строки жестов (см. <see cref="Core.Commands.HotkeyMap"/>).
/// </summary>
public static class KeyGestureMapper
{
    public static string? ToGesture(Key key, ModifierKeys modifiers)
    {
        string? name = KeyName(key);
        if (name is null) return null;

        var parts = new List<string>(4);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(name);
        return string.Join("+", parts);
    }

    private static string? KeyName(Key key)
    {
        switch (key)
        {
            // Сами модификаторы не являются жестом
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
                 Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None:
                return null;
            case Key.OemPlus or Key.Add:
                return "Plus";
            case Key.OemMinus or Key.Subtract:
                return "Minus";
            case Key.Enter:
                return "Enter";
            case Key.Back:
                return "Backspace";
            case Key.PageUp:
                return "PageUp";
            case Key.PageDown:
                return "PageDown";
            case Key.Escape:
                return "Escape";
            case >= Key.D0 and <= Key.D9:
                return ((int)(key - Key.D0)).ToString();
            case >= Key.NumPad0 and <= Key.NumPad9:
                return ((int)(key - Key.NumPad0)).ToString();
            default:
                return key.ToString();
        }
    }
}
