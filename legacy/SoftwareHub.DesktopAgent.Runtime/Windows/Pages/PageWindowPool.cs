using SoftwareHub.DesktopAgent;
using System.Windows.Media;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>一个服务页面对应的可复用 WPF/WebView2 窗口池。</summary>
internal sealed class PageWindowPool
{
    public required string ServiceInstanceId { get; init; }

    public required AgentViewDefinition View { get; init; }

    public required AgentClientRegistration Registration { get; init; }

    public required string AccessToken { get; init; }

    public required Uri Uri { get; init; }

    public required ImageSource Icon { get; init; }

    public Queue<DesktopWindow> Idle { get; } = new();

    public int DesiredCapacity => View.PrewarmWindowCount;

    public Task<bool>? Replenishment { get; set; }

    public bool Retired { get; set; }
}
