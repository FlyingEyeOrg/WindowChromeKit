using System.Windows;
using System.Windows.Media;

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

    private static Brush BrushFrom(string value) =>
        (Brush)new BrushConverter().ConvertFromString(value)!;
}
