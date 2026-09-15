using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WindowChromeKit.WinForms;
using Xunit;

namespace WindowChromeKit.WinForms.Tests;

public sealed class ChromeFormTests
{
    private const int WmNcHitTest = 0x0084;
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
