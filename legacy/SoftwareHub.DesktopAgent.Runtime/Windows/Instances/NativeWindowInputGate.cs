using Serilog;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>
/// 只切换顶层 HWND 的输入状态，使 WPF/WebView2 可视树始终保持启用和渲染。
/// </summary>
internal sealed class NativeWindowInputGate
{
    private readonly string _windowKind;
    private readonly Func<string?> _windowId;
    private readonly Func<IntPtr> _handle;
    private readonly Func<IntPtr, bool> _isWindowEnabled;
    private readonly Action<IntPtr, bool> _enableWindow;
    private readonly ModalWindowBlockState _modal = new();
    private bool _resultPending;

    public NativeWindowInputGate(
        string windowKind,
        Func<string?> windowId,
        Func<IntPtr> handle,
        Func<IntPtr, bool>? isWindowEnabled = null,
        Action<IntPtr, bool>? enableWindow = null)
    {
        _windowKind = windowKind;
        _windowId = windowId;
        _handle = handle;
        _isWindowEnabled = isWindowEnabled ?? NativeWindowMethods.IsWindowEnabled;
        _enableWindow = enableWindow ?? ((windowHandle, enabled) =>
            _ = NativeWindowMethods.EnableWindow(windowHandle, enabled));
    }

    public int ModalReferenceCount => _modal.Count;

    public bool IsModalBlocked => _modal.IsBlocked;

    public bool IsResultPending => _resultPending;

    public bool IsInputBlocked => IsModalBlocked || IsResultPending;

    public void AddModalReference()
    {
        var wasBlocked = IsInputBlocked;
        _modal.Add();
        ApplyAggregateTransition(wasBlocked, "ModalReferenceAdded");
    }

    public void RemoveModalReference()
    {
        var wasBlocked = IsInputBlocked;
        _modal.Remove();
        ApplyAggregateTransition(wasBlocked, "ModalReferenceRemoved");
    }

    public void SetResultPending(bool pending)
    {
        if (_resultPending == pending) return;

        var wasBlocked = IsInputBlocked;
        _resultPending = pending;
        ApplyAggregateTransition(wasBlocked, pending ? "ResultPendingEntered" : "ResultPendingExited");
    }

    public void Reset()
    {
        var wasBlocked = IsInputBlocked;
        _modal.Reset();
        _resultPending = false;
        ApplyAggregateTransition(wasBlocked, "Reset");
    }

    /// <summary>HWND 首次创建后，将尚未应用的聚合状态同步到原生窗口。</summary>
    public void Synchronize(string reason)
    {
        var windowHandle = _handle();
        if (windowHandle == IntPtr.Zero)
        {
            Log.Debug(
                "DesktopAgentNativeInputGateSkipped for {WindowKind} {WindowId}: Hwnd={Hwnd}, ModalCount={ModalCount}, ResultPending={ResultPending}, Reason={Reason}",
                _windowKind,
                _windowId(),
                windowHandle,
                ModalReferenceCount,
                IsResultPending,
                reason);
            return;
        }

        var expectedEnabled = !IsInputBlocked;
        var enabledBefore = _isWindowEnabled(windowHandle);
        var switched = enabledBefore != expectedEnabled;
        if (switched)
        {
            _enableWindow(windowHandle, expectedEnabled);
        }

        var enabledAfter = _isWindowEnabled(windowHandle);
        const string message = "DesktopAgentNativeInputGate for {WindowKind} {WindowId}: Hwnd={Hwnd}, ModalCount={ModalCount}, ResultPending={ResultPending}, ExpectedEnabled={ExpectedEnabled}, EnabledBefore={EnabledBefore}, EnabledAfter={EnabledAfter}, Switched={Switched}, Reason={Reason}";
        object?[] values =
        [
            _windowKind,
            _windowId(),
            windowHandle,
            ModalReferenceCount,
            IsResultPending,
            expectedEnabled,
            enabledBefore,
            enabledAfter,
            switched,
            reason,
        ];
        if (switched)
        {
            Log.Information(message, values);
        }
        else
        {
            Log.Debug(message, values);
        }
    }

    private void ApplyAggregateTransition(bool wasBlocked, string reason)
    {
        if (wasBlocked != IsInputBlocked)
        {
            Synchronize(reason);
        }
    }
}
