using System.Drawing;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>
/// 一套完整的标题栏配色：调色板的六个颜色 + 关闭按钮的两个颜色。
///
/// 关闭色也装在这里，而不是按样式临时分支算 —— 它随**配色来源**而变：
/// Chrome 的红按下变亮（Chrome 自身行为）、Windows 的按下变暗（原生实测）、
/// Element Plus 三款的红又各不相同。放在一处才能保证"一套配色是完整的"。
/// </summary>
internal readonly struct ChromeTitleBarLook
{
    internal ChromeTitleBarLook(
        ChromePalette palette,
        Color closeButtonHover,
        Color closeButtonPressed)
    {
        Palette = palette;
        CloseButtonHover = closeButtonHover;
        CloseButtonPressed = closeButtonPressed;
    }

    /// <summary>标题栏底色/文字 + 普通按钮的悬停/按下填充。</summary>
    internal ChromePalette Palette { get; }

    /// <summary>关闭按钮悬停填充。</summary>
    internal Color CloseButtonHover { get; }

    /// <summary>关闭按钮按下填充。</summary>
    internal Color CloseButtonPressed { get; }
}
