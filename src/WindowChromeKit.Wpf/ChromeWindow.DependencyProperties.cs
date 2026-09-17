using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    public static readonly DependencyProperty ActiveTitleBarBackgroundProperty =
        DependencyProperty.Register(
            nameof(ActiveTitleBarBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                FrozenBrush(0xFF, 0x18, 0x18, 0x18),
                OnChromeVisualChanged
            )
        );

    public static readonly DependencyProperty ActiveTitleBarForegroundProperty =
        DependencyProperty.Register(
            nameof(ActiveTitleBarForeground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                FrozenBrush(0xFF, 0xCC, 0xCC, 0xCC),
                OnChromeVisualChanged
            )
        );

    public static readonly DependencyProperty InactiveTitleBarBackgroundProperty =
        DependencyProperty.Register(
            nameof(InactiveTitleBarBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                FrozenBrush(0xFF, 0x1F, 0x1F, 0x1F),
                OnChromeVisualChanged
            )
        );

    public static readonly DependencyProperty InactiveTitleBarForegroundProperty =
        DependencyProperty.Register(
            nameof(InactiveTitleBarForeground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                FrozenBrush(0xFF, 0x9D, 0x9D, 0x9D),
                OnChromeVisualChanged
            )
        );

    /// <summary>
    /// 有焦点时标题栏顶边那 1 像素线的颜色。默认是半透明基色（#262626 @ 66%），
    /// 绘制时与【当前标题栏底色】混合，因此自定义标题栏配色不需要改动它；
    /// 数值由原生 DWM 边框实测反解（黑底 25 / 白底 112）。套用预置样式时会被样式表覆盖。
    /// </summary>
    public static readonly DependencyProperty TitleBarBorderBrushProperty =
        DependencyProperty.Register(
            nameof(TitleBarBorderBrush),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                FrozenBrush(0xA8, 0x26, 0x26, 0x26),
                OnChromeVisualChanged
            )
        );

    /// <summary>
    /// 失活（无焦点）时标题栏顶边那 1 像素线的颜色。Windows 10 的 DWM 只画左/右/下三边，
    /// 顶边要自己补。默认是半透明基色（#565656 @ 50%，比聚焦更浅更柔），
    /// 绘制时与标题栏底色混合；数值由原生实测反解（黑底 43 / 白底 170）。
    /// </summary>
    public static readonly DependencyProperty InactiveTitleBarBorderBrushProperty =
        DependencyProperty.Register(
            nameof(InactiveTitleBarBorderBrush),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0x80, 0x56, 0x56, 0x56), OnChromeVisualChanged)
        );

    public static readonly DependencyProperty TitleBarHeightProperty = DependencyProperty.Register(
        nameof(TitleBarHeight),
        typeof(double),
        typeof(ChromeWindow),
        new FrameworkPropertyMetadata(40d, OnChromeMetricChanged),
        IsPositiveFiniteDouble
    );

    public static readonly DependencyProperty TitleBarStyleProperty =
        DependencyProperty.Register(
            nameof(TitleBarStyle),
            typeof(ChromeTitleBarStyle),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeTitleBarStyle.Chrome, OnTitleBarStyleChanged),
            IsDefinedTitleBarStyle
        );

    public static readonly DependencyProperty TitleBarPaletteProperty =
        DependencyProperty.Register(
            nameof(TitleBarPalette),
            typeof(ChromeTitleBarPalette),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeTitleBarPalette.Default, OnTitleBarPaletteChanged),
            IsDefinedTitleBarPalette
        );

    public static readonly DependencyProperty CaptionIconBoxMarginProperty =
        DependencyProperty.Register(
            nameof(CaptionIconBoxMargin),
            typeof(Thickness),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(new Thickness(9d, 0d, 0d, 0d))
        );

    public static readonly DependencyProperty CaptionButtonHeightProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonHeight),
            typeof(double),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(39d, OnChromeMetricChanged),
            IsPositiveFiniteDouble
        );

    /// <summary>
    /// 最小化按钮的独立宽度（DIP）。0 表示与其余按钮同宽；Chrome 样式按实测设为 45
    /// （最大化/关闭是 46，Chrome 自己就不等宽）。
    /// </summary>
    public static readonly DependencyProperty MinimizeButtonWidthProperty =
        DependencyProperty.Register(
            nameof(MinimizeButtonWidth),
            typeof(double),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(0d, OnChromeMetricChanged),
            IsNonNegativeFiniteDouble
        );

    public static readonly DependencyProperty CaptionButtonWidthProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonWidth),
            typeof(double),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(46d, OnChromeMetricChanged),
            IsPositiveFiniteDouble
        );

    public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register(
        nameof(TitleBarContent),
        typeof(object),
        typeof(ChromeWindow)
    );

    public static readonly DependencyProperty TitleBarContentTemplateProperty =
        DependencyProperty.Register(
            nameof(TitleBarContentTemplate),
            typeof(DataTemplate),
            typeof(ChromeWindow)
        );

    public static readonly DependencyProperty TitleBarContentTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(TitleBarContentTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(ChromeWindow)
        );

    public static readonly DependencyProperty TitleBarActionsProperty = DependencyProperty.Register(
        nameof(TitleBarActions),
        typeof(object),
        typeof(ChromeWindow)
    );

    public static readonly DependencyProperty TitleBarActionsTemplateProperty =
        DependencyProperty.Register(
            nameof(TitleBarActionsTemplate),
            typeof(DataTemplate),
            typeof(ChromeWindow)
        );

    public static readonly DependencyProperty TitleBarActionsTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(TitleBarActionsTemplateSelector),
            typeof(DataTemplateSelector),
            typeof(ChromeWindow)
        );

    public static readonly DependencyProperty CaptionButtonHoverBackgroundProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonHoverBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0x1A, 0xFF, 0xFF, 0xFF))
        );

    public static readonly DependencyProperty CaptionButtonPressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonPressedBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0x33, 0xFF, 0xFF, 0xFF))
        );

    public static readonly DependencyProperty CloseButtonHoverBackgroundProperty =
        DependencyProperty.Register(
            nameof(CloseButtonHoverBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0xC4, 0x2B, 0x1C))
        );

    public static readonly DependencyProperty CloseButtonPressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(CloseButtonPressedBackground),
            typeof(Brush),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0xA9, 0x23, 0x16))
        );

    public static readonly DependencyProperty CaptionButtonDisabledOpacityProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonDisabledOpacity),
            typeof(double),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(0.4d),
            IsUnitDouble
        );

    public static readonly DependencyProperty ShowTitleBarIconProperty =
        DependencyProperty.Register(
            nameof(ShowTitleBarIcon),
            typeof(bool),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(true)
        );

    private static readonly DependencyPropertyKey EffectiveTitleBarIconPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(EffectiveTitleBarIcon),
            typeof(ImageSource),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(null)
        );

    public static readonly DependencyProperty EffectiveTitleBarIconProperty =
        EffectiveTitleBarIconPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HitTestRoleProperty =
        DependencyProperty.RegisterAttached(
            "HitTestRole",
            typeof(ChromeHitTestRole),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                ChromeHitTestRole.Default,
                FrameworkPropertyMetadataOptions.Inherits
            ),
            IsDefinedChromeHitTestRole
        );

    private static readonly DependencyPropertyKey HoveredChromeRolePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HoveredChromeRole),
            typeof(ChromeHitTestRole),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeHitTestRole.Default)
        );

    public static readonly DependencyProperty HoveredChromeRoleProperty =
        HoveredChromeRolePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey PressedChromeRolePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(PressedChromeRole),
            typeof(ChromeHitTestRole),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeHitTestRole.Default)
        );

    public static readonly DependencyProperty PressedChromeRoleProperty =
        PressedChromeRolePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey TitleBarBorderThicknessPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(TitleBarBorderThickness),
            typeof(Thickness),
            typeof(ChromeWindow),
            new FrameworkPropertyMetadata(new Thickness(0))
        );

    public static readonly DependencyProperty TitleBarBorderThicknessProperty =
        TitleBarBorderThicknessPropertyKey.DependencyProperty;

    public Brush ActiveTitleBarBackground
    {
        get => (Brush)GetValue(ActiveTitleBarBackgroundProperty);
        set => SetValue(ActiveTitleBarBackgroundProperty, value);
    }

    public Brush ActiveTitleBarForeground
    {
        get => (Brush)GetValue(ActiveTitleBarForegroundProperty);
        set => SetValue(ActiveTitleBarForegroundProperty, value);
    }

    public Brush InactiveTitleBarBackground
    {
        get => (Brush)GetValue(InactiveTitleBarBackgroundProperty);
        set => SetValue(InactiveTitleBarBackgroundProperty, value);
    }

    public Brush InactiveTitleBarForeground
    {
        get => (Brush)GetValue(InactiveTitleBarForegroundProperty);
        set => SetValue(InactiveTitleBarForegroundProperty, value);
    }

    public Brush InactiveTitleBarBorderBrush
    {
        get => (Brush)GetValue(InactiveTitleBarBorderBrushProperty);
        set => SetValue(InactiveTitleBarBorderBrushProperty, value);
    }

    public Brush TitleBarBorderBrush
    {
        get => (Brush)GetValue(TitleBarBorderBrushProperty);
        set => SetValue(TitleBarBorderBrushProperty, value);
    }

    public double TitleBarHeight
    {
        get => (double)GetValue(TitleBarHeightProperty);
        set => SetValue(TitleBarHeightProperty, value);
    }

    /// <summary>标题栏按钮高度（DIP）。实测 Chrome 为 39：比标题栏矮 1px，上沿让出 1px 的边框线。</summary>
    public double CaptionButtonHeight
    {
        get => (double)GetValue(CaptionButtonHeightProperty);
        set => SetValue(CaptionButtonHeightProperty, value);
    }

    public double MinimizeButtonWidth
    {
        get => (double)GetValue(MinimizeButtonWidthProperty);
        set => SetValue(MinimizeButtonWidthProperty, value);
    }

    public double CaptionButtonWidth
    {
        get => (double)GetValue(CaptionButtonWidthProperty);
        set => SetValue(CaptionButtonWidthProperty, value);
    }

    /// <summary>获取或设置标题栏的主要自定义内容；为 <see langword="null"/> 时显示默认图标和标题。</summary>
    public object? TitleBarContent
    {
        get => GetValue(TitleBarContentProperty);
        set => SetValue(TitleBarContentProperty, value);
    }

    /// <summary>获取或设置标题栏主要内容的数据模板。</summary>
    public DataTemplate? TitleBarContentTemplate
    {
        get => (DataTemplate?)GetValue(TitleBarContentTemplateProperty);
        set => SetValue(TitleBarContentTemplateProperty, value);
    }

    /// <summary>获取或设置标题栏主要内容的数据模板选择器。</summary>
    public DataTemplateSelector? TitleBarContentTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(TitleBarContentTemplateSelectorProperty);
        set => SetValue(TitleBarContentTemplateSelectorProperty, value);
    }

    /// <summary>获取或设置标题栏右侧、系统按钮之前的操作区域内容。</summary>
    public object? TitleBarActions
    {
        get => GetValue(TitleBarActionsProperty);
        set => SetValue(TitleBarActionsProperty, value);
    }

    /// <summary>获取或设置标题栏操作区域的数据模板。</summary>
    public DataTemplate? TitleBarActionsTemplate
    {
        get => (DataTemplate?)GetValue(TitleBarActionsTemplateProperty);
        set => SetValue(TitleBarActionsTemplateProperty, value);
    }

    /// <summary>获取或设置标题栏操作区域的数据模板选择器。</summary>
    public DataTemplateSelector? TitleBarActionsTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(TitleBarActionsTemplateSelectorProperty);
        set => SetValue(TitleBarActionsTemplateSelectorProperty, value);
    }

    /// <summary>获取或设置普通标题按钮的悬停背景。</summary>
    public Brush CaptionButtonHoverBackground
    {
        get => (Brush)GetValue(CaptionButtonHoverBackgroundProperty);
        set => SetValue(CaptionButtonHoverBackgroundProperty, value);
    }

    /// <summary>获取或设置普通标题按钮的按下覆盖背景。</summary>
    public Brush CaptionButtonPressedBackground
    {
        get => (Brush)GetValue(CaptionButtonPressedBackgroundProperty);
        set => SetValue(CaptionButtonPressedBackgroundProperty, value);
    }

    /// <summary>获取或设置关闭按钮的悬停背景。</summary>
    public Brush CloseButtonHoverBackground
    {
        get => (Brush)GetValue(CloseButtonHoverBackgroundProperty);
        set => SetValue(CloseButtonHoverBackgroundProperty, value);
    }

    /// <summary>获取或设置关闭按钮的按下覆盖背景。</summary>
    public Brush CloseButtonPressedBackground
    {
        get => (Brush)GetValue(CloseButtonPressedBackgroundProperty);
        set => SetValue(CloseButtonPressedBackgroundProperty, value);
    }

    /// <summary>获取或设置禁用标题按钮的不透明度。</summary>
    public double CaptionButtonDisabledOpacity
    {
        get => (double)GetValue(CaptionButtonDisabledOpacityProperty);
        set => SetValue(CaptionButtonDisabledOpacityProperty, value);
    }

    /// <summary>获取或设置标题栏是否显示窗口图标；隐藏后不占用标题栏布局空间。</summary>
    public bool ShowTitleBarIcon
    {
        get => (bool)GetValue(ShowTitleBarIconProperty);
        set => SetValue(ShowTitleBarIconProperty, value);
    }

    /// <summary>
    /// 获取标题栏应显示的实际图标。未显式设置 <see cref="Window.Icon"/> 时，
    /// 该属性会在窗口句柄创建后反映 WPF 为原生窗口选定的图标。
    /// </summary>
    public ImageSource? EffectiveTitleBarIcon =>
        (ImageSource?)GetValue(EffectiveTitleBarIconProperty);

    /// <summary>获取当前指针覆盖的标题栏角色。</summary>
    public ChromeHitTestRole HoveredChromeRole =>
        (ChromeHitTestRole)GetValue(HoveredChromeRoleProperty);

    /// <summary>获取当前按下的标题栏角色。</summary>
    public ChromeHitTestRole PressedChromeRole =>
        (ChromeHitTestRole)GetValue(PressedChromeRoleProperty);

    /// <summary>获取对应当前 DPI 的单物理像素标题栏边框厚度。</summary>
    public Thickness TitleBarBorderThickness =>
        (Thickness)GetValue(TitleBarBorderThicknessProperty);

    /// <summary>设置元素参与标题栏原生命中测试的角色。</summary>
    public static void SetHitTestRole(DependencyObject element, ChromeHitTestRole value)
    {
        if (element is null)
            throw new ArgumentNullException(nameof(element));
        element.SetValue(HitTestRoleProperty, value);
    }

    /// <summary>获取元素当前生效的标题栏原生命中测试角色。</summary>
    public static ChromeHitTestRole GetHitTestRole(DependencyObject element)
    {
        if (element is null)
            throw new ArgumentNullException(nameof(element));
        return (ChromeHitTestRole)element.GetValue(HitTestRoleProperty);
    }

    private static void OnChromeVisualChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs
    ) => ((ChromeWindow)dependencyObject).ApplyVisualState();

    private static bool IsDefinedTitleBarStyle(object value) =>
        value is ChromeTitleBarStyle style && Enum.IsDefined(typeof(ChromeTitleBarStyle), style);

    private static bool IsDefinedTitleBarPalette(object value) =>
        value is ChromeTitleBarPalette palette && Enum.IsDefined(typeof(ChromeTitleBarPalette), palette);

    private static void OnTitleBarStyleChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs
    ) =>
        ((ChromeWindow)dependencyObject).ApplyTitleBarStyle((ChromeTitleBarStyle)eventArgs.NewValue);

    private static void OnTitleBarPaletteChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs
    ) =>
        ((ChromeWindow)dependencyObject).ApplyTitleBarPalette(
            (ChromeTitleBarStyle)((ChromeWindow)dependencyObject).TitleBarStyle
        );

    private static void OnChromeMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs
    )
    {
        var window = (ChromeWindow)dependencyObject;
        window.UpdateDpiVisuals();
        window._frame?.ScheduleNativeFrameRefresh();
    }

    private static bool IsNonNegativeFiniteDouble(object value) =>
        value is double number && number >= 0 && !double.IsNaN(number) && !double.IsInfinity(number);

    private static bool IsPositiveFiniteDouble(object value) =>
        value is double number && IsFinitePositive(number);

    private static bool IsUnitDouble(object value) =>
        value is double number && number >= 0 && number <= 1 && !double.IsNaN(number);

    private static bool IsDefinedChromeHitTestRole(object value) =>
        value is ChromeHitTestRole role && Enum.IsDefined(typeof(ChromeHitTestRole), role);

    private static bool IsFinitePositive(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}
