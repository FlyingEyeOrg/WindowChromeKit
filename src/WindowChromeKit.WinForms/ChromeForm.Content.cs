using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 标题栏自定义：内容插槽、命中角色与默认标题栏开关。
/// 与 WPF 版的 <c>TitleBarContent</c> / <c>SetHitTestRole</c> 对应。
/// </summary>
public partial class ChromeForm
{
    private Control? _titleBarContent;
    private bool _showDefaultTitleBar = true;

    /// <summary>
    /// 标题栏内容控件：占满图标右侧到三个窗口按钮之间的空间，用来放菜单、输入框、按钮等标题栏内容。
    /// 需要自己接收鼠标的内容请用 <see cref="ChromeFrame.SetHitTestRole"/> 标记为 <see cref="ChromeHitTestRole.Client"/>
    /// （该标记沿控件树向下继承，标在最外层容器上即可）。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(null)]
    public Control? TitleBarContent
    {
        get => _titleBarContent;
        set => SetTitleBarSlot(ref _titleBarContent, value);
    }

    private ContentAlignment _captionTextAlignment = ContentAlignment.MiddleLeft;

    /// <summary>
    /// 默认标题栏里标题文字的对齐方式。默认贴左（图标右侧），与 WPF 版和真实 Chrome 一致；
    /// 需要居中可设为 <see cref="ContentAlignment.MiddleCenter"/>。
    /// 三套预置样式（Chrome / VsCode / Windows）都是贴左。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(ContentAlignment.MiddleLeft)]
    public ContentAlignment CaptionTextAlignment
    {
        get => _captionTextAlignment;
        set => SetOption(ref _captionTextAlignment, value);
    }

    /// <summary>
    /// 是否绘制默认标题栏（图标、标题、三个按钮与顶边线）。
    /// 设为 false 后标题栏完全由 <see cref="OnPaintTitleBar"/> 绘制，但内建按钮的悬停/按下状态机仍然工作。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(true)]
    public bool ShowDefaultTitleBar
    {
        get => _showDefaultTitleBar;
        set => SetOption(ref _showDefaultTitleBar, value);
    }

    /// <summary>标题栏内容插槽当前占用的矩形（客户区坐标）；未设置插槽时为空。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rectangle TitleBarContentBounds =>
        _titleBarContent is null ? Rectangle.Empty : _titleBarContent.Bounds;

    private void SetTitleBarSlot(ref Control? field, Control? value)
    {
        if (ReferenceEquals(field, value))
            return;
        if (field is not null)
        {
            UnhookSlotDrag(field);
            Controls.Remove(field);
        }
        field = value;
        if (value is not null)
            HookSlotDrag(value);
        if (value is not null && !Controls.Contains(value))
            Controls.Add(value);
        // 直接摆位，不能只靠 PerformLayout()：本类没有重写 OnLayout，
        // LayoutTitleBarSlots 只由 OnFrameMetricsUpdated / OnFrameSizeChanged 驱动，
        // 所以 PerformLayout() 不会让它执行 —— 实测赋值后插槽会停在 X=0，
        // 要等一次尺寸变化才归位到图标右侧。
        LayoutTitleBarSlots();
        PerformLayout();
        InvalidateCaption();
    }

    /// <summary>
    /// 把内容插槽摆到标题栏里：图标区右侧 → 内容 → 三个窗口按钮。
    /// WinForms 的每个子控件都是独立 HWND，鼠标命中会落在子窗口上、父窗口的
    /// WM_NCHITTEST 不再被询问，所以插槽**按控件自身宽度摆放、不铺满标题栏**，
    /// 未被占用的标题栏仍然可以拖动窗口；插槽里未标记为可交互的部分则由
    /// <see cref="HookSlotDrag"/> 接管成拖动。
    /// </summary>
    private void LayoutTitleBarSlots()
    {
        if (_titleBarContent is null)
            return;
        var slotTop = Metrics.CaptionButtonPaintTop;
        var slotHeight = Math.Max(1, Metrics.CaptionButtonHeight);
        // 右侧避让三个窗口按钮
        var right = ClientRectangle.Right - Metrics.CaptionButtonWidth * 3;
        var left = CaptionTextLeftCore;
        var width = MeasureSlot(_titleBarContent, Math.Max(0, right - left));
        _titleBarContent.SetBounds(left, slotTop, width, slotHeight);
    }

    /// <summary>
    /// 标题栏里"图标之后"的内容起点：**系统菜单命中盒的右缘** —— 原生标题栏的规则。
    ///
    /// 实测 Win32 自绘标题栏里，标题的布局原点正好落在命中盒右缘（图标墨迹 +3、
    /// 命中盒比图标每侧宽 3，所以两者墨迹之间剩 6px 空白）。
    /// 以前这里多加过一个 8px 的「间距」，比原生宽 5px，已去掉。
    ///
    /// 隐藏图标时整列都不占布局，所以回到 0。
    /// **默认标题文字与 <see cref="TitleBarContent"/> 插槽共用这一个起点** ——
    /// 它们原先各写一份，插槽用 0 而标题用 <c>Metrics.IconMargin</c>（12），
    /// 隐藏图标后两者相差 12px。共用之后不会再各自漂移。
    /// </summary>
    internal int CaptionTextLeftCore =>
        ShowTitleBarIcon ? Metrics.SystemMenuLeft + Metrics.SystemMenuWidth : 0;

    /// <summary>插槽宽度：控件自身宽度优先（未设置时用首选宽度），并夹到可用空间内。</summary>
    private static int MeasureSlot(Control control, int available)
    {
        var preferred = control.GetPreferredSize(Size.Empty).Width;
        var width = control.Width > 0 ? control.Width : preferred;
        return Math.Max(1, Math.Min(Math.Max(width, 0), Math.Max(1, available)));
    }

    /// <summary>
    /// 让插槽里"未标记为可交互"的控件在按下时拖动窗口：子控件是独立 HWND，
    /// 父窗口收不到非客户区消息，只能由控件自己发起 HTCAPTION 拖动。
    /// 标记为可交互角色（Client/按钮等）的控件及其子树不接管。
    /// </summary>
    private void HookSlotDrag(Control control)
    {
        if (GetHitTestRole(control) != ChromeHitTestRole.Default)
            return;
        control.MouseDown += OnSlotMouseDown;
        control.DoubleClick += OnSlotDoubleClick;
        foreach (Control child in control.Controls)
            HookSlotDrag(child);
    }

    private void OnSlotMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
            BeginCaptionDrag(doubleClick: false);
    }

    private void UnhookSlotDrag(Control control)
    {
        control.MouseDown -= OnSlotMouseDown;
        control.DoubleClick -= OnSlotDoubleClick;
        foreach (Control child in control.Controls)
            UnhookSlotDrag(child);
    }

    private void OnSlotDoubleClick(object? sender, EventArgs eventArgs) =>
        BeginCaptionDrag(doubleClick: true);

    /// <summary>让系统按"标题栏"接管这次按下：拖动窗口或双击最大化/还原。</summary>
    private void BeginCaptionDrag(bool doubleClick)
    {
        _ = NativeMethods.ReleaseCapture();
        var message = doubleClick ? NativeMethods.WmNcLButtonDblClk : NativeMethods.WmNcLButtonDown;
        _ = NativeMethods.SendMessage(
            Handle,
            message,
            new IntPtr(ChromeFrameGeometry.HtCaption),
            IntPtr.Zero);
    }

}
