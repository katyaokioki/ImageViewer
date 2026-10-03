using ImageViewer.Core.Commands;
using ImageViewer.Core.Imaging;
using ImageViewer.Core.Session;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Tests;

/// <summary>Интеграционные тесты фасада ViewerSession на реальных BMP-файлах.</summary>
public class ViewerSessionTests
{
    /// <summary>Папка с тестовыми изображениями + сессия; освобождаются вместе.</summary>
    private sealed class Fixture : IDisposable
    {
        public required TempFolder Folder { get; init; }
        public required ViewerSession Session { get; init; }

        public void Dispose()
        {
            Session.Dispose();
            Folder.Dispose();
        }
    }

    private static Fixture Create(ViewerSettings? settings = null)
    {
        var folder = new TempFolder();
        folder.Bmp("a1.bmp", 40, 30, new PixelColor(255, 0, 0));
        folder.Bmp("a2.bmp", 20, 60, new PixelColor(0, 255, 0));
        folder.Bmp("a10.bmp", 80, 10, new PixelColor(0, 0, 255));
        folder.File("broken.bmp", 10); // повреждённый файл
        var session = new ViewerSession(new BmpDecoder(), settings ?? new ViewerSettings());
        session.Zoom.SetViewport(new SizeD(400, 300));
        return new Fixture { Folder = folder, Session = session };
    }

    [Fact]
    public async Task Open_LoadsImageAndRaisesEvent()
    {
        using var fx = Create();
        var folder = fx.Folder;
        var session = fx.Session;
        ImageLoadedEventArgs? loaded = null;
        session.ImageLoaded += (_, e) => loaded = e;

        await session.OpenAsync(Path.Combine(folder.Path, "a2.bmp"));

        Assert.NotNull(loaded);
        Assert.Equal("a2.bmp", loaded.Info.FileName);
        Assert.Equal(20, loaded.Image.Width);
        Assert.Equal(60, loaded.Image.Height);
        Assert.Equal(1, loaded.Index);
        Assert.Equal(4, loaded.Count); // a1, a2, a10, broken
        Assert.Equal(new SizeD(20, 60), session.Zoom.ImageSize);
    }

    [Fact]
    public async Task Navigation_MovesThroughFolder()
    {
        using var fx = Create();
        var folder = fx.Folder;
        var session = fx.Session;

        await session.OpenAsync(folder.Path);
        Assert.Equal("a1.bmp", session.CurrentInfo!.FileName);

        Assert.True(await session.NextAsync());
        Assert.Equal("a2.bmp", session.CurrentInfo!.FileName);

        Assert.True(await session.LastAsync());
        Assert.Equal("broken.bmp", Path.GetFileName(session.Navigator.CurrentPath));

        Assert.True(await session.PreviousAsync());
        Assert.Equal("a10.bmp", session.CurrentInfo!.FileName);
    }

    [Fact]
    public async Task BrokenFile_RaisesLoadFailed()
    {
        using var fx = Create();
        var folder = fx.Folder;
        var session = fx.Session;
        ImageLoadFailedEventArgs? failed = null;
        session.ImageLoadFailed += (_, e) => failed = e;

        await session.OpenAsync(Path.Combine(folder.Path, "broken.bmp"));

        Assert.NotNull(failed);
        Assert.Null(session.CurrentImage);
        Assert.NotNull(session.CurrentInfo); // сведения о файле всё равно доступны
    }

    [Fact]
    public async Task Histogram_IsComputedInBackground()
    {
        using var fx = Create();
        var folder = fx.Folder;
        var session = fx.Session;
        var ready = new TaskCompletionSource<Histogram>();
        session.HistogramReady += (_, h) => ready.TrySetResult(h);

        await session.OpenAsync(Path.Combine(folder.Path, "a1.bmp"));
        var histogram = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(40 * 30, histogram.Red[255]);
    }

    [Fact]
    public async Task Execute_HandlesViewCommandsOnly()
    {
        using var fx = Create(new ViewerSettings { DefaultZoomMode = ZoomMode.FitToScreen });
        var folder = fx.Folder;
        var session = fx.Session;
        await session.OpenAsync(Path.Combine(folder.Path, "a1.bmp"));

        Assert.Equal(ZoomMode.FitToScreen, session.Zoom.Mode);
        Assert.True(await session.ExecuteAsync(ViewerCommand.ZoomActualSize));
        Assert.Equal(ZoomMode.ActualSize, session.Zoom.Mode);
        Assert.True(await session.ExecuteAsync(ViewerCommand.RotateClockwise));
        Assert.Equal(Rotation.Rotate90, session.Zoom.Rotation);
        Assert.True(await session.ExecuteAsync(ViewerCommand.NextImage));
        Assert.Equal(1, session.Navigator.CurrentIndex);

        // Команды интерфейса сессия не обрабатывает — это задача UI
        Assert.False(await session.ExecuteAsync(ViewerCommand.ToggleFullscreen));
        Assert.False(await session.ExecuteAsync(ViewerCommand.ShowHelp));
    }

    [Fact]
    public async Task ResetZoomOnNavigate_False_KeepsManualZoom()
    {
        using var fx = Create(new ViewerSettings { ResetZoomOnNavigate = false });
        var folder = fx.Folder;
        var session = fx.Session;
        await session.OpenAsync(Path.Combine(folder.Path, "a1.bmp"));

        session.Zoom.SetScale(3);
        await session.NextAsync();

        Assert.Equal(3, session.Zoom.Scale, 6);
        Assert.Equal(ZoomMode.Manual, session.Zoom.Mode);
    }

    [Fact]
    public async Task ApplySettings_ChangesNavigatorBehaviour()
    {
        using var fx = Create();
        var folder = fx.Folder;
        var session = fx.Session;
        await session.OpenAsync(folder.Path);

        session.ApplySettings(new ViewerSettings { WrapAround = false });

        Assert.False(await session.PreviousAsync()); // мы на первом, а переход по кругу выключен
        Assert.False(session.Navigator.WrapAround);
    }

    [Fact]
    public async Task Slideshow_StopsAtEndWithoutWrap()
    {
        using var fx = Create(new ViewerSettings { WrapAround = false });
        var folder = fx.Folder;
        var session = fx.Session;
        await session.OpenAsync(Path.Combine(folder.Path, "a10.bmp"));

        using var slideshow = new SlideshowController(session);
        var stopped = new TaskCompletionSource();
        slideshow.StateChanged += (_, _) =>
        {
            if (!slideshow.IsRunning) stopped.TrySetResult();
        };

        slideshow.Start(TimeSpan.FromMilliseconds(100));
        Assert.True(slideshow.IsRunning);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(slideshow.IsRunning);
        Assert.Equal(3, session.Navigator.CurrentIndex); // дошли до последнего
    }
}
