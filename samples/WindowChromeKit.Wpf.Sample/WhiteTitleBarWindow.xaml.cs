using System.Windows;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>
/// 白色标题栏（Chrome 浅色配色）示例窗口，用于人工核对原生 frame 模型：
/// 阴影与圆角、阴影里的缩放带、顶边 1 像素线、标题栏度量、最大化行为。
/// </summary>
public partial class WhiteTitleBarWindow : ChromeWindow
{
    public WhiteTitleBarWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText.Text = WindowState == WindowState.Maximized
            ? "窗口状态：最大化（此时客户区应正好等于工作区，顶部没有多余线条）"
            : "窗口状态：普通态（顶部第 0 行应是一条 1 像素边框线）";

    private void OnOpenAnotherClicked(object sender, RoutedEventArgs eventArgs) =>
        new WhiteTitleBarWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        }.Show();

    private void OnToggleMaximizeClicked(object sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
}
