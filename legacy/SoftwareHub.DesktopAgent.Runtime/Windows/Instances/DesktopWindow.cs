using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Serilog;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>承载可复用的服务桌面交互页面及其结果回传。</summary>
internal sealed class DesktopWindow : Window, IDisposable
{
    private const double StagingScreenMargin = 128;
    private const double LayoutTolerance = 1;
    private static readonly JsonSerializerOptions BridgeJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebView2 _webView = new()
    {
        DefaultBackgroundColor = System.Drawing.Color.White,
        MinWidth = 0,
        MinHeight = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly DesktopWindowFrame _frame;
    private readonly AgentClientRegistration _registration;
    private readonly string _accessToken;
    private readonly CoreWebView2Environment _environment;
    private readonly CoreWebView2ControllerOptions _controllerOptions;
    private readonly WebView2InitializationCoordinator _webViewInitialization;
    private readonly Uri _uri;
    private readonly Func<WindowResultSubmission, Task<WindowResultAck>> _reportResult;
    private readonly Func<WindowActionSubmission, Task<WindowActionAck>> _reportAction;
    private readonly Func<DesktopWindow, Action> _releasing;
    private readonly Action<DesktopWindow> _released;
    private readonly NativeWindowInputGate _inputGate;
    private readonly Dictionary<PresentationSizeKey, double> _standardHeightCache = [];
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AgentWindow? _request;
    private AgentWindow? _pendingRequest;
    private CancellationTokenSource? _presentationCancellation;
    private long _presentationGeneration;
    private TaskCompletionSource<RenderedPageMetrics>? _loaded;
    private TaskCompletionSource<RenderedPageMetrics>? _stabilized;
    private TaskCompletionSource<RenderedPageMetrics>? _viewportVerified;
    private double _preferredWidth = 640;
    private double _preferredHeight = 480;
    private bool _initialized;
    private bool _initializingForPresentation;
    private bool _allowClose;
    private bool _presenting;
    private bool _releasePending;
    private bool _presentationFailed;
    private bool _closeAcceptedDuringPresentation;
    private bool _disposed;

    public DesktopWindow(DesktopWindowContext context)
    {
        ServiceInstanceId = context.ServiceInstanceId;
        ViewName = context.View.Name;
        ViewVersion = context.View.Version;
        _registration = context.Registration;
        _accessToken = context.AccessToken;
        _environment = context.Environment;
        _controllerOptions = context.ControllerOptions;
        _webViewInitialization = context.WebViewInitialization;
        _uri = context.Uri;
        _reportResult = context.ReportResult;
        _reportAction = context.ReportAction;
        _releasing = context.Releasing;
        _released = context.Released;
        _inputGate = new NativeWindowInputGate(
            "PageWindow",
            () => WindowId,
            () => new WindowInteropHelper(this).Handle);
        Title = context.View.Name;
        Icon = context.Icon;
        Width = 640;
        Height = 480;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _frame = new DesktopWindowFrame(this, context.View.TitleBar);
        _frame.Content = _webView;
        _frame.DisplayConfigurationChanged += OnDisplayConfigurationChanged;
        SourceInitialized += (_, _) => _inputGate.Synchronize("SourceInitialized");
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _disposed = true;
            DisposePresentationCancellation();
            _frame.DisplayConfigurationChanged -= OnDisplayConfigurationChanged;
            _frame.Dispose();
            _webView.Dispose();
        };
    }

    public string ServiceInstanceId { get; }

    public string ViewName { get; }

    public int ViewVersion { get; }

    public string? WindowId => (_request ?? _pendingRequest)?.WindowId;

    public string? ReleasedWindowId { get; private set; }

    public bool IsAssigned => _request is not null;

    public bool IsModalBlocked => _inputGate.IsModalBlocked;

    public int ModalReferenceCount => _inputGate.ModalReferenceCount;

    public bool CanReuse => !_presentationFailed && !_disposed;

    public bool CloseAcceptedDuringPresentation => _closeAcceptedDuringPresentation;

    public void BlockForModalChild()
    {
        _inputGate.AddModalReference();
    }

    public void ReleaseModalChildBlock()
    {
        _inputGate.RemoveModalReference();
    }

    /// <summary>创建 HWND、初始化 WebView2，并等待页面主动声明已可交互。</summary>
    public async Task<bool> PrepareAsync(AgentWindow initialRequest, Window? owner, CancellationToken token)
    {
        if (_initialized)
        {
            return await WaitForPresentationPhaseAsync(_ready.Task, "ready", token);
        }

        _initialized = true;
        _initializingForPresentation = true;
        _pendingRequest = initialRequest;
        BeginPresentationCancellation(token);
        token = _presentationCancellation!.Token;
        try
        {
            // 标准 WebView2 必须进入已创建 HWND 的 WPF 视觉树后再创建 controller。
            // 首次请求先显示可关闭的加载壳，确保 windowed controller 在有效显示区域初始化。
            var initializationTarget = ResolveTargetMonitor(owner);
            ApplyPresentationShell(initialRequest, owner, initializationTarget);

            Opacity = 1;
            var handle = new WindowInteropHelper(this).EnsureHandle();
            ApplyNativeBounds(handle, CalculateBusinessBounds(initializationTarget, owner));
            Show();
            UpdateLayout();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded, token);
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
            initializationTarget = ResolveWpfDpi(initializationTarget);
            ApplyPresentationShell(initialRequest, owner, initializationTarget);
            ApplyNativeBounds(handle, CalculateBusinessBounds(initializationTarget, owner));
            UpdateLayout();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);

            await _webViewInitialization.RunInteractiveAsync(
                () => _webView.EnsureCoreWebView2Async(_environment, _controllerOptions),
                token);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            _webView.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            _webView.CoreWebView2.PermissionRequested += (_, e) =>
                e.State = CoreWebView2PermissionState.Deny;
            _webView.CoreWebView2.AddWebResourceRequestedFilter(
                new Uri(new Uri(_registration.Endpoint), "/*").ToString(),
                CoreWebView2WebResourceContext.All);
            _webView.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                e.Request.Headers.SetHeader("Authorization", $"Bearer {_accessToken}");
            };
            _webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!SameOrigin(_uri, new Uri(e.Uri))) e.Cancel = true;
            };
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                WebViewPresentationMask.InitializationScript);
            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("""
                (() => {
                  let loadHandler;
                  let loadGeneration = 0;
                  const pendingInteractions = new Map();
                  const send = (type, value) => chrome.webview.postMessage({ type, value });
                  const newRequestId = () => globalThis.crypto?.randomUUID?.()
                    ?? `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
                  const requestInteraction = (type, payload, source, requestedId) => {
                    const requestId = requestedId || newRequestId();
                    return new Promise(resolve => {
                      pendingInteractions.set(requestId, resolve);
                      send(type, { requestId, payload: payload ?? null, source: source ?? null });
                    });
                  };
                  const deliverInteractionResult = value => {
                    const resolve = pendingInteractions.get(value?.requestId);
                    if (resolve) {
                      pendingInteractions.delete(value.requestId);
                      resolve(value);
                    }
                    dispatchEvent(new CustomEvent('softwarehub:interaction-result', {
                      detail: Object.freeze(value ?? {})
                    }));
                  };
                  const nextFrame = () => new Promise(resolve => requestAnimationFrame(resolve));
                  const measure = () => {
                    const root = document.documentElement;
                    const body = document.body;
                    return {
                      viewportWidth: window.innerWidth,
                      viewportHeight: window.innerHeight,
                      contentWidth: Math.max(root?.scrollWidth ?? 0, body?.scrollWidth ?? 0),
                      contentHeight: Math.max(root?.scrollHeight ?? 0, body?.scrollHeight ?? 0)
                    };
                  };
                  const waitForStableLayout = async (requiredStableFrames = 2) => {
                    let resizeVersion = 0;
                    const observer = new ResizeObserver(() => resizeVersion++);
                    if (document.documentElement) observer.observe(document.documentElement);
                    if (document.body) observer.observe(document.body);
                    let previous;
                    let previousResizeVersion = -1;
                    let stableFrames = 0;
                    try {
                      for (let frame = 0; frame < 12; frame++) {
                        await nextFrame();
                        const current = measure();
                        if (previous
                            && resizeVersion === previousResizeVersion
                            && current.viewportWidth === previous.viewportWidth
                            && current.viewportHeight === previous.viewportHeight
                            && current.contentWidth === previous.contentWidth
                            && current.contentHeight === previous.contentHeight) {
                          stableFrames++;
                          if (stableFrames >= requiredStableFrames) return current;
                        } else {
                          stableFrames = 0;
                        }
                        previous = current;
                        previousResizeVersion = resizeVersion;
                      }
                      return measure();
                    } finally {
                      observer.disconnect();
                    }
                  };
                  Object.defineProperty(window, 'softwareHubDesktop', { value: Object.freeze({
                    version: 2,
                    ready: preferences => send('ready', preferences),
                    onLoad: callback => { loadHandler = callback; },
                    complete: value => requestInteraction('complete', value),
                    close: value => requestInteraction('close', value, 'Page'),
                    invoke: value => requestInteraction('action', value, null, value?.requestId),
                    resetReady: () => send('resetReady')
                  })});
                  addEventListener('keydown', event => {
                    if (event.key !== 'Escape' || event.defaultPrevented) return;
                    event.preventDefault();
                    void requestInteraction('close', { action: 'escape' }, 'Escape');
                  }, true);
                  chrome.webview.addEventListener('message', async event => {
                    if (event.data?.type === 'interactionResult') {
                      deliverInteractionResult(event.data.value);
                      return;
                    }
                    if (event.data?.type === 'stabilize') {
                      send('stabilized', await waitForStableLayout());
                      return;
                    }
                    if (event.data?.type === 'verifyViewport') {
                      await nextFrame();
                      send('viewportVerified', measure());
                      return;
                    }
                    if (event.data?.type !== 'load' || !loadHandler) return;
                    const generation = ++loadGeneration;
                    dispatchEvent(new Event('softwarehub:reset'));
                    await loadHandler(event.data.data, Object.freeze(event.data.context ?? {}));
                    if (document.fonts?.ready) await document.fonts.ready;
                    const metrics = await waitForStableLayout(event.data.settleFrames ?? 2);
                    if (generation !== loadGeneration) return;
                    send('loaded', metrics);
                  });
                })();
                """);
            _webView.CoreWebView2.WebMessageReceived += (_, e) => _ = HandleMessageAsync(e.WebMessageAsJson);
            _webView.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) _ready.TrySetResult(false);
            };
            _webView.Source = _uri;

            var ready = await WaitForPresentationPhaseAsync(_ready.Task, "ready", token);
            return ready;
        }
        catch (OperationCanceledException) when (_closeAcceptedDuringPresentation)
        {
            _presentationFailed = true;
            _pendingRequest = null;
            return false;
        }
        catch
        {
            _presentationFailed = true;
            throw;
        }
        finally
        {
            _initializingForPresentation = false;
        }
    }

    /// <summary>把新的业务请求绑定到已预热页面并立即展示。</summary>
    public async Task ShowRequestAsync(AgentWindow request, Window? owner, CancellationToken token)
    {
        if (!_ready.Task.IsCompletedSuccessfully || !_ready.Task.Result)
        {
            throw new InvalidOperationException("桌面页面尚未准备完成。");
        }

        if (_request is not null)
        {
            throw new InvalidOperationException("桌面窗口仍在处理上一条业务请求。");
        }

        _pendingRequest = request;
        if (_presentationCancellation is null)
        {
            BeginPresentationCancellation(token);
        }
        token = _presentationCancellation!.Token;
        _request = request;
        _pendingRequest = null;
        _presenting = true;
        _releasePending = false;
        _loaded = NewMetricsSource();
        _inputGate.SetResultPending(false);
        try
        {
            var target = ResolveTargetMonitor(owner);
            var preferredWidth = request.Width ?? _preferredWidth;
            var preferredHeight = request.Height ?? _preferredHeight;
            ApplyPresentationShell(request, owner, target, preferredWidth, preferredHeight);
            // 复用窗口此时仍位于虚拟屏幕外。任何 ExecuteScriptAsync/渲染回执等待都可能被
            // Chromium 遮挡节流；这里只按序投递遮罩消息，再把同一个 HWND 移回屏幕。
            QueuePresentationMask();
            _inputGate.Synchronize("ShowRequest");

            var presentationTimer = Stopwatch.StartNew();
            var handle = new WindowInteropHelper(this).EnsureHandle();
            // 同一个 HWND 直接进入目标显示器；移动和 DPI 收敛期间由网页内加载层遮盖页面。
            ApplyNativeBounds(handle, CalculateBusinessBounds(target, owner));
            UpdateLayout();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
            target = ResolveWpfDpi(target);
            var sizeKey = new PresentationSizeKey(
                target.EffectiveDpiX,
                target.EffectiveDpiY,
                (int)Math.Round(preferredWidth),
                (int)Math.Round(preferredHeight));
            if (IsStandardPage && _standardHeightCache.TryGetValue(sizeKey, out var cachedHeight))
            {
                preferredHeight = Math.Max(preferredHeight, cachedHeight);
            }
            ApplyBusinessSize(preferredWidth, preferredHeight, target);
            ApplyNativeBounds(handle, CalculateBusinessBounds(target, owner));
            UpdateLayout();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);

            var expectedViewport = MeasureWebViewViewport();

            _webView.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new
                {
                    type = "load",
                    settleFrames = IsStandardPage ? 1 : 2,
                    data = request.Data,
                    context = new { windowId = request.WindowId, revision = request.Revision },
                }));
            var metrics = await WaitForPresentationPhaseAsync(_loaded.Task, "loaded", token);
            var dataRenderedAt = presentationTimer.ElapsedMilliseconds;
            for (var attempt = 0;
                 IsStandardPage
                 && attempt < 3
                 && metrics.ContentHeight > metrics.ViewportHeight + LayoutTolerance;
                 attempt++)
            {
                var previousViewportHeight = metrics.ViewportHeight;
                expectedViewport = GrowStandardWindowForContent(handle, target, owner, metrics.ContentHeight);
                if (expectedViewport.ViewportHeight <= previousViewportHeight + LayoutTolerance) break;
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
                metrics = await StabilizeAsync(token);
            }
            if (IsStandardPage)
            {
                _standardHeightCache[sizeKey] = Height;
            }

            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render, token);
            var attachedMetrics = await VerifyViewportAsync(token);
            var usedStabilityFallback = !ViewportMatches(expectedViewport, attachedMetrics);
            if (usedStabilityFallback)
            {
                attachedMetrics = await StabilizeAsync(token);
            }
            EnsureViewportUnchanged(expectedViewport, attachedMetrics);

            // 同一个 WebView2 已在目标 HWND 和最终 WPF DPI 下完成渲染；揭示后不再调整窗口。
            if (!await HidePresentationMaskAsync(token))
            {
                throw new InvalidOperationException("DesktopAgent 页面加载遮罩无法关闭。");
            }
            Log.Information(
                "DesktopAgentWindowPresented for {ServiceInstanceId} {WindowId}: data {DataRenderedMs} ms, total {TotalMs} ms, stability fallback {UsedStabilityFallback}",
                ServiceInstanceId,
                WindowId,
                dataRenderedAt,
                presentationTimer.ElapsedMilliseconds,
                usedStabilityFallback);
            if (request.Focus)
            {
                Activate();
            }
        }
        catch (OperationCanceledException) when (_closeAcceptedDuringPresentation)
        {
            // 服务已经确认窗口关闭；finally 会把已分配实例正常归还窗口池。
        }
        catch
        {
            _presentationFailed = true;
            AbortRequestPresentation();
            throw;
        }
        finally
        {
            _presenting = false;
            if (_releasePending)
            {
                _releasePending = false;
                ReleaseFromService();
            }
        }
    }

    /// <summary>按更高 Revision 原位刷新已展示窗口的数据和外观。</summary>
    public async Task UpdateRequestAsync(AgentWindow request, CancellationToken token)
    {
        if (_request is null || !string.Equals(_request.WindowId, request.WindowId, StringComparison.Ordinal))
        {
            return;
        }

        _request = request;
        Title = request.Title ?? request.ViewName;
        _frame.ApplyColors(request.TitleBar);
        ApplyBusinessSize(request.Width, request.Height, ResolveWpfDpi(ResolveTargetMonitor(this)));
        Topmost = request.Topmost;
        _loaded = NewMetricsSource();
        _webView.CoreWebView2.PostWebMessageAsJson(
            JsonSerializer.Serialize(new
            {
                type = "load",
                data = request.Data,
                context = new { windowId = request.WindowId, revision = request.Revision },
            }));
        await WaitForPresentationPhaseAsync(_loaded.Task, "loaded", token);
        if (request.Focus)
        {
            Activate();
        }
    }

    private async Task HandleMessageAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement)) return;
        var type = typeElement.GetString();
        if (type == "ready")
        {
            ApplyPreferences(root);
            _ready.TrySetResult(true);
            return;
        }

        if (type == "loaded")
        {
            if (TryReadMetrics(root, out var metrics)) _loaded?.TrySetResult(metrics);
            return;
        }

        if (type == "stabilized")
        {
            if (TryReadMetrics(root, out var metrics)) _stabilized?.TrySetResult(metrics);
            return;
        }

        if (type == "viewportVerified")
        {
            if (TryReadMetrics(root, out var metrics)) _viewportVerified?.TrySetResult(metrics);
            return;
        }

        if (type is not ("complete" or "close" or "action") || _request is null || _inputGate.IsResultPending) return;
        _inputGate.SetResultPending(true);
        var interactionRequest = _request;
        var interactionWindowId = interactionRequest.WindowId;
        Log.Information(
            "DesktopAgentWindowInteractionReceived for {ServiceInstanceId} {WindowId}: {InteractionType}",
            ServiceInstanceId,
            interactionWindowId,
            type);

        var envelope = root.TryGetProperty("value", out var envelopeElement)
            && envelopeElement.ValueKind == JsonValueKind.Object
                ? envelopeElement
                : default;
        var requestId = envelope.ValueKind == JsonValueKind.Object
            && envelope.TryGetProperty("requestId", out var requestIdElement)
            ? requestIdElement.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
        var payload = envelope.ValueKind == JsonValueKind.Object
            && envelope.TryGetProperty("payload", out var payloadElement)
            ? payloadElement
            : default;

        if (type == "action")
        {
            await HandleActionAsync(requestId, payload);
            return;
        }

        string? action = type == "close" ? "reject" : null;
        JsonElement? result = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("action", out var actionElement)) action = actionElement.GetString();
            if (type == "complete" && payload.TryGetProperty("result", out var resultElement))
                result = resultElement.Clone();
        }

        var source = type == "close" && envelope.ValueKind == JsonValueKind.Object
            && envelope.TryGetProperty("source", out var sourceElement)
            ? sourceElement.GetString()
            : null;

        WindowResultAck acknowledgement;
        try
        {
            acknowledgement = await _reportResult(new WindowResultSubmission(
                requestId,
                interactionRequest.WindowId,
                interactionRequest.Revision,
                DateTimeOffset.UtcNow.AddSeconds(15),
                type == "complete" ? "completed" : "closed",
                action,
                result,
                source));
            Log.Information(
                "DesktopAgentWindowInteractionAcknowledged for {ServiceInstanceId} {WindowId}: {AcknowledgementStatus} {WindowState}",
                ServiceInstanceId,
                interactionWindowId,
                acknowledgement.Status,
                acknowledgement.State);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "DesktopAgentWindowInteractionFailed for {ServiceInstanceId} {WindowId}",
                ServiceInstanceId, WindowId);
            acknowledgement = new WindowResultAck(
                requestId,
                "handlerFailed",
                "pending",
                _request?.Revision ?? 0,
                "窗口交互提交失败。",
                "transport_failed");
            await PostInteractionResultAsync(type, requestId, acknowledgement);
            _inputGate.SetResultPending(false);
            return;
        }
        await PostInteractionResultAsync(type, requestId, acknowledgement);
        if (IsTerminalAcknowledgement(acknowledgement))
        {
            await ReleaseFromServiceOnDispatcherAsync();
            return;
        }

        _inputGate.SetResultPending(false);
    }

    private async Task HandleActionAsync(string requestId, JsonElement payload)
    {
        WindowActionAck acknowledgement;
        try
        {
            var action = payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("action", out var actionElement)
                ? actionElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(action))
            {
                acknowledgement = new WindowActionAck(
                    requestId,
                    false,
                    null,
                    "窗口动作名称不能为空。",
                    "invalid_action");
            }
            else
            {
                JsonElement? data = payload.TryGetProperty("data", out var dataElement)
                    ? dataElement.Clone()
                    : null;
                acknowledgement = await _reportAction(new WindowActionSubmission(
                    requestId,
                    _request!.WindowId,
                    _request.Revision,
                    action,
                    data));
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "DesktopAgentWindowActionFailed for {ServiceInstanceId} {WindowId}",
                ServiceInstanceId, WindowId);
            acknowledgement = new WindowActionAck(
                requestId,
                false,
                null,
                "窗口动作提交失败。",
                "transport_failed");
        }

        await PostInteractionResultAsync("action", requestId, acknowledgement);
        _inputGate.SetResultPending(false);
    }

    private async Task PostInteractionResultAsync(string kind, string requestId, object acknowledgement)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "interactionResult",
            value = new { kind, requestId, acknowledgement },
        }, BridgeJsonOptions);
        if (Dispatcher.CheckAccess())
        {
            _webView.CoreWebView2.PostWebMessageAsJson(message);
            return;
        }

        await Dispatcher.InvokeAsync(() => _webView.CoreWebView2.PostWebMessageAsJson(message));
    }

    private Task PostInteractionResultIfAvailableAsync(string kind, string requestId, object acknowledgement) =>
        _webView.CoreWebView2 is null
            ? Task.CompletedTask
            : PostInteractionResultAsync(kind, requestId, acknowledgement);

    private void ApplyPreferences(JsonElement root)
    {
        if (!root.TryGetProperty("value", out var preferences) || preferences.ValueKind != JsonValueKind.Object) return;
        if (preferences.TryGetProperty("width", out var width) && width.TryGetDouble(out var requestedWidth))
        {
            _preferredWidth = Math.Max(320, requestedWidth);
        }

        if (preferences.TryGetProperty("height", out var height) && height.TryGetDouble(out var requestedHeight))
        {
            _preferredHeight = Math.Max(240, requestedHeight);
        }
    }

    private bool IsStandardPage => ViewName.StartsWith("softwarehub.standard-", StringComparison.Ordinal);

    private static void StageOutsideVirtualScreen(IntPtr handle)
    {
        if (!NativeWindowMethods.GetWindowRect(handle, out var bounds))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 DesktopAgent 预热窗口位置。");
        }

        var left = NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmXVirtualScreen)
            - bounds.Width
            - (int)StagingScreenMargin;
        var top = NativeWindowMethods.GetSystemMetrics(NativeWindowMethods.SmYVirtualScreen)
            - bounds.Height
            - (int)StagingScreenMargin;
        ApplyNativeBounds(handle, new NativeRectangle(left, top, left + bounds.Width, top + bounds.Height));
    }

    private static TaskCompletionSource<RenderedPageMetrics> NewMetricsSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool TryReadMetrics(JsonElement root, out RenderedPageMetrics metrics)
    {
        metrics = default;
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("viewportWidth", out var viewportWidth)
            || !value.TryGetProperty("viewportHeight", out var viewportHeight)
            || !value.TryGetProperty("contentWidth", out var contentWidth)
            || !value.TryGetProperty("contentHeight", out var contentHeight)
            || !viewportWidth.TryGetDouble(out var viewportWidthValue)
            || !viewportHeight.TryGetDouble(out var viewportHeightValue)
            || !contentWidth.TryGetDouble(out var contentWidthValue)
            || !contentHeight.TryGetDouble(out var contentHeightValue))
        {
            return false;
        }

        metrics = new RenderedPageMetrics(
            viewportWidthValue,
            viewportHeightValue,
            contentWidthValue,
            contentHeightValue);
        return true;
    }

    private RenderedPageMetrics MeasureWebViewViewport()
    {
        if (_webView.ActualWidth <= 0 || _webView.ActualHeight <= 0)
        {
            throw new InvalidOperationException("DesktopAgent WebView2 尚未完成 WPF 布局。");
        }

        return new RenderedPageMetrics(
            _webView.ActualWidth,
            _webView.ActualHeight,
            0,
            0);
    }

    private RenderedPageMetrics GrowStandardWindowForContent(
        IntPtr businessHandle,
        DesktopMonitorTarget target,
        Window? owner,
        double contentHeight)
    {
        var desiredHeight = Math.Min(
            target.WorkAreaHeightDip,
            contentHeight + DesktopWindowFrame.TitleBarHeight);
        if (desiredHeight > Height + LayoutTolerance)
        {
            Height = desiredHeight;
            var bounds = CalculateBusinessBounds(target, owner);
            ApplyNativeBounds(businessHandle, bounds);
            UpdateLayout();
        }

        return MeasureWebViewViewport();
    }

    private async Task<RenderedPageMetrics> StabilizeAsync(CancellationToken token)
    {
        _stabilized = NewMetricsSource();
        _webView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"stabilize\"}");
        return await WaitForPresentationPhaseAsync(_stabilized.Task, "stabilized", token);
    }

    private async Task<RenderedPageMetrics> VerifyViewportAsync(CancellationToken token)
    {
        _viewportVerified = NewMetricsSource();
        _webView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"verifyViewport\"}");
        return await WaitForPresentationPhaseAsync(_viewportVerified.Task, "viewportVerified", token);
    }

    private static void EnsureViewportUnchanged(RenderedPageMetrics expected, RenderedPageMetrics actual)
    {
        if (ViewportMatches(expected, actual))
        {
            return;
        }

        throw new InvalidOperationException(
            $"WebView2 最终 viewport 与 WPF 布局不一致：期望 {expected.ViewportWidth:F1}x{expected.ViewportHeight:F1}，" +
            $"实际 {actual.ViewportWidth:F1}x{actual.ViewportHeight:F1}。");
    }

    private static bool ViewportMatches(RenderedPageMetrics expected, RenderedPageMetrics actual) =>
        Math.Abs(expected.ViewportWidth - actual.ViewportWidth) <= LayoutTolerance
        && Math.Abs(expected.ViewportHeight - actual.ViewportHeight) <= LayoutTolerance;

    private void ApplyBusinessSize(double? requestedWidth, double? requestedHeight, DesktopMonitorTarget target)
    {
        var maximumWidth = Math.Max(1, target.WorkAreaWidthDip);
        var maximumHeight = Math.Max(1, target.WorkAreaHeightDip);
        Width = Math.Clamp(requestedWidth ?? Width, Math.Min(320, maximumWidth), maximumWidth);
        Height = Math.Clamp(requestedHeight ?? Height, Math.Min(240, maximumHeight), maximumHeight);
    }

    private NativeRectangle CalculateBusinessBounds(DesktopMonitorTarget target, Window? owner)
    {
        NativeRectangle? ownerRectangle = null;
        if (owner is not null)
        {
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            if (ownerHandle != IntPtr.Zero && NativeWindowMethods.GetWindowRect(ownerHandle, out var rectangle))
            {
                ownerRectangle = rectangle;
            }
        }

        return DesktopWindowPlacement.Center(target, Width, Height, ownerRectangle);
    }

    private static DesktopMonitorTarget ResolveTargetMonitor(Window? owner) =>
        DesktopMonitorResolver.Resolve(owner);

    private DesktopMonitorTarget ResolveWpfDpi(DesktopMonitorTarget target)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var dpiX = (uint)Math.Max(1, Math.Round(dpi.DpiScaleX * 96));
        var dpiY = (uint)Math.Max(1, Math.Round(dpi.DpiScaleY * 96));
        return target with { DpiX = dpiX, DpiY = dpiY };
    }

    private void OnDisplayConfigurationChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            if (_request is null && !_initializingForPresentation)
            {
                StageOutsideVirtualScreen(handle);
                return;
            }

            var target = ResolveWpfDpi(ResolveTargetMonitor(this));
            ApplyBusinessSize(Width, Height, target);
            ApplyNativeBounds(handle, CalculateBusinessBounds(target, Owner));
            UpdateLayout();
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "DesktopAgentDisplayRecoveryFailed for {ServiceInstanceId} {WindowId}",
                ServiceInstanceId,
                WindowId);
        }
    }

    private void ApplyPresentationShell(
        AgentWindow request,
        Window? owner,
        DesktopMonitorTarget target,
        double? width = null,
        double? height = null)
    {
        Title = request.Title ?? request.ViewName;
        _frame.ApplyColors(request.TitleBar);
        ApplyBusinessSize(width ?? request.Width ?? _preferredWidth, height ?? request.Height ?? _preferredHeight, target);
        Topmost = request.Topmost;
        ShowActivated = false;
        Owner = owner;
        ShowInTaskbar = owner is null;
    }

    private void BeginPresentationCancellation(CancellationToken token)
    {
        _presentationCancellation?.Dispose();
        _presentationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _closeAcceptedDuringPresentation = false;
    }

    private async Task<bool> HidePresentationMaskAsync(CancellationToken token) =>
        await WaitForPresentationPhaseAsync(
            WebViewPresentationMask.SetVisibilityAsync(
                _webView.CoreWebView2,
                visible: false,
                _presentationGeneration,
                token),
            "maskReveal",
            token);

    private void QueuePresentationMask()
    {
        if (_webView.CoreWebView2 is null) return;
        var generation = Interlocked.Increment(ref _presentationGeneration);
        WebViewPresentationMask.PostVisibility(
            _webView.CoreWebView2,
            visible: true,
            generation);
    }

    private async Task<T> WaitForPresentationPhaseAsync<T>(
        Task<T> operation,
        string phase,
        CancellationToken token)
    {
        try
        {
            return await operation.WaitAsync(TimeSpan.FromSeconds(15), token);
        }
        catch (TimeoutException exception)
        {
            Log.Error(
                exception,
                "DesktopAgentWindowPresentationTimedOut for {ServiceInstanceId} {WindowId}: View={ViewName}, Phase={PresentationPhase}",
                ServiceInstanceId,
                WindowId,
                ViewName,
                phase);
            throw new TimeoutException(
                $"DesktopAgent 窗口 {WindowId ?? "(pending)"} 在 {phase} 阶段等待超时。",
                exception);
        }
    }

    private void DisposePresentationCancellation()
    {
        _presentationCancellation?.Dispose();
        _presentationCancellation = null;
    }

    private static void ApplyNativeBounds(IntPtr handle, NativeRectangle bounds)
    {
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
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法定位 DesktopAgent 业务窗口。");
        }
    }

    private void AbortRequestPresentation()
    {
        QueuePresentationMask();
        Action restoreForeground = static () => { };
        ModalWindowReleaseTransition.Run(
            () => restoreForeground = _releasing(this),
            () =>
            {
                StageOutsideVirtualScreen(new WindowInteropHelper(this).Handle);
            },
            () => restoreForeground(),
            () => Owner = null);
        Opacity = 1;
        ShowInTaskbar = false;
        _inputGate.Reset();
        _request = null;
        _pendingRequest = null;
        _loaded = null;
        DisposePresentationCancellation();
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_allowClose) return;

        args.Cancel = true;
        var request = _request ?? _pendingRequest;
        if (request is null || _inputGate.IsResultPending) return;

        // 标题栏关闭代表操作员拒绝；必须先把终态回传服务，否则 Pending 补拉会重新打开。
        _inputGate.SetResultPending(true);
        _ = RejectAndReleaseAsync(request);
    }

    private async Task RejectAndReleaseAsync(AgentWindow request)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var acknowledgement = await _reportResult(new WindowResultSubmission(
                requestId,
                request.WindowId,
                request.Revision,
                DateTimeOffset.UtcNow.AddSeconds(15),
                "closed",
                "window-close",
                Result: null,
                Source: "WindowChrome"));
            await PostInteractionResultIfAvailableAsync("close", requestId, acknowledgement);
            if (IsTerminalAcknowledgement(acknowledgement))
            {
                _closeAcceptedDuringPresentation = true;
                var assigned = await Dispatcher.InvokeAsync(() =>
                {
                    Hide();
                    _presentationCancellation?.Cancel();
                    return _request is not null;
                });
                if (assigned) await ReleaseFromServiceOnDispatcherAsync();
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "DesktopAgentWindowRejectFailed for {ServiceInstanceId} {WindowId}", ServiceInstanceId, WindowId);
        }

        // 服务没有确认终态时保持窗口与当前操作绑定，不记录、不延迟重放。
        _inputGate.SetResultPending(false);
    }

    private static bool IsTerminalAcknowledgement(WindowResultAck acknowledgement) =>
        acknowledgement.Status is "accepted" or "replayed" or "notFound";

    private async Task ReleaseFromServiceOnDispatcherAsync()
    {
        if (Dispatcher.CheckAccess())
        {
            ReleaseFromService();
            return;
        }

        await Dispatcher.InvokeAsync(ReleaseFromService);
    }

    /// <summary>结束当前业务请求，将已初始化窗口隐藏并归还复用池。</summary>
    public void ReleaseFromService()
    {
        if (_request is null) return;
        if (_presenting)
        {
            _releasePending = true;
            return;
        }

        ReleasedWindowId = _request.WindowId;
        Log.Information(
            "DesktopAgentWindowReleasing for {ServiceInstanceId} {WindowId}",
            ServiceInstanceId,
            ReleasedWindowId);
        // 先恢复 Owner，再把窗口移回虚拟桌面外并解除 Owner；HWND 和 WebView2 始终保留。
        QueuePresentationMask();
        ShowInTaskbar = false;
        Action restoreForeground = static () => { };
        ModalWindowReleaseTransition.Run(
            () => restoreForeground = _releasing(this),
            () => StageOutsideVirtualScreen(new WindowInteropHelper(this).Handle),
            () => restoreForeground(),
            () => Owner = null);
        Opacity = 1;
        _inputGate.Reset();
        _request = null;
        _pendingRequest = null;
        _loaded = null;
        DisposePresentationCancellation();
        _released(this);
        ReleasedWindowId = null;
    }

    /// <summary>服务会话结束或 Agent 退出时永久关闭窗口。</summary>
    public void ClosePermanently()
    {
        if (_disposed) return;
        Opacity = 1;
        _allowClose = true;
        Close();
    }

    private static bool SameOrigin(Uri expected, Uri actual) =>
        expected.Scheme == actual.Scheme
        && expected.Host == actual.Host
        && expected.Port == actual.Port;

    public void Dispose() => ClosePermanently();
}
