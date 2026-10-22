using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>
/// 提供没有默认标题栏、但具备原生缩放、DWM 阴影、Snap、最大化工作区和 DPI 处理能力的窗口基类。
/// 具体标题栏、按钮和视觉由派生类提供；<see cref="ChromeWindow"/> 是基于它实现的默认标题栏窗口。
/// </summary>
public abstract class ChromeFrame : Window, IChromeFrameHost
{
    /// <summary>样式位常量，用于兜底校验（见 <see cref="EnsureNativeStyle"/>）。</summary>
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;

    private protected readonly ChromeInputController _input;
    private protected ChromeFrameController? _frame;

    /// <summary>
    /// 提前于任何实例与 XAML 生效的属性守卫。
    ///
    /// <para>本类保留 <c>WS_CAPTION | WS_THICKFRAME</c>，DWM 的阴影、可见边框、「阴影里那圈」
    /// 不可见缩放带、Snap Layout 和系统菜单全部依赖它；而 <c>WM_NCCALCSIZE</c> 会无条件按
    /// frame 厚度内缩客户区。一旦样式位被摘掉，就会凭空多出一圈没有人绘制的非客户区。</para>
    ///
    /// <para>因此与 frame 争所有权的 WPF 属性在这里被禁用 —— 设置即抛，而不是静默画错。
    /// 完整契约（哪些禁用、哪些放行、为什么）见 <c>docs/frame-property-contract.md</c>。</para>
    ///
    /// <para>必须用 <c>CoerceValueCallback</c>：<c>ValidateValueCallback</c> 在
    /// <c>DependencyProperty.Register</c> 时就固定，<c>OverrideMetadata</c> 无法替换它。
    /// 又因为构造函数跑在 XAML 之前、拦不住 XAML 根元素上的赋值，所以这里才用
    /// <c>OverrideMetadata</c> —— 这是唯一能管住 XAML 的时机。</para>
    /// </summary>
    static ChromeFrame()
    {
        WindowStyleProperty.OverrideMetadata(
            typeof(ChromeFrame),
            new FrameworkPropertyMetadata(
                // OverrideMetadata 的 defaultValue 会**替换**基类默认值，不是占位值。
                // 漏写会让默认值变成 0（即 None），所有窗口默认失去 caption。
                WindowStyle.SingleBorderWindow,
                null,
                CoerceWindowStyle
            )
        );
    }

    /// <summary>
    /// 只放行 <see cref="WindowStyle.SingleBorderWindow"/>；<see cref="WindowStyle.ThreeDBorderWindow"/>
    /// 实测与它没有任何差异，coerce 回单边框；其余一律抛。
    ///
    /// 值没变时 <c>SetValue</c> 直接返回，coerce 不会执行，所以显式写
    /// <c>WindowStyle="SingleBorderWindow"</c> 是安全的。
    /// </summary>
    private static object CoerceWindowStyle(DependencyObject dependencyObject, object baseValue) =>
        baseValue switch
        {
            WindowStyle.SingleBorderWindow => baseValue,
            WindowStyle.ThreeDBorderWindow => WindowStyle.SingleBorderWindow,
            _ => throw new InvalidOperationException(
                "ChromeFrame 需要保留 WS_CAPTION | WS_THICKFRAME 才能提供 DWM 阴影、可见边框、"
                    + "边缘缩放带、Snap Layout 和系统菜单，因此 WindowStyle 只允许 "
                    + $"{nameof(WindowStyle.SingleBorderWindow)}（当前为 {baseValue}）。\n"
                    + "需要「没有标题栏」的窗口请直接继承 ChromeFrame —— 它本来就是这样的窗口骨架；"
                    + "需要圆角请使用 Windows 11 的 DWM 圆角（DWMWA_WINDOW_CORNER_PREFERENCE）；"
                    + "真正无边框/异形窗口本库不支持（会失去 DWM 阴影与缩放带）。"
            ),
        };

    protected ChromeFrame()
    {
        // WindowStyle / AllowsTransparency / ResizeMode 的默认值本来就是对的
        //（SingleBorderWindow / false / CanResize），这里不再重复赋值：
        // 构造函数跑在 XAML 之前，赋值拦不住 XAML，只是给人「设过了」的错觉。
        SnapsToDevicePixels = true;
        _input = new ChromeInputController(this);
        SourceInitialized += OnFrameSourceInitialized;
        StateChanged += OnFrameStateChanged;
        ContentRendered += OnFrameContentRendered;
        Closed += OnFrameClosed;
    }

    internal IntPtr FrameHandle => _frame?.Handle ?? IntPtr.Zero;

    /// <summary>当前窗口是否允许八方向缩放；无标题栏窗口也可直接复用。</summary>
    protected virtual bool IsResizable => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    /// <summary>标题栏按钮占用的宽度（DIP）；frame-only 窗口没有系统按钮时返回 0。</summary>
    protected virtual double CaptionButtonsWidth => 0d;

    /// <summary>自绘标题栏高度（DIP）；frame-only 窗口没有标题栏时返回 0。</summary>
    protected virtual double CaptionHeight => 0d;

    /// <summary>标题栏左侧图标区宽度（DIP）；frame-only 窗口没有图标时返回 0。</summary>
    protected virtual double CaptionLeadingWidth => 0d;

    /// <summary>
    /// 把屏幕坐标映射为语义角色。默认返回 <see cref="ChromeHitTestRole.Default"/>，
    /// 让窗口自身的缩放带（左/右/下 8px、顶部 6px）优先命中；子类可覆写，
    /// 把自定义标题栏标记为 Caption，把可交互控件标记为 Client。
    /// </summary>
    protected virtual ChromeHitTestRole HitTestFrame(Point screenPoint) =>
        ChromeHitTestRole.Default;

    /// <summary>判断指定角色当前是否可用；frame-only 窗口默认没有可执行角色。</summary>
    protected virtual bool IsRoleEnabled(ChromeHitTestRole role) => false;

    /// <summary>在指定标题栏按钮上完成按下和抬起时执行窗口命令。</summary>
    internal virtual bool TryExecuteCaptionButton(
        ChromeHitTestRole pressedRole,
        ChromeHitTestRole releasedRole
    ) => false;

    /// <summary>刷新标题栏 hover/pressed 视觉状态；frame-only 窗口可以为空实现。</summary>
    protected virtual void ApplyVisualState() { }

    /// <summary>DPI 变化后的视觉度量刷新钩子。</summary>
    protected virtual void OnFrameDpiChanged() { }

    /// <summary>
    /// 原生样式位变化后（<c>WM_STYLECHANGED</c>）的钩子。
    /// 默认什么都不做；有自绘标题栏的派生类应在这里重算度量并刷新 frame。
    /// </summary>
    protected virtual void OnFrameNativeStyleChanged() { }

    /// <summary>显示器拓扑或工作区变化通知钩子。</summary>
    protected virtual void OnDisplayConfigurationChanged() { }

    /// <summary>窗口句柄和 frame controller 初始化完成后的扩展点。</summary>
    protected virtual void OnFrameAttached() { }

    /// <summary>窗口状态变化时的扩展点；默认刷新 DWM frame，以处理最大化/还原时的边距切换。</summary>
    protected virtual void OnFrameStateChanged() => _frame?.ScheduleNativeFrameRefresh();

    /// <summary>窗口关闭后的扩展点，派生类可在此退订自己的事件。</summary>
    protected virtual void OnFrameClosed() { }

    /// <summary>DWM frame 更新后请求重新布局；默认对整窗做一次失效和布局。</summary>
    protected virtual void RefreshLayout()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
        UpdateLayout();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs eventArgs)
    {
        base.OnPropertyChanged(eventArgs);
        if (eventArgs.Property == ResizeModeProperty)
            OnFrameResizeModeChanged();
    }

    /// <summary>ResizeMode 变化时的扩展点；客户区缩放带是否生效由 <see cref="IsResizable"/> 决定。</summary>
    protected virtual void OnFrameResizeModeChanged() => _input.PrepareResizeModeChange();

    private void OnFrameSourceInitialized(object? sender, EventArgs eventArgs)
    {
        ApplyTemplate();
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        // 属性层守卫拦不住「附加属性」和「直接改样式位」两条路径，附加前后各兜一次。
        EnsureFrameContract();
        _frame = new ChromeFrameController(this, _input);
        _frame.Attach(source, handle);
        OnFrameDpiChanged();
        _frame.RefreshNativeFrame(notifyClientSize: true);
        OnFrameAttached();
    }

    private void OnFrameStateChanged(object? sender, EventArgs eventArgs) => OnFrameStateChanged();

    private void OnFrameContentRendered(object? sender, EventArgs eventArgs) { }

    /// <summary>
    /// 校验「窗口仍然归我们所有」：没有第二套 frame 实现，且原生样式位没被摘掉。
    ///
    /// <para>属性层守卫（见静态构造函数）只能拦住「通过依赖属性改」的路径。这里兜住两条它管不到的：
    /// <c>WindowChrome</c> 附加属性（别人命名空间上的 DP，无法 OverrideMetadata），
    /// 以及直接 <c>SetWindowLongPtr</c> / 第三方库改写样式位。</para>
    /// </summary>
    private void EnsureFrameContract()
    {
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is not null)
        {
            throw new InvalidOperationException(
                "ChromeFrame 自带完整的原生 frame 实现，不允许同时使用 WindowChrome。"
                    + "WindowChrome 会另外挂一套 WM_NCCALCSIZE / WM_NCHITTEST，"
                    + "与本库的窗口过程争同一批消息，命中结果取决于 hook 顺序。"
            );
        }
        EnsureNativeStyle(GetFrameHandle());
    }

    /// <summary>
    /// 样式位兜底。<c>WS_EX_LAYERED</c> 是唯一实测会让 DWM 停止渲染非客户区的样式位 ——
    /// 一旦它出现，阴影、可见边框、缩放带全部消失，而客户区仍按 frame 厚度内缩，
    /// 那圈就成了没人绘制的空白。
    ///
    /// <para><c>WS_THICKFRAME</c> 缺失**不是**缺陷：<c>ResizeMode=NoResize/CanMinimize</c> 会让
    /// WPF 主动摘掉它，实测 DWM 照样渲染那圈（只是不再可缩放），三种 ResizeMode 的像素完全一致，
    /// 因此这里不校验它，也不该校验。</para>
    /// </summary>
    internal void EnsureNativeStyle(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;
        var exStyle = NativeWindowMethods.GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        if ((exStyle & WsExLayered) == 0)
            return;
        throw new InvalidOperationException(
            "检测到窗口带 WS_EX_LAYERED（通常是 AllowsTransparency=true，或其它代码把窗口改成了分层窗口）。"
                + "分层窗口会让 DWM 停止渲染非客户区：阴影、可见边框和边缘缩放带全部消失，"
                + "而 ChromeFrame 仍按 frame 厚度内缩客户区，留下没人绘制的空白。\n"
                + "需要「没有标题栏」请直接继承 ChromeFrame；需要圆角请用 Windows 11 的 DWM 圆角。"
        );
    }

    /// <summary>窗口关闭后清理。</summary>
    private void OnFrameClosed(object? sender, EventArgs eventArgs)
    {
        _frame?.Detach();
        _frame = null;
        SourceInitialized -= OnFrameSourceInitialized;
        StateChanged -= OnFrameStateChanged;
        ContentRendered -= OnFrameContentRendered;
        Closed -= OnFrameClosed;
        OnFrameClosed();
    }

    /// <summary>已创建的 HWND；尚未创建时返回 <see cref="IntPtr.Zero"/>（不触发句柄创建）。</summary>
    private IntPtr GetFrameHandle() =>
        PresentationSource.FromVisual(this) is HwndSource source ? source.Handle : IntPtr.Zero;

    /// <summary>窗口背景不透明时用窗口背景色兜底；透明背景继续保留玻璃效果。</summary>
    protected virtual Color ResolveCompositionBackgroundColor() =>
        Background is SolidColorBrush { Color.A: 0xFF } brush
            ? brush.Color
            : Colors.Transparent;
    Dispatcher IChromeFrameHost.Dispatcher => Dispatcher;
    IntPtr IChromeFrameHost.FrameHandle => FrameHandle;
    ResizeMode IChromeFrameHost.ResizeMode => ResizeMode;
    bool IChromeFrameHost.IsResizable => IsResizable;
    double IChromeFrameHost.CaptionButtonsWidth => CaptionButtonsWidth;
    double IChromeFrameHost.CaptionHeight => CaptionHeight;
    double IChromeFrameHost.CaptionLeadingWidth => CaptionLeadingWidth;
    Color IChromeFrameHost.CompositionBackgroundColor => ResolveCompositionBackgroundColor();
    ChromeHitTestRole IChromeFrameHost.HitTestFrame(NativePoint point) =>
        HitTestFrame(new Point(point.X, point.Y));
    bool IChromeFrameHost.IsRoleEnabled(ChromeHitTestRole role) => IsRoleEnabled(role);
    bool IChromeFrameHost.TryExecuteCaptionButton(
        ChromeHitTestRole pressedRole,
        ChromeHitTestRole releasedRole
    ) => TryExecuteCaptionButton(pressedRole, releasedRole);
    void IChromeFrameHost.ApplyVisualState() => ApplyVisualState();
    void IChromeFrameHost.OnFrameDpiChanged() => OnFrameDpiChanged();
    void IChromeFrameHost.OnNativeStyleChanged() => OnFrameNativeStyleChanged();
    void IChromeFrameHost.OnDisplayConfigurationChanged() => OnDisplayConfigurationChanged();
    void IChromeFrameHost.RefreshLayout() => RefreshLayout();
}
