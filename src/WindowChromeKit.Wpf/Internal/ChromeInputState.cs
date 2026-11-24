using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Internal;

/// <summary>
/// 标题栏输入状态的不可变快照。
///
/// 输入控制器只产生这份状态，不读取或操作 WPF 视觉；视觉层也不反向查询输入控制器。
/// </summary>
internal readonly record struct ChromeInputState(
    ChromeHitTestRole HoveredRole,
    ChromeHitTestRole PressedRole)
{
    internal static ChromeInputState Default => new(
        ChromeHitTestRole.Default,
        ChromeHitTestRole.Default);
}
