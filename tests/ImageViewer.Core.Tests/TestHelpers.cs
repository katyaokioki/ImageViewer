using ImageViewer.Core.Imaging;

namespace ImageViewer.Core.Tests;

/// <summary>Временная папка, удаляемая после теста.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ivtests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, int size = 0)
    {
        var full = System.IO.Path.Combine(Path, name);
        System.IO.File.WriteAllBytes(full, new byte[size]);
        return full;
    }

    public string Bmp(string name, int width, int height, PixelColor? color = null)
    {
        var full = System.IO.Path.Combine(Path, name);
        BmpEncoder.Save(TestImages.Solid(width, height, color ?? new PixelColor(10, 20, 30)), full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

internal static class TestImages
{
    public static RawImage Solid(int width, int height, PixelColor color)
    {
        var image = new RawImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image.SetPixel(x, y, color);
        return image;
    }
}
