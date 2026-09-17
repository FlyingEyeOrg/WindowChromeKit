# 原生 frame 模型：保留 DWM 阴影与 resize 带、不侵入客户区

本文记录 WindowChromeKit 的核心技术方案：如何做出「像 Chrome 一样保留窗口阴影、阴影处
resize 带、自定义标题栏」，尤其是**不把 resize 热区侵入客户区、也不声明窗口矩形之外像素**
的做法，以及实现过程中实测发现和踩过的坑。

三个实现共用同一套模型，可互相对照：

| 实现 | 位置 | 说明 |
| --- | --- | --- |
| C++ 参考示例 | `samples/WindowChromeKit.Native.Sample` | 纯 Win32，参数按真实 Chrome 实测复刻，作为基准 |
| WPF 库 | `src/WindowChromeKit.Wpf` | `ChromeWindow` |
| WinForms 库 | `src/WindowChromeKit.WinForms` | `ChromeForm`，与 C++ 示例同一套逻辑 |

---

## 1. 目标与矛盾的根源

要同时满足三件事：

1. **保留 DWM 阴影**（窗口外侧那圈柔和过渡）；
2. **阴影里那圈不可见的 resize 热区**（鼠标移到视觉边缘外侧仍能缩放）；
3. **自定义标题栏**（客户区一直画到窗口最顶端，自己画按钮、图标、标题）。

标准的 `WindowStyle="None"` 会同时丢掉 1 和 2；`WindowChrome` 类在 WPF 里能部分满足，
但缩放带位置和命中行为与原生窗口有差异。Windows 给原生窗口提供的机制是：

```
保留 WS_CAPTION | WS_THICKFRAME      → DWM 就画阴影、可见边框、不可见缩放带
再用 WM_NCCALCSIZE 把客户区"顶"到想要的位置  → 客户区可以覆盖标题栏区域
```

于是矛盾落在**缩放带放在哪里**上。

---

## 2. 两代方案

### 2.1 旧方案（overlay）：客户区铺满整个窗口矩形 + 外置透明 HWND

客户区覆盖整个窗口矩形，另建一个**不可激活的 owned 透明窗口**（`WindowResizeOverlay`）
挂在主窗口**外侧**，专门接收鼠标并提供 resize 命中。因为客户区铺满后 DWM 的非客户区
被吃掉了，阴影需要靠 `DwmExtendFrameIntoClientArea(1,1,1,1)` 找回来。

**致命问题：overlay 声明了窗口矩形之外的像素。** 相邻窗口贴近时会被这个 resize 区压住，
表现为「旁边的窗口被莫名其妙挡住一块」。下面是 331 行 overlay 代码、命中同步、放大时隐藏
等一整套管道，只为绕开这一件事。

> 该方案已废弃。`git log` 中 `Move the WPF window onto the native frame model` 记录了迁移。
> overlay 最后存在的状态是提交 `3a98902`（即删除它的 `1f2cbc4` 的父提交），
> `git show 3a98902:src/WindowChromeKit.Wpf/Internal/Frame/WindowResizeOverlay.cs`
> 可以取回实现用于对比验证。

### 2.2 现方案（main）：缩放带收进窗口自己的非客户区

```cpp
// WM_NCCALCSIZE：客户区 = 窗口矩形内缩 frame
client.left   = window.left   + frameX;
client.top    = window.top    + (maximized ? frameY : 0);   // 普通态顶部不内缩
client.right  = window.right  - frameX;
client.bottom = window.bottom - frameY;
```

其中 `frameX/frameY = SM_CXFRAME + SM_CXPADDEDBORDER`（96dpi 下 8px）。

这样：

- **缩放带属于窗口自身的非客户区**，不侵占任何别的窗口的像素；
- **DWM 原生渲染**阴影、可见边框、不可见缩放带 —— 不需要 overlay，也不需要
  `DwmExtendFrameIntoClientArea`；
- **普通态顶部不内缩**（`frameY` 只在最大化时生效），客户区顶到窗口顶边，标题栏可以画满。

代价是：**Windows 10 的 DWM 只在非客户区画边框，顶部没有非客户区 → 顶边那 1px 得自己画**。
这条线是后续最长的尾巴，详见第 5 节。

---

## 3. 命中判定（WM_NCHITTEST）

不调用 `DefWindowProc`，完全按实测的真实 Chrome 优先级自行判定。顺序**很重要**：

| 优先级 | 区域 | 返回 |
| --- | --- | --- |
| 0 | 窗口矩形之外 | `HTNOWHERE`（绝不声明别人的像素） |
| 1 | 客户区之外、窗口矩形之内（"阴影里那圈"） | 左/右/下/角 → 缩放命中；**最大化时 → `HTCAPTION`** |
| 2 | 标题栏按钮 | `HTMINBUTTON` / `HTMAXBUTTON` / `HTCLOSE` |
| 3 | 标题栏里的可交互自定义控件 | `HTCLIENT`（让控件自己接收鼠标） |
| 4 | 系统菜单命中盒子（`SM_CXSMSIZE × SM_CYSMSIZE`） | `HTSYSMENU` |
| 5 | 顶部缩放带（仅标题栏空白处、非最大化） | `HTTOP` |
| 6 | 其余标题栏 → `HTCAPTION`；标题栏之下 → `HTCLIENT` | |

要点：

- **按钮压过一切**，否则鼠标移到按钮上会出现缩放光标；
- **可交互控件（菜单、输入框）优先于缩放带**，否则点不到靠边的控件；
- 顶部缩放带比其余三边窄（默认 6dip，`TopResizeBandDip` 可调），因为标题栏顶部还要
  留给拖动；
- 窗口矩形之外一律 `HTNOWHERE` —— 这是 overlay 方案要解决的问题的正面回答。

### 最大化时的两处特殊处理（实测对齐 Chrome）

1. **顶部那 8 行返回 `HTCAPTION` 而不是 `HTTOP`**。最大化时纵向本来就不能再缩放，给缩放
   光标是错的；Chrome 实测该区域也是 `HTCAPTION`（可拖动还原）。
2. **客户区外、窗口内的其余部分**同理走 `HTCAPTION`。

---

## 4. 必须打开 DWM 非客户区渲染

```csharp
// DWMNCRENDERINGPOLICY：USEWINDOWSTYLE = 0, DISABLED = 1, ENABLED = 2
DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, &policy /* = 2 */, sizeof(int));
```

**必须显式设置。** WinForms 建窗后这个策略**不是"沿用窗口样式"，实测会被关掉**，症状是
阴影/边框消失或时有时无。库在 `OnHandleCreated` 里主动设一次
（WinForms: `EnsureNonClientRendering`）。

---

## 5. 顶边那 1px 线（最大的坑）

### 5.1 为什么必须自己画

- **Win11**：DWM 会画顶边，并且**盖在我们画的那一行之上** → 自绘的线看不到，也不会出错；
- **Win10**：DWM 只画左/右/下三条边，顶部没有非客户区 → **不画就缺一条边**。

所以这条线在 Win11 上"看不出对错"，**所有相关问题都只在 Win10 可见**。

### 5.2 试过但不可行的方案

**`WM_NCCALCSIZE` 顶部返回 1px，把顶边交回 DWM 画。** 思路看似最干净（四边同源），
实测**不成立**：那 1px 会被系统按系统标题栏色填成**不透明纯白**，不是 DWM 的边框。

判别实验（客户区染成品红，第 0 行颜色即结论）：

```
A  topNC=0（现状）  → y0=(255,0,255) 品红 = 客户区      → 顶部无线，必须自绘
B  topNC=1          → y0=(255,255,255) 纯白！           → 系统填充，不是 DWM 边框
C  topNC=1 + 屏蔽 WM_NCPAINT → y0=(255,255,255) 纯白！  → 屏蔽重绘也无效
标准窗口（DWM 真边框）      → 黑底 (43,43,43) / 白底 (170,170,170)
```

在深色标题栏上，纯白比自绘的灰线**更糟**，因此这条路被否决。

### 5.3 也不能用"把按钮改小"来让位

曾用「按钮高度 = 标题栏高 − 1」让出第 0 行给顶线。普通态没问题（那 1px 被线填上了），
**但最大化时不画线 → 空出的那一行露出标题栏底色，浅色标题栏下就是一条 1px 白边**。

正确做法是**让按钮铺满、把线浮在最上层**（见 5.5）。

### 5.4 最终方案：按 DWM 的混合公式画

在原生窗口上分别于**黑底和白色桌面**采样其边框，反解出混合参数 —— 关键发现是
**聚焦与失焦的参数并不相同**：

| 状态 | 黑底 | 白底 | 反解 alpha | 反解基色 |
| --- | --- | --- | --- | --- |
| 聚焦 | 25 | 112 | **66%** | **`#262626`** |
| 失焦 | 43 | 170 | **50%** | **`#565656`** |

```
线色 = alpha × 基色 + (1 - alpha) × 标题栏底色
```

实现上存的是**半透明基色**（不是预先混好的实色），绘制时自动与当前标题栏底色混合：

```csharp
// 聚焦 #262626 @ 66%   失焦 #565656 @ 50%
private Color _topBorderLineActiveColor   = Color.FromArgb(168, 0x26, 0x26, 0x26);
private Color _topBorderLineInactiveColor = Color.FromArgb(128, 0x56, 0x56, 0x56);
```

**这一点决定了扩展性**：因为存的是 alpha + 基色，**自定义任何标题栏配色都不需要改这两个
值**，线会自动跟随底色。只有"基色 + alpha"这四个常量是实测定死的。

实测吻合度（Win10 19045，96dpi）：

| 场景 | 我们的线 | 原生 DWM |
| --- | --- | --- |
| 白底 · 聚焦 | **112** | 112 |
| 白底 · 失焦 | 163 | 170 |
| 黑底 · 聚焦 | **25** | 25 |

### 5.5 绘制时的分层（两种框架机制不同，语义一致）

原则：**按钮铺满整条标题栏，顶线浮在最上层，最大化时线不存在**。

| | WinForms | WPF |
| --- | --- | --- |
| 线的载体 | `Graphics.DrawLine` 画进标题栏位图 | 模板里的 `PART_TopBorderLine` Border |
| 冲突点 | 绘制**顺序**（后画的盖前面的） | 布局**空间**（`BorderThickness` 会挤内容区） |
| 做法 | 按钮绘制矩形**从线之下铺到标题栏底部** | 线做成**不占布局的覆盖层**，画在按钮之上 |

WPF 的具体做法（`Generic.xaml`）：

```xml
<!-- 单独覆盖层，不参与布局、不吃鼠标、绘制在按钮之上；最大化时高度为 0 -->
<Border x:Name="PART_TopBorderLine"
        Grid.Row="0"
        Height="{Binding Path=(local:ChromeWindow.TitleBarBorderThickness).Top, ...}"
        VerticalAlignment="Top"
        Background="{TemplateBinding InactiveTitleBarBorderBrush}"
        IsHitTestVisible="False" />
```

**踩过的坑**：把线从 `Border.BorderThickness` 改成覆盖层时，**忘记改 `IsActive` 触发器
的目标**，导致 `PART_TopBorderLine` 永远用失活画刷、聚焦时不切色。在 Win11 上完全看不到
（被 DWM 覆盖），只有 Win10 才暴露。

### 5.6 这条线属于「窗口边框」，不属于「标题栏内容」

这是一次**职责归位**，不只是代码搬家。

线要补的是 DWM 没画的**第 4 条边**，与左/右/下三条边同性质 —— 所以它属于窗口框架。
但最初它被实现在 `ChromeForm`（标题栏绘制）里，于是有两个真实后果：

1. **纯 `ChromeFrame` 子类拿不到它**：没有开关、没有颜色、没有最大化判定，使用者必须自己
   重测一遍 DWM 的混合参数（那套参数是本项目花了几轮才反解出来的）；
2. **`ShowDefaultTitleBar = false` 时线静默消失**：完全自定义标题栏的代码路径直接 `return`，
   跳过了绘制。使用者想要的是自定义**标题栏内容**，未必想连窗口边框一起接管 ——
   结果是 Win10 上顶部缺一条边，而且没有任何提示。

现在的归属：

| 成员 | 位置 |
| --- | --- |
| `TopBorderLineActiveColor` / `TopBorderLineInactiveColor` / `ShowTopBorderLine` | `ChromeFrame`（公开属性） |
| `ShouldDrawTopBorderLine`（开关 && 非最大化） | `ChromeFrame`（可覆盖） |
| `DrawTopBorderLine(graphics, active?)` | `ChromeFrame`（默认实现，`ChromeForm` 自动调用） |

配套约定：

- `ChromeForm` 在**所有**路径下都调用 `DrawTopBorderLine`，包括 `ShowDefaultTitleBar = false`。
  该路径的注释与文档都写明了这一点；不想要就 `ShowTopBorderLine = false`；
- 它画在**最上层**（与 WPF 覆盖层一致），无论按钮或自定义内容画到哪里都不会被盖住；
- 纯 `ChromeFrame` 子类在自己的 `OnPaint` 里调一次 `DrawTopBorderLine(graphics)` 即可，
  不必重测参数。

**这里有一个必须警惕的坑（本此改动中实际踩到并修掉）**：判定"是否最大化"必须用
**缓存的 `Metrics.Maximized`**，不能用实时查询的 `IsMaximized`。因为按钮的绘制矩形
（`CaptionButtonTop` / `CaptionHitHeight`）是由那个缓存值算出来的 —— 两者必须**同源**。
若顶线用实时值、按钮用缓存值，最大化/还原的那一帧会出现「线已消失但按钮还少一行」或
「线已出现但按钮压住它」的错位。缓存值在 `WM_SIZE` 中更新，早于随后的重绘。

### 5.7 线的可见性矩阵

| 系统 | 顶部 1px 由谁画 | 自绘线是否可见 |
| --- | --- | --- |
| Windows 11 | DWM（盖在我们那行之上） | 不可见（无副作用） |
| Windows 10 | 我们自己 | 可见 —— **所有顶线问题只在这里暴露** |

**结论：验证顶线必须用 Win10。** 在 Win11 上无论怎么改都是"看起来没问题"。

---

## 6. 标题栏预置样式（`ChromeTitleBarStyle`）

三套样式在赋值时把几何与配色一次性套用，之后单独改属性以属性为准。

| 样式 | 标题栏高 | 按钮 | 图标位置 | 标题 | 默认配色 |
| --- | --- | --- | --- | --- | --- |
| `Chrome`（默认） | 40 | 最小化 45 / 最大化 46 / 关闭 46，高 39 | 12px 位 | 贴左 | 跟随系统明暗 |
| `VsCode` | 35 | 46×34 | 12px 位 | 贴左 | 固定深色 `#191A1B` |
| `Windows` | 31 | 45×31（视觉格子） | 贴左 8px | 贴左 | 跟随系统明暗 |

### 6.1 为什么是「样式 + 配色」两个轴，而不是 `ChromeForElementPlus`

标题栏的外观其实是两件独立的事：**几何**（标题栏多高、按钮多大、图标在哪）与
**配色**（底色、文字、按钮填充）。最初的枚举把两者绑在一张表里，于是想换品牌配色时
只有两条路：要么加枚举成员，要么使用者自己抄一遍颜色赋值。

加成员的问题是**相乘**：3 种几何 × N 种品牌配色 = 3N 个成员，每加一个品牌就要多三个；
枚举会慢慢变成一个被拍平的二维矩阵。而换品牌配色时几何**一点没变** ——
它本来就不是另一种"样式"，把它叫做样式会让枚举的语义变浑。

所以拆成两个正交的属性：

```csharp
form.TitleBarStyle   = ChromeTitleBarStyle.VsCode;            // 几何
form.TitleBarPalette = ChromeTitleBarPalette.ElementPlus;     // 配色
```

| `TitleBarPalette` | 含义 |
| --- | --- |
| `Default` | 该样式自带的那套（三套的红各不相同，见下） |
| `ElementPlusPrimary` | Element Plus 主色：`#409EFF` 当底、白字 |
| `ElementPlusDark` | Element Plus 深色主题：`#141414` 底 |
| `ElementPlusNeutral` | 浅色中性底 `#FFFFFF`，只在按钮上出主色 |

**配色值不依赖样式**：同一套配色配到哪套骨架上都是同样的颜色（有测试断言这一点）。
只有 `Default` 例外 —— 它的定义就是"该样式自带的那套"，所以只有那一个分支需要样式参数。
3 种骨架 × 4 种配色 = 12 种组合。

两条实现契约：

1. **两个轴谁后赋值都成立**。`TitleBarStyle` 的 setter 会按*当前*配色来源上色，
   `TitleBarPalette` 的 setter 会按*当前*样式上色 —— 两条路殊途同归。
   测试 `StyleAndPaletteComposeInEitherOrder` 断言两种顺序得到完全一致的状态。
2. **改配色不动几何**。`ApplyTitleBarPalette` 只写颜色属性，
   `ApplyTitleBarGeometry` 只写几何与布局开关，两者互不调用。
   测试 `ElementPlusPaletteChangesOnlyTheColours` 逐款断言几何在换配色前后完全相等。

### 6.2 关闭按钮的红必须随配色走，不能只看样式

这是实现时最容易踩的一点：关闭按钮的颜色最初是**按样式分支**算的
（`if (style == Windows) … else if (style == VsCode) … else …`），
因为调色板结构体里只有六个字段、不含关闭色。加上配色轴后这就不够了 ——
同一个样式在不同配色下需要不同的红：

| | 悬停 | 按下 | 特点 |
| --- | --- | --- | --- |
| `Chrome` / `Default` | `#E81123` | `#F1707A` | 按下**变亮**（Chrome 自身行为） |
| `Windows` / `Default` | `#C42B1C` | `#A92316` | 按下变暗（原生实测） |
| `VsCode` / `Default` | `#E81123` | `#C50F1F` | 按下变深（它的样式表没有 `:active`） |
| `ElementPlus`（三款共用） | `#C42B1C` | `#A92316` | **直接用 Windows 原生那一对**，见下 |

所以关闭色被收进了一个完整的「配色」结构
（`ChromeTitleBarLook` = 调色板六色 + 关闭按钮两色），
`SystemTheme.Look(style)` 与 `ElementPlusTheme.Look(style)` 各自返回一套完整配色，
`ApplyTitleBarPalette` 不再需要任何样式分支。

### 6.3 Element Plus 的关闭按钮为什么用深红

`danger` 原色 `#F56C6C` 在浅色底上**太淡**，这是可量化的：

| 底色 | `#F56C6C` | Windows 原生 `#C42B1C` |
| --- | --- | --- |
| `primary #409EFF`（Chrome 款） | 1.04:1 | **2.04:1** |
| `#FFFFFF`（Windows 款） | 2.90:1 | **5.66:1**（正是原生实测值） |
| `#141414`（VS Code 款） | **6.35:1** ✅ | 3.25:1（偏暗，不用） |

`#F56C6C` 与 `#409EFF` 的**亮度几乎相同**（0.312 vs 0.328），只剩色相差，
所以蓝底上的红块看上去是"糊"的。

三款因此**共用同一对**：直接复用 Windows 原生那一对（悬停 `#C42B1C`、按下 `#A92316`）。
在白底上正好是原生实测的 5.66:1，在 primary 底上也比自己按 `danger` 色阶推的任何一档清楚
（2.04:1 对 1.57:1）。**用实测值比自己推色阶更有依据**，也少一套要维护的推导。

统一成同一对的理由是一致性：否则使用者得记住"深色款的红和浅色款不是同一个"。
代价是深色底上原生红比 EP 亮红略暗（3.25:1 对 6.35:1）——
按下的方向仍然正确（比悬停更深），而且与 `Windows` 样式完全是同一对值，三处不必分别记。

### 标题文字一律贴左

三套样式的标题都在**图标右侧、贴左**绘制，与真实 Chrome 和 WPF 版一致。

历史坑：WinForms 的 `Chrome` 样式曾经是 `MiddleCenter`，而 WPF 模板把标题 `TextBlock` 放在
图标之后的横向 `StackPanel` 里 —— 天然贴左。于是**两个库的同一套样式行为不一致**。

还要注意「居中」的语义陷阱：标题文字的绘制矩形是 `left = 图标右侧`、`right = 客户区宽 − 三按钮总宽`，
`MiddleCenter` 是在**这个矩形内**居中，而不是相对窗口居中。900px 窗口下这个矩形中心比窗口中心
偏左约 55px，看上去「既不像居中也不像贴左」。（标题居中这个词在仓库里已全部移除。）

### `VsCode` 的配色与其它两套隔离

`VsCode` 用**独立**的一份配色（`SystemTheme.VsCode`），不是复用 `SystemTheme.Dark`。

原因：`SystemTheme.Current` 在系统「应用模式」为深色时也会返回 `Dark`，而 `Chrome` / `Windows`
样式用的正是 `Current`。如果 `VsCode` 复用 `Dark`，那么调整 VS Code 的外观就会**连带改掉**
另外两套样式在深色系统下的样子。测试里有一条断言专门锁住这个隔离
（`Chrome` 不得与 `VsCode` 同色）。

配色取自 VS Code 自己的主题定义（`resources/app/extensions/theme-defaults/themes/2026-dark.json`）
与工作台样式表，比抓屏更可靠：

| 项 | 值 | 来源 |
| --- | --- | --- |
| `titleBar.activeBackground` | `#191A1B` | 主题文件 |
| `titleBar.inactiveBackground` | `#191A1B`（**与激活同色**） | 主题文件 |
| `titleBar.activeForeground` | `#8C8C8C` | 主题文件 |
| 按钮悬停 | `#ffffff1a`（白 10%）落在 `#191A1B` 上 ≈ `#2E2F30` | 工作台 CSS `.window-icon:hover` |
| 关闭按钮悬停 | `#e81123e6` | 工作台 CSS `.window-close:hover` |
| 关闭按钮按下 | 样式表**没有** `:active` 规则 → 取更深一档的红做反馈 | —— |

关闭按钮的红因此分成**三套**，不能合并：

| 样式 | 悬停 | 按下 | 特点 |
| --- | --- | --- | --- |
| `Windows` | `#C42B1C` | `#A92316` | 原生实测，按下变暗 |
| `Chrome` | `#E81123` | `#F1707A` | Chrome 自身行为，按下**变亮** |
| `VsCode` | `#E81123` | `#C50F1F` | 按下变暗（它的样式表没有 `:active`，同色会没有反馈） |

### 实测对齐 Chrome 的几处细节

- **最大化时按钮从客户区第 0 行铺满整条标题栏**（普通态从第 1 行起，让出顶线）；
- **三个按钮不等宽**：最小化 45、最大化/关闭 46（Chrome 自己就不等宽）。因此按钮定位
  必须**从右往左按各自宽度累计**，不能简单用 `index × 统一宽度`；
- **命中矩形与绘制矩形共用同一套顶边/高度值**，否则会出现「画在这里、点在那里」。

### 改配色不会动命中区

标题栏的**几何**（标题栏高、按钮宽高、图标边距、最小化按钮宽）与**配色**在
`ApplyTitleBarStyle` 里是分开赋值的两段。命中判定（`ChromeForm.Input.cs`、
`ChromeFrameGeometry.cs`）**只读几何**，对颜色属性零引用 —— 所以纯配色改动不可能改变命中区。

这一点有两层保障：一是上述代码结构，二是测试里 11 项（WinForms）+ 12 项（WPF）命中区断言，
以及每次改完在 Win10 里实扫 `WM_NCHITTEST` 分组复核。

---

## 7. 其他实现要点与坑

### 7.1 `WM_NCCALCSIZE` 早于 `WM_SIZE`

最大化/还原时 **`WM_NCCALCSIZE` 先于 `WM_SIZE` 到达**。若用缓存的窗口状态判断，会让
最大化那一帧少内缩 8px、还原那一帧多内缩 8px（客户区顶部落到窗口之外，DWM 把那 8px
当 frame 画成浅色带）。

**做法**：在 `WM_NCCALCSIZE` 里实时调用 `IsZoomed(hwnd)`，不用缓存标志。

### 7.2 `SWP_FRAMECHANGED` 不产生 `WM_SIZE`

WPF 初始化阶段客户区刚被 `WM_NCCALCSIZE` 改过，而 `SWP_FRAMECHANGED` 本身**不产生
`WM_SIZE`**，WPF 会继续按旧客户区布局（内容比客户区宽/高）。

**做法**：初始化时补发一次带真实客户区尺寸的 `WM_SIZE`。但**最大化/还原这类状态切换自带
真实的 `WM_SIZE`，补发反而会用过期尺寸覆盖** —— 所以只在初始化时补。

### 7.3 最大化时的位置交给系统默认

不要强行指定最大化的窗口位置。系统默认会给出"工作区 + 一个 frame"的结果，客户区正好落在
工作区上。强制指定会导致任务栏在屏幕任意边缘时铺不满。

### 7.4 最小跟踪尺寸要考虑标题栏

最小宽度要能放下「图标区 + 三个按钮 + 两侧 frame」，否则缩到最小时图标（系统菜单入口）
和标题被挤成 0。这些值应从窗口自身的属性推导（`CaptionButtonsWidth`、`CaptionLeadingWidth`），
不要把样式数值写死在测试里。

### 7.5 DPI 与亚像素

顶线厚度按 DPI 缩放：`topLine = 1 / DpiScaleY`。注意 1px 元素在非整数 DPI 下会被抗锯齿
摊成半透明混色 —— 这也是"看起来像带透明度"的原因之一，但**真正的透明度来自 5.4 的
alpha 设计**，两者不要混淆。

### 7.6 窗口图标与系统菜单

系统菜单的命中盒子用 `SM_CXSMSIZE × SM_CYSMSIZE`（96dpi 下 22×22），是**竖向居中的
正方形**，不是贯穿整个标题栏高度。图标 16×16 居其中。

---

## 8. 验证方法（本项目的实测手段）

因为顶线等行为**只在 Win10 可见**，本项目建立了在 Hyper-V Win10 虚拟机中实测的流程。
调试过程中的探针程序没有进仓库，方法记录在此备用。

### 8.1 判别技巧：给客户区染色

测量"某个区域到底是谁画的"时，把客户区染成**品红**。第 0 行的颜色直接给出结论：

- 品红 → 客户区（没人画）
- 纯白 → 系统按系统标题栏色填充（`DefWindowProc` 的非客户区绘制）
- 深灰且随底色变化 → DWM 的边框

### 8.2 用 `extFrameBounds` 而不是 `GetWindowRect` 定位

可见边框在 **`DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`** 上，与 `GetWindowRect`
差一个不可见边框的宽度（96dpi 下 7px）。按 `GetWindowRect` 采样会落在阴影里，量到的是
渐变色而不是边框 —— 本项目一度因此得出错误结论。

### 8.3 焦点状态要真实建立

计划任务启动的进程**拿不到前台权限**，`SetForegroundWindow` 会静默失败，
`Form.ActiveForm` 恒为 `null`，于是"聚焦态"其实一直是失焦态。

**做法**：用 `AttachThreadInput` 把当前线程的输入挂到前台线程上，再
`BringWindowToTop` + `SetForegroundWindow` + `SetFocus`；或用真实鼠标点击
（`SetCursorPos` + `mouse_event`）。

### 8.4 读窗口自己画出的表面

`PrintWindow(hwnd, hdc, 2)` 读的是**窗口最后绘制的表面**，不受遮挡影响，比屏幕截图可靠。
屏幕截图容易采到别的窗口（本项目一次把记事本当成了目标窗口）。

### 8.5 PowerShell 脚本必须是纯 ASCII

**Windows PowerShell 5.1 按 GBK 读取 `.ps1`** —— 脚本里的中文会让解析器报"字符串缺少
终止符"。给虚拟机用的探针脚本一律用 ASCII。

### 8.6 对照原生窗口

同一背景下并排放**我们的窗口**和**一个原生窗口**，逐边对比像素。判断"我们是否画得像"
必须与原生同屏比较，单看自己的窗口无法判断。

---

## 9. 快速自查清单

改动窗口框架/标题栏相关代码后，建议按此清单验证：

- [ ] **Win10** 上：白底 + Chrome 样式，聚焦时顶线 == 原生（112）；失焦 ≈ 163
- [ ] **Win10** 上：黑底 + VsCode 样式，聚焦时顶线 == 原生（25）
- [ ] 三套样式 × 聚焦/失焦，顶线都跟随标题栏明暗（半透明混色）
- [ ] 最大化：按钮铺满标题栏（第 0 行起）、无 1px 缝隙、顶部 8 行是 `HTCAPTION`
- [ ] 最大化再还原，标题栏不出现 1px 缝隙（`WM_NCCALCSIZE` 早于 `WM_SIZE`）
- [ ] 鼠标移到标题栏按钮上，顶线仍可见（按钮不能让出那一行）
- [ ] `ShowDefaultTitleBar = false` 的完全自定义窗口，顶线仍在（它是窗口边框的一部分）；
      不需要时用 `ShowTopBorderLine = false` 显式关掉
- [ ] 最大化/还原切换时，顶线的出现/消失与按钮的铺满/让位**同一帧**发生（同源判定）
- [ ] 窗口边缘外侧 8px（视觉上在阴影里）出现缩放光标；窗口矩形之外是 `HTNOWHERE`
- [ ] 缩放窗口，客户区内容跟手（`SWP_FRAMECHANGED` 的 `WM_SIZE` 补发）
- [ ] 多显示器 / 100%·125%·150%·200% DPI 下几何正确
- [ ] 相邻窗口不会被 resize 热区压住（本方案的核心收益）

---

## 10. 相关文档

- 项目总览与 API 用法：[`README.md`](../README.md)
- C++ 参考示例：[`samples/WindowChromeKit.Native.Sample/README.md`](../samples/WindowChromeKit.Native.Sample/README.md)
