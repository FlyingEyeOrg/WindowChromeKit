# WindowChromeKit

WindowChromeKit 是一个面向 .NET 8 的 WPF 自定义窗口程序集，源自 SoftwareHub DesktopAgent
中稳定使用的原生窗口边框实现。它保留标准的顶级 WPF HWND 和 DWM 合成，同时使用 WPF
绘制可定制的标题栏。

## 功能特性

- 支持原生窗口拖动、系统菜单、最小化、最大化、关闭、双击标题栏和 Snap Layout。
- 保留 `WS_CAPTION | WS_THICKFRAME`，由 DWM 提供阴影、可见边框和「阴影里那圈」不可见缩放带；
  客户区从窗口矩形内缩出 frame（普通态顶部不内缩，避免 Windows 10 画出原生标题栏）。
- 八方向缩放按真实 Chrome 实测的几何自行判定命中（左/右/下 8px、顶部 6px），
  不需要额外的覆盖窗口，也不会声明窗口矩形之外的像素。
- 最大化时客户区正好等于工作区，任务栏位于屏幕任意边缘都能正确铺满。
- 支持多显示器居中和工作区约束，包括负坐标显示器。
- 提供可模板化的 `ChromeWindow`，其画刷和尺寸均可通过依赖属性绑定。
- 提供 Windows 7 和 Windows 10 风格的可选多尺寸窗口图标资源，不改变 WPF 原有的窗口图标规则。
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
    xmlns:chrome="https://windowchromekit.dev/wpf"
    Title="My application"
    Width="900"
    Height="600"
    ResizeMode="CanResize"
    ActiveTitleBarBackground="#181818">
    <Grid />
</chrome:ChromeWindow>
```

`https://windowchromekit.dev/wpf` 是程序集注册的 XAML 命名空间标识，仅用于将 XAML 类型映射到
`WindowChromeKit.Wpf` 命名空间，不会访问网络。使用 NuGet 包或项目引用时都可以保持不变。

`ChromeWindow` 不会为 `Window.Icon` 注入默认值：未设置 `Icon` 或显式使用
`Icon="{x:Null}"` 时，仍由 WPF 使用项目的 `<ApplicationIcon>`；项目没有配置应用程序图标时，
则使用 Windows 默认图标。

推荐由宿主应用配置自己的应用程序图标：

```xml
<!-- MyApp.csproj -->
<PropertyGroup>
    <ApplicationIcon>Assets\App.ico</ApplicationIcon>
</PropertyGroup>
```

然后在窗口中省略 `Icon`，或显式使用 `Icon="{x:Null}"`。程序集也额外提供两个可选资源，应用
可以通过 `WindowChromeIcons` 按需显式引用。图标采用独立的懒加载，只有首次访问对应属性时才会
加载对应的 `.ico` 文件：

```xml
Icon="{x:Static chrome:WindowChromeIcons.Windows7}"
Icon="{x:Static chrome:WindowChromeIcons.Windows10}"
```

也可以在后台代码中使用：

```csharp
Icon = WindowChromeIcons.Windows10;
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

`ShowTitleBarIcon="False"` 可以只隐藏标题栏中的图标及其命中区域，并释放这部分布局空间，
不会改变任务栏或 Alt+Tab 中的窗口图标。完全自定义标题栏时，应绑定只读的
`EffectiveTitleBarIcon`，它既支持显式 `Icon`，也能在窗口句柄创建后取得 WPF 实际选定的图标：

```xml
<Border Width="28"
        Height="40"
        chrome:ChromeWindow.HitTestRole="SystemMenu">
    <Image Width="16"
           Height="16"
           IsHitTestVisible="False"
           Source="{Binding EffectiveTitleBarIcon,
                    RelativeSource={RelativeSource AncestorType={x:Type chrome:ChromeWindow}}}" />
</Border>
```

把图标容器标记为 `SystemMenu` 后，单击会打开系统菜单，双击会执行系统的关闭窗口行为。

### 模板迁移

- 将旧的 `PART_Icon` 角色替换为 `PART_SystemMenu` 或
  `ChromeWindow.HitTestRole="SystemMenu"`。
- 不再强制要求 `PART_Title`、`PART_MaximizeGlyph`，也不要求模板部件必须是具体的
  `Border` 或 `TextBlock` 类型。
- 将可拖动容器标记为 `Caption`，可交互子元素标记为 `Client`，标题栏按钮则标记为各自
  对应的按钮角色。
- 缺少可选区域时会安全降级，不会导致模板加载失败。

现有示例中包含一个独立的自定义标题栏窗口，演示内容插槽、可交互的标题栏文本框、
自定义菜单、菜单高对比度状态、图标切换、标题栏图标显隐、操作控件以及运行时颜色切换。

## 项目结构

- `src/WindowChromeKit.Wpf`：可复用的 WPF 类库。
- `src/WindowChromeKit.WinForms`：可复用的 WinForms 类库（`ChromeForm`），与 C++ 参考示例同一套
  原生 frame 模型：保留 `WS_CAPTION | WS_THICKFRAME`，客户区从窗口矩形内缩出 frame，
  `WM_NCHITTEST` 按 Chrome 实测的优先级判定。与 WPF 版一样内嵌了两个可选窗口图标
  （`WindowChromeIcons.Windows10` / `WindowChromeIcons.Windows7`，直接赋给 `Form.Icon`）。
  标题栏预置样式 `ChromeTitleBarStyle`（`ChromeWindow` / `ChromeForm` 上的 `TitleBarStyle`，
  默认 `Chrome`）——赋值时把该样式的几何与配色一次性套用，之后单独改属性以属性为准：

  | 样式 | 标题栏高 | 按钮 | 图标位置 | 配色 |
  | --- | --- | --- | --- | --- |
  | `Chrome`（默认） | 40 | 46×39 | 12px 位 | 跟随系统明暗 |
  | `VsCode` | 35 | 46×34 | 12px 位 | 固定深色 `#323233` |
  | `Windows` | 32 | 44×32 | 贴左 3px 位 | 跟随系统明暗 |

  三套样式都画顶边线、关闭按钮都用系统标准红；需要完全自定义标题栏时不用这个枚举
  （继承 `ChromeFrame`，或 `ShowDefaultTitleBar = false` 自己绘制）。
  标题栏自定义能力也与 WPF 版对齐：
  `TitleBarContent` / `TitleBarActions` 内容插槽、`SetHitTestRole` 逐控件命中角色
  （标记为 `Client` 的标题栏控件会自己接收鼠标，不会被当成拖动区）、
  以及 `ShowDefaultTitleBar = false` + `OnPaintTitleBar` 的完全自绘。
- `samples/WindowChromeKit.Wpf.Sample`：交互式示例应用程序。
- `samples/WindowChromeKit.WinForms.Sample`：WinForms 示例应用程序。
- `samples/WindowChromeKit.Native.Sample`：纯 Win32（C++）参考示例，按真实 Chrome 窗口实测的参数
  复刻「保留阴影 + 阴影中的 resize 带 + 自绘标题栏」，可作为库实现的对照基准。
- `tests/WindowChromeKit.Wpf.Tests`：算法测试和真实 HWND 集成测试。
- `legacy/SoftwareHub.DesktopAgent.Runtime/Windows`：供后续提取使用、不参与编译的源代码快照。

## 构建与测试

```powershell
dotnet build WindowChromeKit.sln
dotnet test WindowChromeKit.sln
dotnet run --project samples/WindowChromeKit.Wpf.Sample/WindowChromeKit.Wpf.Sample.csproj
```

## NuGet 自动发布

项目默认在本地执行构建和测试，不为 `main` 分支或 Pull Request 配置独立 CI 工作流。
NuGet 发布工作流仍会在打包前执行 Release 构建和全部测试，避免发布无效包。

自动发布使用 NuGet.org Trusted Publishing。NuGet Policy 中的仓库、`publish-nuget.yml`
工作流和 `production` environment 必须与本仓库一致；不需要配置永久的
`NUGET_API_KEY` secret。推送 `v` 开头的 SemVer 标签即可发布：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

发布工作流也支持手动运行；可填写 `package_version`，留空时使用项目中的
`VersionPrefix`。工作流会生成 `WindowChromeKit.Wpf.<版本>.nupkg` 和对应的 `.snupkg`，并
通过 GitHub OIDC 获取短期凭据后发布到 NuGet.org。已经存在的相同版本会通过
`--skip-duplicate` 安全跳过。

建议手动验证所有边缘和四个角的缩放行为，并检查 Snap、最大化/还原，以及窗口在
100%、125%、150% 和 200% 缩放的多显示器之间移动时的表现。标题栏按钮应始终能在其
完整高度范围内点击。
