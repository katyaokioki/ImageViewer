using System.Text;
using ImageViewer.Cli;
using ImageViewer.Cli.Commands;
using ImageViewer.Core.CommandLine;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Imaging;

// ivc — Image Viewer Console: консольный интерфейс к той же библиотеке ImageViewer.Core,
// что и графический просмотрщик. Удобно для демонстрации в Jupyter Notebook и для скриптов.

Console.OutputEncoding = Encoding.UTF8;

ICliCommand[] commands =
[
    new InfoCommand(),
    new ListCommand(),
    new NavigateCommand(),
    new HistogramCommand(),
    new FitCommand(),
    new GenerateCommand(),
    new CheckArgsCommand(),
    new KeysCommand(),
    new SettingsCommand(),
];

var context = new CliContext(CreateDecoder(), Console.Out, HotkeyMap.CreateDefault());

if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "-?" or "/?")
{
    if (args.Length > 1 && commands.FirstOrDefault(c => c.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase)) is { } target)
    {
        Console.WriteLine($"{target.Name} — {target.Summary}");
        Console.WriteLine($"Использование: {target.Usage}");
        return 0;
    }
    PrintHelp();
    return 0;
}

var command = commands.FirstOrDefault(c => c.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase));
if (command is null)
{
    Console.Error.WriteLine($"Неизвестная команда «{args[0]}». Список команд: ivc help");
    return 2;
}

try
{
    var parsed = CliArguments.Parse(args.Skip(1), command.Flags);
    return command.Execute(parsed, context);
}
catch (CliUsageException ex)
{
    Console.Error.WriteLine($"Ошибка: {ex.Message}");
    Console.Error.WriteLine($"Использование: {command.Usage}");
    return 2;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
{
    Console.Error.WriteLine($"Ошибка: {ex.Message}");
    return 1;
}

void PrintHelp()
{
    Console.WriteLine("ivc — консольный инструмент Image Viewer");
    Console.WriteLine($"Декодер: {context.Decoder.Name}");
    Console.WriteLine();
    Console.WriteLine("Команды:");
    foreach (var c in commands)
        Console.WriteLine($"  {c.Name,-12} {c.Summary}");
    Console.WriteLine();
    Console.WriteLine("Подробнее о команде: ivc help <команда>");
    Console.WriteLine();
    Console.WriteLine("Графический просмотрщик запускается так:");
    Console.WriteLine(HelpText.CommandLineUsage("ImageViewer.exe"));
}

static IImageDecoder CreateDecoder()
{
#if WINDOWS
    // На Windows: встроенный BMP + WIC для всех остальных форматов
    return new CompositeImageDecoder(new BmpDecoder(), new ImageViewer.Imaging.Wic.WicImageDecoder());
#else
    // На Linux/macOS: встроенный BMP + SkiaSharp для JPEG, PNG, WebP, GIF, ICO
    return new CompositeImageDecoder(new BmpDecoder(), new ImageViewer.Imaging.Skia.SkiaImageDecoder());
#endif
}
