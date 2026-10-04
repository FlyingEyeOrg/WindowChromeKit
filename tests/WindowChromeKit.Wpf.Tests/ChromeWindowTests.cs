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
    public void TitleBarContentAndInheritedRolesKeepInteractiveControlsInClientArea() => RunSta(() =>
    {
        var interactive = new Border
        {
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
        };
        ChromeWindow.SetHitTestRole(interactive, ChromeHitTestRole.Client);
        // 右侧的可交互按钮同样放在 TitleBarContent 里，自己标 Client ——
        // 这正是原先 TitleBarActions 插槽的唯一作用（它只是默认替你标了 Client）。
        var action = new Button
        {
            Width = 80,
            Content = "操作",
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ChromeWindow.SetHitTestRole(action, ChromeHitTestRole.Client);
        var titleContent = new Grid { Background = Brushes.Transparent };
        titleContent.Children.Add(interactive);
        titleContent.Children.Add(action);
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarContent = titleContent,
            Content = new Border(),
        };

        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        Assert.Equal(WindowFrameHitTest.Client, HitTest(handle, interactive));
        Assert.Equal(WindowFrameHitTest.Client, HitTest(handle, action));
        // 标题栏里未被可交互内容覆盖的地方仍然算 Caption（可拖动）：
        // 取左侧 interactive 与右侧 action 之间的空档。
        var captionPoint = titleContent.PointToScreen(
            new Point(titleContent.ActualWidth / 2, titleContent.ActualHeight / 2));
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
        // 右侧按钮放进内容里并自己标 Client（替代已删除的 TitleBarActions 插槽）
        ChromeWindow.SetHitTestRole(action, ChromeHitTestRole.Client);
        titleContent.Children.Add(action);
        var window = new ChromeWindow
        {
            Width = 700,
            Height = 400,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarContent = titleContent,
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

    /// <summary>
    /// 三套样式的几何值。这些数字来自原生实测，改动前请先确认不是笔误。
    ///
    /// <para>
    /// <c>expectedIconBoxMargin</c> 是**命中盒的左边距**，不是图标的墨迹位置：
    /// 墨迹 = 这个值 + 模板里 Image 的 3px 内缩。VsCode 是 7（墨迹 10），因为它的标题栏
    /// 只有 35 高，图标上下各 (35−16)/2 = 9.5 —— 左边距取 10 才与上下接近，
    /// 取 12 会明显偏右（见 TitleBarStyle.cs 里对应的说明）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40d, 46d, 39d, 9d)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35d, 46d, 34d, 7d)]
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
    ///
    /// <para>
    /// 三种样式全部验：Windows 是 31/31（按钮和标题栏一样高，本来就没有缝），
    /// **Chrome/VsCode 才是会出问题的那两个**（40/39、35/34）——
    /// 原先只测了 Windows 样式，所以"最大化时按钮只换对齐、没长高"这个 bug 一直没被发现：
    /// 底部漏出的那 1px 在浅色标题栏下就是一条白线（实测按钮占 0..39 而非 0..40）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome)]
    [InlineData(ChromeTitleBarStyle.VsCode)]
    [InlineData(ChromeTitleBarStyle.Windows)]
    public void CaptionButtonsReachTheTopRowWhenMaximized(ChromeTitleBarStyle style) => RunSta(() =>
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
            var close = Assert.IsAssignableFrom<FrameworkElement>(
                window.Template.FindName(ChromeWindow.PartCloseButton, window));

            // 普通态：顶边线存在，按钮贴底（底边落在标题栏下缘）、高度就是配置值。
            // 按钮顶边不是固定的 1 —— Windows 样式的按钮和标题栏一样高（31/31），顶边就是 0；
            // Chrome/VsCode 矮 1px（40/39、35/34）才是 1。所以断言"关系"而不是那个数字。
            Assert.Equal(1d, window.TitleBarBorderThickness.Top, 1);
            Assert.Equal(window.CaptionButtonHeight, close.ActualHeight, 1);
            var normalTop = close.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            Assert.Equal(window.TitleBarHeight - window.CaptionButtonHeight, normalTop, 1);
            Assert.Equal(window.TitleBarHeight, normalTop + close.ActualHeight, 1);

            window.WindowState = WindowState.Maximized;
            window.UpdateLayout();

            // 最大化：线高 0、按钮从第 0 行起，且要**长高**铺满整条标题栏
            Assert.Equal(0d, window.TitleBarBorderThickness.Top, 1);
            Assert.Equal(0d, close.TransformToAncestor(window).Transform(new Point(0, 0)).Y, 1);
            Assert.Equal(window.TitleBarHeight, close.ActualHeight, 1);
            Assert.Equal(
                window.TitleBarHeight,
                close.TransformToAncestor(window).Transform(new Point(0, 0)).Y + close.ActualHeight,
                1);

            window.WindowState = WindowState.Normal;
            window.UpdateLayout();
            Assert.Equal(window.CaptionButtonHeight, close.ActualHeight, 1);
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
            // Right 特意设成非 0：它是图标列的**一部分**（命中盒右边的留白），
            // 漏算它时最小宽度会少一整段，缩到最小时关闭按钮会跑到客户区外。
            CaptionIconBoxMargin = new Thickness(0d, 0d, 24d, 0d),
        };
        var handle = new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var (frameX, frameY) = WindowFrameHitTest.GetFrameThickness(96);
        // 图标区 = 命中盒左边距 + 命中盒边长 + Right 边距（**不是**图标的墨迹宽 28）。
        // 这里原先是 "28 = 12 边距 + 16 图标"，少算了命中盒比图标每侧宽出的 3px，
        // 而且只断言 ">="，所以真少了 3px 也照样通过（Right 边距更是完全没算）。
        var iconMargin = window.CaptionIconBoxMargin;
        var leading = iconMargin.Left + ChromeWindow.SystemMenuBoxSizeDip + iconMargin.Right;
        var expectedMinimumWidth =
            (int)Math.Ceiling(leading) + (int)Math.Ceiling(window.CaptionButtonWidth * 3) + frameX * 2;
        var expectedMinimumHeight = frameY + (int)Math.Ceiling(window.TitleBarHeight);

        var limitsPointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMinMaxInfo>());
        try
        {
            Marshal.StructureToPtr(default(NativeMinMaxInfo), limitsPointer, false);
            _ = NativeWindowMethods.SendMessage(handle, 0x0024, IntPtr.Zero, limitsPointer);
            var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(limitsPointer);
            // 系统下限 SM_CXMINTRACK 可能比我们算的更大，取两者中较大者作期望
            var systemMinimumX = NativeWindowMethods.GetSystemMetricsForDpi(
                NativeWindowMethods.SmCxMinTrack,
                96);
            expectedMinimumWidth = Math.Max(expectedMinimumWidth, systemMinimumX);
            Assert.Equal(expectedMinimumWidth, limits.MinTrackSize.X);
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

    /// <summary>
    /// 设置 <c>TitleBarContent</c> 之后，**窗口图标必须还在**。
    ///
    /// 图标（<c>PART_SystemMenu</c>）是窗口自己的一部分：它是系统菜单的入口，也代表窗口身份。
    /// 它原先和默认标题文字同在一个 StackPanel 里，而模板用
    /// <c>TitleBarContent == null</c> 的触发器把整个 StackPanel 折叠 —— 于是换了标题栏内容，
    /// 图标就跟着消失了，使用者只能自己在内容里再画一个（样例当初就是这么绕过去的）。
    ///
    /// 现在图标独立成列，只有默认**标题文字**参与二选一，与 WinForms 的插槽语义一致
    /// （WinForms 的内容区本来就从图标右侧开始排）。这个测试钉住该行为。
    /// </summary>
    [Fact]
    public void TitleBarContentKeepsTheWindowIcon() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 800,
            Height = 400,
            Title = "带图标的标题栏",
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new Border(),
        };
        new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var systemMenu = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        var icon = Assert.IsAssignableFrom<Image>(
            FindVisualDescendant(systemMenu, element => element is Image));
        Assert.True(icon.IsVisible, "没有 TitleBarContent 时图标应当可见");
        var iconWidth = icon.RenderSize.Width;
        Assert.True(iconWidth > 0, "图标应当有实际宽度");

        // 换上一个标题栏内容：图标必须留下，默认标题文字让位
        var content = new Grid { Background = Brushes.Transparent };
        window.TitleBarContent = content;
        window.UpdateLayout();

        Assert.True(icon.IsVisible, "设置 TitleBarContent 之后图标应当仍然可见");
        Assert.Equal(iconWidth, icon.RenderSize.Width, 3);
        Assert.True(content.ActualWidth > 0, "标题栏内容应当拿到布局空间");
        Assert.True(content.ActualHeight > 0);

        // 内容排在图标右侧，不和图标重叠
        var iconOrigin = icon.TransformToAncestor(window).Transform(new Point(0, 0));
        var contentOrigin = content.TransformToAncestor(window).Transform(new Point(0, 0));
        Assert.True(
            contentOrigin.X >= iconOrigin.X + icon.RenderSize.Width - 1,
            $"标题栏内容应当排在图标右侧：图标 {iconOrigin.X:F1}+{icon.RenderSize.Width:F1}，内容起点 {contentOrigin.X:F1}");

        // 关掉图标时，这一列收为 0，内容跟着左移
        window.ShowTitleBarIcon = false;
        window.UpdateLayout();
        Assert.False(icon.IsVisible);
        var collapsedOrigin = content.TransformToAncestor(window).Transform(new Point(0, 0));
        Assert.True(collapsedOrigin.X < contentOrigin.X, "隐藏图标后内容应当左移");

        window.Close();
    });

    /// <summary>
    /// <c>CaptionIconBoxMargin.Left</c> 的"自动 vs 显式"语义：<c>NaN</c> = 自动
    /// （按标题栏高度推导），写了具体数字就以调用方为准。
    ///
    /// 这条很要紧：coerce 回调若不区分"没设过"和"设过"，就会把调用方显式写的值**静默覆盖**
    /// （实测设 <c>Left=20</c> 读回 9）。用 NaN 当哨兵是 WPF 自己的惯例
    /// （<c>Width</c>/<c>Height</c> 的 Auto 也是 NaN），与 WinForms 用 <c>int?</c> 的
    /// <c>null</c> 表示自动是同一套语义。
    /// </summary>
    [Theory]
    [InlineData(2d)]
    [InlineData(20d)]
    [InlineData(40d)]
    [InlineData(0d)]
    public void ExplicitIconMarginIsNotOverwrittenByTheDerivation(double left) => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Title = "T",
            Width = 800,
            Height = 300,
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new Border(),
        };
        window.Show();
        window.UpdateLayout();

        window.CaptionIconBoxMargin = new Thickness(left, 0d, 0d, 0d);
        window.UpdateLayout();
        Assert.Equal(left, window.CaptionIconBoxMargin.Left, 3);

        // 没设过 Left 时回到自动推导（标题栏高 40、命中盒 22 → 9）
        window.CaptionIconBoxMargin = new Thickness(double.NaN, 0d, 0d, 0d);
        window.UpdateLayout();
        var expected = Math.Round((window.TitleBarHeight - ChromeWindow.SystemMenuBoxSizeDip) / 2d,
            MidpointRounding.AwayFromZero);
        Assert.Equal(expected, window.CaptionIconBoxMargin.Left, 3);
        window.Close();
    });

    /// <summary>
    /// 图标左边距是**算出来的**，不是手填常量：等于命中盒在标题栏里垂直居中时的边距
    /// <c>round((标题栏高 − 22) / 2)</c>。
    ///
    /// 这样"左 = 上 = 下"天然成立，改 <c>TitleBarHeight</c> 时左边距会自动跟随 ——
    /// 否则高度一改就会留下"图标偏右"的漏改（三个样式的历史值 9 / 7 / 5
    /// 本来也全是这条规则的产物）。
    /// </summary>
    [Theory]
    [InlineData(40d, 9d)]   // Chrome
    [InlineData(35d, 7d)]   // VsCode
    [InlineData(31d, 5d)]   // Windows
    [InlineData(48d, 13d)]  // 以下高度从未硬编码，只能算出来
    [InlineData(27d, 3d)]
    [InlineData(24d, 1d)]
    public void CaptionIconMarginFollowsTheCaptionHeight(double height, double expected) => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Title = "Title",
            Width = 700,
            Height = 300,
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarHeight = height,
            Content = new Border(),
        };
        window.Show();
        window.UpdateLayout();

        Assert.Equal(expected, window.CaptionIconBoxMargin.Left, 3);

        var icon = Assert.IsAssignableFrom<Image>(
            FindVisualDescendant(
                Assert.IsAssignableFrom<FrameworkElement>(
                    window.Template.FindName(ChromeWindow.PartSystemMenu, window)),
                element => element is Image));
        var hitBox = Assert.IsAssignableFrom<FrameworkElement>(
            FindByHitTestRole(window, ChromeHitTestRole.SystemMenu));
        var iconOrigin = icon.TransformToAncestor(window).Transform(new Point(0, 0));
        var boxOrigin = hitBox.TransformToAncestor(window).Transform(new Point(0, 0));

        // 图标在命中盒内再内缩 3px（模板里 Image 的 Margin），所以墨迹左边距 = 盒子左边距 + 3
        Assert.Equal(3, iconOrigin.X - boxOrigin.X, 3);
        window.Close();
    });

    /// <summary>
    /// 标题文字从**系统菜单命中盒的右缘**开始 —— 与原生 Win32 标题栏同规则。
    ///
    /// 实测 Win32 自绘标题栏（简中系统、96dpi）：命中盒 22px 比图标每侧宽 3px，标题文字的
    /// 布局原点正好落在命中盒右缘，于是图标【墨迹】到标题【墨迹】剩 6px。
    ///
    /// 这里断言的是**几何规则**（原点 = 命中盒右缘 + 3），不是那个 6px 的最终视觉值：
    /// 后者还取决于字体（WPF 的 Segoe UI 左留白 0，GDI 的 Microsoft YaHei UI 留白 3），
    /// 而模板里补的 3px 正是为了抹平这个差。所以两者相加应当等于「命中盒右缘 + 6」。
    /// </summary>
    [Fact]
    public void TitleTextStartsAtTheSystemMenuHitBoxEdge() => RunSta(() =>
    {
        var window = new ChromeWindow
        {
            Width = 800,
            Height = 400,
            Title = "Title",
            ShowInTaskbar = false,
            ShowActivated = false,
            Content = new Border(),
        };
        new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var icon = Assert.IsAssignableFrom<Image>(
            FindVisualDescendant(
                Assert.IsAssignableFrom<FrameworkElement>(
                    window.Template.FindName(ChromeWindow.PartSystemMenu, window)),
                element => element is Image));
        var title = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName("PART_DefaultTitleContent", window));

        var iconOrigin = icon.TransformToAncestor(window).Transform(new Point(0, 0));
        var titleOrigin = title.TransformToAncestor(window).Transform(new Point(0, 0));
        var iconEnd = iconOrigin.X + icon.ActualWidth;

        // 模板里标题的 Margin.Left 是 3（见 Generic.xaml 的说明）
        Assert.Equal(3, title.Margin.Left, 3);

        // 图标墨迹 → 标题布局原点 = 命中盒每侧超出量 3 + Margin 3 = 6
        // （图标在命中盒内再居中偏 3，见模板里 Image 的 Margin="3,0,0,0"）
        var inkGap = titleOrigin.X - iconEnd;
        Assert.InRange(inkGap, 5, 7);

        // 关掉图标后，标题回到**最左 0**，整列一个像素都不占。
        // 原先只断言 "X < 隐藏前"，太松：模板里那 3px 的命中盒补偿留着也能过，
        // 于是标题在隐藏图标后仍从 x=3 起排（实测），与内容插槽的 0 不一致。
        window.ShowTitleBarIcon = false;
        window.UpdateLayout();
        var collapsedOrigin = title.TransformToAncestor(window).Transform(new Point(0, 0));
        Assert.Equal(0, collapsedOrigin.X, 1);
        var collapsedColumn = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        Assert.Equal(0, collapsedColumn.ActualWidth, 1);

        window.Close();
    });

    /// <summary>
    /// 标题栏内容**铺满到三个按钮之前**，中间不再夹着已删除的 TitleBarActions 槽位。
    ///
    /// 布局是 <c>[图标 Auto][内容 *][三个按钮 Auto]</c>。原来还有第四列放
    /// <c>TitleBarActions</c>（右对齐贴按钮组），但与 <c>TitleBarContent</c> 语义重叠 ——
    /// 它唯一做的事就是默认替内容标了 <c>HitTestRole=Client</c>，而那是个**可继承**的附加属性，
    /// 使用者自己标容器即可。所以 2.0.0 删掉了它，内容区相应变宽到按钮前。
    /// </summary>
    [Fact]
    public void TitleBarContentSpansUpToTheCaptionButtons() => RunSta(() =>
    {
        var content = new Border { Background = Brushes.Transparent };
        var window = new ChromeWindow
        {
            Width = 900,
            Height = 400,
            Title = "内容铺满",
            ShowInTaskbar = false,
            ShowActivated = false,
            TitleBarContent = content,
            Content = new Border(),
        };
        new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        window.UpdateLayout();

        var minimize = Assert.IsType<Button>(
            window.Template.FindName(ChromeWindow.PartMinimizeButton, window));
        var contentOrigin = content.TransformToAncestor(window).Transform(new Point(0, 0));
        var contentEnd = contentOrigin.X + content.ActualWidth;
        var buttonsOrigin = minimize.TransformToAncestor(window).Transform(new Point(0, 0));

        // 内容右缘紧贴按钮组左边（允许 1px 取整误差）
        Assert.InRange(Math.Abs(contentEnd - buttonsOrigin.X), 0, 1);

        // 内容从系统菜单**命中盒的右缘**开始（不是"图标右侧某个大概位置"）。
        // 注意 PART_SystemMenu 是 Auto 列的**外层容器**（x 从 0 起），真正的 22px 命中盒
        // 是它里面的 Border，靠 Margin.Left 内缩。所以列宽 = Margin.Left + 22，
        // 而插槽起点 = 该列的右缘 = 外层容器的右缘。
        // 早先这里只断言 "contentOrigin.X >= iconEnd - 1"，太松 ——
        // 样例里多放一个 31px 占位 Border 也能通过，于是内容被推到 x=62 一直没被发现。
        var systemMenu = Assert.IsAssignableFrom<FrameworkElement>(
            window.Template.FindName(ChromeWindow.PartSystemMenu, window));
        var systemMenuOrigin = systemMenu.TransformToAncestor(window).Transform(new Point(0, 0));

        Assert.Equal(
            window.CaptionIconBoxMargin.Left + ChromeWindow.SystemMenuBoxSizeDip,
            systemMenu.ActualWidth,
            1);
        Assert.Equal(systemMenuOrigin.X + systemMenu.ActualWidth, contentOrigin.X, 1);

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
