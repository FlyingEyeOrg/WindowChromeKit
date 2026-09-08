using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowChromeKit.Wpf;

/// <summary>Provides the optional icons embedded in the WindowChromeKit assembly.</summary>
public static class WindowChromeIcons
{
    private static readonly Lazy<ImageSource> Windows7Icon = new(
        () => Load("Windows7WindowIcon.ico"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<ImageSource> Windows10Icon = new(
        () => Load("Windows10WindowIcon.ico"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets the Windows 7 style icon, loading it on first access.</summary>
    public static ImageSource Windows7 => Windows7Icon.Value;

    /// <summary>Gets the Windows 10 style icon, loading it on first access.</summary>
    public static ImageSource Windows10 => Windows10Icon.Value;

    private static BitmapFrame Load(string fileName)
    {
        var assemblyName = typeof(WindowChromeIcons).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The WindowChromeKit assembly has no name.");

        var frame = BitmapFrame.Create(new Uri(
            $"pack://application:,,,/{assemblyName};component/Assets/{fileName}",
            UriKind.Absolute));

        if (frame.CanFreeze)
            frame.Freeze();

        return frame;
    }
}
