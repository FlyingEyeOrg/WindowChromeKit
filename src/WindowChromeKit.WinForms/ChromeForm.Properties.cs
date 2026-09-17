using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>标题栏的可定制度量与配色。所有度量单位是 DIP，使用时按窗口 DPI 缩放。</summary>
public partial class ChromeForm
{
    private ChromeTitleBarStyle _titleBarStyle = ChromeTitleBarStyle.Chrome;
    private ChromeTitleBarPalette _titleBarPalette = ChromeTitleBarPalette.Default;
    private int _captionHeightDip = 40;
    private int _captionButtonWidthDip = 46;
    private int _minimizeButtonWidthDip;
    private int _captionButtonHeightDip = 39;
    private int _captionIconMarginDip = 12;
    private int _topResizeBandDip = 6;
    private bool _showTitleBarIcon = true;
    private Font? _captionFont;
    // 默认配色取 VS Code 的深色标题栏：底色 #323233、文字 #CCCCCC、
    // 悬停 #505050、按下 #5F5F5F，关闭仍用系统标准红。
    private Color _activeCaptionColor = Color.FromArgb(0x32, 0x32, 0x33);
    private Color _inactiveCaptionColor = Color.FromArgb(0x2D, 0x2D, 0x2D);
    private Color _captionTextColor = Color.FromArgb(0xCC, 0xCC, 0xCC);
    private Color _inactiveCaptionTextColor = Color.FromArgb(0x9D, 0x9D, 0x9D);
    private Color _captionButtonHoverColor = Color.FromArgb(0x50, 0x50, 0x50);
    private Color _captionButtonPressedColor = Color.FromArgb(0x5F, 0x5F, 0x5F);
    private Color _closeButtonHoverColor = Color.FromArgb(0xE8, 0x11, 0x23);
    private Color _closeButtonPressedColor = Color.FromArgb(0xF1, 0x70, 0x7A);
    // 顶边线的属性与绘制已上移到 ChromeFrame（它属于窗口边框，不属于标题栏内容）：
    // TopBorderLineActiveColor / TopBorderLineInactiveColor / ShowTopBorderLine /
    // ShouldDrawTopBorderLine / DrawTopBorderLine，见 ChromeFrame.TopBorderLine.cs。
    // 那里也记录了实测反解出的混合参数。

    /// <summary>自绘标题栏高度（DIP）。实测 Chrome 为 40。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(40)]
    [Description("自绘标题栏的高度（DIP），实测 Chrome 为 40。")]
    public int CaptionHeightDip
    {
        get => _captionHeightDip;
        set => SetOption(ref _captionHeightDip, Math.Max(0, value));
    }

    /// <summary>标题栏按钮宽度（DIP）。实测 Chrome 为 46/46/45。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(46)]
    public int CaptionButtonWidthDip
    {
        get => _captionButtonWidthDip;
        set => SetOption(ref _captionButtonWidthDip, Math.Max(1, value));
    }

    /// <summary>标题栏按钮高度（DIP）。实测 Chrome 为 39。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(39)]
    public int CaptionButtonHeightDip
    {
        get => _captionButtonHeightDip;
        set => SetOption(ref _captionButtonHeightDip, Math.Max(1, value));
    }

    /// <summary>标题栏图标左边距（DIP）。实测 Chrome 为 12。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(12)]
    public int CaptionIconMarginDip
    {
        get => _captionIconMarginDip;
        set => SetOption(ref _captionIconMarginDip, Math.Max(0, value));
    }

    /// <summary>顶部缩放带高度（DIP）。实测 Chrome 为 6，明显窄于其余三边的 8。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(6)]
    public int TopResizeBandDip
    {
        get => _topResizeBandDip;
        set => SetOption(ref _topResizeBandDip, Math.Max(0, value));
    }

    /// <summary>是否绘制标题栏图标（关闭后图标区域不再占用布局，也不再返回 <c>HTSYSMENU</c>）。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(true)]
    public bool ShowTitleBarIcon
    {
        get => _showTitleBarIcon;
        set => SetOption(ref _showTitleBarIcon, value);
    }

    /// <summary>
    /// 标题栏**实际**使用的图标，与 WPF 版的 <c>EffectiveTitleBarIcon</c> 同名同语义。
    /// 解析顺序与库内部绘制标题栏时**完全一致**（都走同一个原生回退链），所以拿到的就是屏幕上那一个。
    ///
    /// 用途：把 <see cref="ChromeForm.ShowDefaultTitleBar"/> 设为 false 完全自绘标题栏时，
    /// 可以直接画这个图标，不必自己再调原生 API —— 与 WPF 版的自绘体验对齐。
    ///
    /// 返回的是**独立副本**（调用方负责释放），系统没有图标时为 <c>null</c>。
    /// 句柄未创建时也返回 <c>null</c>（此时窗口图标尚未确定）。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Icon? EffectiveTitleBarIcon
    {
        get
        {
            if (!IsHandleCreated)
                return null;
            var handle = NativeMethods.GetWindowSmallIcon(Handle);
            if (handle == IntPtr.Zero)
                return null;
            // FromHandle 不拥有句柄，Clone 出来的是独立副本，调用方可以安全释放，
            // 也不会误销毁系统所有的句柄。
            using var source = Icon.FromHandle(handle);
            return (Icon)source.Clone();
        }
    }

    /// <summary>标题栏字体；null 表示按 DPI 缩放系统 caption 字体。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(null)]
    public Font? CaptionFont
    {
        get => _captionFont;
        set => SetOption(ref _captionFont, value);
    }

    [Category("WindowChromeKit")]
    public Color ActiveCaptionColor
    {
        get => _activeCaptionColor;
        set => SetOption(ref _activeCaptionColor, value);
    }

    [Category("WindowChromeKit")]
    public Color InactiveCaptionColor
    {
        get => _inactiveCaptionColor;
        set => SetOption(ref _inactiveCaptionColor, value);
    }

    [Category("WindowChromeKit")]
    public Color CaptionTextColor
    {
        get => _captionTextColor;
        set => SetOption(ref _captionTextColor, value);
    }

    [Category("WindowChromeKit")]
    public Color InactiveCaptionTextColor
    {
        get => _inactiveCaptionTextColor;
        set => SetOption(ref _inactiveCaptionTextColor, value);
    }

    [Category("WindowChromeKit")]
    public Color CaptionButtonHoverColor
    {
        get => _captionButtonHoverColor;
        set => SetOption(ref _captionButtonHoverColor, value);
    }

    [Category("WindowChromeKit")]
    public Color CaptionButtonPressedColor
    {
        get => _captionButtonPressedColor;
        set => SetOption(ref _captionButtonPressedColor, value);
    }

    [Category("WindowChromeKit")]
    public Color CloseButtonHoverColor
    {
        get => _closeButtonHoverColor;
        set => SetOption(ref _closeButtonHoverColor, value);
    }

    [Category("WindowChromeKit")]
    public Color CloseButtonPressedColor
    {
        get => _closeButtonPressedColor;
        set => SetOption(ref _closeButtonPressedColor, value);
    }

    /// <summary>当前样式下从客户区顶边算起的按钮顶行（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionButtonTop => Metrics.CaptionButtonTop;

    /// <summary>按钮绘制矩形的顶行（已让出普通态第 0 行的顶边线）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionButtonPaintTop => GetCaptionButtonPaintRect(0).Top;

    /// <summary>按钮绘制矩形的高度（已让出普通态第 0 行的顶边线）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionButtonPaintHeight => GetCaptionButtonPaintRect(0).Height;

    /// <summary>系统菜单（图标）盒子相对客户区左边缘的列号（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SystemMenuLeft => Metrics.SystemMenuLeft;

    /// <summary>系统菜单（图标）盒子相对客户区顶边的行号（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SystemMenuTop => Metrics.SystemMenuTop;

    /// <summary>当前样式下的标题栏按钮高度（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionButtonHeight => Metrics.CaptionButtonHeight;

    /// <summary>当前样式下的标题栏按钮宽度（设备像素，已按 DPI 缩放）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CaptionButtonWidth => Metrics.CaptionButtonWidth;

    /// <summary>
    /// 标题栏预置样式（默认 <see cref="ChromeTitleBarStyle.Chrome"/>）。
    /// 赋值时把该样式的几何、配色与布局开关**应用一次**（配色取自当前的
    /// <see cref="TitleBarPalette"/>）；之后单独修改任何属性都以属性为准，样式不会再覆盖回来。
    /// 默认标题栏关闭时（<see cref="ChromeForm.ShowDefaultTitleBar"/> 为 false）
    /// 或使用 <see cref="ChromeFrame"/> 自绘时，本属性不生效。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(ChromeTitleBarStyle.Chrome)]
    public ChromeTitleBarStyle TitleBarStyle
    {
        get => _titleBarStyle;
        set
        {
            if (_titleBarStyle == value)
                return;
            _titleBarStyle = value;
            ApplyTitleBarStyle(value);
        }
    }

    /// <summary>
    /// 标题栏配色（默认 <see cref="ChromeTitleBarPalette.Default"/>，即该样式自带的那套）。
    ///
    /// 与 <see cref="TitleBarStyle"/> 正交：样式给骨架与默认配色，本属性换成别的配色。
    /// 赋值时只重新套用颜色，**几何不变**；之后单独修改任何颜色属性都以属性为准。
    /// 两个属性谁后赋值都成立 —— 改样式会按当前配色重新上色（Default 时套用新样式自带的那套），
    /// 改配色则与当前样式无关。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(ChromeTitleBarPalette.Default)]
    public ChromeTitleBarPalette TitleBarPalette
    {
        get => _titleBarPalette;
        set
        {
            if (_titleBarPalette == value)
                return;
            _titleBarPalette = value;
            ApplyTitleBarPalette(_titleBarStyle);
        }
    }

    /// <summary>把预置样式套到标题栏上（几何 + 配色 + 布局开关，一次性应用）。</summary>
    private void ApplyTitleBarStyle(ChromeTitleBarStyle style)
    {
        ApplyTitleBarGeometry(style);
        ApplyTitleBarPalette(style);
    }

    /// <summary>只套几何与布局开关；配色由 <see cref="ApplyTitleBarPalette"/> 负责。</summary>
    private void ApplyTitleBarGeometry(ChromeTitleBarStyle style)
    {
        switch (style)
        {
            case ChromeTitleBarStyle.VsCode:
                // VS Code：标题栏 35、按钮 46×34，配色固定深色（不跟随系统明暗）。
                // 标题贴左：VS Code 的标题栏是三段式（左：菜单/导航，中：命令中心，右：按钮），
                // 窗口标题不是居中的。
                CaptionHeightDip = 35;
                CaptionButtonWidthDip = 46;
                CaptionButtonHeightDip = 34;
                CaptionIconMarginDip = 12;
                CaptionTextAlignment = ContentAlignment.MiddleLeft;
                MinimizeButtonWidthDip = 0;
                break;

            case ChromeTitleBarStyle.Windows:
                // 贴近 Windows 11 原生（96dpi 实测一个原生 WPF Window）：
                // 标题栏可见高 31、按钮 36×22（SM_CXSIZE × SM_CYSIZE）、
                // 图标盒子 19×22 贴左且顶边在第 8 行（frame 内缩），标题左对齐
                CaptionHeightDip = 31;
                // 视觉格子 45（原生悬停块实测）= SM_CXSIZE(36) + 2×SM_CXPADDEDBORDER(4)；
                // 注意原生"命中带"只有 33 宽，那是内缩后的判定区，不是画出来的格子
                CaptionButtonWidthDip = 45;
                // 铺满整条标题栏；普通态第 0 行是顶边线，由绘制矩形按需让出（最大化时不画线，
                // 所以最大化时按钮要铺到第 0 行，否则顶部会露出一条底色）
                CaptionButtonHeightDip = 31;
                // 图标在标题栏左侧内缩 8px（原生实测图标落在客户区 8..23）
                CaptionIconMarginDip = 8;
                CaptionTextAlignment = ContentAlignment.MiddleLeft;
                MinimizeButtonWidthDip = 0;
                break;

            default:
                // Chrome 实测：标题栏 40、按钮 46×39、图标 12px 位、标题贴左。
                // 标题位置与 WPF 版保持一致：WPF 模板把标题 TextBlock 放在图标之后的横向
                // StackPanel 里，天然贴左；三套样式都贴左（不再有居中的那套）。
                CaptionHeightDip = 40;
                CaptionButtonWidthDip = 46;
                // Chrome 实测最小化按钮比其余两个窄 1px（45 / 46 / 46）
                MinimizeButtonWidthDip = 45;
                CaptionButtonHeightDip = 39;
                CaptionIconMarginDip = 12;
                CaptionTextAlignment = ContentAlignment.MiddleLeft;
                break;
        }

        ShowTitleBarIcon = true;
        ShowTopBorderLine = true;
    }

    /// <summary>
    /// 按当前的 <see cref="TitleBarPalette"/> 把颜色套到标题栏上。
    /// 几何不变，只改颜色 —— 这也是它与 <see cref="ChromeTitleBarStyle"/> 分成两个轴的原因。
    ///
    /// 配色**直接按枚举值取**，不看样式：同一套配色配到哪套骨架上都是同样的颜色。
    /// 只有 <see cref="ChromeTitleBarPalette.Default"/> 例外 —— 它的定义就是"该样式自带的那套"，
    /// 所以那一个分支才需要样式参数。
    /// </summary>
    private void ApplyTitleBarPalette(ChromeTitleBarStyle style)
    {
        var look = TitleBarPalette == ChromeTitleBarPalette.Default
            ? SystemTheme.Look(style)
            : ElementPlusTheme.Look(TitleBarPalette);
        var palette = look.Palette;
        ActiveCaptionColor = palette.ActiveCaption;
        InactiveCaptionColor = palette.InactiveCaption;
        CaptionTextColor = palette.CaptionText;
        InactiveCaptionTextColor = palette.InactiveCaptionText;
        CaptionButtonHoverColor = palette.ButtonHover;
        CaptionButtonPressedColor = palette.ButtonPressed;
        CloseButtonHoverColor = look.CloseButtonHover;
        CloseButtonPressedColor = look.CloseButtonPressed;
    }

    /// <summary>
    /// 最小化按钮的独立宽度（DIP）。0 表示与其余按钮同宽；Chrome 样式按实测设为 45
    /// （最大化/关闭是 46，Chrome 自己就不等宽）。
    /// </summary>
    [Category("WindowChromeKit")]
    [DefaultValue(0)]
    public int MinimizeButtonWidthDip
    {
        get => _minimizeButtonWidthDip;
        set => SetOption(ref _minimizeButtonWidthDip, Math.Max(0, value));
    }

    private void SetOption<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        UpdateFrameMetrics();
        PerformLayout();
        Invalidate();
    }
}
