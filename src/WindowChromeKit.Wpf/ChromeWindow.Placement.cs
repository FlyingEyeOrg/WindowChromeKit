using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    private bool _centerWhenInitialized;
    private bool _constrainWhenInitialized;

    public void CenterOnTargetMonitor()
    {
        if ((_frame?.Handle ?? IntPtr.Zero) == IntPtr.Zero)
        {
            _centerWhenInitialized = true;
            return;
        }
        WindowPlacementService.CenterOnTargetMonitor(this);
    }

    public void ConstrainToWorkArea()
    {
        if ((_frame?.Handle ?? IntPtr.Zero) == IntPtr.Zero)
        {
            _constrainWhenInitialized = true;
            return;
        }
        WindowPlacementService.ConstrainToWorkArea(this);
    }
}
