using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>创建 BrowserWindow 所需的稳定运行时依赖，避免位置参数误配。</summary>
internal sealed class BrowserWindowContext
{
    public required ImageSource Icon { get; init; }
    public required CoreWebView2Environment Environment { get; init; }
    public required CoreWebView2ControllerOptions ControllerOptions { get; init; }
    public required WebView2InitializationCoordinator WebViewInitialization { get; init; }
    public required Func<BrowserWindowClosingRequest, Task<BrowserWindowClosingAck>> RequestClose { get; init; }
    public required Func<Uri, Task> OpenChild { get; init; }
    public required Func<Action> Releasing { get; init; }
    public required Func<Task> Closed { get; init; }
}
