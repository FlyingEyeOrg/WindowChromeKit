namespace WindowChromeKit.Wpf.Internal;

/// <summary>
/// frame 几何与命中判定的纯计算部分，数值与 C++ 示例（<c>WindowChromeKit.Native.Sample</c>）
/// 及真实 Chrome 窗口实测一致：客户区从窗口矩形内缩
/// (SM_CXFRAME + SM_CXPADDEDBORDER, 0, 同, 同)，左右下缩放带 8px、顶部带 6px（96dpi）。
/// </summary>
internal static class WindowFrameHitTest
{
    // WM_NCHITTEST 返回值
    internal const int Nowhere = 0;
    internal const int Client = 1;
    internal const int Caption = 2;
    internal const int SystemMenu = 3;
    internal const int MinButton = 8;
    internal const int MaxButton = 9;
    internal const int Left = 10;
    internal const int Right = 11;
    internal const int Top = 12;
    internal const int TopLeft = 13;
    internal const int TopRight = 14;
    internal const int Bottom = 15;
    internal const int BottomLeft = 16;
    internal const int BottomRight = 17;
    internal const int Close = 20;

    /// <summary>顶部缩放带宽度（DIP）。实测 Chrome 为 6，明显窄于其余三边的 8。</summary>
    internal const int TopResizeBandDip = 6;

    /// <summary>窗口边框厚度：SM_CXFRAME + SM_CXPADDEDBORDER（96dpi 下为 8px）。</summary>
    internal static (int X, int Y) GetFrameThickness(uint dpi)
    {
        var bordered = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCxPaddedBorder,
            dpi
        );
        return (
            Math.Max(
                1,
                NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi) + bordered
            ),
            Math.Max(
                1,
                NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyFrame, dpi) + bordered
            )
        );
    }

    /// <summary>按 DPI 缩放的顶部缩放带宽度。</summary>
    internal static int GetTopResizeBand(uint dpi) =>
        Math.Max(1, (int)Math.Round(TopResizeBandDip * (dpi == 0 ? 96u : dpi) / 96.0));

    /// <summary>
    /// 客户区 = 窗口矩形内缩出 frame 区域。普通态顶部不内缩（客户区顶到窗口顶边，
    /// 否则 Windows 10 的 DWM 会画整条原生标题栏）；最大化时四边都内缩，正好等于工作区。
    /// </summary>
    internal static NativeRectangle InsetToClient(
        NativeRectangle windowRect,
        int frameX,
        int frameY,
        bool maximized
    ) =>
        new(
            windowRect.Left + frameX,
            windowRect.Top + (maximized ? frameY : 0),
            windowRect.Right - frameX,
            windowRect.Bottom - frameY
        );

    /// <summary>
    /// 缩放部件命中：左/右/下各 frameX/frameY，四角按 frameY 高度，顶部带单独用较窄的 topBand。
    /// 命中按钮之前调用；不在任何带内时返回 <see cref="Client"/>。
    /// </summary>
    internal static int EvaluateResizeHit(
        NativePoint pointer,
        NativeRectangle windowRect,
        int frameX,
        int frameY,
        int topBand
    )
    {
        var left = pointer.X < windowRect.Left + frameX;
        var right = pointer.X >= windowRect.Right - frameX;
        var bottom = pointer.Y >= windowRect.Bottom - frameY;
        var insideTopFrame = pointer.Y < windowRect.Top + frameY;
        if (left && insideTopFrame)
            return TopLeft;
        if (right && insideTopFrame)
            return TopRight;
        if (left && bottom)
            return BottomLeft;
        if (right && bottom)
            return BottomRight;
        if (left)
            return Left;
        if (right)
            return Right;
        if (bottom)
            return Bottom;
        if (pointer.Y < windowRect.Top + topBand)
            return Top;
        return Client;
    }

    internal static int CalculateMinimumTrackWidth(
        int currentMinimum,
        int systemMinimum,
        int resizeBorderWidth,
        double captionButtonsWidthDip,
        uint dpi
    )
    {
        var scale = (dpi == 0 ? 96u : dpi) / 96d;
        var captionButtonsWidth = (int)Math.Ceiling(Math.Max(0, captionButtonsWidthDip) * scale);
        var requiredWidth = captionButtonsWidth + Math.Max(0, resizeBorderWidth);
        return Math.Max(Math.Max(0, currentMinimum), Math.Max(Math.Max(0, systemMinimum), requiredWidth));
    }

    internal static bool Contains(NativeRectangle bounds, NativePoint point) =>
        point.X >= bounds.Left && point.X < bounds.Right
        && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    internal static bool IsResizeHit(int hit) => hit is >= Left and <= BottomRight;

    internal static bool IsCaptionButtonHit(int hit) => hit is MinButton or MaxButton or Close;
}
