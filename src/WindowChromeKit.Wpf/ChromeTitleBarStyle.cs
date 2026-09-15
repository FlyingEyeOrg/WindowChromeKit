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
    /// Chrome 实测几何（标题栏 40、按钮 46×39、图标 22 盒子居中于 12px 位、标题贴左），
    /// 配色跟随系统明暗。默认值。
    /// </summary>
    Chrome,

    /// <summary>
    /// VS Code 风格：标题栏 35、按钮 46×34、标题贴左，配色固定为深色，取自它的主题定义
    /// 2026-dark（底色 #191A1B，激活与失活同色；文字 #8C8C8C；按钮悬停 #2E2F30，
    /// 关闭沿用标准红且按下变深）。这套配色与 Chrome / Windows 样式**相互独立**。
    /// </summary>
    VsCode,

    /// <summary>
    /// 贴近 Windows 11 原生：标题栏 31、按钮 45×31（视觉格子）、图标贴左（5px 盒位让图标落在
    /// 客户区 8px 处）、标题贴左，配色跟随系统明暗。
    /// </summary>
    Windows,
}
