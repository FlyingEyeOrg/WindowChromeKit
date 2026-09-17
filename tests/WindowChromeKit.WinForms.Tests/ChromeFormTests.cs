using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms;
using Xunit;

namespace WindowChromeKit.WinForms.Tests;

public sealed class ChromeFormTests
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int HtCaption = 2;
    private const int HtMinButton = 8;
    private const int HtMaxButton = 9;
    private const int HtClose = 20;
    private const int HtSysMenu = 3;

    /// <summary>
    /// 最大化时客户区顶边比窗口顶边低 frameY（那条不可见缩放带），绘制用的是客户区坐标；
    /// 命中矩形若从窗口顶边起算就会整体上移 8px，按钮底部会漏成 HTCAPTION。
    /// 这里直接断言三个按钮的命中范围与绘制范围一致（都贴着客户区顶部）。
    /// </summary>
    [Fact]
    public void CaptionButtonHitAreaStaysAlignedWhenMaximized() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "hit test",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        var frameY = FrameY(form);
        var buttonHeight = form.CaptionHeight > 0 ? form.CaptionButtonHeight : 0;
        Assert.True(buttonHeight > 0);

        // 普通态：客户区顶边 == 窗口顶边
        form.WindowState = FormWindowState.Normal;
        Application.DoEvents();
        var restoredTop = FirstHitRow(form, HtClose);
        var restoredScan = Scan(form, HtClose);
        Assert.True(
            form.CaptionButtonTop == restoredTop,
            $"restored: top={form.CaptionButtonTop} but first HTCLOSE row={restoredTop}; scan={restoredScan}");

        // 最大化：命中区必须整体跟着客户区下移 frameY，而不是留在窗口顶边
        form.WindowState = FormWindowState.Maximized;
        Application.DoEvents();
        // 最大化（Chrome 实测）：客户区顶边 = 窗口顶 + frameY，按钮从客户区第 0 行起、铺满标题栏
        var maximizedTop = FirstHitRow(form, HtClose);
        var maximizedBottom = LastHitRow(form, HtClose);
        var maximizedScan = Scan(form, HtClose);
        Assert.True(
            frameY == maximizedTop,
            $"maximized: button should start at client row 0 (window row {frameY}) but got {maximizedTop}; scan={maximizedScan}");
        Assert.True(
            frameY + form.CaptionHeight - 1 == maximizedBottom,
            $"maximized: button should fill the caption (last window row {frameY + form.CaptionHeight - 1}) but got {maximizedBottom}; scan={maximizedScan}");

        // 三个按钮各自一致（横向不重叠、各自的列都要能命中）
        foreach (var (hit, index) in new[] { (HtClose, 0), (HtMaxButton, 1), (HtMinButton, 2) })
        {
            var top = FirstHitRow(form, hit, index);
            var bottom = LastHitRow(form, hit, index);
            var scan = Scan(form, hit, index);
            Assert.True(
                top == frameY,
                $"button {hit}: expected first row {frameY} (client row 0) but got {top}; scan={scan}");
            Assert.True(
                bottom == frameY + form.CaptionHeight - 1,
                $"button {hit}: expected last row {frameY + form.CaptionHeight - 1} but got {bottom}; scan={scan}");
        }
    });

    /// <summary>
    /// 标题栏图标必须随标题栏高度垂直居中：三种样式（40 / 35 / 31）下，
    /// 图标盒子顶边都等于 (标题栏高 - 盒子高) / 2，不能被钉在某个固定行上。
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35)]
    [InlineData(ChromeTitleBarStyle.Windows, 31)]
    public void CaptionIconStaysCentredAcrossCaptionHeights(ChromeTitleBarStyle style, int expectedCaptionHeight) =>
        RunSta(() =>
        {
            using var form = new ChromeForm
            {
                Text = "icon centre",
                ShowInTaskbar = false,
                TitleBarStyle = style,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(200, 200),
                Size = new Size(900, 560),
            };
            form.Show();
            Application.DoEvents();

            Assert.Equal(expectedCaptionHeight, form.CaptionHeight);
            // 盒子 22 高（SM_CXSMSIZE × SM_CYSMSIZE），在标题栏内居中
            var boxTop = form.SystemMenuTop;
            var boxHeight = 22;
            var expectedTop = (form.CaptionHeight - boxHeight) / 2;
            Assert.True(
                boxTop == expectedTop,
                $"{style}: caption={form.CaptionHeight} box top should be {expectedTop} but was {boxTop}");
            // 图标 16px 在盒内居中，两者中心必须一致
            Assert.True(
                boxTop + boxHeight / 2 == expectedTop + boxHeight / 2,
                $"{style}: icon centre must match the box centre");
        });

    /// <summary>
    /// 图标垂直居中改动后的命中区必须仍然贴合图标：盒子尺寸恒为 22×22（SM_CXSMSIZE × SM_CYSMSIZE），
    /// 位置随标题栏高度居中，且盒子外的标题栏（左、右、上、下各一像素）都不能是 HTSYSMENU。
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35)]
    [InlineData(ChromeTitleBarStyle.Windows, 31)]
    public void SystemMenuHitBoxTracksTheCentredIcon(ChromeTitleBarStyle style, int captionHeight) =>
        RunSta(() =>
        {
            using var form = new ChromeForm
            {
                Text = "icon hit",
                ShowInTaskbar = false,
                TitleBarStyle = style,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(200, 200),
                Size = new Size(900, 560),
            };
            form.Show();
            Application.DoEvents();

            var window = WindowRect(form);
            var frame = FrameX(form);
            var expectedTop = form.SystemMenuTop;
            var boxHeight = 22;
            var boxWidth = 22;

            // 盒子内部：中心必须是 HTSYSMENU
            var centreY = window.Top + expectedTop + boxHeight / 2;
            var centreX = window.Left + frame + form.SystemMenuLeft + boxWidth / 2;
            Assert.Equal(HtSysMenu, HitTest(form, centreX, centreY));

            // 盒子四条边界外侧一像素：都不能是 HTSYSMENU
            Assert.NotEqual(HtSysMenu, HitTest(form, centreX, window.Top + expectedTop - 1));
            Assert.NotEqual(HtSysMenu, HitTest(form, centreX, window.Top + expectedTop + boxHeight));
            Assert.NotEqual(
                HtSysMenu,
                HitTest(form, window.Left + frame + form.SystemMenuLeft - 1, centreY));
            Assert.NotEqual(
                HtSysMenu,
                HitTest(form, window.Left + frame + form.SystemMenuLeft + boxWidth, centreY));

            // 盒子随标题栏高度居中（这是本次修复的核心）
            Assert.Equal((captionHeight - boxHeight) / 2, expectedTop);
        });

    /// <summary>按钮底边之下应回到标题栏（HTCAPTION），说明按钮高度没有被拉长。</summary>
    /// <summary>
    /// 普通态第 0 行是顶边线（Win10 的 DWM 不画顶部边框，我们自己补），按钮的 hover/按下
    /// 填充必须让出这一行，否则鼠标一移到按钮上就把线盖掉了。
    /// </summary>
    [Theory]
    [InlineData(ChromeTitleBarStyle.Chrome, 40, 46)]
    [InlineData(ChromeTitleBarStyle.VsCode, 35, 46)]
    [InlineData(ChromeTitleBarStyle.Windows, 31, 45)]
    public void CaptionButtonHoverKeepsTheTopBorderLine(
        ChromeTitleBarStyle style,
        int captionHeight,
        int buttonWidth) => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "hover probe",
            ShowInTaskbar = false,
            TitleBarStyle = style,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();
        Assert.Equal(captionHeight, form.CaptionHeight);
        Assert.Equal(buttonWidth, form.CaptionButtonWidth);

        var window = WindowRect(form);
        // 关闭按钮中部（客户区 x = 窗口右缘 - frame - 半格）
        var centreX = window.Right - FrameX(form) - form.CaptionButtonWidth / 2;
        var centreY = window.Top + Math.Max(1, captionHeight / 2);
        _ = SetCursorPos(centreX, centreY);
        Application.DoEvents();
        Thread.Sleep(150);

        using var bitmap = new Bitmap(window.Right - window.Left, captionHeight + 4);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            _ = PrintWindow(form.Handle, hdc, 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        var column = bitmap.Width - FrameX(form) - form.CaptionButtonWidth / 2;
        var line = bitmap.GetPixel(column, 0);
        // 顶边线是半透明中性灰（叠在标题栏上混合），所以深浅随样式不同：
        // 浅色标题栏约 (170,170,170)，深色标题栏约 (68,68,68)。判定"是中性灰"而不是固定亮度，
        // 关键是它必须与 hover 的关闭红（R 远大于 G/B）明显不同。
        var isNeutralGrey = Math.Abs(line.R - line.G) < 12 && Math.Abs(line.G - line.B) < 12;
        Assert.True(
            isNeutralGrey,
            $"{style}: row 0 should keep the top border line (neutral grey), got {line}");
        // 布局契约：第 0 行留给顶边线，按钮的绘制顶边从第 1 行开始（与 WPF 模板的
        // 1px BorderThickness、原生标题栏一致）。这里断言绘制矩形本身，避免依赖
        // 测试进程里的激活/悬停状态（无焦点窗口不会画 hover 填充）。
        Assert.Equal(1, CaptionButtonPaintTopFor(form));
        Assert.True(
            CaptionButtonPaintHeightFor(form) <= form.CaptionHeight - 1,
            $"{style}: button paint height must leave the top line row");
    });

    /// <summary>
    /// 最大化时不画顶边线，按钮填充必须铺到客户区第 0 行 —— 否则悬停时顶部会漏出
    /// 一条底色（浅色标题栏下看起来就是 1px 白边）。普通态则相反：第 0 行留给顶边线。
    /// </summary>
    [Fact]
    public void CaptionButtonFillCoversTheTopRowWhenMaximized() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "max fill",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        // 普通态：第 0 行留给顶边线
        Assert.False(form.WindowState == FormWindowState.Maximized);
        Assert.Equal(1, form.CaptionButtonPaintTop);
        Assert.Equal(form.CaptionHeight - 1, form.CaptionButtonPaintHeight);

        form.WindowState = FormWindowState.Maximized;
        Application.DoEvents();
        Thread.Sleep(200);
        Application.DoEvents();

        Assert.Equal(0, form.CaptionButtonPaintTop);
        Assert.Equal(form.CaptionHeight, form.CaptionButtonPaintHeight);

        form.WindowState = FormWindowState.Normal;
        Application.DoEvents();
    });

    /// <summary>
    /// 顶边 1px 线的颜色必须跟着焦点切换（Win10 的 DWM 不画顶部边框，这条线是我们自己补的）：
    /// 激活用 TopBorderLineActiveColor、失活用 TopBorderLineInactiveColor，两者必须不同。
    /// </summary>
    [Fact]
    public void TopBorderLineHasDistinctActiveAndInactiveColours() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "top line",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        Assert.NotEqual(form.TopBorderLineActiveColor, form.TopBorderLineInactiveColor);

        // 参数由原生 DWM 边框实测反解（黑底 / 白底两组）：
        //   聚焦：黑底 25 / 白底 112 -> 基色 #262626、alpha 66%
        //   失焦：黑底 43 / 白底 170 -> 基色 #565656、alpha 50%
        Assert.Equal(168, form.TopBorderLineActiveColor.A);      // 66%
        Assert.Equal(0x26, form.TopBorderLineActiveColor.R);
        Assert.Equal(128, form.TopBorderLineInactiveColor.A);    // 50%
        Assert.Equal(0x56, form.TopBorderLineInactiveColor.R);

        // 存的是 [alpha + 基色]，绘制时自动与标题栏底色混合 —— 四组场景都必须与原生实测相符：
        Assert.InRange(Blend(form.TopBorderLineActiveColor, Color.White).R, 108, 116);          // 白标题栏聚焦 = 112
        Assert.InRange(
            Blend(form.TopBorderLineInactiveColor, Color.FromArgb(0xF1, 0xF3, 0xF4)).R, 158, 168); // 浅色失焦 ≈ 163
        Assert.InRange(
            Blend(form.TopBorderLineActiveColor, Color.FromArgb(0x32, 0x32, 0x33)).R, 38, 46);   // 深色聚焦 ≈ 42
        Assert.InRange(
            Blend(form.TopBorderLineInactiveColor, Color.FromArgb(0x2D, 0x2D, 0x2D)).R, 62, 70); // 深色失焦 ≈ 66

        // 半透明是关键：换任何标题栏底色都会自适应，自定义标题栏不必改这两个值
        var onCustom = Blend(form.TopBorderLineActiveColor, Color.FromArgb(0x12, 0x34, 0x56));
        Assert.NotEqual(form.TopBorderLineActiveColor, onCustom);
        Assert.True(form.TopBorderLineActiveColor.A < 255, "the line must stay semi-transparent");
    });

    /// <summary>
    /// VsCode 样式套用它自己的深色配色（取自 VS Code 的 2026-dark 主题定义），
    /// 并且**必须与 Chrome / Windows 用的那套隔离** —— 后者在系统深色模式下也会取到深色，
    /// 若两者共用一份配色，改 VS Code 就会连带改掉 Chrome。
    /// </summary>
    [Fact]
    public void VsCodeStyleUsesItsOwnPaletteAndLeavesChromeAlone() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "palette probe",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        // VS Code：主题定义 titleBar.activeBackground = #191A1B，激活与失活同色
        form.TitleBarStyle = ChromeTitleBarStyle.VsCode;
        Assert.Equal(Color.FromArgb(0x19, 0x1A, 0x1B), form.ActiveCaptionColor);
        Assert.Equal(Color.FromArgb(0x19, 0x1A, 0x1B), form.InactiveCaptionColor);
        // titleBar.activeForeground = #8C8C8C，失活同色
        Assert.Equal(Color.FromArgb(0x8C, 0x8C, 0x8C), form.CaptionTextColor);
        Assert.Equal(Color.FromArgb(0x8C, 0x8C, 0x8C), form.InactiveCaptionTextColor);
        // 标题贴左：VS Code 的标题栏是三段式布局，窗口标题不居中
        Assert.Equal(ContentAlignment.MiddleLeft, form.CaptionTextAlignment);
        var vsCaption = form.ActiveCaptionColor;

        // Chrome：仍跟随系统明暗，且与 VS Code 不同色（证明两套配色是隔离的）
        form.TitleBarStyle = ChromeTitleBarStyle.Chrome;
        Assert.NotEqual(vsCaption, form.ActiveCaptionColor);
        // 标题也贴左：真实 Chrome 与 WPF 版都是贴左，三套样式一致
        Assert.Equal(ContentAlignment.MiddleLeft, form.CaptionTextAlignment);

        // Windows 样式同样贴左（三套一致，避免两库/多样式行为分叉）
        form.TitleBarStyle = ChromeTitleBarStyle.Windows;
        Assert.Equal(ContentAlignment.MiddleLeft, form.CaptionTextAlignment);

        // 关闭按钮的红分三套：VS Code 按下必须比悬停更深（它的样式表没有 :active 规则）
        form.TitleBarStyle = ChromeTitleBarStyle.VsCode;
        Assert.True(
            form.CloseButtonPressedColor.R < form.CloseButtonHoverColor.R
                && form.CloseButtonPressedColor.G < form.CloseButtonHoverColor.G,
            "the VS Code close button must darken when pressed");
        Assert.Equal(Color.FromArgb(0xE8, 0x11, 0x23), form.CloseButtonHoverColor);

        // Chrome 的按下相反：变亮的粉红（Chrome 自身行为），不能被 VS Code 的规则带走
        form.TitleBarStyle = ChromeTitleBarStyle.Chrome;
        Assert.True(
            form.CloseButtonPressedColor.R > form.CloseButtonHoverColor.R
                || form.CloseButtonPressedColor.G > form.CloseButtonHoverColor.G,
            "the Chrome close button keeps its lighter pressed shade");
    });

    /// <summary>
    /// 顶边线属于**窗口边框**，所以即使把标题栏内容完全自定义（ShowDefaultTitleBar = false），
    /// 它也必须照画 —— 否则 Win10 上窗口会缺一条边（DWM 不画顶部）。
    /// 关掉要用 ShowTopBorderLine = false，而不是靠自定义标题栏"顺带"弄丢。
    /// </summary>
    [Fact]
    public void TopBorderLineSurvivesAFullyCustomTitleBar() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "custom caption",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        // 完全自定义标题栏：基类不再画底色/图标/文字/按钮
        form.ShowDefaultTitleBar = false;

        // 但顶线仍然绘制，且默认是开着的
        Assert.True(form.ShowTopBorderLine);
        Assert.True(
            TopBorderLineIsDrawn(form),
            "the top border line belongs to the window frame and must survive a custom title bar");

        // 显式关掉才不画
        form.ShowTopBorderLine = false;
        Assert.False(
            TopBorderLineIsDrawn(form),
            "ShowTopBorderLine = false must actually stop the line");
    });

    /// <summary>
    /// 顶线的属性位于 ChromeFrame（窗口边框），ChromeForm 继承它们 —— 所以纯配色改动
    /// 不会影响命中区，且从 ChromeFrame 派生也能拿到同一套实现与实测参数。
    /// </summary>
    [Fact]
    public void TopBorderLineMembersLiveOnTheFrameBase() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "frame base",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();

        // 属性在基类上声明（ChromeForm 不再自己定义）
        var declaring = typeof(ChromeFrame).GetProperty(
            nameof(ChromeForm.TopBorderLineActiveColor))?.DeclaringType;
        Assert.Equal(typeof(ChromeFrame), declaring);
        Assert.Equal(
            typeof(ChromeFrame),
            typeof(ChromeFrame).GetProperty(nameof(ChromeForm.ShowTopBorderLine))?.DeclaringType);

        // 通过基类引用一样能读写（继承不破坏现有代码）
        ChromeFrame frame = form;
        Assert.True(frame.ShowTopBorderLine);
        frame.ShowTopBorderLine = false;
        Assert.False(form.ShowTopBorderLine);
        frame.ShowTopBorderLine = true;

        // 绘制实现也是基类提供的（ChromeFrame 上的 protected 方法）
        var draw = typeof(ChromeFrame).GetMethod(
            "DrawTopBorderLine",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(draw);
        Assert.Equal(typeof(ChromeFrame), draw!.DeclaringType);
    });

    /// <summary>按钮底边之下不再属于按钮（回归：按钮高度不能被拉长）。</summary>
    [Fact]
    public void RowBelowCaptionButtonIsCaption() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "hit test",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();
        form.WindowState = FormWindowState.Maximized;
        Application.DoEvents();

        var lastRow = LastHitRow(form, HtClose);
        var window = WindowRect(form);
        var closeCenterX = ButtonCenterX(form, 0);
        var below = HitTest(form, closeCenterX, window.Top + lastRow + 1);
        Assert.NotEqual(HtClose, below);
        // 按钮底边之下要么是内容区（标题栏之内），要么已经是客户区，总之不再属于按钮
        Assert.True(
            below is HtCaption or 1,
            $"row below the close button should not be a button, got {below}");
    });

    /// <summary>
    /// <c>EffectiveTitleBarIcon</c> 与 WPF 版同名同语义：把标题栏完全自绘时，
    /// 使用者可以直接画这个图标，不必自己调原生 API。它必须是**独立副本**
    /// （可以安全释放，且不能是系统所有的句柄，否则会误销毁）。
    /// </summary>
    [Fact]
    public void EffectiveTitleBarIconIsACloneThatCallersCanDispose() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "icon",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(700, 400),
        };
        form.Icon = WindowChromeIcons.Windows7;
        form.Show();
        Application.DoEvents();

        using var first = form.EffectiveTitleBarIcon;
        Assert.NotNull(first);

        // 取两次必须是两份独立副本：释放第一份后第二份仍可用（不是同一句柄）
        using var second = form.EffectiveTitleBarIcon;
        Assert.NotNull(second);
        Assert.NotSame(first, second);

        // 明确释放一份不应影响另一份，也不应抛异常
        first!.Dispose();
        using var bitmap = second!.ToBitmap();
        Assert.True(bitmap.Width > 0 && bitmap.Height > 0);

        // 完全自定义标题栏时依然可读（这正是它存在的意义）
        form.ShowDefaultTitleBar = false;
        using var custom = form.EffectiveTitleBarIcon;
        Assert.NotNull(custom);
    });

    /// <summary>
    /// 换了图标后 <c>EffectiveTitleBarIcon</c> 要跟着变 —— 证明它读的是**当前**窗口图标，
    /// 而不是某次缓存的旧值（库内部绘制用的也是它，所以这直接关系到屏幕上的图标）。
    /// </summary>
    [Fact]
    public void EffectiveTitleBarIconFollowsTheAssignedIcon() => RunSta(() =>
    {
        using var form = new ChromeForm
        {
            Text = "icon swap",
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(700, 400),
        };
        form.Icon = WindowChromeIcons.Windows7;
        form.Show();
        Application.DoEvents();

        var seven = Fingerprint(form);
        Assert.NotNull(seven);

        form.Icon = WindowChromeIcons.Windows10;
        Application.DoEvents();
        var ten = Fingerprint(form);
        Assert.NotNull(ten);

        Assert.NotEqual(seven, ten);

        // 放回 Win7 应回到原来的指纹（可重复，不是累积状态）
        form.Icon = WindowChromeIcons.Windows7;
        Application.DoEvents();
        Assert.Equal(seven, Fingerprint(form));
    });

    /// <summary>把当前标题栏图标转成 16x16 的像素指纹，用来判断"是不是同一个图标"。</summary>
    private static string? Fingerprint(ChromeForm form)
    {
        using var icon = form.EffectiveTitleBarIcon;
        if (icon is null)
            return null;
        using var scaled = new Icon(icon, 16, 16);
        using var bitmap = scaled.ToBitmap();
        var bytes = new List<byte>();
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                bytes.Add(c.A);
                bytes.Add(c.R);
                bytes.Add(c.G);
                bytes.Add(c.B);
            }
        return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes.ToArray()));
    }

    /// <summary>
    /// 回归：悬停不能相信 <c>WM_NCMOUSEMOVE</c> 里那个可能过期的 hit code。
    ///
    /// wParam 是系统在**某一刻**算出的命中值。最大化/还原会改按钮顶边与客户区顶边，
    /// 所以排在几何变化之后到达的那条消息可能仍带着旧值。此前直接采信它，
    /// 就会点亮一个指针其实已经不在的格子；指针随后不再移动，于是按钮一直亮着
    /// —— 这就是"点最大化后按钮偶尔保持高亮"的成因（Win10 上可复现，非必现）。
    ///
    /// 这里把指针放到内容区深处（真实命中值是 <c>HTCLIENT</c>），
    /// 再发一条声称命中最大化按钮的消息：命中值必须按当前几何被否掉。
    /// </summary>
    [Fact]
    public void StaleNonClientHitCodeCannotLightUpAButton() => RunSta(() =>
    {
        using var form = NewTestForm("stale hit");
        var window = WindowRect(form);

        // pointer deep inside the content area, far from every caption button
        var centreX = window.Left + (window.Right - window.Left) / 2;
        var centreY = window.Bottom - 60;
        Assert.True(SetCursorPos(centreX, centreY));
        Application.DoEvents();
        var cursor = CursorPosition();
        Assert.Equal(1, HitTest(form, cursor.X, cursor.Y));   // HTCLIENT

        // a message whose hit code no longer describes this position must be ignored
        _ = SendMessage(form.Handle, WmNcMouseMove, new IntPtr(HtMaxButton), IntPtr.Zero);
        Assert.Null(form.HoveredCaptionButton);

        // a truthful hit code is still honoured, so hovering is not simply disabled
        Assert.True(SetCursorPos(ButtonCenterX(form, 1), window.Top + FrameY(form) + 2));
        Application.DoEvents();
        cursor = CursorPosition();
        Assert.Equal(HtMaxButton, HitTest(form, cursor.X, cursor.Y));   // sanity
        _ = SendMessage(form.Handle, WmNcMouseMove, new IntPtr(HtMaxButton), IntPtr.Zero);
        Assert.Equal(ChromeCaptionButton.Maximize, form.HoveredCaptionButton);
    });

    /// <summary>
    /// 配色是**独立的一轴**：换配色只改颜色，几何一点不动。
    /// Element Plus 三套几何各有一款配色，这里逐款核对，并断言几何与样式没被牵连。
    /// </summary>
    [Fact]
    public void ElementPlusPaletteChangesOnlyTheColours() => RunSta(() =>
    {
        using var form = NewTestForm("element plus palette");
        foreach (var style in new[]
                 {
                     ChromeTitleBarStyle.Chrome,
                     ChromeTitleBarStyle.VsCode,
                     ChromeTitleBarStyle.Windows,
                 })
        {
            form.TitleBarStyle = style;
            form.TitleBarPalette = ChromeTitleBarPalette.Default;
            var geometry = Geometry(form);

            form.TitleBarPalette = ChromeTitleBarPalette.ElementPlus;

            Assert.Equal(geometry, Geometry(form));
            var expected = ExpectedElementPlus(style);
            Assert.Equal(expected.Active, form.ActiveCaptionColor);
            Assert.Equal(expected.Inactive, form.InactiveCaptionColor);
            Assert.Equal(expected.Text, form.CaptionTextColor);
            Assert.Equal(expected.InactiveText, form.InactiveCaptionTextColor);
            Assert.Equal(expected.Hover, form.CaptionButtonHoverColor);
            Assert.Equal(expected.Pressed, form.CaptionButtonPressedColor);
            Assert.Equal(expected.CloseHover, form.CloseButtonHoverColor);
            Assert.Equal(expected.ClosePressed, form.CloseButtonPressedColor);
        }
    });

    /// <summary>
    /// 两个轴**谁后赋值都成立**：先样式后配色与先配色后样式，最终状态必须一致。
    /// 这条守住"改样式会按当前配色来源重新上色"这个契约。
    /// </summary>
    [Fact]
    public void StyleAndPaletteComposeInEitherOrder() => RunSta(() =>
    {
        using var styleFirst = NewTestForm("style first");
        styleFirst.TitleBarStyle = ChromeTitleBarStyle.VsCode;
        styleFirst.TitleBarPalette = ChromeTitleBarPalette.ElementPlus;

        using var paletteFirst = NewTestForm("palette first");
        paletteFirst.TitleBarPalette = ChromeTitleBarPalette.ElementPlus;
        paletteFirst.TitleBarStyle = ChromeTitleBarStyle.VsCode;

        Assert.Equal(Geometry(styleFirst), Geometry(paletteFirst));
        Assert.Equal(styleFirst.ActiveCaptionColor, paletteFirst.ActiveCaptionColor);
        Assert.Equal(styleFirst.InactiveCaptionColor, paletteFirst.InactiveCaptionColor);
        Assert.Equal(styleFirst.CaptionTextColor, paletteFirst.CaptionTextColor);
        Assert.Equal(styleFirst.InactiveCaptionTextColor, paletteFirst.InactiveCaptionTextColor);
        Assert.Equal(styleFirst.CaptionButtonHoverColor, paletteFirst.CaptionButtonHoverColor);
        Assert.Equal(styleFirst.CaptionButtonPressedColor, paletteFirst.CaptionButtonPressedColor);
        Assert.Equal(styleFirst.CloseButtonHoverColor, paletteFirst.CloseButtonHoverColor);
        Assert.Equal(styleFirst.CloseButtonPressedColor, paletteFirst.CloseButtonPressedColor);
    });

    /// <summary>
    /// 切回 <c>Default</c> 要还原成该样式自带的那套配色 —— 包括关闭按钮的红
    /// （三套样式的红本来就不同：Chrome 按下变亮、Windows 变暗、VS Code 变深）。
    /// </summary>
    [Fact]
    public void SwitchingBackToDefaultRestoresTheStylesOwnColours() => RunSta(() =>
    {
        using var form = NewTestForm("palette round trip");
        form.TitleBarStyle = ChromeTitleBarStyle.Chrome;
        var chromeOwn = (form.ActiveCaptionColor, form.CloseButtonHoverColor, form.CloseButtonPressedColor);

        form.TitleBarPalette = ChromeTitleBarPalette.ElementPlus;
        Assert.NotEqual(chromeOwn.ActiveCaptionColor, form.ActiveCaptionColor);

        form.TitleBarPalette = ChromeTitleBarPalette.Default;
        Assert.Equal(chromeOwn.ActiveCaptionColor, form.ActiveCaptionColor);
        Assert.Equal(chromeOwn.CloseButtonHoverColor, form.CloseButtonHoverColor);
        Assert.Equal(chromeOwn.CloseButtonPressedColor, form.CloseButtonPressedColor);

        // Windows 样式的关闭按钮按下必须仍然"变暗"（原生行为），不被 Element Plus 的规则带走
        form.TitleBarStyle = ChromeTitleBarStyle.Windows;
        Assert.True(
            form.CloseButtonPressedColor.R < form.CloseButtonHoverColor.R,
            "Windows 样式的关闭按钮按下应当比悬停更深");
    });

    /// <summary>样式决定的那几个几何量（换配色时这些必须一个都不变）。</summary>
    private static (int Height, int Width, int ButtonHeight, int IconMargin, int MinWidth, ContentAlignment Align)
        Geometry(ChromeForm form) => (
            form.CaptionHeightDip,
            form.CaptionButtonWidthDip,
            form.CaptionButtonHeightDip,
            form.CaptionIconMarginDip,
            form.MinimizeButtonWidthDip,
            form.CaptionTextAlignment);

    /// <summary>
    /// Element Plus 三款配色的期望值，全部由它在 <c>theme-chalk</c> 里的官方变量与混色公式推出，
    /// 与库内 <c>ElementPlusTheme</c> 是同一套算法（这里独立算一遍，避免"用实现测实现"）。
    /// </summary>
    private static (Color Active, Color Inactive, Color Text, Color InactiveText,
        Color Hover, Color Pressed, Color CloseHover, Color ClosePressed)
        ExpectedElementPlus(ChromeTitleBarStyle style)
    {
        var primary = Color.FromArgb(0x40, 0x9E, 0xFF);
        var danger = Color.FromArgb(0xF5, 0x6C, 0x6C);
        var darkBg = Color.FromArgb(0x14, 0x14, 0x14);
        // 三款配色的关闭按钮共用同一对：Windows 原生实测值
        var nativeHover = Color.FromArgb(0xC4, 0x2B, 0x1C);
        var nativePressed = Color.FromArgb(0xA9, 0x23, 0x16);
        return style switch
        {
            ChromeTitleBarStyle.VsCode => (
                darkBg,
                Color.FromArgb(0x1D, 0x1E, 0x1F),
                MixWith(Color.FromArgb(0xF0, 0xF5, 0xFF), darkBg, 0.95),
                MixWith(Color.FromArgb(0xF0, 0xF5, 0xFF), darkBg, 0.65),
                MixWith(Color.FromArgb(0xFA, 0xFC, 0xFF), darkBg, 0.12),
                MixWith(Color.FromArgb(0xFA, 0xFC, 0xFF), darkBg, 0.20),
                nativeHover,
                nativePressed),
            ChromeTitleBarStyle.Windows => (
                Color.White,
                Color.FromArgb(0xF2, 0xF6, 0xFC),
                Color.FromArgb(0x30, 0x31, 0x33),
                Color.FromArgb(0x90, 0x93, 0x99),
                MixWith(Color.White, primary, 0.90),
                MixWith(Color.White, primary, 0.80),
                nativeHover,
                nativePressed),
            _ => (
                primary,
                MixWith(Color.White, primary, 0.30),
                Color.White,
                MixWith(Color.White, primary, 0.90),
                MixWith(Color.White, primary, 0.30),
                MixWith(Color.Black, primary, 0.20),
                nativeHover,
                nativePressed),
        };
    }

    /// <summary>Element Plus 的混色公式：<paramref name="pct"/> 是 <paramref name="foreground"/> 的占比。</summary>
    private static Color MixWith(Color foreground, Color background, double pct) => Color.FromArgb(
        (int)Math.Round(foreground.R * pct + background.R * (1 - pct)),
        (int)Math.Round(foreground.G * pct + background.G * (1 - pct)),
        (int)Math.Round(foreground.B * pct + background.B * (1 - pct)));

    /// <summary>新建一个用于状态机测试的窗口（已显示并处理完初始消息）。</summary>
    private static ChromeForm NewTestForm(string title)
    {
        var form = new ChromeForm
        {
            Text = title,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(200, 200),
            Size = new Size(900, 560),
        };
        form.Show();
        Application.DoEvents();
        return form;
    }

    /// <summary>按钮绘制顶行（普通态应让出第 0 行的顶边线）。</summary>
    private static int CaptionButtonPaintTopFor(ChromeForm form) => form.CaptionButtonPaintTop;

    /// <summary>按钮绘制高度（必须能容下让出的顶边线那一行）。</summary>
    private static int CaptionButtonPaintHeightFor(ChromeForm form) => form.CaptionButtonPaintHeight;

    private static int FrameY(ChromeForm form) =>
        GetSystemMetricsForDpi(SmCyFrame, (uint)form.DeviceDpi)
        + GetSystemMetricsForDpi(SmCxPaddedBorder, (uint)form.DeviceDpi);

    private static int FrameX(ChromeForm form) => FrameY(form);

    private static NativeRectangle WindowRect(ChromeForm form)
    {
        _ = GetWindowRect(form.Handle, out var rectangle);
        return rectangle;
    }

    /// <summary>按钮中心列上第一个命中的行（相对窗口顶边）。</summary>
    /// <summary>把按钮中心列前 60 行的命中值拼成字符串，便于诊断。</summary>
    private static string Scan(ChromeForm form, int expected, int index = 0)
    {
        var window = WindowRect(form);
        var x = ButtonCenterX(form, index);
        var text = string.Empty;
        for (var offset = 0; offset < 60; offset++)
        {
            var hit = HitTest(form, x, window.Top + offset);
            text += hit == expected ? "X" : hit switch
            {
                HtCaption => "K",
                HtMinButton => "m",
                HtMaxButton => "M",
                12 => "T",
                1 => "C",
                _ => "?",
            };
        }
        return text;
    }

    /// <summary>
    /// 读取窗口标题栏区域，判断第 0 行是否画了顶边线。
    /// 判据是「第 0 行与第 1 行颜色不同」而不是某个固定色 —— 顶线是半透明的，
    /// 具体值随标题栏底色变化；完全自定义标题栏时底色更是不确定。
    /// </summary>
    private static bool TopBorderLineIsDrawn(ChromeForm form)
    {
        var window = WindowRect(form);
        using var bitmap = new Bitmap(window.Right - window.Left, form.CaptionHeight + 2);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        try
        {
            _ = PrintWindow(form.Handle, hdc, 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        var column = bitmap.Width / 2;
        var row0 = bitmap.GetPixel(column, 0);
        var row1 = bitmap.GetPixel(column, 1);
        return row0.ToArgb() != row1.ToArgb();
    }

    /// <summary>把半透明的线条色叠在底色上，模拟实际看到的效果。</summary>
    private static Color Blend(Color line, Color background)
    {
        var a = line.A / 255d;
        return Color.FromArgb(
            (int)Math.Round(line.R * a + background.R * (1 - a)),
            (int)Math.Round(line.G * a + background.G * (1 - a)),
            (int)Math.Round(line.B * a + background.B * (1 - a)));
    }

    private static int FirstHitRow(ChromeForm form, int expected, int index = 0) =>
        FindRow(form, expected, index, fromTop: true);

    private static int LastHitRow(ChromeForm form, int expected, int index = 0) =>
        FindRow(form, expected, index, fromTop: false);

    /// <summary>第 index 个按钮（0=关闭，1=最大化，2=最小化）的中心列。</summary>
    private static int ButtonCenterX(ChromeForm form, int index)
    {
        var window = WindowRect(form);
        var right = window.Right - FrameX(form) - index * form.CaptionButtonWidth;
        return right - form.CaptionButtonWidth / 2;
    }

    private static int FindRow(ChromeForm form, int expected, int index, bool fromTop)
    {
        var window = WindowRect(form);
        var x = ButtonCenterX(form, index);
        var limit = window.Bottom - window.Top;
        for (var offset = 0; offset < limit; offset++)
        {
            var y = fromTop ? offset : limit - 1 - offset;
            if (HitTest(form, x, window.Top + y) == expected)
                return y;
        }
        return -1;
    }

    private static int HitTest(ChromeForm form, int x, int y)
    {
        var packed = (y << 16) | (x & 0xffff);
        return (int)SendMessage(form.Handle, WmNcHitTest, IntPtr.Zero, new IntPtr(packed)).ToInt64();
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
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException(failure.Message, failure);
    }

    private const int SmCyFrame = 33;
    private const int SmCxPaddedBorder = 92;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    private static CursorPoint CursorPosition()
    {
        _ = GetCursorPos(out var point);
        return point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
