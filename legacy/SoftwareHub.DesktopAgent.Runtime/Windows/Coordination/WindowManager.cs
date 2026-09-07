using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Serilog;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>管理 WPF/WebView2 窗口、服务实时连接和重启恢复。</summary>
internal sealed class WindowManager : IAsyncDisposable
{
    private const int MaximumIconBytes = 512 * 1024;
    private static readonly HashSet<string> SupportedIconMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/x-icon",
        "image/vnd.microsoft.icon",
        "image/ico",
        "image/png",
        "application/octet-stream",
    };
    private readonly Dispatcher _dispatcher;
    private readonly RuntimeSettings _settings;
    private readonly AgentInteractionClient _results;
    private readonly AgentSystemWarningWindowManager _systemWarning;
    private readonly DesktopWindowRegistry _windowRegistry;
    private readonly WebView2InitializationCoordinator _webViewInitialization;
    private readonly WebView2EnvironmentProvider _webViewEnvironment;
    // 服务回调属于本机基础设施，不能经过开发者为包恢复设置的 HTTP 代理。
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
    private readonly ConcurrentDictionary<(string ServiceInstanceId, string WindowId), AgentWindow> _known = new();
    private readonly ConcurrentDictionary<string, string> _callbackTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AgentClientRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ImageSource> _serviceIcons = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _suspendedServices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<(string Name, int Version), PageWindowPool>> _pagePools = new(StringComparer.Ordinal);
    private readonly Dictionary<DesktopWindow, PageWindowPool> _windowPools = [];
    private readonly Dictionary<string, List<DesktopWindow>> _windows = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCancellation = new();

    public WindowManager(Dispatcher dispatcher, RuntimeSettings settings, AgentInteractionClient results,
        AgentSystemWarningWindowManager systemWarning,
        DesktopWindowRegistry windowRegistry,
        WebView2InitializationCoordinator webViewInitialization,
        WebView2EnvironmentProvider webViewEnvironment)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _results = results;
        _systemWarning = systemWarning;
        _windowRegistry = windowRegistry;
        _webViewInitialization = webViewInitialization;
        _webViewEnvironment = webViewEnvironment;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        // 首次解码必须发生在 UI Dispatcher；冻结后可安全复用于所有窗口。
        await _dispatcher.InvokeAsync(() => _ = DesktopWindowIconResources.Default);
        await _systemWarning.InitializeAsync(token);
        token.ThrowIfCancellationRequested();
    }

    public async Task<bool> WarmAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        CancellationToken token,
        bool prewarm = true)
    {
        // 注册信息变化时旧回调令牌不能继续用于新页面。
        _callbackTokens.TryRemove(serviceInstanceId, out _);
        _registrations[serviceInstanceId] = registration;
        if (registration.Views.Count == 0)
        {
            await _dispatcher.InvokeAsync(() => ReplacePools(serviceInstanceId, []));
            return true;
        }

        var accessToken = await GetCallbackTokenAsync(serviceInstanceId, registration, token);
        var iconContext = new IconResolutionContext
        {
            ServiceInstanceId = serviceInstanceId,
            Registration = registration,
            AccessToken = accessToken,
            Cache = new Dictionary<string, ImageSource>(StringComparer.Ordinal),
            CancellationToken = token,
        };
        var serviceIcon = await ResolveIconAsync(
            iconContext,
            registration.IconPath,
            DesktopWindowIconResources.Default);
        _serviceIcons[serviceInstanceId] = serviceIcon;
        var prepared = new Dictionary<(string Name, int Version), PageWindowPool>();
        try
        {
            foreach (var view in registration.Views)
            {
                if (!Uri.TryCreate(new Uri(registration.Endpoint), view.Path, out var uri))
                {
                    await CloseWindowsAsync(prepared.Values.SelectMany(pool => pool.Idle));
                    return false;
                }

                var icon = await ResolveIconAsync(
                    iconContext,
                    view.IconPath,
                    serviceIcon);
                var pool = new PageWindowPool
                {
                    ServiceInstanceId = serviceInstanceId,
                    View = view,
                    Registration = registration,
                    AccessToken = accessToken,
                    Uri = uri,
                    Icon = icon,
                };
                var ready = await _dispatcher.InvokeAsync(() =>
                {
                    for (var index = 0; prewarm && index < view.PrewarmWindowCount; index++)
                    {
                        var window = ReservePoolWindow(pool);
                        if (window is null) return false;
                        pool.Idle.Enqueue(window);
                    }
                    return true;
                });
                if (!ready)
                {
                    await CloseWindowsAsync(prepared.Values.SelectMany(item => item.Idle));
                    return false;
                }

                prepared[(view.Name, view.Version)] = pool;
            }

            await _dispatcher.InvokeAsync(() => ReplacePools(serviceInstanceId, prepared));
            return true;
        }
        catch
        {
            await CloseWindowsAsync(prepared.Values.SelectMany(pool => pool.Idle));
            throw;
        }
    }

    /// <summary>服务重连时按配置的长期容量补齐页面池。</summary>
    public async Task<bool> EnsureWarmAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        CancellationToken token)
    {
        var tasks = await _dispatcher.InvokeAsync(() =>
        {
            if (!_pagePools.TryGetValue(serviceInstanceId, out var pools)
                || pools.Count != registration.Views.Count
                || registration.Views.Any(view => !pools.TryGetValue((view.Name, view.Version), out var pool)
                    || !string.Equals(pool.View.Path, view.Path, StringComparison.Ordinal)
                    || !string.Equals(pool.View.IconPath, view.IconPath, StringComparison.Ordinal)
                    || !string.Equals(pool.Registration.IconPath, registration.IconPath, StringComparison.Ordinal)))
            {
                return Array.Empty<Task<bool>>();
            }

            foreach (var pool in pools.Values)
            {
                StartReplenishment(pool);
            }

            return pools.Values
                .Where(pool => pool.Idle.Count < pool.DesiredCapacity && pool.Replenishment is not null)
                .Select(pool => pool.Replenishment!)
                .ToArray();
        });

        if (registration.Views.Count > 0 && tasks.Length == 0)
        {
            var poolsExist = await _dispatcher.InvokeAsync(() =>
                _pagePools.TryGetValue(serviceInstanceId, out var pools)
                && pools.Count == registration.Views.Count
                && pools.Values.All(pool => pool.Idle.Count >= pool.DesiredCapacity));
            if (!poolsExist)
            {
                return await WarmAsync(serviceInstanceId, registration, token);
            }
        }

        if (tasks.Length > 0)
        {
            await Task.WhenAll(tasks).WaitAsync(token);
        }

        return await _dispatcher.InvokeAsync(() =>
            !_pagePools.TryGetValue(serviceInstanceId, out var pools)
                ? registration.Views.Count == 0
                : registration.Views.All(view =>
                    pools.TryGetValue((view.Name, view.Version), out var pool)
                    && pool.Idle.Count >= pool.DesiredCapacity));
    }

    public async Task UpsertAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        AgentWindow item,
        CancellationToken token)
    {
        if (item.OwnerWindowId is not null && !item.Modal)
            throw new InvalidOperationException("只有模态 PageWindow 可以指定 OwnerWindowId。");
        if (!string.Equals(item.Status, "pending", StringComparison.Ordinal))
        {
            await CloseFromServiceAsync(serviceInstanceId, item.WindowId);
            return;
        }

        var windowKey = (serviceInstanceId, item.WindowId);
        while (true)
        {
            if (_known.TryGetValue(windowKey, out var current))
            {
                if (current.Revision >= item.Revision)
                {
                    return;
                }

                if (_known.TryUpdate(windowKey, item, current))
                {
                    break;
                }

                continue;
            }

            if (_known.TryAdd(windowKey, item))
            {
                break;
            }
        }

        var view = registration.Views.FirstOrDefault(x => x.Name == item.ViewName && x.Version == item.ViewVersion);
        if (view is null)
        {
            _known.TryRemove(windowKey, out _);
            throw new InvalidOperationException($"Desktop View {item.ViewName}@{item.ViewVersion} 未注册。");
        }

        await _dispatcher.InvokeAsync(async () =>
        {
            if (_windows.TryGetValue(serviceInstanceId, out var existing))
            {
                var assigned = existing.FirstOrDefault(x => x.WindowId == item.WindowId);
                if (assigned is not null)
                {
                    if (_windowPools.TryGetValue(assigned, out var assignedPool)
                        && !assignedPool.Retired
                        && assignedPool.View.Name == view.Name
                        && assignedPool.View.Version == view.Version)
                    {
                        await assigned.UpdateRequestAsync(item, token);
                        return;
                    }

                    // 注册换代后的旧窗口不能重新进入新页面池。
                    assigned.ReleaseFromService();
                }
            }

            if (!_pagePools.TryGetValue(serviceInstanceId, out var pools)
                || !pools.TryGetValue((view.Name, view.Version), out var pool)
                || pool.Retired)
            {
                _known.TryRemove(windowKey, out _);
                throw new InvalidOperationException($"Desktop View {view.Name}@{view.Version} 页面池尚未就绪。");
            }

            // 池中的窗口槽位已经计入总实例数；预热槽位可直接领取，零预热槽位按需初始化。
            // 总实例上限由 ReservePoolWindow 统一约束；这里仅限制当前服务的可见窗口数。
            if (_windows.TryGetValue(serviceInstanceId, out var visible)
                && visible.Count >= registration.WindowPolicy.MaxVisibleWindowCount)
            {
                Log.Warning("DesktopAgentWindowCapacityExceeded for {ServiceInstanceId}", serviceInstanceId);
                _ = _systemWarning.ShowAsync("业务窗口或 WebView2 实例已达到全局上限。请先完成或关闭现有窗口后重试。");
                _known.TryRemove(windowKey, out _);
                throw new InvalidOperationException("DesktopAgent 可见窗口已达到当前服务上限。");
            }

            if (pool.Idle.Count == 0)
            {
                StartReplenishment(pool);
                if (pool.Replenishment is not null)
                {
                    await pool.Replenishment.WaitAsync(token);
                }
            }

            // 预热容量可以为 0，但真实业务请求仍必须能够按需创建实例。总配额已满时，
            // 先回收其他页面的一个空闲槽位，再把名额让给当前请求。
            if (pool.Idle.Count == 0)
            {
                var demandedWindow = ReservePoolWindow(pool);
                if (demandedWindow is null && TryReleaseIdleCapacity(pool))
                {
                    demandedWindow = ReservePoolWindow(pool);
                }
                if (demandedWindow is not null)
                {
                    pool.Idle.Enqueue(demandedWindow);
                }
            }

            if (pool.Idle.Count == 0)
            {
                _known.TryRemove(windowKey, out _);
                throw new InvalidOperationException($"Desktop View {view.Name}@{view.Version} 没有可用的窗口实例。");
            }

            var window = pool.Idle.Dequeue();
            // TrustedReset 页面关闭后会把同一个 HWND/WebView2 归还池中；活跃期间不补建
            // 第二个实例，避免标准对话框点击时触发一个额外顶层窗口。
            if (pool.View.ReuseMode != DesktopViewReuseMode.TrustedReset) StartReplenishment(pool);
            if (!_windows.TryGetValue(serviceInstanceId, out var list))
            {
                _windows[serviceInstanceId] = list = [];
            }

            list.Add(window);
            DesktopWindowOwner? owner = null;
            if (item.OwnerWindowId is not null)
            {
                if (!_windowRegistry.TryGet(serviceInstanceId, item.OwnerWindowId, out owner))
                {
                    list.Remove(window);
                    pool.Idle.Enqueue(window);
                    _known.TryRemove(windowKey, out _);
                    throw new InvalidOperationException($"Desktop 父窗口 {item.OwnerWindowId} 不存在。");
                }

            }

            var relationshipRegistered = false;
            try
            {
                _windowRegistry.Register(serviceInstanceId, item.WindowId, new DesktopWindowOwner(
                    "PageWindow",
                    item.WindowId,
                    window,
                    window.BlockForModalChild,
                    window.ReleaseModalChildBlock),
                    item.OwnerWindowId,
                    item.Modal && item.OwnerWindowId is not null);
                relationshipRegistered = true;
                if (!await window.PrepareAsync(item, owner?.Window, token))
                {
                    if (window.CloseAcceptedDuringPresentation)
                    {
                        _windowRegistry.Remove(serviceInstanceId, item.WindowId);
                        relationshipRegistered = false;
                        _known.TryRemove(windowKey, out _);
                        list.Remove(window);
                        _windowPools.Remove(window);
                        window.ClosePermanently();
                        StartReplenishment(pool);
                        return;
                    }
                    throw new InvalidOperationException($"Desktop View {view.Name}@{view.Version} 页面初始化失败。");
                }
                await window.ShowRequestAsync(item, owner?.Window, token);
                // 页面可能在首次 onLoad 中立即完成或关闭；此时窗口已按服务终态归还，
                // 不能再把它重新登记为可见窗口或发送 Presented。
                if (!window.IsAssigned)
                {
                    return;
                }
                try
                {
                    await _results.NotifyWindowPresentedAsync(
                        serviceInstanceId,
                        new WindowPresentedNotification(item.WindowId, item.Revision, DateTimeOffset.UtcNow),
                        token);
                }
                catch (Exception exception)
                {
                    // Presented 是通知回调，失败不能撤销已经显示的窗口或伪造业务终态。
                    Log.Warning(exception,
                        "DesktopAgentWindowPresentedNotificationFailed for {ServiceInstanceId} {WindowId}",
                        serviceInstanceId,
                        item.WindowId);
                }
            }
            catch
            {
                if (relationshipRegistered) _windowRegistry.Remove(serviceInstanceId, item.WindowId);
                _known.TryRemove(windowKey, out _);
                list.Remove(window);
                if (window.CanReuse)
                {
                    pool.Idle.Enqueue(window);
                }
                else
                {
                    _windowPools.Remove(window);
                    window.ClosePermanently();
                    StartReplenishment(pool);
                }
                throw;
            }
        }).Task.Unwrap();
    }

    public async Task ReconcileAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        IReadOnlyList<AgentWindow> snapshot,
        CancellationToken token)
    {
        var pendingIds = snapshot.Select(x => x.WindowId).ToHashSet(StringComparer.Ordinal);
        foreach (var known in _known.Where(x => string.Equals(x.Key.ServiceInstanceId, serviceInstanceId, StringComparison.Ordinal)
                                                 && !pendingIds.Contains(x.Key.WindowId)).ToArray())
        {
            _known.TryRemove(known.Key, out _);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            if (!_windows.TryGetValue(serviceInstanceId, out var active))
            {
                return;
            }

            var staleWindows = OrderChildrenFirst(
                active.Where(x => x.WindowId is { } id && !pendingIds.Contains(id)));
            using var closeBatch = _windowRegistry.BeginCloseBatch(
                serviceInstanceId,
                staleWindows.Select(window => window.WindowId!).ToArray());
            foreach (var stale in staleWindows)
            {
                stale.ReleaseFromService();
            }
        });

        _suspendedServices.TryRemove(serviceInstanceId, out _);

        foreach (var item in DesktopWindowOwnershipGraphValidator.OrderPageWindowsParentsFirst(snapshot))
        {
            // 快照是权威状态；即使实时事件已经到达，也由 Revision 保证幂等。
            _known.TryRemove((serviceInstanceId, item.WindowId), out _);
            await UpsertAsync(serviceInstanceId, registration, item, token);
        }
    }

    public Task ActivateAsync(string serviceInstanceId, string windowId) => _dispatcher.InvokeAsync(() =>
    {
        if (_windows.TryGetValue(serviceInstanceId, out var list))
        {
            list.FirstOrDefault(x => x.WindowId == windowId)?.Activate();
        }
    }).Task;

    public Task CloseFromServiceAsync(string serviceInstanceId, string windowId)
    {
        _known.TryRemove((serviceInstanceId, windowId), out _);
        return _dispatcher.InvokeAsync(() => ReleaseWindow(serviceInstanceId, windowId)).Task;
    }

    /// <summary>连接替换或断开时立即隐藏业务窗口，等待服务权威快照重新建立投影。</summary>
    public Task HideServiceAsync(string serviceInstanceId) => _dispatcher.InvokeAsync(() =>
    {
        _suspendedServices[serviceInstanceId] = 0;
        if (!_windows.TryGetValue(serviceInstanceId, out var active))
        {
            return;
        }

        var windows = OrderChildrenFirst(active);
        using var closeBatch = _windowRegistry.BeginCloseBatch(
            serviceInstanceId,
            windows.Where(window => window.WindowId is not null).Select(window => window.WindowId!).ToArray());
        foreach (var window in windows)
        {
            window.ReleaseFromService();
        }
    }).Task;

    private async Task<WindowResultAck> ReportResultAsync(
        string serviceInstanceId,
        WindowResultSubmission result,
        CancellationToken token)
    {
        try
        {
            return await _results.CompleteWindowAsync(serviceInstanceId, result, token);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            var state = await _results.GetWindowStateAsync(serviceInstanceId, result.WindowId, token);
            return state is not null && !string.Equals(state.State, "pending", StringComparison.OrdinalIgnoreCase)
                ? new WindowResultAck(result.SubmissionId, "replayed", state.State, state.Revision)
                : new WindowResultAck(result.SubmissionId, "unknown", "pending", state?.Revision ?? result.ExpectedRevision,
                    "服务尚未确认本次操作，窗口保持当前交互状态。");
        }
    }

    private DesktopWindow? ReservePoolWindow(PageWindowPool pool)
    {
        if (_windowPools.Count >= _settings.WindowResources.MaxTotalWindowInstanceCount
            - _settings.WindowResources.ReservedSystemWarningWindowCount)
        {
            return null;
        }

        var candidate = CreateWindow(pool);
        // 只预占窗口槽位；标准 WPF WebView2 必须随真实可见请求初始化。
        _windowPools[candidate] = pool;
        return candidate;
    }

    public IReadOnlyList<AgentWindow> GetDesiredWindows(string serviceInstanceId) =>
        [.. _known.Where(pair => string.Equals(
                pair.Key.ServiceInstanceId,
                serviceInstanceId,
                StringComparison.Ordinal))
            .Select(pair => pair.Value)];

    internal ImageSource GetServiceIcon(string serviceInstanceId) =>
        _serviceIcons.TryGetValue(serviceInstanceId, out var icon)
            ? icon
            : DesktopWindowIconResources.Default;

    private async Task<WindowActionAck> ReportActionAsync(
        string serviceInstanceId,
        WindowActionSubmission action,
        CancellationToken token)
    {
        try
        {
            return await _results.InvokeWindowActionAsync(serviceInstanceId, action, token);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            return new WindowActionAck(
                action.RequestId,
                false,
                null,
                "服务尚未确认本次窗口动作。",
                "unknown");
        }
    }

    private bool TryReleaseIdleCapacity(PageWindowPool requestedPool)
    {
        foreach (var pools in _pagePools.Values)
        {
            foreach (var candidatePool in pools.Values)
            {
                if (ReferenceEquals(candidatePool, requestedPool)
                    || candidatePool.Retired
                    || !candidatePool.Idle.TryDequeue(out var idleWindow))
                {
                    continue;
                }

                _windowPools.Remove(idleWindow);
                idleWindow.ClosePermanently();
                return true;
            }
        }

        return false;
    }

    private void StartReplenishment(PageWindowPool pool)
    {
        if (pool.Retired || pool.Idle.Count >= pool.DesiredCapacity || pool.Replenishment is not null)
        {
            return;
        }

        pool.Replenishment = ReplenishAsync(pool);
    }

    private async Task<bool> ReplenishAsync(PageWindowPool pool)
    {
        try
        {
            // 标准 WPF WebView2 的 windowed controller 需要可见顶层窗口；不可见预热会
            // 长时间占住全局初始化锁。这里只预留窗口槽位，真实请求通过可关闭加载壳初始化。
            await Dispatcher.Yield(DispatcherPriority.Background);
            while (!pool.Retired && pool.Idle.Count < pool.DesiredCapacity)
            {
                _disposeCancellation.Token.ThrowIfCancellationRequested();
                var window = ReservePoolWindow(pool);
                if (window is null) return false;

                if (pool.Retired)
                {
                    _windowPools.Remove(window);
                    window.ClosePermanently();
                    return false;
                }

                pool.Idle.Enqueue(window);
            }
            return true;
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "DesktopAgentPagePoolReplenishmentFailed for {ServiceInstanceId} {ViewName}@{ViewVersion}",
                pool.ServiceInstanceId,
                pool.View.Name,
                pool.View.Version);
            return false;
        }
        finally
        {
            pool.Replenishment = null;
        }
    }

    private void ReplacePools(
        string serviceInstanceId,
        Dictionary<(string Name, int Version), PageWindowPool> replacement)
    {
        if (_pagePools.Remove(serviceInstanceId, out var previous))
        {
            foreach (var pool in previous.Values)
            {
                RetirePool(pool);
            }
        }

        _pagePools[serviceInstanceId] = replacement;
    }

    private void RetirePool(PageWindowPool pool)
    {
        pool.Retired = true;
        while (pool.Idle.TryDequeue(out var window))
        {
            _windowPools.Remove(window);
            window.ClosePermanently();
        }
    }

    private DesktopWindow CreateWindow(PageWindowPool pool) => new(new DesktopWindowContext
    {
        ServiceInstanceId = pool.ServiceInstanceId,
        View = pool.View,
        Registration = pool.Registration,
        AccessToken = pool.AccessToken,
        Environment = _webViewEnvironment.Environment,
        ControllerOptions = _webViewEnvironment.CreateControllerOptions(pool.ServiceInstanceId),
        WebViewInitialization = _webViewInitialization,
        Uri = pool.Uri,
        Icon = pool.Icon,
        ReportResult = result => ReportResultAsync(pool.ServiceInstanceId, result, CancellationToken.None),
        ReportAction = action => ReportActionAsync(pool.ServiceInstanceId, action, CancellationToken.None),
        Releasing = OnReleasing,
        Released = OnReleased,
    });

    private async Task<ImageSource> ResolveIconAsync(
        IconResolutionContext context,
        string? iconPath,
        ImageSource fallback)
    {
        if (iconPath is null) return fallback;
        if (context.Cache.TryGetValue(iconPath, out var cached)) return cached;

        try
        {
            var icon = await LoadCustomIconAsync(context, iconPath);
            context.Cache[iconPath] = icon;
            return icon;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !context.CancellationToken.IsCancellationRequested)
        {
            Log.Warning(
                exception,
                "DesktopAgentIconLoadFailed for {ServiceInstanceId} {IconPath}; falling back to the parent icon",
                context.ServiceInstanceId,
                iconPath);
            context.Cache[iconPath] = fallback;
            return fallback;
        }
    }

    private async Task<ImageSource> LoadCustomIconAsync(
        IconResolutionContext context,
        string iconPath)
    {
        var endpoint = new Uri(context.Registration.Endpoint, UriKind.Absolute);
        var iconUri = new Uri(endpoint, iconPath);
        if (!SameOrigin(endpoint, iconUri))
        {
            throw new InvalidDataException("Desktop 图标必须与服务回调端点同源。");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, iconUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        using var response = await _http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            context.CancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumIconBytes)
        {
            throw new InvalidDataException($"Desktop 图标超过 {MaximumIconBytes} 字节限制。");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !SupportedIconMediaTypes.Contains(mediaType))
        {
            throw new InvalidDataException($"Desktop 图标 Content-Type {mediaType} 不受支持。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(context.CancellationToken);
        using var content = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, context.CancellationToken);
            if (read == 0) break;
            if (content.Length + read > MaximumIconBytes)
            {
                throw new InvalidDataException($"Desktop 图标超过 {MaximumIconBytes} 字节限制。");
            }

            await content.WriteAsync(buffer.AsMemory(0, read), context.CancellationToken);
        }

        var bytes = content.ToArray();
        if (bytes.Length == 0) throw new InvalidDataException("Desktop 图标内容为空。");
        return await _dispatcher.InvokeAsync(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 256;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return (ImageSource)bitmap;
        });
    }

    private static bool SameOrigin(Uri expected, Uri actual) =>
        string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase)
        && expected.Port == actual.Port;

    private Action OnReleasing(DesktopWindow releasing)
    {
        if (releasing.WindowId is { } windowId)
        {
            return _windowRegistry.Remove(releasing.ServiceInstanceId, windowId);
        }
        return static () => { };
    }

    private void OnReleased(DesktopWindow released)
    {
        if (released.ReleasedWindowId is { } releasedWindowId)
        {
            _known.TryRemove((released.ServiceInstanceId, releasedWindowId), out _);
            _windowRegistry.Remove(released.ServiceInstanceId, releasedWindowId);
        }

        if (_windows.TryGetValue(released.ServiceInstanceId, out var list))
        {
            list.Remove(released);
        }

        if (_windowPools.TryGetValue(released, out var pool)
            && !pool.Retired
            && pool.View.ReuseMode == DesktopViewReuseMode.TrustedReset
            && pool.Idle.Count < pool.DesiredCapacity)
        {
            pool.Idle.Enqueue(released);
        }
        else
        {
            _windowPools.Remove(released);
            released.ClosePermanently();
            if (pool is { Retired: false })
            {
                StartReplenishment(pool);
            }
        }

        ScheduleNextPending(released.ServiceInstanceId);
    }

    private void ScheduleNextPending(string serviceInstanceId)
    {
        if (_suspendedServices.ContainsKey(serviceInstanceId)
            || !_registrations.TryGetValue(serviceInstanceId, out var registration)) return;
        var visibleIds = _windows.TryGetValue(serviceInstanceId, out var visible)
            ? visible.Where(item => item.WindowId is not null).Select(item => item.WindowId!).ToHashSet(StringComparer.Ordinal)
            : [];
        var next = _known.Where(item => string.Equals(item.Key.ServiceInstanceId, serviceInstanceId, StringComparison.Ordinal)
                                        && !visibleIds.Contains(item.Key.WindowId))
            .Select(item => item.Value)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.WindowId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (next is null || !_known.TryRemove((serviceInstanceId, next.WindowId), out _)) return;
        _ = Task.Run(() => UpsertAsync(serviceInstanceId, registration, next, _disposeCancellation.Token));
    }

    private async Task<string> GetCallbackTokenAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        CancellationToken token)
    {
        if (_callbackTokens.TryGetValue(serviceInstanceId, out var cached))
        {
            return cached;
        }

        return await RequestCallbackTokenAsync(serviceInstanceId, registration, token);
    }

    private async Task<string> RequestCallbackTokenAsync(
        string serviceInstanceId,
        AgentClientRegistration registration,
        CancellationToken token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(registration.Endpoint), "/_softwarehub/auth/token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = registration.CallbackCredentials.ClientId,
                ["client_secret"] = registration.CallbackCredentials.ClientSecret,
            }),
        };
        using var response = await _http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenEnvelope>(cancellationToken: token);
        var accessToken = payload?.AccessToken
            ?? throw new InvalidDataException("Service token endpoint did not return access_token.");
        _callbackTokens[serviceInstanceId] = accessToken;
        return accessToken;
    }

    private void ReleaseWindow(string serviceInstanceId, string windowId)
    {
        if (_windows.TryGetValue(serviceInstanceId, out var list))
        {
            list.FirstOrDefault(x => x.WindowId == windowId)?.ReleaseFromService();
        }
    }

    private Task CloseWindowsAsync(IEnumerable<DesktopWindow> windows) => _dispatcher.InvokeAsync(() =>
    {
        foreach (var window in windows)
        {
            window.ClosePermanently();
        }
    }).Task;

    public async Task CloseServiceAsync(string serviceInstanceId)
    {
        _callbackTokens.TryRemove(serviceInstanceId, out _);
        _registrations.TryRemove(serviceInstanceId, out _);
        _serviceIcons.TryRemove(serviceInstanceId, out _);
        _suspendedServices.TryRemove(serviceInstanceId, out _);
        foreach (var known in _known.Where(x => string.Equals(
                     x.Key.ServiceInstanceId,
                     serviceInstanceId,
                     StringComparison.Ordinal)).ToArray())
        {
            _known.TryRemove(known.Key, out _);
        }

        await _dispatcher.InvokeAsync(() =>
        {
            if (_pagePools.Remove(serviceInstanceId, out var pools))
            {
                foreach (var pool in pools.Values)
                {
                    RetirePool(pool);
                }
            }

            if (_windows.Remove(serviceInstanceId, out var active))
            {
                foreach (var item in OrderChildrenFirst(active))
                {
                    _windowPools.Remove(item);
                    item.ClosePermanently();
                }
            }

            _windowRegistry.ClearService(serviceInstanceId);
        });
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCancellation.Cancel();
        await _dispatcher.InvokeAsync(() =>
        {
            foreach (var item in _windowPools.Keys.ToArray())
            {
                item.ClosePermanently();
            }

            _pagePools.Clear();
            _registrations.Clear();
            _serviceIcons.Clear();
            _suspendedServices.Clear();
            _windowPools.Clear();
            _windows.Clear();
        });
        _http.Dispose();
        _disposeCancellation.Dispose();
    }

    private static DesktopWindow[] OrderChildrenFirst(IEnumerable<DesktopWindow> windows) =>
        windows.OrderByDescending(GetOwnerDepth).ToArray();

    private static int GetOwnerDepth(DesktopWindow window)
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

    private sealed class IconResolutionContext
    {
        public required string ServiceInstanceId { get; init; }
        public required AgentClientRegistration Registration { get; init; }
        public required string AccessToken { get; init; }
        public required Dictionary<string, ImageSource> Cache { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }
}
