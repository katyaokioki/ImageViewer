namespace ImageViewer.Core.Imaging;

/// <summary>
/// Несжатое изображение в памяти в формате BGRA32 (4 байта на пиксель, построчно сверху вниз).
/// Это «общий язык» между декодерами разных платформ и логикой библиотеки (гистограмма и т.п.).
/// </summary>
public sealed class RawImage
{
    public const int BytesPerPixel = 4;

    public RawImage(int width, int height, byte[]? pixels = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        long expected = (long)width * height * BytesPerPixel;
        if (expected > Array.MaxLength)
            throw new ArgumentException("Изображение слишком велико для размещения в памяти.");

        if (pixels is not null && pixels.LongLength != expected)
            throw new ArgumentException($"Ожидалось {expected} байт пикселей, получено {pixels.LongLength}.", nameof(pixels));

        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[expected];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Пиксели BGRA32.</summary>
    public byte[] Pixels { get; }

    /// <summary>Число байт в одной строке.</summary>
    public int Stride => Width * BytesPerPixel;

    public long PixelCount => (long)Width * Height;

    public PixelColor GetPixel(int x, int y)
    {
        int i = IndexOf(x, y);
        return new PixelColor(Pixels[i + 2], Pixels[i + 1], Pixels[i], Pixels[i + 3]);
    }

    public void SetPixel(int x, int y, PixelColor c)
    {
        int i = IndexOf(x, y);
        Pixels[i] = c.B;
        Pixels[i + 1] = c.G;
        Pixels[i + 2] = c.R;
        Pixels[i + 3] = c.A;
    }

    private int IndexOf(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return y * Stride + x * BytesPerPixel;
    }
}

/// <summary>Цвет пикселя (RGBA).</summary>
public readonly record struct PixelColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Яркость по ITU-R BT.601.</summary>
    public byte Luminance => (byte)((R * 299 + G * 587 + B * 114) / 1000);

    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
