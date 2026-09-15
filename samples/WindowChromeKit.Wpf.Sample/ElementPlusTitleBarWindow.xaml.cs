using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>
/// 基于预置样式再改配色的示例（WPF 版）：Chrome / VS Code / Windows **三套几何样式各配一款**
/// Element Plus 配色，可以在这里逐一切换对比。
///
/// **顺序很重要**：<c>TitleBarStyle</c> 赋值时会一次性套用整张样式表（几何 + 配色），
/// 所以必须**先选样式、后改颜色**；反过来改的颜色会被样式覆盖回去。
/// 这里统一由 <see cref="ElementPlusPalettes.Apply"/> 处理这个顺序。
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
        var palette = ElementPlusPalettes.Apply(this, style);

        Title = $"Element Plus 配色 × {style} 样式";
        StatusText.Text = $"当前：{style} 几何 + Element Plus 配色。{palette.Caption}";

        Swatches.Children.Clear();
        AddSwatch("激活底", palette.ActiveCaption);
        AddSwatch("失活底", palette.InactiveCaption);
        AddSwatch("悬停填充", palette.ButtonHover);
        AddSwatch("按下填充", palette.ButtonPressed);
        AddSwatch("关闭悬停", palette.CloseButtonHover);
        AddSwatch("关闭按下", palette.CloseButtonPressed);
    }

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
