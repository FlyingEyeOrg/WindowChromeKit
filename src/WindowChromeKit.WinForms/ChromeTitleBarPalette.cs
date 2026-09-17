namespace WindowChromeKit.WinForms;

/// <summary>
/// 标题栏**配色**。<see cref="ChromeTitleBarStyle"/> 提供骨架（几何）与它自带的那套默认配色，
/// 本枚举提供另外多套可选配色 —— 两者是正交的两轴：
///
/// <code>
/// form.TitleBarStyle   = ChromeTitleBarStyle.VsCode;              // 骨架 + 默认配色
/// form.TitleBarPalette = ChromeTitleBarPalette.ElementPlusDark;    // 换成想要的配色
/// </code>
///
/// 之所以按"配色"而不是"品牌"划分值：这里每一套都是**一套完整的、可直接用的颜色**，
/// 不随 <see cref="ChromeTitleBarStyle"/> 变化。想看全部取值，见 README 的配色对照表。
///
/// 应用顺序：赋值 <see cref="ChromeForm.TitleBarStyle"/> 或本属性都会把颜色**重新套用一次**；
/// 之后单独修改任何颜色属性都以属性为准，不会被覆盖回来。
/// </summary>
public enum ChromeTitleBarPalette
{
    /// <summary>
    /// 该样式自带的那套配色（默认值）：<see cref="ChromeTitleBarStyle.Chrome"/> 与
    /// <see cref="ChromeTitleBarStyle.Windows"/> 跟随系统明暗，
    /// <see cref="ChromeTitleBarStyle.VsCode"/> 固定深色。与各样式原本的行为完全一致。
    /// </summary>
    Default,

    /// <summary>
    /// Element Plus **主色**：品牌主色 <c>#409EFF</c> 直接当标题栏底色，白字。
    /// 最"品牌"的一套，适合要让标题栏与产品主色一致的场景。
    /// </summary>
    ElementPlusPrimary,

    /// <summary>
    /// Element Plus **深色**：取自它的深色主题（底色 <c>#141414</c>、文字 <c>#E5EAF3</c>、
    /// 按钮填充用 dark fill-color）。标题栏本来就不抢眼，适合内容为主的工具类窗口。
    /// </summary>
    ElementPlusDark,

    /// <summary>
    /// Element Plus **中性**：浅色中性底（<c>#FFFFFF</c> / 失活 <c>#F2F6FC</c>）保持克制，
    /// 只在按钮的悬停与按下处露出 primary 色阶。最接近原生观感的一套。
    /// </summary>
    ElementPlusNeutral,
}
