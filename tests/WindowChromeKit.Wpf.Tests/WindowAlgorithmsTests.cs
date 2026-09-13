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

    /// <summary>客户区 = 窗口矩形内缩 frame；普通态顶部不内缩，最大化时四边都内缩。</summary>
    [Fact]
    public void ClientAreaInsetsFrameAndKeepsTopFlushWhenRestored()
    {
        var window = new NativeRectangle(100, 100, 876, 628);

        var restored = WindowFrameHitTest.InsetToClient(window, 8, 8, maximized: false);
        Assert.Equal(new NativeRectangle(108, 100, 868, 620), restored);

        var maximizedWindow = new NativeRectangle(-8, -8, 2568, 1400);
        var maximized = WindowFrameHitTest.InsetToClient(maximizedWindow, 8, 8, maximized: true);
        Assert.Equal(new NativeRectangle(0, 0, 2560, 1392), maximized);
    }

    /// <summary>
    /// 缩放带在窗口矩形之内（"阴影里那圈"），左右下为 frame 厚度、顶部更窄；
    /// 数值与 Chrome/C++ 示例实测一致。
    /// </summary>
    [Fact]
    public void ResizeBandsMatchMeasuredChromeGeometry()
    {
        var window = new NativeRectangle(100, 100, 876, 628);
        const int frame = 8;
        const int topBand = 6;

        int Hit(int x, int y) =>
            WindowFrameHitTest.EvaluateResizeHit(
                new NativePoint(x, y), window, frame, frame, topBand);

        Assert.Equal(WindowFrameHitTest.Left, Hit(100, 300));
        Assert.Equal(WindowFrameHitTest.Left, Hit(107, 300));
        Assert.Equal(WindowFrameHitTest.Client, Hit(108, 300));
        // 右侧/底部的 8px 带含最外一个像素，所以客户区从 R-9 / B-9 开始
        Assert.Equal(WindowFrameHitTest.Right, Hit(875, 300));
        Assert.Equal(WindowFrameHitTest.Right, Hit(868, 300));
        Assert.Equal(WindowFrameHitTest.Client, Hit(867, 300));
        Assert.Equal(WindowFrameHitTest.Bottom, Hit(400, 627));
        Assert.Equal(WindowFrameHitTest.Bottom, Hit(400, 620));
        Assert.Equal(WindowFrameHitTest.Client, Hit(400, 619));
        Assert.Equal(WindowFrameHitTest.TopLeft, Hit(100, 100));
        Assert.Equal(WindowFrameHitTest.TopLeft, Hit(107, 107));
        Assert.Equal(WindowFrameHitTest.TopRight, Hit(875, 100));
        Assert.Equal(WindowFrameHitTest.BottomLeft, Hit(100, 627));
        Assert.Equal(WindowFrameHitTest.BottomRight, Hit(875, 627));
        // 顶部带比四角矮：T+0..T+5 是 HTTOP，T+6 起交给标题栏
        Assert.Equal(WindowFrameHitTest.Top, Hit(400, 100));
        Assert.Equal(WindowFrameHitTest.Top, Hit(400, 105));
        Assert.Equal(WindowFrameHitTest.Client, Hit(400, 106));
    }

    /// <summary>窗口矩形之外的任何点都不属于本窗口（HTNOWHERE），与 Chrome 实测一致。</summary>
    [Fact]
    public void PointsOutsideWindowRectangleAreNotClaimed()
    {
        var window = new NativeRectangle(100, 100, 876, 628);

        Assert.False(WindowFrameHitTest.Contains(window, new NativePoint(99, 300)));
        Assert.False(WindowFrameHitTest.Contains(window, new NativePoint(400, 99)));
        Assert.False(WindowFrameHitTest.Contains(window, new NativePoint(876, 300)));
        Assert.False(WindowFrameHitTest.Contains(window, new NativePoint(400, 628)));
        Assert.True(WindowFrameHitTest.Contains(window, new NativePoint(100, 100)));
        Assert.True(WindowFrameHitTest.Contains(window, new NativePoint(875, 627)));
    }

    [Fact]
    public void NativePartClassificationSeparatesResizeFromCaptionButtons()
    {
        Assert.True(WindowFrameHitTest.IsResizeHit(WindowFrameHitTest.Left));
        Assert.True(WindowFrameHitTest.IsResizeHit(WindowFrameHitTest.Top));
        Assert.True(WindowFrameHitTest.IsResizeHit(WindowFrameHitTest.BottomRight));
        Assert.False(WindowFrameHitTest.IsResizeHit(WindowFrameHitTest.Caption));
        Assert.False(WindowFrameHitTest.IsResizeHit(WindowFrameHitTest.Nowhere));

        Assert.True(WindowFrameHitTest.IsCaptionButtonHit(WindowFrameHitTest.Close));
        Assert.True(WindowFrameHitTest.IsCaptionButtonHit(WindowFrameHitTest.MinButton));
        Assert.True(WindowFrameHitTest.IsCaptionButtonHit(WindowFrameHitTest.MaxButton));
        Assert.False(WindowFrameHitTest.IsCaptionButtonHit(WindowFrameHitTest.SystemMenu));
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
