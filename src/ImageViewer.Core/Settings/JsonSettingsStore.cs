using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageViewer.Core.Settings;

/// <summary>Хранилище настроек (файл, реестр, облако — зависит от реализации).</summary>
public interface ISettingsStore
{
    /// <summary>Где хранятся настройки (путь к файлу и т.п.).</summary>
    string Location { get; }

    ViewerSettings Load();

    void Save(ViewerSettings settings);
}

/// <summary>Настройки в JSON-файле. По умолчанию: %AppData%/ImageViewer/settings.json.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public JsonSettingsStore(string? path = null)
    {
        Location = path ?? DefaultPath;
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ImageViewer", "settings.json");

    public string Location { get; }

    /// <summary>Сообщение о последней ошибке загрузки (null — ошибок не было).</summary>
    public string? LastError { get; private set; }

    public ViewerSettings Load()
    {
        LastError = null;
        if (!File.Exists(Location)) return new ViewerSettings();

        try
        {
            var json = File.ReadAllText(Location);
            var settings = JsonSerializer.Deserialize<ViewerSettings>(json, Options) ?? new ViewerSettings();
            return settings.Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Повреждённый файл не должен мешать запуску — используем значения по умолчанию
            LastError = ex.Message;
            return new ViewerSettings();
        }
    }

    public void Save(ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = Path.GetDirectoryName(Location);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(Location, Serialize(settings));
    }

    public static string Serialize(ViewerSettings settings) => JsonSerializer.Serialize(settings, Options);
}
