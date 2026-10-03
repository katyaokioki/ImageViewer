using ImageViewer.Core.Navigation;

namespace ImageViewer.Core.Tests;

public class NavigatorTests
{
    private static TempFolder CreateFolder()
    {
        var folder = new TempFolder();
        folder.File("img1.png", 300);
        folder.File("img2.png", 100);
        folder.File("img10.png", 200);
        folder.File("readme.txt", 50);
        return folder;
    }

    private static string[] Names(FolderNavigator nav) => nav.Files.Select(f => Path.GetFileName(f)).ToArray();

    [Fact]
    public void Open_File_SelectsItInNaturalOrder()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();

        nav.Open(Path.Combine(folder.Path, "img2.png"));

        Assert.Equal(new[] { "img1.png", "img2.png", "img10.png" }, Names(nav));
        Assert.Equal(1, nav.CurrentIndex);
        Assert.Equal(3, nav.Count);
        Assert.EndsWith("img2.png", nav.CurrentPath);
    }

    [Fact]
    public void Open_Directory_SelectsFirstFile()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();

        nav.Open(folder.Path);

        Assert.Equal(0, nav.CurrentIndex);
        Assert.EndsWith("img1.png", nav.CurrentPath);
    }

    [Fact]
    public void Open_MissingPath_Throws()
    {
        var nav = new FolderNavigator();
        Assert.Throws<FileNotFoundException>(() => nav.Open(Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid())));
    }

    [Fact]
    public void Open_UnsupportedFileExplicitly_IsIncluded()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();

        nav.Open(Path.Combine(folder.Path, "readme.txt"));

        Assert.Equal(4, nav.Count);
        Assert.EndsWith("readme.txt", nav.CurrentPath);
    }

    [Fact]
    public void MoveNext_WrapsAroundByDefault()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        nav.Open(Path.Combine(folder.Path, "img10.png"));

        Assert.True(nav.MoveNext());
        Assert.Equal(0, nav.CurrentIndex);

        Assert.True(nav.MovePrevious());
        Assert.Equal(2, nav.CurrentIndex);
    }

    [Fact]
    public void MoveNext_WithoutWrap_StopsAtEnd()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator { WrapAround = false };
        nav.Open(Path.Combine(folder.Path, "img10.png"));

        Assert.False(nav.HasNext);
        Assert.False(nav.MoveNext());
        Assert.Equal(2, nav.CurrentIndex);
        Assert.True(nav.HasPrevious);
    }

    [Fact]
    public void MoveBy_NegativeWithWrap_ComputesModulo()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        nav.Open(folder.Path);

        Assert.True(nav.MoveBy(-5)); // 0 - 5 = -5 → (−5 mod 3) = 1
        Assert.Equal(1, nav.CurrentIndex);
    }

    [Fact]
    public void MoveFirstAndLast_Work()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        nav.Open(Path.Combine(folder.Path, "img2.png"));

        Assert.True(nav.MoveLast());
        Assert.Equal(2, nav.CurrentIndex);
        Assert.False(nav.MoveLast());
        Assert.True(nav.MoveFirst());
        Assert.Equal(0, nav.CurrentIndex);
    }

    [Fact]
    public void CurrentChanged_IsRaisedWithPositions()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        var events = new List<NavigationChangedEventArgs>();
        nav.CurrentChanged += (_, e) => events.Add(e);

        nav.Open(folder.Path);
        nav.MoveNext();

        Assert.Equal(2, events.Count);
        Assert.Equal(2, events[1].Number);
        Assert.Equal(3, events[1].Count);
        Assert.EndsWith("img1.png", events[1].PreviousPath);
    }

    [Fact]
    public void SortBySize_Descending()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator(sortMode: SortMode.Size, sortDescending: true);
        nav.Open(Path.Combine(folder.Path, "img2.png"));

        Assert.Equal(new[] { "img1.png", "img10.png", "img2.png" }, Names(nav));
        Assert.EndsWith("img2.png", nav.CurrentPath); // текущий файл сохраняется
    }

    [Fact]
    public void ChangingSort_KeepsCurrentFile()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        nav.Open(Path.Combine(folder.Path, "img10.png"));

        nav.SortMode = SortMode.Size;

        Assert.Equal(new[] { "img2.png", "img10.png", "img1.png" }, Names(nav));
        Assert.EndsWith("img10.png", nav.CurrentPath);
    }

    [Fact]
    public void Refresh_AfterDeletingCurrent_SelectsNeighbour()
    {
        using var folder = CreateFolder();
        var nav = new FolderNavigator();
        nav.Open(Path.Combine(folder.Path, "img2.png"));

        File.Delete(Path.Combine(folder.Path, "img2.png"));
        folder.File("img3.png");
        nav.Refresh();

        Assert.Equal(new[] { "img1.png", "img3.png", "img10.png" }, Names(nav));
        Assert.Equal(1, nav.CurrentIndex);
    }

    [Fact]
    public void EmptyFolder_HasNoCurrent()
    {
        using var folder = new TempFolder();
        var nav = new FolderNavigator();
        nav.Open(folder.Path);

        Assert.Equal(0, nav.Count);
        Assert.Equal(-1, nav.CurrentIndex);
        Assert.Null(nav.CurrentPath);
        Assert.False(nav.MoveNext());
    }
}

public class NaturalStringComparerTests
{
    [Fact]
    public void SortsNumbersByValue()
    {
        var names = new List<string> { "img10.png", "img2.png", "IMG1.png", "img1b.png" };
        names.Sort(NaturalStringComparer.Instance);
        Assert.Equal(new[] { "IMG1.png", "img1b.png", "img2.png", "img10.png" }, names);
    }

    [Theory]
    [InlineData("a2", "a10", -1)]
    [InlineData("a10", "a2", 1)]
    [InlineData("photo", "Photo", 1)]   // одинаковы без учёта регистра — порядок по ordinal
    [InlineData("b", "A", 1)]
    [InlineData("file099", "file100", -1)]
    [InlineData("x", "x", 0)]
    public void Compare_ReturnsExpectedSign(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(NaturalStringComparer.Instance.Compare(a, b)));
    }

    [Fact]
    public void Nulls_AreOrderedFirst()
    {
        Assert.True(NaturalStringComparer.Instance.Compare(null, "a") < 0);
        Assert.True(NaturalStringComparer.Instance.Compare("a", null) > 0);
    }
}
