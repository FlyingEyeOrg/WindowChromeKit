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
            // 完全自定义：底色、边框线与按钮都由 OnPaintTitleBar 负责
            OnPaintTitleBar(new TitleBarPaintEventArgs(graphics, caption, active, maximized));
            return;
        }

        using (var background = new SolidBrush(active ? ActiveCaptionColor : InactiveCaptionColor))
            graphics.FillRectangle(background, caption);

        // 顶边 1px 边框线：Win10 的 DWM 只在非客户区画边框，顶部这一条要自己补；
        // 最大化时客户区正好等于工作区，画了会在屏幕顶端多出一条线（Chrome 最大化也没有）。
        // Win11 上 DWM 会在同一行覆盖它，因此对 Win11 无影响。
        if (ShowTopBorderLine && !maximized)
        {
            using var line = new Pen(active ? TopBorderLineActiveColor : TopBorderLineInactiveColor);
            graphics.DrawLine(line, 0, 0, client.Width - 1, 0);
        }

        var glyphColor = active ? CaptionTextColor : InactiveCaptionTextColor;
        PaintTitleBarIcon(graphics, caption);
        PaintTitleBarText(graphics, client, glyphColor);
        PaintCaptionButtons(graphics, glyphColor);

        OnPaintTitleBar(new TitleBarPaintEventArgs(graphics, caption, active, maximized));
    }

    private void PaintTitleBarIcon(Graphics graphics, Rectangle caption)
    {
        if (!ShowTitleBarIcon)
            return;
        var icon = NativeMethods.GetWindowSmallIcon(Handle);
        if (icon == IntPtr.Zero)
            return;
        // 图标在系统菜单命中盒子内居中（原生就是这么画的），而不是在整条标题栏里居中
        var top = caption.Top
            + Metrics.SystemMenuTop
            + Math.Max(0, (Metrics.SystemMenuHeight - Metrics.IconSize) / 2);
        // 图标通常是 32bpp 带 alpha：直接 DrawIconEx 到设备相关位图上会忽略 alpha，
        // 只剩 AND 掩码（看起来是纯白剪影）。这里先转成 ARGB 位图再画，保留颜色与透明。
        var source = Icon.FromHandle(icon);
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
            ? client.Width - Metrics.CaptionButtonWidth * 3 - ScaleDip(8, Metrics.Dpi)
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

    private Rectangle GetCaptionButtonPaintRect(int index) =>
        new(
            ClientRectangle.Right - (index + 1) * Metrics.CaptionButtonWidth,
            Metrics.CaptionButtonPaintTop,
            Metrics.CaptionButtonWidth,
            Metrics.CaptionButtonHeight);

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
