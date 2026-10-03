namespace ImageViewer.Core.Session;

/// <summary>
/// Слайд-шоу: через заданный интервал переходит к следующему изображению.
/// Использует <see cref="PeriodicTimer"/>, поэтому не зависит от таймеров конкретного UI-фреймворка;
/// запускайте из UI-потока — переходы будут выполняться в нём же.
/// </summary>
public sealed class SlideshowController : IDisposable
{
    private readonly ViewerSession _session;
    private CancellationTokenSource? _cts;

    public SlideshowController(ViewerSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool IsRunning { get; private set; }

    public TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(3);

    /// <summary>Слайд-шоу запущено или остановлено.</summary>
    public event EventHandler? StateChanged;

    public void Start(TimeSpan? interval = null)
    {
        Stop();
        Interval = interval ?? TimeSpan.FromSeconds(_session.Settings.SlideshowIntervalSeconds);
        if (Interval < TimeSpan.FromMilliseconds(100)) Interval = TimeSpan.FromMilliseconds(100);

        _cts = new CancellationTokenSource();
        IsRunning = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        _ = RunAsync(Interval, _cts.Token);
    }

    public void Stop()
    {
        if (!IsRunning) return;
        _cts?.Cancel();
        _cts = null;
        IsRunning = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    /// <summary>Перезапустить отсчёт (например, если пользователь сам перелистнул изображение).</summary>
    public void Restart()
    {
        if (IsRunning) Start(Interval);
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                bool moved = await _session.NextAsync();
                if (!moved && !ct.IsCancellationRequested)
                {
                    // Дошли до конца папки без перехода по кругу
                    Stop();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // Сессия закрыта во время слайд-шоу
        }
    }

    public void Dispose() => Stop();
}
