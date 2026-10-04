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
    protected override int GetCaptionLeadingWidthDip() =>
        ShowTitleBarIcon ? SystemMenuLeftDesignDip + SystemMenuBoxSizeDip : 0;

    /// <summary>
    /// 命中盒左缘在设计尺寸（96dpi）下的位置：图标左边距减去盒子比图标宽出的每侧 3px。
    /// </summary>
    private int SystemMenuLeftDesignDip =>
        Math.Max(0, CaptionIconMarginDip - (SystemMenuBoxSizeDip - CaptionIconSizeDip) / 2);

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
