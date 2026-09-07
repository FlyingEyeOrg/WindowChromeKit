using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SoftwareHub.DesktopAgent;

namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>整窗客户区、DWM 阴影和 WPF 标题栏的唯一实现。</summary>
internal sealed class DesktopWindowFrame : IDisposable
{
    internal const double TitleBarHeight = 35;
    internal const double CaptionButtonWidth = 46;
    private const int CaptionButtonCount = 3;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmWindowPosChanged = 0x0047;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmCancelMode = 0x001F;
    private const uint WmShowWindow = 0x0018;
    private const uint WmSettingChange = 0x001A;
    private const uint WmDisplayChange = 0x007E;
    private const uint WmNcMouseMove = 0x00A0;
    private const uint WmNcLButtonDown = 0x00A1;
    private const uint WmNcLButtonUp = 0x00A2;
    private const uint WmNcMouseLeave = 0x02A2;
    private const uint WmCaptureChanged = 0x0215;
    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmDwmCompositionChanged = 0x031E;
    private const uint WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;
    private const uint TmeLeave = 0x00000002;
    private const uint TmeNonClient = 0x00000010;

    private readonly Window _window;
    private readonly Grid _root;
    private readonly Border _titleBar;
    private readonly Image _icon;
    private readonly TextBlock _title;
    private readonly Border _minimize;
    private readonly Border _maximize;
    private readonly Border _close;
    private readonly TextBlock _maximizeGlyph;
    private readonly ContentControl _content;
    private HwndSource? _source;
    private DesktopWindowResizeOverlay? _resizeOverlay;
    private IntPtr _handle;
    private int _hotPart;
    private int _pressedPart;
    private bool _trackingMouse;
    private bool _inSizeMove;
    private bool _nativeFrameRefreshPending;
    private bool _refreshingNativeFrame;
    private DesktopWindowTitleBarOptions _colors;
    private Brush _activeBackground = null!;
    private Brush _activeForeground = null!;
    private Brush _inactiveBackground = null!;
    private Brush _inactiveForeground = null!;
    private Brush _border = null!;
    private static readonly Brush ButtonHover = ParseBrush("#FFFFFF1A");
    private static readonly Brush ButtonPressed = ParseBrush("#FFFFFF33");
    private static readonly Brush CloseHover = ParseBrush("#C42B1C");
    private static readonly Brush ClosePressed = ParseBrush("#A92316");
    private bool _disposed;

    internal DesktopWindowFrame(Window window, DesktopWindowTitleBarOptions? colors = null)
    {
        _window = window;
        _colors = DesktopWindowTitleBarOptionsResolver.Resolve(null, colors);
        window.AllowsTransparency = false;
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        window.ResizeMode = ResizeMode.CanResize;
        window.Background = Brushes.Transparent;
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;

        _icon = new Image
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
        };
        _icon.SetBinding(Image.SourceProperty, new Binding(nameof(Window.Icon)) { Source = window });

        _title = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
        };
        _title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });

        _minimize = CreateCaptionButton("\uE921", out _);
        _maximize = CreateCaptionButton("\uE922", out _maximizeGlyph);
        _close = CreateCaptionButton("\uE8BB", out _);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_minimize);
        buttons.Children.Add(_maximize);
        buttons.Children.Add(_close);

        var titleGrid = new Grid
        {
            Height = TitleBarHeight,
            ClipToBounds = true,
            UseLayoutRounding = true,
            SnapsToDevicePixels = true,
        };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition());
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleItems = new StackPanel { Orientation = Orientation.Horizontal };
        titleItems.Children.Add(_icon);
        titleItems.Children.Add(_title);
        var titleContent = new Border { ClipToBounds = true, Child = titleItems };

        Grid.SetColumn(buttons, 1);
        titleGrid.Children.Add(titleContent);
        titleGrid.Children.Add(buttons);
        _titleBar = new Border { Height = TitleBarHeight, BorderThickness = new Thickness(0, 0, 0, 1), Child = titleGrid };

        _content = new ContentControl
        {
            Background = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _root = new Grid { Background = Brushes.White };
        _root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 0,
        });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TitleBarHeight) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_titleBar, 0);
        Grid.SetRow(_content, 1);
        _root.Children.Add(_titleBar);
        _root.Children.Add(_content);
        window.Content = _root;

        window.SourceInitialized += OnSourceInitialized;
        window.Activated += OnActivationChanged;
        window.Deactivated += OnActivationChanged;
        window.StateChanged += OnStateChanged;
        window.ContentRendered += OnContentRendered;
        CacheBrushes();
        ApplyVisualState();
    }

    internal object? Content
    {
        get => _content.Content;
        set => _content.Content = value;
    }

    internal IntPtr ResizeOverlayHandle => _resizeOverlay?.Handle ?? IntPtr.Zero;

    internal void SynchronizeResizeOverlay() => _resizeOverlay?.Synchronize();

    internal event EventHandler? DisplayConfigurationChanged;

    internal void ApplyColors(DesktopWindowTitleBarOptions? colors)
    {
        _colors = DesktopWindowTitleBarOptionsResolver.Resolve(null, colors);
        CacheBrushes();
        ApplyVisualState();
    }

    private void OnSourceInitialized(object? sender, EventArgs eventArgs)
    {
        _handle = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        if (_source?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
        _source?.AddHook(WindowProcedure);
        UpdateDpiVisuals();
        RefreshNativeFrame();
        _resizeOverlay = new DesktopWindowResizeOverlay(_handle, IsCaptionButtonPoint);
    }

    private IntPtr WindowProcedure(IntPtr window, int message, IntPtr wordParameter, IntPtr longParameter, ref bool handled)
    {
        switch ((uint)message)
        {
            case WmGetMinMaxInfo:
                HandleGetMinMaxInfo(window, longParameter);
                // Keep this message unhandled so WPF can merge Window.Min/MaxWidth/Height
                // and update the same native limits it later uses during Measure/Arrange.
                break;
            case WmNcCalcSize:
                HandleNcCalcSize(window, wordParameter, longParameter);
                handled = true;
                return IntPtr.Zero;
            case WmShowWindow:
                if (wordParameter == IntPtr.Zero)
                {
                    _resizeOverlay?.Hide();
                }
                else if (!_inSizeMove)
                {
                    _window.Dispatcher.BeginInvoke(() => _resizeOverlay?.Synchronize());
                }
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
                _window.Dispatcher.BeginInvoke(() =>
                {
                    UpdateDpiVisuals();
                    ScheduleNativeFrameRefresh();
                });
                break;
            case WmDisplayChange:
            case WmSettingChange:
                _window.Dispatcher.BeginInvoke(() =>
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
                if (_pressedPart != pressedPart)
                {
                    _pressedPart = pressedPart;
                    ApplyVisualState();
                }
                break;
            case WmNcLButtonUp:
                if (_pressedPart != 0)
                {
                    _pressedPart = 0;
                    ApplyVisualState();
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

    private static void HandleGetMinMaxInfo(IntPtr window, IntPtr longParameter)
    {
        if (longParameter == IntPtr.Zero) return;
        var limits = Marshal.PtrToStructure<NativeMinMaxInfo>(longParameter);
        var dpi = NativeWindowMethods.GetDpiForWindow(window);
        if (dpi == 0) dpi = 96;
        var resizeBorderWidth = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxFrame, dpi)
            + NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxPaddedBorder, dpi);
        var systemMinimum = NativeWindowMethods.GetSystemMetricsForDpi(NativeWindowMethods.SmCxMinTrack, dpi);
        var minimumTrackWidth = DesktopWindowFrameHitTest.CalculateMinimumTrackWidth(
            limits.MinTrackSize.X,
            systemMinimum,
            resizeBorderWidth,
            CaptionButtonWidth * CaptionButtonCount,
            dpi);
        limits.MinTrackSize = new NativePoint(minimumTrackWidth, limits.MinTrackSize.Y);

        var monitor = NativeWindowMethods.MonitorFromWindow(window, NativeWindowMethods.MonitorDefaultToNearest);
        var info = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
        if (monitor != IntPtr.Zero && NativeWindowMethods.GetMonitorInfo(monitor, ref info))
        {
            var placement = DesktopWindowFrameHitTest.CalculateMaximizedPlacement(info.Monitor, info.WorkArea);
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
            && !DesktopWindowFrameHitTest.MatchesMaximizedBounds(proposed, info.WorkArea, borderX, borderY))
            return;

        var client = DesktopWindowFrameHitTest.ClampToWorkArea(proposed, info.WorkArea);
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
        var titleBounds = GetScreenBounds(_titleBar);
        return DesktopWindowFrameHitTest.Evaluate(
            pointer,
            GetScreenBounds(_icon),
            GetScreenBounds(_minimize),
            GetScreenBounds(_maximize),
            GetScreenBounds(_close),
            titleBounds.Bottom);
    }

    private bool IsCaptionButtonPoint(NativePoint pointer) =>
        Contains(GetScreenBounds(_minimize), pointer)
        || Contains(GetScreenBounds(_maximize), pointer)
        || Contains(GetScreenBounds(_close), pointer);

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

    private void ObserveNonClientMove(int part)
    {
        var visualChanged = _hotPart != part;
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
        if (visualChanged) ApplyVisualState();
    }

    private void ResetPointerVisualState()
    {
        var visualChanged = _hotPart != 0 || _pressedPart != 0;
        _trackingMouse = false;
        _hotPart = 0;
        _pressedPart = 0;
        if (visualChanged) ApplyVisualState();
    }

    private void OnActivationChanged(object? sender, EventArgs eventArgs) => ApplyVisualState();

    private void OnStateChanged(object? sender, EventArgs eventArgs)
    {
        _maximizeGlyph.Text = _window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        UpdateDpiVisuals();
        ApplyVisualState();
        ScheduleNativeFrameRefresh();
    }

    private void OnContentRendered(object? sender, EventArgs eventArgs) => _resizeOverlay?.Synchronize();

    private void ApplyVisualState()
    {
        var active = _window.IsActive;
        var foreground = active ? _activeForeground : _inactiveForeground;
        _titleBar.Background = active ? _activeBackground : _inactiveBackground;
        _titleBar.BorderBrush = _border;
        _title.Foreground = foreground;
        SetButtonVisual(_minimize, DesktopWindowFrameHitTest.MinButton, foreground, false);
        SetButtonVisual(_maximize, DesktopWindowFrameHitTest.MaxButton, foreground, false);
        SetButtonVisual(_close, DesktopWindowFrameHitTest.Close, foreground, true);
    }

    private void SetButtonVisual(Border button, int part, Brush foreground, bool close)
    {
        ((TextBlock)button.Child).Foreground = foreground;
        button.Background = _pressedPart == part
            ? close ? ClosePressed : ButtonPressed
            : _hotPart == part
                ? close ? CloseHover : ButtonHover
                : Brushes.Transparent;
    }

    private void CacheBrushes()
    {
        _activeBackground = ParseBrush(_colors.ActiveBackground!);
        _activeForeground = ParseBrush(_colors.ActiveForeground!);
        _inactiveBackground = ParseBrush(_colors.InactiveBackground!);
        _inactiveForeground = ParseBrush(_colors.InactiveForeground!);
        _border = ParseBrush(_colors.Border!);
    }

    private static Border CreateCaptionButton(string glyph, out TextBlock text)
    {
        text = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border { Width = CaptionButtonWidth, Height = TitleBarHeight, Child = text, IsHitTestVisible = false };
    }

    private static SolidColorBrush ParseBrush(string value)
    {
        byte a = 255;
        var r = byte.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (value.Length == 9) a = byte.Parse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void ApplyDwmFrame()
    {
        if (_handle == IntPtr.Zero) return;
        var margins = new NativeMargins(-1, -1, -1, -1);
        _ = NativeWindowMethods.DwmExtendFrameIntoClientArea(_handle, ref margins);
    }

    private void ScheduleNativeFrameRefresh()
    {
        if (_disposed || _handle == IntPtr.Zero || _nativeFrameRefreshPending) return;
        _nativeFrameRefreshPending = true;
        _window.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
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
            ApplyDwmFrame();
            _ = NativeWindowMethods.SetWindowPos(
                _handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeWindowMethods.SwpFrameChanged
                | NativeWindowMethods.SwpNoZOrder
                | NativeWindowMethods.SwpNoActivate
                | NativeWindowMethods.SwpNoOwnerZOrder
                | NativeWindowMethods.SwpNoSize
                | NativeWindowMethods.SwpNoMove);
            _root.InvalidateMeasure();
            _root.InvalidateArrange();
            _root.InvalidateVisual();
            _window.UpdateLayout();
            _resizeOverlay?.Synchronize();
        }
        finally
        {
            _refreshingNativeFrame = false;
        }
    }

    private void UpdateDpiVisuals()
    {
        var dpi = VisualTreeHelper.GetDpi(_window);
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        _titleBar.BorderThickness = new Thickness(0, 0, 0, 1 / scaleY);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _resizeOverlay?.Dispose();
        _resizeOverlay = null;
        _source?.RemoveHook(WindowProcedure);
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Activated -= OnActivationChanged;
        _window.Deactivated -= OnActivationChanged;
        _window.StateChanged -= OnStateChanged;
        _window.ContentRendered -= OnContentRendered;
    }
}
