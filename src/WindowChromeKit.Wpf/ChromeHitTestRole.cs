namespace WindowChromeKit.Wpf;

/// <summary>指定 <see cref="ChromeWindow"/> 标题栏元素参与原生命中测试的方式。</summary>
public enum ChromeHitTestRole
{
    /// <summary>未指定特殊角色，由命中位置或模板部件决定。</summary>
    Default,

    /// <summary>保留为 WPF 客户区元素，使控件可以接收输入。</summary>
    Client,

    /// <summary>作为可拖动的窗口标题区域。</summary>
    Caption,

    /// <summary>作为窗口系统菜单区域。</summary>
    SystemMenu,

    /// <summary>作为原生最小化按钮。</summary>
    MinimizeButton,

    /// <summary>作为原生最大化或还原按钮。</summary>
    MaximizeButton,

    /// <summary>作为原生关闭按钮。</summary>
    CloseButton,
}
