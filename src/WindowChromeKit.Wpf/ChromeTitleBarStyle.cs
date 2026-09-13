namespace WindowChromeKit.Wpf;

/// <summary>
/// 标题栏预置样式。样式是一张**文档化的表**（几何 + 配色），在赋值
/// <see cref="ChromeWindow.TitleBarStyle"/> 时**应用一次**；之后单独修改任何属性都以属性为准。
///
/// 需要完全自定义标题栏时不使用本枚举：换 <see cref="ChromeWindow"/> 的 ControlTemplate，
/// 或用 TitleBarContent / TitleBarActions 插槽填自己的内容。
/// </summary>
public enum ChromeTitleBarStyle
{
    /// <summary>
    /// Chrome 实测几何（标题栏 40、按钮 46×39、图标 22 盒子居中于 12px 位），配色跟随系统明暗。
    /// 默认值。
    /// </summary>
    Chrome,

    /// <summary>
    /// VS Code 风格：标题栏 35、按钮 46×34，配色固定为深色
    /// （底色 #323233、文字 #CCCCCC、悬停 #505050、按下 #5F5F5F，关闭沿用标准红）。
    /// </summary>
    VsCode,

    /// <summary>
    /// 贴近 Windows 11 原生：标题栏 32、按钮 44×32、图标贴左（3px 位），配色跟随系统明暗。
    /// </summary>
    Windows,
}
