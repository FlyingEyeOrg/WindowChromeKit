using System.Runtime.InteropServices;

namespace WindowChromeKit.Wpf.Internal;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeNcCalcSizeParameters
{
    internal NativeRectangle Proposed;
    internal NativeRectangle PreviousWindow;
    internal NativeRectangle PreviousClient;
    internal IntPtr WindowPosition;
}
