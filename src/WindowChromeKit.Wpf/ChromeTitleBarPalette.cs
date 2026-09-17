namespace WindowChromeKit.Wpf;

/// <summary>
/// 标题栏**配色来源**。与 <see cref="ChromeTitleBarStyle"/> 是两个正交的轴：
/// 样式决定几何（标题栏高、按钮尺寸、图标位置），配色决定用哪套颜色。
///
/// 为什么不把配色做成 <see cref="ChromeTitleBarStyle"/> 的成员（例如
/// <c>ChromeForElementPlus</c>）：那会让两者**相乘** —— 3 种几何 × N 种品牌配色
/// = 3N 个成员，每加一个品牌就要多三个。拆成两轴后是 3 + N，
/// 而且语义也更准：换品牌配色时几何一点没变，它本来就不是另一种"样式"。
///
/// 应用顺序：赋值 <see cref="ChromeWindow.TitleBarStyle"/> 或本属性都会把颜色
/// **重新套用一次**；之后单独修改任何颜色属性都以属性为准，不会被覆盖回来。
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
    /// Element Plus 色板。三套几何样式**各有一款**：Chrome 用品牌主色当底、
    /// VS Code 用它的深色主题、Windows 保持中性底只把主色用在按钮上。
    /// 数值取自 Element Plus 自己的主题变量（<c>theme-chalk</c> 的
    /// <c>common/var.scss</c> 与 <c>dark/var.scss</c>），派生色按它的混色公式算出。
    /// </summary>
    ElementPlus,
}
