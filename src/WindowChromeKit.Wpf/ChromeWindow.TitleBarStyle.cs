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

    /// <summary>把预置样式套到标题栏上（几何 + 配色，一次性应用）。</summary>
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
                // 贴近 Windows 11 原生：标题栏 32、按钮 44×32（SM_CXSIZE + 2×SM_CXPADDEDBORDER）、
                // 图标贴左
                palette = SystemTheme.Current;
                TitleBarHeight = 32d;
                CaptionButtonWidth = 44d;
                CaptionButtonHeight = 32d;
                CaptionIconBoxMargin = new Thickness(0d);
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
        // 关闭按钮与顶边线三套样式都沿用系统标准值
        CloseButtonHoverBackground = FrozenBrush(0xFF, 0xE8, 0x11, 0x23);
        CloseButtonPressedBackground = FrozenBrush(0xFF, 0xF1, 0x70, 0x7A);
        // 顶边线：与 DWM 画在其余三边的实测值一致，三套样式统一
        TitleBarBorderBrush = FrozenBrush(0xFF, 0x70, 0x70, 0x70);
        ShowTitleBarIcon = true;
    }
}
