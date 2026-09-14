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
    /// 系统菜单（图标）盒子距标题栏左边缘的距离（DIP）。
    /// Chrome / VsCode 样式为 9（图标留在 12px 位），Windows 样式为 0（图标贴左）。
    /// </summary>
    public Thickness CaptionIconBoxMargin
    {
        get => (Thickness)GetValue(CaptionIconBoxMarginProperty);
        set => SetValue(CaptionIconBoxMarginProperty, value);
    }

    /// <summary>
    /// 把预置样式套到标题栏上（几何 + 配色，一次性应用）。
    /// 注意图标盒子的纵向边距比 WinForms / 原生示例少 1：模板里标题栏有一条 1px 顶边线
    /// （TitleBarBorderThickness），内容整体被它顶下去一行。
    /// </summary>
    private void ApplyTitleBarStyle(ChromeTitleBarStyle style)
    {
        ChromePalette palette;
        switch (style)
        {
            case ChromeTitleBarStyle.VsCode:
                // VS Code：标题栏 35、按钮 46×34，配色固定深色
                palette = SystemTheme.Dark;
                TitleBarHeight = 35d;
                CaptionButtonWidth = 46d;
                CaptionButtonHeight = 34d;
                CaptionIconBoxMargin = new Thickness(9d, 0d, 0d, 0d);
                break;

            case ChromeTitleBarStyle.Windows:
                // 贴近 Windows 11 原生（96dpi 实测一个原生 WPF Window）：
                // 标题栏可见高 31、按钮 36×22（SM_CXSIZE × SM_CYSIZE）、
                // 图标盒子贴左且顶边在第 8 行（frame 内缩）
                palette = SystemTheme.Current;
                TitleBarHeight = 31d;
                // 视觉格子 45（原生悬停块实测）= SM_CXSIZE(36) + 2×SM_CXPADDEDBORDER(4)
                CaptionButtonWidth = 45d;
                // 视觉上铺满整条标题栏：原生悬停高亮一直顶到顶边与右上圆角
                CaptionButtonHeight = 31d;
                // 图标在盒内左对齐并有 3px 内缩，盒子再左移 5 才能让图标落在客户区 8..23（原生实测）
                CaptionIconBoxMargin = new Thickness(5d, 0d, 0d, 0d);
                break;

            default:
                // Chrome 实测：标题栏 40、按钮 46×39、图标 12px 位
                palette = SystemTheme.Current;
                TitleBarHeight = 40d;
                CaptionButtonWidth = 46d;
                CaptionButtonHeight = 39d;
                CaptionIconBoxMargin = new Thickness(9d, 0d, 0d, 0d);
                break;
        }

        ActiveTitleBarBackground = palette.ActiveCaption;
        InactiveTitleBarBackground = palette.InactiveCaption;
        ActiveTitleBarForeground = palette.CaptionText;
        InactiveTitleBarForeground = palette.InactiveCaptionText;
        CaptionButtonHoverBackground = palette.ButtonHover;
        CaptionButtonPressedBackground = palette.ButtonPressed;
        // 关闭按钮的红分两套：Windows 样式用原生实测值（悬停 #C42B1C），
        // Chrome / VS Code 用经典的 #E81123
        if (style == ChromeTitleBarStyle.Windows)
        {
            CloseButtonHoverBackground = FrozenBrush(0xFF, 0xC4, 0x2B, 0x1C);
            CloseButtonPressedBackground = FrozenBrush(0xFF, 0xA9, 0x23, 0x16);
        }
        else
        {
            CloseButtonHoverBackground = FrozenBrush(0xFF, 0xE8, 0x11, 0x23);
            CloseButtonPressedBackground = FrozenBrush(0xFF, 0xF1, 0x70, 0x7A);
        }
        // 顶边线：与 DWM 画在其余三边的实测值一致（激活 #707070 / 失活 #AAAAAA），三套样式统一
        TitleBarBorderBrush = FrozenBrush(0xFF, 0x70, 0x70, 0x70);
        InactiveTitleBarBorderBrush = FrozenBrush(0xFF, 0xAA, 0xAA, 0xAA);
        ShowTitleBarIcon = true;
    }
}
