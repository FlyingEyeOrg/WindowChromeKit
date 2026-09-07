using System.Windows.Threading;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>串行化 WPF WebView2 Controller 初始化，避免多个可见窗口同时进入原生初始化。</summary>
internal sealed class WebView2InitializationCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WebView2InitializationCoordinator(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public async Task RunInteractiveAsync(Func<Task> initialize, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            // Semaphore 的异步等待可能在线程池恢复；所有 WPF/WebView2 调用都必须回到 UI Dispatcher。
            await _dispatcher.InvokeAsync(initialize).Task.Unwrap();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
