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
    internal const string PartTitleBar = "PART_TitleBar";
    internal const string PartSystemMenu = "PART_SystemMenu";
    internal const string PartMinimizeButton = "PART_MinimizeButton";
    internal const string PartMaximizeButton = "PART_MaximizeButton";
    internal const string PartCloseButton = "PART_CloseButton";
    private const uint WmGetIcon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int IdiApplication = 32512;
    private FrameworkElement? _titleBar;
    private FrameworkElement? _systemMenu;
    private FrameworkElement? _minimize;
    private FrameworkElement? _maximize;
    private FrameworkElement? _close;
    private bool _effectiveIconRefreshPending;

    public override void OnApplyTemplate()
    {
        _input.CancelCaptionButtonPress();
        _titleBar = null;
        _systemMenu = null;
        _minimize = null;
        _maximize = null;
        _close = null;
        base.OnApplyTemplate();
        _titleBar = GetTemplateChild(PartTitleBar) as FrameworkElement;
        _systemMenu = GetTemplateChild(PartSystemMenu) as FrameworkElement;
        _minimize = GetTemplateChild(PartMinimizeButton) as FrameworkElement;
        _maximize = GetTemplateChild(PartMaximizeButton) as FrameworkElement;
        _close = GetTemplateChild(PartCloseButton) as FrameworkElement;
        ApplyResizeMode();
        UpdateDpiVisuals();
        ApplyVisualState();
    }

    protected override void ApplyVisualState()
    {
        var hoveredRole = _input.HotRole;
        var pressedOrigin = _input.PressedRole;
        var pressedRole = pressedOrigin == hoveredRole ? pressedOrigin : ChromeHitTestRole.Default;
        SetValue(HoveredChromeRolePropertyKey, hoveredRole);
        SetValue(PressedChromeRolePropertyKey, pressedRole);
        _ = VisualStateManager.GoToState(this, IsActive ? "Active" : "Inactive", true);
        _ = VisualStateManager.GoToState(
            this,
            WindowState switch
            {
                WindowState.Maximized => "Maximized",
                WindowState.Minimized => "Minimized",
                _ => "NormalWindow",
            },
            true
        );
        GoToCaptionButtonState(
            ChromeHitTestRole.MinimizeButton,
            "Minimize",
            hoveredRole,
            pressedRole
        );
        GoToCaptionButtonState(
            ChromeHitTestRole.MaximizeButton,
            "Maximize",
            hoveredRole,
            pressedRole
        );
        GoToCaptionButtonState(ChromeHitTestRole.CloseButton, "Close", hoveredRole, pressedRole);
    }

    private void GoToCaptionButtonState(
        ChromeHitTestRole role,
        string prefix,
        ChromeHitTestRole hoveredRole,
        ChromeHitTestRole pressedRole
    )
    {
        var suffix =
            !IsRoleEnabled(role) ? "Disabled"
            : pressedRole == role ? "Pressed"
            : hoveredRole == role ? "PointerOver"
            : "Normal";
        _ = VisualStateManager.GoToState(this, prefix + suffix, true);
    }

    private void OnActivationChanged(object? sender, EventArgs eventArgs) => ApplyVisualState();

    internal void UpdateDpiVisuals()
    {
        var scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        SetValue(
            TitleBarBorderThicknessPropertyKey,
            new Thickness(0, 0, 0, 1 / (scaleY <= 0 ? 1 : scaleY))
        );
    }

    private void ScheduleEffectiveTitleBarIconRefresh()
    {
        if (Icon is not null || (_frame?.Handle ?? IntPtr.Zero) == IntPtr.Zero)
        {
            RefreshEffectiveTitleBarIcon();
            return;
        }
        // Icon 清空后，WPF 会异步把应用程序图标重新写入 HWND；下一轮再读取才能得到最终结果。
        if (_effectiveIconRefreshPending)
            return;
        _effectiveIconRefreshPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                _effectiveIconRefreshPending = false;
                if (_frame is not null)
                    RefreshEffectiveTitleBarIcon();
            }
        );
    }

    private void RefreshEffectiveTitleBarIcon()
    {
        if (Icon is { } explicitIcon)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, explicitIcon);
            return;
        }
        if ((_frame?.Handle ?? IntPtr.Zero) == IntPtr.Zero)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, null);
            return;
        }
        var iconHandle = NativeWindowMethods.SendMessage(
            _frame!.Handle,
            WmGetIcon,
            new IntPtr(IconSmall2),
            IntPtr.Zero
        );
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.SendMessage(
                _frame!.Handle,
                WmGetIcon,
                new IntPtr(IconSmall),
                IntPtr.Zero
            );
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.SendMessage(
                _frame!.Handle,
                WmGetIcon,
                new IntPtr(IconBig),
                IntPtr.Zero
            );
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
        if (iconHandle == IntPtr.Zero)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, null);
            return;
        }
        // WM_GETICON 和 LoadIcon 返回的句柄均由系统所有，不能调用 DestroyIcon。
        var source = Imaging.CreateBitmapSourceFromHIcon(
            iconHandle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions()
        );
        if (source.CanFreeze)
            source.Freeze();
        SetValue(EffectiveTitleBarIconPropertyKey, source);
    }
}
