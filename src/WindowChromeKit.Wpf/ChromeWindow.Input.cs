using System.Windows;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    /// <summary>在 WPF 视觉树上做屏幕点命中测试，返回语义角色。</summary>
    protected override ChromeHitTestRole HitTestFrame(Point screenPoint)
    {
        var clientPoint = PointFromScreen(screenPoint);
        if (InputHitTest(clientPoint) is DependencyObject hit)
        {
            var role = GetHitTestRole(hit);
            if (role != ChromeHitTestRole.Default)
                return role;
        }
        var pointer = new NativePoint((int)screenPoint.X, (int)screenPoint.Y);
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

    internal override bool TryExecuteCaptionButton(
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

    /// <summary>
    /// 标题栏按钮当前是否可用。这是**唯一**判定来源：命中层（<c>RoleToNativePart</c>）
    /// 与视觉层（<c>IsRoleEnabled</c>）都必须读它。
    ///
    /// 曾经两处不同源：视觉层用 <see cref="ChromeFrame.IsResizable"/>，命中层用
    /// <c>ResizeMode != NoResize</c>。于是 <c>CanMinimize</c> 下最大化按钮被置灰，
    /// 命中层却仍把它报成 <c>HTMAXBUTTON</c> —— 外观和命中互相矛盾。
    /// </summary>
    protected override bool IsRoleEnabled(ChromeHitTestRole role) => role switch
    {
        ChromeHitTestRole.MinimizeButton => ResizeMode != ResizeMode.NoResize,
        ChromeHitTestRole.MaximizeButton => IsResizable,
        ChromeHitTestRole.CloseButton => true,
        _ => false,
    };

    internal int VisibleCaptionButtonCount => ResizeMode switch
    {
        ResizeMode.NoResize => 1,
        _ => 3,
    };

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
