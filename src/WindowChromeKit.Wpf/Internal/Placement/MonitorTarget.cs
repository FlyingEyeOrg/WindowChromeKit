namespace WindowChromeKit.Wpf.Internal;

/// <summary>目标显示器的物理工作区和有效 DPI。</summary>
internal readonly record struct MonitorTarget(
    IntPtr Handle,
    NativeRectangle WorkArea,
    uint DpiX,
    uint DpiY = 0)
{
    internal double WorkAreaWidthDip => WindowPlacement.PixelsToDip(WorkArea.Width, EffectiveDpiX);

    internal double WorkAreaHeightDip => WindowPlacement.PixelsToDip(WorkArea.Height, EffectiveDpiY);

    internal uint EffectiveDpiX => DpiX == 0 ? 96u : DpiX;

    internal uint EffectiveDpiY => DpiY == 0 ? EffectiveDpiX : DpiY;
}
