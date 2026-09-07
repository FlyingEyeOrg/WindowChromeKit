using System.Runtime.InteropServices;

namespace SoftwareHub.DesktopAgent.Runtime;

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
