using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace WindowChromeKit.WinForms;

/// <summary>标题栏的可定制度量与配色。所有度量单位是 DIP，使用时按窗口 DPI 缩放。</summary>
public partial class ChromeForm
{
    private int _captionHeightDip = 40;
    private int _captionButtonWidthDip = 46;
    private int _captionButtonHeightDip = 39;
    private int _captionIconMarginDip = 12;
    private int _topResizeBandDip = 6;
    private bool _showTitleBarIcon = true;
    private Font? _captionFont;
    private Color _activeCaptionColor = Color.White;
    private Color _inactiveCaptionColor = Color.FromArgb(0xF1, 0xF3, 0xF4);
    private Color _captionTextColor = Color.FromArgb(0x20, 0x21, 0x24);
    private Color _inactiveCaptionTextColor = Color.FromArgb(0x80, 0x86, 0x8B);
    private Color _captionButtonHoverColor = Color.FromArgb(0xE8, 0xEA, 0xED);
    private Color _captionButtonPressedColor = Color.FromArgb(0xDA, 0xDC, 0xE0);
    private Color _closeButtonHoverColor = Color.FromArgb(0xE8, 0x11, 0x23);
    private Color _closeButtonPressedColor = Color.FromArgb(0xF1, 0x70, 0x7A);
    private Color _topBorderLineActiveColor = Color.FromArgb(0x70, 0x70, 0x70);
    private Color _topBorderLineInactiveColor = Color.FromArgb(0xAA, 0xAA, 0xAA);
    private bool _showTopBorderLine = true;

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

    /// <summary>顶边 1px 边框线（激活）：Win10 的 DWM 只在非客户区画边框，顶部这一条要自己补。</summary>
    [Category("WindowChromeKit")]
    public Color TopBorderLineActiveColor
    {
        get => _topBorderLineActiveColor;
        set => SetOption(ref _topBorderLineActiveColor, value);
    }

    /// <summary>顶边 1px 边框线（失活）。</summary>
    [Category("WindowChromeKit")]
    public Color TopBorderLineInactiveColor
    {
        get => _topBorderLineInactiveColor;
        set => SetOption(ref _topBorderLineInactiveColor, value);
    }

    /// <summary>是否自绘顶边线。Win11 上 DWM 会覆盖同一行，因此对 Win11 无影响。</summary>
    [Category("WindowChromeKit")]
    [DefaultValue(true)]
    public bool ShowTopBorderLine
    {
        get => _showTopBorderLine;
        set => SetOption(ref _showTopBorderLine, value);
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
