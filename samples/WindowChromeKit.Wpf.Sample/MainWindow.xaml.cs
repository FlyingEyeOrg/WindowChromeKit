using System.Windows;
using System.Windows.Controls;

namespace WindowChromeKit.Wpf.Sample;

public partial class MainWindow : ChromeWindow
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnResizeModeChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (!IsInitialized || ResizeModeSelector.SelectedItem is not ComboBoxItem item) return;
        if (Enum.TryParse(item.Content?.ToString(), out ResizeMode mode)) ResizeMode = mode;
    }

    private void OnCenterClicked(object sender, RoutedEventArgs eventArgs) => CenterOnTargetMonitor();

    private void OnConstrainClicked(object sender, RoutedEventArgs eventArgs) => ConstrainToWorkArea();

    private void OnOpenOwnedWindowClicked(object sender, RoutedEventArgs eventArgs)
    {
        var child = new ChromeWindow
        {
            Owner = this,
            Title = "Owned ChromeWindow",
            Width = 460,
            Height = 280,
            ResizeMode = ResizeMode.CanResize,
            Content = new TextBlock
            {
                Margin = new Thickness(24),
                TextWrapping = TextWrapping.Wrap,
                Text = "This window uses the standard WPF Owner relationship and the same native chrome behavior.",
            },
        };
        child.CenterOnTargetMonitor();
        child.ShowDialog();
    }
}
