using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>提供 DesktopAgent 自带且可跨窗口安全复用的默认图标。</summary>
internal static class DesktopWindowIconResources
{
    private static readonly object Gate = new();
    private static ImageSource? _default;

    public static ImageSource Default
    {
        get
        {
            lock (Gate)
            {
                return _default ??= LoadDefault();
            }
        }
    }

    private static BitmapImage LoadDefault()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/SoftwareHub.ico", UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
