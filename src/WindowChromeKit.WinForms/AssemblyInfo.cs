using System.Runtime.CompilerServices;

// 测试要断言标题栏字形的字体与码位（CaptionGlyphFontFamilyName / MinimizeGlyph 等），
// 这些是 internal 常量，与 WPF 项目用同样的方式开放给测试程序集。
[assembly: InternalsVisibleTo("WindowChromeKit.WinForms.Tests")]
