using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>创建页面窗口所需的页面定义、认证信息和运行时依赖。</summary>
internal sealed class DesktopWindowContext
{
    public required string ServiceInstanceId { get; init; }
    public required AgentViewDefinition View { get; init; }
    public required AgentClientRegistration Registration { get; init; }
    public required string AccessToken { get; init; }
    public required CoreWebView2Environment Environment { get; init; }
    public required CoreWebView2ControllerOptions ControllerOptions { get; init; }
    public required WebView2InitializationCoordinator WebViewInitialization { get; init; }
    public required Uri Uri { get; init; }
    public required ImageSource Icon { get; init; }
    public required Func<WindowResultSubmission, Task<WindowResultAck>> ReportResult { get; init; }
    public required Func<WindowActionSubmission, Task<WindowActionAck>> ReportAction { get; init; }
    public required Func<DesktopWindow, Action> Releasing { get; init; }
    public required Action<DesktopWindow> Released { get; init; }
}
