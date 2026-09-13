using System.Windows;
using System.Windows.Input;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    private void ApplyResizeMode()
    {
        _input.PrepareResizeModeChange();
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void OnFrameDpiChanged() => UpdateDpiVisuals();

    protected override double CaptionButtonsWidth => CaptionButtonWidth * VisibleCaptionButtonCount;

    protected override void OnFrameAttached()
    {
        // 先刷新输入状态，再刷新图标和延迟居中。
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

    protected override void OnFrameStateChanged()
    {
        UpdateDpiVisuals();
        ApplyVisualState();
        base.OnFrameStateChanged();
        ApplyResizeMode();
    }

    protected override void OnFrameClosed()
    {
        Activated -= OnActivationChanged;
        Deactivated -= OnActivationChanged;
    }

    protected override void OnDisplayConfigurationChanged() =>
        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
}
