using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 演示标题栏的两个轴：<see cref="ChromeTitleBarStyle"/>（几何）与
/// <see cref="ChromeTitleBarPalette"/>（配色）。Chrome / VS Code / Windows 三套几何
/// 各配一款 Element Plus 配色，可以在这里逐一切换对比。
///
/// 两者正交，**谁后赋值都成立**；之后单独改颜色属性同样以属性为准。
/// 色板本身在库里（<c>ElementPlusTheme</c>），这里只负责切换与显示 ——
/// 所以本示例的色块读的是窗口上**实际生效**的颜色，没有再抄一份表。
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
                "1. 标题栏是两个正交的轴：TitleBarStyle 管几何（标题栏高、按钮尺寸、图标位置），\r\n"
                + "   TitleBarPalette 管配色。下面三个按钮同时切换两者（几何 + Element Plus 配色）。\r\n"
                + "2. 两者谁后赋值都成立，不会互相覆盖；之后单独改颜色属性也以属性为准。\r\n"
                + "3. 色板来自库里（ElementPlusTheme），数值取自 Element Plus 的官方变量：\r\n"
                + "   浅色用 common/var.scss，深色用 dark/var.scss。\r\n"
                + "4. 下面的色块读的是窗口上实际生效的颜色，不是另抄的一份表。",
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
        // 两个轴分别赋值：样式管几何、配色管颜色。顺序任意，两者会自行组合。
        TitleBarStyle = style;
        TitleBarPalette = ChromeTitleBarPalette.ElementPlus;

        Text = $"Element Plus 配色 × {style} 样式";
        _status.Text = $"当前：{style} 几何 + Element Plus 配色。{Describe(style)}";

        // 色块直接读窗口上实际生效的颜色 —— 不再抄一份配色表，改了库这里自动跟着变
        _swatches.Controls.Clear();
        AddSwatch("激活底", ActiveCaptionColor, Contrast(ActiveCaptionColor));
        AddSwatch("失活底", InactiveCaptionColor, Contrast(InactiveCaptionColor));
        AddSwatch("悬停填充", CaptionButtonHoverColor, Contrast(CaptionButtonHoverColor));
        AddSwatch("按下填充", CaptionButtonPressedColor, Contrast(CaptionButtonPressedColor));
        AddSwatch("关闭悬停", CloseButtonHoverColor, Contrast(CloseButtonHoverColor));
        AddSwatch("关闭按下", CloseButtonPressedColor, Contrast(CloseButtonPressedColor));
    }

    /// <summary>各款配色的一句话说明（色值由库提供，这里只讲它为什么这么选）。</summary>
    private static string Describe(ChromeTitleBarStyle style) => style switch
    {
        ChromeTitleBarStyle.VsCode => "VS Code 的标题栏本来就深，配 Element Plus 的深色主题（按下比悬停更亮）",
        ChromeTitleBarStyle.Windows => "保持中性底以贴近原生，只在按钮悬停/按下处露出 primary 色阶",
        _ => "直接用品牌主色当标题栏底，最醒目的一款",
    };

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
