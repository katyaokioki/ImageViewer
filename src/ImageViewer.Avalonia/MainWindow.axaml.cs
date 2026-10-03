using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Session;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;
using ImageViewer.Imaging.Skia;
using ImageViewer.Ui.Services;
using ImageViewer.Ui.Views;

namespace ImageViewer.Ui;

/// <summary>
/// Главное окно Avalonia-версии. Как и в WPF-версии, здесь только «склейка»:
/// события мыши/клавиатуры → команды → <see cref="ViewerSession"/> → отображение результата.
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
    private readonly HashSet<Key> _pressedKeys = [];

    private ViewerSettings _savedSettings;
    private ViewerSettings _settings;

    private WindowState _restoreState = WindowState.Normal;
    private bool _isDragging;
    private Point _lastPointer;

    /// <summary>Конструктор без параметров нужен дизайнеру XAML в IDE.</summary>
    public MainWindow() : this(new JsonSettingsStore(), new ViewerSettings(), new ViewerSettings(), new CommandLineOptions())
    {
    }

    public MainWindow(ISettingsStore store, ViewerSettings savedSettings, ViewerSettings effectiveSettings, CommandLineOptions options)
    {
        InitializeComponent();

        _store = store;
        _savedSettings = savedSettings;
        _settings = effectiveSettings;
        _options = options;

        _session = new ViewerSession(new CompositeImageDecoder(new BmpDecoder(), new SkiaImageDecoder()), _settings);
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
            Toast.IsVisible = false;
        };

        _loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _loadingTimer.Tick += (_, _) =>
        {
            _loadingTimer.Stop();
            if (_session.IsLoading) LoadingIndicator.IsVisible = true;
        };

        // Матрица из ZoomController задаётся относительно левого верхнего угла, а не центра
        ImageView.RenderTransformOrigin = RelativePoint.TopLeft;

        // Клавиатура: туннельная маршрутизация — окно получает клавиши раньше дочерних элементов
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);

        // Мышь
        ImageCanvas.SizeChanged += (_, _) => UpdateViewport();
        ImageCanvas.PointerWheelChanged += OnPointerWheelChanged;
        ImageCanvas.PointerPressed += OnPointerPressed;
        ImageCanvas.PointerMoved += OnPointerMoved;
        ImageCanvas.PointerReleased += OnPointerReleased;
        ImageCanvas.PointerCaptureLost += (_, _) => EndDrag();
        ImageCanvas.ContextMenu = BuildContextMenu();
        ScalingChanged += (_, _) =>
        {
            UpdateViewport();
            UpdateTransform();
        };

        // Перетаскивание файлов
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        LogScaleCheckBox.IsCheckedChanged += (_, _) => HistogramView.UseLogScale = LogScaleCheckBox.IsChecked == true;

        Opened += OnOpened;
        Closing += OnClosing;

        ApplyVisualSettings();
        UpdateStatusBar();
    }

    #region Запуск и закрытие

    private async void OnOpened(object? sender, EventArgs e)
    {
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

        if (_options.StartSlideshow && _session.Navigator.Count > 1)
        {
            _slideshow.Start(TimeSpan.FromSeconds(_settings.SlideshowIntervalSeconds));
            ShowToast($"Слайд-шоу: {_settings.SlideshowIntervalSeconds:0.#} с");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
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

        var old = ImageView.Source as Bitmap;
        ImageView.Source = BitmapConverter.ToBitmap(e.Image);
        old?.Dispose(); // освобождаем нативную память предыдущей картинки

        ImageView.Width = e.Image.Width;
        ImageView.Height = e.Image.Height;
        Placeholder.IsVisible = false;

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
        Placeholder.IsVisible = true;
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
        Placeholder.IsVisible = true;
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
        LoadingIndicator.IsVisible = false;
    }

    #endregion

    #region Отображение

    /// <summary>Применяет матрицу ZoomController к картинке с учётом масштабирования экрана (HiDPI).</summary>
    private void UpdateTransform()
    {
        if (ImageView.Source is not null && _session.Zoom.IsReady)
        {
            var m = _session.Zoom.GetTransform();
            double s = RenderScaling;
            // ZoomController работает в физических пикселях, Avalonia — в логических
            ImageView.RenderTransform = new MatrixTransform(
                new Matrix(m.M11 / s, m.M12 / s, m.M21 / s, m.M22 / s, m.OffsetX / s, m.OffsetY / s));
        }
        UpdateStatusBar();
    }

    private void UpdateViewport()
    {
        double s = RenderScaling;
        var size = ImageCanvas.Bounds.Size;
        _session.Zoom.SetViewport(new SizeD(size.Width * s, size.Height * s));
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

    private void ApplyVisualSettings()
    {
        ThemeManager.Apply(_settings.Theme);
        BackgroundLayer.Background = BackgroundBrushFactory.Create(_settings);
        InfoPanel.IsVisible = _settings.ShowInfoPanel;
        HistogramSection.IsVisible = _settings.ShowHistogram;
        StatusBar.IsVisible = _settings.ShowStatusBar;
        RenderOptions.SetBitmapInterpolationMode(ImageView,
            _settings.ScalingQuality == ScalingQuality.NearestNeighbor
                ? BitmapInterpolationMode.None
                : BitmapInterpolationMode.HighQuality);
        UpdateStatusBar();
    }

    public void ShowToast(string message, double seconds = 2.2)
    {
        ToastText.Text = message;
        Toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
        _toastTimer.Start();
    }

    #endregion

    #region Команды

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // В Avalonia нет признака автоповтора — отслеживаем удерживаемые клавиши сами
        bool isRepeat = !_pressedKeys.Add(e.Key);

        var gesture = KeyGestureMapper.ToGesture(e.Key, e.KeyModifiers);
        if (gesture is null) return;

        var command = _hotkeys.Resolve(gesture);
        if (command == ViewerCommand.None) return;

        e.Handled = true;
        if (isRepeat && !CommandCatalog.IsRepeatable(command)) return;

        await ExecuteCommandAsync(command);
    }

    private void OnKeyUp(object? sender, KeyEventArgs e) => _pressedKeys.Remove(e.Key);

    private async Task ExecuteCommandAsync(ViewerCommand command)
    {
        var nav = _session.Navigator;

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
                await ShowSettingsDialogAsync();
                break;
            case ViewerCommand.ShowHelp:
                await new HelpWindow(HelpText.Full(_hotkeys, "ImageViewerX")).ShowDialog(this);
                break;
            case ViewerCommand.Escape:
                if (_slideshow.IsRunning) _slideshow.Stop();
                else if (WindowState == WindowState.FullScreen) ExitFullscreen();
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
        var options = new FilePickerOpenOptions
        {
            Title = "Открыть изображение",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Изображения")
                {
                    Patterns = ImageFormats.SupportedExtensions.Select(ext => "*" + ext).ToList()
                },
                FilePickerFileTypes.All
            ]
        };
        if (_session.Navigator.FolderPath is { } folder)
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await OpenPathAsync(path);
    }

    private async Task ShowSettingsDialogAsync()
    {
        var dialog = new SettingsWindow(_savedSettings.Clone(), _store.Location);
        if (!await dialog.ShowDialog<bool>(this)) return;

        _savedSettings = dialog.Settings.Normalize();
        try
        {
            _store.Save(_savedSettings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Не удалось сохранить настройки: {ex.Message}", 5);
        }

        _settings = _savedSettings.Clone();
        _session.ApplySettings(_settings);
        ApplyVisualSettings();
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
                var gesture = _hotkeys.GetGestures(command).FirstOrDefault();
                var item = new MenuItem
                {
                    Header = gesture is null ? CommandCatalog.Describe(command) : $"{CommandCatalog.Describe(command)}   ({gesture})"
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
        if (WindowState == WindowState.FullScreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (WindowState == WindowState.FullScreen) return;
        _restoreState = WindowState;
        WindowState = WindowState.FullScreen;
    }

    private void ExitFullscreen()
    {
        if (WindowState != WindowState.FullScreen) return;
        WindowState = _restoreState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    #endregion

    #region Мышь и перетаскивание файлов

    private PointD ToViewport(Point p) => new(p.X * RenderScaling, p.Y * RenderScaling);

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_session.CurrentImage is null) return;
        double factor = Math.Pow(_session.Zoom.ZoomStep, e.Delta.Y);
        _session.Zoom.ZoomBy(factor, ToViewport(e.GetPosition(ImageCanvas)));
        e.Handled = true;
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(ImageCanvas).Properties;

        if (properties.IsXButton1Pressed)
        {
            await ExecuteCommandAsync(ViewerCommand.PreviousImage);
            return;
        }
        if (properties.IsXButton2Pressed)
        {
            await ExecuteCommandAsync(ViewerCommand.NextImage);
            return;
        }
        if (properties.IsMiddleButtonPressed)
        {
            _session.Zoom.ApplyMode(_session.Zoom.Mode == ZoomMode.ActualSize ? _settings.DefaultZoomMode : ZoomMode.ActualSize);
            return;
        }
        if (!properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            ToggleFullscreen();
            return;
        }
        if (_session.CurrentImage is null) return;

        _isDragging = true;
        _lastPointer = e.GetPosition(ImageCanvas);
        e.Pointer.Capture(ImageCanvas);
        ImageCanvas.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(ImageCanvas);
        UpdatePixelInfo(position);

        if (!_isDragging) return;
        var delta = position - _lastPointer;
        _lastPointer = position;
        _session.Zoom.Pan(delta.X * RenderScaling, delta.Y * RenderScaling);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag()
    {
        _isDragging = false;
        ImageCanvas.Cursor = Cursor.Default;
    }

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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null) await OpenPathAsync(path);
    }

    #endregion
}
