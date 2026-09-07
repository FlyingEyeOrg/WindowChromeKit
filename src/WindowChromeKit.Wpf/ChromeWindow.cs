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

/// <summary>提供完全可模板化标题栏以及原生缩放、系统菜单、阴影和 Snap Layout 的 WPF 窗口。</summary>
[TemplatePart(Name = PartTitleBar, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartSystemMenu, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartMinimizeButton, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartMaximizeButton, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartCloseButton, Type = typeof(FrameworkElement))]
public class ChromeWindow : Window
{
    internal const string PartTitleBar = "PART_TitleBar";
    internal const string PartSystemMenu = "PART_SystemMenu";
    internal const string PartMinimizeButton = "PART_MinimizeButton";
    internal const string PartMaximizeButton = "PART_MaximizeButton";
    internal const string PartCloseButton = "PART_CloseButton";

    private const uint WmCancelMode = 0x001F;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmShowWindow = 0x0018;
    private const uint WmSettingChange = 0x001A;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmGetIcon = 0x007F;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcMouseMove = 0x00A0;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmNcMouseLeave = 0x02A2;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const uint WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int IconSmall2 = 2;
    private const int IdiApplication = 32512;

    public static readonly DependencyProperty ActiveTitleBarBackgroundProperty =
        DependencyProperty.Register(
            nameof(ActiveTitleBarBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0x18, 0x18, 0x18), OnChromeVisualChanged));

    public static readonly DependencyProperty ActiveTitleBarForegroundProperty =
        DependencyProperty.Register(
            nameof(ActiveTitleBarForeground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0xCC, 0xCC, 0xCC), OnChromeVisualChanged));

    public static readonly DependencyProperty InactiveTitleBarBackgroundProperty =
        DependencyProperty.Register(
            nameof(InactiveTitleBarBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0x1F, 0x1F, 0x1F), OnChromeVisualChanged));

    public static readonly DependencyProperty InactiveTitleBarForegroundProperty =
        DependencyProperty.Register(
            nameof(InactiveTitleBarForeground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0x9D, 0x9D, 0x9D), OnChromeVisualChanged));

    public static readonly DependencyProperty TitleBarBorderBrushProperty =
        DependencyProperty.Register(
            nameof(TitleBarBorderBrush), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0x2B, 0x2B, 0x2B), OnChromeVisualChanged));

    public static readonly DependencyProperty TitleBarHeightProperty =
        DependencyProperty.Register(
            nameof(TitleBarHeight), typeof(double), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(35d, OnChromeMetricChanged), IsPositiveFiniteDouble);

    public static readonly DependencyProperty CaptionButtonWidthProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonWidth), typeof(double), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(46d, OnChromeMetricChanged), IsPositiveFiniteDouble);

    public static readonly DependencyProperty TitleBarContentProperty =
        DependencyProperty.Register(nameof(TitleBarContent), typeof(object), typeof(ChromeWindow));

    public static readonly DependencyProperty TitleBarContentTemplateProperty =
        DependencyProperty.Register(nameof(TitleBarContentTemplate), typeof(DataTemplate), typeof(ChromeWindow));

    public static readonly DependencyProperty TitleBarContentTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(TitleBarContentTemplateSelector), typeof(DataTemplateSelector), typeof(ChromeWindow));

    public static readonly DependencyProperty TitleBarActionsProperty =
        DependencyProperty.Register(nameof(TitleBarActions), typeof(object), typeof(ChromeWindow));

    public static readonly DependencyProperty TitleBarActionsTemplateProperty =
        DependencyProperty.Register(nameof(TitleBarActionsTemplate), typeof(DataTemplate), typeof(ChromeWindow));

    public static readonly DependencyProperty TitleBarActionsTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(TitleBarActionsTemplateSelector), typeof(DataTemplateSelector), typeof(ChromeWindow));

    public static readonly DependencyProperty CaptionButtonHoverBackgroundProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonHoverBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0x1A, 0xFF, 0xFF, 0xFF)));

    public static readonly DependencyProperty CaptionButtonPressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonPressedBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0x33, 0xFF, 0xFF, 0xFF)));

    public static readonly DependencyProperty CloseButtonHoverBackgroundProperty =
        DependencyProperty.Register(
            nameof(CloseButtonHoverBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0xC4, 0x2B, 0x1C)));

    public static readonly DependencyProperty CloseButtonPressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(CloseButtonPressedBackground), typeof(Brush), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(FrozenBrush(0xFF, 0xA9, 0x23, 0x16)));

    public static readonly DependencyProperty CaptionButtonDisabledOpacityProperty =
        DependencyProperty.Register(
            nameof(CaptionButtonDisabledOpacity), typeof(double), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(0.4d), IsUnitDouble);

    public static readonly DependencyProperty ShowTitleBarIconProperty =
        DependencyProperty.Register(
            nameof(ShowTitleBarIcon), typeof(bool), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(true));

    private static readonly DependencyPropertyKey EffectiveTitleBarIconPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(EffectiveTitleBarIcon), typeof(ImageSource), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty EffectiveTitleBarIconProperty =
        EffectiveTitleBarIconPropertyKey.DependencyProperty;

    public static readonly DependencyProperty HitTestRoleProperty =
        DependencyProperty.RegisterAttached(
            "HitTestRole", typeof(ChromeHitTestRole), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(
                ChromeHitTestRole.Default,
                FrameworkPropertyMetadataOptions.Inherits),
            IsDefinedChromeHitTestRole);

    private static readonly DependencyPropertyKey HoveredChromeRolePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HoveredChromeRole), typeof(ChromeHitTestRole), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeHitTestRole.Default));

    public static readonly DependencyProperty HoveredChromeRoleProperty = HoveredChromeRolePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey PressedChromeRolePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(PressedChromeRole), typeof(ChromeHitTestRole), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(ChromeHitTestRole.Default));

    public static readonly DependencyProperty PressedChromeRoleProperty = PressedChromeRolePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey TitleBarBorderThicknessPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(TitleBarBorderThickness), typeof(Thickness), typeof(ChromeWindow),
            new FrameworkPropertyMetadata(new Thickness(0, 0, 0, 1)));

    public static readonly DependencyProperty TitleBarBorderThicknessProperty =
        TitleBarBorderThicknessPropertyKey.DependencyProperty;

    private FrameworkElement? _titleBar;
    private FrameworkElement? _systemMenu;
    private FrameworkElement? _minimize;
    private FrameworkElement? _maximize;
    private FrameworkElement? _close;
    private HwndSource? _source;
    private WindowResizeOverlay? _resizeOverlay;
    private IntPtr _handle;
    private int _hotPart;
    private int _pressedPart;
    private bool _trackingCaptionButtonPress;
    private bool _trackingMouse;
    private bool _inSizeMove;
    private bool _nativeFrameRefreshPending;
    private bool _refreshingNativeFrame;
    private bool _effectiveIconRefreshPending;
    private bool _centerWhenInitialized;
    private bool _constrainWhenInitialized;
    private bool _disposed;

    static ChromeWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ChromeWindow), new FrameworkPropertyMetadata(typeof(ChromeWindow)));
    }

    public ChromeWindow()
    {
        AllowsTransparency = false;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        // 不在窗口根级强制布局取整，否则奇数标题栏中嵌套偶数高度控件时，
        // 两层居中产生的半像素会分别取整，最终累积为一个像素的垂直偏差。
        SnapsToDevicePixels = true;

        SourceInitialized += OnChromeSourceInitialized;
        Activated += OnActivationChanged;
        Deactivated += OnActivationChanged;
        StateChanged += OnChromeStateChanged;
        ContentRendered += OnChromeContentRendered;
        Closed += OnChromeClosed;

        CommandBindings.Add(new CommandBinding(
            SystemCommands.MinimizeWindowCommand, ExecuteMinimizeCommand, CanExecuteMinimizeCommand));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.MaximizeWindowCommand, ExecuteMaximizeCommand, CanExecuteMaximizeCommand));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.RestoreWindowCommand, ExecuteRestoreCommand, CanExecuteRestoreCommand));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, ExecuteCloseCommand, CanExecuteAlways));
        CommandBindings.Add(new CommandBinding(
            SystemCommands.ShowSystemMenuCommand, ExecuteShowSystemMenuCommand, CanExecuteAlways));
    }

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
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HitTestRoleProperty, value);
    }

    /// <summary>获取元素当前生效的标题栏原生命中测试角色。</summary>
    public static ChromeHitTestRole GetHitTestRole(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ChromeHitTestRole)element.GetValue(HitTestRoleProperty);
    }

    /// <summary>Windows 报告显示拓扑或工作区变化后触发。</summary>
    public event EventHandler? DisplayConfigurationChanged;

    internal IntPtr ResizeOverlayHandle => _resizeOverlay?.Handle ?? IntPtr.Zero;

    internal void SynchronizeResizeOverlay() => _resizeOverlay?.Synchronize();

    public override void OnApplyTemplate()
    {
        CancelCaptionButtonPress();
        _titleBar = null;
        _systemMenu = null;
        _minimize = null;
        _maximize = null;
        _close = null;
        base.OnApplyTemplate();
        _titleBar = GetTemplateChild(PartTitleBar) as FrameworkElement;
        _systemMenu = GetTemplateChild(PartSystemMenu) as FrameworkElement;
        _minimize = GetTemplateChild(PartMinimizeButton) as FrameworkElement;
        _maximize = GetTemplateChild(PartMaximizeButton) as FrameworkElement;
        _close = GetTemplateChild(PartCloseButton) as FrameworkElement;
        ApplyResizeMode();
        UpdateDpiVisuals();
        ApplyVisualState();
    }

    /// <summary>在所有者所在显示器居中；没有所有者时在前台窗口所在显示器居中。</summary>
    public void CenterOnTargetMonitor()
    {
        if (_handle == IntPtr.Zero)
        {
            _centerWhenInitialized = true;
            return;
        }

        var target = MonitorResolver.Resolve(Owner, this);
        NativeRectangle? ownerBounds = null;
        if (Owner is not null)
        {
            var ownerHandle = new WindowInteropHelper(Owner).Handle;
            if (ownerHandle != IntPtr.Zero && NativeWindowMethods.GetWindowRect(ownerHandle, out var rectangle))
                ownerBounds = rectangle;
        }

        SetNativeBounds(WindowPlacement.Center(target, EffectiveWidth, EffectiveHeight, ownerBounds));
    }

    /// <summary>移动并调整窗口，使窗口边界位于最近显示器的工作区内。</summary>
    public void ConstrainToWorkArea()
    {
        if (_handle == IntPtr.Zero)
        {
            _constrainWhenInitialized = true;
            return;
        }

        if (!NativeWindowMethods.GetWindowRect(_handle, out var current)) return;
        SetNativeBounds(WindowPlacement.Clamp(MonitorResolver.Resolve(null, this), current));
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs eventArgs)
    {
        base.OnPropertyChanged(eventArgs);
        if (eventArgs.Property == ResizeModeProperty) ApplyResizeMode();
        if (eventArgs.Property == IconProperty) ScheduleEffectiveTitleBarIconRefresh();
    }

    private double EffectiveWidth => IsFinitePositive(Width)
        ? Width
        : IsFinitePositive(ActualWidth) ? ActualWidth : Math.Max(1, MinWidth);

    private double EffectiveHeight => IsFinitePositive(Height)
        ? Height
        : IsFinitePositive(ActualHeight) ? ActualHeight : Math.Max(1, MinHeight);

    private void OnChromeSourceInitialized(object? sender, EventArgs eventArgs)
    {
        ApplyTemplate();
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        if (_source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        _source?.AddHook(WindowProcedure);
        UpdateDpiVisuals();
        RefreshNativeFrame();
        ApplyResizeMode();
        RefreshEffectiveTitleBarIcon();

        if (_centerWhenInitialized)
        {
            _centerWhenInitialized = false;
            CenterOnTargetMonitor();
        }
        else if (_constrainWhenInitialized)
        {
            _constrainWhenInitialized = false;
            ConstrainToWorkArea();
        }
    }

    private IntPtr WindowProcedure(
        IntPtr window, int message, IntPtr wordParameter, IntPtr longParameter, ref bool handled)
    {
        switch ((uint)message)
        {
            case WmGetMinMaxInfo:
                HandleGetMinMaxInfo(window, longParameter);
                break;
            case WmNcCalcSize:
                HandleNcCalcSize(window, wordParameter, longParameter);
                handled = true;
                return IntPtr.Zero;
            case WmShowWindow:
                if (wordParameter == IntPtr.Zero) _resizeOverlay?.Hide();
                else if (!_inSizeMove) Dispatcher.BeginInvoke(() => _resizeOverlay?.Synchronize());
                break;
            case WmNcHitTest:
                handled = true;
                return new IntPtr(HitTest(GetScreenPoint(longParameter)));
            case WmWindowPosChanged:
                if (!_inSizeMove) _resizeOverlay?.Synchronize();
                break;
            case WmDwmCompositionChanged:
                ScheduleNativeFrameRefresh();
                break;
            case WmEnterSizeMove:
                _inSizeMove = true;
                _resizeOverlay?.Hide();
                break;
            case WmExitSizeMove:
                _inSizeMove = false;
                _resizeOverlay?.Synchronize();
                break;
            case WmDpiChanged:
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateDpiVisuals();
                    ScheduleNativeFrameRefresh();
                });
                break;
            case WmDisplayChange:
            case WmSettingChange:
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateDpiVisuals();
                    ScheduleNativeFrameRefresh();
                    if ((uint)message == WmDisplayChange || wordParameter.ToInt64() == SpiSetWorkArea)
                        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
                });
                break;
            case WmNcMouseMove:
                ObserveNonClientMove(wordParameter.ToInt32());
                break;
            case WmNcLButtonDown:
                var pressedPart = wordParameter.ToInt32();
                var pressedRole = NativePartToRole(pressedPart);
                if (IsCaptionButtonRole(pressedRole))
                {
                    _hotPart = pressedPart;
                    _pressedPart = IsRoleEnabled(pressedRole) ? pressedPart : 0;
                    _trackingCaptionButtonPress = _pressedPart != 0;
                    if (_trackingCaptionButtonPress) _ = NativeWindowMethods.SetCapture(window);
                    ApplyVisualState();

                    // 系统默认过程会独占跟踪非客户区按钮，导致自定义按钮收不到抬起消息。
                    // 这里接管捕获，确保移出、移回、取消和最终命令具有普通 Button 一致的语义。
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmNcLButtonUp:
                var releasedPart = wordParameter.ToInt32();
                if (_trackingCaptionButtonPress)
                {
                    CompleteCaptionButtonPress(releasedPart);
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmMouseMove:
                if (_trackingCaptionButtonPress)
                {
                    UpdateCapturedCaptionButtonPointer();
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmLButtonUp:
                if (_trackingCaptionButtonPress)
                {
                    var currentPart = GetCaptionButtonPartAtCursor();
                    CompleteCaptionButtonPress(currentPart);
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmNcMouseLeave:
                if (_trackingCaptionButtonPress) break;
                ResetPointerVisualState();
                break;
            case WmCancelMode:
                CancelCaptionButtonPress();
                break;
            case WmCaptureChanged:
                _trackingCaptionButtonPress = false;
                ResetPointerVisualState();
                break;
        }

        return IntPtr.Zero;
    }

    private void HandleGetMinMaxInfo(IntPtr window, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero) return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0) dpi = 96;
        var resizeBorderWidth = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        var systemMinimum = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxMinTrack, dpi);
        limits.MinTrackSize = new NativePoint(
            WindowFrameHitTest.CalculateMinimumTrackWidth(
                limits.MinTrackSize.X,
                systemMinimum,
                IsResizable ? resizeBorderWidth : 0,
                CaptionButtonWidth * VisibleCaptionButtonCount,
                dpi),
            limits.MinTrackSize.Y);

        var monitor = NativeWindowMethods.MonitorFromWindow(window, NativeWindowMethods.MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor != IntPtr.Zero && NativeWindowMethods.GetMonitorInfo(monitor, ref info))
        {
            var placement = WindowFrameHitTest.CalculateMaximizedPlacement(info.Monitor, info.WorkArea);
            limits.MaxPosition = placement.Position;
            limits.MaxSize = placement.Size;
        }
        Marshal.StructureToPtr(limits, longParameter, false);
    }

    private static void HandleNcCalcSize(IntPtr window, IntPtr wordParameter, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero) return;
        var monitor = NativeWindowMethods.MonitorFromWindow(window, NativeWindowMethods.MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeWindowMethods.GetMonitorInfo(monitor, ref info)) return;
        var proposed = wordParameter != IntPtr.Zero
            ? Marshal.PtrToStructure<NativeNcCalcSizeParameters>(longParameter).Proposed
            : Marshal.PtrToStructure<NativeRectangle>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0) dpi = 96;
        var borderX = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        var borderY = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCyFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        if (!NativeWindowMethods.IsZoomed(window)
            && !WindowFrameHitTest.MatchesMaximizedBounds(proposed, info.WorkArea, borderX, borderY)) return;

        var client = WindowFrameHitTest.ClampToWorkArea(proposed, info.WorkArea);
        if (wordParameter != IntPtr.Zero)
        {
            var parameters = Marshal.PtrToStructure<NativeNcCalcSizeParameters>(longParameter);
            parameters.Proposed = client;
            Marshal.StructureToPtr(parameters, longParameter, false);
        }
        else
        {
            Marshal.StructureToPtr(client, longParameter, false);
        }
    }

    private int HitTest(NativePoint pointer)
    {
        try
        {
            return RoleToNativePart(GetRoleAtPoint(pointer));
        }
        catch (InvalidOperationException)
        {
            return WindowFrameHitTest.Client;
        }
        catch (ArgumentException)
        {
            return WindowFrameHitTest.Client;
        }
    }

    private ChromeHitTestRole GetRoleAtPoint(NativePoint pointer)
    {
        var clientPoint = PointFromScreen(new Point(pointer.X, pointer.Y));
        if (InputHitTest(clientPoint) is DependencyObject hit)
        {
            var role = GetHitTestRole(hit);
            if (role != ChromeHitTestRole.Default) return role;
        }

        if (_close is not null && Contains(GetScreenBounds(_close), pointer))
            return ChromeHitTestRole.CloseButton;
        if (_maximize is not null && Contains(GetScreenBounds(_maximize), pointer))
            return ChromeHitTestRole.MaximizeButton;
        if (_minimize is not null && Contains(GetScreenBounds(_minimize), pointer))
            return ChromeHitTestRole.MinimizeButton;
        if (_systemMenu is not null && Contains(GetScreenBounds(_systemMenu), pointer))
            return ChromeHitTestRole.SystemMenu;
        if (_titleBar is not null && Contains(GetScreenBounds(_titleBar), pointer))
            return ChromeHitTestRole.Caption;
        return ChromeHitTestRole.Client;
    }

    private void ApplyResizeMode()
    {
        var canMinimize = ResizeMode != ResizeMode.NoResize;
        var canMaximize = IsResizable;
        var cancelPress = false;

        if (!canMinimize)
        {
            if (_hotPart == WindowFrameHitTest.MinButton) _hotPart = 0;
            cancelPress = _pressedPart == WindowFrameHitTest.MinButton;
        }
        if (!canMaximize)
        {
            if (_hotPart == WindowFrameHitTest.MaxButton) _hotPart = 0;
            cancelPress |= _pressedPart == WindowFrameHitTest.MaxButton;
        }
        if (cancelPress) CancelCaptionButtonPress();
        else ApplyVisualState();
        CommandManager.InvalidateRequerySuggested();

        if (_handle == IntPtr.Zero) return;
        if (IsResizable)
        {
            // Overlay 与 owner 均由当前 Dispatcher 线程创建，HTTRANSPARENT 才能继续命中主窗口。
            _resizeOverlay ??= new WindowResizeOverlay(_handle, GetRoleAtPoint);
            _resizeOverlay.Synchronize();
        }
        else
        {
            _resizeOverlay?.Dispose();
            _resizeOverlay = null;
        }
    }

    private bool IsResizable => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    internal bool TryExecuteCaptionButton(ChromeHitTestRole pressedRole, ChromeHitTestRole releasedRole)
    {
        if (pressedRole != releasedRole || !IsRoleEnabled(pressedRole)) return false;

        switch (pressedRole)
        {
            case ChromeHitTestRole.MinimizeButton:
                SystemCommands.MinimizeWindow(this);
                return true;
            case ChromeHitTestRole.MaximizeButton:
                if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
                else SystemCommands.MaximizeWindow(this);
                return true;
            case ChromeHitTestRole.CloseButton:
                SystemCommands.CloseWindow(this);
                return true;
            default:
                return false;
        }
    }

    private bool IsRoleEnabled(ChromeHitTestRole role) => role switch
    {
        ChromeHitTestRole.MinimizeButton => ResizeMode != ResizeMode.NoResize,
        ChromeHitTestRole.MaximizeButton => IsResizable,
        ChromeHitTestRole.CloseButton => true,
        _ => false,
    };

    private int RoleToNativePart(ChromeHitTestRole role) => role switch
    {
        ChromeHitTestRole.Caption => WindowFrameHitTest.Caption,
        ChromeHitTestRole.SystemMenu => WindowFrameHitTest.SystemMenu,
        ChromeHitTestRole.MinimizeButton when ResizeMode != ResizeMode.NoResize => WindowFrameHitTest.MinButton,
        ChromeHitTestRole.MaximizeButton when ResizeMode != ResizeMode.NoResize => WindowFrameHitTest.MaxButton,
        ChromeHitTestRole.CloseButton => WindowFrameHitTest.Close,
        _ => WindowFrameHitTest.Client,
    };

    private static ChromeHitTestRole NativePartToRole(int part) => part switch
    {
        WindowFrameHitTest.Caption => ChromeHitTestRole.Caption,
        WindowFrameHitTest.SystemMenu => ChromeHitTestRole.SystemMenu,
        WindowFrameHitTest.MinButton => ChromeHitTestRole.MinimizeButton,
        WindowFrameHitTest.MaxButton => ChromeHitTestRole.MaximizeButton,
        WindowFrameHitTest.Close => ChromeHitTestRole.CloseButton,
        _ => ChromeHitTestRole.Default,
    };

    private static bool IsCaptionButtonRole(ChromeHitTestRole role) =>
        role is ChromeHitTestRole.MinimizeButton
            or ChromeHitTestRole.MaximizeButton
            or ChromeHitTestRole.CloseButton;

    private int VisibleCaptionButtonCount => ResizeMode switch
    {
        ResizeMode.NoResize => 1,
        _ => 3,
    };

    private void ApplyVisualState()
    {
        var hoveredRole = NativePartToRole(_hotPart);
        var pressedOrigin = NativePartToRole(_pressedPart);
        var pressedRole = pressedOrigin == hoveredRole ? pressedOrigin : ChromeHitTestRole.Default;
        SetValue(HoveredChromeRolePropertyKey, hoveredRole);
        SetValue(PressedChromeRolePropertyKey, pressedRole);

        _ = VisualStateManager.GoToState(this, IsActive ? "Active" : "Inactive", true);
        _ = VisualStateManager.GoToState(this, WindowState switch
        {
            WindowState.Maximized => "Maximized",
            WindowState.Minimized => "Minimized",
            _ => "NormalWindow",
        }, true);
        GoToCaptionButtonState(ChromeHitTestRole.MinimizeButton, "Minimize", hoveredRole, pressedRole);
        GoToCaptionButtonState(ChromeHitTestRole.MaximizeButton, "Maximize", hoveredRole, pressedRole);
        GoToCaptionButtonState(ChromeHitTestRole.CloseButton, "Close", hoveredRole, pressedRole);
    }

    private void GoToCaptionButtonState(
        ChromeHitTestRole role,
        string prefix,
        ChromeHitTestRole hoveredRole,
        ChromeHitTestRole pressedRole)
    {
        var suffix = !IsRoleEnabled(role)
            ? "Disabled"
            : pressedRole == role
                ? "Pressed"
                : hoveredRole == role ? "PointerOver" : "Normal";
        _ = VisualStateManager.GoToState(this, prefix + suffix, true);
    }

    private void ExecuteMinimizeCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.MinimizeWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteMinimizeCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = ResizeMode != ResizeMode.NoResize;
        eventArgs.Handled = true;
    }

    private void ExecuteMaximizeCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.MaximizeWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteMaximizeCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = IsResizable && WindowState != WindowState.Maximized;
        eventArgs.Handled = true;
    }

    private void ExecuteRestoreCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.RestoreWindow(this);
        eventArgs.Handled = true;
    }

    private void CanExecuteRestoreCommand(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = WindowState != WindowState.Normal;
        eventArgs.Handled = true;
    }

    private void ExecuteCloseCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        SystemCommands.CloseWindow(this);
        eventArgs.Handled = true;
    }

    private static void CanExecuteAlways(object sender, CanExecuteRoutedEventArgs eventArgs)
    {
        eventArgs.CanExecute = true;
        eventArgs.Handled = true;
    }

    private void ExecuteShowSystemMenuCommand(object sender, ExecutedRoutedEventArgs eventArgs)
    {
        var bounds = _systemMenu is null ? default : GetScreenBounds(_systemMenu);
        var location = bounds.Width > 0 && bounds.Height > 0
            ? new Point(bounds.Left, bounds.Bottom)
            : PointToScreen(new Point(0, TitleBarHeight));
        SystemCommands.ShowSystemMenu(this, location);
        eventArgs.Handled = true;
    }

    private void OnActivationChanged(object? sender, EventArgs eventArgs) => ApplyVisualState();

    private void OnChromeStateChanged(object? sender, EventArgs eventArgs)
    {
        UpdateDpiVisuals();
        ApplyVisualState();
        ScheduleNativeFrameRefresh();
        ApplyResizeMode();
    }

    private void OnChromeContentRendered(object? sender, EventArgs eventArgs) => _resizeOverlay?.Synchronize();

    private void ObserveNonClientMove(int part)
    {
        var changed = _hotPart != part;
        _hotPart = part;
        if (!_trackingMouse)
        {
            var tracking = new NativeTrackMouseEvent
            {
                Size = Marshal.SizeOf<NativeTrackMouseEvent>(),
                Flags = TmeLeave | TmeNonClient,
                WindowHandle = _handle,
            };
            _trackingMouse = NativeWindowMethods.TrackMouseEvent(ref tracking);
        }
        if (changed) ApplyVisualState();
    }

    private void UpdateCapturedCaptionButtonPointer()
    {
        var part = GetCaptionButtonPartAtCursor();
        if (_hotPart == part) return;
        _hotPart = part;
        ApplyVisualState();
    }

    private int GetCaptionButtonPartAtCursor()
    {
        if (!NativeWindowMethods.GetCursorPos(out var pointer)) return 0;
        try
        {
            var role = GetRoleAtPoint(pointer);
            return IsCaptionButtonRole(role) ? RoleToNativePart(role) : 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }

    private void CompleteCaptionButtonPress(int releasedPart)
    {
        var pressedPart = _pressedPart;
        _trackingCaptionButtonPress = false;
        _ = NativeWindowMethods.ReleaseCapture();
        _pressedPart = 0;
        _hotPart = releasedPart;
        ApplyVisualState();
        _ = TryExecuteCaptionButton(
            NativePartToRole(pressedPart),
            NativePartToRole(releasedPart));
    }

    private void CancelCaptionButtonPress()
    {
        var releaseCapture = _trackingCaptionButtonPress;
        _trackingCaptionButtonPress = false;
        if (releaseCapture) _ = NativeWindowMethods.ReleaseCapture();
        ResetPointerVisualState();
    }

    private void ResetPointerVisualState()
    {
        var changed = _hotPart != 0 || _pressedPart != 0;
        _trackingMouse = false;
        _hotPart = 0;
        _pressedPart = 0;
        if (changed) ApplyVisualState();
    }

    private void UpdateDpiVisuals()
    {
        var scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        SetValue(
            TitleBarBorderThicknessPropertyKey,
            new Thickness(0, 0, 0, 1 / (scaleY <= 0 ? 1 : scaleY)));
    }

    private void ScheduleNativeFrameRefresh()
    {
        if (_disposed || _handle == IntPtr.Zero || _nativeFrameRefreshPending) return;
        _nativeFrameRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _nativeFrameRefreshPending = false;
            RefreshNativeFrame();
        }));
    }

    private void RefreshNativeFrame()
    {
        if (_disposed || _handle == IntPtr.Zero || _refreshingNativeFrame) return;
        _refreshingNativeFrame = true;
        try
        {
            var margins = new NativeMargins(-1, -1, -1, -1);
            _ = NativeWindowMethods.DwmExtendFrameIntoClientArea(_handle, ref margins);
            _ = NativeWindowMethods.SetWindowPos(
                _handle, IntPtr.Zero, 0, 0, 0, 0,
                NativeWindowMethods.SwpFrameChanged
                | NativeWindowMethods.SwpNoZOrder
                | NativeWindowMethods.SwpNoActivate
                | NativeWindowMethods.SwpNoOwnerZOrder
                | NativeWindowMethods.SwpNoSize
                | NativeWindowMethods.SwpNoMove);
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
            UpdateLayout();
            _resizeOverlay?.Synchronize();
        }
        finally
        {
            _refreshingNativeFrame = false;
        }
    }

    private void SetNativeBounds(NativeRectangle bounds)
    {
        if (!NativeWindowMethods.SetWindowPos(
                _handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                NativeWindowMethods.SwpNoZOrder
                | NativeWindowMethods.SwpNoActivate
                | NativeWindowMethods.SwpNoOwnerZOrder))
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法设置窗口位置。");
        }
    }

    private static NativePoint GetScreenPoint(IntPtr longParameter)
    {
        var packed = longParameter.ToInt64();
        return new NativePoint(
            unchecked((short)(packed & 0xffff)),
            unchecked((short)((packed >> 16) & 0xffff)));
    }

    private static bool Contains(NativeRectangle bounds, NativePoint point) =>
        point.X >= bounds.Left && point.X < bounds.Right
        && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    private static NativeRectangle GetScreenBounds(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return default;
        var origin = element.PointToScreen(new Point());
        var opposite = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return new NativeRectangle(
            (int)Math.Floor(origin.X),
            (int)Math.Floor(origin.Y),
            (int)Math.Ceiling(opposite.X),
            (int)Math.Ceiling(opposite.Y));
    }

    private static void OnChromeVisualChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs) =>
        ((ChromeWindow)dependencyObject).ApplyVisualState();

    private static void OnChromeMetricChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var window = (ChromeWindow)dependencyObject;
        window.UpdateDpiVisuals();
        window.ScheduleNativeFrameRefresh();
    }

    private static bool IsPositiveFiniteDouble(object value) => value is double number && IsFinitePositive(number);

    private static bool IsUnitDouble(object value) =>
        value is double number && number >= 0 && number <= 1 && !double.IsNaN(number);

    private static bool IsDefinedChromeHitTestRole(object value) =>
        value is ChromeHitTestRole role && Enum.IsDefined(role);

    private static bool IsFinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void ScheduleEffectiveTitleBarIconRefresh()
    {
        if (Icon is not null || _handle == IntPtr.Zero)
        {
            RefreshEffectiveTitleBarIcon();
            return;
        }

        // Icon 清空后，WPF 会异步把应用程序图标重新写入 HWND；下一轮再读取才能得到最终结果。
        if (_effectiveIconRefreshPending) return;
        _effectiveIconRefreshPending = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _effectiveIconRefreshPending = false;
            if (!_disposed) RefreshEffectiveTitleBarIcon();
        });
    }

    private void RefreshEffectiveTitleBarIcon()
    {
        if (Icon is { } explicitIcon)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, explicitIcon);
            return;
        }

        if (_handle == IntPtr.Zero)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, null);
            return;
        }

        var iconHandle = NativeWindowMethods.SendMessage(
            _handle, WmGetIcon, new IntPtr(IconSmall2), IntPtr.Zero);
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.SendMessage(
                _handle, WmGetIcon, new IntPtr(IconSmall), IntPtr.Zero);
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.SendMessage(
                _handle, WmGetIcon, new IntPtr(IconBig), IntPtr.Zero);
        if (iconHandle == IntPtr.Zero)
            iconHandle = NativeWindowMethods.LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));

        if (iconHandle == IntPtr.Zero)
        {
            SetValue(EffectiveTitleBarIconPropertyKey, null);
            return;
        }

        // WM_GETICON 和 LoadIcon 返回的句柄均由系统所有，不能调用 DestroyIcon。
        var source = Imaging.CreateBitmapSourceFromHIcon(
            iconHandle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        if (source.CanFreeze) source.Freeze();
        SetValue(EffectiveTitleBarIconPropertyKey, source);
    }

    private void OnChromeClosed(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        _disposed = true;
        CancelCaptionButtonPress();
        _resizeOverlay?.Dispose();
        _resizeOverlay = null;
        _source?.RemoveHook(WindowProcedure);
        _source = null;
        SourceInitialized -= OnChromeSourceInitialized;
        Activated -= OnActivationChanged;
        Deactivated -= OnActivationChanged;
        StateChanged -= OnChromeStateChanged;
        ContentRendered -= OnChromeContentRendered;
        Closed -= OnChromeClosed;
    }
}
