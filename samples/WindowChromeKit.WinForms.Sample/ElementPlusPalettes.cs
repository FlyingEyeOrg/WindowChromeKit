using System.Drawing;
using WindowChromeKit.WinForms;

namespace WindowChromeKit.WinForms.Sample;

/// <summary>
/// Element Plus 的三款标题栏配色：Chrome / VsCode / Windows **每套几何样式各配一款**，
/// 让同一套预置样式既能保持它的几何特征，又能换成 Element Plus 的品牌观感。
///
/// 全部数值取自 Element Plus 自己的主题变量（<c>theme-chalk/src/common/var.scss</c> 与
/// <c>theme-chalk/src/dark/var.scss</c>），派生色按它的混色公式算出，而不是抓屏反推：
/// <code>light-N = mix(white, base, N×10%)      （浅色主题）</code>
/// <code>light-N = mix(#141414, base, N×10%)      （深色主题，与暗底混合）</code>
/// <code>dark-2  = mix(black, base, 20%)          （浅色主题）</code>
/// <code>dark-2  = mix(white, base, 20%)          （深色主题，向白混合）</code>
/// </summary>
internal static class ElementPlusPalettes
{
    // ---- Element Plus 基础变量 ----
    private static readonly Color Primary = Color.FromArgb(0x40, 0x9E, 0xFF);   // color-primary
    private static readonly Color Danger = Color.FromArgb(0xF5, 0x6C, 0x6C);    // color-danger
    private static readonly Color DarkBg = Color.FromArgb(0x14, 0x14, 0x14);    // dark bg-color ''
    private static readonly Color DarkOverlay = Color.FromArgb(0x1D, 0x1E, 0x1F); // dark bg-color overlay
    private static readonly Color FillBase = Color.FromArgb(0xFA, 0xFC, 0xFF);  // dark fill-color base
    private static readonly Color TextBase = Color.FromArgb(0xF0, 0xF5, 0xFF);  // dark text-color base
    private static readonly Color White = Color.White;
    private static readonly Color Black = Color.Black;

    // 关闭按钮在**浅色底**上要用更深的红，否则"太淡"。
    // 这不是审美问题，是可量化的：danger #F56C6C 与白底的对比度只有 2.90:1，
    // 而原生 Windows 关闭按钮是 5.66:1；与 primary #409EFF 的对比度更是只有 1.04:1
    // —— 因为两者的**亮度几乎相同**（0.312 vs 0.328），只剩色相差，看上去就是"糊"。
    // 所以按 Element Plus 自己的混色公式往深走：dark-2 官方有，dark-3 按同一公式外推。
    //   深色底（VsCode 款）相反：底色已经接近黑，越深越看不见，那边保持亮红。
    private static readonly Color DangerDeep = Mix(Black, Danger, 0.20);   // danger-dark-2 #C45656
    private static readonly Color DangerDeeper = Mix(Black, Danger, 0.30); // 外推一档 #AC4C4C

    /// <summary>
    /// 一款配色：标题栏底色/文字、按钮填充、关闭按钮，共 8 个值。
    /// 用普通类而非 record —— 本示例也编译 net48（VM 验证用），那里没有 record 需要的
    /// <c>IsExternalInit</c>。与 WPF 样例保持同样的写法。
    /// </summary>
    internal sealed class Palette
    {
        internal Palette(
            Color activeCaption,
            Color inactiveCaption,
            Color captionText,
            Color inactiveCaptionText,
            Color buttonHover,
            Color buttonPressed,
            Color closeButtonHover,
            Color closeButtonPressed,
            string caption)
        {
            ActiveCaption = activeCaption;
            InactiveCaption = inactiveCaption;
            CaptionText = captionText;
            InactiveCaptionText = inactiveCaptionText;
            ButtonHover = buttonHover;
            ButtonPressed = buttonPressed;
            CloseButtonHover = closeButtonHover;
            CloseButtonPressed = closeButtonPressed;
            Caption = caption;
        }

        internal Color ActiveCaption { get; }
        internal Color InactiveCaption { get; }
        internal Color CaptionText { get; }
        internal Color InactiveCaptionText { get; }
        internal Color ButtonHover { get; }
        internal Color ButtonPressed { get; }
        internal Color CloseButtonHover { get; }
        internal Color CloseButtonPressed { get; }
        internal string Caption { get; }
    }

    /// <summary>取某套几何样式对应的 Element Plus 配色。</summary>
    internal static Palette For(ChromeTitleBarStyle style) => style switch
    {
        // VS Code 的标题栏是深色的，所以配 Element Plus 的**深色**主题：
        // 底色用 dark bg、文字用 dark text-color、按钮填充用 dark fill-color。
        // 注意深色主题的 dark-2 是**向白混**，所以关闭按钮按下比悬停更**亮**。
        ChromeTitleBarStyle.VsCode => new Palette(
            activeCaption: DarkBg,
            inactiveCaption: DarkOverlay,
            captionText: Mix(TextBase, DarkBg, 0.95),          // text-color primary
            inactiveCaptionText: Mix(TextBase, DarkBg, 0.65),  // text-color secondary
            buttonHover: Mix(FillBase, DarkBg, 0.12),          // fill-color ''
            buttonPressed: Mix(FillBase, DarkBg, 0.20),        // fill-color darker
            closeButtonHover: Danger,
            closeButtonPressed: Mix(White, Danger, 0.20),      // 深色主题 dark-2 = 混白
            caption: "深色主题：底色 #141414、文字 #E5EAF3、关闭按钮按下变亮"),

        // Windows 样式本来就贴近原生浅色，配 Element Plus 的**浅色中性色 + primary 强调**：
        // 底色用中性色保持原生观感，只在按钮悬停/按下处露出 primary 色阶。
        ChromeTitleBarStyle.Windows => new Palette(
            activeCaption: White,
            inactiveCaption: Color.FromArgb(0xF2, 0xF6, 0xFC),  // fill-color extra-light
            captionText: Color.FromArgb(0x30, 0x31, 0x33),      // text-color primary
            inactiveCaptionText: Color.FromArgb(0x90, 0x93, 0x99), // text-color secondary
            buttonHover: Mix(White, Primary, 0.90),            // primary light-9
            buttonPressed: Mix(White, Primary, 0.80),          // primary light-8
            closeButtonHover: DangerDeep,                      // 白底上要深红，见上面 DangerDeep 的说明
            closeButtonPressed: DangerDeeper,
            caption: "浅色中性底 + primary 强调：底色 #FFFFFF、关闭按钮悬停 #C45656"),

        // Chrome 样式直接吃 primary 当标题栏底，最"品牌"的一款。
        _ => new Palette(
            activeCaption: Primary,
            inactiveCaption: Mix(White, Primary, 0.30),        // primary light-3
            captionText: White,
            inactiveCaptionText: Mix(White, Primary, 0.90),    // primary light-9
            buttonHover: Mix(White, Primary, 0.30),            // primary light-3
            buttonPressed: Mix(Black, Primary, 0.20),          // primary dark-2
            closeButtonHover: DangerDeep,                      // primary 底与 danger 亮度太近，须用深红
            closeButtonPressed: DangerDeeper,
            caption: "品牌主色底 #409EFF、失活 #79BBFF、关闭按钮悬停 #C45656"),
    };

    /// <summary>
    /// 把配色套到窗口上。**必须在 <see cref="ChromeForm.TitleBarStyle"/> 之后调用** ——
    /// 赋值样式时会一次性套用整张样式表（几何 + 配色），先改颜色会被它覆盖回去。
    /// </summary>
    internal static Palette Apply(ChromeForm form, ChromeTitleBarStyle style)
    {
        // 1) 先套几何（这一步也会把配色设成该样式的默认值）
        form.TitleBarStyle = style;

        // 2) 再覆盖配色，此时以属性为准
        var palette = For(style);
        form.ActiveCaptionColor = palette.ActiveCaption;
        form.InactiveCaptionColor = palette.InactiveCaption;
        form.CaptionTextColor = palette.CaptionText;
        form.InactiveCaptionTextColor = palette.InactiveCaptionText;
        form.CaptionButtonHoverColor = palette.ButtonHover;
        form.CaptionButtonPressedColor = palette.ButtonPressed;
        form.CloseButtonHoverColor = palette.CloseButtonHover;
        form.CloseButtonPressedColor = palette.CloseButtonPressed;

        // 顶边 1 像素线不用管：它是半透明基色，与标题栏底色混合，换任何配色都自动跟随。
        return palette;
    }

    /// <summary>按 Element Plus 的混色公式：<c>pct</c> 是 <paramref name="fg"/> 的占比。</summary>
    private static Color Mix(Color fg, Color bg, double pct) => Color.FromArgb(
        (int)Math.Round(fg.R * pct + bg.R * (1 - pct)),
        (int)Math.Round(fg.G * pct + bg.G * (1 - pct)),
        (int)Math.Round(fg.B * pct + bg.B * (1 - pct)));
}
