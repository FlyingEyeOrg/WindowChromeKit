namespace WindowChromeKit.WinForms;

/// <summary>
/// 标题栏区域的语义角色，与 WPF 版的 <c>ChromeHitTestRole</c> 同名同语义。
/// 通过 <see cref="ChromeFrame.SetHitTestRole"/> 标记标题栏里的自定义控件，
/// 被标记为 <see cref="Client"/> 等可交互角色的控件会优先拿到鼠标，不会被当成拖动区域。
/// </summary>
public enum ChromeHitTestRole
{
    /// <summary>未标记：由窗体按几何判定（标题栏空白处 → 可拖动）。</summary>
    Default,

    /// <summary>可交互内容：控件自己接收鼠标，不参与窗口拖动。</summary>
    Client,

    /// <summary>标题栏空白处：可拖动窗口、双击最大化/还原。</summary>
    Caption,

    /// <summary>系统菜单图标区：左键单击弹出系统菜单。</summary>
    SystemMenu,

    /// <summary>最小化按钮。</summary>
    MinimizeButton,

    /// <summary>最大化/还原按钮。</summary>
    MaximizeButton,

    /// <summary>关闭按钮。</summary>
    CloseButton,
}
