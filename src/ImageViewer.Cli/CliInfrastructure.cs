using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageViewer.Core.Commands;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Cli;

/// <summary>Разобранные аргументы подкоманды: позиционные значения, именованные параметры и флаги.</summary>
public sealed class CliArguments
{
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    /// <summary>Аргументы после «--» передаются как есть (для команды check-args).</summary>
    public List<string> Rest { get; } = [];

    /// <param name="args">Аргументы после имени команды.</param>
    /// <param name="flags">Имена параметров без значения (например, "json", "desc").</param>
    public static CliArguments Parse(IEnumerable<string> args, IReadOnlySet<string> flags)
    {
        var result = new CliArguments();
        var list = args.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            string a = list[i];
            if (a == "--")
            {
                result.Rest.AddRange(list.Skip(i + 1));
                break;
            }
            if (a.StartsWith("--", StringComparison.Ordinal) && a.Length > 2)
            {
                string name = a[2..];
                int eq = name.IndexOf('=');
                if (eq > 0)
                    result._options[name[..eq]] = name[(eq + 1)..];
                else if (flags.Contains(name))
                    result._flags.Add(name);
                else if (i + 1 < list.Count)
                    result._options[name] = list[++i];
                else
                    throw new CliUsageException($"Параметр --{name} требует значение.");
            }
            else
            {
                result.Positional.Add(a);
            }
        }
        return result;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string name) => _options.GetValueOrDefault(name);

    public int GetInt(string name, int defaultValue, int min = int.MinValue, int max = int.MaxValue)
    {
        var raw = Get(name);
        if (raw is null) return defaultValue;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
            throw new CliUsageException($"--{name}: ожидается целое число от {min} до {max}, получено «{raw}».");
        return value;
    }

    public string RequirePositional(int index, string what) =>
        index < Positional.Count ? Positional[index] : throw new CliUsageException($"Не указан аргумент: {what}.");
}

/// <summary>Ошибка в использовании команды — печатается вместе с подсказкой.</summary>
public sealed class CliUsageException(string message) : Exception(message);

/// <summary>Общие зависимости команд.</summary>
public sealed class CliContext(IImageDecoder decoder, TextWriter output, HotkeyMap hotkeys)
{
    public IImageDecoder Decoder { get; } = decoder;
    public TextWriter Out { get; } = output;
    public HotkeyMap Hotkeys { get; } = hotkeys;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void WriteJson<T>(T value) => Out.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}

/// <summary>Подкоманда консольной утилиты.</summary>
public interface ICliCommand
{
    string Name { get; }
    string Summary { get; }
    string Usage { get; }

    /// <summary>Флаги (параметры без значения), которые понимает команда.</summary>
    IReadOnlySet<string> Flags { get; }

    int Execute(CliArguments args, CliContext context);
}

/// <summary>Базовый класс команд: общие флаги и вспомогательные методы.</summary>
public abstract class CliCommandBase : ICliCommand
{
    private static readonly HashSet<string> NoFlags = [];

    public abstract string Name { get; }
    public abstract string Summary { get; }
    public abstract string Usage { get; }
    public virtual IReadOnlySet<string> Flags => NoFlags;

    public abstract int Execute(CliArguments args, CliContext context);

    protected static string RequireFile(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new CliUsageException($"Файл не найден: {path}");
        return full;
    }

    protected static string RequireFileOrDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full) && !Directory.Exists(full)) throw new CliUsageException($"Файл или папка не найдены: {path}");
        return full;
    }

    /// <summary>Разбор размера вида «1920x1080».</summary>
    protected static (int Width, int Height) ParseSize(string value, string what)
    {
        var parts = value.ToLowerInvariant().Replace('×', 'x').Split('x');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
            w > 0 && h > 0)
            return (w, h);
        throw new CliUsageException($"{what}: ожидается размер вида 1920x1080, получено «{value}».");
    }

    protected static void WriteTable(TextWriter output, IEnumerable<KeyValuePair<string, string>> rows)
    {
        var list = rows.ToList();
        int width = list.Count == 0 ? 0 : list.Max(r => r.Key.Length);
        foreach (var (key, value) in list)
            output.WriteLine($"  {key.PadRight(width)}  {value}");
    }
}
