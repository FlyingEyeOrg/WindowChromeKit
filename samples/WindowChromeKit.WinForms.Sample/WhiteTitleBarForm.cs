using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 白色标题栏示例：与 WPF 版 <c>WhiteTitleBarWindow</c> 一一对应，用 Chrome 浅色配色
/// （白底 #FFFFFF / 文字 #202124 / 悬停 #E8EAED / 关闭悬停 #E81123），
/// 窗口里带上人工核对清单，用来检查原生 frame 的各项行为。
/// </summary>
public sealed class WhiteTitleBarForm : ChromeForm
{
    private readonly Label _status;

    public WhiteTitleBarForm()
    {
        Text = "白色标题栏示例（WinForms）";
        Icon = WindowChromeIcons.Windows10;
        Width = 880;
        Height = 600;
        MinimumSize = new Size(460, 320);
        StartPosition = FormStartPosition.CenterScreen;
        // 内容区背景纯白：方便观察窗口可见边缘与阴影（标题栏下的那条界线也能看清）
        BackColor = Color.White;

        // Chrome 浅色配色；顶部 1 像素线的颜色与 DWM 画在其余三边的实测值一致
        ActiveCaptionColor = Color.FromArgb(0xFF, 0xFF, 0xFF);
        CaptionTextColor = Color.FromArgb(0x20, 0x21, 0x24);
        InactiveCaptionColor = Color.FromArgb(0xF1, 0xF3, 0xF4);
        InactiveCaptionTextColor = Color.FromArgb(0x80, 0x86, 0x8B);
        CaptionButtonHoverColor = Color.FromArgb(0xE8, 0xEA, 0xED);
        CaptionButtonPressedColor = Color.FromArgb(0xDA, 0xDC, 0xE0);
        CloseButtonHoverColor = Color.FromArgb(0xE8, 0x11, 0x23);
        CloseButtonPressedColor = Color.FromArgb(0xF1, 0x70, 0x7A);
        TopBorderLineActiveColor = Color.FromArgb(0x70, 0x70, 0x70);
        TopBorderLineInactiveColor = Color.FromArgb(0xAA, 0xAA, 0xAA);

        var header = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 20, 24, 0),
            Font = new Font(Font.FontFamily, Font.Size + 4f, FontStyle.Bold),
            Text = "白色标题栏（Chrome 浅色配色）",
        };

        var checklist = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 10, 24, 0),
            Text =
                "与 samples/WindowChromeKit.Native.Sample、WindowChromeKit.Wpf 同一套原生 frame 模型，下面是人工核对清单。\r\n\r\n"
                + "1. 阴影与圆角：窗口外侧应有一圈柔和阴影，四角是 Windows 11 的圆角，没有硬边或黑边。\r\n"
                + "2. 阴影里的缩放带：把鼠标移到窗口外侧约 8 像素（视觉上在阴影里）应出现缩放光标；顶部这条带更窄（约 6 像素）。\r\n"
                + "3. 顶边 1 像素线：普通态窗口最上面一行是边框线（Windows 11 由 DWM 画）；最大化后不应出现这条线。\r\n"
                + "4. 标题栏：高 40、图标左边距 12、图标 16、标题居中；按钮 46×39，悬停灰色、关闭悬停红色。\r\n"
                + "5. 最大化：客户区正好铺满工作区，标题栏不出现 1 像素缝隙，最大化按钮变成「还原」字形，悬停弹出 Snap Layouts。\r\n"
                + "6. 拖动与双击：拖标题栏可移动，双击标题栏在最大化/还原之间切换，右击标题栏图标弹出系统菜单。\r\n\r\n"
                + "下面这块浅蓝区域铺满内容区（DisplayRectangle）：它的上边紧贴标题栏下边，左右与下边紧贴可见边缘内侧（frame 之外）。",
        };

        var contentSample = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(24, 12, 24, 12),
            BackColor = Color.FromArgb(0xE8, 0xF0, 0xFE),
            BorderStyle = BorderStyle.FixedSingle,
        };
        contentSample.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(0x17, 0x4E, 0xA6),
            Text = "内容区样例：拖动窗口右下角缩放时，这块应始终贴着可见边缘。",
        });

        _status = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(0x5F, 0x63, 0x68),
            Text = "窗口状态：普通态",
        };

        var openAnother = new Button { Text = "再开一个白色窗口", AutoSize = true, Margin = new Padding(0, 0, 12, 0) };
        openAnother.Click += (_, _) => new WhiteTitleBarForm { Owner = this }.Show();
        var toggleMaximize = new Button { Text = "最大化 / 还原", AutoSize = true };
        toggleMaximize.Click += (_, _) =>
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(12, 10, 24, 12),
        };
        // 顺序：左边「再开一个」，右边「最大化 / 还原」（AutoSize 列按首选宽度测量，
        // 用 RightToLeft 会被裁掉一部分）
        buttonRow.Controls.Add(openAnother);
        buttonRow.Controls.Add(toggleMaximize);

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.Controls.Add(_status, 0, 0);
        statusRow.Controls.Add(buttonRow, 1, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 4,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(checklist, 0, 1);
        layout.Controls.Add(contentSample, 0, 2);
        layout.Controls.Add(statusRow, 0, 3);

        Controls.Add(layout);

        // WinForms 没有 WPF 的 StateChanged，用 Resize 覆盖最大化/还原
        Resize += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    private void UpdateStatus() =>
        _status.Text = WindowState == FormWindowState.Maximized
            ? "窗口状态：最大化（此时客户区应正好等于工作区，顶部没有多余线条）"
            : "窗口状态：普通态（顶部第 0 行应是一条 1 像素边框线）";
}
