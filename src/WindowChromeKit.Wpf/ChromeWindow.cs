using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>提供完全可模板化标题栏以及原生缩放、系统菜单、阴影和 Snap Layout 的 WPF 窗口。</summary>
[TemplatePart(Name = PartTitleBar, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartSystemMenu, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartMinimizeButton, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartMaximizeButton, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartCloseButton, Type = typeof(FrameworkElement))]
public partial class ChromeWindow : ChromeFrame
{
    static ChromeWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(typeof(ChromeWindow))
        );
    }

    public ChromeWindow()
    {
        // 默认样式：Chrome 实测几何 + 跟随系统明暗
        ApplyTitleBarStyle(TitleBarStyle);
        Activated += OnActivationChanged;
        Deactivated += OnActivationChanged;
        CommandBindings.Add(
            new CommandBinding(
                SystemCommands.MinimizeWindowCommand,
                ExecuteMinimizeCommand,
                CanExecuteMinimizeCommand
            )
        );
        CommandBindings.Add(
            new CommandBinding(
                SystemCommands.MaximizeWindowCommand,
                ExecuteMaximizeCommand,
                CanExecuteMaximizeCommand
            )
        );
        CommandBindings.Add(
            new CommandBinding(
                SystemCommands.RestoreWindowCommand,
                ExecuteRestoreCommand,
                CanExecuteRestoreCommand
            )
        );
        CommandBindings.Add(
            new CommandBinding(
                SystemCommands.CloseWindowCommand,
                ExecuteCloseCommand,
                CanExecuteAlways
            )
        );
        CommandBindings.Add(
            new CommandBinding(
                SystemCommands.ShowSystemMenuCommand,
                ExecuteShowSystemMenuCommand,
                CanExecuteAlways
            )
        );
    }

    public event EventHandler? DisplayConfigurationChanged;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs eventArgs)
    {
        base.OnPropertyChanged(eventArgs);
        if (eventArgs.Property == IconProperty)
            ScheduleEffectiveTitleBarIconRefresh();
    }

    protected override void OnFrameResizeModeChanged() => ApplyResizeMode();

    private void ExecuteMinimizeCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.MinimizeWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteMinimizeCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = ResizeMode != ResizeMode.NoResize;
        eventArgs.Handled = true;
    }

    private void ExecuteMaximizeCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.MaximizeWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteMaximizeCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = IsResizable && WindowState != WindowState.Maximized;
        eventArgs.Handled = true;
    }

    private void ExecuteRestoreCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.RestoreWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteRestoreCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = WindowState != WindowState.Normal;
        eventArgs.Handled = true;
    }

    private void ExecuteCloseCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.CloseWindow(this);
        eventArgs.Handled = true;
    }

    private static void CanExecuteAlways(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = true;
        eventArgs.Handled = true;
    }

    private void ExecuteShowSystemMenuCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        var bounds = _systemMenu is null ? default : GetScreenBounds(_systemMenu);
        var location =
            bounds.Width > 0 && bounds.Height > 0
                ? new Point(bounds.Left, bounds.Bottom)
                : PointToScreen(new Point(0, TitleBarHeight));
        SystemCommands.ShowSystemMenu(this, location);
        eventArgs.Handled = true;
    }
}
