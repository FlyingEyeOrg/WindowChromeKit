namespace SoftwareHub.DesktopAgent.Runtime;

internal readonly record struct RenderedPageMetrics(
    double ViewportWidth,
    double ViewportHeight,
    double ContentWidth,
    double ContentHeight);
