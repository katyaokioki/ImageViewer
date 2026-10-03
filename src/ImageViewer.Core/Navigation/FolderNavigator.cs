using ImageViewer.Core.Imaging;

namespace ImageViewer.Core.Navigation;

/// <summary>Порядок сортировки файлов в папке.</summary>
public enum SortMode
{
    Name,
    DateModified,
    Size,
    Extension
}

/// <summary>Данные события смены текущего изображения.</summary>
public sealed class NavigationChangedEventArgs(int index, int count, string? path, string? previousPath) : EventArgs
{
    public int Index { get; } = index;
    public int Count { get; } = count;
    public string? Path { get; } = path;
    public string? PreviousPath { get; } = previousPath;

    /// <summary>Номер для отображения пользователю (с единицы).</summary>
    public int Number => Index + 1;
}

/// <summary>Навигация по набору изображений.</summary>
public interface IImageNavigator
{
    string? FolderPath { get; }
    IReadOnlyList<string> Files { get; }
    int Count { get; }
    int CurrentIndex { get; }
    string? CurrentPath { get; }
    bool WrapAround { get; set; }
    SortMode SortMode { get; set; }
    bool SortDescending { get; set; }
    bool HasNext { get; }
    bool HasPrevious { get; }

    event EventHandler<NavigationChangedEventArgs>? CurrentChanged;

    /// <summary>Открыть файл (будет выбран в его папке) или папку (будет выбран первый файл).</summary>
    void Open(string path);

    bool MoveNext();
    bool MovePrevious();
    bool MoveFirst();
    bool MoveLast();
    bool MoveTo(int index);
    bool MoveBy(int delta);

    /// <summary>Перечитать содержимое папки, сохранив текущий файл.</summary>
    void Refresh();
}

/// <summary>Навигатор по изображениям в одной папке файловой системы.</summary>
public sealed class FolderNavigator : IImageNavigator
{
    private readonly List<string> _files = [];
    private readonly Func<string, bool> _filter;
    private SortMode _sortMode;
    private bool _sortDescending;

    public FolderNavigator(Func<string, bool>? filter = null, SortMode sortMode = SortMode.Name, bool sortDescending = false)
    {
        _filter = filter ?? ImageFormats.IsSupported;
        _sortMode = sortMode;
        _sortDescending = sortDescending;
    }

    public string? FolderPath { get; private set; }
    public IReadOnlyList<string> Files => _files;
    public int Count => _files.Count;
    public int CurrentIndex { get; private set; } = -1;
    public string? CurrentPath => CurrentIndex >= 0 && CurrentIndex < _files.Count ? _files[CurrentIndex] : null;
    public bool WrapAround { get; set; } = true;
    public bool HasNext => Count > 0 && (WrapAround ? Count > 1 : CurrentIndex < Count - 1);
    public bool HasPrevious => Count > 0 && (WrapAround ? Count > 1 : CurrentIndex > 0);

    public SortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (_sortMode == value) return;
            _sortMode = value;
            Resort();
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (_sortDescending == value) return;
            _sortDescending = value;
            Resort();
        }
    }

    public event EventHandler<NavigationChangedEventArgs>? CurrentChanged;

    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string? target = null;
        string folder;

        if (File.Exists(full))
        {
            folder = Path.GetDirectoryName(full)!;
            target = full;
        }
        else if (Directory.Exists(full))
        {
            folder = full;
        }
        else
        {
            throw new FileNotFoundException($"Файл или папка не найдены: {path}", path);
        }

        string? previous = CurrentPath;
        FolderPath = folder;
        LoadFolder(extraFile: target);

        int index = target is null ? (_files.Count > 0 ? 0 : -1) : IndexOf(target);
        SetIndex(index, previous, force: true);
    }

    public bool MoveNext() => MoveBy(1);
    public bool MovePrevious() => MoveBy(-1);
    public bool MoveFirst() => MoveTo(0);
    public bool MoveLast() => MoveTo(Count - 1);

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= Count || index == CurrentIndex) return false;
        SetIndex(index, CurrentPath);
        return true;
    }

    public bool MoveBy(int delta)
    {
        if (Count == 0 || delta == 0) return false;

        int target = CurrentIndex + delta;
        if (WrapAround)
        {
            // Переход по кругу: после последнего — первый
            target = ((target % Count) + Count) % Count;
        }
        else
        {
            target = Math.Clamp(target, 0, Count - 1);
        }

        if (target == CurrentIndex) return false;
        SetIndex(target, CurrentPath);
        return true;
    }

    public void Refresh()
    {
        if (FolderPath is null) return;
        string? current = CurrentPath;
        int oldIndex = CurrentIndex;

        if (!Directory.Exists(FolderPath))
        {
            _files.Clear();
            SetIndex(-1, current, force: true);
            return;
        }

        LoadFolder(extraFile: null);
        int index = current is null ? -1 : IndexOf(current);
        if (index < 0 && Count > 0)
            index = Math.Clamp(oldIndex, 0, Count - 1); // текущий файл удалён — берём соседний

        SetIndex(index, current, force: true);
    }

    private void Resort()
    {
        if (FolderPath is null) return;
        string? current = CurrentPath;
        SortFiles(_files.Select(f => new FileInfo(f)).ToList());
        CurrentIndex = current is null ? -1 : IndexOf(current);
        CurrentChanged?.Invoke(this, new NavigationChangedEventArgs(CurrentIndex, Count, CurrentPath, current));
    }

    private void LoadFolder(string? extraFile)
    {
        var infos = new DirectoryInfo(FolderPath!)
            .EnumerateFiles()
            .Where(f => (f.Attributes & FileAttributes.Hidden) == 0 && _filter(f.FullName))
            .ToList();

        // Файл, явно переданный пользователем, показываем даже если его расширение не в списке
        if (extraFile is not null && !infos.Any(f => PathEquals(f.FullName, extraFile)))
            infos.Add(new FileInfo(extraFile));

        SortFiles(infos);
    }

    private void SortFiles(List<FileInfo> infos)
    {
        IComparer<string> byName = NaturalStringComparer.Instance;
        IOrderedEnumerable<FileInfo> ordered = _sortMode switch
        {
            SortMode.DateModified => infos.OrderBy(f => f.LastWriteTimeUtc),
            SortMode.Size => infos.OrderBy(f => f.Exists ? f.Length : 0),
            SortMode.Extension => infos.OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase),
            _ => infos.OrderBy(f => f.Name, byName)
        };
        if (_sortMode != SortMode.Name) ordered = ordered.ThenBy(f => f.Name, byName);

        var list = ordered.Select(f => f.FullName).ToList();
        if (_sortDescending) list.Reverse();

        _files.Clear();
        _files.AddRange(list);
    }

    private int IndexOf(string path) => _files.FindIndex(f => PathEquals(f, path));

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void SetIndex(int index, string? previousPath, bool force = false)
    {
        if (!force && index == CurrentIndex) return;
        CurrentIndex = index;
        CurrentChanged?.Invoke(this, new NavigationChangedEventArgs(CurrentIndex, Count, CurrentPath, previousPath));
    }
}
