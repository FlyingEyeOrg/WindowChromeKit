using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>
/// 定义原生 frame 控制器和输入控制器需要宿主窗口提供的最小契约。
/// 这样 controller 不再依赖具体的 <see cref="ChromeWindow"/>，也能被未来的
/// <c>ChromeFrame</c> 基类实现。
/// </summary>
internal interface IChromeFrameHost
{
    /// <summary>宿主窗口的 Dispatcher，用于异步刷新 frame 和 DPI 视觉。</summary>
    Dispatcher Dispatcher { get; }

    /// <summary>宿主窗口的原生句柄；尚未初始化时为 <see cref="IntPtr.Zero"/>。</summary>
    IntPtr FrameHandle { get; }

    /// <summary>宿主窗口当前的 <see cref="System.Windows.ResizeMode"/>。</summary>
    ResizeMode ResizeMode { get; }

    /// <summary>宿主窗口当前是否允许八方向缩放。</summary>
    bool IsResizable { get; }

    /// <summary>标题栏按钮占用的宽度（DIP），用于计算最小跟踪宽度。</summary>
    double CaptionButtonsWidth { get; }

    /// <summary>自绘标题栏高度（DIP）；没有标题栏的 frame-only 窗口返回 0。</summary>
    double CaptionHeight { get; }

    /// <summary>标题栏左侧图标区（系统菜单命中区）宽度（DIP）；不显示图标时为 0。</summary>
    double CaptionLeadingWidth { get; }

    /// <summary>未被 WPF 内容覆盖时的合成兜底色。</summary>
    Color CompositionBackgroundColor { get; }

    /// <summary>把屏幕坐标点映射为语义角色；没有标题栏的窗口可以返回 Client。</summary>
    ChromeHitTestRole HitTestFrame(NativePoint point);

    /// <summary>判断指定角色当前是否可用。</summary>
    bool IsRoleEnabled(ChromeHitTestRole role);

    /// <summary>在指定按钮上完成按下和抬起时执行窗口命令。</summary>
    bool TryExecuteCaptionButton(ChromeHitTestRole pressedRole, ChromeHitTestRole releasedRole);

    /// <summary>刷新宿主标题栏的 hover/pressed 视觉状态。</summary>
    void ApplyVisualState();

    /// <summary>DPI 变化后刷新视觉度量。</summary>
    void OnFrameDpiChanged();

    /// <summary>显示器拓扑或工作区变化通知。</summary>
    void OnDisplayConfigurationChanged();

    /// <summary>请求宿主重新布局，通常跟在 DWM frame 更新之后。</summary>
    void RefreshLayout();
}
