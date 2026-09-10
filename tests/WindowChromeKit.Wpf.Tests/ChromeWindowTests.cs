using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WindowChromeKit.Wpf.Tests;

public sealed class ChromeWindowTests
{
    [Fact]
    public void DependencyPropertiesExposeDefaultsAndValidateMetrics() => RunSta(() =>
    {
        var window = new ChromeWindow();

        Assert.Equal(35, window.TitleBarHeight);
        Assert.Equal(46, window.CaptionButtonWidth);
        Assert.Equal(ResizeMode.CanResize, window.ResizeMode);
        Assert.Null(window.Icon);
        Assert.True(window.ShowTitleBarIcon);
        Assert.Null(window.EffectiveTitleBarIcon);
        Assert.False(window.UseLayoutRounding);
        Assert.Equal(ChromeHitTestRole.Default, window.HoveredChromeRole);
        Assert.Equal(ChromeHitTestRole.Default, window.PressedChromeRole);
        Assert.Equal(0.4, window.CaptionButtonDisabledOpacity);
        Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
        Assert.Throws<ArgumentException>(() => window.TitleBarHeight = 0);
        Assert.Throws<ArgumentException>(() => window.CaptionButtonWidth = double.NaN);
        Assert.Throws<ArgumentException>(() => window.CaptionButtonDisabledOpacity = 1.1);
        Assert.Throws<ArgumentException>(() =>
            ChromeWindow.SetHitTestRole(window, (ChromeHitTestRole)999));
    });

    [Fact]
    public void OptionalWindowIconResourcesCanBeLoaded() => RunSta(() =>
    {
        var windows7 = BitmapFrame.Create(new Uri(
            "pack://application:,,,/WindowChromeKit.Wpf;component/Assets/Windows7WindowIcon.ico",
            UriKind.Absolute));
        var windows10 = BitmapFrame.Create(new Uri(
            "pack://application:,,,/WindowChromeKit.Wpf;component/Assets/Windows10WindowIcon.ico",
            UriKind.Absolute));

        Assert.True(windows7.PixelWidth > 0);
        Assert.True(windows7.PixelHeight > 0);
        Assert.True(windows10.PixelWidth > 0);
        Assert.True(windows10.PixelHeight > 0);
    });

    [Fact]
    public void BuiltInWindowIconsAreLoadedLazilyAndCached() => RunSta(() =>
    {
        var windows7 = WindowChromeIcons.Windows7;
        var windows10 = WindowChromeIcons.Windows10;

        Assert.Same(windows7, WindowChromeIcons.Windows7);
        Assert.Same(windows10, WindowChromeIcons.Windows10);
        Assert.True(windows7.Width > 0);
        Assert.True(windows7.Height > 0);
        Assert.True(windows10.Width > 0);
        Assert.True(windows10.Height > 0);
    });

    [Fact]
    public void TitleBarSlotsAndInheritedRolesKeepInteractiveControlsInClientArea() => RunSta(() =>
    {
        var interactive = new Border
        {
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
        };
        ChromeWindow.SetHitTestRole(interactive, ChromeHitTestRole.Client);
        var titleContent = new Grid { Background = Brushes.Transparent };
        titleContent.Children.Add(interactive);
        var action = new Button { Width = 80, Content = "操作" };
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarContent = titleContent,
            TitleBarActions = action,
            Content = new Border(),
        };

        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        Assert.Equal(WindowFrameHitTest.Client, HitTest(handle, interactive));
        Assert.Equal(WindowFrameHitTest.Client, HitTest(handle, action));
        var captionPoint = titleContent.PointToScreen(new Point(titleContent.ActualWidth - 2, titleContent.ActualHeight / 2));
        Assert.Equal(WindowFrameHitTest.Caption, SendHitTest(handle, captionPoint));

        ChromeWindow.SetHitTestRole(interactive, ChromeHitTestRole.SystemMenu);
        Assert.Equal(WindowFrameHitTest.SystemMenu, HitTest(handle, interactive));
        window.Close();
    });

    [Fact]
    public void EvenHeightTitleBarTextBoxKeepsItsTextAlignedWithButtonText() => RunSta(() =>
    {
        var textBox = new TextBox
        {
            Width = 220,
            Height = 20,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "标题栏输入框",
        };
        ChromeWindow.SetHitTestRole(textBox, ChromeHitTestRole.Client);
        var titleContent = new Grid { Background = Brushes.Transparent };
        titleContent.Children.Add(textBox);
        var action = new Button
        {
            Width = 74,
            Height = 27,
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Content = "操作",
        };
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarContent = titleContent,
            TitleBarActions = action,
        };

        _ = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var textBoxView = Assert.IsAssignableFrom<UIElement>(
            FindVisualDescendant(textBox, element => element.GetType().Name == "TextBoxView"));
        var buttonText = Assert.IsType<TextBlock>(
            FindVisualDescendant(action, element => element is TextBlock));
        var textBoxCenter = textBoxView.PointToScreen(
            new Point(0, textBoxView.RenderSize.Height / 2));
        var buttonTextCenter = buttonText.PointToScreen(
            new Point(0, buttonText.RenderSize.Height / 2));

        Assert.InRange(Math.Abs(textBoxCenter.Y - buttonTextCenter.Y), 0, 0.25);
        window.Close();
    });

    [Fact]
    public void ResizeOverlaySynchronizesWithChromeHitTestRoles() => RunSta(() =>
    {
        var textBox = new TextBox
        {
            Width = 140,
            Height = 25,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "标题栏输入框",
        };
        ChromeWindow.SetHitTestRole(textBox, ChromeHitTestRole.Client);
        var titleContent = new Grid { Background = Brushes.Transparent };
        titleContent.Children.Add(textBox);
        var action = new Button
        {
            Width = 90,
            Height = 35,
            Content = "标题栏操作",
        };
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "Overlay hit test",
            TitleBarContent = titleContent,
            TitleBarActions = action,
        };

        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();
        window.SynchronizeResizeOverlay();

        var overlay = window.ResizeOverlayHandle;
        Assert.NotEqual(IntPtr.Zero, overlay);
        Assert.Equal(
            NativeWindowMethods.GetWindowThreadProcessId(handle, out _),
            NativeWindowMethods.GetWindowThreadProcessId(overlay, out _));

        var titleBar = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartTitleBar, window));
        var minimize = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var actionPoint = action.PointToScreen(new Point(action.ActualWidth / 2, 1));
        var minimizePoint = minimize.PointToScreen(new Point(minimize.ActualWidth / 2, 1));
        var textBoxPoint = textBox.PointToScreen(new Point(textBox.ActualWidth / 2, 1));
        var captionPoint = titleBar.PointToScreen(new Point(titleBar.ActualWidth / 2, 1));

        Assert.Equal(WindowResizeOverlay.HitTransparent, SendHitTest(overlay, actionPoint));
        Assert.Equal(WindowResizeOverlay.HitTransparent, SendHitTest(overlay, minimizePoint));
        Assert.Equal(WindowResizeOverlay.HitTransparent, SendHitTest(overlay, textBoxPoint));
        Assert.Equal(WindowFrameHitTest.Top, SendHitTest(overlay, captionPoint));

        ChromeWindow.SetHitTestRole(action, ChromeHitTestRole.Caption);
        Assert.Equal(WindowFrameHitTest.Top, SendHitTest(overlay, actionPoint));
        ChromeWindow.SetHitTestRole(action, ChromeHitTestRole.Client);
        Assert.Equal(WindowResizeOverlay.HitTransparent, SendHitTest(overlay, actionPoint));

        Assert.True(NativeWindowMethods.GetWindowRect(handle, out var ownerBounds));
        Assert.Equal(
            WindowFrameHitTest.Left,
            SendHitTest(overlay, new Point(ownerBounds.Left - 1, ownerBounds.Top + 100)));
        Assert.Equal(
            WindowFrameHitTest.Bottom,
            SendHitTest(overlay, new Point(ownerBounds.Left + 100, ownerBounds.Bottom)));

        window.Close();
    });

    [Fact]
    public void OptionalTemplatePartsCanBeMissingAndTemplateCanBeReapplied() => RunSta(() =>
    {
        const string templateXaml = """
            <ControlTemplate
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:chrome="https://windowchromekit.dev/wpf"
                TargetType="{x:Type chrome:ChromeWindow}">
                <Grid Background="White" />
            </ControlTemplate>
            """;
        var window = new ChromeWindow
        {
            Width = 500,
            Height = 300,
            ShowInTaskbar = false,
            ShowActivated = false,
            Template = (ControlTemplate)XamlReader.Parse(templateXaml),
        };

        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.ApplyTemplate();
        window.Template = (ControlTemplate)XamlReader.Parse(templateXaml);
        window.ApplyTemplate();
        window.UpdateLayout();

        var origin = new NativePoint(0, 0);
        Assert.True(NativeWindowMethods.ClientToScreen(handle, ref origin));
        Assert.Equal(
            WindowFrameHitTest.Client,
            SendHitTest(handle, new Point(origin.X + 20, origin.Y + 20)));
        window.Close();
    });

    [Fact]
    public void CaptionCommandsHonorStateResizeModeAndCloseCancellation() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 500,
            Height = 300,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        _ = new WindowInteropHelper(window).EnsureHandle();
        window.Show();

        Assert.True(SystemCommands.MinimizeWindowCommand.CanExecute(null, window));
        Assert.True(SystemCommands.MaximizeWindowCommand.CanExecute(null, window));
        Assert.True(window.TryExecuteCaptionButton(ChromeHitTestRole.MaximizeButton, ChromeHitTestRole.MaximizeButton));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.True(window.TryExecuteCaptionButton(ChromeHitTestRole.MaximizeButton, ChromeHitTestRole.MaximizeButton));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.ResizeMode = ResizeMode.CanMinimize;
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.True(SystemCommands.MinimizeWindowCommand.CanExecute(null, window));
        Assert.False(SystemCommands.MaximizeWindowCommand.CanExecute(null, window));
        Assert.False(window.TryExecuteCaptionButton(ChromeHitTestRole.MaximizeButton, ChromeHitTestRole.MaximizeButton));

        window.ResizeMode = ResizeMode.NoResize;
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.False(SystemCommands.MinimizeWindowCommand.CanExecute(null, window));

        var cancelClose = true;
        window.Closing += (_, eventArgs) => eventArgs.Cancel = cancelClose;
        Assert.True(window.TryExecuteCaptionButton(ChromeHitTestRole.CloseButton, ChromeHitTestRole.CloseButton));
        Assert.True(window.IsVisible);
        cancelClose = false;
        window.Close();
    });

    [Fact]
    public void DefaultTemplateHostsContentAndTracksResizeMode() => RunSta(() =>
    {
        var content = new Border();
        var window = new ChromeWindow
        {
            Width = 800,
            Height = 500,
            Title = "Chrome test",
            Content = content,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            ShowActivated = false,
        };

        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();
        window.SynchronizeResizeOverlay();

        Assert.Null(window.Icon);
        Assert.NotNull(window.EffectiveTitleBarIcon);
        var titleBar = Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName(ChromeWindow.PartTitleBar, window));
        var systemMenu = Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        var minimize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var maximize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));
        var close = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartCloseButton, window));
        Assert.Equal(35, titleBar.ActualHeight, 3);
        Assert.Equal(46, close.ActualWidth, 3);
        Assert.Same(window.InactiveTitleBarForeground, titleBar.GetValue(TextElement.ForegroundProperty));
        Assert.True(content.ActualWidth > 0);
        Assert.True(content.ActualHeight > 0);
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        Assert.Equal(36, systemMenu.ActualWidth, 3);
        Assert.Equal(WindowFrameHitTest.SystemMenu, HitTest(handle, systemMenu));

        window.ShowTitleBarIcon = false;
        window.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, systemMenu.Visibility);
        Assert.Equal(0, systemMenu.ActualWidth, 3);
        window.ShowTitleBarIcon = true;
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, systemMenu.Visibility);

        var explicitIcon = new DrawingImage(new GeometryDrawing(
            Brushes.DodgerBlue,
            null,
            new RectangleGeometry(new Rect(0, 0, 16, 16))));
        explicitIcon.Freeze();
        window.Icon = explicitIcon;
        Assert.Same(explicitIcon, window.EffectiveTitleBarIcon);
        window.Icon = null;
        window.Dispatcher.Invoke(DispatcherPriority.Loaded, () => { });
        Assert.NotNull(window.EffectiveTitleBarIcon);

        var overlay = window.ResizeOverlayHandle;
        Assert.NotEqual(IntPtr.Zero, overlay);
        Assert.True(NativeWindowMethods.IsWindowVisible(overlay));
        Assert.True(NativeWindowMethods.GetWindowRect(handle, out var bounds));
        Assert.True(NativeWindowMethods.GetClientRect(handle, out var client));
        Assert.Equal(bounds.Width, client.Width);
        Assert.Equal(bounds.Height, client.Height);
        var clientOrigin = new NativePoint(0, 0);
        Assert.True(NativeWindowMethods.ClientToScreen(handle, ref clientOrigin));
        var titleOrigin = titleBar.PointToScreen(new Point());
        var titleBottom = titleBar.PointToScreen(new Point(0, titleBar.ActualHeight));
        var contentOrigin = content.PointToScreen(new Point());
        var contentOpposite = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
        Assert.InRange(Math.Abs(titleOrigin.X - clientOrigin.X), 0, 1);
        Assert.InRange(Math.Abs(titleOrigin.Y - clientOrigin.Y), 0, 1);
        Assert.InRange(Math.Abs(contentOrigin.X - clientOrigin.X), 0, 1);
        Assert.InRange(Math.Abs(contentOrigin.Y - titleBottom.Y), 0, 1);
        Assert.InRange(Math.Abs(contentOpposite.X - (clientOrigin.X + client.Width)), 0, 1);
        Assert.InRange(Math.Abs(contentOpposite.Y - (clientOrigin.Y + client.Height)), 0, 1);

        window.Width = 1;
        window.UpdateLayout();
        var captionButtons = Assert.IsType<StackPanel>(VisualTreeHelper.GetParent(minimize));
        Assert.Equal(46 * 3, captionButtons.ActualWidth, 3);
        Assert.True(NativeWindowMethods.GetClientRect(handle, out var narrowClient));
        var narrowOrigin = new NativePoint(0, 0);
        Assert.True(NativeWindowMethods.ClientToScreen(handle, ref narrowOrigin));
        var captionRight = captionButtons.PointToScreen(new Point(captionButtons.ActualWidth, 0));
        Assert.InRange(Math.Abs(captionRight.X - (narrowOrigin.X + narrowClient.Width)), 0, 1);

        window.Width = 800;
        window.UpdateLayout();

        window.ResizeMode = ResizeMode.CanMinimize;
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.True(minimize.IsEnabled);
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        Assert.False(maximize.IsEnabled);
        Assert.Equal(0.4, maximize.Opacity, 3);
        Assert.Equal(46 * 3, captionButtons.ActualWidth, 3);
        var minimizeCenter = minimize.PointToScreen(new Point(minimize.ActualWidth / 2, minimize.ActualHeight / 2));
        var minimizeHit = NativeWindowMethods.SendMessage(
            handle,
            0x0084,
            IntPtr.Zero,
            PackScreenPoint(minimizeCenter));
        Assert.Equal(WindowFrameHitTest.MinButton, minimizeHit.ToInt32());
        _ = NativeWindowMethods.SendMessage(
            handle,
            0x00A0,
            new IntPtr(WindowFrameHitTest.MinButton),
            PackScreenPoint(minimizeCenter));
        Assert.Equal(ChromeHitTestRole.MinimizeButton, window.HoveredChromeRole);
        Assert.Same(window.CaptionButtonHoverBackground, minimize.Background);
        _ = NativeWindowMethods.SendMessage(
            handle,
            0x00A1,
            new IntPtr(WindowFrameHitTest.MinButton),
            PackScreenPoint(minimizeCenter));
        Assert.Equal(ChromeHitTestRole.MinimizeButton, window.PressedChromeRole);
        Assert.Same(window.CaptionButtonPressedBackground, minimize.Background);
        _ = NativeWindowMethods.SendMessage(
            handle,
            0x00A2,
            new IntPtr(WindowFrameHitTest.MinButton),
            PackScreenPoint(minimizeCenter));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(ChromeHitTestRole.Default, window.PressedChromeRole);
        Assert.Equal(WindowState.Minimized, window.WindowState);
        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
        _ = NativeWindowMethods.SendMessage(handle, 0x02A2, IntPtr.Zero, IntPtr.Zero);
        Assert.Equal(ChromeHitTestRole.Default, window.HoveredChromeRole);
        Assert.Equal(IntPtr.Zero, window.ResizeOverlayHandle);
        Assert.False(NativeWindowMethods.IsWindow(overlay));
        Assert.False(window.TryExecuteCaptionButton(ChromeHitTestRole.MinimizeButton, ChromeHitTestRole.CloseButton));
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.True(window.TryExecuteCaptionButton(ChromeHitTestRole.MinimizeButton, ChromeHitTestRole.MinimizeButton));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(WindowState.Minimized, window.WindowState);
        window.WindowState = WindowState.Normal;
        window.UpdateLayout();

        window.ResizeMode = ResizeMode.NoResize;
        Assert.Equal(Visibility.Collapsed, minimize.Visibility);
        Assert.Equal(Visibility.Collapsed, maximize.Visibility);
        Assert.False(window.TryExecuteCaptionButton(ChromeHitTestRole.MinimizeButton, ChromeHitTestRole.MinimizeButton));
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.ResizeMode = ResizeMode.CanResizeWithGrip;
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        window.UpdateLayout();
        window.SynchronizeResizeOverlay();
        Assert.NotEqual(IntPtr.Zero, window.ResizeOverlayHandle);
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        Assert.True(maximize.IsEnabled);
        Assert.Equal(1, maximize.Opacity, 3);
        var grip = Assert.IsType<ResizeGrip>(window.Template.FindName("PART_ResizeGrip", window));
        Assert.Equal(Visibility.Visible, grip.Visibility);

        var finalOverlay = window.ResizeOverlayHandle;
        window.Close();
        Assert.False(NativeWindowMethods.IsWindow(finalOverlay));
    });

    [Fact]
    public void NativeMinimumAndMaximumUseCurrentMonitorWorkArea() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 450,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var monitor = NativeWindowMethods.MonitorFromWindow(handle, NativeWindowMethods.MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        Assert.True(NativeWindowMethods.GetMonitorInfo(monitor, ref info));
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(default(NativeMinMaxInfo), pointer, false);
            _ = NativeWindowMethods.SendMessage(handle, 0x0024, IntPtr.Zero, pointer);
            var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(pointer);
            var expected = WindowFrameHitTest.CalculateMaximizedPlacement(info.Monitor, info.WorkArea);
            Assert.Equal(expected.Position.X, limits.MaxPosition.X);
            Assert.Equal(expected.Position.Y, limits.MaxPosition.Y);
            Assert.Equal(expected.Size.X, limits.MaxSize.X);
            Assert.Equal(expected.Size.Y, limits.MaxSize.Y);
            Assert.True(limits.MinTrackSize.X > 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            window.Close();
        }
    });

    [Fact]
    public void ChromeFrameCreatesResizeOverlayByDefault()
    {
        RunSta(() =>
        {
            var frame = new TestChromeFrame
            {
                Width = 600,
                Height = 400,
                ShowInTaskbar = false,
                ShowActivated = false,
            };

            _ = new WindowInteropHelper(frame).EnsureHandle();
            frame.Show();
            frame.UpdateLayout();
            frame.SynchronizeResizeOverlay();

            var overlay = frame.ResizeOverlayHandle;
            Assert.NotEqual(IntPtr.Zero, overlay);
            Assert.True(NativeWindowMethods.IsWindowVisible(overlay));

            Assert.True(NativeWindowMethods.GetWindowRect(overlay, out var overlayBounds));
            var topBorderPoint = PackScreenPoint(
                new Point(overlayBounds.Left + 100, overlayBounds.Top));
            Assert.Equal(
                WindowFrameHitTest.Top,
                NativeWindowMethods.SendMessage(
                    overlay, 0x0084, IntPtr.Zero, topBorderPoint).ToInt32());

            frame.Close();
            Assert.False(NativeWindowMethods.IsWindow(overlay));
        });
    }

    private sealed class TestChromeFrame : ChromeFrame { }

    private static IntPtr PackScreenPoint(Point point)
    {
        var x = unchecked((ushort)(short)Math.Round(point.X));
        var y = unchecked((ushort)(short)Math.Round(point.Y));
        return new IntPtr(unchecked((int)((uint)x | ((uint)y << 16))));
    }

    private static int HitTest(IntPtr handle, FrameworkElement element) =>
        SendHitTest(
            handle,
            element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2)));

    private static int SendHitTest(IntPtr handle, Point point) =>
        NativeWindowMethods.SendMessage(handle, 0x0084, IntPtr.Zero, PackScreenPoint(point)).ToInt32();

    private static DependencyObject? FindVisualDescendant(
        DependencyObject root,
        Func<DependencyObject, bool> predicate)
    {
        if (predicate(root)) return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var result = FindVisualDescendant(VisualTreeHelper.GetChild(root, index), predicate);
            if (result is not null) return result;
        }
        return null;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

}
