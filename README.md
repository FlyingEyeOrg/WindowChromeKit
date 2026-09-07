# WindowChromeKit

WindowChromeKit 是一个面向 .NET 8 的 WPF 自定义窗口程序集，源自 SoftwareHub DesktopAgent
中稳定使用的原生窗口边框实现。它保留标准的顶级 WPF HWND 和 DWM 合成，同时使用 WPF
绘制可定制的标题栏。

## 功能特性

- 支持原生窗口拖动、系统菜单、最小化、最大化、关闭、双击标题栏和 Snap Layout。
- 在窗口客户区覆盖完整窗口的同时保留 DWM 阴影。
- 通过从属、不可激活的 Win32 覆盖窗口，实现支持 DPI 的八方向缩放。
- 在任务栏位于屏幕任意边缘时，都能正确计算最大化工作区。
- 支持多显示器居中和工作区约束，包括负坐标显示器。
- 提供可模板化的 `ChromeWindow`，其画刷和尺寸均可通过依赖属性绑定。
- 默认提供 Windows 7 风格的多尺寸窗口图标，应用仍可通过 `Icon` 属性覆盖。
- 保留标准 WPF `ResizeMode`、`Owner`、`Closing` 和模态窗口语义。

本程序集不依赖 SoftwareHub、WebView2、SignalR 或 Serilog。

## 使用方法

引用 `src/WindowChromeKit.Wpf/WindowChromeKit.Wpf.csproj`，然后让 XAML 根元素继承
`ChromeWindow`：

```xml
<chrome:ChromeWindow
    x:Class="MyApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:chrome="clr-namespace:WindowChromeKit.Wpf;assembly=WindowChromeKit.Wpf"
    Title="My application"
    Width="900"
    Height="600"
    ResizeMode="CanResize"
    ActiveTitleBarBackground="#181818">
    <Grid />
</chrome:ChromeWindow>
```

后台代码继承相同的基类：

```csharp
public partial class MainWindow : ChromeWindow
{
    public MainWindow() => InitializeComponent();
}
```

调用 `CenterOnTargetMonitor()` 可将窗口居中到所有者窗口或前台窗口所在的显示器；调用
`ConstrainToWorkArea()` 可将当前窗口范围移动到最近显示器的工作区内。

## 自定义标题栏

使用 `TitleBarContent` 放置品牌、标题或导航内容，使用 `TitleBarActions` 放置位于原生标题栏
按钮之前的 WPF 控件。操作区域默认可交互；在主标题区域内，需要为每个可交互的子树明确
指定命中测试角色：

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

`HitTestRole` 是可继承的附加属性，支持 `Client`、`Caption`、`SystemMenu`、
`MinimizeButton`、`MaximizeButton` 和 `CloseButton`。透明的 `Panel` 或 `Border` 必须设置
`Background="Transparent"`，才能参与 WPF 命中测试。角色区域重叠时，最上层的可见元素优先。

如果使用完全自定义的 `ControlTemplate`，应把角色设置在实际参与命中测试的元素上。程序集
不要求固定的元素类型，缺少可选区域时也不会抛出异常。自定义按钮建议绑定 WPF
`SystemCommands`，以保留键盘和 UI Automation 支持；最大化按钮的命中目标应继续使用
`MaximizeButton`，以保留 Windows 11 Snap Layout。

模板可以根据 `HoveredChromeRole` 和 `PressedChromeRole` 分别实现悬停及按下状态。默认模板
还将标题栏按钮的悬停画刷、按下画刷和禁用透明度公开为依赖属性。

### 模板迁移

- 将旧的 `PART_Icon` 角色替换为 `PART_SystemMenu` 或
  `ChromeWindow.HitTestRole="SystemMenu"`。
- 不再强制要求 `PART_Title`、`PART_MaximizeGlyph`，也不要求模板部件必须是具体的
  `Border` 或 `TextBlock` 类型。
- 将可拖动容器标记为 `Caption`，可交互子元素标记为 `Client`，标题栏按钮则标记为各自
  对应的按钮角色。
- 缺少可选区域时会安全降级，不会导致模板加载失败。

现有示例中包含一个独立的自定义标题栏窗口，演示内容插槽、可交互的标题栏文本框、
自定义菜单、操作控件以及运行时颜色切换。

## 项目结构

- `src/WindowChromeKit.Wpf`：可复用的 WPF 类库。
- `samples/WindowChromeKit.Wpf.Sample`：交互式示例应用程序。
- `tests/WindowChromeKit.Wpf.Tests`：算法测试和真实 HWND 集成测试。
- `legacy/SoftwareHub.DesktopAgent.Runtime/Windows`：供后续提取使用、不参与编译的源代码快照。

## 构建与测试

```powershell
dotnet build WindowChromeKit.sln
dotnet test WindowChromeKit.sln
dotnet run --project samples/WindowChromeKit.Wpf.Sample/WindowChromeKit.Wpf.Sample.csproj
```

建议手动验证所有边缘和四个角的缩放行为，并检查 Snap、最大化/还原，以及窗口在
100%、125%、150% 和 200% 缩放的多显示器之间移动时的表现。标题栏按钮应始终能在其
完整高度范围内点击。
