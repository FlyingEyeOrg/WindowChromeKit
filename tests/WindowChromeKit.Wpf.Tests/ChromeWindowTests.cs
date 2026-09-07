using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
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
        Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
        Assert.Throws<ArgumentException>(() => window.TitleBarHeight = 0);
        Assert.Throws<ArgumentException>(() => window.CaptionButtonWidth = double.NaN);
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

        var titleBar = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartTitleBar, window));
        var title = Assert.IsType<TextBlock>(window.Template.FindName(ChromeWindow.PartTitle, window));
        var minimize = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var maximize = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));
        var close = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartCloseButton, window));
        Assert.Equal(35, titleBar.ActualHeight, 3);
        Assert.Equal(46, close.ActualWidth, 3);
        Assert.Same(window.InactiveTitleBarForeground, title.Foreground);
        Assert.True(content.ActualWidth > 0);
        Assert.True(content.ActualHeight > 0);
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.Equal(Visibility.Visible, maximize.Visibility);

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
        Assert.Equal(IntPtr.Zero, window.ResizeOverlayHandle);
        Assert.False(NativeWindowMethods.IsWindow(overlay));
        Assert.False(window.TryExecuteMinimizeButton(WindowFrameHitTest.MinButton, WindowFrameHitTest.Close));
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.True(window.TryExecuteMinimizeButton(WindowFrameHitTest.MinButton, WindowFrameHitTest.MinButton));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(WindowState.Minimized, window.WindowState);
        window.WindowState = WindowState.Normal;
        window.UpdateLayout();

        window.ResizeMode = ResizeMode.NoResize;
        Assert.Equal(Visibility.Collapsed, minimize.Visibility);
        Assert.Equal(Visibility.Collapsed, maximize.Visibility);
        Assert.False(window.TryExecuteMinimizeButton(WindowFrameHitTest.MinButton, WindowFrameHitTest.MinButton));
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.ResizeMode = ResizeMode.CanResizeWithGrip;
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

    private static IntPtr PackScreenPoint(Point point)
    {
        var x = unchecked((ushort)(short)Math.Round(point.X));
        var y = unchecked((ushort)(short)Math.Round(point.Y));
        return new IntPtr(unchecked((int)((uint)x | ((uint)y << 16))));
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
