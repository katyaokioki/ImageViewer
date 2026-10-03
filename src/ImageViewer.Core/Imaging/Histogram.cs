using System.Text;

namespace ImageViewer.Core.Imaging;

public enum HistogramChannel
{
    Luminance,
    Red,
    Green,
    Blue
}

/// <summary>
/// Гистограмма изображения по каналам R, G, B и яркости (256 уровней).
/// Для больших изображений строится по равномерной выборке пикселей — это быстро и статистически достаточно.
/// </summary>
public sealed class Histogram
{
    public const int Bins = 256;

    private Histogram()
    {
    }

    public int[] Red { get; } = new int[Bins];
    public int[] Green { get; } = new int[Bins];
    public int[] Blue { get; } = new int[Bins];
    public int[] Luminance { get; } = new int[Bins];

    /// <summary>Сколько пикселей реально учтено.</summary>
    public long SampleCount { get; private set; }

    /// <summary>Шаг выборки (1 — учтён каждый пиксель).</summary>
    public int SampleStep { get; private set; } = 1;

    public double MeanLuminance { get; private set; }

    /// <summary>Максимум по всем каналам — удобно для нормировки при рисовании.</summary>
    public int MaxValue { get; private set; }

    public int[] GetChannel(HistogramChannel channel) => channel switch
    {
        HistogramChannel.Red => Red,
        HistogramChannel.Green => Green,
        HistogramChannel.Blue => Blue,
        _ => Luminance
    };

    /// <summary>Строит гистограмму.</summary>
    /// <param name="image">Изображение.</param>
    /// <param name="maxSamples">Ограничение числа учитываемых пикселей.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static Histogram Compute(RawImage image, int maxSamples = 2_000_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var result = new Histogram();
        int step = maxSamples > 0 && image.PixelCount > maxSamples
            ? (int)Math.Ceiling(Math.Sqrt((double)image.PixelCount / maxSamples))
            : 1;
        result.SampleStep = step;

        var px = image.Pixels;
        long lumSum = 0, count = 0;

        for (int y = 0; y < image.Height; y += step)
        {
            if ((y & 127) == 0) cancellationToken.ThrowIfCancellationRequested();

            int i = y * image.Stride;
            int rowStep = step * RawImage.BytesPerPixel;
            for (int x = 0; x < image.Width; x += step, i += rowStep)
            {
                byte b = px[i], g = px[i + 1], r = px[i + 2];
                int lum = (r * 299 + g * 587 + b * 114) / 1000;
                result.Red[r]++;
                result.Green[g]++;
                result.Blue[b]++;
                result.Luminance[lum]++;
                lumSum += lum;
                count++;
            }
        }

        result.SampleCount = count;
        result.MeanLuminance = count > 0 ? (double)lumSum / count : 0;
        result.MaxValue = new[] { result.Red.Max(), result.Green.Max(), result.Blue.Max(), result.Luminance.Max() }.Max();
        return result;
    }

    /// <summary>Значение (0..255), ниже которого лежит заданная доля пикселей канала.</summary>
    public int Percentile(HistogramChannel channel, double fraction)
    {
        var data = GetChannel(channel);
        long target = (long)Math.Ceiling(SampleCount * Math.Clamp(fraction, 0, 1));
        long acc = 0;
        for (int i = 0; i < Bins; i++)
        {
            acc += data[i];
            if (acc >= target && acc > 0) return i;
        }
        return Bins - 1;
    }

    /// <summary>Псевдографика для консоли: столбцы из символов блоков.</summary>
    public string ToAsciiChart(HistogramChannel channel = HistogramChannel.Luminance, int width = 64, int height = 12)
    {
        width = Math.Clamp(width, 8, Bins);
        height = Math.Clamp(height, 3, 50);
        var data = GetChannel(channel);

        // Группируем 256 уровней в width столбцов
        var columns = new long[width];
        for (int i = 0; i < Bins; i++)
            columns[i * width / Bins] += data[i];
        long max = Math.Max(1, columns.Max());

        var sb = new StringBuilder();
        for (int row = height; row >= 1; row--)
        {
            sb.Append('│');
            foreach (long c in columns)
            {
                double level = (double)c / max * height;
                sb.Append(level >= row ? '█' : level >= row - 0.5 ? '▄' : ' ');
            }
            sb.AppendLine();
        }
        sb.Append('└').Append('─', width).AppendLine();
        sb.Append(' ').Append('0').Append(' ', Math.Max(1, width - 4)).Append("255");
        return sb.ToString();
    }
}
