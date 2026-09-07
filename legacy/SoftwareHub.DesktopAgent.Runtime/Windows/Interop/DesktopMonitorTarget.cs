namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>目标显示器的物理工作区和有效 DPI。</summary>
internal readonly record struct DesktopMonitorTarget(
    IntPtr Handle,
    NativeRectangle WorkArea,
    uint DpiX,
    uint DpiY = 0)
{
    internal double WorkAreaWidthDip => DesktopWindowPlacement.PixelsToDip(WorkArea.Width, EffectiveDpiX);

    internal double WorkAreaHeightDip => DesktopWindowPlacement.PixelsToDip(WorkArea.Height, EffectiveDpiY);

    internal uint EffectiveDpiX => DpiX == 0 ? 96u : DpiX;

    internal uint EffectiveDpiY => DpiY == 0 ? EffectiveDpiX : DpiY;
}
