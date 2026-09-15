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
        // 纵向以客户区顶边为基准：绘制用的是客户区坐标，最大化时客户区顶边比窗口顶边低
        // frameY（那条不可见缩放带），从窗口顶边起算会让命中区整体上移 8px
        var clientTop = ChromeFrameGeometry.GetClientArea(
            windowRect,
            Metrics.FrameX,
            Metrics.FrameY,
            Metrics.Maximized).Top;
        foreach (var (button, rect) in GetCaptionButtonRects(windowRect, clientTop))
        {
            if (ChromeFrameGeometry.Contains(rect, pointer))
                return ToHitTest(button);
        }
        return 0;
    }

    /// <summary>标题栏三个按钮的命中矩形（窗口坐标）。从右往左依次排列，各自宽度可以不同。</summary>
    private IEnumerable<(ChromeCaptionButton Button, NativeRectangle Rect)> GetCaptionButtonRects(
        NativeRectangle windowRect,
        int clientTop)
    {
        var offset = 0;
        foreach (var button in new[]
                 {
                     ChromeCaptionButton.Close,
                     ChromeCaptionButton.Maximize,
                     ChromeCaptionButton.Minimize,
                 })
        {
            var width = Metrics.GetCaptionButtonWidth(button);
            yield return (
                button,
                ChromeFrameGeometry.GetCaptionButtonRect(
                    windowRect, clientTop, Metrics.FrameX, width,
                    Metrics.CaptionHitHeight, Metrics.CaptionButtonTop, offset));
            offset += width;
        }
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
        // wParam 里的 hit code 是**系统在某一刻**算出来的，可能早于最近一次几何变化
        // （最大化/还原会改按钮顶边与客户区顶边）。如果那条消息排在 WM_SIZE 之后到达，
        // 直接采信它就会点亮一个指针其实已经不在的格子，而指针随后不再移动，
        // 于是悬停一直留着 —— 这就是"点最大化后按钮偶尔一直亮着"的成因。
        // 所以这里用当前几何复核一次：复核结果才算数。
        if (button is not null && ButtonAtCursor() != button)
            button = null;

        if (button == _hotButton)
            return;
        _hotButton = button;
        InvalidateCaption();
    }

    /// <summary>
    /// 几何变化后（最大化/还原、DPI、尺寸）按**当前指针位置**重新判定悬停。
    ///
    /// 为什么必须做：切换最大化后按钮的顶边与客户区顶边都变了，指针虽然没动，
    /// 它所在的格子却可能变了。此前只有 <c>WM_CAPTURECHANGED</c> 那条路径会清状态
    /// （依赖 <c>ReleaseCapture</c> 确实发出该消息），一旦那条路径没走到，
    /// 残留的悬停就没人清 —— 表现为"鼠标不在按钮上，按钮却一直亮着"。
    /// 这里主动重算，不依赖捕获消息的时序。
    /// </summary>
    private void RevalidateHoverAgainstCurrentGeometry()
    {
        // 构造期也会走到这里（UpdateFrameMetrics 在构造函数里被调用）。此时还没有窗口，
        // 既没有"指针在哪个格子"可言，也不能让 ButtonAtCursor 去碰 Control.Handle ——
        // 读 Handle 会**强制创建窗口句柄**，那会改变构造期的既有行为。
        if (!IsHandleCreated)
            return;
        // 按下期间不重算：那时指针位置由捕获驱动，且按钮可能因几何变化而移出指针
        // （原生行为是按住不放、移动出按钮就取消高亮，这一层由 UpdatePressedButtonHover 处理）。
        if (_pressedButton is not null)
            return;
        var button = ButtonAtCursor();
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
        if (hit == ChromeFrameGeometry.HtSysMenu)
            return ShowSystemMenu();
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

    /// <summary>
    /// 自己弹出系统菜单。命中盒子是自绘的（以图标为中心），而系统内部那句
    /// "只在系统菜单盒子里按下才弹菜单"用的是 SM_CXSMSIZE 贴在标题栏最左的盒子，
    /// 直接交给 DefWindowProc 会让命中盒子右侧（图标右半边）点了毫无反应。
    /// </summary>
    private bool ShowSystemMenu()
    {
        var menu = NativeMethods.GetSystemMenu(Handle, false);
        if (menu == IntPtr.Zero || !NativeMethods.GetCursorPos(out var point))
            return true;
        var command = NativeMethods.TrackPopupMenuEx(
            menu,
            NativeMethods.TpmLeftAlign | NativeMethods.TpmLeftButton | NativeMethods.TpmReturnCmd,
            point.X,
            point.Y,
            Handle,
            IntPtr.Zero);
        if (command != 0)
            _ = NativeMethods.SendMessage(Handle, NativeMethods.WmSysCommand, new IntPtr(command), IntPtr.Zero);
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
        // 与命中测试用同一套矩形（纵向同样以客户区顶边为基准），否则最大化时按下/抬起会落在不同格子里
        var clientTop = ChromeFrameGeometry.GetClientArea(
            windowRect,
            Metrics.FrameX,
            Metrics.FrameY,
            Metrics.Maximized).Top;
        foreach (var (button, rect) in GetCaptionButtonRects(windowRect, clientTop))
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
