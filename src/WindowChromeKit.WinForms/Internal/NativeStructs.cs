using System.Runtime.InteropServices;

namespace WindowChromeKit.WinForms.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRectangle(int left, int top, int right, int bottom)
{
    internal int Left = left;
    internal int Top = top;
    internal int Right = right;
    internal int Bottom = bottom;

    internal readonly int Width => Right - Left;
    internal readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint(int x, int y)
{
    internal int X = x;
    internal int Y = y;
}

/// <summary>WM_NCCALCSIZE（wParam = TRUE）的参数。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeNcCalcSizeParameters
{
    internal NativeRectangle Proposed;
    internal NativeRectangle Window;
    internal NativeRectangle Client;
    internal uint Style;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMargins(int left, int right, int top, int bottom)
{
    internal int Left = left;
    internal int Right = right;
    internal int Top = top;
    internal int Bottom = bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTrackMouseEvent
{
    internal uint Size;
    internal uint Flags;
    internal IntPtr Window;
    internal uint HoverTime;
}
