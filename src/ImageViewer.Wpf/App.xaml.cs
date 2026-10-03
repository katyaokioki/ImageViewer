using System.Windows;
using System.Windows.Threading;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Settings;
using ImageViewer.Wpf.Views;

namespace ImageViewer.Wpf;

/// <summary>
/// Точка входа: разбор командной строки → загрузка настроек → применение переопределений → главное окно.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var parse = CommandLineParser.Parse(e.Args);
        var options = parse.Options;

        if (options.ShowHelp)
        {
            // Запуск с --help: показываем только справку
            var help = new HelpWindow(HelpText.Full(HotkeyMap.CreateDefault()))
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true
            };
            MainWindow = help;
            help.Show();
            return;
        }

        if (!parse.IsValid)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, parse.Errors) + Environment.NewLine + Environment.NewLine +
                "Справка по параметрам: ImageViewer --help или F1 в программе.",
                "Ошибки в параметрах командной строки", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var store = new JsonSettingsStore(options.SettingsPath);
        if (options.ResetSettings) store.Save(new ViewerSettings());
        var saved = store.Load();

        // Параметры командной строки действуют только на этот запуск и не портят сохранённые настройки
        var effective = saved.Clone();
        options.ApplyTo(effective);

        var window = new MainWindow(store, saved, effective, options);
        MainWindow = window;
        window.Show();

        if (store.LastError is { } error)
            window.ShowToast($"Файл настроек повреждён, используются значения по умолчанию: {error}");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Непредвиденная ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
