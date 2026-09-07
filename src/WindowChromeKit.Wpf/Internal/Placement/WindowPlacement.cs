namespace WindowChromeKit.Wpf.Internal;

/// <summary>在目标显示器物理像素空间中计算窗口最终外框。</summary>
internal static class WindowPlacement
{
    private const double DefaultDpi = 96;

    internal static NativeRectangle Center(
        MonitorTarget target,
        double widthDip,
        double heightDip,
        NativeRectangle? ownerRectangle = null)
    {
        var width = Math.Min(target.WorkArea.Width, Math.Max(1, DipToPixels(widthDip, target.EffectiveDpiX)));
        var height = Math.Min(target.WorkArea.Height, Math.Max(1, DipToPixels(heightDip, target.EffectiveDpiY)));
        var anchor = ownerRectangle ?? target.WorkArea;
        var left = anchor.Left + (anchor.Width - width) / 2;
        var top = anchor.Top + (anchor.Height - height) / 2;
        left = Math.Clamp(left, target.WorkArea.Left, Math.Max(target.WorkArea.Left, target.WorkArea.Right - width));
        top = Math.Clamp(top, target.WorkArea.Top, Math.Max(target.WorkArea.Top, target.WorkArea.Bottom - height));
        return new NativeRectangle(left, top, left + width, top + height);
    }

    internal static NativeRectangle Clamp(MonitorTarget target, NativeRectangle bounds)
    {
        var width = Math.Min(target.WorkArea.Width, Math.Max(1, bounds.Width));
        var height = Math.Min(target.WorkArea.Height, Math.Max(1, bounds.Height));
        var left = Math.Clamp(
            bounds.Left,
            target.WorkArea.Left,
            Math.Max(target.WorkArea.Left, target.WorkArea.Right - width));
        var top = Math.Clamp(
            bounds.Top,
            target.WorkArea.Top,
            Math.Max(target.WorkArea.Top, target.WorkArea.Bottom - height));
        return new NativeRectangle(left, top, left + width, top + height);
    }

    internal static int DipToPixels(double value, uint dpi) =>
        checked((int)Math.Round(value * (dpi == 0 ? DefaultDpi : dpi) / DefaultDpi));

    internal static double PixelsToDip(int value, uint dpi) =>
        value * DefaultDpi / (dpi == 0 ? DefaultDpi : dpi);
}
