using System.Drawing;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>
/// frame 几何与命中测试的纯计算部分。所有数值都取自真实 Chrome 窗口实测（见 README）：
/// 客户区内缩 (SM_CXFRAME + SM_CXPADDEDBORDER, 0, 同, 同)，四边缩放带 8px、顶部带 6px，
/// 按钮命中带从窗口矩形 T+1 起、46x39。
/// </summary>
internal static class ChromeFrameGeometry
{
    internal const int HtNowhere = 0;
    internal const int HtClient = 1;
    internal const int HtCaption = 2;
    internal const int HtSysMenu = 3;
    internal const int HtMinButton = 8;
    internal const int HtMaxButton = 9;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;
    internal const int HtClose = 20;

    /// <summary>窗口边框厚度：SM_CXFRAME + SM_CXPADDEDBORDER（96dpi 下为 8px）。</summary>
    internal static (int X, int Y) GetFrameThickness(uint dpi) =>
        (
            Math.Max(1, NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxFrame, dpi)
                + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpi)),
            Math.Max(1, NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyFrame, dpi)
                + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxPaddedBorder, dpi))
        );

    /// <summary>
    /// 标题栏系统菜单（图标）命中盒子的尺寸：原生窗口用 SM_CXSMSIZE × SM_CYSMSIZE
    /// （96dpi 下 22×22），是一个竖向居中的正方形，而不是贯穿整个标题栏高度。
    /// </summary>
    internal static (int Width, int Height) GetSystemMenuBoxSize(uint dpi) =>
        (
            NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmSize, dpi),
            NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCySmSize, dpi)
        );

    /// <summary>小图标尺寸（96dpi 下为 16px）。</summary>
    internal static int GetSmallIconSize(uint dpi) =>
        NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxSmIcon, dpi);

    /// <summary>
    /// 客户区 = 窗口矩形内缩出 frame 区域。普通态顶部不内缩（客户区顶到窗口顶边，
    /// 否则 Windows 10 的 DWM 会画整条原生标题栏）；最大化时四边都内缩，正好等于工作区。
    /// </summary>
    internal static NativeRectangle GetClientArea(
        NativeRectangle windowRect,
        int frameX,
        int frameY,
        bool maximized) =>
        new(
            windowRect.Left + frameX,
            windowRect.Top + (maximized ? frameY : 0),
            windowRect.Right - frameX,
            windowRect.Bottom - frameY);

    /// <summary>caption 按钮的矩形（窗口坐标）。按钮贴着客户区右边缘排列。</summary>
    internal static NativeRectangle GetCaptionButtonRect(
        NativeRectangle windowRect,
        int frameX,
        int captionButtonWidth,
        int captionButtonHeight,
        int captionButtonTop,
        ChromeCaptionButton button)
    {
        var index = button switch
        {
            ChromeCaptionButton.Close => 0,
            ChromeCaptionButton.Maximize => 1,
            _ => 2,
        };
        var right = windowRect.Right - frameX - index * captionButtonWidth;
        return new NativeRectangle(
            right - captionButtonWidth,
            windowRect.Top + captionButtonTop,
            right,
            windowRect.Top + captionButtonTop + captionButtonHeight);
    }

    /// <summary>
    /// 缩放部件命中：左/右/下各 frameX/frameY，顶部单独用较窄的 topBand。
    /// 不在任何边带内时返回 <see cref="HtClient"/>。
    /// </summary>
    internal static int EvaluateResizeHit(
        NativePoint pointer,
        NativeRectangle windowRect,
        int frameX,
        int frameY,
        int topBand)
    {
        var left = pointer.X < windowRect.Left + frameX;
        var right = pointer.X >= windowRect.Right - frameX;
        var bottom = pointer.Y >= windowRect.Bottom - frameY;
        var insideTopFrame = pointer.Y < windowRect.Top + frameY;
        if (left && insideTopFrame)
            return HtTopLeft;
        if (right && insideTopFrame)
            return HtTopRight;
        if (left && bottom)
            return HtBottomLeft;
        if (right && bottom)
            return HtBottomRight;
        if (left)
            return HtLeft;
        if (right)
            return HtRight;
        if (bottom)
            return HtBottom;
        if (pointer.Y < windowRect.Top + topBand)
            return HtTop;
        return HtClient;
    }

    internal static bool Contains(NativeRectangle bounds, NativePoint point) =>
        point.X >= bounds.Left && point.X < bounds.Right
        && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    internal static bool IsResizeHit(int hit) => hit is >= HtLeft and <= HtBottomRight;

    internal static bool IsCaptionButtonHit(int hit) => hit is HtMinButton or HtMaxButton or HtClose;
}
