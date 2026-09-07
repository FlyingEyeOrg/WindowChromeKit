using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

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
        var minimize = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var maximize = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));
        var close = Assert.IsType<Border>(window.Template.FindName(ChromeWindow.PartCloseButton, window));
        Assert.Equal(35, titleBar.ActualHeight, 3);
        Assert.Equal(46, close.ActualWidth, 3);
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

        window.ResizeMode = ResizeMode.CanMinimize;
        window.UpdateLayout();
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.Equal(Visibility.Collapsed, maximize.Visibility);
        Assert.Equal(IntPtr.Zero, window.ResizeOverlayHandle);
        Assert.False(NativeWindowMethods.IsWindow(overlay));

        window.ResizeMode = ResizeMode.NoResize;
        Assert.Equal(Visibility.Collapsed, minimize.Visibility);
        Assert.Equal(Visibility.Collapsed, maximize.Visibility);

        window.ResizeMode = ResizeMode.CanResizeWithGrip;
        window.UpdateLayout();
        window.SynchronizeResizeOverlay();
        Assert.NotEqual(IntPtr.Zero, window.ResizeOverlayHandle);
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
