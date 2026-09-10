using System.Windows;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>演示直接继承 <see cref="ChromeFrame"/>，不依赖 ChromeWindow 默认标题栏的无标题栏窗口。</summary>
public partial class ChromeFrameWindow : ChromeFrame
{
    public ChromeFrameWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeGlyph();
    }

    protected override ChromeHitTestRole HitTestFrame(Point screenPoint)
    {
        // 按钮留给 WPF 处理，其余顶部区域作为 Caption，交给基类执行原生拖动 / 双击最大化。
        if (IsInside(MinimizeButton, screenPoint)
            || IsInside(MaximizeButton, screenPoint)
            || IsInside(CloseButton, screenPoint))
        {
            return ChromeHitTestRole.Client;
        }

        return IsInside(TitleBar, screenPoint) ? ChromeHitTestRole.Caption : ChromeHitTestRole.Client;
    }

    private static bool IsInside(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        var local = element.PointFromScreen(screenPoint);
        return local.X >= 0
            && local.Y >= 0
            && local.X < element.ActualWidth
            && local.Y < element.ActualHeight;
    }

    private void UpdateMaximizeGlyph()
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void OnMinimizeClicked(object sender, RoutedEventArgs eventArgs) =>
        SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClicked(object sender, RoutedEventArgs eventArgs)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnTestButtonClicked(object sender, RoutedEventArgs eventArgs) =>
        InteractionStatus.Text = "按钮点击正常：" + DateTime.Now.ToString("HH:mm:ss");
}
