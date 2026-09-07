using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>长期 Web 应用的 WPF/WebView2 窗口实例。</summary>
internal sealed class BrowserWindow : IAsyncDisposable
{
    private readonly WebView2 _webView = new()
    {
        DefaultBackgroundColor = System.Drawing.Color.White,
        MinWidth = 0,
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Hidden,
    };
    private readonly Grid _presentationRoot = new() { Background = Brushes.White };
    private readonly Border _initializationSurface = CreateInitializationSurface();
    private readonly Window _window = new();
    private readonly DesktopWindowFrame _frame;
    private readonly CoreWebView2Environment _environment;
    private readonly CoreWebView2ControllerOptions _controllerOptions;
    private readonly WebView2InitializationCoordinator _webViewInitialization;
    private readonly Func<BrowserWindowClosingRequest, Task<BrowserWindowClosingAck>> _requestClose;
    private readonly Func<Uri, Task> _openChild;
    private readonly Func<Task> _closed;
    private readonly Func<Action> _releasing;
    private AgentBrowserWindow _projection;
    private HashSet<string> _allowedOrigins;
    private bool _forceClose;
    private bool _closePending;
    private bool _closedOnce;
    private readonly NativeWindowInputGate _inputGate;
    private int _webViewDisposed;
    private CancellationTokenSource? _initializationCancellation;
    private string? _lastRequestedUrl;
    private long _navigationGeneration;

    public BrowserWindow(
        AgentBrowserWindow projection,
        BrowserWindowContext context)
    {
        _projection = projection;
        _window.Icon = context.Icon;
        _environment = context.Environment;
        _controllerOptions = context.ControllerOptions;
        _webViewInitialization = context.WebViewInitialization;
        _requestClose = context.RequestClose;
        _openChild = context.OpenChild;
        _releasing = context.Releasing;
        _closed = context.Closed;
        _allowedOrigins = new HashSet<string>(projection.AllowedOrigins, StringComparer.OrdinalIgnoreCase);
        _inputGate = new NativeWindowInputGate(
            "BrowserWindow",
            () => WindowId,
            () => new WindowInteropHelper(_window).Handle);
        _frame = new DesktopWindowFrame(_window, projection.TitleBar);
        _frame.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        _presentationRoot.Children.Add(_webView);
        _presentationRoot.Children.Add(_initializationSurface);
        _frame.Content = _presentationRoot;
        _window.SourceInitialized += (_, _) => _inputGate.Synchronize("SourceInitialized");
        _window.Closing += OnClosing;
        _window.Closed += (_, _) =>
        {
            _frame.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
            _frame.Dispose();
            DisposeWebViewOnce();
            _ = NotifyClosedOnceAsync();
        };
        ApplyProjection(projection);
    }

    public string WindowId => _projection.WindowId;

    public Window Surface => _window;

    public int ModalReferenceCount => _inputGate.ModalReferenceCount;

    public bool IsAssigned => !_closedOnce;

    public void BlockForModalChild()
    {
        _inputGate.AddModalReference();
    }

    public void ReleaseModalChildBlock()
    {
        _inputGate.RemoveModalReference();
    }

    public async Task InitializeAndShowAsync(CancellationToken token)
    {
        _initializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = _initializationCancellation.Token;
        try
        {
            if (_projection.Center) _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // 先在隐藏状态创建 HWND，让自定义 frame 的 SWP_FRAMECHANGED/WM_NCCALCSIZE
            // 在窗口第一次进入 DWM 可见树之前完成。标准 WebView2 在 WPF Visibility.Hidden
            // 时仍可创建不可见的 windowed controller，不需要用 Window.Opacity 模拟隐藏。
            _ = new WindowInteropHelper(_window).EnsureHandle();
            _window.Show();
            EnsureWithinWorkArea(_projection.Center);
            _window.UpdateLayout();
            await _window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded, token);
            await _window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
            await _webViewInitialization.RunInteractiveAsync(
                () => _webView.EnsureCoreWebView2Async(_environment, _controllerOptions),
                token);
            var core = _webView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.NewWindowRequested += OnNewWindowRequested;
            core.PermissionRequested += (_, eventArgs) => eventArgs.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, eventArgs) => eventArgs.Cancel = true;
            core.NavigationStarting += (_, eventArgs) =>
            {
                if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var target)
                    || !_allowedOrigins.Contains(GetOrigin(target)))
                    eventArgs.Cancel = true;
            };
            await core.AddScriptToExecuteOnDocumentCreatedAsync(WebViewPresentationMask.InitializationScript);
            var windowIdJson = JsonSerializer.Serialize(WindowId);
            await core.AddScriptToExecuteOnDocumentCreatedAsync($$"""
            (() => {
              const send = (operation, value, requestId) => chrome.webview.postMessage({
                type: 'browserWindow', operation, value: value ?? null, requestId: requestId ?? null
              });
              const closeRequests = new Map();
              chrome.webview.addEventListener('message', event => {
                const message = event.data;
                if (message?.type !== 'browserWindowCloseResult') return;
                const resolve = closeRequests.get(message.requestId);
                if (!resolve) return;
                closeRequests.delete(message.requestId);
                resolve(Object.freeze({
                  accepted: message.accepted === true,
                  code: message.code ?? null,
                  message: message.message ?? null
                }));
              });
              const current = globalThis.softwareHubDesktop ?? {};
              Object.defineProperty(globalThis, 'softwareHubDesktop', {
                configurable: true,
                value: Object.freeze({ ...current, window: Object.freeze({
                  windowId: {{windowIdJson}},
                  minimize: () => send('minimize'),
                  maximize: () => send('maximize'),
                  restore: () => send('restore'),
                  close: () => new Promise(resolve => {
                    const requestId = crypto.randomUUID();
                    closeRequests.set(requestId, resolve);
                    send('close', 'Page', requestId);
                  })
                }) })
              });
            })();
            """);
            core.WebMessageReceived += (_, eventArgs) => _ = HandleBridgeMessageAsync(eventArgs.WebMessageAsJson);
            var targetUrl = new Uri(_projection.TargetUrl ?? _projection.Url, UriKind.Absolute);
            var initialNavigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnInitialNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
            {
                if (!IsSameDocument(core.Source, targetUrl)) return;
                initialNavigation.TrySetResult(eventArgs.IsSuccess);
            }
            var initialPresentationCompleted = false;
            core.NavigationCompleted += (_, eventArgs) =>
            {
                if (initialPresentationCompleted && eventArgs.IsSuccess)
                    _ = RevealCompletedNavigationAsync(core);
            };
            core.NavigationCompleted += OnInitialNavigationCompleted;
            try
            {
                _lastRequestedUrl = _projection.Url;
                _webView.Source = new Uri(_lastRequestedUrl);
                if (!await initialNavigation.Task.WaitAsync(TimeSpan.FromSeconds(15), token))
                    throw new InvalidOperationException("DesktopAgent 浏览器窗口首次导航失败。");
            }
            finally
            {
                core.NavigationCompleted -= OnInitialNavigationCompleted;
            }
            // 导航期间 HWND 可能已收到 DPI/工作区调整。只提交当前 WPF 布局，避免用
            // 显示前缓存的物理像素边界反向覆盖窗口并造成标题栏右侧布局越界。
            await RevealCompletedNavigationAsync(core, token);
            initialPresentationCompleted = true;
            _initializationSurface.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Visible;
            _window.UpdateLayout();
            await _window.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
            if (_projection.Focus) _window.Activate();
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (_forceClose)
        {
            // 加载过程中已由服务确认关闭，不把本地主动取消当作初始化失败。
        }
        finally
        {
            _initializationCancellation?.Dispose();
            _initializationCancellation = null;
        }
    }

    public void Update(AgentBrowserWindow projection)
    {
        _projection = projection;
        _allowedOrigins = new HashSet<string>(projection.AllowedOrigins, StringComparer.OrdinalIgnoreCase);
        ApplyProjection(projection);
        EnsureWithinWorkArea();
        if (!string.Equals(_lastRequestedUrl, projection.Url, StringComparison.Ordinal))
        {
            _lastRequestedUrl = projection.Url;
            _webView.Source = new Uri(projection.Url);
        }
    }

    public async Task ExecuteAsync(BrowserWindowCommand command)
    {
        switch (command.Operation)
        {
            case BrowserWindowOperation.Show:
                _window.Show();
                EnsureWithinWorkArea();
                break;
            case BrowserWindowOperation.Hide: _window.Hide(); break;
            case BrowserWindowOperation.Activate:
                _window.Show();
                EnsureWithinWorkArea();
                _window.Activate();
                break;
            case BrowserWindowOperation.Minimize: _window.WindowState = WindowState.Minimized; break;
            case BrowserWindowOperation.Maximize: _window.WindowState = WindowState.Maximized; break;
            case BrowserWindowOperation.Restore:
                _window.WindowState = WindowState.Normal;
                EnsureWithinWorkArea();
                break;
            case BrowserWindowOperation.Reload: _webView.Reload(); break;
            case BrowserWindowOperation.LoadUrl:
                if (Uri.TryCreate(command.Value, UriKind.Absolute, out var target)
                    && _allowedOrigins.Contains(GetOrigin(target)))
                {
                    _lastRequestedUrl = target.ToString();
                    _webView.Source = target;
                }
                else throw new InvalidOperationException("BrowserWindow 导航目标不在允许的 Origin 中。");
                break;
            case BrowserWindowOperation.SetTitle: _window.Title = command.Value ?? string.Empty; break;
            case BrowserWindowOperation.Close: await CloseFromServiceAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    public Task HideAsync()
    {
        _window.Hide();
        return Task.CompletedTask;
    }

    public Task CloseFromServiceAsync()
    {
        CloseCore();
        return Task.CompletedTask;
    }

    private void ApplyProjection(AgentBrowserWindow projection)
    {
        _window.Title = projection.Title ?? string.Empty;
        _frame.ApplyColors(projection.TitleBar);
        if (projection.Width is { } width) _window.Width = width;
        if (projection.Height is { } height) _window.Height = height;
        _window.MinWidth = projection.MinWidth ?? 0;
        _window.MinHeight = projection.MinHeight ?? 0;
        _window.Topmost = projection.Topmost;
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            EnsureWithinWorkArea();
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "DesktopAgentBrowserWindowDisplayRecoveryFailed for {WindowId}",
                WindowId);
        }
    }

    private void EnsureWithinWorkArea(bool center = false)
    {
        if (!_window.IsVisible || _window.WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;

        var target = DesktopMonitorResolver.Resolve(
            _window.Owner,
            center ? null : _window);
        var maximumWidth = Math.Max(1, target.WorkAreaWidthDip);
        var maximumHeight = Math.Max(1, target.WorkAreaHeightDip);
        _window.MinWidth = Math.Clamp(_projection.MinWidth ?? 0, 0, maximumWidth);
        _window.MinHeight = Math.Clamp(_projection.MinHeight ?? 0, 0, maximumHeight);

        var desiredWidth = _projection.Width ?? GetCurrentDimension(_window.Width, _window.ActualWidth, maximumWidth);
        var desiredHeight = _projection.Height ?? GetCurrentDimension(_window.Height, _window.ActualHeight, maximumHeight);
        _window.Width = Math.Clamp(desiredWidth, Math.Max(1, _window.MinWidth), maximumWidth);
        _window.Height = Math.Clamp(desiredHeight, Math.Max(1, _window.MinHeight), maximumHeight);
        _window.UpdateLayout();

        NativeRectangle bounds;
        if (center)
        {
            NativeRectangle? ownerBounds = null;
            var ownerHandle = _window.Owner is null ? IntPtr.Zero : new WindowInteropHelper(_window.Owner).Handle;
            if (ownerHandle != IntPtr.Zero && NativeWindowMethods.GetWindowRect(ownerHandle, out var rectangle))
                ownerBounds = rectangle;
            bounds = DesktopWindowPlacement.Center(target, _window.Width, _window.Height, ownerBounds);
        }
        else
        {
            if (!NativeWindowMethods.GetWindowRect(handle, out var currentBounds))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 DesktopAgent BrowserWindow 位置。");
            bounds = DesktopWindowPlacement.Clamp(target, currentBounds);
        }

        if (!NativeWindowMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height,
                NativeWindowMethods.SwpNoZOrder
                | NativeWindowMethods.SwpNoActivate
                | NativeWindowMethods.SwpNoOwnerZOrder))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法约束 DesktopAgent BrowserWindow 工作区边界。");
        }
    }

    private static double GetCurrentDimension(double requested, double actual, double maximum) =>
        double.IsFinite(requested) && requested > 0
            ? requested
            : double.IsFinite(actual) && actual > 0
                ? actual
                : maximum;

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_forceClose) return;
        eventArgs.Cancel = true;
        _ = RequestCloseAsync("WindowChrome");
    }

    private async Task RequestCloseAsync(string source, string? pageRequestId = null)
    {
        if (_closePending)
        {
            if (pageRequestId is not null)
                PostCloseResult(new BrowserWindowClosingAck(
                    pageRequestId,
                    false,
                    "已有关闭请求正在裁决。",
                    "CLOSE_PENDING"));
            return;
        }
        _closePending = true;
        try
        {
            var requestId = pageRequestId ?? Guid.NewGuid().ToString("N");
            var acknowledgement = await _requestClose(new BrowserWindowClosingRequest(
                requestId,
                WindowId,
                _webView.Source?.ToString() ?? _projection.Url,
                source,
                DateTimeOffset.UtcNow.AddSeconds(15)));
            if (acknowledgement.Accepted)
            {
                CloseCore();
            }
            if (pageRequestId is not null)
                PostCloseResult(acknowledgement);
        }
        catch (Exception)
        {
            if (pageRequestId is not null)
                PostCloseResult(new BrowserWindowClosingAck(
                    pageRequestId,
                    false,
                    "关闭请求暂时无法送达 Web 服务。",
                    "SERVICE_UNAVAILABLE"));
        }
        finally
        {
            _closePending = false;
        }
    }

    private async Task HandleBridgeMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var type) || type.GetString() != "browserWindow") return;
        var operation = root.TryGetProperty("operation", out var operationElement)
            ? operationElement.GetString()
            : null;
        switch (operation)
        {
            case "minimize": _window.WindowState = WindowState.Minimized; break;
            case "maximize": _window.WindowState = WindowState.Maximized; break;
            case "restore": _window.WindowState = WindowState.Normal; break;
            case "close":
                var requestId = root.TryGetProperty("requestId", out var requestIdElement)
                    ? requestIdElement.GetString()
                    : null;
                await RequestCloseAsync("Page", requestId);
                break;
        }
    }

    private void PostCloseResult(BrowserWindowClosingAck acknowledgement) =>
        _webView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "browserWindowCloseResult",
            acknowledgement.RequestId,
            acknowledgement.Accepted,
            acknowledgement.Code,
            acknowledgement.Message,
        }));

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var target)
            && _allowedOrigins.Contains(GetOrigin(target)))
            _ = _openChild(target);
    }

    private async Task RevealCompletedNavigationAsync(
        CoreWebView2 core,
        CancellationToken token = default)
    {
        try
        {
            var generation = Interlocked.Increment(ref _navigationGeneration);
            if (!await WebViewPresentationMask.SetVisibilityAsync(
                    core,
                    visible: false,
                    generation,
                    token))
            {
                Log.Warning(
                    "DesktopAgentBrowserWindowPresentationMaskUnavailable for {WindowId} generation {Generation}",
                    WindowId,
                    generation);
            }
        }
        catch (Exception exception) when (_forceClose || exception is not OperationCanceledException)
        {
            if (!_forceClose)
            {
                Log.Warning(exception, "DesktopAgentBrowserWindowPresentationMaskFailed for {WindowId}", WindowId);
            }
        }
    }

    private async Task NotifyClosedOnceAsync()
    {
        if (_closedOnce) return;
        _closedOnce = true;
        await _closed();
    }

    private static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private static bool IsSameDocument(string source, Uri target)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var current)) return false;
        return Uri.Compare(
            current,
            target,
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static Border CreateInitializationSurface()
    {
        var progress = new ProgressBar
        {
            Width = 180,
            Height = 3,
            IsIndeterminate = true,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var label = new TextBlock
        {
            Text = "正在加载…",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Foreground = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(progress);
        content.Children.Add(label);
        return new Border { Background = Brushes.White, Child = content };
    }

    private void CloseCore()
    {
        if (_closedOnce) return;
        _forceClose = true;
        _initializationCancellation?.Cancel();
        Action restoreForeground = static () => { };
        ModalWindowReleaseTransition.Run(
            () => restoreForeground = _releasing(),
            () =>
            {
                if (_window.IsVisible) _window.Hide();
            },
            () => restoreForeground(),
            () => _window.Owner = null);
        _window.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_closedOnce)
        {
            CloseCore();
            await NotifyClosedOnceAsync();
        }
        DisposeWebViewOnce();
    }

    private void DisposeWebViewOnce()
    {
        if (Interlocked.Exchange(ref _webViewDisposed, 1) == 0)
        {
            _webView.Dispose();
        }
    }
}
