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

    /// <summary>
    /// 是否已排队一次“布局完成后按光标重算 hover”。
    /// 窗口状态连续变化（如最大化紧接着还原）时只保留一次重算。
    /// </summary>
    private bool _pointerReevaluationPending;
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

        // 只在“由失活变为激活”的那一次提交后驱动渲染。
        //
        // 激活 → 失活（点别的窗口）不需要：那种场景没有后续的系统等待，渲染自然会提交。
        // 只有失活 → 激活且紧接着进入 SC_MOVE 时才会被挡住，见 FlushCompositionTarget。
        if (!IsActive || wasActive)
            return;
        FlushCompositionTarget();
    }

    /// <summary>
    /// 让 WPF 立刻把已提交的视觉渲染出来，绕过“要等 Dispatcher 轮次”的默认时序。
    ///
    /// <para><b>不要随意调用。</b>它会让 WPF 走一整遍 resize 渲染路径（取消排队的渲染操作、
    /// 同步执行 <c>RenderMessageHandler</c>）。只应在“激活已经发生、紧接着要进入系统内部
    /// 等待”时使用，否则就是多余的重绘。目前唯一调用点是
    /// <see cref="OnActivationChanged"/> 的失活→激活转换。</para>
    ///
    /// <para><b>背景（现象）。</b>窗口失焦后，用鼠标按住标题栏空白处不动，窗口在几毫秒内
    /// 就激活了，标题栏模板的背景属性也已经变成激活色，但屏幕上仍是失活色，大约
    /// 500ms 后才切换。单击、按住后拖动都没有这个问题，只有“按住且不动”才会出现。</para>
    ///
    /// <para><b>原因。</b>在 <c>HTCAPTION</c> 上按下后，<c>DefWindowProc</c> 会为
    /// <c>SC_MOVE</c> 进入系统的模态交互循环。这个循环只派发它认识的消息（鼠标、键盘等），
    /// <b>不服务 WPF 的 Dispatcher 队列</b>。实测：在该循环进行期间投递
    /// <c>DispatcherPriority.Send</c> 的操作，同样要等到 <c>WM_ENTERSIZEMOVE</c> 之后
    /// 才执行——所以 UI 线程并没有死锁，只是 Dispatcher 队列没人处理。</para>
    ///
    /// <para>而 WPF 的画面提交恰好依赖这个队列：<c>MediaContext</c> 把
    /// <c>RenderMessageHandler</c> 作为一个 Dispatcher 操作排在 Render 优先级上，
    /// 等轮次到了才执行；真正把帧交给合成器的 <c>Channel.SyncFlush</c> 是 internal，
    /// 应用代码无法直接调用。于是属性早已正确，帧却一直排队，直到模态循环结束。</para>
    ///
    /// <para><b>为什么这样做。</b>WPF 自己也有一条“同步渲染”的路径：
    /// <c>WM_SIZE</c> 会进入 <c>HwndTarget.OnResize</c> → <c>MediaContext.Resize</c>，
    /// 后者取消排队的渲染操作并<b>同步调用</b> <c>RenderMessageHandler</c>。源码注释原话是
    /// “run the dispatch for the render queue item by hand”。而 <c>OnResize</c> 并不检查
    /// 尺寸是否真的变化，因此用<b>当前</b>客户区尺寸发一条 <c>WM_SIZE</c> 就够了：
    /// 窗口几何完全不变，但渲染被同步提交。</para>
    ///
    /// <para><b>为什么用 <c>SendMessage</c> 而不是 <c>PostMessage</c>。</b>异步投递的消息
    /// 同样要经过消息队列，而模态循环不取它们——实测所有 <c>PostMessage</c>（含
    /// <c>WM_SIZE</c>、<c>WM_PAINT</c>、<c>WM_NULL</c>）都积压到
    /// <c>WM_ENTERSIZEMOVE</c> 之后才被处理。而 <c>SendMessage</c> 是同步调用窗口过程，
    /// 不经过队列，所以能穿透模态循环——实测立即返回。</para>
    ///
    /// <para><b>试过但无效的方案</b>（记录在此，避免后来者重复踩坑）：
    /// <c>DwmFlush</c>（只等 DWM 合成已有内容，不产出新帧）、
    /// <c>SetWindowPos(SWP_FRAMECHANGED)</c>（同样只让 DWM 重新合成旧内容）、
    /// <c>UpdateWindow</c> / <c>RedrawWindow(RDW_UPDATENOW)</c>（WPF 不走 WM_PAINT）、
    /// <c>UpdateLayout</c> / <c>InvalidateVisual</c>（只做布局与标记，不提交）、
    /// 各种 <c>PostMessage</c>。还有一个被否决的方案是接管 <c>HTCAPTION</c> 自己实现拖动——
    /// 那会丢掉 Aero Snap、双击最大化、最大化拖出还原，代价远大于收益。</para>
    ///
    /// <para><b>踩过的坑：<c>wParam</c> 必须如实填写窗口状态。</b>早期版本把 <c>wParam</c>
    /// 写死为 <c>SIZE_RESTORED</c>（0），结果最大化窗口一旦激活就被还原成普通态 ——
    /// 因为 WPF 的 <c>Window</c> 在收到 <c>SIZE_RESTORED</c> 且自身状态不是
    /// <see cref="WindowState.Normal"/> 时，会主动把 <see cref="WindowState"/> 改回
    /// <see cref="WindowState.Normal"/>。所以这里按当前状态填
    /// <c>SIZE_MAXIMIZED</c> / <c>SIZE_RESTORED</c>，最小化则直接跳过。</para>
    ///
    /// <para>本方法不改变窗口几何、不改变激活状态、不改消息顺序，因此系统拖动、双击
    /// 最大化与 Aero Snap 仍走原生路径。</para>
    /// </summary>
    private void FlushCompositionTarget()
    {
        var handle = FrameHandle;
        if (handle == IntPtr.Zero)
            return;

        // 最小化时不刷新：HwndTarget 对 SIZE_MINIMIZED 走的是“不渲染”分支，
        // 而且最小化状态下也看不到标题栏。
        if (WindowState == WindowState.Minimized)
            return;

        if (!NativeWindowMethods.GetClientRect(handle, out var client))
            return;

        // wParam 必须如实反映当前窗口状态，否则 WPF 会把窗口改成错误的状态。
        //
        // Window 的 WM_SIZE 处理里有这样一段（PresentationFramework/Window.cs）：
        //   case SIZE_RESTORED:
        //       if (_previousWindowState != WindowState.Normal) {
        //           if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        //           ...
        //       }
        // 也就是说：最大化时发一条 wParam = SIZE_RESTORED 的 WM_SIZE，窗口会被**还原**。
        // 这正是本方法早期版本的缺陷 —— wParam 写死 0，导致最大化窗口激活时掉回普通态。
        var windowState = WindowState == WindowState.Maximized ? SizeMaximized : SizeRestored;
        _ = NativeWindowMethods.SendMessage(
            handle,
            WmSize,
            new IntPtr(windowState),
            new IntPtr(unchecked((client.Height << 16) | (client.Width & 0xFFFF)))
        );
    }

    private const uint WmSize = 0x0005;

    /// <summary><c>SIZE_RESTORED</c>：普通态。</summary>
    private const int SizeRestored = 0;

    /// <summary><c>SIZE_MAXIMIZED</c>：最大化。</summary>
    private const int SizeMaximized = 2;

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
