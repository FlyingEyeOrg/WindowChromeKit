using System.Collections.Concurrent;
using System.Windows.Threading;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>按 Agent Client Session 管理长期 BrowserWindow 投影。</summary>
internal sealed class BrowserWindowManager : IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly AgentInteractionClient _client;
    private readonly DesktopWindowRegistry _registry;
    private readonly WindowManager _pageWindows;
    private readonly WebView2InitializationCoordinator _webViewInitialization;
    private readonly WebView2EnvironmentProvider _webViewEnvironment;
    private readonly ConcurrentDictionary<(string ServiceId, string WindowId), BrowserWindow> _windows = new();
    private readonly ConcurrentDictionary<(string ServiceId, string WindowId), AgentBrowserWindow> _desiredWindows = new();

    public BrowserWindowManager(
        Dispatcher dispatcher,
        AgentInteractionClient client,
        DesktopWindowRegistry registry,
        WindowManager pageWindows,
        WebView2InitializationCoordinator webViewInitialization,
        WebView2EnvironmentProvider webViewEnvironment)
    {
        _dispatcher = dispatcher;
        _client = client;
        _registry = registry;
        _pageWindows = pageWindows;
        _webViewInitialization = webViewInitialization;
        _webViewEnvironment = webViewEnvironment;
    }

    public async Task ReconcileAsync(string serviceId, IReadOnlyList<AgentBrowserWindow> desired, CancellationToken token)
    {
        var desiredIds = desired.Select(item => item.WindowId).ToHashSet(StringComparer.Ordinal);
        if (desiredIds.Count != desired.Count)
            throw new InvalidOperationException("BrowserWindow 标识必须在服务会话内唯一。");
        foreach (var key in _desiredWindows.Keys.Where(key => key.ServiceId == serviceId && !desiredIds.Contains(key.WindowId)).ToArray())
            _desiredWindows.TryRemove(key, out _);
        var staleWindows = OrderChildrenFirst(_windows.Where(pair =>
            pair.Key.ServiceId == serviceId && !desiredIds.Contains(pair.Key.WindowId)));
        var closeBatch = await _dispatcher.InvokeAsync(() => _registry.BeginCloseBatch(
            serviceId,
            staleWindows.Select(pair => pair.Key.WindowId).ToArray()));
        try
        {
            foreach (var pair in staleWindows)
                await CloseAsync(pair.Key.ServiceId, pair.Key.WindowId);
        }
        finally
        {
            await _dispatcher.InvokeAsync(closeBatch.Dispose);
        }
        foreach (var window in DesktopWindowOwnershipGraphValidator.OrderBrowserWindowsParentsFirst(desired))
        {
            _desiredWindows[(serviceId, window.WindowId)] = window;
            await UpsertAsync(serviceId, window, allowMissingOwner: true, token);
        }
    }

    public async Task UpsertAsync(string serviceId, AgentBrowserWindow projection, CancellationToken token)
    {
        Validate(projection);
        var key = (serviceId, projection.WindowId);
        var hadPrevious = _desiredWindows.TryGetValue(key, out var previous);
        _desiredWindows[key] = projection;
        try
        {
            await UpsertAsync(serviceId, projection, allowMissingOwner: false, token);
        }
        catch
        {
            if (hadPrevious) _desiredWindows[key] = previous!;
            else _desiredWindows.TryRemove(key, out _);
            throw;
        }
    }

    public IReadOnlyList<AgentBrowserWindow> GetDesiredWindows(string serviceId) =>
        [.. _desiredWindows.Where(pair => pair.Key.ServiceId == serviceId).Select(pair => pair.Value)];

    public Task BindSynchronizedOwnersAsync(string serviceId, CancellationToken token) =>
        _dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            var ordered = DesktopWindowOwnershipGraphValidator.OrderBrowserWindowsParentsFirst(
                _desiredWindows.Where(pair => pair.Key.ServiceId == serviceId).Select(pair => pair.Value).ToArray());
            foreach (var projection in ordered)
            {
                if (!_windows.TryGetValue((serviceId, projection.WindowId), out var window))
                    throw new InvalidOperationException($"BrowserWindow {projection.WindowId} 不存在。");
                ApplyOwner(serviceId, window, projection.OwnerWindowId, allowMissingOwner: false, rebindRegistry: true);
            }
        }).Task;

    private async Task UpsertAsync(
        string serviceId,
        AgentBrowserWindow projection,
        bool allowMissingOwner,
        CancellationToken token)
    {
        Validate(projection);
        var key = (serviceId, projection.WindowId);
        if (_windows.TryGetValue(key, out var existing))
        {
            await _dispatcher.InvokeAsync(() =>
            {
                ApplyOwner(serviceId, existing, projection.OwnerWindowId, allowMissingOwner, rebindRegistry: true);
                existing.Update(projection);
            });
            return;
        }
        await _dispatcher.InvokeAsync(async () =>
        {
            var instance = new BrowserWindow(
                projection,
                new BrowserWindowContext
                {
                    Icon = _pageWindows.GetServiceIcon(serviceId),
                    Environment = _webViewEnvironment.Environment,
                    ControllerOptions = _webViewEnvironment.CreateControllerOptions(serviceId),
                    WebViewInitialization = _webViewInitialization,
                    RequestClose = request => _client.RequestBrowserWindowCloseAsync(
                        serviceId,
                        request,
                        CancellationToken.None),
                    OpenChild = uri => OpenChildAsync(serviceId, projection, uri, token),
                    Releasing = () => _registry.Remove(serviceId, projection.WindowId),
                    Closed = () => NotifyClosedAsync(serviceId, projection.WindowId),
                });
            if (!_windows.TryAdd(key, instance))
            {
                await instance.DisposeAsync();
                return;
            }
            var relationshipRegistered = false;
            try
            {
                var registeredOwnerId = projection.OwnerWindowId;
                if (registeredOwnerId is not null
                    && allowMissingOwner
                    && !_registry.TryGet(serviceId, registeredOwnerId, out _))
                {
                    registeredOwnerId = null;
                }
                _registry.Register(serviceId, projection.WindowId, new DesktopWindowOwner(
                    "BrowserWindow",
                    projection.WindowId,
                    instance.Surface,
                    instance.BlockForModalChild,
                    instance.ReleaseModalChildBlock),
                    registeredOwnerId,
                    modal: false);
                relationshipRegistered = true;
                ApplyOwner(serviceId, instance, projection.OwnerWindowId, allowMissingOwner, rebindRegistry: false);
                await instance.InitializeAndShowAsync(token);
                if (!instance.IsAssigned)
                {
                    _registry.Remove(serviceId, projection.WindowId);
                    relationshipRegistered = false;
                    _windows.TryRemove(key, out _);
                }
            }
            catch
            {
                if (relationshipRegistered) _registry.Remove(serviceId, projection.WindowId);
                _windows.TryRemove(key, out _);
                await instance.DisposeAsync();
                throw;
            }
        }).Task.Unwrap();
    }

    public Task ExecuteAsync(string serviceId, BrowserWindowCommand command) =>
        _dispatcher.InvokeAsync(async () =>
        {
            if (!_windows.TryGetValue((serviceId, command.WindowId), out var window))
                throw new InvalidOperationException($"BrowserWindow {command.WindowId} 不存在。");
            await window.ExecuteAsync(command);
        }).Task.Unwrap();

    public async Task HideServiceAsync(string serviceId)
    {
        foreach (var pair in _windows.Where(pair => pair.Key.ServiceId == serviceId).ToArray())
            await _dispatcher.InvokeAsync(pair.Value.HideAsync).Task.Unwrap();
    }

    public async Task CloseServiceAsync(string serviceId)
    {
        var windows = OrderChildrenFirst(_windows.Where(pair => pair.Key.ServiceId == serviceId));
        var closeBatch = await _dispatcher.InvokeAsync(() => _registry.BeginCloseBatch(
            serviceId,
            windows.Select(pair => pair.Key.WindowId).ToArray()));
        try
        {
            foreach (var pair in windows)
                await CloseAsync(pair.Key.ServiceId, pair.Key.WindowId);
        }
        finally
        {
            await _dispatcher.InvokeAsync(closeBatch.Dispose);
        }
        foreach (var key in _desiredWindows.Keys.Where(key => key.ServiceId == serviceId).ToArray())
            _desiredWindows.TryRemove(key, out _);
    }

    private async Task CloseAsync(string serviceId, string windowId)
    {
        _desiredWindows.TryRemove((serviceId, windowId), out _);
        if (_windows.TryRemove((serviceId, windowId), out var window))
        {
            await _dispatcher.InvokeAsync(window.CloseFromServiceAsync).Task.Unwrap();
        }
    }

    private Task OpenChildAsync(
        string serviceId,
        AgentBrowserWindow parent,
        Uri uri,
        CancellationToken token) =>
        UpsertAsync(serviceId, parent with
        {
            WindowId = Guid.NewGuid().ToString("N"),
            OwnerWindowId = parent.WindowId,
            Url = uri.ToString(),
            TargetUrl = uri.ToString(),
            Title = null,
            Revision = 1,
        }, token);

    private async Task NotifyClosedAsync(string serviceId, string windowId)
    {
        _windows.TryRemove((serviceId, windowId), out _);
        _desiredWindows.TryRemove((serviceId, windowId), out _);
        _registry.Remove(serviceId, windowId);
        await _client.NotifyBrowserWindowClosedAsync(
            serviceId,
            new BrowserWindowClosedNotification(windowId),
            CancellationToken.None);
    }

    private static void Validate(AgentBrowserWindow projection)
    {
        if (!Uri.TryCreate(projection.Url, UriKind.Absolute, out var url)
            || url.Scheme is not ("http" or "https")
            || !projection.AllowedOrigins.Contains(url.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("BrowserWindow URL 或 Origin 配置无效。");
        if (projection.TargetUrl is { } targetValue
            && (!Uri.TryCreate(targetValue, UriKind.Absolute, out var target)
                || target.Scheme is not ("http" or "https")
                || !projection.AllowedOrigins.Contains(
                    target.GetLeftPart(UriPartial.Authority),
                    StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("BrowserWindow 目标 URL 或 Origin 配置无效。");
        }
        if (projection.Width is <= 0 || projection.Height is <= 0
            || projection.MinWidth is < 0 || projection.MinHeight is < 0)
            throw new InvalidOperationException("BrowserWindow 的 Width/Height 必须为正数，MinWidth/MinHeight 不能为负数。");
    }

    private void ApplyOwner(
        string serviceId,
        BrowserWindow window,
        string? ownerWindowId,
        bool allowMissingOwner,
        bool rebindRegistry)
    {
        if (ownerWindowId is null)
        {
            ApplyValidatedOwner(null);
            return;
        }

        if (!_registry.TryGet(serviceId, ownerWindowId, out var owner))
        {
            if (allowMissingOwner) return;
            throw new InvalidOperationException($"Desktop 父窗口 {ownerWindowId} 不存在。");
        }

        ApplyValidatedOwner(owner);

        void ApplyValidatedOwner(DesktopWindowOwner? resolvedOwner)
        {
            var previousOwnerId = rebindRegistry
                ? _registry.GetOwnerWindowId(serviceId, window.WindowId)
                : null;
            if (rebindRegistry)
                _registry.Rebind(serviceId, window.WindowId, ownerWindowId, modal: false);
            try
            {
                window.Surface.Owner = resolvedOwner?.Window;
                window.Surface.ShowInTaskbar = resolvedOwner is null;
            }
            catch
            {
                if (rebindRegistry)
                    _registry.Rebind(serviceId, window.WindowId, previousOwnerId, modal: false);
                throw;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var window in _windows.Values) await window.DisposeAsync();
        _windows.Clear();
        _desiredWindows.Clear();
    }

    private static KeyValuePair<(string ServiceId, string WindowId), BrowserWindow>[] OrderChildrenFirst(
        IEnumerable<KeyValuePair<(string ServiceId, string WindowId), BrowserWindow>> windows) =>
        windows.OrderByDescending(pair => GetOwnerDepth(pair.Value.Surface)).ToArray();

    private static int GetOwnerDepth(System.Windows.Window window)
    {
        var depth = 0;
        var owner = window.Owner;
        while (owner is not null)
        {
            depth++;
            owner = owner.Owner;
        }
        return depth;
    }
}
