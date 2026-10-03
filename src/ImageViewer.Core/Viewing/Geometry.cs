namespace ImageViewer.Core.Viewing;

/// <summary>Размер в пикселях (не зависит от System.Drawing / WPF / Avalonia).</summary>
public readonly record struct SizeD(double Width, double Height)
{
    public static SizeD Empty { get; } = new(0, 0);
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public override string ToString() => $"{Width:0.##}×{Height:0.##}";
}

/// <summary>Точка в пикселях.</summary>
public readonly record struct PointD(double X, double Y)
{
    public override string ToString() => $"({X:0.##}; {Y:0.##})";
}

/// <summary>Поворот изображения по часовой стрелке.</summary>
public enum Rotation
{
    None = 0,
    Rotate90 = 90,
    Rotate180 = 180,
    Rotate270 = 270
}

/// <summary>
/// Аффинная матрица 3×2 в соглашении «вектор-строка» (как System.Windows.Media.Matrix и Avalonia.Matrix):
/// x' = x·M11 + y·M21 + OffsetX;  y' = x·M12 + y·M22 + OffsetY.
/// </summary>
public readonly record struct Matrix2D(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static Matrix2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public double Determinant => M11 * M22 - M12 * M21;

    public PointD Transform(PointD p) =>
        new(p.X * M11 + p.Y * M21 + OffsetX, p.X * M12 + p.Y * M22 + OffsetY);

    public Matrix2D Invert()
    {
        double det = Determinant;
        if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Матрица вырождена.");
        double i11 = M22 / det, i12 = -M12 / det, i21 = -M21 / det, i22 = M11 / det;
        return new Matrix2D(i11, i12, i21, i22,
            -(OffsetX * i11 + OffsetY * i21),
            -(OffsetX * i12 + OffsetY * i22));
    }
}
