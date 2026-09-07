using System.Runtime.InteropServices;

namespace SoftwareHub.DesktopAgent.Runtime;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeMonitorInfo
{
    internal int Size;
    internal NativeRectangle Monitor;
    internal NativeRectangle WorkArea;
    internal uint Flags;
}
