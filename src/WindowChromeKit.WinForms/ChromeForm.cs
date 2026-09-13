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

    protected override int GetCaptionButtonCount() => 3;

    protected override int GetCaptionIconMarginDip() => CaptionIconMarginDip;

    protected override int GetCaptionLeadingWidthDip() =>
        ShowTitleBarIcon ? CaptionIconMarginDip + CaptionIconSizeDip : 0;

    protected override int GetTopResizeBandDip() => TopResizeBandDip;

    protected override void OnFrameMetricsUpdated() => LayoutTitleBarSlots();

    protected override void OnFrameSizeChanged() => LayoutTitleBarSlots();

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
