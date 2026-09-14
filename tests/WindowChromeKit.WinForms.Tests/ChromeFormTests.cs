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
        var maximizedTop = FirstHitRow(form, HtClose);
        var maximizedBottom = LastHitRow(form, HtClose);
        var maximizedScan = Scan(form, HtClose);
        Assert.True(
            form.CaptionButtonTop + frameY == maximizedTop,
            $"maximized: expected first row {form.CaptionButtonTop + frameY} but got {maximizedTop}; scan={maximizedScan}");
        Assert.True(
            maximizedTop + buttonHeight - 1 == maximizedBottom,
            $"maximized: expected last row {maximizedTop + buttonHeight - 1} but got {maximizedBottom}; scan={maximizedScan}");

        // 三个按钮各自一致（横向不重叠、各自的列都要能命中）
        foreach (var (hit, index) in new[] { (HtClose, 0), (HtMaxButton, 1), (HtMinButton, 2) })
        {
            var top = FirstHitRow(form, hit, index);
            var bottom = LastHitRow(form, hit, index);
            var scan = Scan(form, hit, index);
            Assert.True(
                top == form.CaptionButtonTop + frameY,
                $"button {hit}: expected first row {form.CaptionButtonTop + frameY} but got {top}; scan={scan}");
            Assert.True(
                bottom == top + buttonHeight - 1,
                $"button {hit}: expected last row {top + buttonHeight - 1} but got {bottom}; scan={scan}");
        }
    });

    /// <summary>按钮底边之下应回到标题栏（HTCAPTION），说明按钮高度没有被拉长。</summary>
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
