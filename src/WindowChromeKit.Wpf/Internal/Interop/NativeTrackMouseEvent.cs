using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTrackMouseEvent
{
    internal int Size;
    internal uint Flags;
    internal IntPtr WindowHandle;
    internal uint HoverTime;
}
