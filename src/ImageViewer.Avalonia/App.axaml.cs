using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Settings;
using ImageViewer.Ui.Views;

namespace ImageViewer.Ui;

/// <summary>
/// Приложение Avalonia: разбор командной строки → загрузка настроек → переопределения → главное окно.
/// Та же последовательность, что и в WPF-версии, — вся логика в ImageViewer.Core.
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var parse = CommandLineParser.Parse(args);
            var options = parse.Options;

            if (options.ShowHelp)
            {
                desktop.MainWindow = new HelpWindow(HelpText.Full(HotkeyMap.CreateDefault(), "ImageViewerX"));
            }
            else
            {
                var store = new JsonSettingsStore(options.SettingsPath);
                if (options.ResetSettings) store.Save(new ViewerSettings());
                var saved = store.Load();

                // Параметры командной строки действуют только на этот запуск
                var effective = saved.Clone();
                options.ApplyTo(effective);

                var window = new MainWindow(store, saved, effective, options);
                desktop.MainWindow = window;

                var messages = new List<string>(parse.Errors);
                if (store.LastError is { } error)
                    messages.Add($"Файл настроек повреждён, используются значения по умолчанию: {error}");
                if (messages.Count > 0)
                    window.Opened += (_, _) => window.ShowToast(string.Join(Environment.NewLine, messages), seconds: 6);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
