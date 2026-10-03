using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ImageViewer.Core.Imaging;

namespace ImageViewer.Wpf.Controls;

/// <summary>
/// Элемент, рисующий гистограмму: полупрозрачные области R, G, B и линия яркости.
/// Наследуется от FrameworkElement и рисует себя в <see cref="OnRender"/> — это быстрее, чем сотни фигур.
/// </summary>
public sealed class HistogramView : FrameworkElement
{
    public static readonly DependencyProperty HistogramProperty = DependencyProperty.Register(
        nameof(Histogram), typeof(Histogram), typeof(HistogramView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UseLogScaleProperty = DependencyProperty.Register(
        nameof(UseLogScale), typeof(bool), typeof(HistogramView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush RedFill = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0x40, 0x40)));
    private static readonly Brush GreenFill = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0xE0, 0x40)));
    private static readonly Brush BlueFill = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0x40, 0x80, 0xFF)));
    private static readonly Pen LuminancePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xE0, 0xF0, 0xF0, 0xF0)), 1.2));
    private static readonly Pen FramePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0x80, 0x80, 0x80)), 1));

    public Histogram? Histogram
    {
        get => (Histogram?)GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    /// <summary>Логарифмическая шкала — лучше видны редкие тона.</summary>
    public bool UseLogScale
    {
        get => (bool)GetValue(UseLogScaleProperty);
        set => SetValue(UseLogScaleProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(Brushes.Transparent, FramePen, rect);

        var histogram = Histogram;
        if (histogram is null || histogram.SampleCount == 0 || rect.Width < 2 || rect.Height < 2)
        {
            var text = new FormattedText("Вычисление…", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((rect.Width - text.Width) / 2, (rect.Height - text.Height) / 2));
            return;
        }

        // Нормируем по максимуму цветовых каналов
        double max = new[] { histogram.Red.Max(), histogram.Green.Max(), histogram.Blue.Max() }.Max();
        max = Math.Max(1, max);

        dc.DrawGeometry(RedFill, null, BuildGeometry(histogram.Red, max, rect, closed: true));
        dc.DrawGeometry(GreenFill, null, BuildGeometry(histogram.Green, max, rect, closed: true));
        dc.DrawGeometry(BlueFill, null, BuildGeometry(histogram.Blue, max, rect, closed: true));
        dc.DrawGeometry(null, LuminancePen, BuildGeometry(histogram.Luminance, Math.Max(max, histogram.Luminance.Max()), rect, closed: false));
    }

    private StreamGeometry BuildGeometry(int[] data, double max, Rect rect, bool closed)
    {
        double Scale(int v) => UseLogScale ? Math.Log(1 + v) / Math.Log(1 + max) : v / max;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            double step = rect.Width / (Histogram.Bins - 1);
            var start = closed ? new Point(0, rect.Height) : new Point(0, rect.Height - Math.Min(1, Scale(data[0])) * rect.Height);
            ctx.BeginFigure(start, closed, closed);
            for (int i = 0; i < Histogram.Bins; i++)
            {
                double y = rect.Height - Math.Min(1, Scale(data[i])) * rect.Height;
                ctx.LineTo(new Point(i * step, y), true, false);
            }
            if (closed) ctx.LineTo(new Point(rect.Width, rect.Height), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
