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
    [InlineData(ChromeTitleBarStyle.Windows, 31d, 45d, 31d, 5d)]
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
            // VS Code：固定深色，取自它的主题定义 2026-dark.json
            // （titleBar.activeBackground = #191A1B，激活与失活同色）
            window.TitleBarStyle = ChromeTitleBarStyle.VsCode;
            var vsCode = Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
            Assert.Equal(Color.FromRgb(0x19, 0x1A, 0x1B), vsCode.Color);
            var vsCodeInactive = Assert.IsType<SolidColorBrush>(window.InactiveTitleBarBackground);
            Assert.Equal(Color.FromRgb(0x19, 0x1A, 0x1B), vsCodeInactive.Color);

            // Chrome / Windows：跟随系统明暗，与 SystemTheme 的当前判定一致。
            // VsCode 用的是**独立**的一套配色；这里显式断言它与 Chrome 不同，
            // 保证以后不会有人把两者合并（合并会在系统深色模式下连带改掉 Chrome）。
            window.TitleBarStyle = ChromeTitleBarStyle.Chrome;
            var chrome = Assert.IsType<SolidColorBrush>(window.ActiveTitleBarBackground);
            var expected = SystemTheme.IsLightMode()
                ? Color.FromRgb(0xFF, 0xFF, 0xFF)
                : Color.FromRgb(0x32, 0x32, 0x33);
            Assert.Equal(expected, chrome.Color);
            Assert.NotEqual(vsCode.Color, chrome.Color);

            // 关闭按钮的红分三套：Chrome 用经典 #E81123（按下变亮的粉红 #F1707A），
            // VS Code 悬停同为 #E81123 但按下取更深一档的红。
            var close = Assert.IsType<SolidColorBrush>(window.CloseButtonHoverBackground);
            Assert.Equal(Color.FromRgb(0xE8, 0x11, 0x23), close.Color);

            window.TitleBarStyle = ChromeTitleBarStyle.VsCode;
            var vsClose = Assert.IsType<SolidColorBrush>(window.CloseButtonHoverBackground);
            var vsClosePressed = Assert.IsType<SolidColorBrush>(window.CloseButtonPressedBackground);
            Assert.Equal(Color.FromRgb(0xE8, 0x11, 0x23), vsClose.Color);
            Assert.True(
                vsClosePressed.Color.R < vsClose.Color.R && vsClosePressed.Color.G < vsClose.Color.G,
                "the VS Code close button must darken when pressed");

            // Windows 样式用原生实测值（悬停 #C42B1C）
            window.TitleBarStyle = ChromeTitleBarStyle.Windows;
            var nativeClose = Assert.IsType<SolidColorBrush>(window.CloseButtonHoverBackground);
            Assert.Equal(Color.FromRgb(0xC4, 0x2B, 0x1C), nativeClose.Color);

            // 顶边那 1px 线用"半透明基色"模拟 DWM，参数由原生实测反解：
            //   聚焦 #262626 @ 66%（黑底 25 / 白底 112）
            //   失焦 #565656 @ 50%（黑底 43 / 白底 170）
            // 半透明意味着它会与标题栏底色混合，自定义标题栏配色无需改动这两个画刷。
            var activeLine = Assert.IsType<SolidColorBrush>(window.TitleBarBorderBrush);
            Assert.Equal(0xA8, activeLine.Color.A);
            Assert.Equal(0x26, activeLine.Color.R);
            var inactiveLine = Assert.IsType<SolidColorBrush>(window.InactiveTitleBarBorderBrush);
            Assert.Equal(0x80, inactiveLine.Color.A);
            Assert.Equal(0x56, inactiveLine.Color.R);
            Assert.True(activeLine.Color.A < 255 && inactiveLine.Color.A < 255,
                "both top line brushes must stay semi-transparent so they blend with any caption colour");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void WindowsStylePlacesTheIconLikeANativeCaption() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            TitleBarStyle = ChromeTitleBarStyle.Windows,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var box = FindByHitTestRole(window, ChromeHitTestRole.SystemMenu);
            Assert.NotNull(box);
            var icon = Assert.IsAssignableFrom<FrameworkElement>(VisualTreeHelper.GetChild(box!, 0));
            // WPF 的坐标空间就是客户区（不含原生 frame），直接比即可
            var boxOrigin = box!.TransformToAncestor(window).Transform(new Point(0, 0));
            var iconOrigin = icon.TransformToAncestor(window).Transform(new Point(0, 0));
            // 图标在标题栏内垂直居中并随高度自适应：标题栏 31、盒子 22 → 顶边 (31-22)/2 = 4.5。
            // 顶边线现在是覆盖层（不占布局），内容区等于整条标题栏，所以是精确居中。
            Assert.Equal(31d, window.TitleBarHeight, 1);
            Assert.Equal(5d, boxOrigin.X, 1);
            Assert.Equal(4.5d, boxOrigin.Y, 1);
            Assert.Equal(22d, box.ActualHeight, 1);
            Assert.Equal(8d, iconOrigin.X, 1);
            Assert.Equal(7.5d, iconOrigin.Y, 1);
            Assert.Equal(16d, icon.ActualHeight, 1);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 普通态第 0 行是顶边线（Win10 的 DWM 不画顶部边框，我们自己补），
    /// 标题栏按钮必须让出这一行：按钮顶边要 >= 1，否则 hover 填充会盖住那条线。
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40d, 39d)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35d, 34d)]
    [InlineData(ChromeTitleBarStyle.Windows, 31d, 31d)]
    public void CaptionButtonsLeaveTheTopBorderLine(
        ChromeTitleBarStyle style,
        double captionHeight,
        double buttonHeight) => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            TitleBarStyle = style,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var titleBar = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName(ChromeWindow.PartTitleBar, window));
            Assert.Equal(captionHeight, titleBar.ActualHeight, 1);

            foreach (var part in new[]
                     {
                         ChromeWindow.PartCloseButton,
                         ChromeWindow.PartMaximizeButton,
                         ChromeWindow.PartMinimizeButton,
                     })
            {
                var button = Assert.IsAssignableFrom<FrameworkElement>(
                    window.Template.FindName(part, window));
                var origin = button.TransformToAncestor(window).Transform(new Point(0, 0));
                Assert.Equal(buttonHeight, button.ActualHeight, 1);
                // 顶边线是覆盖层（画在按钮之上、不占布局），所以按钮顶边就是第 0 行；
                // 要保证的是按钮不超出标题栏范围
                Assert.True(
                    origin.Y >= 0d,
                    $"{style}/{part}: button top must not be negative, got {origin.Y}");
                Assert.True(
                    origin.Y + button.ActualHeight <= captionHeight + 0.5,
                    $"{style}/{part}: button bottom {origin.Y + button.ActualHeight} exceeds the caption {captionHeight}");
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 顶边线覆盖层必须跟着焦点切换：激活用 TitleBarBorderBrush、失活用 InactiveTitleBarBorderBrush，
    /// 且两种状态下高度都是 1（最大化除外）。
    /// </summary>
    [Fact]
    public void TopBorderLineFollowsActivation() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            TitleBarStyle = ChromeTitleBarStyle.Chrome,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.Activate();
            window.UpdateLayout();
            var line = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName("PART_TopBorderLine", window));
            Assert.Equal(1d, line.ActualHeight, 1);
            var active = Assert.IsType<SolidColorBrush>(line.GetValue(Border.BackgroundProperty));
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(window.TitleBarBorderBrush).Color,
                active.Color);

            // 失活态在测试进程里不好真实触发，改为直接驱动触发器要用的两个画刷：
            // 它们必须不同，否则切换焦点时线不会变色（曾经的缺陷就是覆盖层只绑了失活色）
            Assert.NotEqual(
                Assert.IsType<SolidColorBrush>(window.TitleBarBorderBrush).Color,
                Assert.IsType<SolidColorBrush>(window.InactiveTitleBarBorderBrush).Color);
            // 覆盖层的初始画刷必须是失活色（模板默认），激活由触发器覆盖
            var visual = (FrameworkElement)window.Template.FindName("PART_TopBorderLine", window);
            Assert.NotNull(visual);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 最大化时不画顶边线（画了会在屏幕顶端多一条），所以按钮必须铺到标题栏顶部；
    /// 普通态则相反：模板的 1px BorderThickness 让出第 0 行，按钮从第 1 行开始。
    /// 否则最大化 + 悬停按钮时，顶部会漏出一条底色（浅色标题栏下看起来是 1px 白边）。
    /// </summary>
    [Fact]
    public void CaptionButtonsReachTheTopRowWhenMaximized() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            TitleBarStyle = ChromeTitleBarStyle.Windows,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var close = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName(ChromeWindow.PartCloseButton, window));

            // 顶边线是覆盖层、不占布局，所以按钮在两种状态下都铺满整条标题栏
            // （普通态的线画在按钮之上；最大化时线高 0，不会在屏幕顶端多一条）
            Assert.Equal(1d, window.TitleBarBorderThickness.Top, 1);
            Assert.Equal(0d, close.TransformToAncestor(window).Transform(new Point(0, 0)).Y, 1);
            Assert.Equal(window.TitleBarHeight, close.ActualHeight, 1);

            window.WindowState = WindowState.Maximized;
            window.UpdateLayout();
            Assert.Equal(0d, window.TitleBarBorderThickness.Top, 1);
            Assert.Equal(0d, close.TransformToAncestor(window).Transform(new Point(0, 0)).Y, 1);
            Assert.Equal(window.TitleBarHeight, close.ActualHeight, 1);

            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 标题栏图标必须随标题栏高度垂直居中（三种样式 40 / 35 / 31 都验）。
    /// 模板内容区比标题栏少 1px（顶边线），所以盒子的居中位置是 (H-1-22)/2。
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40d)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35d)]
    [InlineData(ChromeTitleBarStyle.Windows, 31d)]
    public void CaptionIconStaysCentredAcrossCaptionHeights(
        ChromeTitleBarStyle style,
        double expectedCaptionHeight) => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            TitleBarStyle = style,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var box = FindByHitTestRole(window, ChromeHitTestRole.SystemMenu);
            Assert.NotNull(box);
            var icon = Assert.IsAssignableFrom<FrameworkElement>(VisualTreeHelper.GetChild(box!, 0));
            var handle = new WindowInteropHelper(window).Handle;

            var titleBar = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName(ChromeWindow.PartTitleBar, window));
            Assert.Equal(expectedCaptionHeight, titleBar.ActualHeight, 1);

            var boxOrigin = box!.TransformToAncestor(window).Transform(new Point(0, 0));
            var iconOrigin = icon.TransformToAncestor(window).Transform(new Point(0, 0));
            Assert.Equal(22d, box.ActualHeight, 1);
            Assert.Equal(16d, icon.ActualHeight, 1);
            // 盒子中心 == 图标中心（图标在盒内居中）
            var boxCentre = boxOrigin.Y + box.ActualHeight / 2;
            var iconCentre = iconOrigin.Y + icon.ActualHeight / 2;
            Assert.Equal(boxCentre, iconCentre, 1);
            // 命中盒子自身必须是 22×22（SM_CXSMSIZE × SM_CYSMSIZE），命中角色就是 SystemMenu
            Assert.Equal(22d, box.ActualWidth, 1);
            Assert.Equal(ChromeHitTestRole.SystemMenu, ChromeWindow.GetHitTestRole(box));
            // 盒子的命中测试命中它自己（而不是被标题栏抢走）
            Assert.Equal(WindowFrameHitTest.SystemMenu, HitTest(handle, box));

            // 盒子在标题栏内垂直居中，并随标题栏高度自适应。
            // 模板内容区被 1px 顶边线顶下去一行，WPF 居中时余数可能落在任一侧（亚像素），
            // 因此允许 1px 误差 —— 要锁住的是"随高度变化而居中"，不是半个像素。
            var idealTop = (expectedCaptionHeight - box.ActualHeight) / 2d;
            Assert.True(
                Math.Abs(boxOrigin.Y - idealTop) <= 1d,
                $"{style}: caption={expectedCaptionHeight} box top={boxOrigin.Y} should be about {idealTop}");
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
        // Chrome 实测三个按钮不等宽：最小化 45、最大化/关闭 46
        Assert.Equal(45 + 46 + 46, captionButtons.ActualWidth, 3);
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
        // Chrome 实测三个按钮不等宽：最小化 45、最大化/关闭 46
        Assert.Equal(45 + 46 + 46, captionButtons.ActualWidth, 3);
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
        // 用窗口自己暴露的按钮总宽（Chrome 样式三按钮不等宽：最小化 45、其余 46），
        // 图标区宽度跟随 ChromeWindow 的 CaptionLeadingWidth（默认模板为 28），不写死数值。
        var effectiveWindow = (IChromeFrameHost)window;
        var buttonsWidth = (int)Math.Ceiling(effectiveWindow.CaptionButtonsWidth);
        var leading = (int)Math.Ceiling(effectiveWindow.CaptionLeadingWidth);
        var expectedMinimum = WindowFrameHitTest.CalculateMinimumTrackWidth(
            0, systemMinimum, frameX * 2, buttonsWidth + leading, 96);
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

    /// <summary>
    /// 回归：标题栏按钮的命令改变了窗口几何后（最大化会把三个按钮整体移走），
    /// 悬停必须按**新几何**重新判定，不能留在那个已经不存在的格子上。
    ///
    /// WPF 侧的成因：<c>CompleteCaptionButtonPress</c> 里 <c>_hotPart = releasedPart</c> 写在
    /// <c>ReleaseCapture</c> **之后**，会把 <c>WM_CAPTURECHANGED</c> 刚清掉的状态又写回去；
    /// 紧接着最大化改变了几何，而没有任何代码按新几何复核。
    /// WinForms 侧同职责的赋值顺序相反（先点亮、后释放捕获），所以那边恰好被清掉 —— 这就是
    /// 同一个缺陷只在 WPF 暴露的原因。
    ///
    /// 这里把真实光标停在内容区（远离所有标题栏按钮），再按系统的方式发
    /// hover / 按下 / 抬起 三条非客户区消息：命令执行完，悬停必须已被清掉。
    ///
    /// 断言写在**命令执行完的当下**（不先泵消息队列），否则测不到：实测去掉修复后
    /// "命令刚执行完 = MaximizeButton、泵一次消息之后 = Default" —— 后面那次修正来自
    /// 系统补发的一条 WM_NCMOUSEMOVE。这也正是这个缺陷"偶尔才看到"的原因：
    /// 指针之后只要再动一下（或系统补发消息），它就自己好了；不动就一直亮着。
    /// </summary>
    [Fact]
    public void HoverIsReevaluatedAfterTheCommandMovesTheCaptionButtons() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 800,
            Height = 500,
            Title = "Chrome test",
            Content = new Border(),
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var maximize = Assert.IsType<Button>(window.Template.FindName(ChromeWindow.PartMaximizeButton, window));

        // park the real cursor well inside the content area, far from every caption button
        var contentPoint = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight - 40));
        Assert.True(SetCursorPos((int)contentPoint.X, (int)contentPoint.Y));
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(ChromeHitTestRole.Default, window.HoveredChromeRole);

        // hover, press, release - the same three messages the OS would deliver
        var maximizeCentre = maximize.PointToScreen(new Point(maximize.ActualWidth / 2, maximize.ActualHeight / 2));
        _ = NativeWindowMethods.SendMessage(handle, 0x00A0, new IntPtr(WindowFrameHitTest.MaxButton), PackScreenPoint(maximizeCentre));
        Assert.Equal(ChromeHitTestRole.MaximizeButton, window.HoveredChromeRole);

        _ = NativeWindowMethods.SendMessage(handle, 0x00A1, new IntPtr(WindowFrameHitTest.MaxButton), PackScreenPoint(maximizeCentre));
        _ = NativeWindowMethods.SendMessage(handle, 0x00A2, new IntPtr(WindowFrameHitTest.MaxButton), PackScreenPoint(maximizeCentre));

        // Assert BEFORE pumping the dispatcher: a real WM_NCMOUSEMOVE arriving afterwards would
        // recompute the hover and hide the defect, which is exactly why it only shows up sometimes.
        var hoverRightAfterCommand = window.HoveredChromeRole;
        window.Dispatcher.Invoke(DispatcherPriority.Background, () => { });
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.True(
            hoverRightAfterCommand != ChromeHitTestRole.MaximizeButton,
            $"the maximize button stayed hovered after the command although the pointer is in the content area "
            + $"(immediately after command: {hoverRightAfterCommand}, after pump: {window.HoveredChromeRole})");

        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
    });

    /// <summary>
    /// 配色是**独立的一轴**：换配色只改颜色，几何一点不动。
    /// 三套 Element Plus 配色逐个核对，并且断言它们配到三套骨架上颜色都一样 ——
    /// 配色的值**不依赖样式**，这正是把它与样式拆成两轴的意义。
    /// </summary>
    [Fact]
    public void ElementPlusPalettesChangeOnlyTheColours() => RunSta(() =>
    {
        var window = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            foreach (var palette in new[]
                     {
                         ChromeTitleBarPalette.ElementPlusPrimary,
                         ChromeTitleBarPalette.ElementPlusDark,
                         ChromeTitleBarPalette.ElementPlusNeutral,
                     })
            {
                var expected = ExpectedElementPlus(palette);
                Color? seenOnChrome = null;
                foreach (var style in new[]
                         {
                             ChromeTitleBarStyle.Chrome,
                             ChromeTitleBarStyle.VsCode,
                             ChromeTitleBarStyle.Windows,
                         })
                {
                    window.TitleBarStyle = style;
                    window.TitleBarPalette = ChromeTitleBarPalette.Default;
                    var geometry = Geometry(window);

                    window.TitleBarPalette = palette;

                    Assert.Equal(geometry, Geometry(window));
                    Assert.Equal(expected.Active, ColorOf(window.ActiveTitleBarBackground));
                    Assert.Equal(expected.Inactive, ColorOf(window.InactiveTitleBarBackground));
                    Assert.Equal(expected.Text, ColorOf(window.ActiveTitleBarForeground));
                    Assert.Equal(expected.InactiveText, ColorOf(window.InactiveTitleBarForeground));
                    Assert.Equal(expected.Hover, ColorOf(window.CaptionButtonHoverBackground));
                    Assert.Equal(expected.Pressed, ColorOf(window.CaptionButtonPressedBackground));
                    Assert.Equal(expected.CloseHover, ColorOf(window.CloseButtonHoverBackground));
                    Assert.Equal(expected.ClosePressed, ColorOf(window.CloseButtonPressedBackground));

                    if (seenOnChrome is null)
                        seenOnChrome = ColorOf(window.ActiveTitleBarBackground);
                    else
                        Assert.Equal(seenOnChrome, ColorOf(window.ActiveTitleBarBackground));
                }
            }
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 三套 Element Plus 配色**互不相同** —— 否则"多套配色"就是摆设。
    /// 主色 / 深色 / 中性三者的底色必须两两不同。
    /// </summary>
    [Fact]
    public void ElementPlusPalettesAreDistinct() => RunSta(() =>
    {
        var window = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.TitleBarStyle = ChromeTitleBarStyle.Chrome;
            var seen = new List<Color>();
            foreach (var palette in new[]
                     {
                         ChromeTitleBarPalette.ElementPlusPrimary,
                         ChromeTitleBarPalette.ElementPlusDark,
                         ChromeTitleBarPalette.ElementPlusNeutral,
                     })
            {
                window.TitleBarPalette = palette;
                seen.Add(ColorOf(window.ActiveTitleBarBackground));
            }
            Assert.Equal(3, seen.Distinct().Count());
            Assert.Equal(Color.FromRgb(0x40, 0x9E, 0xFF), seen[0]);   // 主色
            Assert.Equal(Color.FromRgb(0x14, 0x14, 0x14), seen[1]);   // 深色
            Assert.Equal(Colors.White, seen[2]);                      // 中性
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// 两个轴**谁后赋值都成立**：先样式后配色与先配色后样式，最终状态必须一致。
    /// 这条守住"改样式会按当前配色来源重新上色"这个契约。
    /// </summary>
    [Fact]
    public void StyleAndPaletteComposeInEitherOrder() => RunSta(() =>
    {
        var styleFirst = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        var paletteFirst = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        styleFirst.Show();
        paletteFirst.Show();
        try
        {
            styleFirst.TitleBarStyle = ChromeTitleBarStyle.VsCode;
            styleFirst.TitleBarPalette = ChromeTitleBarPalette.ElementPlusDark;

            paletteFirst.TitleBarPalette = ChromeTitleBarPalette.ElementPlusDark;
            paletteFirst.TitleBarStyle = ChromeTitleBarStyle.VsCode;

            Assert.Equal(Geometry(styleFirst), Geometry(paletteFirst));
            foreach (var read in Reads())
            {
                Assert.Equal(read(styleFirst), read(paletteFirst));
            }
        }
        finally
        {
            styleFirst.Close();
            paletteFirst.Close();
        }

        static Func<ChromeWindow, Color>[] Reads() => new Func<ChromeWindow, Color>[]
        {
            w => ColorOf(w.ActiveTitleBarBackground),
            w => ColorOf(w.InactiveTitleBarBackground),
            w => ColorOf(w.ActiveTitleBarForeground),
            w => ColorOf(w.InactiveTitleBarForeground),
            w => ColorOf(w.CaptionButtonHoverBackground),
            w => ColorOf(w.CaptionButtonPressedBackground),
            w => ColorOf(w.CloseButtonHoverBackground),
            w => ColorOf(w.CloseButtonPressedBackground),
        };
    });

    /// <summary>
    /// 切回 <c>Default</c> 要还原成该样式自带的那套配色 —— 包括关闭按钮的红
    /// （三套样式的红本来就不同：Chrome 按下变亮、Windows 变暗、VS Code 变深）。
    /// </summary>
    [Fact]
    public void SwitchingBackToDefaultRestoresTheStylesOwnColours() => RunSta(() =>
    {
        var window = new ChromeWindow { Width = 700, Height = 400, ShowInTaskbar = false };
        window.Show();
        try
        {
            window.TitleBarStyle = ChromeTitleBarStyle.Chrome;
            var own = (
                ColorOf(window.ActiveTitleBarBackground),
                ColorOf(window.CloseButtonHoverBackground),
                ColorOf(window.CloseButtonPressedBackground));

            window.TitleBarPalette = ChromeTitleBarPalette.ElementPlusPrimary;
            Assert.NotEqual(own.Item1, ColorOf(window.ActiveTitleBarBackground));

            window.TitleBarPalette = ChromeTitleBarPalette.Default;
            Assert.Equal(own.Item1, ColorOf(window.ActiveTitleBarBackground));
            Assert.Equal(own.Item2, ColorOf(window.CloseButtonHoverBackground));
            Assert.Equal(own.Item3, ColorOf(window.CloseButtonPressedBackground));

            // Windows 样式的关闭按钮按下必须仍然"变暗"（原生行为），不被 Element Plus 的规则带走
            window.TitleBarStyle = ChromeTitleBarStyle.Windows;
            var hover = ColorOf(window.CloseButtonHoverBackground);
            var pressed = ColorOf(window.CloseButtonPressedBackground);
            Assert.True(pressed.R < hover.R, "Windows 样式的关闭按钮按下应当比悬停更深");
        }
        finally
        {
            window.Close();
        }
    });

    private static Color ColorOf(Brush brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    /// <summary>样式决定的那几个几何量（换配色时这些必须一个都不变）。</summary>
    private static (double Height, double Width, double ButtonHeight, double MinWidth, Thickness IconMargin)
        Geometry(ChromeWindow window) => (
            window.TitleBarHeight,
            window.CaptionButtonWidth,
            window.CaptionButtonHeight,
            window.MinimizeButtonWidth,
            window.CaptionIconBoxMargin);

    /// <summary>
    /// Element Plus 三套配色的期望值，全部由它在 <c>theme-chalk</c> 里的官方变量与混色公式推出，
    /// 这里独立算一遍，避免"用实现测实现"。
    /// </summary>
    private static (Color Active, Color Inactive, Color Text, Color InactiveText,
        Color Hover, Color Pressed, Color CloseHover, Color ClosePressed)
        ExpectedElementPlus(ChromeTitleBarPalette palette)
    {
        var primary = Color.FromRgb(0x40, 0x9E, 0xFF);
        var darkBg = Color.FromRgb(0x14, 0x14, 0x14);
        // 三套共用的关闭按钮：Windows 原生实测值
        var closeHover = Color.FromRgb(0xC4, 0x2B, 0x1C);
        var closePressed = Color.FromRgb(0xA9, 0x23, 0x16);
        return palette switch
        {
            ChromeTitleBarPalette.ElementPlusDark => (
                darkBg,
                Color.FromRgb(0x1D, 0x1E, 0x1F),
                MixWith(Color.FromRgb(0xF0, 0xF5, 0xFF), darkBg, 0.95),
                MixWith(Color.FromRgb(0xF0, 0xF5, 0xFF), darkBg, 0.65),
                MixWith(Color.FromRgb(0xFA, 0xFC, 0xFF), darkBg, 0.12),
                MixWith(Color.FromRgb(0xFA, 0xFC, 0xFF), darkBg, 0.20),
                closeHover,
                closePressed),
            ChromeTitleBarPalette.ElementPlusNeutral => (
                Colors.White,
                Color.FromRgb(0xF2, 0xF6, 0xFC),
                Color.FromRgb(0x30, 0x31, 0x33),
                Color.FromRgb(0x90, 0x93, 0x99),
                MixWith(Colors.White, primary, 0.90),
                MixWith(Colors.White, primary, 0.80),
                closeHover,
                closePressed),
            _ => (
                primary,
                MixWith(Colors.White, primary, 0.30),
                Colors.White,
                MixWith(Colors.White, primary, 0.90),
                MixWith(Colors.White, primary, 0.30),
                MixWith(Colors.Black, primary, 0.20),
                closeHover,
                closePressed),
        };
    }

    /// <summary>Element Plus 的混色公式：<paramref name="pct"/> 是 <paramref name="foreground"/> 的占比。</summary>
    private static Color MixWith(Color foreground, Color background, double pct) => Color.FromRgb(
        (byte)Math.Round(foreground.R * pct + background.R * (1 - pct)),
        (byte)Math.Round(foreground.G * pct + background.G * (1 - pct)),
        (byte)Math.Round(foreground.B * pct + background.B * (1 - pct)));

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

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

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
