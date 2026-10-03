using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;
using ImageViewer.Wpf.Services;

namespace ImageViewer.Wpf.Views;

/// <summary>Окно настроек. Работает с копией настроек; результат — в <see cref="Settings"/> при DialogResult = true.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(ViewerSettings settings, string location)
    {
        InitializeComponent();

        ThemeBox.ItemsSource = Enum.GetValues<AppTheme>();
        BackgroundBox.ItemsSource = Enum.GetValues<BackgroundMode>();
        ScalingBox.ItemsSource = Enum.GetValues<ScalingQuality>();
        WindowModeBox.ItemsSource = Enum.GetValues<WindowStartMode>();
        ZoomModeBox.ItemsSource = Enum.GetValues<ZoomMode>().Where(m => m != ZoomMode.Manual).ToArray();
        SortBox.ItemsSource = Enum.GetValues<SortMode>();

        Settings = settings;
        DataContext = Settings;
        LocationText.Text = $"Файл настроек: {location}\nПараметры командной строки действуют только на текущий запуск.";
        UpdateColorPreview();
    }

    public ViewerSettings Settings { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewerSettings.IsValidHexColor(Settings.BackgroundColor))
        {
            MessageBox.Show(this, "Цвет фона должен быть в формате #RRGGBB, например #202020.", "Неверный цвет",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            ColorBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        var lastPath = Settings.LastOpenedPath;
        Settings = new ViewerSettings { LastOpenedPath = lastPath };
        DataContext = Settings;
        UpdateColorPreview();
    }

    private void OnColorTextChanged(object sender, TextChangedEventArgs e) => UpdateColorPreview();

    private void UpdateColorPreview()
    {
        if (ColorPreview is null || ColorBox is null) return;
        ColorPreview.Background = ViewerSettings.IsValidHexColor(ColorBox.Text)
            ? BackgroundBrushFactory.ParseColor(ColorBox.Text)
            : null;
    }
}

/// <summary>Конвертер: значение перечисления → русское название (для ComboBox).</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => DisplayNames.Get(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
