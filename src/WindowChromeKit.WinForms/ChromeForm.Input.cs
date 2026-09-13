using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 标题栏按钮的命中、悬停与按压状态机。原生 frame（客户区内缩、缩放带、标题栏/客户区判定）
/// 由 <see cref="ChromeFrame"/> 负责，这里只补标题栏按钮这一层。
/// </summary>
public partial class ChromeForm
{
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;

    /// <summary>标题栏三个按钮的命中（窗口坐标）；没有命中时返回 0。</summary>
    private protected override int HitTestCaptionButtons(NativeRectangle windowRect, NativePoint pointer)
    {
        foreach (var (button, rect) in GetCaptionButtonRects(windowRect))
        {
            if (ChromeFrameGeometry.Contains(rect, pointer))
                return ToHitTest(button);
        }
        return 0;
    }

    /// <summary>标题栏三个按钮的命中矩形（窗口坐标）。顺序与绘制顺序一致。</summary>
    private IEnumerable<(ChromeCaptionButton Button, NativeRectangle Rect)> GetCaptionButtonRects(
        NativeRectangle windowRect)
    {
        yield return (
            ChromeCaptionButton.Close,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, Metrics.FrameX, Metrics.CaptionButtonWidth,
                Metrics.CaptionButtonHeight, Metrics.CaptionButtonTop, ChromeCaptionButton.Close));
        yield return (
            ChromeCaptionButton.Maximize,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, Metrics.FrameX, Metrics.CaptionButtonWidth,
                Metrics.CaptionButtonHeight, Metrics.CaptionButtonTop, ChromeCaptionButton.Maximize));
        yield return (
            ChromeCaptionButton.Minimize,
            ChromeFrameGeometry.GetCaptionButtonRect(
                windowRect, Metrics.FrameX, Metrics.CaptionButtonWidth,
                Metrics.CaptionButtonHeight, Metrics.CaptionButtonTop, ChromeCaptionButton.Minimize));
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

    private void HandleCaptionMouseMove(int hit)
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

    private void HandleCaptionMouseLeave()
    {
        _trackingNonClientMouse = false;
        if (_pressedButton is not null || _hotButton is null)
            return;
        _hotButton = null;
        InvalidateCaption();
    }

    /// <summary>按下标题栏按钮时接管鼠标捕获，抬起时仍在同一按钮上才执行（与原生按钮一致）。</summary>
    private bool HandleCaptionButtonDown(int hit)
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

    private bool HandleCaptionButtonUp()
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

    /// <summary>客户区鼠标移动：跟踪按下状态的移入/移出，并在指针离开按钮时清掉悬停。</summary>
    private bool HandleCaptionMouseMoveInClient()
    {
        if (_pressedButton is not null)
        {
            UpdatePressedButtonHover();
            return true;
        }
        ClearHotButtonWhenPointerLeftButtons();
        return false;
    }

    private void UpdatePressedButtonHover()
    {
        var button = ButtonAtCursor();
        if (button == _hotButton)
            return;
        _hotButton = button;
        InvalidateCaption();
    }

    /// <summary>客户区鼠标移动时清掉标题栏按钮的悬停状态（例如移到标题栏里的自定义控件上）。</summary>
    private void ClearHotButtonWhenPointerLeftButtons()
    {
        if (_hotButton is null || ButtonAtCursor() is not null)
            return;
        _hotButton = null;
        _trackingNonClientMouse = false;
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
