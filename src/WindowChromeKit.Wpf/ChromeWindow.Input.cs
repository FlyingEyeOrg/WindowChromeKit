using System.Windows;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    /// <summary>在 WPF 视觉树上做屏幕点命中测试，返回语义角色；
    /// 原生消息路由由 <see cref="ChromeInputController"/> 负责。</summary>
    internal ChromeHitTestRole GetRoleAtPoint(NativePoint pointer)
    {
        var clientPoint = PointFromScreen(new Point(pointer.X, pointer.Y));
        if (InputHitTest(clientPoint) is DependencyObject hit)
        {
            var role = GetHitTestRole(hit);
            if (role != ChromeHitTestRole.Default)
                return role;
        }
        if (_close is not null && Contains(GetScreenBounds(_close), pointer))
            return ChromeHitTestRole.CloseButton;
        if (_maximize is not null && Contains(GetScreenBounds(_maximize), pointer))
            return ChromeHitTestRole.MaximizeButton;
        if (_minimize is not null && Contains(GetScreenBounds(_minimize), pointer))
            return ChromeHitTestRole.MinimizeButton;
        if (_systemMenu is not null && Contains(GetScreenBounds(_systemMenu), pointer))
            return ChromeHitTestRole.SystemMenu;
        if (_titleBar is not null && Contains(GetScreenBounds(_titleBar), pointer))
            return ChromeHitTestRole.Caption;
        return ChromeHitTestRole.Client;
    }

    internal bool IsResizable => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    internal bool TryExecuteCaptionButton(
        ChromeHitTestRole pressedRole,
        ChromeHitTestRole releasedRole
    )
    {
        if (pressedRole != releasedRole || !IsRoleEnabled(pressedRole))
            return false;
        switch (pressedRole)
        {
            case ChromeHitTestRole.MinimizeButton:
                SystemCommands.MinimizeWindow(this);
                return true;
            case ChromeHitTestRole.MaximizeButton:
                if (WindowState == WindowState.Maximized)
                    SystemCommands.RestoreWindow(this);
                else
                    SystemCommands.MaximizeWindow(this);
                return true;
            case ChromeHitTestRole.CloseButton:
                SystemCommands.CloseWindow(this);
                return true;
            default:
                return false;
        }
    }

    internal bool IsRoleEnabled(ChromeHitTestRole role) =>
        role switch
        {
            ChromeHitTestRole.MinimizeButton => ResizeMode != ResizeMode.NoResize,
            ChromeHitTestRole.MaximizeButton => IsResizable,
            ChromeHitTestRole.CloseButton => true,
            _ => false,
        };

    internal int VisibleCaptionButtonCount =>
        ResizeMode switch
        {
            ResizeMode.NoResize => 1,
            _ => 3,
        };

    internal bool IsTrackingCaptionButtonPress => _input.IsTrackingCaptionButtonPress;

    internal int HitTest(NativePoint pointer) => _input.HitTest(pointer);

    internal void PrepareResizeModeChange() => _input.PrepareResizeModeChange();

    internal bool HandleNcLButtonDown(IntPtr window, int part) =>
        _input.HandleNcLButtonDown(window, part);

    internal bool HandleNcLButtonUp(int part) => _input.HandleNcLButtonUp(part);

    internal bool HandleMouseMove() => _input.HandleMouseMove();

    internal bool HandleLButtonUp() => _input.HandleLButtonUp();

    internal void HandleCaptureChanged() => _input.HandleCaptureChanged();

    internal void ObserveNonClientMove(int part) => _input.ObserveNonClientMove(part);

    internal void CancelCaptionButtonPress() => _input.CancelCaptionButtonPress();

    internal void ResetPointerVisualState() => _input.ResetPointerVisualState();

    private static bool Contains(NativeRectangle bounds, NativePoint point) =>
        point.X >= bounds.Left
        && point.X < bounds.Right
        && point.Y >= bounds.Top
        && point.Y < bounds.Bottom;

    private static NativeRectangle GetScreenBounds(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return default;
        var origin = element.PointToScreen(new Point());
        var opposite = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new NativeRectangle(
            (int)Math.Floor(origin.X),
            (int)Math.Floor(origin.Y),
            (int)Math.Ceiling(opposite.X),
            (int)Math.Ceiling(opposite.Y)
        );
    }
}
