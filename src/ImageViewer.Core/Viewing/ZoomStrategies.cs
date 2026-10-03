namespace ImageViewer.Core.Viewing;

/// <summary>Режим масштабирования.</summary>
public enum ZoomMode
{
    /// <summary>Масштаб задан пользователем (колесо мыши, +/−).</summary>
    Manual,
    /// <summary>100% — один пиксель изображения на один пиксель экрана.</summary>
    ActualSize,
    /// <summary>Вписать целиком в экран (увеличивая маленькие).</summary>
    FitToScreen,
    /// <summary>Вписать, только если изображение больше экрана; маленькие — 100%.</summary>
    ShrinkToFit,
    /// <summary>По ширине экрана.</summary>
    FitWidth,
    /// <summary>По высоте экрана.</summary>
    FitHeight,
    /// <summary>Заполнить экран целиком (с обрезкой лишнего).</summary>
    Fill
}

/// <summary>
/// Стратегия вычисления масштаба (паттерн «Стратегия»). Каждый автоматический режим — отдельный наследник.
/// </summary>
public abstract class ZoomStrategy
{
    private static readonly Dictionary<ZoomMode, ZoomStrategy> Registry = new ZoomStrategy[]
    {
        new ActualSizeStrategy(),
        new FitToScreenStrategy(),
        new ShrinkToFitStrategy(),
        new FitWidthStrategy(),
        new FitHeightStrategy(),
        new FillStrategy()
    }.ToDictionary(s => s.Mode);

    public abstract ZoomMode Mode { get; }

    public abstract string DisplayName { get; }

    /// <summary>Выравнивать ли изображение по верхнему краю (удобно для длинных скриншотов при «по ширине»).</summary>
    public virtual bool AlignTop => false;

    /// <param name="image">Размер изображения с учётом поворота.</param>
    /// <param name="viewport">Размер области просмотра.</param>
    public abstract double ComputeScale(SizeD image, SizeD viewport);

    public static IReadOnlyCollection<ZoomStrategy> All => Registry.Values;

    public static ZoomStrategy For(ZoomMode mode) =>
        Registry.TryGetValue(mode, out var s)
            ? s
            : throw new ArgumentException("Для ручного режима нет стратегии.", nameof(mode));

    public static string GetDisplayName(ZoomMode mode) =>
        mode == ZoomMode.Manual ? "Вручную" : For(mode).DisplayName;

    public override string ToString() => DisplayName;
}

public sealed class ActualSizeStrategy : ZoomStrategy
{
    public override ZoomMode Mode => ZoomMode.ActualSize;
    public override string DisplayName => "100%";
    public override double ComputeScale(SizeD image, SizeD viewport) => 1.0;
}

public class FitToScreenStrategy : ZoomStrategy
{
    public override ZoomMode Mode => ZoomMode.FitToScreen;
    public override string DisplayName => "Вписать в экран";

    public override double ComputeScale(SizeD image, SizeD viewport) =>
        Math.Min(viewport.Width / image.Width, viewport.Height / image.Height);
}

/// <summary>Наследует «вписать», но не увеличивает маленькие изображения.</summary>
public sealed class ShrinkToFitStrategy : FitToScreenStrategy
{
    public override ZoomMode Mode => ZoomMode.ShrinkToFit;
    public override string DisplayName => "Уменьшить до экрана";
    public override double ComputeScale(SizeD image, SizeD viewport) => Math.Min(1.0, base.ComputeScale(image, viewport));
}

public sealed class FitWidthStrategy : ZoomStrategy
{
    public override ZoomMode Mode => ZoomMode.FitWidth;
    public override string DisplayName => "По ширине";
    public override bool AlignTop => true;
    public override double ComputeScale(SizeD image, SizeD viewport) => viewport.Width / image.Width;
}

public sealed class FitHeightStrategy : ZoomStrategy
{
    public override ZoomMode Mode => ZoomMode.FitHeight;
    public override string DisplayName => "По высоте";
    public override double ComputeScale(SizeD image, SizeD viewport) => viewport.Height / image.Height;
}

public sealed class FillStrategy : ZoomStrategy
{
    public override ZoomMode Mode => ZoomMode.Fill;
    public override string DisplayName => "Заполнить экран";

    public override double ComputeScale(SizeD image, SizeD viewport) =>
        Math.Max(viewport.Width / image.Width, viewport.Height / image.Height);
}
