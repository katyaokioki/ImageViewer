using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;
using ImageViewer.Ui.Services;

namespace ImageViewer.Ui.Views;

/// <summary>Окно настроек. Работает с копией настроек; результат — <see cref="Settings"/>, если диалог вернул true.</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow() : this(new ViewerSettings(), string.Empty)
    {
    }

    public SettingsWindow(ViewerSettings settings, string location)
    {
        InitializeComponent();

        Setup(ThemeBox, Enum.GetValues<AppTheme>());
        Setup(BackgroundBox, Enum.GetValues<BackgroundMode>());
        Setup(ScalingBox, Enum.GetValues<ScalingQuality>());
        Setup(WindowModeBox, Enum.GetValues<WindowStartMode>());
        Setup(ZoomModeBox, Enum.GetValues<ZoomMode>().Where(m => m != ZoomMode.Manual).ToArray());
        Setup(SortBox, Enum.GetValues<SortMode>());

        Settings = settings;
        DataContext = Settings;
        LocationText.Text = $"Файл настроек: {location}\nПараметры командной строки действуют только на текущий запуск.";

        ColorBox.TextChanged += (_, _) => UpdatePreviews();
        ZoomStepSlider.ValueChanged += (_, _) => UpdatePreviews();
        IntervalSlider.ValueChanged += (_, _) => UpdatePreviews();

        SaveButton.Click += (_, _) =>
        {
            if (!ViewerSettings.IsValidHexColor(Settings.BackgroundColor))
            {
                ErrorText.Text = "Цвет фона должен быть в формате #RRGGBB, например #202020.";
                ErrorText.IsVisible = true;
                ColorBox.Focus();
                return;
            }
            Close(true);
        };
        CancelButton.Click += (_, _) => Close(false);
        ResetButton.Click += (_, _) =>
        {
            Settings = new ViewerSettings { LastOpenedPath = Settings.LastOpenedPath };
            DataContext = Settings;
            UpdatePreviews();
        };

        UpdatePreviews();
    }

    public ViewerSettings Settings { get; private set; }

    /// <summary>Заполняет список значениями перечисления и показывает их русские названия.</summary>
    private static void Setup<T>(ComboBox box, T[] values) where T : struct, Enum
    {
        box.ItemsSource = values;
        box.ItemTemplate = new FuncDataTemplate<object>((value, _) => new TextBlock { Text = DisplayNames.Get(value) });
    }

    private void UpdatePreviews()
    {
        ColorPreview.Background = ViewerSettings.IsValidHexColor(ColorBox.Text)
            ? BackgroundBrushFactory.ParseColor(ColorBox.Text!)
            : null;
        ZoomStepText.Text = $"×{ZoomStepSlider.Value:0.00}";
        IntervalText.Text = $"{IntervalSlider.Value:0.#} с";
    }
}
