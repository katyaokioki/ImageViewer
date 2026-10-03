namespace ImageViewer.Core.Imaging;

/// <summary>
/// Декодер изображений. Конкретная реализация зависит от платформы:
/// WIC для Windows/WPF, ImageSharp или SkiaSharp для Avalonia/MAUI, встроенный BMP-декодер и т.д.
/// </summary>
public interface IImageDecoder
{
    /// <summary>Название декодера (для отображения в информации о файле).</summary>
    string Name { get; }

    /// <summary>Может ли декодер открыть данный файл.</summary>
    bool CanDecode(string path);

    /// <summary>Декодирует файл в BGRA32.</summary>
    RawImage Decode(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Базовый класс для декодеров, работающих со списком расширений и потоком.
/// Наследники реализуют только <see cref="DecodeStream"/>.
/// </summary>
public abstract class ImageDecoderBase : IImageDecoder
{
    private readonly HashSet<string> _extensions;

    protected ImageDecoderBase(string name, IEnumerable<string> extensions)
    {
        Name = name;
        _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }

    public IReadOnlySet<string> Extensions => _extensions;

    public virtual bool CanDecode(string path) => _extensions.Contains(Path.GetExtension(path));

    public RawImage Decode(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return DecodeStream(stream, cancellationToken);
    }

    /// <summary>Декодирует изображение из потока.</summary>
    public abstract RawImage DecodeStream(Stream stream, CancellationToken cancellationToken = default);

    public override string ToString() => Name;
}

/// <summary>
/// Составной декодер (паттерн «Компоновщик» / «Цепочка»): перебирает декодеры по порядку
/// и использует первый, который умеет открывать файл.
/// </summary>
public sealed class CompositeImageDecoder : IImageDecoder
{
    private readonly List<IImageDecoder> _decoders;

    public CompositeImageDecoder(params IEnumerable<IImageDecoder> decoders)
    {
        _decoders = decoders.ToList();
    }

    public string Name => string.Join(" + ", _decoders.Select(d => d.Name));

    public IReadOnlyList<IImageDecoder> Decoders => _decoders;

    public void Add(IImageDecoder decoder) => _decoders.Add(decoder);

    public bool CanDecode(string path) => _decoders.Any(d => d.CanDecode(path));

    /// <summary>Возвращает декодер, который будет использован для файла.</summary>
    public IImageDecoder? Resolve(string path) => _decoders.FirstOrDefault(d => d.CanDecode(path));

    public RawImage Decode(string path, CancellationToken cancellationToken = default)
    {
        var decoder = Resolve(path)
            ?? throw new NotSupportedException($"Нет декодера для файла «{Path.GetFileName(path)}».");
        return decoder.Decode(path, cancellationToken);
    }
}
