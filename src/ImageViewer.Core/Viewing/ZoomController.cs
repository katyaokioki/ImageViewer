namespace ImageViewer.Core.Viewing;

/// <summary>
/// Модель «камеры» просмотра: масштаб, смещение, поворот и режим вписывания.
/// Ничего не знает о UI: на вход — размеры изображения и окна в пикселях,
/// на выход — матрица преобразования, которую любой UI-фреймворк применяет к картинке.
/// </summary>
public sealed class ZoomController
{
    public const double MinScale = 0.01;
    public const double MaxScale = 64.0;

    private double _zoomStep = 1.25;

    /// <summary>Размер исходного изображения (без поворота).</summary>
    public SizeD ImageSize { get; private set; } = SizeD.Empty;

    /// <summary>Размер области просмотра.</summary>
    public SizeD ViewportSize { get; private set; } = SizeD.Empty;

    /// <summary>Текущий масштаб (1.0 = 100%).</summary>
    public double Scale { get; private set; } = 1.0;

    /// <summary>Координата X левого верхнего угла отображаемого (повёрнутого) изображения в окне.</summary>
    public double OffsetX { get; private set; }

    /// <summary>Координата Y левого верхнего угла отображаемого изображения в окне.</summary>
    public double OffsetY { get; private set; }

    public Rotation Rotation { get; private set; } = Rotation.None;

    public ZoomMode Mode { get; private set; } = ZoomMode.ShrinkToFit;

    /// <summary>Множитель шага при увеличении/уменьшении.</summary>
    public double ZoomStep
    {
        get => _zoomStep;
        set => _zoomStep = value > 1.0 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Шаг должен быть больше 1.");
    }

    /// <summary>Возникает при любом изменении масштаба, смещения или поворота.</summary>
    public event EventHandler? Changed;

    public bool IsReady => !ImageSize.IsEmpty && !ViewportSize.IsEmpty;

    /// <summary>Размер изображения с учётом поворота (при 90/270 ширина и высота меняются местами).</summary>
    public SizeD RotatedImageSize =>
        Rotation is Rotation.Rotate90 or Rotation.Rotate270
            ? new SizeD(ImageSize.Height, ImageSize.Width)
            : ImageSize;

    /// <summary>Размер изображения на экране.</summary>
    public SizeD DisplaySize => new(RotatedImageSize.Width * Scale, RotatedImageSize.Height * Scale);

    public int ZoomPercent => (int)Math.Round(Scale * 100);

    /// <summary>Можно ли перемещать изображение (оно больше окна хотя бы по одной оси).</summary>
    public bool CanPan => DisplaySize.Width > ViewportSize.Width + 0.5 || DisplaySize.Height > ViewportSize.Height + 0.5;

    /// <summary>Новое изображение: сбрасываем поворот и применяем режим.</summary>
    public void SetImage(SizeD imageSize, ZoomMode mode)
    {
        ImageSize = imageSize;
        Rotation = Rotation.None;
        if (mode == ZoomMode.Manual)
        {
            // Сохраняем текущий масштаб, просто центрируем
            Mode = ZoomMode.Manual;
            Center();
            Clamp();
            OnChanged();
        }
        else
        {
            ApplyMode(mode);
        }
    }

    /// <summary>Изменение размера окна. Точка изображения в центре окна остаётся в центре.</summary>
    public void SetViewport(SizeD viewport)
    {
        if (viewport == ViewportSize) return;

        var oldViewport = ViewportSize;
        var (fx, fy) = CenterFraction(oldViewport);
        ViewportSize = viewport;

        if (!IsReady)
        {
            OnChanged();
            return;
        }

        if (oldViewport.IsEmpty)
        {
            ApplyMode(Mode);
            return;
        }

        if (Mode != ZoomMode.Manual)
            Scale = ClampScale(ZoomStrategy.For(Mode).ComputeScale(RotatedImageSize, ViewportSize));

        var d = DisplaySize;
        OffsetX = ViewportSize.Width / 2 - fx * d.Width;
        OffsetY = ViewportSize.Height / 2 - fy * d.Height;
        Clamp();
        OnChanged();
    }

    /// <summary>Установить режим масштабирования (100%, вписать, по ширине…).</summary>
    public void ApplyMode(ZoomMode mode)
    {
        Mode = mode;
        if (mode == ZoomMode.Manual || !IsReady)
        {
            OnChanged();
            return;
        }

        var strategy = ZoomStrategy.For(mode);
        Scale = ClampScale(strategy.ComputeScale(RotatedImageSize, ViewportSize));
        Center();
        if (strategy.AlignTop) OffsetY = 0;
        Clamp();
        OnChanged();
    }

    public void ZoomIn(PointD? anchor = null) => ZoomBy(ZoomStep, anchor);

    public void ZoomOut(PointD? anchor = null) => ZoomBy(1.0 / ZoomStep, anchor);

    public void ZoomBy(double factor, PointD? anchor = null) => SetScale(Scale * factor, anchor);

    /// <summary>
    /// Установить масштаб, сохраняя неподвижной точку <paramref name="anchor"/> (например, курсор мыши).
    /// По умолчанию — центр окна.
    /// </summary>
    public void SetScale(double scale, PointD? anchor = null)
    {
        if (!IsReady) return;
        double newScale = ClampScale(scale);
        var a = anchor ?? new PointD(ViewportSize.Width / 2, ViewportSize.Height / 2);
        double ratio = newScale / Scale;

        OffsetX = a.X - (a.X - OffsetX) * ratio;
        OffsetY = a.Y - (a.Y - OffsetY) * ratio;
        Scale = newScale;
        Mode = ZoomMode.Manual;
        Clamp();
        OnChanged();
    }

    /// <summary>Сдвиг изображения на (dx, dy) пикселей экрана.</summary>
    public void Pan(double dx, double dy)
    {
        if (!IsReady) return;
        OffsetX += dx;
        OffsetY += dy;
        Clamp();
        OnChanged();
    }

    public void RotateClockwise() => Rotate(90);

    public void RotateCounterClockwise() => Rotate(270);

    private void Rotate(int degrees)
    {
        Rotation = (Rotation)(((int)Rotation + degrees) % 360);
        if (Mode != ZoomMode.Manual)
        {
            ApplyMode(Mode);
            return;
        }
        Center();
        Clamp();
        OnChanged();
    }

    /// <summary>
    /// Матрица «пиксель изображения → пиксель окна»: поворот вокруг центра, масштаб, перенос.
    /// </summary>
    public Matrix2D GetTransform()
    {
        double w = ImageSize.Width, h = ImageSize.Height, k = Scale;
        double angle = (int)Rotation * Math.PI / 180.0;
        // Округляем, чтобы для 90° получить точные 0 и 1, а не 6e-17
        double c = Math.Round(Math.Cos(angle)), s = Math.Round(Math.Sin(angle));
        var d = DisplaySize;

        // Поворот по часовой стрелке в экранных координатах (ось Y вниз): x' = x·c − y·s, y' = x·s + y·c
        double m11 = c * k, m12 = s * k, m21 = -s * k, m22 = c * k;
        double cx = w / 2, cy = h / 2;
        double offsetX = OffsetX + d.Width / 2 - (cx * m11 + cy * m21);
        double offsetY = OffsetY + d.Height / 2 - (cx * m12 + cy * m22);
        return new Matrix2D(m11, m12, m21, m22, offsetX, offsetY);
    }

    /// <summary>Перевод координат окна в координаты пикселя исходного изображения.</summary>
    public PointD ViewportToImage(PointD point) => GetTransform().Invert().Transform(point);

    /// <summary>Перевод координат пикселя изображения в координаты окна.</summary>
    public PointD ImageToViewport(PointD point) => GetTransform().Transform(point);

    private (double fx, double fy) CenterFraction(SizeD viewport)
    {
        var d = DisplaySize;
        if (viewport.IsEmpty || d.IsEmpty) return (0.5, 0.5);
        return ((viewport.Width / 2 - OffsetX) / d.Width, (viewport.Height / 2 - OffsetY) / d.Height);
    }

    private void Center()
    {
        var d = DisplaySize;
        OffsetX = (ViewportSize.Width - d.Width) / 2;
        OffsetY = (ViewportSize.Height - d.Height) / 2;
    }

    /// <summary>
    /// Ограничение смещения: если изображение меньше окна — центрируем, иначе не даём «уехать» за край.
    /// </summary>
    private void Clamp()
    {
        var d = DisplaySize;
        OffsetX = ClampAxis(OffsetX, d.Width, ViewportSize.Width);
        OffsetY = ClampAxis(OffsetY, d.Height, ViewportSize.Height);
    }

    private static double ClampAxis(double offset, double content, double view) =>
        content <= view ? (view - content) / 2 : Math.Clamp(offset, view - content, 0);

    private static double ClampScale(double scale) =>
        double.IsFinite(scale) ? Math.Clamp(scale, MinScale, MaxScale) : 1.0;

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
