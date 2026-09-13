using System.Drawing;
using System.Threading;

namespace WindowChromeKit.WinForms;

/// <summary>
/// 本程序集内嵌的可选窗口图标，与 WPF 版的 <c>WindowChromeIcons</c> 同名同语义。
/// 直接赋给 <see cref="System.Windows.Forms.Form.Icon"/> 即可，例如：
/// <code>Icon = WindowChromeIcons.Windows10;</code>
/// 赋值后会同时作用于标题栏图标、任务栏和 Alt+Tab，不改变 WinForms 原有的窗口图标规则。
/// </summary>
public static class WindowChromeIcons
{
    private static readonly Lazy<Icon> Windows7Icon = new(
        () => Load("Windows7WindowIcon.ico"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<Icon> Windows10Icon = new(
        () => Load("Windows10WindowIcon.ico"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Windows 7 风格的窗口图标（首次访问时加载）。</summary>
    public static Icon Windows7 => Windows7Icon.Value;

    /// <summary>Windows 10 / 11 风格的窗口图标（首次访问时加载）。</summary>
    public static Icon Windows10 => Windows10Icon.Value;

    private static Icon Load(string fileName)
    {
        var assembly = typeof(WindowChromeIcons).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            $"{typeof(WindowChromeIcons).Namespace}.Assets.{fileName}")
            ?? throw new InvalidOperationException(
                $"内嵌图标 \"{fileName}\" 不存在，请检查 WindowChromeKit.WinForms.csproj 的 EmbeddedResource 配置。");
        return new Icon(stream);
    }
}
