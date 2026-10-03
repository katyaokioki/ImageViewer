using ImageViewer.Core.Viewing;

namespace ImageViewer.Core.Tests;

public class ZoomControllerTests
{
    private const double Eps = 1e-6;

    private static ZoomController Create(double iw, double ih, double vw, double vh, ZoomMode mode)
    {
        var zoom = new ZoomController();
        zoom.SetViewport(new SizeD(vw, vh));
        zoom.SetImage(new SizeD(iw, ih), mode);
        return zoom;
    }

    [Fact]
    public void FitToScreen_ScalesDownAndCenters()
    {
        var zoom = Create(4000, 3000, 1000, 1000, ZoomMode.FitToScreen);

        Assert.Equal(0.25, zoom.Scale, Eps);
        Assert.Equal(new SizeD(1000, 750), zoom.DisplaySize);
        Assert.Equal(0, zoom.OffsetX, Eps);
        Assert.Equal(125, zoom.OffsetY, Eps);
        Assert.Equal(25, zoom.ZoomPercent);
    }

    [Fact]
    public void FitToScreen_EnlargesSmallImage()
    {
        var zoom = Create(100, 50, 1000, 1000, ZoomMode.FitToScreen);
        Assert.Equal(10, zoom.Scale, Eps);
    }

    [Fact]
    public void ShrinkToFit_DoesNotEnlargeSmallImage()
    {
        var zoom = Create(200, 100, 1000, 1000, ZoomMode.ShrinkToFit);

        Assert.Equal(1, zoom.Scale, Eps);
        Assert.Equal(400, zoom.OffsetX, Eps);
        Assert.Equal(450, zoom.OffsetY, Eps);
    }

    [Fact]
    public void FitWidth_AlignsTallImageToTop()
    {
        var zoom = Create(1000, 5000, 500, 1000, ZoomMode.FitWidth);

        Assert.Equal(0.5, zoom.Scale, Eps);
        Assert.Equal(0, zoom.OffsetY, Eps);
        Assert.True(zoom.CanPan);
    }

    [Fact]
    public void FitHeight_UsesViewportHeight()
    {
        var zoom = Create(1000, 5000, 500, 1000, ZoomMode.FitHeight);
        Assert.Equal(0.2, zoom.Scale, Eps);
    }

    [Fact]
    public void Fill_CoversWholeViewport()
    {
        var zoom = Create(4000, 3000, 1000, 1000, ZoomMode.Fill);

        Assert.Equal(1.0 / 3, zoom.Scale, Eps);
        Assert.True(zoom.DisplaySize.Width >= 1000 - Eps);
        Assert.True(zoom.DisplaySize.Height >= 1000 - Eps);
    }

    [Fact]
    public void ActualSize_IsOneToOne()
    {
        var zoom = Create(4000, 3000, 1000, 1000, ZoomMode.ActualSize);
        Assert.Equal(1, zoom.Scale, Eps);
        Assert.Equal(100, zoom.ZoomPercent);
    }

    [Fact]
    public void ZoomAtPoint_KeepsImagePointUnderCursor()
    {
        var zoom = Create(1000, 1000, 500, 500, ZoomMode.ActualSize);
        var anchor = new PointD(100, 100);
        var before = zoom.ViewportToImage(anchor);

        zoom.ZoomBy(2, anchor);
        var after = zoom.ViewportToImage(anchor);

        Assert.Equal(2, zoom.Scale, Eps);
        Assert.Equal(ZoomMode.Manual, zoom.Mode);
        Assert.Equal(before.X, after.X, 1e-6);
        Assert.Equal(before.Y, after.Y, 1e-6);
    }

    [Fact]
    public void ZoomInAndOut_AreInverse()
    {
        var zoom = Create(1000, 1000, 500, 500, ZoomMode.ActualSize);
        zoom.ZoomIn();
        zoom.ZoomOut();
        Assert.Equal(1, zoom.Scale, Eps);
    }

    [Fact]
    public void Scale_IsClampedToLimits()
    {
        var zoom = Create(100, 100, 500, 500, ZoomMode.ActualSize);

        zoom.SetScale(1000);
        Assert.Equal(ZoomController.MaxScale, zoom.Scale, Eps);

        zoom.SetScale(0);
        Assert.Equal(ZoomController.MinScale, zoom.Scale, Eps);
    }

    [Fact]
    public void Pan_IsIgnoredWhenImageFitsViewport()
    {
        var zoom = Create(200, 100, 1000, 1000, ZoomMode.ShrinkToFit);
        zoom.Pan(300, 300);

        Assert.Equal(400, zoom.OffsetX, Eps);
        Assert.Equal(450, zoom.OffsetY, Eps);
    }

    [Fact]
    public void Pan_IsClampedToImageEdges()
    {
        var zoom = Create(2000, 2000, 500, 500, ZoomMode.ActualSize);

        zoom.Pan(100_000, 100_000);
        Assert.Equal(0, zoom.OffsetX, Eps);
        Assert.Equal(0, zoom.OffsetY, Eps);

        zoom.Pan(-100_000, -100_000);
        Assert.Equal(-1500, zoom.OffsetX, Eps);
        Assert.Equal(-1500, zoom.OffsetY, Eps);
    }

    [Fact]
    public void Rotate90_SwapsDimensionsAndRefits()
    {
        var zoom = Create(400, 200, 1000, 1000, ZoomMode.FitToScreen);
        Assert.Equal(2.5, zoom.Scale, Eps);

        zoom.RotateClockwise();

        Assert.Equal(Rotation.Rotate90, zoom.Rotation);
        Assert.Equal(new SizeD(200, 400), zoom.RotatedImageSize);
        Assert.Equal(2.5, zoom.Scale, Eps);
    }

    [Fact]
    public void RotateFourTimes_ReturnsToNone()
    {
        var zoom = Create(400, 200, 1000, 1000, ZoomMode.FitToScreen);
        for (int i = 0; i < 4; i++) zoom.RotateClockwise();
        Assert.Equal(Rotation.None, zoom.Rotation);

        zoom.RotateCounterClockwise();
        Assert.Equal(Rotation.Rotate270, zoom.Rotation);
    }

    [Fact]
    public void Transform_Rotate90_MapsCornersClockwise()
    {
        var zoom = Create(100, 50, 50, 100, ZoomMode.ActualSize);
        zoom.RotateClockwise();
        var m = zoom.GetTransform();

        // Левый верхний угол уходит в правый верхний, правый верхний — в правый нижний
        var topLeft = m.Transform(new PointD(0, 0));
        var topRight = m.Transform(new PointD(100, 0));
        Assert.Equal(50, topLeft.X, Eps);
        Assert.Equal(0, topLeft.Y, Eps);
        Assert.Equal(50, topRight.X, Eps);
        Assert.Equal(100, topRight.Y, Eps);
    }

    [Fact]
    public void ViewportToImage_IsInverseOfImageToViewport()
    {
        var zoom = Create(1234, 567, 800, 600, ZoomMode.FitToScreen);
        zoom.RotateClockwise();
        zoom.ZoomBy(1.7, new PointD(200, 300));

        var p = new PointD(321.5, 123.25);
        var back = zoom.ViewportToImage(zoom.ImageToViewport(p));

        Assert.Equal(p.X, back.X, 1e-6);
        Assert.Equal(p.Y, back.Y, 1e-6);
    }

    [Fact]
    public void ViewportResize_RecomputesAutomaticMode()
    {
        var zoom = Create(2000, 1000, 1000, 1000, ZoomMode.FitToScreen);
        Assert.Equal(0.5, zoom.Scale, Eps);

        zoom.SetViewport(new SizeD(500, 500));
        Assert.Equal(0.25, zoom.Scale, Eps);
        Assert.Equal(ZoomMode.FitToScreen, zoom.Mode);
    }

    [Fact]
    public void ViewportResize_KeepsManualScale()
    {
        var zoom = Create(2000, 1000, 1000, 1000, ZoomMode.ActualSize);
        zoom.SetScale(1.5);
        zoom.SetViewport(new SizeD(600, 600));
        Assert.Equal(1.5, zoom.Scale, Eps);
    }

    [Fact]
    public void SetImage_WithManualMode_KeepsScale()
    {
        var zoom = Create(2000, 1000, 1000, 1000, ZoomMode.ActualSize);
        zoom.SetScale(3);
        zoom.SetImage(new SizeD(500, 500), ZoomMode.Manual);
        Assert.Equal(3, zoom.Scale, Eps);
    }

    [Fact]
    public void Changed_IsRaisedOnZoom()
    {
        var zoom = Create(100, 100, 500, 500, ZoomMode.ActualSize);
        int raised = 0;
        zoom.Changed += (_, _) => raised++;

        zoom.ZoomIn();
        zoom.Pan(1, 1);
        zoom.ApplyMode(ZoomMode.FitToScreen);

        Assert.Equal(3, raised);
    }

    [Fact]
    public void ZoomStep_MustBeGreaterThanOne()
    {
        var zoom = new ZoomController();
        Assert.Throws<ArgumentOutOfRangeException>(() => zoom.ZoomStep = 1.0);
    }

    [Fact]
    public void StrategyRegistry_ContainsAllAutomaticModes()
    {
        var modes = ZoomStrategy.All.Select(s => s.Mode).ToHashSet();
        foreach (var mode in Enum.GetValues<ZoomMode>().Where(m => m != ZoomMode.Manual))
            Assert.Contains(mode, modes);
        Assert.Throws<ArgumentException>(() => ZoomStrategy.For(ZoomMode.Manual));
    }

    [Fact]
    public void ShrinkToFit_InheritsFitToScreen()
    {
        Assert.IsAssignableFrom<FitToScreenStrategy>(ZoomStrategy.For(ZoomMode.ShrinkToFit));
    }
}
