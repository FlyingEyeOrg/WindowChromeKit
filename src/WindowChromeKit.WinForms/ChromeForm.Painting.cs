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
        var maximized = _metrics.Maximized;
        var caption = new Rectangle(0, 0, client.Width, _metrics.CaptionHeight);

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
        var top = caption.Top + (caption.Height - _metrics.IconSize) / 2;
        var hdc = graphics.GetHdc();
        try
        {
            _ = NativeMethods.DrawIconEx(
                hdc,
                _metrics.IconMargin,
                top,
                icon,
                _metrics.IconSize,
                _metrics.IconSize,
                0,
                IntPtr.Zero,
                DiNormal);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private void PaintTitleBarText(Graphics graphics, Rectangle client, Color color)
    {
        var left = _metrics.IconMargin + (ShowTitleBarIcon ? _metrics.IconSize + ScaleDip(8, _metrics.Dpi) : 0);
        var right = client.Width - _metrics.CaptionButtonWidth * 3 - ScaleDip(8, _metrics.Dpi);
        if (right <= left)
            return;
        var bounds = new Rectangle(left, 0, right - left, _metrics.CaptionHeight);
        TextRenderer.DrawText(
            graphics,
            Text,
            ResolveCaptionFont(),
            bounds,
            color,
            TextFormatFlags.SingleLine
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
    }

    private void PaintCaptionButtons(Graphics graphics, Color glyphColor)
    {
        // 用实时度量判断：最大化时按钮画"还原"双框
        var actualMaximized = _metrics.Maximized;
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
            ClientRectangle.Right - (index + 1) * _metrics.CaptionButtonWidth,
            _metrics.CaptionButtonPaintTop,
            _metrics.CaptionButtonWidth,
            _metrics.CaptionButtonHeight);

    private void DrawCaptionGlyph(
        Graphics graphics,
        Rectangle bounds,
        ChromeCaptionButton button,
        bool maximized,
        Color color)
    {
        var centerX = (bounds.Left + bounds.Right) / 2;
        var centerY = (bounds.Top + bounds.Bottom) / 2;
        var half = ScaleDip(5, _metrics.Dpi);
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
        if (_scaledCaptionFont is not null && _scaledCaptionFontDpi == _metrics.Dpi)
            return _scaledCaptionFont;
        _scaledCaptionFont?.Dispose();
        var baseFont = SystemFonts.CaptionFont ?? DefaultFont;
        _scaledCaptionFont = new Font(
            baseFont.FontFamily,
            baseFont.SizeInPoints * _metrics.Dpi / 96f,
            baseFont.Style,
            GraphicsUnit.Point);
        _scaledCaptionFontDpi = _metrics.Dpi;
        return _scaledCaptionFont;
    }

    private void InvalidateCaption()
    {
        if (!IsHandleCreated)
            return;
        Invalidate(new Rectangle(0, 0, ClientSize.Width, _metrics.CaptionHeight));
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
