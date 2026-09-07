using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMargins(int left, int right, int top, int bottom)
{
    internal int Left = left;
    internal int Right = right;
    internal int Top = top;
    internal int Bottom = bottom;
}
