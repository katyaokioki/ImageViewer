using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Session;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;
using ImageViewer.Imaging.Wic;
using ImageViewer.Wpf.Services;
using ImageViewer.Wpf.Views;
using Microsoft.Win32;

namespace ImageViewer.Wpf;

/// <summary>
/// Главное окно. Здесь только «склейка» UI с библиотекой: вся логика — в <see cref="ViewerSession"/>,
/// окно лишь переводит события мыши/клавиатуры в команды и отображает результат.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsStore _store;
    private readonly CommandLineOptions _options;
    private readonly ViewerSession _session;
    private readonly SlideshowController _slideshow;
    private readonly HotkeyMap _hotkeys = HotkeyMap.CreateDefault();
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _loadingTimer;

    /// <summary>Сохранённые настройки (то, что лежит в файле).</summary>
    private ViewerSettings _savedSettings;

    /// <summary>Действующие настройки: сохранённые + параметры командной строки + переключения горячими клавишами.</summary>
    private ViewerSettings _settings;

    private DpiScale _dpi;

    // Полноэкранный режим
    private bool _isFullscreen;
    private WindowState _restoreState = WindowState.Normal;
    private WindowStyle _restoreStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _restoreResizeMode = ResizeMode.CanResize;

    // Перетаскивание изображения мышью
    private bool _isDragging;
    private Point _lastMousePosition;

    public MainWindow(ISettingsStore store, ViewerSettings savedSettings, ViewerSettings effectiveSettings, CommandLineOptions options)
    {
        InitializeComponent();

        _store = store;
        _savedSettings = savedSettings;
        _settings = effectiveSettings;
        _options = options;
        _dpi = VisualTreeHelper.GetDpi(this);

        _session = new ViewerSession(new WicImageDecoder(), _settings);
        _session.ImageLoading += OnImageLoading;
        _session.ImageLoaded += OnImageLoaded;
        _session.ImageLoadFailed += OnImageLoadFailed;
        _session.ImageCleared += OnImageCleared;
        _session.HistogramReady += OnHistogramReady;
        _session.Zoom.Changed += (_, _) => UpdateTransform();

        _slideshow = new SlideshowController(_session);
        _slideshow.StateChanged += (_, _) => UpdateStatusBar();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        // Индикатор загрузки показываем с задержкой, чтобы он не мигал при быстрой загрузке
        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _loadingTimer.Tick += (_, _) =>
        {
            _loadingTimer.Stop();
            if (_session.IsLoading) LoadingIndicator.Visibility = Visibility.Visible;
        };

        ImageCanvas.ContextMenu = BuildContextMenu();
        ApplyVisualSettings();
        UpdateStatusBar();
    }

    #region Запуск и закрытие

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dpi = VisualTreeHelper.GetDpi(this);

        switch (_settings.WindowStartMode)
        {
            case WindowStartMode.Fullscreen:
                EnterFullscreen();
                break;
            case WindowStartMode.Maximized:
                WindowState = WindowState.Maximized;
                break;
        }

        string? path = _options.Path;
        if (path is null && _settings.RestoreLastFile && _savedSettings.LastOpenedPath is { } last &&
            (File.Exists(last) || Directory.Exists(last)))
        {
            path = last;
        }

        if (path is null) return;

        await OpenPathAsync(path);

        if (_options.Index is { } number)
        {
            if (number <= _session.Navigator.Count) await _session.GoToAsync(number - 1);
            else ShowToast($"В папке только {_session.Navigator.Count} изображений — номер {number} недоступен");
        }

        if (_options.StartSlideshow && _session.Navigator.Count > 0)
        {
            _slideshow.Start(TimeSpan.FromSeconds(_settings.SlideshowIntervalSeconds));
            ShowToast($"Слайд-шоу: {_settings.SlideshowIntervalSeconds:0.#} с");
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _slideshow.Dispose();
        _savedSettings.LastOpenedPath = _session.Navigator.CurrentPath ?? _savedSettings.LastOpenedPath;
        try
        {
            _store.Save(_savedSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не удалось сохранить — не мешаем закрытию
        }
        _session.Dispose();
    }

    private async Task OpenPathAsync(string path)
    {
        try
        {
            await _session.OpenAsync(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowToast($"Не удалось открыть: {ex.Message}");
        }
    }

    #endregion

    #region События сессии

    private void OnImageLoading(object? sender, string path)
    {
        _loadingTimer.Stop();
        _loadingTimer.Start();
    }

    private void OnImageLoaded(object? sender, ImageLoadedEventArgs e)
    {
        HideLoading();

        ImageView.Source = e.Image.ToBitmapSource();
        ImageView.Width = e.Image.Width;
        ImageView.Height = e.Image.Height;
        Placeholder.Visibility = Visibility.Collapsed;

        HistogramView.Histogram = null;
        HistogramStats.Text = string.Empty;

        UpdateTransform();
        UpdateInfoPanel();
        UpdateTitle();
    }

    private void OnImageLoadFailed(object? sender, ImageLoadFailedEventArgs e)
    {
        HideLoading();
        ImageView.Source = null;
        Placeholder.Visibility = Visibility.Visible;
        PlaceholderText.Text = $"Не удалось открыть «{Path.GetFileName(e.Path)}»\n{e.Error.Message}\n\n← → — другие изображения папки";
        HistogramView.Histogram = null;
        HistogramStats.Text = string.Empty;
        UpdateInfoPanel();
        UpdateTitle();
        UpdateStatusBar();
    }

    private void OnImageCleared(object? sender, EventArgs e)
    {
        HideLoading();
        ImageView.Source = null;
        Placeholder.Visibility = Visibility.Visible;
        PlaceholderText.Text = "В папке нет поддерживаемых изображений\nCtrl+O — открыть другой файл";
        InfoItems.ItemsSource = null;
        UpdateTitle();
        UpdateStatusBar();
    }

    private void OnHistogramReady(object? sender, Histogram histogram)
    {
        HistogramView.Histogram = histogram;
        HistogramStats.Text = string.Create(CultureInfo.InvariantCulture,
            $"Средняя яркость: {histogram.MeanLuminance:0.#}   " +
            $"Тени (5%): {histogram.Percentile(HistogramChannel.Luminance, 0.05)}   " +
            $"Света (95%): {histogram.Percentile(HistogramChannel.Luminance, 0.95)}\n" +
            $"Выборка: {histogram.SampleCount:N0} пикс. (шаг {histogram.SampleStep})");
    }

    private void HideLoading()
    {
        _loadingTimer.Stop();
        LoadingIndicator.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region Отображение

    /// <summary>Применяет матрицу из ZoomController к картинке с учётом DPI монитора.</summary>
    private void UpdateTransform()
    {
        if (ImageView.Source is not null && _session.Zoom.IsReady)
        {
            var m = _session.Zoom.GetTransform();
            double sx = _dpi.DpiScaleX, sy = _dpi.DpiScaleY;
            // ZoomController работает в физических пикселях, WPF — в аппаратно-независимых единицах (1/96 дюйма)
            ImageView.RenderTransform = new MatrixTransform(m.M11 / sx, m.M12 / sy, m.M21 / sx, m.M22 / sy, m.OffsetX / sx, m.OffsetY / sy);
        }
        UpdateStatusBar();
    }

    private void UpdateViewport()
    {
        _session.Zoom.SetViewport(new SizeD(ImageCanvas.ActualWidth * _dpi.DpiScaleX, ImageCanvas.ActualHeight * _dpi.DpiScaleY));
    }

    private void UpdateStatusBar()
    {
        var nav = _session.Navigator;
        var info = _session.CurrentInfo;
        var zoom = _session.Zoom;

        PositionText.Text = nav.Count > 0 ? $"{nav.CurrentIndex + 1} / {nav.Count}" : "0 / 0";
        FileText.Text = info is null
            ? "Нет изображения"
            : $"{info.FileName}   ·   {info.DimensionsText}   ·   {info.FileSizeText}   ·   {ImageFormats.GetDisplayName(info.Format)}";

        var parts = new List<string>();
        if (_session.CurrentImage is not null)
        {
            parts.Add($"{zoom.ZoomPercent}%");
            parts.Add(DisplayNames.Get(zoom.Mode));
            if (zoom.Rotation != Rotation.None) parts.Add($"⟳ {(int)zoom.Rotation}°");
        }
        if (_settings.ScalingQuality == ScalingQuality.NearestNeighbor) parts.Add("пиксели");
        if (_slideshow.IsRunning) parts.Add($"▶ слайд-шоу {_slideshow.Interval.TotalSeconds:0.#} с");
        ZoomText.Text = string.Join("   ·   ", parts);
    }

    private void UpdateTitle()
    {
        var nav = _session.Navigator;
        Title = _session.CurrentInfo is { } info
            ? $"{info.FileName} ({nav.CurrentIndex + 1}/{nav.Count}) — Image Viewer"
            : "Image Viewer";
    }

    private void UpdateInfoPanel()
    {
        var info = _session.CurrentInfo;
        if (info is null)
        {
            InfoItems.ItemsSource = null;
            return;
        }

        var items = info.ToDisplayPairs().ToList();
        items.Add(new("Номер в папке", $"{_session.Navigator.CurrentIndex + 1} из {_session.Navigator.Count}"));
        items.Add(new("Декодер", _session.Decoder.Name));
        InfoItems.ItemsSource = items;
    }

    /// <summary>Применяет тему, фон, видимость панелей и качество масштабирования.</summary>
    private void ApplyVisualSettings()
    {
        ThemeManager.Apply(_settings.Theme);
        BackgroundLayer.Background = BackgroundBrushFactory.Create(_settings);
        InfoPanel.Visibility = _settings.ShowInfoPanel ? Visibility.Visible : Visibility.Collapsed;
        HistogramSection.Visibility = _settings.ShowHistogram ? Visibility.Visible : Visibility.Collapsed;
        StatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        RenderOptions.SetBitmapScalingMode(ImageView,
            _settings.ScalingQuality == ScalingQuality.NearestNeighbor ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        UpdateStatusBar();
    }

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    #endregion

    #region Команды

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var gesture = KeyGestureMapper.ToGesture(key, Keyboard.Modifiers);
        if (gesture is null) return;

        var command = _hotkeys.Resolve(gesture);
        if (command == ViewerCommand.None) return;

        e.Handled = true;
        // Автоповтор при удержании клавиши разрешён только для «повторяемых» команд (листание, зум, сдвиг)
        if (e.IsRepeat && !CommandCatalog.IsRepeatable(command)) return;

        await ExecuteCommandAsync(command);
    }

    private async Task ExecuteCommandAsync(ViewerCommand command)
    {
        var nav = _session.Navigator;

        // Подсказки на границах папки
        if (command is ViewerCommand.NextImage or ViewerCommand.SkipForward && nav.Count > 0 && !nav.HasNext)
        {
            ShowToast(nav.Count == 1 ? "В папке одно изображение" : "Это последнее изображение");
            return;
        }
        if (command is ViewerCommand.PreviousImage or ViewerCommand.SkipBackward && nav.Count > 0 && !nav.HasPrevious)
        {
            ShowToast(nav.Count == 1 ? "В папке одно изображение" : "Это первое изображение");
            return;
        }

        int before = nav.CurrentIndex;
        if (await _session.ExecuteAsync(command))
        {
            if (command is ViewerCommand.NextImage && nav.CurrentIndex < before) ShowToast("Снова с первого изображения");
            if (command is ViewerCommand.PreviousImage && nav.CurrentIndex > before) ShowToast("Переход к последнему изображению");
            if (command is ViewerCommand.ReloadFolder) ShowToast($"Папка обновлена: {nav.Count} изображений");
            if (IsNavigation(command)) _slideshow.Restart();
            return;
        }

        switch (command)
        {
            case ViewerCommand.OpenFile:
                await ShowOpenDialogAsync();
                break;
            case ViewerCommand.ToggleFullscreen:
                ToggleFullscreen();
                break;
            case ViewerCommand.ToggleInfoPanel:
                _settings.ShowInfoPanel = !_settings.ShowInfoPanel;
                ApplyVisualSettings();
                break;
            case ViewerCommand.ToggleHistogram:
                _settings.ShowHistogram = !_settings.ShowHistogram;
                if (_settings.ShowHistogram) _settings.ShowInfoPanel = true;
                ApplyVisualSettings();
                break;
            case ViewerCommand.ToggleStatusBar:
                _settings.ShowStatusBar = !_settings.ShowStatusBar;
                ApplyVisualSettings();
                break;
            case ViewerCommand.ToggleTheme:
                _settings.Theme = _settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
                ApplyVisualSettings();
                ShowToast($"Тема: {DisplayNames.Get(_settings.Theme)}");
                break;
            case ViewerCommand.CycleBackground:
                _settings.BackgroundMode = (BackgroundMode)(((int)_settings.BackgroundMode + 1) % Enum.GetValues<BackgroundMode>().Length);
                ApplyVisualSettings();
                ShowToast($"Фон: {DisplayNames.Get(_settings.BackgroundMode)}");
                break;
            case ViewerCommand.ToggleScalingQuality:
                _settings.ScalingQuality = _settings.ScalingQuality == ScalingQuality.HighQuality
                    ? ScalingQuality.NearestNeighbor
                    : ScalingQuality.HighQuality;
                ApplyVisualSettings();
                ShowToast($"Масштабирование: {DisplayNames.Get(_settings.ScalingQuality)}");
                break;
            case ViewerCommand.ToggleSlideshow:
                if (nav.Count < 2)
                {
                    ShowToast("Для слайд-шоу нужно хотя бы два изображения");
                    break;
                }
                _slideshow.Toggle();
                ShowToast(_slideshow.IsRunning ? $"Слайд-шоу запущено ({_slideshow.Interval.TotalSeconds:0.#} с)" : "Слайд-шоу остановлено");
                break;
            case ViewerCommand.ShowSettings:
                ShowSettingsDialog();
                break;
            case ViewerCommand.ShowHelp:
                new HelpWindow(HelpText.Full(_hotkeys)) { Owner = this }.ShowDialog();
                break;
            case ViewerCommand.Escape:
                if (_slideshow.IsRunning) _slideshow.Stop();
                else if (_isFullscreen) ExitFullscreen();
                else Close();
                break;
            case ViewerCommand.Exit:
                Close();
                break;
        }
    }

    private static bool IsNavigation(ViewerCommand c) => c is ViewerCommand.NextImage or ViewerCommand.PreviousImage
        or ViewerCommand.FirstImage or ViewerCommand.LastImage or ViewerCommand.SkipForward or ViewerCommand.SkipBackward;

    private async Task ShowOpenDialogAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Открыть изображение",
            Filter = ImageFormats.BuildDialogFilter(),
            InitialDirectory = _session.Navigator.FolderPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog(this) == true)
            await OpenPathAsync(dialog.FileName);
    }

    private void ShowSettingsDialog()
    {
        var dialog = new SettingsWindow(_savedSettings.Clone(), _store.Location) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _savedSettings = dialog.Settings.Normalize();
        try
        {
            _store.Save(_savedSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Не удалось сохранить настройки", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Явно сохранённые настройки заменяют действующие (в том числе переопределения из командной строки)
        _settings = _savedSettings.Clone();
        _session.ApplySettings(_settings);
        ApplyVisualSettings();
        UpdateStatusBar();
        ShowToast("Настройки сохранены");
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        ViewerCommand[][] groups =
        [
            [ViewerCommand.OpenFile, ViewerCommand.ReloadFolder],
            [ViewerCommand.PreviousImage, ViewerCommand.NextImage, ViewerCommand.FirstImage, ViewerCommand.LastImage],
            [ViewerCommand.ZoomActualSize, ViewerCommand.ZoomFitToScreen, ViewerCommand.ZoomShrinkToFit,
             ViewerCommand.ZoomFitWidth, ViewerCommand.ZoomFitHeight, ViewerCommand.ZoomFill],
            [ViewerCommand.RotateClockwise, ViewerCommand.RotateCounterClockwise],
            [ViewerCommand.ToggleFullscreen, ViewerCommand.ToggleInfoPanel, ViewerCommand.ToggleSlideshow,
             ViewerCommand.ToggleTheme, ViewerCommand.CycleBackground],
            [ViewerCommand.ShowSettings, ViewerCommand.ShowHelp, ViewerCommand.Exit]
        ];

        foreach (var group in groups)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            foreach (var command in group)
            {
                var item = new MenuItem
                {
                    Header = CommandCatalog.Describe(command),
                    InputGestureText = _hotkeys.GetGestures(command).FirstOrDefault() ?? string.Empty
                };
                var captured = command;
                item.Click += async (_, _) => await ExecuteCommandAsync(captured);
                menu.Items.Add(item);
            }
        }
        return menu;
    }

    #endregion

    #region Полноэкранный режим

    private void ToggleFullscreen()
    {
        if (_isFullscreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (_isFullscreen) return;
        _restoreState = WindowState;
        _restoreStyle = WindowStyle;
        _restoreResizeMode = ResizeMode;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // Сначала Normal, затем Maximized — иначе развёрнутое окно без рамки не перекроет панель задач
        WindowState = WindowState.Normal;
        WindowState = WindowState.Maximized;
        _isFullscreen = true;
    }

    private void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        WindowState = WindowState.Normal;
        WindowStyle = _restoreStyle;
        ResizeMode = _restoreResizeMode;
        if (_restoreState == WindowState.Maximized) WindowState = WindowState.Maximized;
        _isFullscreen = false;
    }

    #endregion

    #region Мышь и перетаскивание файлов

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => UpdateViewport();

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        _dpi = e.NewDpi;
        UpdateViewport();
        UpdateTransform();
    }

    private PointD ToViewport(Point p) => new(p.X * _dpi.DpiScaleX, p.Y * _dpi.DpiScaleY);

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_session.CurrentImage is null) return;
        // Одна «ступенька» колеса = Delta 120; тачпады присылают меньшие значения
        double steps = e.Delta / 120.0;
        double factor = Math.Pow(_session.Zoom.ZoomStep, steps);
        _session.Zoom.ZoomBy(factor, ToViewport(e.GetPosition(ImageCanvas)));
        e.Handled = true;
    }

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            return;
        }
        if (_session.CurrentImage is null) return;

        _isDragging = true;
        _lastMousePosition = e.GetPosition(ImageCanvas);
        ImageCanvas.CaptureMouse();
        ImageCanvas.Cursor = Cursors.SizeAll;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ImageCanvas);
        UpdatePixelInfo(position);

        if (!_isDragging) return;
        var delta = position - _lastMousePosition;
        _lastMousePosition = position;
        _session.Zoom.Pan(delta.X * _dpi.DpiScaleX, delta.Y * _dpi.DpiScaleY);
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ImageCanvas.ReleaseMouseCapture();

    private void OnCanvasLostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDragging = false;
        ImageCanvas.Cursor = null;
    }

    private async void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        switch (e.ChangedButton)
        {
            case MouseButton.XButton1:
                await ExecuteCommandAsync(ViewerCommand.PreviousImage);
                e.Handled = true;
                break;
            case MouseButton.XButton2:
                await ExecuteCommandAsync(ViewerCommand.NextImage);
                e.Handled = true;
                break;
            case MouseButton.Middle:
                // Средняя кнопка: переключение 100% ↔ режим по умолчанию
                _session.Zoom.ApplyMode(_session.Zoom.Mode == ZoomMode.ActualSize ? _settings.DefaultZoomMode : ZoomMode.ActualSize);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Координаты и цвет пикселя под курсором.</summary>
    private void UpdatePixelInfo(Point position)
    {
        var image = _session.CurrentImage;
        if (image is null || !_session.Zoom.IsReady)
        {
            PixelText.Text = string.Empty;
            return;
        }

        var p = _session.Zoom.ViewportToImage(ToViewport(position));
        int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
        PixelText.Text = x >= 0 && y >= 0 && x < image.Width && y < image.Height
            ? $"x: {x}  y: {y}  {image.GetPixel(x, y)}"
            : string.Empty;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await OpenPathAsync(files[0]);
    }

    private void OnLogScaleChanged(object sender, RoutedEventArgs e) =>
        HistogramView.UseLogScale = LogScaleCheckBox.IsChecked == true;

    #endregion
}
