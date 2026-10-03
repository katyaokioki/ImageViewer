using Avalonia;

namespace ImageViewer.Ui;

internal static class Program
{
    // Точка входа. Аргументы командной строки передаются в App через ClassicDesktopStyleApplicationLifetime.Args
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Используется также дизайнером Avalonia в IDE
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
