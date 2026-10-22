# 窗口属性契约：frame 归库所有，内容归应用所有

本文是 `ChromeFrame` / `ChromeWindow` 的**属性所有权契约**，也是「哪些属性设置了会抛异常」的唯一依据。

它回答一个具体问题：**应用把这两个类当普通 `Window` 用，误设了某个 WPF 属性，窗口就花了 —— 我们怎么防。**

> **状态：契约已定，守卫代码尚未落地（见第 9 节）。**
> **本文所有"实测"结论来自 Windows 11 26300 / 96dpi 的探针程序**（方法见第 7 节）；
> 标了「待 Win10 复测」的项尚未在该平台验证。相关背景见 [`native-frame-model.md`](native-frame-model.md)。

---

## 0. 一句话

**`ChromeFrame` 卖的是「窗口边框的所有权」。** 保留 `WS_CAPTION | WS_THICKFRAME` 是整套方案的地基 ——
DWM 的阴影、可见边框、「阴影里那圈」不可见缩放带、Snap Layout、系统菜单全靠它。

而 WPF 里有一组属性天然也在争这块所有权。它们一旦被拨动，样式位就没了，但我们的
`WM_NCCALCSIZE` 仍然无条件按 `GetFrameThickness()`（96dpi 下 8px）内缩客户区
（`Internal/Frame/ChromeFrameController.cs:384-413`），于是凭空多出一圈**没有人绘制的非客户区**。

策略：**会拆地基、且我们能拦住的 → 禁用并抛；拦不住的 → 文档列为红线；不动地基的 → 放行。**

---

## 1. 先看实测：哪些属性真的拆地基

这是本文最重要的一张表。**它推翻了三条流传很广的经验说法**，也是禁用清单的依据。

| 组合 | `WS_CAPTION` | `WS_THICKFRAME` | DWM 非客户区渲染 | 不可见边框 | 判定 |
| --- | --- | --- | --- | --- | --- |
| 基线（默认属性） | ✅ | ✅ | **ON** | 7px | 正常 |
| `WindowStyle=None` | ❌ | ✅ | **ON** | 6px | ⚠️ 见 2.1 |
| `WindowStyle=ToolWindow` | ✅ | ✅ | ON | 7px | ⚠️ 见 2.1 |
| `WindowStyle=ThreeDBorderWindow` | ✅ | ✅ | ON | 7px | 安全 |
| `ResizeMode=NoResize` | ✅ | ❌ | ON | 7px | ⚠️ 见 2.2 |
| `ResizeMode=CanMinimize` | ✅ | ❌ | ON | 7px | ⚠️ 见 2.2 |
| `ResizeMode=CanResizeWithGrip` | ✅ | ✅ | ON | 7px | 安全 |
| `Opacity=0.5` | ✅ | ✅ | ON | 7px | **安全**（不产生 layered） |
| `AllowsTransparency=true` + `WindowStyle=None` | ❌ | ✅ | **OFF** | 0 | ❌ 红线 |
| `AllowsTransparency=true`（不配 `None`） | —— | —— | —— | —— | WPF 自己抛异常 |

三条被推翻的说法：

1. **「`WindowStyle=None` 会丢阴影和缩放带」—— 在 Windows 11 上不成立。** DWM 仍渲染非客户区，
   只是 `WS_CAPTION` 没了（不可见边框从 7px 变 6px，因为少了 caption 那一侧）。
2. **「`Opacity<1` 会让窗口走 layered」—— 不成立。** 实测 `WS_EX_LAYERED` 未置位，样式位无任何变化。
3. **「`AllowsTransparency` 的互斥检查可以被 coerce 拦下」—— 不成立。** 它先于 coerce 执行，
   抛出的是 WPF 自己的消息（见 2.3）。

---

## 2. 禁用清单

### 2.1 `WindowStyle`：禁用，抛异常

**只允许 `SingleBorderWindow`。**

虽然实测 `None` 在 Win11 上不丢 DWM 渲染，但它仍然破坏了我们的前提，且破坏方式**随系统版本变化**：

- `WS_CAPTION` 被摘掉 → 不可见边框的宽度从 7px 变 6px，与 `GetFrameThickness()` 的假设不一致（8px）；
- 系统菜单、`WS_SYSMENU` 语义的退化；
- **待 Win10 复测**：Win10 的 DWM 在 `WS_CAPTION` 缺失时是否仍渲染非客户区，尚未验证 ——
  而按 `native-frame-model.md` 5.7，Win10 才是真正暴露问题的平台。

`ToolWindow` 同理：实测 style 位与 `SingleBorderWindow` **完全相同**，只多了 `WS_EX_TOOLWINDOW`，
但它改变任务栏/Alt+Tab/系统菜单语义，属于我们不该替应用决定的范畴。

`ThreeDBorderWindow` 实测无差异，但既然没有理由使用，一并按 `SingleBorderWindow` 对待
（coerce 而非抛，避免无意义的破坏）。

### 2.2 `ResizeMode`：**不禁用**，但必须修两处不同源

`ResizeMode` 是 WPF 标准语义，模板触发器（`Themes/Generic.xaml:237-243`、`:268-272`）、命令可用性、
5 个测试用例都依赖它。**禁用等于删功能，本次决定保留。**

实测确认：`NoResize` / `CanMinimize` 会让 WPF 摘掉 `WS_THICKFRAME`，但**客户区内缩仍是 16/8，
且那圈不是死区** —— DWM 照样渲染它（阴影渐变照样存在），只是它不再可缩放。
三种 `ResizeMode` 的像素采样**完全一致**。

所以 `WS_THICKFRAME` 缺失**不能**被守卫当成错误：那会把 `NoResize` 这个合法功能误伤掉。
（初版守卫确实这么写过，实测后撤掉了。）

两处仍然必须收敛的不同源：

| # | 缺陷 | 位置 | 后果 |
| --- | --- | --- | --- |
| 1 | `CaptionButtonsWidth` 用 `CaptionButtonWidth * VisibleCaptionButtonCount`，**漏算 `MinimizeButtonWidth`** | `ChromeWindow.NativeFrame.cs:17-19` | Chrome 样式（45/46/46）恰好少算 1px（安全方向）；一旦调大 `MinimizeButtonWidth`，最小宽度不够，关闭按钮被挤出客户区右侧 |
| 2 | 按钮可用性两处判定不同源：`IsRoleEnabled` 用 `IsResizable`，`RoleToNativePart` 用 `ResizeMode != NoResize` | `ChromeWindow.Input.cs:58-70` + `Internal/Input/ChromeInputController.cs:274-285` | `CanMinimize` 下按钮置灰，命中层却仍返回 `HTMAXBUTTON` |

**两处已在本次修复**（见第 9 节），并各自补了回归测试。

### 2.3 `AllowsTransparency`：**拦不住，只能禁用并依赖 WPF 自己的异常**

实测：`AllowsTransparency = true` 而 `WindowStyle != None` 时，**WPF 在我们的 coerce 之前就抛了**：

```
InvalidOperationException: 当 AllowsTransparency 为 true 时，WindowStyle.None 是 WindowStyle 的唯一有效值。
```

原因是它的互斥检查在 `ValidateValueCallback` 路径上，而 validate 先于 coerce 执行，
且 `ValidateValueCallback` **无法通过 `OverrideMetadata` 替换**。

所以这条的结论是：

- **不要**为它写 coerce —— 写了也不会执行，属于死代码；
- `true` + `WindowStyle=None` 这个**合法组合**才是真正的红线（实测 DWM 非客户区渲染直接 OFF，
  不可见边框变 0，阴影与缩放带全消失）。它只能靠 `WM_STYLECHANGED` + `WS_EX_LAYERED` 检查兜底（见 2.5）；
- 文档把它列为红线属性。

### 2.4 `WindowChrome`（`System.Windows.Shell` 附加属性）：禁用，抛异常

它是别人命名空间上的附加依赖属性，**无法 `OverrideMetadata`**。只能在 frame 附加时读一次并抛：

```csharp
// ChromeFrame.OnFrameSourceInitialized 里，attach 之前
if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is not null)
    throw new InvalidOperationException("ChromeFrame 自带 frame 实现，不允许同时使用 WindowChrome。");
```

### 2.5 兜底层：校验真实样式位（拦不住的那部分）

属性层守卫只拦得住「通过依赖属性改」的路径，拦不住 `SetWindowLongPtr`、第三方库、模板内部改写。
因此在下列时机各校验一次，不符就抛：

- `OnFrameSourceInitialized` 之后；
- `WM_STYLECHANGED`（**当前完全没监听**，运行时改 `ResizeMode`/`WindowStyle` 我们事后才知道）。

校验项：

| 期望 | 违规含义 |
| --- | --- |
| `GWL_EXSTYLE` 不含 `WS_EX_LAYERED` | 有其它代码把窗口变成了 layered → DWM 非客户区渲染 OFF |
| `GWL_STYLE` 含 `WS_THICKFRAME` | 缩放带消失，而客户区仍内缩 → 死区 |
| `GWL_STYLE` 含 `WS_CAPTION` | 与 `GetFrameThickness()` 的假设不一致 |

---

## 3. 实现机制（落地时照做）

### 3.1 只能用 `CoerceValueCallback`

「设置就抛」直觉上用 `ValidateValueCallback`，但**它在 `DependencyProperty.Register` 时固定，
没有公开 API 能通过 `OverrideMetadata` 替换**。子类只能替换 `CoerceValueCallback`。

在 coerce 里抛出的异常会从 `SetValue` 正常向外传播；在 XAML 里表现为
`XamlParseException` 包裹我们的消息。

### 3.2 `OverrideMetadata` 必须显式写回原默认值

`OverrideMetadata` 传入的 `defaultValue` 会**替换**基类默认值。写成 `null` 或漏写，
`WindowStyle` 的默认值会变成 `0`（即 `None`）—— **所有窗口默认就没了 caption**，比不设守卫还糟。

```csharp
static ChromeFrame()
{
    WindowStyleProperty.OverrideMetadata(
        typeof(ChromeFrame),
        new FrameworkPropertyMetadata(
            WindowStyle.SingleBorderWindow,        // ← 必须写回，不能省
            null,
            (_, value) => value switch
            {
                WindowStyle.SingleBorderWindow => value,
                WindowStyle.ThreeDBorderWindow => WindowStyle.SingleBorderWindow,  // 实测无差异，coerce
                _ => throw new InvalidOperationException(
                    "ChromeFrame 需要保留 WS_CAPTION | WS_THICKFRAME 才能提供 DWM 阴影、"
                        + "可见边框和边缘缩放带，因此 WindowStyle 只允许 SingleBorderWindow。"
                        + "需要无标题栏窗口请直接继承 ChromeFrame；需要圆角请用 Win11 的 DWM 圆角。"),
            }));
}
```

只需写一处：`ChromeWindow` 继承 `ChromeFrame` 且未另设元数据，会自动沿用。

**值没变时不会触发 coerce** —— `SetValue` 在值相等时直接返回，所以显式写
`WindowStyle="SingleBorderWindow"` 是安全的，不会误伤。

### 3.3 `ChromeFrame` 构造函数里那三行是冗余的

`ChromeFrame.cs:20-22`：

```csharp
AllowsTransparency = false;        // 基类默认值本来就是 false
WindowStyle = WindowStyle.SingleBorderWindow;   // 基类默认值本来就是 SingleBorderWindow
ResizeMode = ResizeMode.CanResize;             // 基类默认值本来就是 CanResize
```

三行都只是把默认值又赋了一遍，而且**构造函数跑在 XAML 之前，一行都拦不住 XAML 根元素上的赋值**。
这正是当前问题的来源：看起来"设了正确值"，实际没有任何保护。落地守卫后可删。

### 3.4 异常消息格式

统一 `InvalidOperationException`，消息必须含 `ChromeFrame` 字样（便于在
`XamlParseException.InnerException` 链里定位）：

```
ChromeFrame 需要保留 <原因>，因此 <属性名> 只允许 <合法值>。<替代做法>。
```

---

## 4. 保留但需要约束的属性

| 属性 | 建议 | 原因 |
| --- | --- | --- |
| `UseLayoutRounding` | **coerce 成 `false`，不抛** | 顶边 1px 线高是 `1 / DpiScaleY`（`ChromeWindow.Visuals.cs:117`），布局取整会把它舍成 0 → **Win10 顶线静默消失**。抛异常太重（全局样式会误触） |
| `SizeToContent` | coerce 到 `Manual`（或至少 `Trace`） | 与最小跟踪尺寸、最大化工作区换算打架，不拆 frame |
| `CaptionButtonHeight > TitleBarHeight` | coerce 到 `TitleBarHeight` | 模板 `ClipToBounds` 裁掉视觉，命中判定却用元素**实际**屏幕边界（`ChromeWindow.Input.cs:78-90`）→ 看得见的点不到、看不见的点得到 |
| `TextElement.Foreground` / `FontFamily` 写在窗口上 | 保持现状 + 文档说明 | 模板 `Setter` 会覆盖标题栏的 `TextElement.Foreground`；入口是 `ActiveTitleBarForeground` |
| `Background` | 保持现状 + 文档说明 | 只在 `Attach` 时同步给 `CompositionTarget.BackgroundColor`（`ChromeFrameController.cs:66-74`），运行时修改不跟随 |

---

## 5. 白名单：可以放心用

`Topmost`、`Owner`、`ShowInTaskbar`、`ShowActivated`、`Title`、`Icon`、`Background`、`Opacity`（实测安全）、
`Content` / `ContentTemplate` / `ContentStringFormat`、`MinWidth` / `MinHeight`、
`MaxWidth` / `MaxHeight`、`WindowState`、`WindowStartupLocation`、`Template`（需保留 `PART_*`，
缺失时安全降级）、`IsEnabled`、`FlowDirection`（RTL 未做镜像，属未实现功能而非冲突）、
`FontFamily` / `FontSize`（影响内容，不影响标题栏几何）。

---

## 6. 用户意图 → 正确 API

| 他想干什么 | 他会写 | 正确做法 |
| --- | --- | --- |
| 取消边框 | `WindowStyle="None"`（❌ 抛） | 继承 `ChromeFrame`（本来就是"没有标题栏"的窗口骨架）；**frame 必须保留** |
| 去掉标题栏 | `WindowStyle="None"`（❌ 抛） | `ChromeFrame`，或 `ChromeWindow` + 自定义 `Template` |
| 做圆角 / 异形 | `AllowsTransparency="True"` + `WindowStyle="None"`（❌ 红线） | 圆角用 Win11 的 `DWMWA_WINDOW_CORNER_PREFERENCE`；异形/真半透明**本库不支持** |
| 淡入淡出 | `Opacity`（✅ 安全） | 直接动画窗口 `Opacity` 即可，实测不影响 DWM 渲染 |
| 想要系统阴影 | `WindowChrome.SetWindowChrome(...)`（❌ 抛） | 阴影本来就有（来自 DWM），不需要 |
| 禁止缩放 / 只允许最小化 | `ResizeMode="NoResize"` / `"CanMinimize"`（✅ 允许，但有已知问题） | 见 2.2 |
| 全屏 | 自己挂 `WM_GETMINMAXINFO` | 目前没有封装，需自行处理（`native-frame-model.md` 7.3） |
| 自定义整个标题栏 | `WindowStyle="None"` + 自绘（❌ 抛） | `ChromeFrame` + 覆写 `HitTestFrame`；`ChromeWindow` 用 `TitleBarContent` |
| 改标题栏几何 | 先 `TitleBarStyle` 再改属性 | 先赋 `TitleBarStyle`，**之后**再单独改几何属性（样式只套用一次） |

---

## 7. 实测方法与既有数据

探针程序（**未入库**，需要时按本节重建）：控制台 + `net8.0-windows` + `UseWPF`，引用
`WindowChromeKit.Wpf`，对每种属性组合做三件事：

1. `GetWindowLongPtr(GWL_STYLE / GWL_EXSTYLE)` 读样式位；
2. `DwmGetWindowAttribute(DWMWA_NCRENDERING_ENABLED=1)` 判断 DWM 是否渲染非客户区；
3. `GetWindowRect` 与 `DWMWA_EXTENDED_FRAME_BOUNDS=9` 的差 = 不可见边框宽度。

**不要用 `PrintWindow(…, 2)` 采像素**：WPF 内容会整块渲染成黑色，结论不可信（本次实测踩到）。
真实屏幕采样要用 `BitBlt` 从 `CreateDC("DISPLAY")` 抓，并且窗口必须 `Topmost` + `ShowActivated`。

平台：**Windows 11 26300，96dpi，DWM 合成开启**。

---

## 8. 一条被证伪的猜测（留档，避免重复踩）

首次采样基线窗口时，标题栏在 `y=1..39` 全是 `FFFFFF`，而客户区是染色的品红，于是曾判断为
「Win11 的 DWM 把 `WS_CAPTION` 声明的区域按系统标题栏色填白」。

**该判断是错的。** 把 `ActiveTitleBarBackground` 设成品红绝不会撞色的绿色 `#008000` 后重测：

```
y=0        A1927E   ← 顶边线（我们画的）
y=1..39    008000   ← 正是 ActiveTitleBarBackground，我们的标题栏
y=40       FF00FF   ← 客户区
```

`FFFFFF` 只是浅色主题下 `SystemTheme.Light.ActiveCaption` 的值（`Internal/SystemTheme.cs:41`），
**标题栏一直是我们自己画的，系统没有覆盖它**。

教训：**采样时必须用不可能与系统配色撞色的画刷**（品红/绿这种饱和度极高的值），
否则"系统填充"和"我们自己画的"无法区分 —— 这与 `native-frame-model.md` 8.1 的染色判别法是
同一条要求。

---

## 9. 落地状态

| 步 | 内容 | 状态 |
| --- | --- | --- |
| 1 | 本文档 | ✅ 已定稿 |
| 2 | `ChromeFrame` 静态构造：`WindowStyle` coerce | ✅ 已实现 |
| 3 | `OnFrameSourceInitialized`：`WindowChrome` 检查 + `WS_EX_LAYERED` 兜底；`WM_STYLECHANGED` 钩子 | ✅ 已实现 |
| 4 | 删除 `ChromeFrame.cs` 三行冗余赋值 | ✅ 已删除 |
| 5 | `ResizeMode` 两处不同源修复（2.2） | ✅ 已修复 |
| 6 | `UseLayoutRounding` coerce | ⏳ 未做（可选） |
| 7 | 测试矩阵（第 10 节）+ README 补链 | 部分：4 个契约测试已加；README 待补 |
| 8 | Win10 复测 `WindowStyle=None` 的 DWM 行为 | ⏳ 待做 |

**已实现的守卫**（`ChromeFrame.cs`）：

- `WindowStyle`：只放行 `SingleBorderWindow`；`ThreeDBorderWindow` coerce 回单边框；其余抛
  `InvalidOperationException`。XAML 路径下表现为 `XamlParseException` + 带行号的 `InnerException`。
- `WindowChrome` 附加属性：frame 附加时抛。
- `WS_EX_LAYERED`：`OnFrameSourceInitialized` 检查，出现即抛。
- `WM_STYLECHANGED`：转发给宿主重算几何（`IChromeFrameHost.OnNativeStyleChanged`）。
- **不校验 `WS_THICKFRAME`** —— 实测证明它缺失是 `ResizeMode` 的合法结果，不是缺陷。

**未实现**：`AllowsTransparency` 没有写 coerce —— 实测证明 WPF 的内置检查先于 coerce 执行，
写了也是死代码（见 2.3）。`Opacity` 不禁用 —— 实测不产生 layered（见第 1 节）。

### 关于 `WM_STYLECHANGED` 的一个实测限制

WPF 的 `HwndSource` 会在样式变化后重写自己的样式位：用 `SetWindowLongPtr` 强行写入
`WS_EX_LAYERED` 会被立刻覆盖回去（已实测，返回 0 且写入无效）。所以**无法用伪造样式位的方式
测试分层窗口守卫**；该守卫只能靠真实路径（`AllowsTransparency`）触发，而那条路径被 WPF 先抛了。
测试里因此只断言"四种 `ResizeMode` 都合法"，不伪造系统状态。

---

## 10. 测试矩阵

现有真实 HWND 用例只覆盖 `CanResize` 路径（`tests/WindowChromeKit.Wpf.Tests/ChromeWindowTests.cs:1274`、
`:1062`）。新增的四个契约测试（已落地）：

| 测试 | 锁住什么 |
| --- | --- |
| `WindowStyleIsGuardedToSingleBorder` | 默认值没被 `OverrideMetadata` 改坏；显式写正确值安全；`ThreeDBorder` 被 coerce；`None`/`ToolWindow` 抛且消息含 `ChromeFrame`，抛后值未落库 |
| `WindowChromeIsRejectedWhenTheFrameAttaches` | 附加属性那条拦不住的路径有兜底 |
| `EveryResizeModeIsAllowedDespiteLosingThickFrame` | 四种 `ResizeMode` 都合法；`WS_THICKFRAME` 的有无恰好等于"是否可缩放"，守卫不得误伤 |
| `CaptionButtonAvailabilityAndHitTestShareOneDecision` | `CanMinimize` 下按钮置灰**且**命中不再报 `HTMAXBUTTON` |
| `CaptionButtonsWidthCountsEachButtonRealWidth` | 总宽按各按钮真实宽度累加；调宽最小化按钮后最小窗口宽度跟着变大 |

仍建议补的组合（尚未覆盖）：

```
WindowStyle        { SingleBorder, ThreeDBorder, None, ToolWindow }
ResizeMode         { CanResize, CanMinimize, CanResizeWithGrip, NoResize }
Opacity            { 1, 0.5 }
AllowsTransparency { false, true }（true 应抛 WPF 自己的异常）
UseLayoutRounding  { false, true }
Template           { 默认, 缺 PART_MinimizeButton }
```

其余断言：`Opacity=0.5` 时 `GWL_EXSTYLE` 不含 `WS_EX_LAYERED`（锁住第 1 节的实测结论，
防止将来 WPF 行为变化）、`GetSystemMenu() != IntPtr.Zero`。

窗口内部的 `DwmGetWindowAttribute` / `GetWindowLongPtr` 已被 `NativeWindowMethods` 暴露，
测试可直接用。像素类断言（顶线、白条）只留在 VM 人工验证里 —— 见 `native-frame-model.md` 第 8 节。
