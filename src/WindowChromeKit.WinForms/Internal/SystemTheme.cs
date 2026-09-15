using System.Drawing;
using Microsoft.Win32;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>标题栏配色。浅色取 Chrome 的浅色配色，深色取 VS Code / Chrome 深色标题栏的实测值。</summary>
internal readonly struct ChromePalette
{
    internal ChromePalette(
        Color activeCaption,
        Color inactiveCaption,
        Color captionText,
        Color inactiveCaptionText,
        Color buttonHover,
        Color buttonPressed)
    {
        ActiveCaption = activeCaption;
        InactiveCaption = inactiveCaption;
        CaptionText = captionText;
        InactiveCaptionText = inactiveCaptionText;
        ButtonHover = buttonHover;
        ButtonPressed = buttonPressed;
    }

    internal Color ActiveCaption { get; }
    internal Color InactiveCaption { get; }
    internal Color CaptionText { get; }
    internal Color InactiveCaptionText { get; }
    internal Color ButtonHover { get; }
    internal Color ButtonPressed { get; }
}

/// <summary>系统明暗与两套标题栏配色。</summary>
internal static class SystemTheme
{
    private const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Chrome 浅色：白底 #FFFFFF、文字 #202124、失活 #F1F3F4 / #80868B。</summary>
    internal static readonly ChromePalette Light = new(
        Color.FromArgb(0xFF, 0xFF, 0xFF),
        Color.FromArgb(0xF1, 0xF3, 0xF4),
        Color.FromArgb(0x20, 0x21, 0x24),
        Color.FromArgb(0x80, 0x86, 0x8B),
        Color.FromArgb(0xE8, 0xEA, 0xED),
        Color.FromArgb(0xDA, 0xDC, 0xE0));

    /// <summary>
    /// Chrome 深色标题栏（系统"应用模式"为深色时 Chrome 样式使用）：
    /// #323233 底、#CCCCCC 文字、失活 #2D2D2D / #9D9D9D。
    /// </summary>
    internal static readonly ChromePalette Dark = new(
        Color.FromArgb(0x32, 0x32, 0x33),
        Color.FromArgb(0x2D, 0x2D, 0x2D),
        Color.FromArgb(0xCC, 0xCC, 0xCC),
        Color.FromArgb(0x9D, 0x9D, 0x9D),
        Color.FromArgb(0x50, 0x50, 0x50),
        Color.FromArgb(0x5F, 0x5F, 0x5F));

    /// <summary>
    /// VS Code 深色标题栏（<c>VsCode</c> 样式专用，与系统明暗无关）。
    /// 取自 VS Code 的主题定义 <c>2026-dark.json</c> 与工作台样式表：
    /// <c>titleBar.activeBackground = #191A1B</c>（激活与失活同色）、
    /// <c>titleBar.activeForeground = #8C8C8C</c>，
    /// 按钮悬停是 <c>#ffffff1a</c>（白色 10% 叠加）落在 #191A1B 上的结果。
    /// 独立成一套而不是复用 <see cref="Dark"/>：后者在系统深色模式下也被
    /// Chrome / Windows 样式使用，改它会连带改变那两套样式。
    /// </summary>
    internal static readonly ChromePalette VsCode = new(
        Color.FromArgb(0x19, 0x1A, 0x1B),
        Color.FromArgb(0x19, 0x1A, 0x1B),
        Color.FromArgb(0x8C, 0x8C, 0x8C),
        Color.FromArgb(0x8C, 0x8C, 0x8C),
        Color.FromArgb(0x2E, 0x2F, 0x30),
        Color.FromArgb(0x3A, 0x3B, 0x3C));

    /// <summary>当前系统明暗对应的配色（读取"应用模式"设置，读不到时按浅色处理）。</summary>
    internal static ChromePalette Current => IsLightMode() ? Light : Dark;

    /// <summary>系统当前是否使用浅色"应用模式"。</summary>
    internal static bool IsLightMode()
    {
        try
        {
            var value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
            return value is not int flag || flag != 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
            or UnauthorizedAccessException
            or System.IO.IOException)
        {
            return true;
        }
    }
}
