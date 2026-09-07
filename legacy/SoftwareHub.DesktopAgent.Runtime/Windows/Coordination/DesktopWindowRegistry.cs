using Serilog;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>当前进程内 PageWindow 与 BrowserWindow 共用的 Owner 森林。</summary>
internal sealed class DesktopWindowRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(string ServiceId, string WindowId), Node> _windows = [];
    private readonly Dictionary<string, CloseBatch> _closeBatches = new(StringComparer.Ordinal);

    public void Register(
        string serviceId,
        string windowId,
        DesktopWindowOwner window,
        string? ownerWindowId,
        bool modal)
    {
        lock (_gate)
        {
            var key = (serviceId, windowId);
            if (_windows.ContainsKey(key))
                throw new InvalidOperationException($"Desktop 窗口 {windowId} 已存在。");

            var owner = ResolveOwner(serviceId, windowId, ownerWindowId);
            var node = new Node(serviceId, window, ownerWindowId, modal);
            _windows.Add(key, node);
            try
            {
                Bind(owner, node);
            }
            catch
            {
                _windows.Remove(key);
                throw;
            }

            Log.Information(
                "DesktopAgentOwnerNodeRegistered for {ServiceInstanceId} {WindowKind} {WindowId}: OwnerWindowId={OwnerWindowId}, Modal={Modal}",
                serviceId, window.WindowKind, windowId, ownerWindowId, modal);
        }
    }

    public void Rebind(string serviceId, string windowId, string? ownerWindowId, bool modal)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue((serviceId, windowId), out var node))
                throw new InvalidOperationException($"Desktop 窗口 {windowId} 不存在。");
            if (string.Equals(node.OwnerWindowId, ownerWindowId, StringComparison.Ordinal)
                && node.Modal == modal) return;

            var owner = ResolveOwner(serviceId, windowId, ownerWindowId);
            EnsureAcyclic(serviceId, windowId, ownerWindowId);
            Unbind(node);
            node.OwnerWindowId = ownerWindowId;
            node.Modal = modal;
            Bind(owner, node);
            Log.Information(
                "DesktopAgentOwnerEdgeRebound for {ServiceInstanceId} {WindowId}: OwnerWindowId={OwnerWindowId}, Modal={Modal}",
                serviceId, windowId, ownerWindowId, modal);
        }
    }

    public bool TryGet(string serviceId, string windowId, out DesktopWindowOwner owner) =>
        TryGetCore(serviceId, windowId, out owner);

    public string? GetOwnerWindowId(string serviceId, string windowId)
    {
        lock (_gate)
        {
            return _windows.TryGetValue((serviceId, windowId), out var node)
                ? node.OwnerWindowId
                : throw new InvalidOperationException($"Desktop 窗口 {windowId} 不存在。");
        }
    }

    private bool TryGetCore(string serviceId, string windowId, out DesktopWindowOwner owner)
    {
        lock (_gate)
        {
            if (_windows.TryGetValue((serviceId, windowId), out var node))
            {
                owner = node.Window;
                return true;
            }
            owner = null!;
            return false;
        }
    }

    /// <summary>
    /// 解除节点与直接 Owner 的关系，并返回应在子窗口隐藏后、Owner 解绑前执行的前台恢复动作。
    /// </summary>
    public Action Remove(string serviceId, string windowId)
    {
        lock (_gate)
        {
            if (!_windows.TryGetValue((serviceId, windowId), out var node)) return static () => { };
            var batched = _closeBatches.TryGetValue(serviceId, out var batch)
                          && batch.ClosingWindowIds.Contains(windowId);
            var restore = batched ? null : CreateSingleRestore(node);
            _windows.Remove((serviceId, windowId));
            Unbind(node);
            // Owner 关闭应由权威状态按叶到根驱动。若传输暂时乱序，保留子节点的
            // OwnerWindowId，以便后续快照重建；绝不提升或重新挂接到祖先。
            Log.Information(
                "DesktopAgentOwnerNodeRemoved for {ServiceInstanceId} {WindowKind} {WindowId}: ChildCount={ChildCount}",
                serviceId, node.Window.WindowKind, windowId, node.Children.Count);
            return restore is null ? static () => { } : () => RestoreForeground(restore);
        }
    }

    /// <summary>在叶到根的级联关闭期间抑制逐窗口激活，并在批次完成后只恢复一次。</summary>
    public IDisposable BeginCloseBatch(string serviceId, IEnumerable<string> closingWindowIds)
    {
        lock (_gate)
        {
            if (_closeBatches.ContainsKey(serviceId))
                throw new InvalidOperationException($"服务 {serviceId} 的 Desktop 窗口关闭批次不能嵌套。");
            var ids = closingWindowIds.ToHashSet(StringComparer.Ordinal);
            var batch = new CloseBatch(this, serviceId, ids, CreateBatchRestore(serviceId, ids));
            _closeBatches.Add(serviceId, batch);
            return batch;
        }
    }

    public void ClearService(string serviceId)
    {
        lock (_gate)
        {
            foreach (var key in _windows.Keys.Where(key => key.ServiceId == serviceId).ToArray())
            {
                if (_windows.Remove(key, out var node)) Unbind(node);
            }
        }
    }

    private Node? ResolveOwner(string serviceId, string windowId, string? ownerWindowId)
    {
        if (ownerWindowId is null) return null;
        if (string.Equals(windowId, ownerWindowId, StringComparison.Ordinal))
            throw new InvalidOperationException("Desktop 窗口不能拥有自身。");
        if (!_windows.TryGetValue((serviceId, ownerWindowId), out var owner))
            throw new InvalidOperationException($"Desktop 父窗口 {ownerWindowId} 不存在。");
        EnsureAcyclic(serviceId, windowId, ownerWindowId);
        return owner;
    }

    private void EnsureAcyclic(string serviceId, string windowId, string? ownerWindowId)
    {
        var cursor = ownerWindowId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (cursor is not null)
        {
            if (!visited.Add(cursor) || string.Equals(cursor, windowId, StringComparison.Ordinal))
                throw new InvalidOperationException("Desktop 父子窗口关系存在循环。");
            cursor = _windows.TryGetValue((serviceId, cursor), out var node) ? node.OwnerWindowId : null;
        }
    }

    private static void Bind(Node? owner, Node child)
    {
        if (owner is null) return;
        owner.Children.Add(child.Window.WindowId);
        if (child.Modal) owner.Window.Block();
        Log.Information(
            "DesktopAgentOwnerEdgeBound: OwnerWindowId={OwnerWindowId}, ChildWindowId={ChildWindowId}, Modal={Modal}",
            owner.Window.WindowId, child.Window.WindowId, child.Modal);
    }

    private void Unbind(Node child)
    {
        if (child.OwnerWindowId is null) return;
        _windows.TryGetValue((child.ServiceId, child.OwnerWindowId), out var owner);
        if (owner is null) return;
        owner.Children.Remove(child.Window.WindowId);
        if (child.Modal) owner.Window.Release();
        Log.Information(
            "DesktopAgentOwnerEdgeUnbound: OwnerWindowId={OwnerWindowId}, ChildWindowId={ChildWindowId}, Modal={Modal}",
            owner.Window.WindowId, child.Window.WindowId, child.Modal);
    }

    private ForegroundRestore? CreateSingleRestore(Node closing)
    {
        if (closing.OwnerWindowId is null
            || !_windows.TryGetValue((closing.ServiceId, closing.OwnerWindowId), out var owner)
            || !IsForegroundInTree(closing)) return null;
        return new ForegroundRestore(owner);
    }

    private ForegroundRestore? CreateBatchRestore(string serviceId, HashSet<string> closingIds)
    {
        var foreground = NativeWindowMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;
        var foregroundNode = _windows.Values.FirstOrDefault(node => node.Window.Handle == foreground);
        if (foregroundNode is null || !string.Equals(foregroundNode.ServiceId, serviceId, StringComparison.Ordinal))
            return null;

        var foregroundRoot = GetRoot(foregroundNode);
        Node? nearest = null;
        var nearestDepth = -1;
        foreach (var windowId in closingIds)
        {
            if (!_windows.TryGetValue((serviceId, windowId), out var closing)
                || GetRoot(closing) != foregroundRoot) continue;
            var cursorId = closing.OwnerWindowId;
            while (cursorId is not null && closingIds.Contains(cursorId))
                cursorId = _windows.TryGetValue((serviceId, cursorId), out var cursor) ? cursor.OwnerWindowId : null;
            if (cursorId is null || !_windows.TryGetValue((serviceId, cursorId), out var candidate)) continue;
            var depth = GetDepth(candidate);
            if (depth > nearestDepth)
            {
                nearest = candidate;
                nearestDepth = depth;
            }
        }
        return nearest is null ? null : new ForegroundRestore(nearest);
    }

    private bool IsForegroundInTree(Node node)
    {
        var foreground = NativeWindowMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        var root = GetRoot(node);
        return _windows.Values.Any(candidate =>
            GetRoot(candidate) == root && candidate.Window.Handle == foreground);
    }

    private Node GetRoot(Node node)
    {
        var cursor = node;
        while (cursor.OwnerWindowId is { } ownerId
               && _windows.TryGetValue((cursor.ServiceId, ownerId), out var owner))
            cursor = owner;
        return cursor;
    }

    private Node? FindNodeByHandle(IntPtr handle) =>
        _windows.Values.FirstOrDefault(node => node.Window.Handle == handle);

    private int GetDepth(Node node)
    {
        var depth = 0;
        var cursor = node;
        while (cursor.OwnerWindowId is { } ownerId
               && _windows.TryGetValue((cursor.ServiceId, ownerId), out var owner))
        {
            depth++;
            cursor = owner;
        }
        return depth;
    }

    private void RestoreForeground(ForegroundRestore restore)
    {
        lock (_gate)
        {
            var target = SelectForegroundTarget(restore.Owner);
            if (target == IntPtr.Zero) return;
            _ = NativeWindowMethods.SetForegroundWindow(target);
            Log.Information(
                "DesktopAgentOwnerTreeForegroundRestored for {ServiceInstanceId} {WindowId}: Hwnd={Hwnd}",
                restore.Owner.ServiceId, restore.Owner.Window.WindowId, target);
        }
    }

    private IntPtr SelectForegroundTarget(Node directOwner)
    {
        var ownerHandle = directOwner.Window.Handle;
        if (IsEligible(ownerHandle)) return ownerHandle;

        var popupHandle = NativeWindowMethods.GetLastActivePopup(ownerHandle);
        var popup = FindNodeByHandle(popupHandle);
        var root = GetRoot(directOwner);
        if (popup is not null && GetRoot(popup) == root && IsEligible(popupHandle)) return popupHandle;

        return _windows.Values
            .Where(node => node != directOwner && GetRoot(node) == root && IsEligible(node.Window.Handle))
            .OrderByDescending(GetDepth)
            .Select(node => node.Window.Handle)
            .FirstOrDefault();
    }

    private static bool IsEligible(IntPtr handle) =>
        handle != IntPtr.Zero
        && NativeWindowMethods.IsWindow(handle)
        && NativeWindowMethods.IsWindowVisible(handle)
        && NativeWindowMethods.IsWindowEnabled(handle);

    private void CompleteBatch(CloseBatch batch)
    {
        ForegroundRestore? restore;
        lock (_gate)
        {
            if (!_closeBatches.TryGetValue(batch.ServiceId, out var active)
                || !ReferenceEquals(active, batch)) return;
            _closeBatches.Remove(batch.ServiceId);
            restore = batch.Restore;
        }
        if (restore is not null) RestoreForeground(restore);
    }

    private sealed record ForegroundRestore(Node Owner);

    private sealed class CloseBatch(
        DesktopWindowRegistry registry,
        string serviceId,
        HashSet<string> closingWindowIds,
        ForegroundRestore? restore) : IDisposable
    {
        private int _disposed;
        public string ServiceId { get; } = serviceId;
        public HashSet<string> ClosingWindowIds { get; } = closingWindowIds;
        public ForegroundRestore? Restore { get; } = restore;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) registry.CompleteBatch(this);
        }
    }

    private sealed class Node(string serviceId, DesktopWindowOwner window, string? ownerWindowId, bool modal)
    {
        public string ServiceId { get; } = serviceId;
        public DesktopWindowOwner Window { get; } = window;
        public string? OwnerWindowId { get; set; } = ownerWindowId;
        public bool Modal { get; set; } = modal;
        public HashSet<string> Children { get; } = new(StringComparer.Ordinal);
    }
}
