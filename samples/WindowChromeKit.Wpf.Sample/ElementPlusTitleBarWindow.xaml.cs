using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>
/// 演示标题栏的两个轴：<see cref="ChromeTitleBarStyle"/>（几何）与
/// <see cref="ChromeTitleBarPalette"/>（配色）。Chrome / VS Code / Windows 三套几何
/// 各配一款 Element Plus 配色，可以在这里逐一切换对比。
///
/// 两者正交，**谁后赋值都成立**；之后单独改颜色属性同样以属性为准。
/// 色板本身在库里（<c>ElementPlusTheme</c>），这里只负责切换与显示。
///
/// 顶边线不用管：它存的是半透明基色，绘制时与标题栏底色混合，换成任何配色都会自动跟随。
/// </summary>
public partial class ElementPlusTitleBarWindow
{
    public ElementPlusTitleBarWindow()
    {
        InitializeComponent();
        UsePalette(ChromeTitleBarStyle.Chrome);
    }

    private void OnChromeClicked(object sender, RoutedEventArgs eventArgs) =>
        UsePalette(ChromeTitleBarStyle.Chrome);

    private void OnVsCodeClicked(object sender, RoutedEventArgs eventArgs) =>
        UsePalette(ChromeTitleBarStyle.VsCode);

    private void OnWindowsClicked(object sender, RoutedEventArgs eventArgs) =>
        UsePalette(ChromeTitleBarStyle.Windows);

    private void UsePalette(ChromeTitleBarStyle style)
    {
        // 两个轴分别赋值：样式管几何、配色管颜色。顺序任意，两者会自行组合。
        TitleBarStyle = style;
        TitleBarPalette = ChromeTitleBarPalette.ElementPlus;

        Title = $"Element Plus 配色 × {style} 样式";
        StatusText.Text = $"当前：{style} 几何 + Element Plus 配色。{Describe(style)}";

        // 色块直接读窗口上实际生效的颜色 —— 不再抄一份配色表，改了库这里自动跟着变
        Swatches.Children.Clear();
        AddSwatch("激活底", ActiveTitleBarBackground);
        AddSwatch("失活底", InactiveTitleBarBackground);
        AddSwatch("悬停填充", CaptionButtonHoverBackground);
        AddSwatch("按下填充", CaptionButtonPressedBackground);
        AddSwatch("关闭悬停", CloseButtonHoverBackground);
        AddSwatch("关闭按下", CloseButtonPressedBackground);
    }

    /// <summary>各款配色的一句话说明（色值由库提供，这里只讲它为什么这么选）。</summary>
    private static string Describe(ChromeTitleBarStyle style) => style switch
    {
        ChromeTitleBarStyle.VsCode => "VS Code 的标题栏本来就深，配 Element Plus 的深色主题（按下比悬停更亮）",
        ChromeTitleBarStyle.Windows => "保持中性底以贴近原生，只在按钮悬停/按下处露出 primary 色阶",
        _ => "直接用品牌主色当标题栏底，最醒目的一款",
    };

    private void AddSwatch(string label, Brush background)
    {
        var color = (background as SolidColorBrush)?.Color ?? Colors.Transparent;
        var text = $"{label} #{color.R:X2}{color.G:X2}{color.B:X2}";
        Swatches.Children.Add(new Border
        {
            Width = 196,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 6),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // 按背景亮度选前景色，保证色块上的文字可读
                Foreground = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 150
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x33))
                    : Brushes.White,
            },
        });
    }
}
