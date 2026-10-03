using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Settings;

namespace ImageViewer.Ui.Services;

/// <summary>Преобразование <see cref="RawImage"/> (BGRA32 из библиотеки) в Bitmap Avalonia.</summary>
public static class BitmapConverter
{
    public static Bitmap ToBitmap(RawImage image)
    {
        // Закрепляем массив, чтобы сборщик мусора не переместил его, пока Avalonia копирует пиксели
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                new PixelSize(image.Width, image.Height), new Vector(96, 96), image.Stride);
        }
        finally
        {
            handle.Free();
        }
    }
}

/// <summary>Темы: встроенная тема Fluent (ThemeVariant) + собственные кисти панелей.</summary>
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
        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        foreach (var (key, color) in theme == AppTheme.Light ? Light : Dark)
            app.Resources[key] = new SolidColorBrush(color);
    }
}

/// <summary>Кисти фона под изображением.</summary>
public static class BackgroundBrushFactory
{
    public static IBrush Create(ViewerSettings settings) => settings.BackgroundMode switch
    {
        BackgroundMode.SolidColor => ParseColor(settings.BackgroundColor),
        BackgroundMode.Checkerboard => CreateCheckerboard(settings.Theme),
        _ => Brushes.Transparent
    };

    public static IBrush ParseColor(string hex) =>
        Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Black;

    /// <summary>«Шахматка» 16×16, повторяемая плиткой, — для изображений с прозрачностью.</summary>
    public static IBrush CreateCheckerboard(AppTheme theme)
    {
        var (a, b) = theme == AppTheme.Light
            ? (new PixelColor(0xFF, 0xFF, 0xFF), new PixelColor(0xD6, 0xD6, 0xD6))
            : (new PixelColor(0x3A, 0x3A, 0x3A), new PixelColor(0x2A, 0x2A, 0x2A));

        var tile = new RawImage(16, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                tile.SetPixel(x, y, (x < 8) == (y < 8) ? b : a);

        return new ImageBrush(BitmapConverter.ToBitmap(tile))
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute)
        };
    }
}

/// <summary>Перевод клавиш Avalonia в платформенно-независимые строки жестов HotkeyMap.</summary>
public static class KeyGestureMapper
{
    public static string? ToGesture(Key key, KeyModifiers modifiers)
    {
        string? name = KeyName(key);
        if (name is null) return null;

        var parts = new List<string>(4);
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Ctrl"); // Meta = Cmd на macOS
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(name);
        return string.Join("+", parts);
    }

    private static string? KeyName(Key key)
    {
        switch (key)
        {
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
