using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>持有 Agent 进程唯一的 WebView2 环境，并为每个服务会话生成隔离的浏览器配置。</summary>
internal sealed class WebView2EnvironmentProvider : IAsyncDisposable
{
    private readonly RuntimeSettings _settings;
    private string? _userDataRoot;

    public WebView2EnvironmentProvider(RuntimeSettings settings) => _settings = settings;

    public CoreWebView2Environment Environment { get; private set; } = null!;

    public async Task InitializeAsync(CancellationToken token)
    {
        _userDataRoot = Path.Combine(
            Path.GetTempPath(),
            "SoftwareHub",
            "DesktopAgent",
            $"{System.Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_userDataRoot);
        Environment = await CoreWebView2Environment.CreateAsync(
            _settings.WebView2RuntimePath,
            _userDataRoot);
        token.ThrowIfCancellationRequested();
    }

    public CoreWebView2ControllerOptions CreateControllerOptions(string serviceInstanceId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serviceInstanceId)));
        var options = Environment.CreateCoreWebView2ControllerOptions();
        // 保持 profile 路径简短，避免 WebView2 Runtime 在较长用户数据根目录下
        // 创建 WebView2 controller 时触发内部路径限制。摘要仍足以在单 Agent 进程内隔离服务。
        options.ProfileName = $"svc-{hash[..16]}";
        return options;
    }

    public ValueTask DisposeAsync()
    {
        Environment = null!;
        if (_userDataRoot is { } userDataRoot)
        {
            try { Directory.Delete(userDataRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return ValueTask.CompletedTask;
    }
}
