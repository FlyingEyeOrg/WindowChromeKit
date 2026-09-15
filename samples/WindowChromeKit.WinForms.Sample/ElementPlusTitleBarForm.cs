using System.Drawing;
using System.Windows.Forms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 基于预置样式再改配色的示例：先套用 <see cref="ChromeTitleBarStyle.Chrome"/> 的几何，
/// 再把标题栏配色换成 Element Plus 的主色。
///
/// **顺序很重要**：<c>TitleBarStyle</c> 赋值时会一次性套用整张样式表（几何 + 配色），
/// 所以必须**先选样式、后改颜色**；反过来改的颜色会被样式覆盖回去。
///
/// 顶边线不用管：它存的是半透明基色，绘制时与标题栏底色混合，
/// 换成任何配色都会自动跟随（本窗口激活时混出约 #2F4F70）。
/// </summary>
public sealed class ElementPlusTitleBarForm : ChromeForm
{
    // Element Plus 官方变量（取自它的 theme-chalk/common/var.scss）：
    //   primary       #409EFF
    //   primary light-3 = primary 混 30% 白 = #79BBFF（失活底色）
    //   primary light-9 = primary 混 90% 白 = #ECF5FF（失活文字）
    //   primary dark-2  = primary 混 20% 黑 = #337ECC（按钮 hover）
    //   danger        #F56C6C / danger dark-2 #C45656（关闭按钮）
    private static readonly Color Primary = Color.FromArgb(0x40, 0x9E, 0xFF);
    private static readonly Color PrimaryLight3 = Color.FromArgb(0x79, 0xBB, 0xFF);
    private static readonly Color PrimaryLight9 = Color.FromArgb(0xEC, 0xF5, 0xFF);
    private static readonly Color PrimaryDark2 = Color.FromArgb(0x33, 0x7E, 0xCC);
    private static readonly Color Danger = Color.FromArgb(0xF5, 0x6C, 0x6C);
    private static readonly Color DangerDark2 = Color.FromArgb(0xC4, 0x56, 0x56);

    public ElementPlusTitleBarForm()
    {
        Icon = WindowChromeIcons.Windows10;
        Text = "Element Plus 主色标题栏（Chrome 样式 + 自定义配色）";
        Width = 900;
        Height = 560;
        MinimumSize = new Size(460, 320);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        // 1) 先套样式：拿到 Chrome 的几何（标题栏 40、按钮 46×39、图标 12px 位）。
        //    这一步会把配色设成 Chrome 的默认值 —— 所以颜色必须在它之后改。
        TitleBarStyle = ChromeTitleBarStyle.Chrome;

        // 2) 再改配色：以属性为准，样式不会再覆盖回来。
        ActiveCaptionColor = Primary;              // 品牌主色
        InactiveCaptionColor = PrimaryLight3;      // 同色系变淡
        CaptionTextColor = Color.White;            // 主色上必须用白字
        InactiveCaptionTextColor = PrimaryLight9;  // 近白，失活时仍可读
        CaptionButtonHoverColor = PrimaryDark2;    // 比底色深一档
        CaptionButtonPressedColor = PrimaryDark2;  // Element Plus 用同一个 active 色
        CloseButtonHoverColor = Danger;
        CloseButtonPressedColor = DangerDark2;

        // 顶线保持默认即可 —— 它是半透明的，会自动与上面的底色混合。

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(24, 18, 24, 0),
            Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x30, 0x31, 0x33),
            Text = "基于 Chrome 样式，只换配色",
        };

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 172,
            Padding = new Padding(24, 8, 24, 0),
            ForeColor = Color.FromArgb(0x60, 0x62, 0x66),
            Text =
                "1. 顺序：先 TitleBarStyle = Chrome，再改 ActiveCaptionColor 等属性。\r\n"
                + "   反过来写的话，样式表会把颜色覆盖回默认值。\r\n"
                + "2. 顶边 1 像素线不用改：它是半透明基色，与标题栏底色混合（激活时约 #2F4F70），\r\n"
                + "   换任何配色都自动跟随。\r\n"
                + "3. 配色取自 Element Plus 的官方变量（theme-chalk/common/var.scss）：\r\n"
                + "   primary #409EFF、light-3 #79BBFF、light-9 #ECF5FF、\r\n"
                + "   dark-2 #337ECC、danger #F56C6C、danger dark-2 #C45656。\r\n"
                + "4. 点标题栏 / 点别的窗口，看激活与失活两套配色，以及按钮的悬停与按下填充。",
        };

        var swatches = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(24, 8, 0, 0),
        };
        AddSwatch(swatches, "激活底 #409EFF", Primary, Color.White);
        AddSwatch(swatches, "失活底 #79BBFF", PrimaryLight3, Color.FromArgb(0x30, 0x31, 0x33));
        AddSwatch(swatches, "按钮 hover #337ECC", PrimaryDark2, Color.White);
        AddSwatch(swatches, "关闭 hover #F56C6C", Danger, Color.White);

        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        // Dock 顺序：后加的在上层，所以按“从下往上”添加
        Controls.Add(spacer);
        Controls.Add(swatches);
        Controls.Add(hint);
        Controls.Add(header);
    }

    private static void AddSwatch(Control parent, string text, Color background, Color foreground)
    {
        parent.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Width = 190,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = background,
            ForeColor = foreground,
            Margin = new Padding(0, 0, 10, 0),
        });
    }
}
