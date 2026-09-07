using System.Windows;
using System.Windows.Interop;

namespace SoftwareHub.DesktopAgent.Runtime;

internal sealed record DesktopWindowOwner(
    string WindowKind,
    string WindowId,
    Window Window,
    Action Block,
    Action Release)
{
    public IntPtr Handle => new WindowInteropHelper(Window).Handle;
}
