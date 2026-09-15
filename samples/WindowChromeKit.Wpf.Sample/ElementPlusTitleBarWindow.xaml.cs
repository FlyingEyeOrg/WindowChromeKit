using System.Windows.Media;
using WindowChromeKit.Wpf;

namespace WindowChromeKit.Wpf.Sample;

/// <summary>
/// 基于预置样式再改配色的示例（WPF 版）：先套用 <see cref="ChromeTitleBarStyle.Chrome"/> 的几何，
/// 再把标题栏配色换成 Element Plus 的主色。
///
/// **顺序很重要**：<c>TitleBarStyle</c> 赋值时会一次性套用整张样式表（几何 + 配色），
/// 所以必须**先选样式、后改颜色**；反过来改的颜色会被样式覆盖回去。
///
/// 顶边线不用管：它存的是半透明基色，绘制时与标题栏底色混合，
/// 换成任何配色都会自动跟随（本窗口激活时混出约 #2F4F70）。
/// </summary>
public partial class ElementPlusTitleBarWindow
{
    // Element Plus 官方变量（取自它的 theme-chalk/common/var.scss）：
    //   primary       #409EFF
    //   primary light-3 = primary 混 30% 白 = #79BBFF（失活底色）
    //   primary light-9 = primary 混 90% 白 = #ECF5FF（失活文字）
    //   primary dark-2  = primary 混 20% 黑 = #337ECC（按钮 hover）
    //   danger        #F56C6C / danger dark-2 #C45656（关闭按钮）
    private static readonly Brush Primary = Frozen(0x40, 0x9E, 0xFF);
    private static readonly Brush PrimaryLight3 = Frozen(0x79, 0xBB, 0xFF);
    private static readonly Brush PrimaryLight9 = Frozen(0xEC, 0xF5, 0xFF);
    private static readonly Brush PrimaryDark2 = Frozen(0x33, 0x7E, 0xCC);
    private static readonly Brush Danger = Frozen(0xF5, 0x6C, 0x6C);
    private static readonly Brush DangerDark2 = Frozen(0xC4, 0x56, 0x56);

    public ElementPlusTitleBarWindow()
    {
        InitializeComponent();

        // 1) 先套样式：拿到 Chrome 的几何（标题栏 40、按钮 46×39、图标 12px 位）。
        //    这一步会把配色设成 Chrome 的默认值 —— 所以颜色必须在它之后改。
        TitleBarStyle = ChromeTitleBarStyle.Chrome;

        // 2) 再改配色：以属性为准，样式不会再覆盖回来。
        ActiveTitleBarBackground = Primary;              // 品牌主色
        InactiveTitleBarBackground = PrimaryLight3;      // 同色系变淡
        ActiveTitleBarForeground = Brushes.White;        // 主色上必须用白字
        InactiveTitleBarForeground = PrimaryLight9;      // 近白，失活时仍可读
        CaptionButtonHoverBackground = PrimaryDark2;     // 比底色深一档
        CaptionButtonPressedBackground = PrimaryDark2;   // Element Plus 用同一个 active 色
        CloseButtonHoverBackground = Danger;
        CloseButtonPressedBackground = DangerDark2;

        // 顶线保持默认即可 —— 它是半透明的，会自动与上面的底色混合。
    }

    private static Brush Frozen(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
