using System.Runtime.InteropServices;

namespace SoftwareHub.DesktopAgent.Runtime;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeTrackMouseEvent
{
    internal int Size;
    internal uint Flags;
    internal IntPtr WindowHandle;
    internal uint HoverTime;
}
