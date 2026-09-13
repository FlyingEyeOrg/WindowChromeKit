# WindowChromeKit.Native.Sample（C++ / Win32）

用**纯 Win32** 复刻 Google Chrome 在 Windows 上的窗口方案：保留窗口阴影、把 resize border 放在
阴影里、标题栏完全自绘。这份示例不依赖 WPF，也不依赖 `WindowChromeKit.Wpf`，作用是给库的实现
提供一个可对照的原生参考。

## 一、方案（三段几何）

```
        ┌──────────────────────────────────────────────┐  ← windowRect（阴影画在它之外）
        │ ┌──────────────────────────────────────────┐ │
        │ │   7px 不可见边框 = 阴影里的 resize 带      │ │  ← DWM 的 padded border
        │ │ ┌──────────────────────────────────────┐ │ │
        │ │ │  1px DWM 可见边线                     │ │ │
        │ │ │ ┌──────────────────────────────────┐ │ │ │
        │ │ │ │  客户区 = 可见窗口 = 自绘标题栏     │ │ │ │
        │ │ │ │                                  │ │ │ │
        │ │ │ └──────────────────────────────────┘ │ │ │
        │ │ └──────────────────────────────────────┘ │ │
        │ └──────────────────────────────────────────┘ │
        └──────────────────────────────────────────────┘
```

| 环节 | 做法 |
|---|---|
| 样式 | `WS_OVERLAPPEDWINDOW \| WS_CLIPCHILDREN \| WS_CLIPSIBLINGS`（保留 `WS_CAPTION`/`WS_THICKFRAME`），系统菜单 / Snap / 动画 / 阴影全部来自它 |
| DWM | `DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED`；不使用 `DwmExtendFrameIntoClientArea` |
| `WM_NCCALCSIZE` | 客户区 = 窗口矩形内缩 `(frameX, topInset, frameX, frameY)`；最大化时四边都内缩，客户区正好等于工作区 |
| `WM_NCHITTEST` | 自己决定命中，优先级见下 |
| 标题栏 | 客户区顶部 40px（DIP）用 GDI 双缓冲自绘：图标、标题、三个原生语义按钮 |

**DWM 的 frame 区域 = 窗口矩形 − 客户区。** 只要客户区让出这块，DWM 就会自动给出 1px 可见边线、
7px 不可见 resize 边框和窗口外的阴影，而且这些像素属于窗口本身 —— 不需要任何覆盖窗口。

## 二、命中优先级（`HitTestFrame`）

| 顺序 | 区域 | 返回 |
|---|---|---|
| 0 | 窗口矩形之外 | `HTNOWHERE`（绝不声明别人的像素） |
| 1 | 标题栏按钮 | `HTCLOSE` / `HTMAXBUTTON` / `HTMINBUTTON`（Snap Layouts 依赖 `HTMAXBUTTON`） |
| 2 | 左 / 右 / 下各 `frameX/frameY`，含四角 | `HTLEFT` / `HTRIGHT` / `HTBOTTOM` / `HTTOPLEFT` … |
| 3 | 顶部 `topBand`（默认 6px，普通态） | `HTTOP` |
| 4 | 标题栏图标区 | `HTSYSMENU`（单击/右键打开系统菜单，双击关闭） |
| 5 | 标题栏空白 | `HTCAPTION`（白送拖动、双击最大化、Aero Snap、拖到顶部还原） |
| 6 | 其余 | `HTCLIENT` |

最大化时跳过第 3 步：纵向不可缩放，顶部整块交给标题栏（与 Chrome 一致，可在顶部拖动还原）。

## 三、数值来源：真实 Chrome 窗口实测

在 Windows 11（26200，100% DPI）上对一个**普通态** Chrome 窗口逐像素发送 `WM_NCHITTEST` 测得：

| 项 | 实测值 |
|---|---|
| `windowRect` / 可见边界 / 客户区 | `60,60,620,520` / `67,60,613,513` / `68,60,612,512` |
| 不可见边框 | 左 7 / 上 0 / 右 7 / 下 7 |
| 客户区内缩 | 左 8 / 上 0 / 右 8 / 下 8 |
| `DWMWA_VISIBLE_FRAME_BORDER_THICKNESS` | 1 |
| 左 / 右 / 下 resize 带 | 各 8px（`HTLEFT`/`HTRIGHT`/`HTBOTTOM`，从窗口矩形边界起算） |
| 顶部 resize 带 | 6px（`HTTOP`），按钮压过它 |
| 按钮 | 关闭 46px、最大化 46px、最小化 45px，高 39px，从顶边 +1px 开始 |
| 优先级 | 侧边/角带 **>** 按钮 **>** 顶部带 **>** `HTCAPTION` / `HTCLIENT` |
| 窗口矩形之外的探针 | 全部 `HTNOWHERE` |
| 阴影带归属（`WindowFromPoint`） | 可见边界外侧属于 `Chrome_WidgetWin_1` 顶层窗口本身，进入可见区后才是内容子窗口 |
| 最大化 | `windowRect = 工作区外扩 8px`，客户区 = 工作区 |
| 顶层样式 | `0x16CF0000`（最大化时 `0x17CF0000`），ex-style `0x00200100`（含 `WS_EX_NOREDIRECTIONBITMAP`） |

示例里的常量（`main.cpp` 顶部）就是按这张表对齐的：三种按钮宽度用统一的 46 DIP
（Chrome 的最小化按钮实测 45px 是整体取整误差），顶部带 6 DIP，其余三边取
`SM_CXFRAME + SM_CXPADDEDBORDER`。

与 Chrome 有意的差异只有一处：

- **`WS_EX_NOREDIRECTIONBITMAP`**：Chrome 用它走 DirectComposition，本示例用 GDI 双缓冲，
  因此不需要。

### 顶部绝不能留非客户区（Win10 原生标题栏的坑）

客户区顶边必须顶到窗口顶边，`WM_NCCALCSIZE` 里顶部内缩恒为 0。原因是在 **Windows 10** 上，
只要带 `WS_CAPTION` 的窗口在顶部留了哪怕 1px 非客户区，DWM 就会把整条 **23px 的原生标题栏**
（`SM_CYCAPTION` 高、系统原生最小化/最大化/关闭按钮）画出来，压在自绘标题栏上面；
Win11 不会。Chrome 在两种系统上都是客户区顶到顶边，所以从未遇到这个现象。

顶部这条 1px 边框线的处理，普通态与最大化态不同：

- **普通态：示例自己补一条**。Win10 的 DWM 只在非客户区里画边框，而我们把客户区顶到了窗口顶边，
  于是 DWM 不画这一条，会出现"左/右/下三边有框、顶边缺一条"的不对称。颜色取 DWM 画在其余三边的
  实测值（激活 `rgb(112,112,112)`、失焦 `rgb(170,170,170)`），四条边才一致。
  Win11 上 DWM 会在同一行自己画边框覆盖它 —— 纯红探针验证过 0 个像素可见，实测两边屏幕第一行
  都是 DWM 的 `rgb(183,179,179)`，对 Win11 无影响。
- **最大化：不画**。此时客户区正好等于工作区，画了就会在屏幕顶端多出一条线；Chrome 最大化同样没有。

非客户区渲染（阴影、不可见 resize 边框、左/右/下三条边框线）始终交给 DWM：
`DWMWA_NCRENDERING_POLICY = DWMNCRP_ENABLED`，与 Chrome 的配置一致。

### 主题配色

示例当前用**浅色（白色）标题栏**：标题栏底色 `#FFFFFF`（失活 `#F1F3F4`）、标题文字 `#202124`、
按钮悬停 `#E8EAED`、按下 `#DADCE0`、关闭按钮悬停红底白字 `#E81123`、
标题栏图标走**原生路径**，与 Windows/WPF 一致：

- 取图标顺序：`WM_GETICON(ICON_SMALL2)` → `WM_GETICON(ICON_SMALL)` → 窗口类 `GCLP_HICONSM`
  → 窗口类 `GCLP_HICON` → 系统默认图标（WPF 的 `EffectiveTitleBarIcon` 也是这个语义）；
- 图标资源在 `app.ico` + `app.rc`（资源 ID 101），窗口类的 `hIcon`/`hIconSm` 都取自它 ——
  相当于 WPF 的 `<ApplicationIcon>`，因此标题栏、任务栏、Alt+Tab 用的是同一个图标；
- 换图标只改 `app.ico`（或在代码里 `WM_SETICON` 设置窗口图标），标题栏会自动跟着变。

**客户区是纯白 `#FFFFFF` 且不画任何边框** —— 这样窗口最外那一圈（DWM 边框线 + 阴影）
不会被内容里的线条干扰，便于直接观察边缘效果。
换深色主题只要改 `main.cpp` 顶部那组颜色常量，并把 `ApplyChromeFrameAttributes()` 里的
`BOOL dark` 改成 `TRUE`（它决定 DWM 边线与阴影的明暗，要和标题栏底色配套）。

## 四、构建与运行

需要 Visual Studio 2022（v143）与 Windows 10 SDK。项目只提供 x64。

```powershell
# 在仓库根目录
msbuild samples\WindowChromeKit.Native.Sample\WindowChromeKit.Native.Sample.vcxproj /p:Configuration=Release /p:Platform=x64
samples\WindowChromeKit.Native.Sample\bin\x64\Release\WindowChromeKit.Native.Sample.exe
```

也可以直接在 Visual Studio 里打开 `WindowChromeKit.sln`，示例已加入 samples 解决方案文件夹。

### 静态链接，拷贝即用

Release 与 Debug 都用 `/MT`（`MultiThreaded` / `MultiThreadedDebug`）静态链接 CRT，
**只需要拷贝一个 exe** 到目标机器，不需要安装 VC++ 运行库。实测导入表只剩系统 DLL：

```
dwmapi.dll   KERNEL32.dll   USER32.dll   GDI32.dll
```

为配合「直接拷到 Win10 上跑」，版本敏感的 API 全部做了动态解析或失败回退 ——
**exe 里已经没有任何会挑系统版本的静态导入**：

| API / 属性 | 原生最低系统 | 处理方式 |
|---|---|---|
| `SetProcessDpiAwarenessContext` | Win10 1703 | `GetProcAddress` 动态调用，取不到就退回 manifest 的 `dpiAwareness` |
| `GetDpiForWindow` / `GetDpiForSystem` / `GetSystemMetricsForDpi` | Win10 1607 | 同样动态解析；取不到时回退到系统 DPI（`LOGPIXELSX`）与按比例缩放的 `GetSystemMetrics` |
| `DWMWA_USE_IMMERSIVE_DARK_MODE`(20) | Win10 1809 | 只影响 DWM 边线与阴影的明暗；设置失败无副作用（标题栏本来就是自绘的） |

因此静态导入只剩 `dwmapi` / `kernel32` / `user32` / `gdi32` 里的通用 API，
子系统版本 6.00，x64；从 Win7 到 Win11 都能加载（Win10 1809 之前 DWM 属性走回退路径，
顶部边线与 DPI 缩放按回退值处理）。

## 五、可以自己验证的现象

1. **阴影里的 resize 带**：把鼠标移到窗口边缘**外侧**（视觉上在阴影里）1–7px 处，光标变成缩放
   光标；这些像素属于本窗口（`WindowFromPoint` 返回本窗口，而不是任何辅助窗口）。
2. **窗口矩形之外不抢邻居**：把两个示例窗口贴在一起，只有自己窗口矩形内的像素会被自己接住。
3. **内容贴边仍可点**：右下角有个紧贴客户区边缘的按钮，可正常点击 —— resize 带完全在客户区之外。
4. **实时读数**：内容区显示 `windowRect`、`clientRect`、frame 厚度、DPI、最大化状态和
   **光标处的 `WM_NCHITTEST` 结果**，把鼠标沿边缘移动就能看到它在 `HTLEFT/HTTOP/HTCLIENT/HTCAPTION`
   之间切换。
5. **原生行为**：拖动标题栏移动、双击标题栏最大化/还原、拖到屏幕边缘 Aero Snap、
   悬停最大化按钮出现 Windows 11 Snap Layouts、`Alt+Space` 系统菜单、
   任务栏在任意边时最大化客户区都等于工作区。

在 Win10 目标机上额外确认三点：

1. **自绘标题栏上方不再出现原生标题栏**（这是顶部留非客户区才会触发的现象）；
2. 顶部那条 1px 边线是否可见 —— Win10 上由示例自绘，Win11 上由 DWM 绘制；
3. 阴影与不可见边框随 DPI 缩放正确（150% 时 `frameX` 应为 11px，窗口边缘那圈 resize 带同步变宽）。

## 六、与 `WindowChromeKit.Wpf` 的对应关系

| 本示例 | WPF 库 |
|---|---|
| `WM_NCCALCSIZE` 内缩 | `WindowFrameHitTest.CalculateClientArea` |
| `WM_NCHITTEST` 三段式 | `ChromeFrameController.HitTest` + `WindowFrameHitTest.EvaluateResizeHit` |
| `HitTestFrame` 里的按钮/标题栏矩形 | `ChromeWindow.ResolveRole` + `TryHitTestInteractiveContent` |
| `DrawCaptionGlyph` / `PaintWindow` | `Themes/Generic.xaml` 默认模板 |
| `ExecuteCaptionCommand` | `ChromeWindow.TryExecuteCaptionButton` |

WPF 库当前的 `WindowResizeOverlay`（额外 owned HWND 在窗口外侧补 8px）与本示例不是同一套模型：
它把 resize 带放在窗口矩形**之外**并交给另一个窗口，因而会与相邻窗口互相抢占；本示例与 Chrome
一样，把带子放在窗口自己的 frame 区域里。
