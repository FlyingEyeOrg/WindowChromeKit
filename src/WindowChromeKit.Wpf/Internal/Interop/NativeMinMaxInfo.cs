using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMinMaxInfo
{
    internal NativePoint Reserved;
    internal NativePoint MaxSize;
    internal NativePoint MaxPosition;
    internal NativePoint MinTrackSize;
    internal NativePoint MaxTrackSize;
}
