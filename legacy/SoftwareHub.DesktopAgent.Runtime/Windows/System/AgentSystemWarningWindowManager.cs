using System.IO;
using System.Net;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>持有一个独立环境的系统告警窗口，不占用任何服务窗口配额。</summary>
internal sealed class AgentSystemWarningWindowManager : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly RuntimeSettings _settings;
    private CoreWebView2Environment? _environment;
    private AgentSystemWarningWindow? _window;

    public AgentSystemWarningWindowManager(Dispatcher dispatcher, RuntimeSettings settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareHub", "DesktopAgent", _settings.AgentId, "SystemWarning");
        Directory.CreateDirectory(userData);
        _environment = await CoreWebView2Environment.CreateAsync(_settings.WebView2RuntimePath, userData);
        token.ThrowIfCancellationRequested();
    }

    public Task ShowAsync(string message) => _dispatcher.InvokeAsync(async () =>
    {
        if (_environment is null) return;
        if (_window is null)
        {
            _window = new AgentSystemWarningWindow();
            _window.Closed += (_, _) => _window = null;
            _window.Opacity = 0;
            _window.Show();
            try
            {
                // 标准 WebView2 先进入已创建 HWND 的 WPF 视觉树，再创建 windowed controller。
                await _window.Browser.EnsureCoreWebView2Async(_environment);
                await _window.Browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    WebViewPresentationMask.InitializationScript);
                _window.Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _window.Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _window.Browser.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            }
            catch
            {
                _window.Close();
                _window = null;
                throw;
            }
        }

        await _window.NavigateToStringAsync($$"""
            <!doctype html><html lang="zh-CN"><meta charset="utf-8">
            <style>body{font-family:'Microsoft YaHei UI',sans-serif;padding:28px;line-height:1.7}h2{margin-top:0;color:#b42318}</style>
            <body><h2>DesktopAgent 资源已达到上限</h2><p>{{WebUtility.HtmlEncode(message)}}</p></body></html>
            """);
        if (!_window.IsVisible) _window.Show();
        _window.Opacity = 1;
        _window.Activate();
    }).Task.Unwrap();

    public async ValueTask DisposeAsync()
    {
        await _dispatcher.InvokeAsync(() => _window?.Close());
        _window = null;
    }
}
