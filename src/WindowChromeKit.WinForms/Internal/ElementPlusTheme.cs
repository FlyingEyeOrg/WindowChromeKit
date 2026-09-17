using System.Drawing;

namespace WindowChromeKit.WinForms.Internal;

/// <summary>
/// Element Plus 配色：三套几何样式各一款。
///
/// 数值全部取自 Element Plus 自己的主题变量（<c>theme-chalk/src/common/var.scss</c> 与
/// <c>theme-chalk/src/dark/var.scss</c>），派生色按它的混色公式算出，而不是抓屏反推：
/// <code>light-N = mix(white, base, N×10%)      浅色主题</code>
/// <code>light-N = mix(#141414, base, N×10%)     深色主题（与暗底混合）</code>
/// <code>dark-2  = mix(black, base, 20%)          浅色主题</code>
/// <code>dark-2  = mix(white, base, 20%)          深色主题（向白混合）</code>
/// </summary>
internal static class ElementPlusTheme
{
    private static readonly Color Primary = Color.FromArgb(0x40, 0x9E, 0xFF);    // color-primary
    private static readonly Color Danger = Color.FromArgb(0xF5, 0x6C, 0x6C);     // color-danger
    private static readonly Color DarkBg = Color.FromArgb(0x14, 0x14, 0x14);     // dark bg-color ''
    private static readonly Color DarkOverlay = Color.FromArgb(0x1D, 0x1E, 0x1F); // dark bg-color overlay
    private static readonly Color FillBase = Color.FromArgb(0xFA, 0xFC, 0xFF);   // dark fill-color base
    private static readonly Color TextBase = Color.FromArgb(0xF0, 0xF5, 0xFF);   // dark text-color base
    private static readonly Color NeutralCaption = Color.FromArgb(0xF2, 0xF6, 0xFC); // fill-color extra-light
    private static readonly Color LightCaptionText = Color.FromArgb(0x30, 0x31, 0x33); // text-color primary
    private static readonly Color LightCaptionTextDim = Color.FromArgb(0x90, 0x93, 0x99); // text-color secondary
    private static readonly Color White = Color.White;
    private static readonly Color Black = Color.Black;

    /// <summary>
    /// 浅色底上的关闭按钮直接用 **Windows 原生那一对**（悬停 #C42B1C、按下 #A92316）。
    ///
    /// 不用 Element Plus 的 danger #F56C6C：它在浅底上太淡，这是可量化的 ——
    /// 与白底的对比度只有 2.90:1（原生是 5.66:1），与 primary #409EFF 更是只有 1.04:1，
    /// 因为两者的**亮度几乎相同**（0.312 vs 0.328），只剩色相差，看上去就是"糊"的。
    /// 原生这对在白底上正好就是 5.66:1，在 primary 底上也比按 danger 色阶推的任何一档更清楚
    /// （2.04:1 对 1.57:1）—— 而且它是实测值，比自己推的色阶更有依据。
    ///
    /// 深色底（VS Code 款）不用这一对：原生红在近黑底上偏暗（3.25:1），
    /// 那边保留 Element Plus 的亮红 danger（6.35:1）。
    /// </summary>
    private static readonly Color NativeCloseHover = Color.FromArgb(0xC4, 0x2B, 0x1C);
    private static readonly Color NativeClosePressed = Color.FromArgb(0xA9, 0x23, 0x16);

    internal static ChromeTitleBarLook Look(ChromeTitleBarStyle style) => style switch
    {
        // VS Code 的标题栏本来就是深色，所以配 Element Plus 的**深色主题**。
        // 注意深色主题的 dark-2 是向白混，所以关闭按钮按下比悬停更亮。
        ChromeTitleBarStyle.VsCode => new ChromeTitleBarLook(
            new ChromePalette(
                activeCaption: DarkBg,
                inactiveCaption: DarkOverlay,
                captionText: Mix(TextBase, DarkBg, 0.95),          // text-color primary
                inactiveCaptionText: Mix(TextBase, DarkBg, 0.65),  // text-color secondary
                buttonHover: Mix(FillBase, DarkBg, 0.12),          // fill-color ''
                buttonPressed: Mix(FillBase, DarkBg, 0.20)),       // fill-color darker
            closeButtonHover: Danger,
            closeButtonPressed: Mix(White, Danger, 0.20)),

        // Windows 样式本来就贴近原生浅色，所以配**浅色中性色 + primary 强调**：
        // 底色保持中性，只在按钮悬停/按下处露出 primary 色阶。
        ChromeTitleBarStyle.Windows => new ChromeTitleBarLook(
            new ChromePalette(
                activeCaption: White,
                inactiveCaption: NeutralCaption,
                captionText: LightCaptionText,
                inactiveCaptionText: LightCaptionTextDim,
                buttonHover: Mix(White, Primary, 0.90),            // primary light-9
                buttonPressed: Mix(White, Primary, 0.80)),         // primary light-8
            closeButtonHover: NativeCloseHover,
            closeButtonPressed: NativeClosePressed),

        // Chrome 样式直接吃 primary 当标题栏底，最"品牌"的一款。
        _ => new ChromeTitleBarLook(
            new ChromePalette(
                activeCaption: Primary,
                inactiveCaption: Mix(White, Primary, 0.30),        // primary light-3
                captionText: White,
                inactiveCaptionText: Mix(White, Primary, 0.90),    // primary light-9
                buttonHover: Mix(White, Primary, 0.30),            // primary light-3
                buttonPressed: Mix(Black, Primary, 0.20)),         // primary dark-2
            closeButtonHover: NativeCloseHover,
            closeButtonPressed: NativeClosePressed),
    };

    /// <summary>按 Element Plus 的混色公式：<paramref name="pct"/> 是 <paramref name="fg"/> 的占比。</summary>
    private static Color Mix(Color fg, Color bg, double pct) => Color.FromArgb(
        (int)Math.Round(fg.R * pct + bg.R * (1 - pct)),
        (int)Math.Round(fg.G * pct + bg.G * (1 - pct)),
        (int)Math.Round(fg.B * pct + bg.B * (1 - pct)));
}
