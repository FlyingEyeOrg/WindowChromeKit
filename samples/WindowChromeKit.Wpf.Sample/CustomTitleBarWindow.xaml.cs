using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowChromeKit.Wpf.Sample;

public partial class CustomTitleBarWindow : ChromeWindow
{
    private bool _alternateTheme;

    public CustomTitleBarWindow()
    {
        InitializeComponent();
    }

    private void OnToggleThemeClicked(object sender, RoutedEventArgs eventArgs)
    {
        _alternateTheme = !_alternateTheme;
        ActiveTitleBarBackground = BrushFrom(_alternateTheme ? "#3D2352" : "#102A43");
        CaptionButtonHoverBackground = BrushFrom(_alternateTheme ? "#68478D" : "#334E68");
        CaptionButtonPressedBackground = BrushFrom(_alternateTheme ? "#815AC0" : "#486581");
    }

    private void OnNewWindowClicked(object sender, RoutedEventArgs eventArgs)
    {
        var window = new CustomTitleBarWindow { Owner = this };
        window.CenterOnTargetMonitor();
        window.Show();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs eventArgs) => Close();

    private void OnUseWpfIconClicked(object sender, RoutedEventArgs eventArgs) => Icon = null;

    private void OnUseWindows7IconClicked(object sender, RoutedEventArgs eventArgs) =>
        Icon = LoadLibraryIcon("Windows7WindowIcon.ico");

    private void OnUseWindows10IconClicked(object sender, RoutedEventArgs eventArgs) =>
        Icon = LoadLibraryIcon("Windows10WindowIcon.ico");

    private void OnShowTitleBarIconClicked(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
            ShowTitleBarIcon = menuItem.IsChecked;
    }

    private void OnAboutClicked(object sender, RoutedEventArgs eventArgs) =>
        MessageBox.Show(
            this,
            "WindowChromeKit 自定义标题栏菜单示例。",
            "关于 WindowChromeKit",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private static Brush BrushFrom(string value) =>
        (Brush)new BrushConverter().ConvertFromString(value)!;

    private static BitmapFrame LoadLibraryIcon(string fileName)
    {
        var icon = BitmapFrame.Create(new Uri(
            $"pack://application:,,,/WindowChromeKit.Wpf;component/Assets/{fileName}",
            UriKind.Absolute));
        if (icon.CanFreeze) icon.Freeze();
        return icon;
    }
}
