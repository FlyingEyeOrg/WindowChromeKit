using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

/// <summary>
/// A WPF window with a templateable client-area title bar and native Windows
/// resize, maximize, system-menu, shadow, and snap behavior.
/// </summary>
[TemplatePart(Name = PartTitleBar, Type = typeof(Border))]
[TemplatePart(Name = PartIcon, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartTitle, Type = typeof(TextBlock))]
[TemplatePart(Name = PartMinimizeButton, Type = typeof(Border))]
[TemplatePart(Name = PartMaximizeButton, Type = typeof(Border))]
[TemplatePart(Name = PartCloseButton, Type = typeof(Border))]
public class ChromeWindow : Window
{
    internal const string PartTitleBar = "PART_TitleBar";
    internal const string PartIcon = "PART_Icon";
    internal const string PartTitle = "PART_Title";
    internal const string PartMinimizeButton = "PART_MinimizeButton";
    internal const string PartMaximizeButton = "PART_MaximizeButton";
    internal const string PartCloseButton = "PART_CloseButton";
    internal const string PartMaximizeGlyph = "PART_MaximizeGlyph";

    private const uint WmCancelMode = 0x001F;
    private const uint WmShowWindow = 0x0018;
    private const uint WmSettingChange = 0x001A;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmDisplayChange = 0x007E;
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

    private static readonly Brush ButtonHover = FrozenBrush(0x1A, 0xFF, 0xFF, 0xFF);
    private static readonly Brush ButtonPressed = FrozenBrush(0x33, 0xFF, 0xFF, 0xFF);
    private static readonly Brush CloseHover = FrozenBrush(0xFF, 0xC4, 0x2B, 0x1C);
    private static readonly Brush ClosePressed = FrozenBrush(0xFF, 0xA9, 0x23, 0x16);

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

    private Border? _titleBar;
    private FrameworkElement? _icon;
    private TextBlock? _title;
    private Border? _minimize;
    private Border? _maximize;
    private Border? _close;
    private TextBlock? _maximizeGlyph;
    private HwndSource? _source;
    private WindowResizeOverlay? _resizeOverlay;
    private IntPtr _handle;
    private int _hotPart;
    private int _pressedPart;
    private bool _trackingMouse;
    private bool _inSizeMove;
    private bool _nativeFrameRefreshPending;
    private bool _refreshingNativeFrame;
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
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        SourceInitialized += OnChromeSourceInitialized;
        Activated += OnActivationChanged;
        Deactivated += OnActivationChanged;
        StateChanged += OnChromeStateChanged;
        ContentRendered += OnChromeContentRendered;
        Closed += OnChromeClosed;
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

    /// <summary>Raised after Windows reports a display topology or work-area change.</summary>
    public event EventHandler? DisplayConfigurationChanged;

    internal IntPtr ResizeOverlayHandle => _resizeOverlay?.Handle ?? IntPtr.Zero;

    internal void SynchronizeResizeOverlay() => _resizeOverlay?.Synchronize();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _titleBar = RequirePart<Border>(PartTitleBar);
        _icon = RequirePart<FrameworkElement>(PartIcon);
        _title = RequirePart<TextBlock>(PartTitle);
        _minimize = RequirePart<Border>(PartMinimizeButton);
        _maximize = RequirePart<Border>(PartMaximizeButton);
        _close = RequirePart<Border>(PartCloseButton);
        _maximizeGlyph = GetTemplateChild(PartMaximizeGlyph) as TextBlock;
        ApplyResizeMode();
        UpdateDpiVisuals();
        ApplyVisualState();
    }

    /// <summary>Centers this window on its owner monitor, or on the foreground monitor when it has no owner.</summary>
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

    /// <summary>Moves and sizes this window so its bounds fit within the nearest monitor work area.</summary>
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
                _pressedPart = wordParameter.ToInt32();
                ApplyVisualState();
                break;
            case WmNcLButtonUp:
                var releasedPart = wordParameter.ToInt32();
                var pressedPart = _pressedPart;
                _pressedPart = 0;
                ApplyVisualState();
                if (TryExecuteMinimizeButton(pressedPart, releasedPart))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
                break;
            case WmNcMouseLeave:
            case WmCancelMode:
            case WmCaptureChanged:
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
        if (_titleBar is null || _icon is null || _minimize is null || _maximize is null || _close is null)
            return WindowFrameHitTest.Client;
        return WindowFrameHitTest.Evaluate(
            pointer,
            GetScreenBounds(_icon),
            GetScreenBounds(_minimize),
            GetScreenBounds(_maximize),
            GetScreenBounds(_close),
            GetScreenBounds(_titleBar).Bottom);
    }

    private bool IsCaptionButtonPoint(NativePoint pointer) =>
        _minimize is not null && Contains(GetScreenBounds(_minimize), pointer)
        || _maximize is not null && Contains(GetScreenBounds(_maximize), pointer)
        || _close is not null && Contains(GetScreenBounds(_close), pointer);

    private void ApplyResizeMode()
    {
        var canMinimize = ResizeMode != ResizeMode.NoResize;
        var canMaximize = IsResizable;

        if (_minimize is not null)
            _minimize.Visibility = canMinimize ? Visibility.Visible : Visibility.Collapsed;
        if (_maximize is not null)
        {
            _maximize.Visibility = canMinimize ? Visibility.Visible : Visibility.Collapsed;
            _maximize.IsEnabled = canMaximize;
            _maximize.Opacity = canMaximize ? 1 : 0.4;
        }

        if (!canMinimize)
        {
            if (_hotPart == WindowFrameHitTest.MinButton) _hotPart = 0;
            if (_pressedPart == WindowFrameHitTest.MinButton) _pressedPart = 0;
        }
        if (!canMaximize)
        {
            if (_hotPart == WindowFrameHitTest.MaxButton) _hotPart = 0;
            if (_pressedPart == WindowFrameHitTest.MaxButton) _pressedPart = 0;
        }
        ApplyVisualState();

        if (_handle == IntPtr.Zero) return;
        if (IsResizable)
        {
            _resizeOverlay ??= new WindowResizeOverlay(_handle, IsCaptionButtonPoint);
            _resizeOverlay.Synchronize();
        }
        else
        {
            _resizeOverlay?.Dispose();
            _resizeOverlay = null;
        }
    }

    private bool IsResizable => ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;

    internal bool TryExecuteMinimizeButton(int pressedPart, int releasedPart)
    {
        if (ResizeMode == ResizeMode.NoResize
            || pressedPart != WindowFrameHitTest.MinButton
            || releasedPart != WindowFrameHitTest.MinButton)
        {
            return false;
        }

        SystemCommands.MinimizeWindow(this);
        return true;
    }

    private int VisibleCaptionButtonCount => ResizeMode switch
    {
        ResizeMode.NoResize => 1,
        _ => 3,
    };

    private void ApplyVisualState()
    {
        if (_titleBar is null || _title is null || _minimize is null || _maximize is null || _close is null) return;
        var foreground = IsActive ? ActiveTitleBarForeground : InactiveTitleBarForeground;
        _titleBar.Background = IsActive ? ActiveTitleBarBackground : InactiveTitleBarBackground;
        _titleBar.BorderBrush = TitleBarBorderBrush;
        _title.Foreground = foreground;
        SetButtonVisual(_minimize, WindowFrameHitTest.MinButton, foreground, false);
        SetButtonVisual(_maximize, WindowFrameHitTest.MaxButton, foreground, false);
        SetButtonVisual(_close, WindowFrameHitTest.Close, foreground, true);
    }

    private void SetButtonVisual(Border button, int part, Brush foreground, bool close)
    {
        button.SetValue(TextElement.ForegroundProperty, foreground);
        if (!button.IsEnabled)
        {
            button.Background = Brushes.Transparent;
            return;
        }
        button.Background = _pressedPart == part
            ? close ? ClosePressed : ButtonPressed
            : _hotPart == part
                ? close ? CloseHover : ButtonHover
                : Brushes.Transparent;
    }

    private void OnActivationChanged(object? sender, EventArgs eventArgs) => ApplyVisualState();

    private void OnChromeStateChanged(object? sender, EventArgs eventArgs)
    {
        if (_maximizeGlyph is not null)
            _maximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
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
        if (_titleBar is null) return;
        var scaleY = VisualTreeHelper.GetDpi(this).DpiScaleY;
        _titleBar.BorderThickness = new Thickness(0, 0, 0, 1 / (scaleY <= 0 ? 1 : scaleY));
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

    private T RequirePart<T>(string name) where T : DependencyObject =>
        GetTemplateChild(name) as T
        ?? throw new InvalidOperationException(
            $"ChromeWindow template must define {name} as {typeof(T).Name}.");

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

    private static bool IsFinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void OnChromeClosed(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        _disposed = true;
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
