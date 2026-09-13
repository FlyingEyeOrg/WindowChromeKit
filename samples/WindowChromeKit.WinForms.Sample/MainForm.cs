using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 示例窗体：直接继承 <see cref="ChromeForm"/>，标题栏由程序集自绘，
/// 内容区（<see cref="Control.DisplayRectangle"/>，即客户区减去标题栏）里放普通 WinForms 控件。
/// </summary>
public sealed class MainForm : ChromeForm
{
    private readonly Button _edgeButton;

    public MainForm()
    {
        Text = "WindowChromeKit WinForms Sample";
        // 与 C++ 示例同样的窗口尺寸，便于并排比对
        Width = 776;
        Height = 528;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(320, 200);

        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 132,
            Padding = new Padding(16, 12, 16, 0),
            Text =
                "窗口样式：保留 WS_CAPTION | WS_THICKFRAME，DWM 提供阴影与\"阴影里那圈\"不可见缩放带。\r\n" +
                "WM_NCCALCSIZE 把客户区从窗口矩形内缩出 frame；WM_NCHITTEST 按 Chrome 实测的优先级判定。\r\n" +
                "把鼠标移到窗口边缘外侧 8px（视觉上在阴影里）会变成缩放光标；窗口矩形之外是 HTNOWHERE。\r\n" +
                "拖动标题栏移动、双击标题栏最大化/还原、悬停最大化按钮弹出 Windows 11 Snap Layouts。\r\n" +
                "下面这两个按钮放在内容区（DisplayRectangle）里，右下角那个紧贴内容区边缘。",
        };

        var openButton = new Button
        {
            Text = "打开第二个窗口",
            Width = 180,
            Height = 32,
            Left = 16,
            Top = 200,
        };
        openButton.Click += (_, _) => new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(Left + 48, Top + 48) }.Show();

        _edgeButton = new Button
        {
            Text = "贴内容区右下角（可点）",
            Width = 200,
            Height = 28,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };

        Controls.Add(info);
        Controls.Add(openButton);
        Controls.Add(_edgeButton);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // 内容区（DisplayRectangle）右下角：这两个坐标来自重写后的内容区，标题栏不占用它
        var content = DisplayRectangle;
        _edgeButton.Location = new Point(
            content.Right - _edgeButton.Width,
            content.Bottom - _edgeButton.Height);
    }
}
