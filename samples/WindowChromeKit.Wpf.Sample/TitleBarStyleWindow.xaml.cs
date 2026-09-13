using System.Windows;
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

    private void UseStyle(ChromeTitleBarStyle style)
    {
        TitleBarStyle = style;
        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText.Text = $"当前样式：{TitleBarStyle}（标题栏 {TitleBarHeight:0} / 按钮 {CaptionButtonWidth:0}×{CaptionButtonHeight:0}）";
}
