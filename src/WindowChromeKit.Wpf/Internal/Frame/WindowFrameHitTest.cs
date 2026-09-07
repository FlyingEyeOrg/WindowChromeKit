namespace WindowChromeKit.Wpf.Internal;

internal static class WindowFrameHitTest
{
    internal const int Client = 1;
    internal const int Caption = 2;
    internal const int SystemMenu = 3;
    internal const int MinButton = 8;
    internal const int Left = 10;
    internal const int Right = 11;
    internal const int Top = 12;
    internal const int TopLeft = 13;
    internal const int TopRight = 14;
    internal const int Bottom = 15;
    internal const int BottomLeft = 16;
    internal const int BottomRight = 17;
    internal const int Close = 20;
    internal const int MaxButton = 9;

    internal static int Evaluate(
        NativePoint pointer,
        NativeRectangle icon,
        NativeRectangle minimize,
        NativeRectangle maximize,
        NativeRectangle close,
        int titleBottom)
    {
        if (Contains(close, pointer)) return Close;
        if (Contains(maximize, pointer)) return MaxButton;
        if (Contains(minimize, pointer)) return MinButton;
        if (Contains(icon, pointer)) return SystemMenu;
        return pointer.Y < titleBottom ? Caption : Client;
    }

    internal static (NativePoint Position, NativePoint Size) CalculateMaximizedPlacement(
        NativeRectangle monitor,
        NativeRectangle workArea) =>
        (
            new NativePoint(workArea.Left - monitor.Left, workArea.Top - monitor.Top),
            new NativePoint(workArea.Width, workArea.Height));

    internal static int CalculateMinimumTrackWidth(
        int currentMinimum,
        int systemMinimum,
        int resizeBorderWidth,
        double captionButtonsWidthDip,
        uint dpi)
    {
        var scale = (dpi == 0 ? 96u : dpi) / 96d;
        var captionButtonsWidth = (int)Math.Ceiling(Math.Max(0, captionButtonsWidthDip) * scale);
        var requiredWidth = captionButtonsWidth + Math.Max(0, resizeBorderWidth);
        return Math.Max(Math.Max(0, currentMinimum), Math.Max(Math.Max(0, systemMinimum), requiredWidth));
    }

    internal static NativeRectangle ClampToWorkArea(NativeRectangle proposed, NativeRectangle workArea) =>
        new(
            Math.Max(proposed.Left, workArea.Left),
            Math.Max(proposed.Top, workArea.Top),
            Math.Min(proposed.Right, workArea.Right),
            Math.Min(proposed.Bottom, workArea.Bottom));

    internal static bool MatchesMaximizedBounds(
        NativeRectangle proposed,
        NativeRectangle workArea,
        int resizeBorderWidth,
        int resizeBorderHeight)
    {
        var borderX = Math.Max(0, resizeBorderWidth);
        var borderY = Math.Max(0, resizeBorderHeight);
        return proposed.Left == workArea.Left - borderX
            && proposed.Top == workArea.Top - borderY
            && proposed.Right == workArea.Right + borderX
            && proposed.Bottom == workArea.Bottom + borderY;
    }

    private static bool Contains(NativeRectangle bounds, NativePoint point) =>
        point.X >= bounds.Left && point.X < bounds.Right
        && point.Y >= bounds.Top && point.Y < bounds.Bottom;
}
