using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Navigation;
using ImageViewer.Core.Session;
using ImageViewer.Core.Settings;
using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Tests;

public class CommandLineParserTests
{
    [Fact]
    public void Parses_PathAndOptions()
    {
        var r = CommandLineParser.Parse(["photo.jpg", "--zoom", "fit", "-f", "-t", "light", "--info"]);

        Assert.True(r.IsValid);
        Assert.Equal("photo.jpg", r.Options.Path);
        Assert.Equal(ZoomMode.FitToScreen, r.Options.ZoomMode);
        Assert.Equal(WindowStartMode.Fullscreen, r.Options.WindowMode);
        Assert.Equal(AppTheme.Light, r.Options.Theme);
        Assert.True(r.Options.ShowInfoPanel);
    }

    [Theory]
    [InlineData("100", ZoomMode.ActualSize)]
    [InlineData("fit", ZoomMode.FitToScreen)]
    [InlineData("shrink", ZoomMode.ShrinkToFit)]
    [InlineData("WIDTH", ZoomMode.FitWidth)]
    [InlineData("height", ZoomMode.FitHeight)]
    [InlineData("fill", ZoomMode.Fill)]
    public void Parses_ZoomModes_IncludingEqualsSyntax(string value, ZoomMode expected)
    {
        var r = CommandLineParser.Parse([$"--zoom={value}"]);
        Assert.True(r.IsValid);
        Assert.Equal(expected, r.Options.ZoomMode);
    }

    [Fact]
    public void Slideshow_WithOptionalInterval()
    {
        var withInterval = CommandLineParser.Parse(["--slideshow", "5", "C:\\Photos"]);
        Assert.True(withInterval.Options.StartSlideshow);
        Assert.Equal(5, withInterval.Options.SlideshowIntervalSeconds);
        Assert.Equal("C:\\Photos", withInterval.Options.Path);

        var withoutInterval = CommandLineParser.Parse(["-s", "photo.jpg"]);
        Assert.True(withoutInterval.Options.StartSlideshow);
        Assert.Null(withoutInterval.Options.SlideshowIntervalSeconds);
        Assert.Equal("photo.jpg", withoutInterval.Options.Path);
    }

    [Fact]
    public void Background_AcceptsColorAndKeywords()
    {
        Assert.Equal(BackgroundMode.Checkerboard, CommandLineParser.Parse(["-b", "checker"]).Options.BackgroundMode);

        var color = CommandLineParser.Parse(["--background", "#FF8800"]).Options;
        Assert.Equal(BackgroundMode.SolidColor, color.BackgroundMode);
        Assert.Equal("#FF8800", color.BackgroundColor);
    }

    [Theory]
    [InlineData("--zoom", "huge")]
    [InlineData("--theme", "pink")]
    [InlineData("--index", "0")]
    [InlineData("--sort", "random")]
    [InlineData("--background", "red")]
    public void InvalidValues_ProduceErrors(string option, string value)
    {
        var r = CommandLineParser.Parse([option, value]);
        Assert.False(r.IsValid);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void UnknownOptionAndMissingValue_ProduceErrors()
    {
        var r = CommandLineParser.Parse(["--bogus", "--zoom"]);
        Assert.Equal(2, r.Errors.Count);
    }

    [Fact]
    public void SecondPositional_IsAnError()
    {
        var r = CommandLineParser.Parse(["a.jpg", "b.jpg"]);
        Assert.False(r.IsValid);
        Assert.Equal("a.jpg", r.Options.Path);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("-?")]
    [InlineData("/?")]
    public void Help_IsRecognized(string arg)
    {
        Assert.True(CommandLineParser.Parse([arg]).Options.ShowHelp);
    }

    [Fact]
    public void ApplyTo_OverridesOnlySpecifiedSettings()
    {
        var settings = new ViewerSettings { Theme = AppTheme.Dark, WrapAround = true, SortMode = SortMode.Name };
        var r = CommandLineParser.Parse(["--sort", "date", "--desc", "--no-wrap", "--zoom", "100", "-n", "3"]);

        r.Options.ApplyTo(settings);

        Assert.Equal(AppTheme.Dark, settings.Theme); // не указано — не изменилось
        Assert.Equal(SortMode.DateModified, settings.SortMode);
        Assert.True(settings.SortDescending);
        Assert.False(settings.WrapAround);
        Assert.Equal(ZoomMode.ActualSize, settings.DefaultZoomMode);
        Assert.Equal(3, r.Options.Index);
        Assert.True(r.Options.HasOverrides);
    }

    [Fact]
    public void HelpText_MentionsAllMainOptions()
    {
        var text = HelpText.CommandLineUsage();
        foreach (var option in new[] { "--zoom", "--fullscreen", "--theme", "--background", "--slideshow", "--sort", "--index", "--help" })
            Assert.Contains(option, text);
    }
}

public class HotkeyMapTests
{
    [Theory]
    [InlineData("shift+ctrl+o", "Ctrl+Shift+O")]
    [InlineData("Control+Alt+Right", "Ctrl+Alt+Right")]
    [InlineData("ctrl++", "Ctrl+Plus")]
    [InlineData("esc", "Escape")]
    [InlineData("pgdn", "PageDown")]
    [InlineData("f1", "F1")]
    public void Normalize_ProducesCanonicalGesture(string input, string expected)
    {
        Assert.Equal(expected, HotkeyMap.Normalize(input));
    }

    [Theory]
    [InlineData("Right", ViewerCommand.NextImage)]
    [InlineData("Left", ViewerCommand.PreviousImage)]
    [InlineData("ctrl+o", ViewerCommand.OpenFile)]
    [InlineData("F1", ViewerCommand.ShowHelp)]
    [InlineData("1", ViewerCommand.ZoomActualSize)]
    [InlineData("4", ViewerCommand.ZoomFitWidth)]
    [InlineData("Plus", ViewerCommand.ZoomIn)]
    [InlineData("F11", ViewerCommand.ToggleFullscreen)]
    [InlineData("Ctrl+Right", ViewerCommand.PanRight)]
    [InlineData("Ctrl+Shift+Alt+Z", ViewerCommand.None)]
    public void Default_ResolvesGestures(string gesture, ViewerCommand expected)
    {
        Assert.Equal(expected, HotkeyMap.CreateDefault().Resolve(gesture));
    }

    [Fact]
    public void EveryCatalogCommand_HasAtLeastOneGesture()
    {
        var map = HotkeyMap.CreateDefault();
        foreach (var info in CommandCatalog.All)
            Assert.NotEmpty(map.GetGestures(info.Command));
    }

    [Fact]
    public void Rebinding_ReplacesCommand()
    {
        var map = HotkeyMap.CreateDefault();
        map.Bind(ViewerCommand.Exit, "X");
        Assert.Equal(ViewerCommand.Exit, map.Resolve("x"));
        Assert.True(map.Unbind("X"));
        Assert.Equal(ViewerCommand.None, map.Resolve("X"));
    }

    [Fact]
    public void UnknownModifier_Throws()
    {
        Assert.Throws<FormatException>(() => HotkeyMap.Normalize("Hyper+A"));
    }

    [Fact]
    public void HelpText_ContainsKeysAndDescriptions()
    {
        var text = HotkeyMap.CreateDefault().BuildHelpText();
        Assert.Contains("F1", text);
        Assert.Contains("Следующее изображение", text);
        Assert.Contains("Колесо", text);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var folder = new TempFolder();
        var store = new JsonSettingsStore(Path.Combine(folder.Path, "sub", "settings.json"));
        var settings = new ViewerSettings
        {
            Theme = AppTheme.Light,
            BackgroundMode = BackgroundMode.Checkerboard,
            DefaultZoomMode = ZoomMode.FitWidth,
            SlideshowIntervalSeconds = 7.5,
            LastOpenedPath = "C:\\photo.jpg"
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal(BackgroundMode.Checkerboard, loaded.BackgroundMode);
        Assert.Equal(ZoomMode.FitWidth, loaded.DefaultZoomMode);
        Assert.Equal(7.5, loaded.SlideshowIntervalSeconds);
        Assert.Equal("C:\\photo.jpg", loaded.LastOpenedPath);
        Assert.Contains("\"theme\": \"Light\"", File.ReadAllText(store.Location)); // перечисления хранятся строками
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var folder = new TempFolder();
        var loaded = new JsonSettingsStore(Path.Combine(folder.Path, "none.json")).Load();
        Assert.Equal(AppTheme.Dark, loaded.Theme);
    }

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaultsAndReportsError()
    {
        using var folder = new TempFolder();
        var path = Path.Combine(folder.Path, "settings.json");
        File.WriteAllText(path, "{ это не JSON");
        var store = new JsonSettingsStore(path);

        var loaded = store.Load();

        Assert.Equal(new ViewerSettings().Theme, loaded.Theme);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void Normalize_FixesInvalidValues()
    {
        var s = new ViewerSettings
        {
            ZoomStep = 0.5,
            SlideshowIntervalSeconds = 100_000,
            BackgroundColor = "red",
            DefaultZoomMode = ZoomMode.Manual
        }.Normalize();

        Assert.Equal(1.25, s.ZoomStep);
        Assert.Equal(ViewerSettings.MaxSlideshowSeconds, s.SlideshowIntervalSeconds);
        Assert.True(ViewerSettings.IsValidHexColor(s.BackgroundColor));
        Assert.Equal(ZoomMode.ShrinkToFit, s.DefaultZoomMode);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var a = new ViewerSettings { Theme = AppTheme.Dark };
        var b = a.Clone();
        b.Theme = AppTheme.Light;
        Assert.Equal(AppTheme.Dark, a.Theme);
    }

    [Theory]
    [InlineData("#fff", true)]
    [InlineData("#112233", true)]
    [InlineData("#80112233", true)]
    [InlineData("112233", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    public void HexColorValidation(string value, bool expected)
    {
        Assert.Equal(expected, ViewerSettings.IsValidHexColor(value));
    }
}

public class LruCacheTests
{
    [Fact]
    public void EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        Assert.True(cache.TryGet("a", out _)); // «a» становится самым свежим
        cache.Set("c", 3);

        Assert.False(cache.Contains("b"));
        Assert.True(cache.Contains("a"));
        Assert.True(cache.Contains("c"));
        Assert.Equal(new[] { "c", "a" }, cache.Keys);
    }

    [Fact]
    public void Set_ExistingKey_UpdatesValue()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("a", 5);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("a", out int value));
        Assert.Equal(5, value);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var cache = new LruCache<int, string>(3);
        cache.Set(1, "x");
        cache.Set(2, "y");

        Assert.True(cache.Remove(1));
        Assert.False(cache.Remove(1));
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }
}
