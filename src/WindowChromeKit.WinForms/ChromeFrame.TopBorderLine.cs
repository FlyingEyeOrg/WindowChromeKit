using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 窗口边框的顶边 1 像素线。
///
/// 它属于**窗口边框**而不是标题栏内容：Windows 10 的 DWM 只在非客户区画边框，
/// 而本库把客户区顶到了窗口顶边（顶部非客户区高度为 0），所以这一条边没有 DWM 来画，
/// 必须由库补上 —— 与左/右/下三条边同性质。放在 <see cref="ChromeFrame"/> 而不是
/// <see cref="ChromeForm"/>，是为了让完全自绘窗口也拿得到同一套实现，不必自己重测
/// DWM 的混合参数。
///
/// 颜色存的是 <c>[alpha + 基色]</c>（半透明），绘制时与【当前标题栏底色】混合，
/// 因此自定义标题栏配色不需要改这两个值：
/// <code>线色 = alpha × 基色 + (1 - alpha) × 标题栏底色</code>
/// </summary>
public partial class ChromeFrame
{
    // 参数由原生 DWM 边框实测反解（黑底/白底两组）：
    //   聚焦：黑底 25 / 白底 112 -> 基色 #262626、alpha 66%
    //   失焦：黑底 43 / 白底 170 -> 基色 #565656、alpha 50%
    private Color _topBorderLineActiveColor = Color.FromArgb(168, 0x26, 0x26, 0x26);
    private Color _topBorderLineInactiveColor = Color.FromArgb(128, 0x56, 0x56, 0x56);
    private bool _showTopBorderLine = true;

    /// <summary>
    /// 有焦点时顶边 1 像素线的颜色。默认是半透明基色（#262626 @ 66%），绘制时与
    /// 【当前标题栏底色】混合，因此自定义标题栏配色无需改动它。数值由原生 DWM 边框实测反解。
    /// </summary>
    [Category("WindowChromeKit")]
    public Color TopBorderLineActiveColor
    {
        get => _topBorderLineActiveColor;
        set => SetFrameOption(ref _topBorderLineActiveColor, value);
    }

    /// <summary>
    /// 失活（无焦点）时顶边 1 像素线的颜色。默认是半透明基色（#565656 @ 50%，比聚焦更浅更柔），
    /// 同样与标题栏底色混合。
    /// </summary>
    [Category("WindowChromeKit")]
    public Color TopBorderLineInactiveColor
    {
        get => _topBorderLineInactiveColor;
        set => SetFrameOption(ref _topBorderLineInactiveColor, value);
    }

    /// <summary>
    /// 是否绘制顶边 1 像素线。默认 <c>true</c>。
    /// Windows 10 上 DWM 不画顶部这条边，关掉它会缺一条边；Windows 11 上该行由 DWM 覆盖，
    /// 因此这个开关对 Win11 无可见影响。最大化时始终不画（客户区等于工作区，
    /// 画了会在屏幕顶端多出一条线，Chrome 最大化也没有）。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(true)]
    public bool ShowTopBorderLine
    {
        get => _showTopBorderLine;
        set => SetFrameOption(ref _showTopBorderLine, value);
    }

    /// <summary>窗口当前是否激活（决定顶线取激活色还是失活色）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    protected bool IsFrameActive => ActiveForm == this;

    /// <summary>
    /// 当前是否应当绘制顶边线：开关打开、且不是最大化。
    /// 派生类可覆盖以改变时机（例如某些自定义布局下始终不画）。
    ///
    /// 这里用 <see cref="FrameMetrics.Maximized"/>（缓存值）而不是实时查询
    /// <see cref="IsMaximized"/>：按钮的绘制矩形（<c>CaptionButtonTop</c> / <c>CaptionHitHeight</c>）
    /// 由同一个缓存值算出，两者必须同源 —— 否则最大化/还原那一帧会出现
    /// 「线已消失但按钮还少一行」或「线已出现但按钮压住它」的错位。
    /// 缓存在 <c>WM_SIZE</c> 里更新，早于随后的重绘。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    protected virtual bool ShouldDrawTopBorderLine => ShowTopBorderLine && !Metrics.Maximized;

    /// <summary>
    /// 绘制顶边 1 像素线（客户区第 0 行）。
    ///
    /// <see cref="ChromeForm"/> 会在自己的 <c>OnPaint</c> 里自动调用它 —— 包括把
    /// <see cref="ChromeForm.ShowDefaultTitleBar"/> 设为 false 的完全自绘场景，因为这条线是
    /// **窗口边框**的一部分，不应随标题栏内容的自定义而消失。
    ///
    /// 直接继承 <see cref="ChromeFrame"/> 时本类不参与绘制（<see cref="ChromeFrame"/> 不重写
    /// <c>OnPaint</c>），需要在你的 <c>OnPaint</c> 里显式调用一次即可拿到同一套实现。
    /// 线条绘制在**最上层**，不会被标题栏底色或按钮覆盖。
    /// </summary>
    /// <param name="graphics">客户区绘图表面。</param>
    /// <param name="active">窗口是否激活；传 null 时按 <see cref="IsFrameActive"/> 实时判断。</param>
    protected void DrawTopBorderLine(Graphics graphics, bool? active = null)
    {
        if (!ShouldDrawTopBorderLine || graphics is null)
            return;
        var client = ClientRectangle;
        if (client.Width <= 0)
            return;
        var isActive = active ?? IsFrameActive;
        using var line = new Pen(isActive ? TopBorderLineActiveColor : TopBorderLineInactiveColor);
        graphics.DrawLine(line, 0, 0, client.Width - 1, 0);
    }

    /// <summary>
    /// 顶线的默认参数随 DPI / 主题变化时无需重算（纯颜色），所以只需要重绘那一行。
    /// </summary>
    private void SetFrameOption<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        InvalidateTopBorderLine();
    }

    /// <summary>只重绘顶边那一行。</summary>
    private void InvalidateTopBorderLine()
    {
        if (!IsHandleCreated)
            return;
        Invalidate(new Rectangle(0, 0, ClientSize.Width, 1));
    }
}
