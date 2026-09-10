using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    // ChromeWindow 的生命周期编排：把 HwndSource 交给 frame controller，

    // 并把状态变化转发给输入、视觉和布局部分。

    private ChromeFrameController? _frame;

    internal IntPtr FrameHandle => _frame?.Handle ?? IntPtr.Zero;

    internal IntPtr ResizeOverlayHandle => _frame?.ResizeOverlayHandle ?? IntPtr.Zero;

    internal void SynchronizeResizeOverlay() => _frame?.SynchronizeResizeOverlay();

    private void OnChromeSourceInitialized(object? sender, EventArgs eventArgs)
    {
        ApplyTemplate();
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        _frame = new ChromeFrameController(this);
        _frame.Attach(source, handle);
        UpdateDpiVisuals();
        _frame.RefreshNativeFrame();
        ApplyResizeMode();
        RefreshEffectiveTitleBarIcon();
        if (_centerWhenInitialized)
        {
            _centerWhenInitialized = false;
            CenterOnTargetMonitor();
        }
        else if (_constrainWhenInitialized)
        {
            _constrainWhenInitialized = false;
            ConstrainToWorkArea();
        }
    }

    private void ApplyResizeMode()
    {
        PrepareResizeModeChange();
        CommandManager.InvalidateRequerySuggested();
        _frame?.UpdateResizeMode(IsResizable);
    }

    private void OnChromeStateChanged(object? sender, EventArgs eventArgs)
    {
        UpdateDpiVisuals();
        ApplyVisualState();
        _frame?.ScheduleNativeFrameRefresh();
        ApplyResizeMode();
    }

    private void OnChromeContentRendered(object? sender, EventArgs eventArgs) =>
        _frame?.SynchronizeResizeOverlay();

    internal void RefreshLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        UpdateLayout();
    }

    internal void NotifyDisplayConfigurationChanged() =>
        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);

    private void OnChromeClosed(object? sender, EventArgs eventArgs)
    {
        if (_frame is null)
            return;
        CancelCaptionButtonPress();
        _frame.Detach();
        _frame = null;
        SourceInitialized -= OnChromeSourceInitialized;
        Activated -= OnActivationChanged;
        Deactivated -= OnActivationChanged;
        StateChanged -= OnChromeStateChanged;
        ContentRendered -= OnChromeContentRendered;
        Closed -= OnChromeClosed;
    }
}
