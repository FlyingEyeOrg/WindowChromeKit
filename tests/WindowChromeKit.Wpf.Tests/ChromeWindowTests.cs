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
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf.Tests;

public sealed class ChromeWindowTests
{
    [Fact]
    public void DependencyPropertiesExposeDefaultsAndValidateMetrics() => RunSta(() =>
    {
        var window = new ChromeWindow();

        Assert.Equal(40, window.TitleBarHeight);
        Assert.Equal(39, window.CaptionButtonHeight);
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
    public void HitTestFollowsChromePriorityOnWindowItself() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "hit test",
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();
        Assert.True(NativeWindowMethods.GetWindowRect(handle, out var bounds));

        var titleBar = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartTitleBar, window));
        var systemMenu = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        var minimize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var maximize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));
        var close = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartCloseButton, window));

        // 1) 窗口矩形之外：Chrome 实测返回 HTNOWHERE，绝不声明别人的像素
        Assert.Equal(WindowFrameHitTest.Nowhere, SendHitTest(handle, new Point(bounds.Left - 1, bounds.Top + 100)));
        Assert.Equal(WindowFrameHitTest.Nowhere, SendHitTest(handle, new Point(bounds.Left + 100, bounds.Bottom)));
        Assert.Equal(WindowFrameHitTest.Nowhere, SendHitTest(handle, new Point(bounds.Right, bounds.Top + 100)));

        // 2) 左/右/下三边与四角：窗口矩形之内、可见窗口之外的缩放带
        Assert.Equal(WindowFrameHitTest.Left, SendHitTest(handle, new Point(bounds.Left, bounds.Top + 100)));
        Assert.Equal(WindowFrameHitTest.Right, SendHitTest(handle, new Point(bounds.Right - 1, bounds.Top + 100)));
        Assert.Equal(WindowFrameHitTest.Bottom, SendHitTest(handle, new Point(bounds.Left + 300, bounds.Bottom - 1)));
        Assert.Equal(WindowFrameHitTest.TopLeft, SendHitTest(handle, new Point(bounds.Left, bounds.Top)));
        Assert.Equal(WindowFrameHitTest.TopRight, SendHitTest(handle, new Point(bounds.Right - 1, bounds.Top)));

        // 3) 顶部带最窄（6px），其下交给标题栏
        Assert.Equal(WindowFrameHitTest.Top, SendHitTest(handle, new Point(bounds.Left + 300, bounds.Top)));
        Assert.Equal(WindowFrameHitTest.Caption, SendHitTest(handle, new Point(bounds.Left + 300, bounds.Top + 20)));
        Assert.True(titleBar.ActualHeight > 20);

        // 4) 图标区 → HTSYSMENU，三个按钮 → 各自的命中值
        Assert.Equal(WindowFrameHitTest.SystemMenu, HitTest(handle, systemMenu));
        Assert.Equal(WindowFrameHitTest.MinButton, HitTest(handle, minimize));
        Assert.Equal(WindowFrameHitTest.MaxButton, HitTest(handle, maximize));
        Assert.Equal(WindowFrameHitTest.Close, HitTest(handle, close));

        // 5) 客户区
        Assert.Equal(WindowFrameHitTest.Client, SendHitTest(handle, new Point(bounds.Left + 300, bounds.Top + 300)));
    });

    /// <summary>
    /// 标记为 Client 的标题栏自定义内容（例如标题栏里的菜单）必须占满整个标题栏高度：
    /// 它们落在顶部 6px 缩放带内时也要返回 HTCLIENT，不能被 HTTOP 切掉。
    /// </summary>
    [Fact]
    public void InteractiveTitleBarContentWinsOverResizeBands() => RunSta(() =>
    {
        var menu = new Border
        {
            Width = 240,
            Height = 40,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ChromeWindow.SetHitTestRole(menu, ChromeHitTestRole.Client);
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "title bar menu",
            TitleBarContent = menu,
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();
        var origin = menu.PointToScreen(new Point(0, 0));

        // 标题栏顶部有 1px 边框线，菜单顶边落在窗口第 1 行；
        // 第 1、4 行都在顶部 6px 缩放带之内，但菜单是可交互元素，必须返回 HTCLIENT。
        Assert.Equal(
            WindowFrameHitTest.Client,
            SendHitTest(handle, new Point(origin.X + 40, origin.Y)));
        Assert.Equal(
            WindowFrameHitTest.Client,
            SendHitTest(handle, new Point(origin.X + 40, origin.Y + 3)));

        // 同一行的标题栏空白处：这里仍然让给缩放带
        Assert.Equal(
            WindowFrameHitTest.Top,
            SendHitTest(handle, new Point(origin.X + 420, origin.Y + 3)));
        // 顶部带只有 6px，其下是标题栏
        Assert.Equal(
            WindowFrameHitTest.Caption,
            SendHitTest(handle, new Point(origin.X + 420, origin.Y + 8)));
        window.Close();
    });

    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40d, 46d, 39d, 9d)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35d, 46d, 34d, 9d)]
    [InlineData(ChromeTitleBarStyle.Windows, 32d, 44d, 32d, 0d)]
    public void TitleBarStyleAppliesDocumentedGeometry(
        ChromeTitleBarStyle style,
        double expectedHeight,
        double expectedButtonWidth,
        double expectedButtonHeight,
        double expectedIconMargin) => RunSta(() =>
    {
        var window = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.TitleBarStyle = style;
            window.UpdateLayout();

            var titleBar = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName(ChromeWindow.PartTitleBar, window));
            var close = Assert.IsType<Button>(
                window.Template.FindName(ChromeWindow.PartCloseButton, window));

            Assert.Equal(expectedHeight, window.TitleBarHeight, 3);
            Assert.Equal(expectedHeight, titleBar.ActualHeight, 3);
            Assert.Equal(expectedButtonWidth, window.CaptionButtonWidth, 3);
            Assert.Equal(expectedButtonWidth, close.ActualWidth, 3);
            Assert.Equal(expectedButtonHeight, window.CaptionButtonHeight, 3);
            Assert.Equal(expectedIconMargin, window.CaptionIconBoxMargin.Left, 3);
            Assert.True(window.ShowTitleBarIcon);

            // 套用样式之后单独改属性，以属性为准（样式不会覆盖回来）
            window.CaptionButtonWidth = 60d;
            Assert.Equal(60d, window.CaptionButtonWidth, 3);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TitleBarStyleAppliesDocumentedPalette() => RunSta(() =>
    {
        var window = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            // VS Code：固定深色
            window.TitleBarStyle = ChromeTitleBarStyle.VsCode;
            var vsCode = Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
            Assert.Equal(Color.FromRgb(0x32, 0x32, 0x33), vsCode.Color);

            // Chrome / Windows：跟随系统明暗，与 SystemTheme 的当前判定一致
            window.TitleBarStyle = ChromeTitleBarStyle.Chrome;
            var chrome = Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
            var expected = SystemTheme.IsLightMode()
                ? Color.FromRgb(0xFF, 0xFF, 0xFF)
                : Color.FromRgb(0x32, 0x32, 0x33);
            Assert.Equal(expected, chrome.Color);

            // 关闭按钮三套样式都沿用系统标准红
            var close = Assert.IsType<SolidColorBrush>(window.CloseButtonHoverBackground);
            Assert.Equal(Color.FromRgb(0xE8, 0x11, 0x23), close.Color);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 最小尺寸必须把自绘标题栏与 frame 算进去：系统默认的 SM_CYMINTRACK（39）按标准窗口
    /// 的原生标题栏算，直接沿用会把 40 高的标题栏压扁；WM_SIZING 再兜一次底。
    /// </summary>
    [Fact]
    public void MinimumSizeKeepsCaptionAndFrameUsable() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "minimum size",
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(96);
        // 图标区（28 = 12 边距 + 16 图标）+ 三个标题栏按钮 + 两侧 frame：
        // 缩到最小时图标（系统菜单入口）不能被挤没。
        var expectedMinimumWidth = 28 + (int)Math.Ceiling(window.CaptionButtonWidth * 3) + frameX * 2;
        var expectedMinimumHeight = frameY + (int)Math.Ceiling(window.TitleBarHeight);

        var limitsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(default(NativeMinMaxInfo), limitsPointer, false);
            _ = NativeWindowMethods.SendMessage(handle, 0x0024, IntPtr.Zero, limitsPointer);
            var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(limitsPointer);
            Assert.True(
                limits.MinTrackSize.X >= expectedMinimumWidth,
                $"MinTrackSize.X={limits.MinTrackSize.X} 应至少覆盖三个标题栏按钮与两侧 frame（{expectedMinimumWidth}）");
            Assert.True(
                limits.MinTrackSize.Y > expectedMinimumHeight,
                $"MinTrackSize.Y={limits.MinTrackSize.Y} 应大于标题栏与 frame 之和（{expectedMinimumHeight}）");
            Assert.True(
                limits.MinTrackSize.Y > NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyMinTrack, 96),
                "最小高度必须大于系统默认的 SM_CYMINTRACK（那是按原生标题栏算的）");
        }
        finally
        {
            Marshal.FreeHGlobal(limitsPointer);
        }

        // 拖动缩放：过小的矩形要被夹回最小尺寸，且保持被拖动的那条边
        var rectPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRectangle>());
        try
        {
            Marshal.StructureToPtr(new NativeRectangle(100, 100, 140, 118), rectPointer, false);
            _ = NativeWindowMethods.SendMessage(handle, 0x0214, new IntPtr(8), rectPointer);
            var rect = Marshal.PtrToStructure<NativeRectangle>(rectPointer);
            Assert.True(rect.Width >= expectedMinimumWidth, $"WM_SIZING 后的宽度 {rect.Width} 太小");
            Assert.True(rect.Height > expectedMinimumHeight, $"WM_SIZING 后的高度 {rect.Height} 太小");
            Assert.Equal(100, rect.Left);
            Assert.Equal(100, rect.Top);
        }
        finally
        {
            Marshal.FreeHGlobal(rectPointer);
        }
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

        Assert.Null(window.Icon);
        Assert.NotNull(window.EffectiveTitleBarIcon);
        var titleBar = Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName(ChromeWindow.PartTitleBar, window));
        var systemMenu = Assert.IsAssignableFrom<FrameworkElement>(window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        var minimize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var maximize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));
        var close = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartCloseButton, window));
        Assert.Equal(40, titleBar.ActualHeight, 3);
        Assert.Equal(46, close.ActualWidth, 3);
        Assert.Same(window.InactiveTitleBarForeground, titleBar.GetValue(TextElement.ForegroundProperty));
        Assert.True(content.ActualWidth > 0);
        Assert.True(content.ActualHeight > 0);
        Assert.Equal(Visibility.Visible, minimize.Visibility);
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        // 图标区 = 命中盒子（22，SM_CXSMSIZE）+ 左边距 9；命中盒子本身是 22×22 的正方形
        Assert.Equal(31, systemMenu.ActualWidth, 3);
        var menuBox = FindByHitTestRole(systemMenu, ChromeHitTestRole.SystemMenu);
        Assert.NotNull(menuBox);
        Assert.Equal(22, menuBox!.ActualWidth, 3);
        Assert.Equal(22, menuBox.ActualHeight, 3);
        Assert.Equal(WindowFrameHitTest.SystemMenu, HitTest(handle, menuBox));

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

        // 新模型：客户区从窗口矩形内缩出 frame（普通态顶部不内缩），不再有外置 overlay
        Assert.True(NativeWindowMethods.GetWindowRect(handle, out var bounds));
        Assert.True(NativeWindowMethods.GetClientRect(handle, out var client));
        var frameThickness = WindowFrameHitTest.GetFrameThickness(
            (uint)VisualTreeHelper.GetDpi(window).PixelsPerInchX);
        Assert.Equal(bounds.Width - frameThickness.X * 2, client.Width);
        Assert.Equal(bounds.Height - frameThickness.Y, client.Height);
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
        // 窗口尺寸在上面被改过，客户区要按当前值重新读，不能沿用 Show 之后的快照
        Assert.True(NativeWindowMethods.GetClientRect(handle, out var liveClient));
        var liveOrigin = new NativePoint(0, 0);
        Assert.True(NativeWindowMethods.ClientToScreen(handle, ref liveOrigin));
        Assert.InRange(Math.Abs(contentOpposite.X - (liveOrigin.X + liveClient.Width)), 0, 1);
        Assert.InRange(Math.Abs(contentOpposite.Y - (liveOrigin.Y + liveClient.Height)), 0, 1);

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
        Assert.Equal(Visibility.Visible, maximize.Visibility);
        Assert.True(maximize.IsEnabled);
        Assert.Equal(1, maximize.Opacity, 3);
        var grip = Assert.IsType<ResizeGrip>(window.Template.FindName("PART_ResizeGrip", window));
        Assert.Equal(Visibility.Visible, grip.Visibility);

        window.Close();
        Assert.False(NativeWindowMethods.IsWindow(handle));
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
        // 最小跟踪宽度：至少放下三个标题栏按钮 + 两侧 frame（最大化位置交给系统默认值）
        var (frameX, _) = WindowFrameHitTest.GetFrameThickness(
            (uint)VisualTreeHelper.GetDpi(window).PixelsPerInchX);
        var systemMinimum = NativeWindowMethods.GetSystemMetricsForDpi(
            NativeWindowMethods.SmCxMinTrack, 96);
        // 46*3 是三个标题栏按钮，28 是左侧图标区（12 边距 + 16 图标）
        var expectedMinimum = WindowFrameHitTest.CalculateMinimumTrackWidth(
            0, systemMinimum, frameX * 2, 46 * 3 + 28, 96);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(default(NativeMinMaxInfo), pointer, false);
            _ = NativeWindowMethods.SendMessage(handle, 0x0024, IntPtr.Zero, pointer);
            var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(pointer);
            Assert.Equal(expectedMinimum, limits.MinTrackSize.X);
            Assert.True(limits.MinTrackSize.X > 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            window.Close();
        }
    });

    [Fact]
    public void ChromeFrameInsetsClientAndProvidesResizeBands()
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

            var handle = new WindowInteropHelper(frame).EnsureHandle();
            frame.Show();
            frame.UpdateLayout();

            // 客户区 = 窗口矩形内缩 frame；顶部不内缩，因此顶部带落在客户区之内
            Assert.True(NativeWindowMethods.GetWindowRect(handle, out var bounds));
            Assert.True(NativeWindowMethods.GetClientRect(handle, out var client));
            var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(
                (uint)VisualTreeHelper.GetDpi(frame).PixelsPerInchX);
            Assert.Equal(bounds.Width - frameX * 2, client.Width);
            Assert.Equal(bounds.Height - frameY, client.Height);
            Assert.Equal(
                WindowFrameHitTest.Top,
                SendHitTest(handle, new Point(bounds.Left + 100, bounds.Top)));

            frame.Close();
            Assert.False(NativeWindowMethods.IsWindow(handle));
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

    private static FrameworkElement? FindByHitTestRole(DependencyObject root, ChromeHitTestRole role)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && ChromeWindow.GetHitTestRole(element) == role)
                return element;
            var found = FindByHitTestRole(child, role);
            if (found is not null)
                return found;
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
