using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Ui.Controls;

/// <summary>
/// Пользовательский элемент — гистограмма R, G, B и яркости.
/// Наследуется от Control и рисует себя в <see cref="Render"/>.
/// </summary>
public sealed class HistogramControl : Control
{
    public static readonly StyledProperty<Histogram?> HistogramProperty =
        AvaloniaProperty.Register<HistogramControl, Histogram?>(nameof(Histogram));

    public static readonly StyledProperty<bool> UseLogScaleProperty =
        AvaloniaProperty.Register<HistogramControl, bool>(nameof(UseLogScale));

    private static readonly IBrush RedFill = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0x40, 0x40));
    private static readonly IBrush GreenFill = new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0xE0, 0x40));
    private static readonly IBrush BlueFill = new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0x80, 0xFF));
    private static readonly IPen LuminancePen = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xF0, 0xF0, 0xF0)), 1.2);
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), 1);

    static HistogramControl()
    {
        // Перерисовка при изменении свойств
        AffectsRender<HistogramControl>(HistogramProperty, UseLogScaleProperty);
    }

    public Histogram? Histogram
    {
        get => GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public bool UseLogScale
    {
        get => GetValue(UseLogScaleProperty);
        set => SetValue(UseLogScaleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(Brushes.Transparent, FramePen, rect);

        var histogram = Histogram;
        if (histogram is null || histogram.SampleCount == 0 || rect.Width < 2 || rect.Height < 2)
        {
            var text = new FormattedText("Вычисление…", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, 11, Brushes.Gray);
            context.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
            return;
        }

        double max = Math.Max(1, new[] { histogram.Red.Max(), histogram.Green.Max(), histogram.Blue.Max() }.Max());

        context.DrawGeometry(RedFill, null, BuildGeometry(histogram.Red, max, rect, closed: true));
        context.DrawGeometry(GreenFill, null, BuildGeometry(histogram.Green, max, rect, closed: true));
        context.DrawGeometry(BlueFill, null, BuildGeometry(histogram.Blue, max, rect, closed: true));
        context.DrawGeometry(null, LuminancePen,
            BuildGeometry(histogram.Luminance, Math.Max(max, histogram.Luminance.Max()), rect, closed: false));
    }

    private StreamGeometry BuildGeometry(int[] data, double max, Rect rect, bool closed)
    {
        bool log = UseLogScale;
        double Scale(int v) => Math.Min(1, log ? Math.Log(1 + v) / Math.Log(1 + max) : v / max);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double step = rect.Width / (Histogram.Bins - 1);
            var start = closed
                ? new Point(0, rect.Height)
                : new Point(0, rect.Height - Scale(data[0]) * rect.Height);
            ctx.BeginFigure(start, closed);
            for (int i = 0; i < Histogram.Bins; i++)
                ctx.LineTo(new Point(i * step, rect.Height - Scale(data[i]) * rect.Height));
            if (closed) ctx.LineTo(new Point(rect.Width, rect.Height));
            ctx.EndFigure(closed);
        }
        return geometry;
    }
}
