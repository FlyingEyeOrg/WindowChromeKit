using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 无默认标题栏、但保留原生窗口边框的 WinForms 窗体。
///
/// 做法与仓库里的 C++ 示例完全一致（数值都取自真实 Chrome 窗口实测）：
/// <list type="bullet">
/// <item>保留 <c>WS_CAPTION | WS_THICKFRAME</c>，由 DWM 提供阴影、可见边框和"阴影里那圈"不可见缩放带；</item>
/// <item><c>WM_NCCALCSIZE</c> 把客户区从窗口矩形内缩出 frame 区域（普通态顶部不内缩，
/// 否则 Windows 10 的 DWM 会画一条原生标题栏压在上面）；</item>
/// <item><c>WM_NCHITTEST</c> 按 Chrome 的优先级自行判定：窗口矩形之外 → <c>HTNOWHERE</c>，
/// 标题栏按钮 → <c>HTCLOSE/HTMAXBUTTON/HTMINBUTTON</c>，左/右/下 8px 与四角 → 缩放值，
/// 顶部 6px → <c>HTTOP</c>，图标区 → <c>HTSYSMENU</c>，标题栏 → <c>HTCAPTION</c>，其余 → <c>HTCLIENT</c>；</item>
/// <item>标题栏在客户区顶部自绘（含图标、标题、三个按钮与悬停/按下状态），
/// 可用 <see cref="OnPaintTitleBar"/> 或配色属性完全自定义。</item>
/// </list>
///
/// 注意：应用需要一份声明了 <c>supportedOS</c> 的 manifest（见示例工程的 app.manifest），
/// 否则 Windows 会按旧版应用对待，DWM 不会渲染窗口 frame —— 窗口既没有阴影也没有可见边框线。
/// </summary>
public partial class ChromeForm : Form
{
    private FrameMetrics _metrics = new();
    private ChromeCaptionButton? _hotButton;
    private ChromeCaptionButton? _pressedButton;
    private bool _trackingNonClientMouse;

    public ChromeForm()
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

    /// <summary>内容区 = 客户区减去自绘标题栏与 <see cref="Control.Padding"/>，停靠/锚定的子控件自动排在标题栏下方。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override Rectangle DisplayRectangle
    {
        get
        {
            var client = ClientRectangle;
            var top = client.Top + _metrics.CaptionHeight + Padding.Top;
            var width = client.Width - Padding.Horizontal;
            var height = client.Height - _metrics.CaptionHeight - Padding.Vertical;
            return new Rectangle(
                client.Left + Padding.Left,
                top,
                Math.Max(0, width),
                Math.Max(0, height));
        }
    }

    /// <summary>客户区内标题栏所占的高度（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionHeight => _metrics.CaptionHeight;

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
        _hotButton = null;
        _pressedButton = null;
        _trackingNonClientMouse = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case NativeMethods.WmNcCalcSize:
                HandleNcCalcSize(ref m);
                return;
            case NativeMethods.WmNcHitTest:
                m.Result = new IntPtr(HitTestAt(GetScreenPoint(m.LParam)));
                return;
            case NativeMethods.WmNcMouseMove:
                HandleNonClientMouseMove((int)m.WParam);
                break;
            case NativeMethods.WmNcMouseLeave:
                HandleNonClientMouseLeave();
                break;
            case NativeMethods.WmNcLButtonDown:
                if (HandleNonClientButtonDown((int)m.WParam))
                    return;
                break;
            case NativeMethods.WmLButtonUp:
                if (HandleButtonUp())
                    return;
                break;
            case NativeMethods.WmMouseMove:
                if (_pressedButton is not null)
                {
                    UpdatePressedButtonHover();
                    return;
                }
                break;
            case NativeMethods.WmCaptureChanged:
                ResetPointerState();
                break;
            case NativeMethods.WmSize:
                if (UpdateMaximizedState())
                    UpdateFrameMetrics();
                break;
            case NativeMethods.WmActivate:
                Invalidate();
                break;
            case NativeMethods.WmDpiChanged:
            case NativeMethods.WmSettingChange:
            case NativeMethods.WmThemeChanged:
                UpdateFrameMetrics();
                Invalidate();
                break;
        }
        base.WndProc(ref m);
    }

    /// <summary>窗口当前是否最大化（直接问窗口，不受 WinForms 状态更新时机影响）。</summary>
    private bool IsMaximized => IsHandleCreated && NativeMethods.IsZoomed(Handle);

    /// <summary>把帧度量重算一遍（DPI、窗口状态、系统主题变化后调用）。</summary>
    protected void UpdateFrameMetrics()
    {
        var dpi = (uint)Math.Max(96, DeviceDpi);
        var (frameX, frameY) = ChromeFrameGeometry.GetFrameThickness(dpi);
        var maximized = IsMaximized;
        _metrics = new FrameMetrics
        {
            Dpi = dpi,
            FrameX = frameX,
            FrameY = frameY,
            Maximized = maximized,
            CaptionHeight = ScaleDip(CaptionHeightDip, dpi),
            CaptionButtonWidth = ScaleDip(CaptionButtonWidthDip, dpi),
            CaptionButtonHeight = ScaleDip(CaptionButtonHeightDip, dpi),
            TopResizeBand = ScaleDip(TopResizeBandDip, dpi),
            IconMargin = ScaleDip(CaptionIconMarginDip, dpi),
            IconSize = ChromeFrameGeometry.GetSmallIconSize(dpi),
            // 命中矩形固定用窗口坐标 T+1 起（Chrome 实测，最大化时也不变）
            CaptionButtonTop = 1,
            // 绘制用客户区坐标：普通态第 1 行（第 0 行留给顶边线），最大化时第 0 行
            CaptionButtonPaintTop = maximized ? 0 : 1,
        };
    }

    private static int ScaleDip(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    private bool UpdateMaximizedState()
    {
        var maximized = IsMaximized;
        if (maximized == _metrics.Maximized)
            return false;
        _metrics.Maximized = maximized;
        return true;
    }

    private void HandleNcCalcSize(ref Message m)
    {
        if (m.WParam == IntPtr.Zero || m.LParam == IntPtr.Zero)
            return;
        // 必须实时判断窗口状态：最大化时 WM_NCCALCSIZE 先于 WM_SIZE 到达，
        // 用缓存值会让最大化那一帧的顶部少内缩 8px（客户区落到屏幕外、标题栏被裁）。
        var maximized = IsMaximized;
        if (maximized != _metrics.Maximized)
            UpdateFrameMetrics();
        var parameters = Marshal.PtrToStructure<NativeNcCalcSizeParameters>(m.LParam);
        var client = ChromeFrameGeometry.GetClientArea(
            parameters.Proposed,
            _metrics.FrameX,
            _metrics.FrameY,
            maximized);
        parameters.Proposed = client;
        Marshal.StructureToPtr(parameters, m.LParam, false);
        // 客户区就是上面这块：不调用 base，跳过 WinForms 自己的非客户区计算
        m.Result = IntPtr.Zero;
    }

    private static NativePoint GetScreenPoint(IntPtr longParameter)
    {
        var packed = longParameter.ToInt64();
        return new NativePoint(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }
}

/// <summary>按 DPI 与窗口状态算好的 frame 度量，避免每次命中测试都重算。</summary>
internal sealed class FrameMetrics
{
    internal uint Dpi { get; init; } = 96;
    internal int FrameX { get; init; } = 8;
    internal int FrameY { get; init; } = 8;
    internal bool Maximized { get; set; }
    internal int CaptionHeight { get; init; } = 40;
    internal int CaptionButtonWidth { get; init; } = 46;
    internal int CaptionButtonHeight { get; init; } = 39;
    internal int CaptionButtonTop { get; init; } = 1;
    internal int CaptionButtonPaintTop { get; init; } = 1;
    internal int TopResizeBand { get; init; } = 6;
    internal int IconSize { get; init; } = 16;
    internal int IconMargin { get; init; } = 12;
}
