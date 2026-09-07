using System.Runtime.InteropServices;

namespace SoftwareHub.DesktopAgent.Runtime;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NativeWindowClass
{
    internal uint Size;
    internal uint Style;
    internal IntPtr WindowProcedure;
    internal int ClassExtraBytes;
    internal int WindowExtraBytes;
    internal IntPtr Instance;
    internal IntPtr Icon;
    internal IntPtr Cursor;
    internal IntPtr Background;
    [MarshalAs(UnmanagedType.LPWStr)] internal string? MenuName;
    [MarshalAs(UnmanagedType.LPWStr)] internal string ClassName;
    internal IntPtr SmallIcon;
}
