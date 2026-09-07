using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>独立于业务窗口池的 WebView2 全局资源告警窗口。</summary>
internal sealed class AgentSystemWarningWindow : Window, IDisposable
{
    private readonly DesktopWindowFrame _frame;
    private int _disposed;

    public AgentSystemWarningWindow()
    {
        Title = "SoftwareHub DesktopAgent";
        Icon = DesktopWindowIconResources.Default;
        Width = 520;
        Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        ShowActivated = false;
        Topmost = true;
        Browser = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.White,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _frame = new DesktopWindowFrame(this);
        _frame.Content = Browser;
        Closed += (_, _) => Dispose();
    }

    public WebView2 Browser { get; }

    internal async Task NavigateToStringAsync(string html, CancellationToken token = default)
    {
        Browser.CoreWebView2.PostWebMessageAsJson(
            WebViewPresentationMask.CreateCommand(visible: true, generation: 1));
        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs) =>
            navigation.TrySetResult(eventArgs.IsSuccess);
        Browser.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        try
        {
            Browser.NavigateToString(html);
            if (!await navigation.Task.WaitAsync(TimeSpan.FromSeconds(15), token))
                throw new InvalidOperationException("DesktopAgent 系统告警页面导航失败。");
            if (!await WebViewPresentationMask.SetVisibilityAsync(
                    Browser.CoreWebView2,
                    visible: false,
                    generation: 1,
                    token))
            {
                throw new InvalidOperationException("DesktopAgent 系统告警页面加载遮罩无法关闭。");
            }
        }
        finally
        {
            Browser.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Browser.Dispose();
        _frame.Dispose();
    }
}
