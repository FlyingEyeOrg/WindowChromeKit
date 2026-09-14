using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 原生 frame 容器：保留 <c>WS_CAPTION | WS_THICKFRAME</c>，由 DWM 提供阴影、可见边框与
/// "阴影里那圈"不可见缩放带；<c>WM_NCCALCSIZE</c> 把客户区从窗口矩形内缩出 frame，
/// <c>WM_NCHITTEST</c> 按真实 Chrome 窗口实测的优先级判定（窗口矩形之外 → <c>HTNOWHERE</c>，
/// 客户区之外的 frame 环 → 缩放值，客户区之内 → 可交互控件 / 图标区 / 标题栏 / 客户区）。
///
/// 本类不带任何默认标题栏，适合完全自绘窗口；需要 Chrome 风格默认标题栏请用
/// <see cref="ChromeForm"/>（它继承本类并补上标题栏、按钮与配色）。
/// </summary>
public partial class ChromeFrame : Form
{
    private static readonly ConditionalWeakTable<Control, RoleBox> Roles = new();
    private protected FrameMetrics Metrics = new();

    protected ChromeFrame()
    {
        SetStyle(
            ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
            true);
        DoubleBuffered = true;
        // Sizable 才会带上 WS_CAPTION | WS_THICKFRAME（阴影、Snap、系统菜单都来自它）
        FormBorderStyle = FormBorderStyle.Sizable;
        UpdateFrameMetrics();
    }

    /// <summary>当前窗体是否允许八方向缩放；为 false 时四边缩放带不再生效（frame 仍然保留）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    protected virtual bool IsResizable =>
        FormBorderStyle is FormBorderStyle.Sizable or FormBorderStyle.SizableToolWindow;

    /// <summary>客户区内标题栏所占的高度（设备像素，已按 DPI 缩放）；无标题栏时为 0。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionHeight => Metrics.CaptionHeight;

    /// <summary>内容区 = 客户区减去标题栏与 <see cref="Control.Padding"/>，停靠/锚定的子控件自动排在标题栏下方。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override Rectangle DisplayRectangle
    {
        get
        {
            var client = ClientRectangle;
            var top = client.Top + Metrics.CaptionHeight + Padding.Top;
            var width = client.Width - Padding.Horizontal;
            var height = client.Height - Metrics.CaptionHeight - Padding.Vertical;
            return new Rectangle(
                client.Left + Padding.Left,
                top,
                Math.Max(0, width),
                Math.Max(0, height));
        }
    }

    /// <summary>标题栏按钮高度（DIP）。无标题栏时返回 0。</summary>
    protected virtual int GetCaptionButtonHeightDip() => 0;

    /// <summary>标题栏图标左边距（DIP）。无标题栏时返回 0。</summary>
    protected virtual int GetCaptionIconMarginDip() => 0;

    /// <summary>顶部缩放带宽度（DIP）。实测 Chrome 为 6，明显窄于其余三边的 8。</summary>
    protected virtual int GetTopResizeBandDip() => 6;

    /// <summary>标题栏高度（DIP）。无标题栏的 frame 容器返回 0。</summary>
    protected virtual int GetCaptionHeightDip() => 0;

    /// <summary>单个标题栏按钮的宽度（DIP）。无标题栏时返回 0。</summary>
    protected virtual int GetCaptionButtonWidthDip() => 0;

    /// <summary>标题栏按钮数量，用于计算最小宽度。</summary>
    protected virtual int GetCaptionButtonCount() => 0;

    /// <summary>标题栏左侧图标区（系统菜单命中区）宽度（DIP）；没有时返回 0。</summary>
    protected virtual int GetCaptionLeadingWidthDip() => 0;

    /// <summary>
    /// 给标题栏里的自定义控件标记命中角色（与 WPF 版同名同语义）。
    /// 子控件会沿父链继承标记，所以通常只需要标记最外层的容器。
    /// </summary>
    public static void SetHitTestRole(Control control, ChromeHitTestRole role)
    {
        if (control is null)
            throw new ArgumentNullException(nameof(control));
        Roles.Remove(control);
        if (role != ChromeHitTestRole.Default)
            Roles.Add(control, new RoleBox(role));
    }

    /// <summary>读取控件自身的命中角色标记（不含父链）。</summary>
    public static ChromeHitTestRole GetHitTestRole(Control control) =>
        control is not null && Roles.TryGetValue(control, out var box) ? box.Role : ChromeHitTestRole.Default;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 与 Chrome 相同：非客户区交给 DWM 渲染（阴影、可见边框、不可见缩放带都来自它）。
        // 这里必须显式打开：WinForms 建窗后该策略不是"沿用窗口样式"，实测会被关掉。
        _ = NativeMethods.EnsureNonClientRendering(Handle);
        UpdateFrameMetrics();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ResetPointerState();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WmGetMinMaxInfo:
                // 先让 Form 按 MinimumSize 写一遍最小/最大值，再用"标题栏 + frame"兜底
                base.WndProc(ref m);
                HandleGetMinMaxInfo(ref m);
                return;
            case NativeMethods.WmSizing:
                HandleSizing(ref m);
                return;
            case NativeMethods.WmNcCalcSize:
                HandleNcCalcSize(ref m);
                return;
            case NativeMethods.WmNcHitTest:
                m.Result = new IntPtr(HitTestAt(GetScreenPoint(m.LParam)));
                return;
            case NativeMethods.WmNcMouseMove:
                OnNonClientMouseMove((int)m.WParam);
                break;
            case NativeMethods.WmNcMouseLeave:
                OnNonClientMouseLeave();
                break;
            case NativeMethods.WmNcLButtonDown:
                if (OnNonClientButtonDown((int)m.WParam))
                    return;
                break;
            case NativeMethods.WmLButtonUp:
                if (OnButtonUp())
                    return;
                break;
            case NativeMethods.WmMouseMove:
                if (OnFrameMouseMove())
                    return;
                break;
            case NativeMethods.WmCaptureChanged:
                OnCaptureChanged();
                break;
            case NativeMethods.WmSize:
                if (UpdateMaximizedState())
                    UpdateFrameMetrics();
                OnFrameSizeChanged();
                break;
            case NativeMethods.WmActivate:
                OnFrameActivated();
                break;
            case NativeMethods.WmDpiChanged:
            case NativeMethods.WmSettingChange:
            case NativeMethods.WmThemeChanged:
                UpdateFrameMetrics();
                OnFrameEnvironmentChanged();
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>鼠标进入非客户区（wParam 为命中值）；默认交给 <see cref="ChromeForm"/> 做按钮悬停。</summary>
    protected virtual void OnNonClientMouseMove(int hit) { }

    /// <summary>鼠标离开非客户区。</summary>
    protected virtual void OnNonClientMouseLeave() => ResetPointerState();

    /// <summary>非客户区左键按下；返回 true 表示已接管（不再交给默认处理）。</summary>
    protected virtual bool OnNonClientButtonDown(int hit) => false;

    /// <summary>客户区左键抬起；返回 true 表示已接管。</summary>
    protected virtual bool OnButtonUp() => false;

    /// <summary>客户区鼠标移动；返回 true 表示已接管。</summary>
    protected virtual bool OnFrameMouseMove() => false;

    /// <summary>鼠标捕获变化。</summary>
    protected virtual void OnCaptureChanged() => ResetPointerState();

    /// <summary>窗口尺寸变化，派生类可在此重新摆放标题栏内容。</summary>
    protected virtual void OnFrameSizeChanged() { }

    /// <summary>窗口激活状态变化。</summary>
    protected virtual void OnFrameActivated() => Invalidate();

    /// <summary>DPI 或系统主题变化。</summary>
    protected virtual void OnFrameEnvironmentChanged() => Invalidate();

    /// <summary>清空派生类维护的指针状态。</summary>
    protected virtual void ResetPointerState() { }

    /// <summary>窗口当前是否最大化（直接问窗口，不受 WinForms 状态更新时机影响）。</summary>
    protected bool IsMaximized => IsHandleCreated && NativeMethods.IsZoomed(Handle);

    /// <summary>把 frame 度量重算一遍（DPI、窗口状态、系统主题变化后调用）。</summary>
    protected void UpdateFrameMetrics()
    {
        var dpi = (uint)Math.Max(96, DeviceDpi);
        var (frameX, frameY) = ChromeFrameGeometry.GetFrameThickness(dpi);
        var maximized = IsMaximized;
        var (menuBoxWidth, menuBoxHeight) = ChromeFrameGeometry.GetSystemMenuBoxSize(dpi);
        var iconSize = ChromeFrameGeometry.GetSmallIconSize(dpi);
        var iconLeft = ScaleDip(GetCaptionIconMarginDip(), dpi);
        var captionHeight = ScaleDip(GetCaptionHeightDip(), dpi);
        Metrics = new FrameMetrics
        {
            Dpi = dpi,
            FrameX = frameX,
            FrameY = frameY,
            Maximized = maximized,
            CaptionHeight = captionHeight,
            CaptionButtonWidth = ScaleDip(GetCaptionButtonWidthDip(), dpi),
            CaptionButtonsWidth = ScaleDip(GetCaptionButtonWidthDip() * GetCaptionButtonCount(), dpi),
            CaptionButtonHeight = ScaleDip(GetCaptionButtonHeightDip(), dpi),
            // （顶边在下面统一算：需要同时用到标题栏与按钮高度）
            CaptionLeadingWidth = ScaleDip(GetCaptionLeadingWidthDip(), dpi),
            // 命中盒子与原生一致（22×22 正方形、竖向居中），但以画出来的图标为中心，
            // 所以图标仍留在 IconMargin（默认 12），不会因为对齐命中区而左移
            SystemMenuLeft = iconLeft - Math.Max(0, (menuBoxWidth - iconSize) / 2),
            SystemMenuWidth = menuBoxWidth,
            // 盒子在标题栏（去掉顶边线那一行）内垂直居中，随标题栏高度自适应
            SystemMenuTop = CenteredSystemMenuTop(captionHeight, menuBoxHeight),
            SystemMenuHeight = menuBoxHeight,
            IconMargin = iconLeft,
            IconSize = iconSize,
            TopResizeBand = ScaleDip(GetTopResizeBandDip(), dpi),
            // 按钮顶边：贴标题栏底部（保留 1px 下边距），但不高于第 1 行 ——
            // Chrome 实测按钮顶到第 1 行（40 高 / 39 按钮），原生窄标题栏（31 高 / 22 按钮）
            // 则落在第 8 行，即 frame 内缩那条 content 线上
            CaptionButtonTop = ScaleDip(GetCaptionButtonHeightDip(), dpi) >= captionHeight
                ? 0
                : Math.Max(1, captionHeight - 1 - ScaleDip(GetCaptionButtonHeightDip(), dpi)),
            // 绘制顶行：普通态比命中顶行少 1 行（第 0 行留给顶边线），最大化时相同。
            // 绘制与命中都相对**客户区**顶边，两者只在普通态差这 1 行。
            CaptionButtonPaintTop = maximized
                ? CaptionButtonTopOf(dpi, captionHeight)
                : Math.Max(0, CaptionButtonTopOf(dpi, captionHeight) - 1),
        };
        OnFrameMetricsUpdated();
    }

    /// <summary>度量重算完成后的钩子（派生类据此更新自己的标题栏度量）。</summary>
    protected virtual void OnFrameMetricsUpdated() { }

    internal static int ScaleDip(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    /// <summary>
    /// 标题栏图标盒子的垂直位置：始终在标题栏内垂直居中，随标题栏高度自适应。
    /// （不钉在 frame 内缩线上：那条线是命中优先级的边界，不该决定图标画在哪。）
    /// </summary>
    private static int CenteredSystemMenuTop(int captionHeight, int boxHeight) =>
        Math.Max(0, (captionHeight - boxHeight) / 2);


    /// <summary>
    /// 按钮顶边（客户区行）：铺满标题栏时贴顶，否则贴底留 1px 且不低于第 1 行。
    /// 注意"贴顶"不等于第 0 行：普通态第 0 行是顶边线（与 WPF 模板的 1px BorderThickness、
    /// 原生标题栏一致），按钮填充必须从第 1 行开始，否则 hover 会盖住那条线。
    /// </summary>
    private int CaptionButtonTopOf(uint dpi, int captionHeight)
    {
        var buttonHeight = ScaleDip(GetCaptionButtonHeightDip(), dpi);
        return buttonHeight >= captionHeight
            ? 0
            : Math.Max(1, captionHeight - 1 - buttonHeight);
    }

    /// <summary>最小尺寸里留给内容区的高度（DIP）。标准窗口的最小值去掉原生标题栏后也就剩这么一条。</summary>
    private const int MinimumContentHeightDip = 8;

    private bool UpdateMaximizedState()
    {
        var maximized = IsMaximized;
        if (maximized == Metrics.Maximized)
            return false;
        Metrics.Maximized = maximized;
        return true;
    }

    /// <summary>
    /// 窗口最小尺寸 = 内容之外还要放得下自绘标题栏与 frame。
    /// 系统默认的 SM_CXMINTRACK/SM_CYMINTRACK 是按"标准窗口（原生标题栏 23px）"给的，
    /// 我们的标题栏更高、客户区又内缩了 frame，直接沿用会把标题栏压扁；
    /// 因此最小高度 = 下 frame + 标题栏 + 一条内容，最小宽度 = 图标区 + 按钮 + 两侧 frame。
    /// 另外始终尊重 <see cref="Form.MinimumSize"/>。
    /// </summary>
    private (int Width, int Height) GetMinimumTrackSize()
    {
        var systemMinimumX = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCxMinTrack, Metrics.Dpi);
        var systemMinimumY = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SmCyMinTrack, Metrics.Dpi);
        var minimumContent = ScaleDip(MinimumContentHeightDip, Metrics.Dpi);
        var width = Math.Max(
            systemMinimumX,
            Metrics.CaptionLeadingWidth + Metrics.CaptionButtonsWidth + Metrics.FrameX * 2);
        var height = Math.Max(
            systemMinimumY,
            Metrics.FrameY + Metrics.CaptionHeight + minimumContent);
        return (
            Math.Max(width, MinimumSize.Width),
            Math.Max(height, MinimumSize.Height));
    }

    private void HandleGetMinMaxInfo(ref Message m)
    {
        if (m.LParam == IntPtr.Zero)
            return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(m.LParam);
        var (minimumWidth, minimumHeight) = GetMinimumTrackSize();
        limits.MinTrackSize = new NativePoint(
            Math.Max(limits.MinTrackSize.X, minimumWidth),
            Math.Max(limits.MinTrackSize.Y, minimumHeight));
        Marshal.StructureToPtr(limits, m.LParam, false);
    }

    /// <summary>拖动缩放时把矩形夹到最小尺寸；被拖动的那条边保持不变。</summary>
    private void HandleSizing(ref Message m)
    {
        if (m.LParam == IntPtr.Zero)
            return;
        var rect = Marshal.PtrToStructure<NativeRectangle>(m.LParam);
        var (minimumWidth, minimumHeight) = GetMinimumTrackSize();
        var width = Math.Max(rect.Width, minimumWidth);
        var height = Math.Max(rect.Height, minimumHeight);
        if (width == rect.Width && height == rect.Height)
            return;
        var edge = (int)m.WParam;
        var draggingLeft = edge is NativeMethods.WmszLeft or NativeMethods.WmszTopLeft or NativeMethods.WmszBottomLeft;
        var draggingTop = edge is NativeMethods.WmszTop or NativeMethods.WmszTopLeft or NativeMethods.WmszTopRight;
        var updated = new NativeRectangle(
            draggingLeft ? rect.Right - width : rect.Left,
            draggingTop ? rect.Bottom - height : rect.Top,
            draggingLeft ? rect.Right : rect.Left + width,
            draggingTop ? rect.Bottom : rect.Top + height);
        Marshal.StructureToPtr(updated, m.LParam, false);
    }

    private void HandleNcCalcSize(ref Message m)
    {
        if (m.WParam == IntPtr.Zero || m.LParam == IntPtr.Zero)
            return;
        // 必须实时判断窗口状态：最大化/还原时 WM_NCCALCSIZE 先于 WM_SIZE 到达，
        // 用缓存值会让最大化那一帧少内缩 8px、还原那一帧多内缩 8px
        //（客户区顶部落到窗口之外，DWM 把那 8px 当 frame 画成浅色带）。
        var maximized = IsMaximized;
        if (maximized != Metrics.Maximized)
            UpdateFrameMetrics();
        var parameters = Marshal.PtrToStructure<NativeNcCalcSizeParameters>(m.LParam);
        var client = ChromeFrameGeometry.GetClientArea(
            parameters.Proposed,
            Metrics.FrameX,
            Metrics.FrameY,
            maximized);
        parameters.Proposed = client;
        Marshal.StructureToPtr(parameters, m.LParam, false);
        // 客户区就是上面这块：不调用 base，跳过 WinForms 自己的非客户区计算
        m.Result = IntPtr.Zero;
    }

    private int HitTestAt(NativePoint pointer)
    {
        var windowRect = GetWindowRectangle();
        // Chrome 实测：窗口矩形之外的任何点都返回 HTNOWHERE，绝不声明别人的像素
        if (!ChromeFrameGeometry.Contains(windowRect, pointer))
            return ChromeFrameGeometry.HtNowhere;

        var client = ChromeFrameGeometry.GetClientArea(
            windowRect,
            Metrics.FrameX,
            Metrics.FrameY,
            Metrics.Maximized);

        // 1) 客户区之外、窗口矩形之内：就是"阴影里那圈"不可见边框，只有缩放语义
        if (!ChromeFrameGeometry.Contains(client, pointer))
        {
            if (!IsResizable)
                return ChromeFrameGeometry.HtClient;
            return ChromeFrameGeometry.EvaluateResizeHit(
                pointer,
                windowRect,
                Metrics.FrameX,
                Metrics.FrameY,
                Metrics.TopResizeBand);
        }

        // 2) 标题栏按钮（最小化/最大化/关闭）压过一切
        var buttonHit = HitTestCaptionButtons(windowRect, pointer);
        if (buttonHit != 0)
            return buttonHit;

        var captionTop = client.Top;
        var insideCaption = Metrics.CaptionHeight > 0
            && pointer.Y < captionTop + Metrics.CaptionHeight;
        var clientPoint = new Point(pointer.X - client.Left, pointer.Y - captionTop);

        // 3) 标题栏里的可交互自定义控件（菜单、输入框等）优先于缩放带，
        //    它们占满整个标题栏高度，不会被顶部 6px 的 HTTOP 切掉
        if (insideCaption && TryGetInteractiveRoleAt(clientPoint, out var role))
            return ToHitTest(role);

        // 4) 系统菜单命中盒子：与原生标题栏同形状 —— SM_CXSMSIZE × SM_CYSMSIZE
        //    （96dpi 下 22×22 的正方形、竖向居中），中心对准画出来的图标。
        //    盒子之外的标题栏（含图标左边那点空白）仍然可拖动。
        if (insideCaption
            && Metrics.SystemMenuWidth > 0
            && pointer.X >= client.Left + Metrics.SystemMenuLeft
            && pointer.X < client.Left + Metrics.SystemMenuLeft + Metrics.SystemMenuWidth
            && pointer.Y >= captionTop + Metrics.SystemMenuTop
            && pointer.Y < captionTop + Metrics.SystemMenuTop + Metrics.SystemMenuHeight)
        {
            return ChromeFrameGeometry.HtSysMenu;
        }

        // 5) 顶部缩放带：只有标题栏空白处让出来；最大化时客户区正好等于工作区，纵向不可缩放
        if (IsResizable
            && !Metrics.Maximized
            && pointer.Y < windowRect.Top + Metrics.TopResizeBand)
        {
            return ChromeFrameGeometry.HtTop;
        }

        // 6) 其余标题栏可拖动；标题栏之下是客户区
        return insideCaption ? ChromeFrameGeometry.HtCaption : ChromeFrameGeometry.HtClient;
    }

    /// <summary>标题栏按钮命中（窗口坐标）；没有命中时返回 0。派生类提供按钮布局。</summary>
    private protected virtual int HitTestCaptionButtons(NativeRectangle windowRect, NativePoint pointer) => 0;

    /// <summary>窗口矩形（含 frame，即"阴影里那圈"的外边界）。</summary>
    private protected NativeRectangle GetWindowRectangle() =>
        NativeMethods.GetWindowRect(Handle, out var rectangle)
            ? rectangle
            : new NativeRectangle(Left, Top, Right, Bottom);

    /// <summary>指针处最深的子控件是否带可交互角色（沿父链继承标记）。</summary>
    private bool TryGetInteractiveRoleAt(Point clientPoint, out ChromeHitTestRole role)
    {
        var control = FindDeepestControlAt(clientPoint);
        while (control is not null && !ReferenceEquals(control, this))
        {
            var current = GetHitTestRole(control);
            if (current != ChromeHitTestRole.Default)
            {
                role = current;
                return IsInteractiveRole(current);
            }
            control = control.Parent;
        }
        role = ChromeHitTestRole.Default;
        return false;
    }

    private Control? FindDeepestControlAt(Point clientPoint)
    {
        var screenPoint = PointToScreen(clientPoint);
        var control = GetChildAtPoint(clientPoint, GetChildAtPointSkip.Invisible);
        while (control is not null)
        {
            var nested = control.GetChildAtPoint(
                control.PointToClient(screenPoint),
                GetChildAtPointSkip.Invisible);
            if (nested is null)
                break;
            control = nested;
        }
        return control;
    }

    internal static bool IsInteractiveRole(ChromeHitTestRole role) =>
        role is ChromeHitTestRole.Client
            or ChromeHitTestRole.SystemMenu
            or ChromeHitTestRole.MinimizeButton
            or ChromeHitTestRole.MaximizeButton
            or ChromeHitTestRole.CloseButton;

    internal static int ToHitTest(ChromeHitTestRole role) => role switch
    {
        ChromeHitTestRole.SystemMenu => ChromeFrameGeometry.HtSysMenu,
        ChromeHitTestRole.MinimizeButton => ChromeFrameGeometry.HtMinButton,
        ChromeHitTestRole.MaximizeButton => ChromeFrameGeometry.HtMaxButton,
        ChromeHitTestRole.CloseButton => ChromeFrameGeometry.HtClose,
        ChromeHitTestRole.Caption => ChromeFrameGeometry.HtCaption,
        _ => ChromeFrameGeometry.HtClient,
    };

    private static NativePoint GetScreenPoint(IntPtr longParameter)
    {
        var packed = longParameter.ToInt64();
        return new NativePoint(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }

    private sealed class RoleBox(ChromeHitTestRole role)
    {
        internal ChromeHitTestRole Role { get; } = role;
    }
}

/// <summary>按 DPI 与窗口状态算好的 frame 度量，避免每次命中测试都重算。</summary>
internal sealed class FrameMetrics
{
    internal uint Dpi { get; init; } = 96;
    internal int FrameX { get; init; } = 8;
    internal int FrameY { get; init; } = 8;
    internal bool Maximized { get; set; }
    internal int CaptionHeight { get; init; }
    internal int CaptionButtonWidth { get; init; }
    internal int CaptionButtonsWidth { get; init; }
    internal int CaptionButtonHeight { get; init; }
    internal int CaptionLeadingWidth { get; init; }
    internal int SystemMenuLeft { get; init; }
    internal int SystemMenuWidth { get; init; }
    internal int SystemMenuTop { get; init; }
    internal int SystemMenuHeight { get; init; }
    internal int IconMargin { get; init; }
    internal int IconSize { get; init; }
    internal int TopResizeBand { get; init; } = 6;
    internal int CaptionButtonTop { get; init; } = 1;
    internal int CaptionButtonPaintTop { get; init; } = 1;
}
