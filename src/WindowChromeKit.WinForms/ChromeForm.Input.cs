using System.Runtime.InteropServices;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>命中测试与标题栏按钮的悬停/按压状态机。优先级与真实 Chrome 实测一致。</summary>
public partial class ChromeForm
{
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;

    /// <summary>命中优先级：窗口矩形之外 → 标题栏按钮 → 三边与四角 → 顶部带 → 标题栏/客户区。</summary>
    private int HitTestAt(NativePoint pointer)
    {
        var windowRect = GetWindowRectangle();
        // Chrome 实测：窗口矩形之外的任何点都返回 HTNOWHERE，绝不声明别人的像素
        if (!ChromeFrameGeometry.Contains(windowRect, pointer))
            return ChromeFrameGeometry.HtNowhere;

        // 1) 标题栏按钮优先（也压过顶部缩放带；最大化时按钮依旧可命中）
        foreach (var (button, rect) in GetCaptionButtonRects(windowRect))
        {
            if (ChromeFrameGeometry.Contains(rect, pointer))
                return ToHitTest(button);
        }

        // 2) 左/右/下三边与四角：正好是 frame 区域，普通态下它在窗口矩形内、可见窗口外，
        //    视觉上就是"阴影里那一圈"；顶部带由同一函数按较窄的 topBand 判定
        if (IsResizable)
        {
            var hit = ChromeFrameGeometry.EvaluateResizeHit(
                pointer,
                windowRect,
                _metrics.FrameX,
                _metrics.FrameY,
                _metrics.TopResizeBand);
            // 最大化时纵向不可缩放，顶部整块交给标题栏（可拖动还原）
            if (hit == ChromeFrameGeometry.HtTop && _metrics.Maximized)
                hit = ChromeFrameGeometry.HtClient;
            if (hit != ChromeFrameGeometry.HtClient)
                return hit;
        }

        // 3) 标题栏：图标区给系统菜单，其余可拖动；标题栏之下是客户区
        var captionTop = windowRect.Top + (_metrics.Maximized ? _metrics.FrameY : 0);
        if (pointer.Y < captionTop + _metrics.CaptionHeight)
        {
            if (ShowTitleBarIcon
                && pointer.X < windowRect.Left + _metrics.FrameX + _metrics.IconMargin + _metrics.IconSize)
            {
                return ChromeFrameGeometry.HtSysMenu;
            }
            return ChromeFrameGeometry.HtCaption;
        }
        return ChromeFrameGeometry.HtClient;
    }

    /// <summary>窗口矩形（含 frame，即"阴影里那圈"的外边界）。</summary>
    private NativeRectangle GetWindowRectangle() =>
        NativeMethods.GetWindowRect(Handle, out var rectangle)
            ? rectangle
            : new NativeRectangle(Left, Top, Right, Bottom);

    /// <summary>标题栏三个按钮的命中矩形（窗口坐标）。顺序与绘制顺序一致。</summary>
    private IEnumerable<(ChromeCaptionButton Button, NativeRectangle Rect)> GetCaptionButtonRects(
        NativeRectangle windowRect)
    {
        yield return (
            ChromeCaptionButton.Close,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, _metrics.FrameX, _metrics.CaptionButtonWidth,
                _metrics.CaptionButtonHeight, _metrics.CaptionButtonTop, ChromeCaptionButton.Close));
        yield return (
            ChromeCaptionButton.Maximize,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, _metrics.FrameX, _metrics.CaptionButtonWidth,
                _metrics.CaptionButtonHeight, _metrics.CaptionButtonTop, ChromeCaptionButton.Maximize));
        yield return (
            ChromeCaptionButton.Minimize,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, _metrics.FrameX, _metrics.CaptionButtonWidth,
                _metrics.CaptionButtonHeight, _metrics.CaptionButtonTop, ChromeCaptionButton.Minimize));
    }

    private static int ToHitTest(ChromeCaptionButton button) => button switch
    {
        ChromeCaptionButton.Minimize => ChromeFrameGeometry.HtMinButton,
        ChromeCaptionButton.Maximize => ChromeFrameGeometry.HtMaxButton,
        _ => ChromeFrameGeometry.HtClose,
    };

    private static ChromeCaptionButton? FromHitTest(int hit) => hit switch
    {
        ChromeFrameGeometry.HtMinButton => ChromeCaptionButton.Minimize,
        ChromeFrameGeometry.HtMaxButton => ChromeCaptionButton.Maximize,
        ChromeFrameGeometry.HtClose => ChromeCaptionButton.Close,
        _ => null,
    };

    private void HandleNonClientMouseMove(int hit)
    {
        if (!_trackingNonClientMouse)
        {
            var track = new NativeTrackMouseEvent
            {
                Size = (uint)Marshal.SizeOf<NativeTrackMouseEvent>(),
                Flags = TmeLeave | TmeNonClient,
                Window = Handle,
            };
            _trackingNonClientMouse = NativeMethods.TrackMouseEvent(ref track);
        }
        var button = FromHitTest(hit);
        if (button == _hotButton)
            return;
        _hotButton = button;
        InvalidateCaption();
    }

    private void HandleNonClientMouseLeave()
    {
        _trackingNonClientMouse = false;
        if (_pressedButton is not null || _hotButton is null)
            return;
        _hotButton = null;
        InvalidateCaption();
    }

    /// <summary>按下标题栏按钮时接管鼠标捕获，抬起时仍在同一按钮上才执行（与原生按钮一致）。</summary>
    private bool HandleNonClientButtonDown(int hit)
    {
        var button = FromHitTest(hit);
        if (button is null)
            return false;
        _hotButton = button;
        _pressedButton = IsCaptionButtonEnabled(button.Value) ? button : null;
        if (_pressedButton is not null)
            _ = NativeMethods.SetCapture(Handle);
        InvalidateCaption();
        return true;
    }

    private bool HandleButtonUp()
    {
        if (_pressedButton is null)
            return false;
        var pressed = _pressedButton.Value;
        var released = ButtonAtCursor();
        _pressedButton = null;
        _hotButton = released;
        _ = NativeMethods.ReleaseCapture();
        InvalidateCaption();
        if (released == pressed)
            ExecuteCaptionButton(pressed);
        return true;
    }

    private void UpdatePressedButtonHover()
    {
        var button = ButtonAtCursor();
        if (button == _hotButton)
            return;
        _hotButton = button;
        InvalidateCaption();
    }

    private void ResetPointerState()
    {
        if (_pressedButton is null && _hotButton is null)
            return;
        _pressedButton = null;
        _hotButton = null;
        InvalidateCaption();
    }

    private ChromeCaptionButton? ButtonAtCursor()
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return null;
        var windowRect = GetWindowRectangle();
        foreach (var (button, rect) in GetCaptionButtonRects(windowRect))
        {
            if (ChromeFrameGeometry.Contains(rect, point))
                return button;
        }
        return null;
    }

    /// <summary>判断某个标题栏按钮当前是否可用（默认跟随 <see cref="Form.MinimizeBox"/> 等属性）。</summary>
    protected virtual bool IsCaptionButtonEnabled(ChromeCaptionButton button) => button switch
    {
        ChromeCaptionButton.Minimize => MinimizeBox,
        ChromeCaptionButton.Maximize => MaximizeBox,
        _ => ControlBox,
    };

    /// <summary>执行标题栏按钮命令；派生类可覆写以接入自己的命令。</summary>
    protected virtual void ExecuteCaptionButton(ChromeCaptionButton button)
    {
        switch (button)
        {
            case ChromeCaptionButton.Minimize:
                WindowState = FormWindowState.Minimized;
                break;
            case ChromeCaptionButton.Maximize:
                WindowState = WindowState == FormWindowState.Maximized
                    ? FormWindowState.Normal
                    : FormWindowState.Maximized;
                break;
            case ChromeCaptionButton.Close:
                Close();
                break;
        }
    }
}
