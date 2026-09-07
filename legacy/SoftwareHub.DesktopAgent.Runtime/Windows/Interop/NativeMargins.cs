using System.Runtime.InteropServices;

namespace SoftwareHub.DesktopAgent.Runtime;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMargins(int left, int right, int top, int bottom)
{
    internal int Left = left;
    internal int Right = right;
    internal int Top = top;
    internal int Bottom = bottom;
}
