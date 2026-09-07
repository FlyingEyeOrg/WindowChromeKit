namespace WindowChromeKit.Wpf.Tests;

public sealed class WindowAlgorithmsTests
{
    [Theory]
    [InlineData(96, 136, 8, 146)]
    [InlineData(120, 170, 9, 182)]
    [InlineData(144, 202, 11, 218)]
    [InlineData(192, 262, 13, 289)]
    public void MinimumTrackWidthContainsCaptionButtonsAndResizeBorder(
        uint dpi, int systemMinimum, int resizeBorderWidth, int expected)
    {
        var result = WindowFrameHitTest.CalculateMinimumTrackWidth(
            0, systemMinimum, resizeBorderWidth, 46 * 3, dpi);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    public void ResizeOverlayUsesTopInsideAndOtherBordersOutside(int border)
    {
        var owner = new NativeRectangle(-1800, -200, -800, 500);
        var overlay = WindowResizeOverlay.CalculateBounds(owner, border, border);

        Assert.Equal(owner.Left - border, overlay.Left);
        Assert.Equal(owner.Top, overlay.Top);
        Assert.Equal(owner.Right + border, overlay.Right);
        Assert.Equal(owner.Bottom + border, overlay.Bottom);
        Assert.Equal(WindowFrameHitTest.TopLeft, Hit(overlay.Left, overlay.Top));
        Assert.Equal(WindowFrameHitTest.TopRight, Hit(overlay.Right - 1, overlay.Top));
        Assert.Equal(WindowFrameHitTest.BottomLeft, Hit(overlay.Left, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.BottomRight, Hit(overlay.Right - 1, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.Left, Hit(overlay.Left, owner.Top + 100));
        Assert.Equal(WindowFrameHitTest.Right, Hit(overlay.Right - 1, owner.Top + 100));
        Assert.Equal(WindowFrameHitTest.Top, Hit(owner.Left + 100, owner.Top));
        Assert.Equal(WindowFrameHitTest.Bottom, Hit(owner.Left + 100, overlay.Bottom - 1));
        Assert.Equal(WindowFrameHitTest.Client, Hit(owner.Left + 100, owner.Top + border));

        int Hit(int x, int y) => WindowResizeOverlay.EvaluateHit(
            new NativePoint(x, y), overlay, border, border);
    }

    [Fact]
    public void CaptionPartsUseActualScreenRectangles()
    {
        var icon = new NativeRectangle(110, 109, 126, 125);
        var minimize = new NativeRectangle(962, 100, 1008, 135);
        var maximize = new NativeRectangle(1008, 100, 1054, 135);
        var close = new NativeRectangle(1054, 100, 1100, 135);

        Assert.Equal(WindowFrameHitTest.SystemMenu, Hit(110, 109));
        Assert.Equal(WindowFrameHitTest.MinButton, Hit(980, 125));
        Assert.Equal(WindowFrameHitTest.MaxButton, Hit(1020, 125));
        Assert.Equal(WindowFrameHitTest.Close, Hit(1080, 125));
        Assert.Equal(WindowFrameHitTest.Caption, Hit(500, 125));
        Assert.Equal(WindowFrameHitTest.Client, Hit(500, 135));

        int Hit(int x, int y) => WindowFrameHitTest.Evaluate(
            new NativePoint(x, y), icon, minimize, maximize, close, 135);
    }

    [Fact]
    public void MaximizedPlacementSupportsNegativeCoordinateMonitors()
    {
        var monitor = new NativeRectangle(-2560, -200, 0, 1240);
        var workArea = new NativeRectangle(-2560, -160, 0, 1200);

        var result = WindowFrameHitTest.CalculateMaximizedPlacement(monitor, workArea);

        Assert.Equal(0, result.Position.X);
        Assert.Equal(40, result.Position.Y);
        Assert.Equal(2560, result.Size.X);
        Assert.Equal(1360, result.Size.Y);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1032)]
    [InlineData(0, 48, 1920, 1080)]
    [InlineData(48, 0, 1920, 1080)]
    [InlineData(0, 0, 1872, 1080)]
    public void MaximizedClientIsClampedForEveryTaskbarEdge(
        int left, int top, int right, int bottom)
    {
        var workArea = new NativeRectangle(left, top, right, bottom);
        var proposed = new NativeRectangle(left - 8, top - 8, right + 8, bottom + 8);

        Assert.Equal(workArea, WindowFrameHitTest.ClampToWorkArea(proposed, workArea));
    }

    [Fact]
    public void PlacementCentersInOwnerAndClampsToWorkArea()
    {
        var target = new MonitorTarget(
            IntPtr.Zero,
            new NativeRectangle(-1920, 0, 0, 1080),
            144,
            144);
        var owner = new NativeRectangle(-1500, 200, -900, 800);

        var centered = WindowPlacement.Center(target, 640, 480, owner);
        var clamped = WindowPlacement.Clamp(
            target,
            new NativeRectangle(-2500, -200, 500, 1400));

        Assert.Equal(WindowPlacement.DipToPixels(640, 144), centered.Width);
        Assert.Equal(WindowPlacement.DipToPixels(480, 144), centered.Height);
        Assert.InRange(centered.Left, target.WorkArea.Left, target.WorkArea.Right - centered.Width);
        Assert.InRange(centered.Top, target.WorkArea.Top, target.WorkArea.Bottom - centered.Height);
        Assert.Equal(target.WorkArea, clamped);
    }
}
