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
