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

    /// <summary>
    /// 图标那一列里系统菜单命中盒子的边长（DIP）：96dpi 下等于 <c>SM_CXSMSIZE</c>（22）。
    ///
    /// 必须与 <c>Themes/Generic.xaml</c> 里 <c>PART_SystemMenu</c> 内层 Border 的
    /// <c>Width</c>/<c>Height</c> 一致 —— 最小窗口宽度按「这个边长 + <c>CaptionIconBoxMargin.Left</c>」
    /// 计算（见 <see cref="CaptionLeadingWidth"/>），两处不一致会把三个按钮挤出客户区右侧。
    /// </summary>
    internal const double SystemMenuBoxSizeDip = 22d;

    private const uint WmGetIcon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int IdiApplication = 32512;
    private FrameworkElement? _titleBar;

    /// <summary>
    /// 上一次处理激活变化时的 <see cref="Window.IsActive"/>。
    /// 用来识别“失活 → 激活”这一种转换，避免每次激活都驱动一次渲染。
    /// </summary>
    private bool _lastKnownActive;
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
        CommitVisualState();
    }

    protected override void ApplyVisualState()
    {
        var input = VisualInputState;
        var hoveredRole = input.HoveredRole;
        var pressedOrigin = input.PressedRole;
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

    private void OnActivationChanged(object? sender, EventArgs eventArgs)
    {
        var wasActive = _lastKnownActive;
        _lastKnownActive = IsActive;
        CommitVisualState();

        // 只在“由失活变为激活”的那一次提交后驱动渲染：
        // 这正是 HTCAPTION 按下导致激活、随后被 SC_MOVE 挡住的场景。
        if (!IsActive || wasActive)
            return;
        FlushRenderDispatcher();
    }

    /// <summary>
    /// 同步驱动一次 WPF 的渲染调度，把已排队的这一帧提交出去。
    ///
    /// <para><b>不要随意调用。</b>它会把 Dispatcher 队列里所有 Render 级工作同步执行完，
    /// 属于“把一帧强行推到屏幕”的手段，只应在原生消息栈内、即将进入系统等待之前
    /// 需要“先呈现再阻塞”时使用。目前唯一调用点是
    /// <see cref="OnActivationChanged"/> 的失活→激活转换。</para>
    ///
    /// <para>WPF 的渲染提交不是“属性一变就上屏”：<c>MediaContext</c> 把
    /// <c>RenderMessageHandler</c> 作为一个 Dispatcher 操作排在 Render 优先级上，
    /// 由 Dispatcher 在之后的轮次里取出执行。真正提交用的 <c>Channel.SyncFlush</c>
    /// 是 internal，应用代码无法直接调用。</para>
    ///
    /// <para>需要它的场景：在 <c>HTCAPTION</c> 上按下后，<c>DefWindowProc</c> 会为
    /// <c>SC_MOVE</c> 进入一段系统内部的等待。那段等待期间 UI 线程一直在这条消息的
    /// 处理栈上，Dispatcher 排不上新的轮次，排队中的渲染就一直不提交 —— 表现为
    /// 窗口已经激活、属性也已经正确，标题栏却仍显示失活色，直到进入移动循环才更新。</para>
    ///
    /// <para>因此这里让 Dispatcher 执行到 Render 优先级，等价于手动驱动一次
    /// <c>RenderMessageHandler</c>。这是 WPF 公开能力下唯一能同步提交一帧的方式：
    /// <c>UpdateLayout</c> 只做布局、<c>InvalidateVisual</c> 只做标记、
    /// <c>UpdateWindow</c> 只触发 WM_PAINT，都不会让合成器提交。</para>
    ///
    /// <para>不泵送输入、不嵌套 <c>DispatcherFrame</c>，因此不会改变系统拖动、
    /// 双击最大化或 Aero Snap 的消息路径。</para>
    /// </summary>
    private void FlushRenderDispatcher() =>
        Dispatcher.Invoke(DispatcherPriority.Render, static () => { });

    internal void UpdateDpiVisuals()
    {
        var scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        // 顶边 1px 边框线：Windows 10 的 DWM 只在非客户区画边框，顶部这一条要自己补，
        // 否则会出现"三条边有框、顶边缺一条"。最大化时客户区正好等于工作区，画了会在
        // 屏幕顶端多出一条线（Chrome 最大化也没有）；Win11 上 DWM 会覆盖同一行，无副作用。
        var topLine = WindowState == WindowState.Maximized ? 0 : 1 / (scaleY <= 0 ? 1 : scaleY);
        SetValue(TitleBarBorderThicknessPropertyKey, new Thickness(0, topLine, 0, 0));
        // 按钮高度与顶边线是一对：普通态按钮让出第 0 行给顶边线，最大化时顶边线为 0、
        // 按钮要长高铺满。放在这里是因为 UpdateDpiVisuals 已经是这四种变化的唯一汇聚点：
        // 模板套用、状态切换、度量变化、DPI 变化。
        UpdateEffectiveCaptionButtonHeight();
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
