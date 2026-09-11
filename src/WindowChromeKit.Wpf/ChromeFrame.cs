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
    private protected readonly ChromeInputController _input;
    private protected ChromeFrameController? _frame;

    protected ChromeFrame()
    {
        AllowsTransparency = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SnapsToDevicePixels = true;
        _input = new ChromeInputController(this);
        SourceInitialized += OnFrameSourceInitialized;
        StateChanged += OnFrameStateChanged;
        ContentRendered += OnFrameContentRendered;
        Closed += OnFrameClosed;
    }

    internal IntPtr FrameHandle => _frame?.Handle ?? IntPtr.Zero;

    internal IntPtr ResizeOverlayHandle => _frame?.ResizeOverlayHandle ?? IntPtr.Zero;

    internal void SynchronizeResizeOverlay() => _frame?.SynchronizeResizeOverlay();

    /// <summary>按当前 <see cref="IsResizable"/> 重新创建或同步外置 resize overlay。</summary>
    protected void RefreshResizeOverlay() => _frame?.UpdateResizeMode(IsResizable);

    /// <summary>当前窗口是否允许八方向缩放；无标题栏窗口也可直接复用。</summary>
    protected virtual bool IsResizable => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    /// <summary>标题栏按钮占用的宽度（DIP）；frame-only 窗口没有系统按钮时返回 0。</summary>
    protected virtual double CaptionButtonsWidth => 0d;

    /// <summary>
    /// 把屏幕坐标映射为语义角色。默认返回 <see cref="ChromeHitTestRole.Default"/>，
    /// 让 resize overlay 能优先命中窗口四边（包括顶部内侧条带）；子类可覆写，
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

    /// <summary>显示器拓扑或工作区变化通知钩子。</summary>
    protected virtual void OnDisplayConfigurationChanged() { }

    /// <summary>窗口句柄和 frame controller 初始化完成后的扩展点；默认初始化 resize overlay。</summary>
    protected virtual void OnFrameAttached() => RefreshResizeOverlay();

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

    /// <summary>ResizeMode 变化时更新 resize overlay；派生类可覆写为完整的视觉/状态刷新。</summary>
    protected virtual void OnFrameResizeModeChanged()
    {
        _input.PrepareResizeModeChange();
        _frame?.UpdateResizeMode(IsResizable);
    }

    private void OnFrameSourceInitialized(object? sender, EventArgs eventArgs)
    {
        ApplyTemplate();
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        _frame = new ChromeFrameController(this, _input);
        _frame.Attach(source, handle);
        OnFrameDpiChanged();
        _frame.RefreshNativeFrame();
        OnFrameAttached();
    }

    private void OnFrameStateChanged(object? sender, EventArgs eventArgs) => OnFrameStateChanged();

    private void OnFrameContentRendered(object? sender, EventArgs eventArgs) =>
        _frame?.SynchronizeResizeOverlay();

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
    void IChromeFrameHost.OnDisplayConfigurationChanged() => OnDisplayConfigurationChanged();
    void IChromeFrameHost.RefreshLayout() => RefreshLayout();
}
