using System.Drawing;
using System.Windows.Forms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 标题栏样式示例：一个窗口里用按钮切换 <see cref="ChromeTitleBarStyle"/>，
/// 直观对比 Chrome / VS Code / Windows 三种预置样式（几何 + 配色一次性套用）。
/// </summary>
public sealed class TitleBarStyleForm : ChromeForm
{
    private readonly Label _status;

    public TitleBarStyleForm()
    {
        Text = "标题栏样式示例（WinForms）";
        Icon = WindowChromeIcons.Windows10;
        Width = 880;
        Height = 560;
        MinimumSize = new Size(460, 320);
        StartPosition = FormStartPosition.CenterScreen;
        // 内容区纯白，方便看清标题栏与内容区的分界
        BackColor = Color.White;

        var header = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 20, 24, 0),
            Font = new Font(Font.FontFamily, Font.Size + 4f, FontStyle.Bold),
            Text = "标题栏样式：Chrome / VS Code / Windows",
        };

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 10, 24, 0),
            Text =
                "1. 阴影与圆角：窗口外侧应有一圈柔和阴影，四角是 Windows 11 的圆角，没有硬边或黑边。\r\n"
                + "2. 阴影里的缩放带：把鼠标移到窗口外侧约 8 像素（视觉上在阴影里）应出现缩放光标；顶部这条带更窄（约 6 像素）。\r\n"
                + "3. 顶边 1 像素线：普通态窗口最上面一行是边框线（Windows 11 由 DWM 画）；最大化后不应出现这条线。\r\n"
                + "4. 标题栏：图标盒子 22×22、图标 16 在盒内；点图标弹系统菜单，拖标题栏移动，双击最大化/还原。\r\n"
                + "5. 样式切换：下面三个按钮改 TitleBarStyle，几何与配色一次性套用；之后单独改属性以属性为准。\r\n"
                + "6. 最大化：客户区正好铺满工作区，标题栏不出现 1 像素缝隙，最大化按钮变「还原」，悬停弹出 Snap Layouts。",
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(24, 8, 0, 0),
        };
        foreach (var style in new[]
                 {
                     ChromeTitleBarStyle.Chrome,
                     ChromeTitleBarStyle.VsCode,
                     ChromeTitleBarStyle.Windows,
                 })
        {
            var button = new Button
            {
                Text = Describe(style).Name,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 12, 0),
            };
            var captured = style;
            button.Click += (_, _) =>
            {
                TitleBarStyle = captured;
                UpdateStatus();
            };
            buttons.Controls.Add(button);
        }

        _status = new Label
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 10, 24, 0),
            Text = string.Empty,
        };

        var preview = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(24, 12, 24, 24),
            BackColor = Color.FromArgb(0xE8, 0xF0, 0xFE),
            BorderStyle = BorderStyle.FixedSingle,
        };
        preview.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(0x17, 0x4E, 0xA6),
            Text = "拖动标题栏、双击最大化、点图标弹系统菜单 —— 三套样式下行为完全一致",
        });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(hint, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        layout.Controls.Add(_status, 0, 3);
        layout.Controls.Add(preview, 0, 4);
        Controls.Add(layout);

        UpdateStatus();
    }

    /// <summary>样式说明（标题栏高度、按钮尺寸、图标位置、配色来源）。</summary>
    private static (string Name, string Detail) Describe(ChromeTitleBarStyle style) => style switch
    {
        ChromeTitleBarStyle.VsCode => (
            "VS Code 样式",
            "标题栏 35 / 按钮 46×34 / 图标 12px 位 / 配色固定深色 #323233"),
        ChromeTitleBarStyle.Windows => (
            "Windows 样式",
            "标题栏 32 / 按钮 44×32 / 图标贴左 3px 位 / 配色跟随系统明暗"),
        _ => (
            "Chrome 样式",
            "标题栏 40 / 按钮 46×39 / 图标 12px 位 / 配色跟随系统明暗"),
    };

    private void UpdateStatus() => _status.Text = $"当前样式：{Describe(TitleBarStyle).Detail}";

}
