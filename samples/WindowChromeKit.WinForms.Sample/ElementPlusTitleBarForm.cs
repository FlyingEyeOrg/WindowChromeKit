using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 基于预置样式再改配色的示例：Chrome / VS Code / Windows **三套几何样式各配一款**
/// Element Plus 配色，可以在这里逐一切换对比。
///
/// **顺序很重要**：<c>TitleBarStyle</c> 赋值时会一次性套用整张样式表（几何 + 配色），
/// 所以必须**先选样式、后改颜色**；反过来改的颜色会被样式覆盖回去。
/// 这里统一由 <see cref="ElementPlusPalettes.Apply"/> 处理这个顺序。
///
/// 顶边线不用管：它存的是半透明基色，绘制时与标题栏底色混合，换成任何配色都会自动跟随。
/// </summary>
public sealed class ElementPlusTitleBarForm : ChromeForm
{
    private readonly Label _status;
    private readonly FlowLayoutPanel _swatches;

    public ElementPlusTitleBarForm()
    {
        // Win7 图标：暖橙色在深色底和 primary 蓝底上都看得清，
        // 而 Win10 图标的蓝会融进 primary 底色。
        Icon = WindowChromeIcons.Windows7;
        Text = "Element Plus 配色 × 三套样式";
        Width = 960;
        Height = 600;
        MinimumSize = new Size(520, 360);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;

        var header = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(24, 18, 24, 0),
            Font = new Font(Font.FontFamily, Font.Size + 3f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x30, 0x31, 0x33),
            Text = "Element Plus 配色 × 三套样式",
        };

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 152,
            Padding = new Padding(24, 8, 24, 0),
            ForeColor = Color.FromArgb(0x60, 0x62, 0x66),
            Text =
                "1. 同一个窗口里切换「几何样式」（Chrome / VS Code / Windows），每套都换上一款 Element Plus 配色。\r\n"
                + "2. 顺序：先 TitleBarStyle，再改颜色属性 —— 反过来写样式表会把颜色覆盖回默认值。\r\n"
                + "   本示例统一走 ElementPlusPalettes.Apply，它固定按这个顺序赋值。\r\n"
                + "3. 顶边 1 像素线不用改：它是半透明基色，与标题栏底色混合，换任何配色都自动跟随。\r\n"
                + "4. 配色取自 Element Plus 的官方变量：浅色用 common/var.scss，深色用 dark/var.scss。\r\n"
                + "   注意深色主题的 dark-2 是「向白混」，所以 VS Code 那款的关闭按钮按下会比悬停更亮。",
        };

        _swatches = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 92,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(24, 4, 0, 0),
        };

        _status = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 6, 24, 0),
            ForeColor = Color.FromArgb(0x60, 0x62, 0x66),
            Text = string.Empty,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
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
                Text = style switch
                {
                    ChromeTitleBarStyle.VsCode => "VS Code 样式 × Element Plus 深色",
                    ChromeTitleBarStyle.Windows => "Windows 样式 × Element Plus 中性",
                    _ => "Chrome 样式 × Element Plus 主色",
                },
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 12, 0),
            };
            var captured = style;
            button.Click += (_, _) => UsePalette(captured);
            buttons.Controls.Add(button);
        }

        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        // Dock 顺序：后加的在上层，所以按“从下往上”添加
        Controls.Add(spacer);
        Controls.Add(_status);
        Controls.Add(_swatches);
        Controls.Add(buttons);
        Controls.Add(hint);
        Controls.Add(header);

        UsePalette(ChromeTitleBarStyle.Chrome);
    }

    private void UsePalette(ChromeTitleBarStyle style)
    {
        var palette = ElementPlusPalettes.Apply(this, style);

        Text = $"Element Plus 配色 × {style} 样式";
        _status.Text = $"当前：{style} 几何 + Element Plus 配色。{palette.Caption}";

        _swatches.Controls.Clear();
        AddSwatch("激活底", palette.ActiveCaption, Contrast(palette.ActiveCaption));
        AddSwatch("失活底", palette.InactiveCaption, Contrast(palette.InactiveCaption));
        AddSwatch("悬停填充", palette.ButtonHover, Contrast(palette.ButtonHover));
        AddSwatch("按下填充", palette.ButtonPressed, Contrast(palette.ButtonPressed));
        AddSwatch("关闭悬停", palette.CloseButtonHover, Contrast(palette.CloseButtonHover));
        AddSwatch("关闭按下", palette.CloseButtonPressed, Contrast(palette.CloseButtonPressed));
    }

    /// <summary>按背景亮度选黑或白前景，保证色块上的文字可读。</summary>
    private static Color Contrast(Color background) =>
        (background.R * 299 + background.G * 587 + background.B * 114) / 1000 > 150
            ? Color.FromArgb(0x30, 0x31, 0x33)
            : Color.White;

    private void AddSwatch(string label, Color background, Color foreground)
    {
        var text = $"{label} #{background.R:X2}{background.G:X2}{background.B:X2}";
        _swatches.Controls.Add(new Label
        {
            Text = text,
            AutoSize = false,
            Width = 196,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = background,
            ForeColor = foreground,
            Margin = new Padding(0, 0, 10, 6),
        });
    }
}
