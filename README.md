# WindowChromeKit

WindowChromeKit is a .NET 8 WPF window library extracted from the native window-frame
work in SoftwareHub DesktopAgent. It keeps a normal top-level WPF HWND and DWM
composition while drawing the title bar in WPF.

## Capabilities

- Native drag, system menu, minimize, maximize, close, double-click, and Snap Layout behavior.
- DWM shadow with a full client-area window.
- DPI-aware eight-direction resize using an owned, non-activating Win32 overlay.
- Correct maximize bounds for work areas with the taskbar on any edge.
- Multi-monitor centering and work-area constraints, including negative coordinates.
- A templateable `ChromeWindow` with bindable brush and sizing dependency properties.
- Standard WPF `ResizeMode`, `Owner`, `Closing`, and modal-window semantics.

The library has no dependency on SoftwareHub, WebView2, SignalR, or Serilog.

## Usage

Reference `src/WindowChromeKit.Wpf/WindowChromeKit.Wpf.csproj`, then derive the XAML
root from `ChromeWindow`:

```xml
<chrome:ChromeWindow
    x:Class="MyApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:chrome="clr-namespace:WindowChromeKit.Wpf;assembly=WindowChromeKit.Wpf"
    Title="My application"
    Width="900"
    Height="600"
    ResizeMode="CanResizeWithGrip"
    ActiveTitleBarBackground="#181818">
    <Grid />
</chrome:ChromeWindow>
```

The code-behind inherits the same base class:

```csharp
public partial class MainWindow : ChromeWindow
{
    public MainWindow() => InitializeComponent();
}
```

Use `CenterOnTargetMonitor()` to center on the owner or foreground monitor and
`ConstrainToWorkArea()` to move the current bounds inside the nearest work area.

## Custom title bars

Use `TitleBarContent` for branding or navigation and `TitleBarActions` for WPF
controls placed before the native caption buttons. The actions slot is interactive
by default. Inside the main title slot, mark each interactive subtree explicitly:

```xml
<chrome:ChromeWindow.TitleBarContent>
    <Grid Background="Transparent">
        <TextBlock Text="My application" />
        <TextBox Width="180"
                 HorizontalAlignment="Right"
                 chrome:ChromeWindow.HitTestRole="Client" />
    </Grid>
</chrome:ChromeWindow.TitleBarContent>
```

`HitTestRole` is inherited and supports `Client`, `Caption`, `SystemMenu`,
`MinimizeButton`, `MaximizeButton`, and `CloseButton`. A transparent `Panel` or
`Border` must set `Background="Transparent"` to participate in WPF hit testing.
The topmost visible element wins when roles overlap.

For a completely custom `ControlTemplate`, assign roles to the actual hit targets.
The library does not require a specific element type or throw when an optional
region is absent. Bind custom buttons to WPF `SystemCommands` for keyboard and UI
Automation support, and retain `MaximizeButton` on the maximize hit target to keep
Windows 11 Snap Layout.

Templates can react to `HoveredChromeRole` and `PressedChromeRole` to implement
independent hover and pressed/cover visuals. The default template also exposes the
caption hover/pressed brushes and disabled-button opacity as dependency properties.

### Template migration

- Replace the old `PART_Icon` role with `PART_SystemMenu` or
  `ChromeWindow.HitTestRole="SystemMenu"`.
- `PART_Title`, `PART_MaximizeGlyph`, and concrete `Border`/`TextBlock` part types
  are no longer required.
- Mark the draggable container as `Caption`, interactive children as `Client`, and
  caption controls with their corresponding button roles.
- Missing optional regions now degrade safely instead of failing template loading.

The existing sample contains a second custom-title-bar window demonstrating content
slots, an interactive title-bar text box, action controls, and runtime color changes.

## Projects

- `src/WindowChromeKit.Wpf` — reusable WPF class library.
- `samples/WindowChromeKit.Wpf.Sample` — interactive demonstration application.
- `tests/WindowChromeKit.Wpf.Tests` — algorithm and real-HWND integration tests.
- `legacy/SoftwareHub.DesktopAgent.Runtime/Windows` — non-compiling source snapshot for later extraction.

## Build and test

```powershell
dotnet build WindowChromeKit.sln
dotnet test WindowChromeKit.sln
dotnet run --project samples/WindowChromeKit.Wpf.Sample/WindowChromeKit.Wpf.Sample.csproj
```

For manual validation, resize every edge and corner, exercise Snap and maximize/restore,
move the window across monitors at 100%, 125%, 150%, and 200% scaling, and verify that
caption buttons remain clickable across their full height.
