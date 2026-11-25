using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WindowChromeKit.WinForms;

/// <summary>
/// Chrome 风格默认标题栏窗体：在 <see cref="ChromeFrame"/> 的原生 frame 之上，
/// 提供自绘标题栏（图标、标题、最小化/最大化/关闭），默认配色取 VS Code 的深色标题栏；
/// 配色、度量、标题栏内容插槽与逐控件命中角色都可以自定义。
///
/// 需要完全自绘标题栏时用 <see cref="ChromeFrame"/>，或把
/// <see cref="ShowDefaultTitleBar"/> 设为 false 后自己绘制。
/// </summary>
public partial class ChromeForm : ChromeFrame
{
    /// <summary>图标在设计尺寸下的边长（DIP）；实际绘制用 DPI 相关的 SM_CXSMICON。</summary>
    private const int CaptionIconSizeDip = 16;

    /// <summary>创建默认标题栏窗体，并套用默认样式 <see cref="ChromeTitleBarStyle.Chrome"/>。</summary>
    public ChromeForm() => ApplyTitleBarStyle(_titleBarStyle);

    private ChromeCaptionButton? _hotButton;
    private ChromeCaptionButton? _pressedButton;
    private bool _trackingNonClientMouse;

    /// <summary>当前悬停的标题栏按钮；没有悬停时为 null。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ChromeCaptionButton? HoveredCaptionButton => _hotButton;

    /// <summary>当前按下的标题栏按钮；没有按下时为 null。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ChromeCaptionButton? PressedCaptionButton => _pressedButton;

    protected override int GetCaptionHeightDip() => CaptionHeightDip;

    protected override int GetCaptionButtonHeightDip() => CaptionButtonHeightDip;

    protected override int GetCaptionButtonWidthDip() => CaptionButtonWidthDip;

    /// <summary>
    /// 最小化按钮的宽度：Chrome 实测三个按钮并不等宽（最小化 45、最大化/关闭 46），
    /// 未启用"照抄 Chrome 不等宽"时（MinimizeButtonWidthDip 为 0）沿用统一宽度。
    /// </summary>
    protected override int GetCaptionButtonWidthDip(ChromeCaptionButton button) =>
        button == ChromeCaptionButton.Minimize && MinimizeButtonWidthDip > 0
            ? MinimizeButtonWidthDip
            : CaptionButtonWidthDip;

    protected override int GetCaptionButtonCount() => 3;

    protected override int GetCaptionIconMarginDip() => CaptionIconMarginDip;

    /// <summary>
    /// 标题栏左侧图标区宽度（DIP），用于算最小窗口宽度。
    ///
    /// 要报**插槽的真实宽度**（= 系统菜单命中盒的右缘），而不是图标的墨迹宽度：
    /// 命中盒比图标每侧宽 3px，所以墨迹宽度会比插槽少 3px（Chrome 28 对 31）。
    /// 少算这 3px 会把三个按钮挤出客户区右侧 —— 与 WPF 版同一个坑
    /// （那边 <c>CaptionLeadingWidth</c> 原先是 <c>Left + 22</c> 但漏了 <c>Right</c>）。
    /// 命中盒尺寸是 DPI 相关的系统度量，这里按 96dpi 的设计值折算回 DIP。
    /// </summary>
    /// <summary>
    /// 标题栏左侧图标区宽度（DIP），用于算最小窗口宽度。
    ///
    /// 要报**插槽的真实宽度**（= 系统菜单命中盒的右缘），而不是图标的墨迹宽度：
    /// 命中盒比图标每侧宽 3px，所以墨迹宽度会比插槽少 3px。
    /// 少算这 3px 会把三个按钮挤出客户区右侧 —— 与 WPF 版同一个坑。
    ///
    /// 命中盒现在按标题栏高度等比缩放（见 <c>ChromeFrame.UpdateFrameMetrics</c>），
    /// 所以这里也用同样的比例换算，不能再用固定的 22。
    /// </summary>
    protected override int GetCaptionLeadingWidthDip()
    {
        if (!ShowTitleBarIcon)
            return 0;
        var (box, _) = GetScaledSystemMenuMetricsDip();
        // 命中盒左缘 = (标题栏高 − 盒高)/2（垂直居中同一套边距），插槽宽 = 盒左 + 盒宽。
        // 与 ChromeFrame.UpdateFrameMetrics 里的 SystemMenuLeft 同一条公式。
        return Math.Max(0, (CaptionHeightDip - box) / 2) + box;
    }

    /// <summary>
    /// 命中盒与图标在 96dpi 设计尺寸下的边长（DIP，已按标题栏高度等比缩放）。
    ///
    /// 与 <c>ChromeFrame.UpdateFrameMetrics</c> 用同一条规则：以**原生可见标题栏高度**
    /// 为基准，按 <c>当前标题栏高 / 原生标题栏高</c> 缩放，保证"插槽宽度"与实际绘制一致。
    /// </summary>
    private (int Box, int Icon) GetScaledSystemMenuMetricsDip()
    {
        var scale = Internal.NativeCaptionMetrics.Height > 0d
            ? CaptionHeightDip / Internal.NativeCaptionMetrics.Height
            : 1d;
        if (scale <= 0d || double.IsInfinity(scale))
            scale = 1d;
        return (
            Math.Max(1, (int)Math.Round(SystemMenuBoxSizeDip * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(CaptionIconSizeDip * scale, MidpointRounding.AwayFromZero)));
    }

    /// <summary>系统菜单命中盒边长（DIP）。与 <c>SM_CXSMSIZE</c> 在 96dpi 下的值一致。</summary>
    private const int SystemMenuBoxSizeDip = 22;

    protected override int GetTopResizeBandDip() => TopResizeBandDip;

    protected override void OnFrameMetricsUpdated()
    {
        LayoutTitleBarSlots();
        RevalidateHoverAgainstCurrentGeometry();
    }

    protected override void OnFrameSizeChanged()
    {
        LayoutTitleBarSlots();
        RevalidateHoverAgainstCurrentGeometry();
    }

    protected override void ResetPointerState()
    {
        _hotButton = null;
        _pressedButton = null;
        _trackingNonClientMouse = false;
        InvalidateCaption();
    }

    protected override void OnNonClientMouseMove(int hit) => HandleCaptionMouseMove(hit);

    protected override void OnNonClientMouseLeave() => HandleCaptionMouseLeave();

    protected override bool OnNonClientButtonDown(int hit) => HandleCaptionButtonDown(hit);

    protected override bool OnButtonUp() => HandleCaptionButtonUp();

    protected override bool OnFrameMouseMove() => HandleCaptionMouseMoveInClient();

    protected override void OnCaptureChanged() => ResetPointerState();
}
