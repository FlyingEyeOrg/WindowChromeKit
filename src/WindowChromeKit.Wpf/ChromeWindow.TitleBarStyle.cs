using System.Windows;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>
/// 标题栏预置样式的应用逻辑。<see cref="ChromeTitleBarStyle"/> 是一张文档化的表
/// （几何 + 配色），在赋值 <see cref="TitleBarStyle"/> 时应用一次。
/// </summary>
public partial class ChromeWindow
{
    /// <summary>
    /// 标题栏预置样式（默认 <see cref="ChromeTitleBarStyle.Chrome"/>）。
    /// 赋值时把该样式的几何与配色**应用一次**；之后单独修改任何属性都以属性为准，
    /// 样式不会再覆盖回来。
    /// </summary>
    public ChromeTitleBarStyle TitleBarStyle
    {
        get => (ChromeTitleBarStyle)GetValue(TitleBarStyleProperty);
        set => SetValue(TitleBarStyleProperty, value);
    }

    /// <summary>
    /// 标题栏配色来源（默认 <see cref="ChromeTitleBarPalette.Default"/>，即该样式自带的那套）。
    ///
    /// 与 <see cref="TitleBarStyle"/> 正交：样式管几何，本属性管颜色。赋值时只重新套用颜色，
    /// **几何不变**；之后单独修改任何颜色属性都以属性为准。
    /// 两个属性谁后赋值都成立 —— 改样式会按当前配色来源重新上色，改配色来源会按当前样式上色。
    /// </summary>
    public ChromeTitleBarPalette TitleBarPalette
    {
        get => (ChromeTitleBarPalette)GetValue(TitleBarPaletteProperty);
        set => SetValue(TitleBarPaletteProperty, value);
    }

    /// <summary>
    /// 系统菜单（图标）盒子距标题栏左边缘的距离（DIP）。
    /// Chrome / VsCode 样式为 9（图标留在 12px 位），Windows 样式为 0（图标贴左）。
    /// </summary>
    public Thickness CaptionIconBoxMargin
    {
        get => (Thickness)GetValue(CaptionIconBoxMarginProperty);
        set => SetValue(CaptionIconBoxMarginProperty, value);
    }

    /// <summary>
    /// 最小化按钮的实际宽度：<see cref="MinimizeButtonWidth"/> 为 0 时回落到统一宽度
    /// （Chrome 实测三个按钮不等宽，最小化是 45、其余 46）。
    /// </summary>
    public double EffectiveMinimizeButtonWidth =>
        MinimizeButtonWidth > 0d ? MinimizeButtonWidth : CaptionButtonWidth;

    /// <summary>
    /// 把预置样式套到标题栏上（几何 + 配色，一次性应用）。
    /// 注意图标盒子的纵向边距比 WinForms / 原生示例少 1：模板里标题栏有一条 1px 顶边线
    /// （TitleBarBorderThickness），内容整体被它顶下去一行。
    /// </summary>
    /// <summary>按钮宽度变化时刷新只读计算属性。</summary>
    private static void OnButtonWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var window = (ChromeWindow)d;
        window.CoerceValue(MinimizeButtonWidthProperty);
    }

    private void ApplyTitleBarStyle(ChromeTitleBarStyle style)
    {
        ApplyTitleBarGeometry(style);
        ApplyTitleBarPalette(style);
    }

    /// <summary>只套几何；配色由 <see cref="ApplyTitleBarPalette"/> 负责。</summary>
    private void ApplyTitleBarGeometry(ChromeTitleBarStyle style)
    {
        switch (style)
        {
            case ChromeTitleBarStyle.VsCode:
                // VS Code：标题栏 35、按钮 46×34，配色固定深色（不跟随系统明暗）
                TitleBarHeight = 35d;
                CaptionButtonWidth = 46d;
                CaptionButtonHeight = 34d;
                MinimizeButtonWidth = 0d;
                CaptionIconBoxMargin = new Thickness(9d, 0d, 0d, 0d);
                break;

            case ChromeTitleBarStyle.Windows:
                // 贴近 Windows 11 原生（96dpi 实测一个原生 WPF Window）：
                // 标题栏可见高 31、按钮 36×22（SM_CXSIZE × SM_CYSIZE）、
                // 图标盒子贴左且顶边在第 8 行（frame 内缩）
                TitleBarHeight = 31d;
                // 视觉格子 45（原生悬停块实测）= SM_CXSIZE(36) + 2×SM_CXPADDEDBORDER(4)
                CaptionButtonWidth = 45d;
                // 铺满整条标题栏：普通态靠模板的 1px 顶边线（BorderThickness）让出第 0 行，
                // 最大化时该线为 0，按钮自然铺到顶，不会在顶部漏出一条底色
                CaptionButtonHeight = 31d;
                MinimizeButtonWidth = 0d;
                // 图标在盒内左对齐并有 3px 内缩，盒子再左移 5 才能让图标落在客户区 8..23（原生实测）
                CaptionIconBoxMargin = new Thickness(5d, 0d, 0d, 0d);
                break;

            default:
                // Chrome 实测：标题栏 40、按钮 46×39、图标 12px 位
                TitleBarHeight = 40d;
                CaptionButtonWidth = 46d;
                // Chrome 实测最小化按钮比其余两个窄 1px（45 / 46 / 46）
                MinimizeButtonWidth = 45d;
                CaptionButtonHeight = 39d;
                CaptionIconBoxMargin = new Thickness(9d, 0d, 0d, 0d);
                break;
        }

        // 顶边线用"半透明基色"模拟 DWM，参数由原生边框实测反解（黑底/白底两组）：
        //   聚焦：黑底 25 / 白底 112 -> 基色 #262626、alpha 66%
        //   失焦：黑底 43 / 白底 170 -> 基色 #565656、alpha 50%
        // 存的是 [alpha + 基色]，绘制时自动与【当前标题栏底色】混合，
        // 因此自定义标题栏配色无需改动这里：
        //   线色 = alpha * 基色 + (1 - alpha) * 标题栏底色
        TitleBarBorderBrush = FrozenBrush(0xA8, 0x26, 0x26, 0x26);
        InactiveTitleBarBorderBrush = FrozenBrush(0x80, 0x56, 0x56, 0x56);
        ShowTitleBarIcon = true;
    }

    /// <summary>
    /// 按当前的 <see cref="TitleBarPalette"/> 把颜色套到标题栏上。
    /// 几何不变，只改颜色 —— 这也是它与 <see cref="TitleBarStyle"/> 分成两个轴的原因。
    ///
    /// 配色**直接按枚举值取**，不看样式：同一套配色配到哪套骨架上都是同样的颜色。
    /// 只有 <see cref="ChromeTitleBarPalette.Default"/> 例外 —— 它的定义就是"该样式自带的那套"，
    /// 所以那一个分支才需要样式参数。
    /// </summary>
    private void ApplyTitleBarPalette(ChromeTitleBarStyle style)
    {
        var look = TitleBarPalette == ChromeTitleBarPalette.Default
            ? SystemTheme.Look(style)
            : ElementPlusTheme.Look(TitleBarPalette);
        var palette = look.Palette;
        ActiveTitleBarBackground = palette.ActiveCaption;
        InactiveTitleBarBackground = palette.InactiveCaption;
        ActiveTitleBarForeground = palette.CaptionText;
        InactiveTitleBarForeground = palette.InactiveCaptionText;
        CaptionButtonHoverBackground = palette.ButtonHover;
        CaptionButtonPressedBackground = palette.ButtonPressed;
        CloseButtonHoverBackground = look.CloseButtonHover;
        CloseButtonPressedBackground = look.CloseButtonPressed;
    }
}
