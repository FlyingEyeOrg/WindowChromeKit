using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

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
public partial class ElementPlusTitleBarWindow
{
    private ChromeTitleBarStyle _style = ChromeTitleBarStyle.Chrome;
    private ChromeTitleBarPalette _palette = ChromeTitleBarPalette.ElementPlusPrimary;

    public ElementPlusTitleBarWindow()
    {
        InitializeComponent();
        Apply();

        StyleChromeButton.Click += (_, _) => SelectStyle(ChromeTitleBarStyle.Chrome);
        StyleVsCodeButton.Click += (_, _) => SelectStyle(ChromeTitleBarStyle.VsCode);
        StyleWindowsButton.Click += (_, _) => SelectStyle(ChromeTitleBarStyle.Windows);
        PalettePrimaryButton.Click += (_, _) => SelectPalette(ChromeTitleBarPalette.ElementPlusPrimary);
        PaletteDarkButton.Click += (_, _) => SelectPalette(ChromeTitleBarPalette.ElementPlusDark);
        PaletteNeutralButton.Click += (_, _) => SelectPalette(ChromeTitleBarPalette.ElementPlusNeutral);
    }

    private void SelectStyle(ChromeTitleBarStyle style)
    {
        _style = style;
        Apply();
    }

    private void SelectPalette(ChromeTitleBarPalette palette)
    {
        _palette = palette;
        Apply();
    }

    /// <summary>把两个轴分别套上去，并刷新标题、说明与色块。</summary>
    private void Apply()
    {
        // 两个轴各管一半：样式管几何（Default 配色时也管颜色），配色管颜色。顺序任意。
        TitleBarStyle = _style;
        TitleBarPalette = _palette;

        Title = $"{_style} 骨架 × {DescribePalette(_palette)}";
        StatusText.Text = $"当前：{_style} 骨架 + {DescribePalette(_palette)}。{DescribeStyle(_style)}";
        HighlightSelection();
        RefreshSwatches();
    }

    /// <summary>用粗体标出当前选中的按钮（不引入额外配色以免干扰观察）。</summary>
    private void HighlightSelection()
    {
        SetSelected(StyleChromeButton, _style == ChromeTitleBarStyle.Chrome);
        SetSelected(StyleVsCodeButton, _style == ChromeTitleBarStyle.VsCode);
        SetSelected(StyleWindowsButton, _style == ChromeTitleBarStyle.Windows);
        SetSelected(PalettePrimaryButton, _palette == ChromeTitleBarPalette.ElementPlusPrimary);
        SetSelected(PaletteDarkButton, _palette == ChromeTitleBarPalette.ElementPlusDark);
        SetSelected(PaletteNeutralButton, _palette == ChromeTitleBarPalette.ElementPlusNeutral);
    }

    private static void SetSelected(Button button, bool selected) =>
        button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;

    /// <summary>色块直接读窗口上实际生效的颜色 —— 不抄配色表，改了库这里自动跟着变。</summary>
    private void RefreshSwatches()
    {
        Swatches.Children.Clear();
        AddSwatch("激活底", ActiveTitleBarBackground);
        AddSwatch("失活底", InactiveTitleBarBackground);
        AddSwatch("悬停填充", CaptionButtonHoverBackground);
        AddSwatch("按下填充", CaptionButtonPressedBackground);
        AddSwatch("关闭悬停", CloseButtonHoverBackground);
        AddSwatch("关闭按下", CloseButtonPressedBackground);
    }

    private static string DescribePalette(ChromeTitleBarPalette palette) => palette switch
    {
        ChromeTitleBarPalette.ElementPlusPrimary => "Element Plus 主色",
        ChromeTitleBarPalette.ElementPlusDark => "Element Plus 深色",
        ChromeTitleBarPalette.ElementPlusNeutral => "Element Plus 中性",
        _ => "样式自带配色",
    };

    /// <summary>各骨架的一句话说明（色值由库提供，这里只讲骨架的区别）。</summary>
    private static string DescribeStyle(ChromeTitleBarStyle style) => style switch
    {
        ChromeTitleBarStyle.VsCode => "标题栏 35、按钮 46×34",
        ChromeTitleBarStyle.Windows => "标题栏 31、按钮 45×31、图标更贴左",
        _ => "标题栏 40、按钮 46×39",
    };

    private void AddSwatch(string label, Brush background)
    {
        var color = (background as SolidColorBrush)?.Color ?? Colors.Transparent;
        var text = $"{label} #{color.R:X2}{color.G:X2}{color.B:X2}";
        Swatches.Children.Add(new Border
        {
            Width = 196,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 6),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // 按背景亮度选前景色，保证色块上的文字可读
                Foreground = (color.R * 299 + color.G * 587 + color.B * 114) / 1000 > 150
                    ? new SolidColorBrush(Color.FromRgb(0x30, 0x31, 0x33))
                    : Brushes.White,
            },
        });
    }
}
