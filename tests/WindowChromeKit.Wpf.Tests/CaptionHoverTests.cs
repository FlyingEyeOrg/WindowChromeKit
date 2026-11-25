using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowChromeKit.Wpf;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf.Tests;

public sealed class CaptionHoverTests
{
    /// <summary>
    /// 回归：窗口状态切换（最大化/还原）后，标题栏按钮的 hover 必须被清空。
    ///
    /// <para>现象是「鼠标已经不在按钮上，按钮却仍然亮着」，要等下一次
    /// <c>WM_NCMOUSEMOVE</c> 才恢复。</para>
    ///
    /// <para>成因：hover 是按**旧几何**记录的部件值；最大化/还原会整体移动三个按钮，
    /// 旧值就指向了已经不存在的位置。而 <c>OnFrameStateChanged</c> 原本直接
    /// <c>CommitVisualState()</c>，会把这份过期 hover 画出来。</para>
    /// </summary>
    [Fact]
    public void WindowStateChangeClearsCaptionHover() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 800,
            Height = 500,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarStyle = ChromeTitleBarStyle.Chrome,
            TitleBarPalette = ChromeTitleBarPalette.ElementPlusPrimary,
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var input = GetInputController(window);
        Assert.NotNull(input);

        // 制造一个"hover 停在最大化按钮上"的状态：直接送入非客户区悬停部件。
        input!.ObserveNonClientMove(WindowFrameHitTest.MaxButton);
        Assert.Equal(ChromeHitTestRole.MaximizeButton, input.HotRole);

        // 切换窗口状态：几何变化，旧 hover 失效。
        SystemCommands.MaximizeWindow(window);
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });

        Assert.Equal(ChromeHitTestRole.Default, input.HotRole);
        Assert.Equal(ChromeHitTestRole.Default, input.PressedRole);

        // 还原同样要清。
        input.ObserveNonClientMove(WindowFrameHitTest.Close);
        Assert.Equal(ChromeHitTestRole.CloseButton, input.HotRole);

        SystemCommands.RestoreWindow(window);
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });

        Assert.Equal(ChromeHitTestRole.Default, input.HotRole);

        window.Close();
    });

    private static ChromeInputController? GetInputController(ChromeFrame window) =>
        typeof(ChromeFrame)
            .GetField("_input", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(window) as ChromeInputController;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
