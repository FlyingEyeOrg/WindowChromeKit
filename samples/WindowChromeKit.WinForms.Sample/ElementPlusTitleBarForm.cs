using System.Drawing;
using System.Windows.Forms;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// 演示标题栏的两个轴：<see cref="ChromeTitleBarStyle"/>（骨架 + 它自带的默认配色）与
/// <see cref="ChromeTitleBarPalette"/>（另外多套可选配色）。
///
/// 界面上是**两行独立的按钮**：上行切换骨架、下行切换配色，两者自由组合互不干扰 ——
/// 3 种骨架 × 3 套 Element Plus 配色 = 9 种组合，任何组合都能直接选。
///
/// 色板本身在库里（<c>ElementPlusTheme</c>），这里只负责切换与显示 ——
/// 下面的色块读的是窗口上**实际生效**的颜色，没有再抄一份表。
/// </summary>
public sealed class ElementPlusTitleBarForm : ChromeForm
{
    private readonly Label _status;
    private readonly FlowLayoutPanel _swatches;
    private readonly Button[] _styleButtons;
    private readonly Button[] _paletteButtons;
    private ChromeTitleBarStyle _style = ChromeTitleBarStyle.Chrome;
    private ChromeTitleBarPalette _palette = ChromeTitleBarPalette.ElementPlusPrimary;

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
            Height = 132,
            Padding = new Padding(24, 8, 24, 0),
            ForeColor = Color.FromArgb(0x60, 0x62, 0x66),
            Text =
                "1. 上行按钮换**骨架**（TitleBarStyle：标题栏高、按钮尺寸、图标位置），\r\n"
                + "   下行按钮换**配色**（TitleBarPalette）。两行互不干扰，3×3 共 9 种组合。\r\n"
                + "2. 两者谁后赋值都成立，不会互相覆盖；之后单独改颜色属性也以属性为准。\r\n"
                + "3. 三套配色取自 Element Plus 的官方变量（common/var.scss 与 dark/var.scss）：\r\n"
                + "   主色 = primary 当底；深色 = 它的深色主题；中性 = 浅色底只在按钮上出主色。\r\n"
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

        _styleButtons = new Button[3];
        var styleRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(24, 6, 0, 0),
        };
        var styleIndex = 0;
        foreach (var style in new[]
                 {
                     ChromeTitleBarStyle.Chrome,
                     ChromeTitleBarStyle.VsCode,
                     ChromeTitleBarStyle.Windows,
                 })
        {
            var button = new Button
            {
                Text = $"骨架：{style}",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 8, 0),
            };
            var captured = style;
            button.Click += (_, _) =>
            {
                _style = captured;
                Apply();
            };
            _styleButtons[styleIndex++] = button;
            styleRow.Controls.Add(button);
        }

        _paletteButtons = new Button[3];
        var paletteRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(24, 6, 0, 0),
        };
        var paletteIndex = 0;
        foreach (var (palette, label) in new[]
                 {
                     (ChromeTitleBarPalette.ElementPlusPrimary, "配色：主色"),
                     (ChromeTitleBarPalette.ElementPlusDark, "配色：深色"),
                     (ChromeTitleBarPalette.ElementPlusNeutral, "配色：中性"),
                 })
        {
            var button = new Button
            {
                Text = label,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 8, 0),
            };
            var captured = palette;
            button.Click += (_, _) =>
            {
                _palette = captured;
                Apply();
            };
            _paletteButtons[paletteIndex++] = button;
            paletteRow.Controls.Add(button);
        }

        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        // Dock 顺序：后加的在上层，所以按“从下往上”添加
        Controls.Add(spacer);
        Controls.Add(_status);
        Controls.Add(_swatches);
        Controls.Add(paletteRow);
        Controls.Add(styleRow);
        Controls.Add(hint);
        Controls.Add(header);

        Apply();
    }

    /// <summary>把两个轴分别套上去，并刷新按钮状态、标题与色块。</summary>
    private void Apply()
    {
        // 两个轴各管一半：样式管几何（以及 Default 配色时的颜色），配色管颜色。
        // 顺序任意，两者会自行组合。
        TitleBarStyle = _style;
        TitleBarPalette = _palette;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        Text = $"{_style} 骨架 × {DescribePalette(_palette)}";
        _status.Text = $"当前：{_style} 几何 + {DescribePalette(_palette)}。{Describe(_style)}";
        HighlightSelection();
        RefreshSwatches();
    }

    /// <summary>把当前选中的骨架/配色按钮标出来（用粗体，不引入额外配色以免干扰观察）。</summary>
    private void HighlightSelection()
    {
        ChromeTitleBarStyle[] styles =
        {
            ChromeTitleBarStyle.Chrome,
            ChromeTitleBarStyle.VsCode,
            ChromeTitleBarStyle.Windows,
        };
        for (var i = 0; i < _styleButtons.Length; i++)
            SetSelected(_styleButtons[i], styles[i] == _style);

        ChromeTitleBarPalette[] palettes =
        {
            ChromeTitleBarPalette.ElementPlusPrimary,
            ChromeTitleBarPalette.ElementPlusDark,
            ChromeTitleBarPalette.ElementPlusNeutral,
        };
        for (var i = 0; i < _paletteButtons.Length; i++)
            SetSelected(_paletteButtons[i], palettes[i] == _palette);
    }

    private static void SetSelected(Button button, bool selected)
    {
        var style = selected ? FontStyle.Bold : FontStyle.Regular;
        if (button.Font.Style != style)
            button.Font = new Font(button.Font, style);
    }

    /// <summary>色块直接读窗口上实际生效的颜色 —— 不抄配色表，改了库这里自动跟着变。</summary>
    private void RefreshSwatches()
    {
        _swatches.Controls.Clear();
        AddSwatch("激活底", ActiveCaptionColor, Contrast(ActiveCaptionColor));
        AddSwatch("失活底", InactiveCaptionColor, Contrast(InactiveCaptionColor));
        AddSwatch("悬停填充", CaptionButtonHoverColor, Contrast(CaptionButtonHoverColor));
        AddSwatch("按下填充", CaptionButtonPressedColor, Contrast(CaptionButtonPressedColor));
        AddSwatch("关闭悬停", CloseButtonHoverColor, Contrast(CloseButtonHoverColor));
        AddSwatch("关闭按下", CloseButtonPressedColor, Contrast(CloseButtonPressedColor));
    }

    private static string DescribePalette(ChromeTitleBarPalette palette) => palette switch
    {
        ChromeTitleBarPalette.ElementPlusPrimary => "Element Plus 主色",
        ChromeTitleBarPalette.ElementPlusDark => "Element Plus 深色",
        ChromeTitleBarPalette.ElementPlusNeutral => "Element Plus 中性",
        _ => "样式自带配色",
    };

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
