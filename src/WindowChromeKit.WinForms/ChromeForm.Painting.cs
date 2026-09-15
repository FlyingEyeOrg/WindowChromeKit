using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WindowChromeKit.WinForms.Internal;

namespace WindowChromeKit.WinForms;

/// <summary>标题栏绘制。默认与 C++ 示例、真实 Chrome 的布局一致（40 高、46x39 按钮、12 图标边距）。</summary>
public partial class ChromeForm
{
    private const int DiNormal = 0x0003;
    private Font? _scaledCaptionFont;
    private uint _scaledCaptionFontDpi;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        PaintTitleBar(e.Graphics);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scaledCaptionFont?.Dispose();
            _scaledCaptionFont = null;
        }
        base.Dispose(disposing);
    }

    /// <summary>标题栏绘制完成后的扩展点；可在此叠加自定义内容（默认不绘制任何东西）。</summary>
    protected virtual void OnPaintTitleBar(TitleBarPaintEventArgs e) { }

    private void PaintTitleBar(Graphics graphics)
    {
        var client = ClientRectangle;
        if (client.Width <= 0 || client.Height <= 0)
            return;

        var active = ActiveForm == this;
        var maximized = Metrics.Maximized;
        var caption = new Rectangle(0, 0, client.Width, Metrics.CaptionHeight);
        if (!ShowDefaultTitleBar)
        {
            // 完全自定义：标题栏底色、图标、文字与按钮都由 OnPaintTitleBar 负责。
            // 但**顶边线仍然绘制** —— 它是窗口边框的一部分（Win10 上 DWM 不画顶部这条边），
            // 不该因为自定义了标题栏内容就缺一条边。不需要时用 ShowTopBorderLine = false 关掉。
            OnPaintTitleBar(new TitleBarPaintEventArgs(graphics, caption, active, maximized));
            DrawTopBorderLine(graphics, active);
            return;
        }

        using (var background = new SolidBrush(active ? ActiveCaptionColor : InactiveCaptionColor))
            graphics.FillRectangle(background, caption);

        var glyphColor = active ? CaptionTextColor : InactiveCaptionTextColor;
        PaintTitleBarIcon(graphics, caption);
        PaintTitleBarText(graphics, client, glyphColor);
        PaintCaptionButtons(graphics, glyphColor);

        OnPaintTitleBar(new TitleBarPaintEventArgs(graphics, caption, active, maximized));

        // 顶边 1px 边框线最后画，作为最上层（与 WPF 模板的覆盖层一致）：
        // 无论按钮/自定义内容画到哪里，这条边都不会被盖住。绘制实现见
        // ChromeFrame.TopBorderLine.cs（含实测反解出的 DWM 混合参数与最大化判定）。
        DrawTopBorderLine(graphics, active);
    }

    private void PaintTitleBarIcon(Graphics graphics, Rectangle caption)
    {
        if (!ShowTitleBarIcon)
            return;
        // 与公开的 EffectiveTitleBarIcon 走同一条解析链（单一来源）：
        // 使用者自绘标题栏时拿到的图标与这里画的必然是同一个。
        using var source = EffectiveTitleBarIcon;
        if (source is null)
            return;
        // 图标在系统菜单命中盒子内居中（原生就是这么画的），而不是在整条标题栏里居中
        var top = caption.Top
            + Metrics.SystemMenuTop
            + Math.Max(0, (Metrics.SystemMenuHeight - Metrics.IconSize) / 2);
        // 图标通常是 32bpp 带 alpha：直接 DrawIconEx 到设备相关位图上会忽略 alpha，
        // 只剩 AND 掩码（看起来是纯白剪影）。这里先转成 ARGB 位图再画，保留颜色与透明。
        using var bitmap = source.ToBitmap();
        graphics.DrawImage(
            bitmap,
            new Rectangle(Metrics.IconMargin, top, Metrics.IconSize, Metrics.IconSize));
    }

    private void PaintTitleBarText(Graphics graphics, Rectangle client, Color color)
    {
        // 与 WPF 版一致：设置了标题栏内容插槽后，默认标题文字不再绘制（避免和自定义内容重叠）
        if (TitleBarContent is not null)
            return;
        var left = Metrics.IconMargin + (ShowTitleBarIcon ? Metrics.IconSize + ScaleDip(8, Metrics.Dpi) : 0);
        // 右侧避让：设置了操作插槽时贴到插槽左侧，否则避让三个窗口按钮
        var right = TitleBarActionsBounds.IsEmpty
            ? client.Width - Metrics.TotalCaptionButtonWidth - ScaleDip(8, Metrics.Dpi)
            : TitleBarActionsBounds.Left - ScaleDip(8, Metrics.Dpi);
        if (right <= left)
            return;
        var bounds = new Rectangle(left, 0, right - left, Metrics.CaptionHeight);
        var flags = TextFormatFlags.SingleLine
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix;
        flags |= CaptionTextAlignment switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft =>
                TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight =>
                TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };
        TextRenderer.DrawText(graphics, Text, ResolveCaptionFont(), bounds, color, flags);
    }

    private void PaintCaptionButtons(Graphics graphics, Color glyphColor)
    {
        // 用实时度量判断：最大化时按钮画"还原"双框
        var actualMaximized = Metrics.Maximized;
        for (var index = 0; index < 3; index++)
        {
            var button = index switch
            {
                0 => ChromeCaptionButton.Close,
                1 => ChromeCaptionButton.Maximize,
                _ => ChromeCaptionButton.Minimize,
            };
            var bounds = GetCaptionButtonPaintRect(index);
            var hot = _hotButton == button;
            var pressed = _pressedButton == button;
            var color = glyphColor;
            if (hot || pressed)
            {
                var isClose = button == ChromeCaptionButton.Close;
                var background = isClose
                    ? (pressed ? CloseButtonPressedColor : CloseButtonHoverColor)
                    : (pressed ? CaptionButtonPressedColor : CaptionButtonHoverColor);
                using var brush = new SolidBrush(background);
                graphics.FillRectangle(brush, bounds);
                color = isClose ? Color.White : CaptionTextColor;
            }
            DrawCaptionGlyph(graphics, bounds, button, actualMaximized, color);
        }
    }

    /// <summary>
    /// 按钮的绘制矩形。普通态第 0 行是顶边线（与 WPF 模板的 BorderThickness、原生标题栏一致），
    /// 按钮填充与字形都要让出这一行，否则 hover 会把那条线盖掉。
    /// </summary>
    private Rectangle GetCaptionButtonPaintRect(int index)
    {
        // 与 Chrome 浏览器实测一致：
        //   普通态：第 0 行是顶边线 → 按钮从第 1 行起，一直铺到标题栏底部（39 高）
        //   最大化：不画顶边线 → 按钮从第 0 行起，铺满整条标题栏（40 高）
        // 与命中矩形共享同一套顶边/高度（单一来源，避免绘制与命中脱节）
        var top = Metrics.CaptionButtonTop;
        var height = Metrics.CaptionHitHeight;
        var button = index switch
        {
            0 => ChromeCaptionButton.Close,
            1 => ChromeCaptionButton.Maximize,
            _ => ChromeCaptionButton.Minimize,
        };
        // 从右往左排列：关闭贴右，往左依次是最大化、最小化（Chrome 实测 45/46/46 不等宽）
        var offset = 0;
        if (button != ChromeCaptionButton.Close)
            offset += Metrics.GetCaptionButtonWidth(ChromeCaptionButton.Close);
        if (button == ChromeCaptionButton.Minimize)
            offset += Metrics.GetCaptionButtonWidth(ChromeCaptionButton.Maximize);
        var width = Metrics.GetCaptionButtonWidth(button);
        return new Rectangle(
            ClientRectangle.Right - offset - width,
            top,
            width,
            height);
    }

    private void DrawCaptionGlyph(
        Graphics graphics,
        Rectangle bounds,
        ChromeCaptionButton button,
        bool maximized,
        Color color)
    {
        var centerX = (bounds.Left + bounds.Right) / 2;
        var centerY = (bounds.Top + bounds.Bottom) / 2;
        var half = ScaleDip(5, Metrics.Dpi);
        using var pen = new Pen(color);
        pen.Alignment = PenAlignment.Center;
        switch (button)
        {
            case ChromeCaptionButton.Minimize:
                graphics.DrawLine(pen, centerX - half, centerY, centerX + half + 1, centerY);
                break;
            case ChromeCaptionButton.Maximize when !maximized:
                graphics.DrawRectangle(pen, centerX - half, centerY - half, half * 2 + 1, half * 2 + 1);
                break;
            case ChromeCaptionButton.Maximize:
                // 还原：两个错开的方框
                graphics.DrawRectangle(pen, centerX - half, centerY - half + 2, half * 2 - 1, half * 2 - 1);
                graphics.DrawLine(pen, centerX - half + 2, centerY - half + 2, centerX - half + 2, centerY - half);
                graphics.DrawLine(pen, centerX - half + 2, centerY - half, centerX + half + 1, centerY - half);
                graphics.DrawLine(pen, centerX + half + 1, centerY - half, centerX + half + 1, centerY + half - 2);
                break;
            case ChromeCaptionButton.Close:
                graphics.DrawLine(pen, centerX - half, centerY - half, centerX + half + 1, centerY + half + 1);
                graphics.DrawLine(pen, centerX + half, centerY - half, centerX - half - 1, centerY + half + 1);
                break;
        }
    }

    private Font ResolveCaptionFont()
    {
        if (CaptionFont is not null)
            return CaptionFont;
        if (_scaledCaptionFont is not null && _scaledCaptionFontDpi == Metrics.Dpi)
            return _scaledCaptionFont;
        _scaledCaptionFont?.Dispose();
        var baseFont = SystemFonts.CaptionFont ?? DefaultFont;
        _scaledCaptionFont = new Font(
            baseFont.FontFamily,
            baseFont.SizeInPoints * Metrics.Dpi / 96f,
            baseFont.Style,
            GraphicsUnit.Point);
        _scaledCaptionFontDpi = Metrics.Dpi;
        return _scaledCaptionFont;
    }

    private void InvalidateCaption()
    {
        if (!IsHandleCreated)
            return;
        Invalidate(new Rectangle(0, 0, ClientSize.Width, Metrics.CaptionHeight));
    }
}

/// <summary>标题栏绘制参数，供 <see cref="ChromeForm.OnPaintTitleBar"/> 使用。</summary>
public sealed class TitleBarPaintEventArgs : EventArgs
{
    internal TitleBarPaintEventArgs(Graphics graphics, Rectangle captionBounds, bool active, bool maximized)
    {
        Graphics = graphics;
        CaptionBounds = captionBounds;
        Active = active;
        Maximized = maximized;
    }

    /// <summary>标题栏所在的绘图表面（客户区坐标）。</summary>
    public Graphics Graphics { get; }

    /// <summary>标题栏矩形（客户区坐标，含图标、标题与按钮）。</summary>
    public Rectangle CaptionBounds { get; }

    /// <summary>窗口当前是否激活。</summary>
    public bool Active { get; }

    /// <summary>窗口当前是否最大化。</summary>
    public bool Maximized { get; }
}
