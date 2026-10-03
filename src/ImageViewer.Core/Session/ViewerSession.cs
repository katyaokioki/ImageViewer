using ImageViewer.Core.Commands;
using ImageViewer.Core.Files;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Session;

/// <summary>Изображение загружено и готово к показу.</summary>
public sealed class ImageLoadedEventArgs(ImageFileInfo info, RawImage image, int index, int count, bool fromCache) : EventArgs
{
    public ImageFileInfo Info { get; } = info;
    public RawImage Image { get; } = image;
    public int Index { get; } = index;
    public int Count { get; } = count;
    public bool FromCache { get; } = fromCache;
}

/// <summary>Не удалось загрузить изображение.</summary>
public sealed class ImageLoadFailedEventArgs(string path, Exception error, int index, int count) : EventArgs
{
    public string Path { get; } = path;
    public Exception Error { get; } = error;
    public int Index { get; } = index;
    public int Count { get; } = count;
}

/// <summary>
/// Фасад над всей логикой просмотра: навигация по папке + декодирование + масштаб + гистограмма + кэш.
/// UI создаёт одну сессию, подписывается на события и вызывает методы — больше ему ничего знать не нужно.
/// </summary>
/// <remarks>
/// Асинхронные методы следует вызывать из UI-потока: тяжёлая работа (декодирование, гистограмма)
/// выполняется в пуле потоков, а события возникают в исходном контексте синхронизации (то есть в UI-потоке).
/// </remarks>
public sealed class ViewerSession : IDisposable
{
    private sealed record CachedImage(ImageFileInfo Info, RawImage Image);

    private readonly LruCache<string, CachedImage> _cache;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;
    private bool _hasShownImage;

    public ViewerSession(IImageDecoder decoder, ViewerSettings settings, IImageNavigator? navigator = null, int cacheSize = 5)
    {
        Decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Navigator = navigator ?? new FolderNavigator(path => ImageFormats.IsSupported(path) && decoder.CanDecode(path));
        Zoom = new ZoomController();
        _cache = new LruCache<string, CachedImage>(cacheSize, StringComparer.OrdinalIgnoreCase);
        ApplySettings(settings);
    }

    public IImageDecoder Decoder { get; }
    public IImageNavigator Navigator { get; }
    public ZoomController Zoom { get; }
    public ViewerSettings Settings { get; private set; }

    public ImageFileInfo? CurrentInfo { get; private set; }
    public RawImage? CurrentImage { get; private set; }
    public Histogram? CurrentHistogram { get; private set; }
    public bool IsLoading { get; private set; }

    /// <summary>Начата загрузка файла (аргумент — путь).</summary>
    public event EventHandler<string>? ImageLoading;
    public event EventHandler<ImageLoadedEventArgs>? ImageLoaded;
    public event EventHandler<ImageLoadFailedEventArgs>? ImageLoadFailed;
    public event EventHandler<Histogram>? HistogramReady;
    /// <summary>Папка пуста или текущий файл исчез.</summary>
    public event EventHandler? ImageCleared;

    /// <summary>Применить (новые) настройки к навигатору и масштабированию.</summary>
    public void ApplySettings(ViewerSettings settings)
    {
        Settings = settings;
        Zoom.ZoomStep = settings.ZoomStep;
        Navigator.WrapAround = settings.WrapAround;
        Navigator.SortMode = settings.SortMode;
        Navigator.SortDescending = settings.SortDescending;
    }

    /// <summary>Открыть файл или папку.</summary>
    public async Task OpenAsync(string path)
    {
        ThrowIfDisposed();
        Navigator.Open(path);
        await LoadCurrentAsync();
    }

    public Task<bool> NextAsync() => NavigateAsync(Navigator.MoveNext);
    public Task<bool> PreviousAsync() => NavigateAsync(Navigator.MovePrevious);
    public Task<bool> FirstAsync() => NavigateAsync(Navigator.MoveFirst);
    public Task<bool> LastAsync() => NavigateAsync(Navigator.MoveLast);
    public Task<bool> MoveByAsync(int delta) => NavigateAsync(() => Navigator.MoveBy(delta));
    public Task<bool> GoToAsync(int index) => NavigateAsync(() => Navigator.MoveTo(index));

    /// <summary>Перечитать папку (например, после добавления файлов).</summary>
    public async Task RefreshAsync()
    {
        string? before = Navigator.CurrentPath;
        if (before is not null) _cache.Remove(before);
        Navigator.Refresh();
        await LoadCurrentAsync();
    }

    private async Task<bool> NavigateAsync(Func<bool> move)
    {
        ThrowIfDisposed();
        if (!move()) return false;
        await LoadCurrentAsync();
        return true;
    }

    /// <summary>Загрузить текущий файл навигатора.</summary>
    public async Task LoadCurrentAsync()
    {
        ThrowIfDisposed();
        string? path = Navigator.CurrentPath;

        // Отменяем предыдущую загрузку. Старый CTS не освобождаем: на его токен ещё могут ссылаться фоновые задачи.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (path is null)
        {
            CurrentInfo = null;
            CurrentImage = null;
            CurrentHistogram = null;
            IsLoading = false;
            ImageCleared?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsLoading = true;
        ImageLoading?.Invoke(this, path);
        int index = Navigator.CurrentIndex, count = Navigator.Count;

        try
        {
            bool fromCache = _cache.TryGet(path, out var cached);
            if (!fromCache)
                cached = await Task.Run(() => LoadEntry(path, ct), ct);

            if (ct.IsCancellationRequested) return; // пользователь уже ушёл к другому файлу

            CurrentInfo = cached.Info;
            CurrentImage = cached.Image;
            CurrentHistogram = null;
            IsLoading = false;

            // Первое изображение — всегда режим по умолчанию; дальше — по настройке ResetZoomOnNavigate
            var mode = Settings.ResetZoomOnNavigate || !_hasShownImage ? Settings.DefaultZoomMode : Zoom.Mode;
            _hasShownImage = true;
            Zoom.SetImage(new SizeD(cached.Image.Width, cached.Image.Height), mode);

            ImageLoaded?.Invoke(this, new ImageLoadedEventArgs(cached.Info, cached.Image, index, count, fromCache));

            _ = ComputeHistogramAsync(cached.Image, ct);
            PreloadNeighbors(ct);
        }
        catch (OperationCanceledException)
        {
            // Нормальная ситуация при быстром листании
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            IsLoading = false;
            CurrentImage = null;
            CurrentHistogram = null;
            CurrentInfo = TryReadInfo(path);
            ImageLoadFailed?.Invoke(this, new ImageLoadFailedEventArgs(path, ex, index, count));
        }
    }

    /// <summary>
    /// Выполнить команду, если она относится к логике просмотра (навигация, масштаб, поворот).
    /// Возвращает false для команд интерфейса (полный экран, справка…) — их обрабатывает UI.
    /// </summary>
    public async Task<bool> ExecuteAsync(ViewerCommand command)
    {
        double panX = Zoom.ViewportSize.Width * 0.1;
        double panY = Zoom.ViewportSize.Height * 0.1;

        switch (command)
        {
            case ViewerCommand.NextImage: await NextAsync(); return true;
            case ViewerCommand.PreviousImage: await PreviousAsync(); return true;
            case ViewerCommand.FirstImage: await FirstAsync(); return true;
            case ViewerCommand.LastImage: await LastAsync(); return true;
            case ViewerCommand.SkipForward: await MoveByAsync(10); return true;
            case ViewerCommand.SkipBackward: await MoveByAsync(-10); return true;
            case ViewerCommand.ReloadFolder: await RefreshAsync(); return true;

            case ViewerCommand.ZoomIn: Zoom.ZoomIn(); return true;
            case ViewerCommand.ZoomOut: Zoom.ZoomOut(); return true;
            case ViewerCommand.ZoomActualSize: Zoom.ApplyMode(ZoomMode.ActualSize); return true;
            case ViewerCommand.ZoomFitToScreen: Zoom.ApplyMode(ZoomMode.FitToScreen); return true;
            case ViewerCommand.ZoomShrinkToFit: Zoom.ApplyMode(ZoomMode.ShrinkToFit); return true;
            case ViewerCommand.ZoomFitWidth: Zoom.ApplyMode(ZoomMode.FitWidth); return true;
            case ViewerCommand.ZoomFitHeight: Zoom.ApplyMode(ZoomMode.FitHeight); return true;
            case ViewerCommand.ZoomFill: Zoom.ApplyMode(ZoomMode.Fill); return true;

            case ViewerCommand.RotateClockwise: Zoom.RotateClockwise(); return true;
            case ViewerCommand.RotateCounterClockwise: Zoom.RotateCounterClockwise(); return true;
            // Pan: «сдвинуть влево» = показать то, что левее, т.е. картинка едет вправо
            case ViewerCommand.PanLeft: Zoom.Pan(panX, 0); return true;
            case ViewerCommand.PanRight: Zoom.Pan(-panX, 0); return true;
            case ViewerCommand.PanUp: Zoom.Pan(0, panY); return true;
            case ViewerCommand.PanDown: Zoom.Pan(0, -panY); return true;

            default:
                return false;
        }
    }

    private CachedImage LoadEntry(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = ImageInfoReader.Read(path);
        var image = Decoder.Decode(path, ct);
        var entry = new CachedImage(info.WithDecodedSize(image), image);
        _cache.Set(path, entry);
        return entry;
    }

    private async Task ComputeHistogramAsync(RawImage image, CancellationToken ct)
    {
        try
        {
            var histogram = await Task.Run(() => Histogram.Compute(image, cancellationToken: ct), ct);
            if (ct.IsCancellationRequested || !ReferenceEquals(image, CurrentImage)) return;
            CurrentHistogram = histogram;
            HistogramReady?.Invoke(this, histogram);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Фоновая предзагрузка соседних изображений в кэш.</summary>
    private void PreloadNeighbors(CancellationToken ct)
    {
        if (Navigator.Count < 2) return;
        var candidates = new List<string>();
        foreach (int delta in new[] { 1, -1 })
        {
            int i = Navigator.CurrentIndex + delta;
            if (Navigator.WrapAround) i = ((i % Navigator.Count) + Navigator.Count) % Navigator.Count;
            if (i >= 0 && i < Navigator.Count) candidates.Add(Navigator.Files[i]);
        }

        foreach (var path in candidates.Distinct())
        {
            if (_cache.Contains(path)) continue;
            _ = Task.Run(() =>
            {
                try
                {
                    LoadEntry(path, ct);
                }
                catch
                {
                    // Ошибка предзагрузки не критична: при переходе файл загрузится заново и покажет ошибку
                }
            }, ct);
        }
    }

    private static ImageFileInfo? TryReadInfo(string path)
    {
        try { return ImageInfoReader.Read(path); }
        catch { return null; }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _cache.Clear();
    }
}
