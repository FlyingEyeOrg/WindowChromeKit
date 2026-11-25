using System.Windows;
using System.Windows.Controls;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>
/// 标题栏样式示例：一个窗口里切换 <see cref="ChromeTitleBarStyle"/>，
/// 直观对比 Chrome / VS Code / Windows 三种预置样式。
/// </summary>
public partial class TitleBarStyleWindow
{
    public TitleBarStyleWindow()
    {
        InitializeComponent();
        UpdateStatus();
    }

    private void OnChromeStyleClicked(object sender, RoutedEventArgs eventArgs) =>
        UseStyle(ChromeTitleBarStyle.Chrome);

    private void OnVsCodeStyleClicked(object sender, RoutedEventArgs eventArgs) =>
        UseStyle(ChromeTitleBarStyle.VsCode);

    private void OnWindowsStyleClicked(object sender, RoutedEventArgs eventArgs) =>
        UseStyle(ChromeTitleBarStyle.Windows);

    /// <summary>
    /// 打开一个**完全原生**的 WPF 窗口做对照：不套用本库的模板，标题栏、图标、按钮
    /// 全部由系统绘制。图标刻意与本窗口相同，便于直接比较图标相对标题栏的大小。
    /// </summary>
    private void OnNativeWindowClicked(object sender, RoutedEventArgs eventArgs)
    {
        var native = new Window
        {
            Title = "原生 WPF 窗口（系统标题栏）",
            Icon = Icon,
            Width = Width,
            Height = Height,
            Left = Left + 40,
            Top = Top + 40,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Content = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                Child = new TextBlock
                {
                    Text = "这个窗口使用系统原生标题栏。\n把它的图标本窗口的图标放在一起比较大小。",
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x5F, 0x63, 0x68)),
                },
            },
        };
        native.Show();
    }

    private void UseStyle(ChromeTitleBarStyle style)
    {
        TitleBarStyle = style;
        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText.Text = $"当前样式：{TitleBarStyle}（标题栏 {TitleBarHeight:0} / 按钮 {CaptionButtonWidth:0}×{CaptionButtonHeight:0}）";
}
