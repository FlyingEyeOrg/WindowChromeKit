# 视觉验证清单

改动窗口框架、命中判定或标题栏绘制之后，按本文验证。清单分两部分：

- **第 1 部分：自动化**（单元测试 + VM 实测脚本）—— 可重复执行，给出像素级证据；
- **第 2 部分：人工目视** —— 自动化测不到的交互手感。

> **为什么必须有第 1.3 节的 Win10 环节**：顶部那 1px 边框线在 Windows 11 上由 DWM
> 覆盖，**本机永远看不出问题**。历史上所有顶线缺陷都只在 Win10 暴露。
> 详见 [`native-frame-model.md`](native-frame-model.md) 第 5 节。

---

## 1. 自动化验证

### 1.1 单元测试（宿主机，任意系统）

```bash
cd /mnt/d/Project/FlyingEye/WindowChromeKit

'/mnt/c/Program Files/dotnet/dotnet.exe' test \
  tests/WindowChromeKit.WinForms.Tests/WindowChromeKit.WinForms.Tests.csproj -c Debug --nologo

'/mnt/c/Program Files/dotnet/dotnet.exe' test \
  tests/WindowChromeKit.Wpf.Tests/WindowChromeKit.Wpf.Tests.csproj -c Debug --nologo
```

**通过标准**：WinForms **16/16**、WPF **35/35**，0 失败。

覆盖的关键契约（回归时重点看这些不红）：

| 测试 | 守住什么 |
| --- | --- |
| `CaptionButtonHitAreaStaysAlignedWhenMaximized` | 最大化时按钮命中区不偏移 |
| `CaptionButtonHoverKeepsTheTopBorderLine` | hover 不盖住第 0 行的顶线 |
| `CaptionButtonFillCoversTheTopRowWhenMaximized` | 最大化时按钮铺满、不漏 1px 底色 |
| `RowBelowCaptionButtonIsCaption` | 按钮高度没被拉长 |
| `TopBorderLineHasDistinctActiveAndInactiveColours` | 顶线半透明 + 四场景混色值 |
| `TopBorderLineSurvivesAFullyCustomTitleBar` | 完全自定义标题栏时顶线仍在；关掉真没了 |
| `TopBorderLineMembersLiveOnTheFrameBase` | 顶线成员声明在 `ChromeFrame` |
| `VsCodeStyleUsesItsOwnPaletteAndLeavesChromeAlone` | VsCode 配色与 Chrome/Windows 隔离 |
| `WindowsStylePlacesTheIconLikeANativeCaption` | 图标居中与原生度量 |
| `NativeMinimumAndMaximumUseCurrentMonitorWorkArea` | 最小宽度含图标区+按钮 |

### 1.2 完整构建

```bash
'/mnt/d/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe' \
  WindowChromeKit.sln /p:Configuration=Release /p:Platform=x64 /nologo /v:minimal
```

**通过标准**：0 error、0 warning。

### 1.3 VM 像素验证（Windows 10 客户机）

这是唯一能看见顶线真实颜色的环节。需要 **Hyper-V 客户机 `Win10-Test` 已登录**
（无登录会话时计划任务无法在交互桌面运行）。

**前置：提权通道**（宿主机需要管理员权限才能用 PowerShell Direct）。
若 `WCKElev` 任务不存在，先注册（会弹一次 UAC）：

```powershell
# 宿主机，管理员
schtasks /create /tn WCKElev /xml C:\Users\admin\AppData\Local\Temp\WCKElev.xml /f
```

之后每次只需（**不再弹 UAC**）：

```powershell
schtasks /run /tn WCKElev      # 它执行 elev-cmd.ps1，输出写到 elev-out-<时间戳>.txt
```

**VM 凭据**（`elev-cmd.ps1` 里使用）：

```
VM 名  : Win10-Test
用户名 : shawman.private@hotmail.com
密码   : 19970218*Lxf
```

**客户机侧环境**：

- `C:\WCK` —— 探针程序与结果文件的工作目录（net48 编译，客户机只有 .NET Framework 4.8）
- 交互桌面投送：计划任务 + `LogonType Interactive` + `RunLevel Highest`
- 结果读取：写文件后用 `Copy-Item -FromSession` 取回
  
**关键约束**（踩过的坑，见 `native-frame-model.md` 第 8 节）：

- `PrintWindow` 对本库窗口**返回全黑**，必须用 `CopyFromScreen`（需交互桌面）；
- PowerShell Direct 在 **Session 0**，`CopyFromScreen` 会报"句柄无效"——**必须**走计划任务；
- `.ps1` 探针文件里**不能有中文**（PowerShell 5.1 按 GBK 读，会解析失败）。

**采样点必须避开图标笔画**：按钮中心正是图标所在位置，测 hover / pressed 填充要取
**按钮角落**（例如 `按钮左缘 + 5px`），否则读到的是图标颜色而不是填充色。

典型误判：`Chrome` 样式的图标色是 `#202124`（= `(32,33,36)`），若在按钮中心采样，
hover 前后都读到 `(32,33,36)`，会误以为"hover 没生效"。

**测焦点切换必须先制造一次真实的状态转换**：如果窗口本来就在前台，
`SetForegroundWindow` 不构成 `WM_ACTIVATE` 转换，那一帧可能仍是失活外观。
正确顺序是「先让**另一个窗口**取得焦点 → 再切回目标窗口 → 采样」。
判定实验（4 轮循环）与单次采样混在一起时，单次那些容易读出失活色而误报产品缺陷。

**建议**：优先使用「先失焦、再激活、再采样、最后 `Refresh()` 复核」的四步循环；
若 `Refresh()` 前后颜色一致，就说明不是陈旧重绘问题。

### 1.4 期望值对照表

三套样式逐项（96dpi、浅色系统主题下）。这些值取自代码中的调色板，
是**回归时的比对基准**。

#### 标题栏底色与顶线

| 样式 | 状态 | 标题栏底色 | 顶线（DWM 混合后） |
| --- | --- | --- | --- |
| `Chrome` | 聚焦 | `(255,255,255)` | `(112,112,112)` |
| `Chrome` | 失焦 | `(241,243,244)` | `(163,164,165)` |
| `VsCode` | 聚焦 | `(25,26,27)` | `(34,34,34)` |
| `VsCode` | 失焦 | `(25,26,27)` | `(55,56,56)` |
| `Windows` | 聚焦 | `(255,255,255)` | `(112,112,112)` |
| `Windows` | 失焦 | `(241,243,244)` | `(163,164,165)` |

顶线颜色是**半透明基色**，绘制时与标题栏底色混合：

```
线色 = alpha × 基色 + (1 - alpha) × 标题栏底色
聚焦 #262626 @ 66%      失焦 #565656 @ 50%
```

#### 按钮悬停 / 按下填充

| 样式 | hover（最小化/最大化） | pressed（最小化/最大化） | 关闭 hover | 关闭 pressed | 特点 |
| --- | --- | --- | --- | --- | --- |
| `Chrome` | `(232,234,237)` | `(218,220,224)` | `(232,17,35)` | `(241,112,122)` | 关闭 pressed **变亮** |
| `VsCode` | `(46,47,48)` | `(58,59,60)` | `(232,17,35)` | `(197,15,31)` | 关闭 pressed **变深** |
| `Windows` | `(232,234,237)` | `(218,220,224)` | `(196,43,28)` | `(169,35,22)` | 关闭用原生实测值 |

> VsCode 的按钮填充比标题栏亮一档（`#2E2F30` 叠在 `#191A1B` 上），
> 这是 VS Code 工作台样式表 `.window-icon:hover { background-color: #ffffff1a }` 的效果。
>
> **关闭按钮的红分三套，不能合并**：Chrome 真实行为的按下是**变亮**的粉红，
> 而 VS Code 与 Windows 的按下是**变深**。单元测试
> `VsCodeStyleUsesItsOwnPaletteAndLeavesChromeAlone` 守住这个区分。

#### 标题栏文字颜色

| 样式 | 聚焦 | 失焦 |
| --- | --- | --- |
| `Chrome` | `(32,33,36)` `#202124` | `(128,134,139)` `#80868B` |
| `VsCode` | `(140,140,140)` `#8C8C8C` | `(140,140,140)` `#8C8C8C` |
| `Windows` | `(32,33,36)` `#202124` | `(128,134,139)` `#80868B` |

#### `Chrome` / `Windows` 在系统深色模式下的取值

这两套样式跟随系统"应用模式"。上表给的是**浅色系统**下的值；系统为深色时取：

| 项 | 值 |
| --- | --- |
| 标题栏（聚焦 / 失焦） | `(50,50,51)` `#323233` / `(45,45,45)` `#2D2D2D` |
| 文字（聚焦 / 失焦） | `(204,204,204)` `#CCCCCC` / `(157,157,157)` `#9D9D9D` |
| 按钮 hover / pressed | `(80,80,80)` `#505050` / `(95,95,95)` `#5F5F5F` |

> `VsCode` **不受系统明暗影响**，永远是 `#191A1B`。它用的是**独立**的一套调色板
> （`SystemTheme.VsCode`），刻意不复用深色模式那套 —— 否则调整 VS Code 外观会连带
> 改掉 `Chrome` / `Windows` 在深色系统下的样子。

#### `TitleBarPalette = ElementPlus` 的取值

与上面的预置配色**并列**的另一轴（`TitleBarStyle` 之外的第二个属性）。三套几何各一款：

| 样式 | 激活底 | 失活底 | 文字 | 普通按钮 hover/pressed | 关闭 hover/pressed |
| --- | --- | --- | --- | --- | --- |
| `Chrome` | `#409EFF` | `#79BBFF` | `#FFFFFF` / `#ECF5FF` | `#79BBFF` / `#337ECC` | `#C42B1C` / `#A92316` |
| `VsCode` | `#141414` | `#1D1E1F` | `#E5EAF3` / `#A3A6AD` | `#303030` / `#424243` | `#C42B1C` / `#A92316` |
| `Windows` | `#FFFFFF` | `#F2F6FC` | `#303133` / `#909399` | `#ECF5FF` / `#D9ECFF` | `#C42B1C` / `#A92316` |

**三款共用一个关闭按钮**：直接复用 Windows 原生那一对（悬停 `#C42B1C`、按下 `#A92316`，
实测值），不按底色分支。

**回归要点**（两个轴的正交性）：

- [ ] 换 `TitleBarPalette` 时**几何一个都不变**（标题栏高、按钮宽高、图标边距、最小化按钮宽、标题对齐）
- [ ] 先设样式后设配色、与先设配色后设样式，最终状态**完全一致**
- [ ] 切回 `Default` 能还原该样式自带的配色，**包括关闭按钮的红**
      （Chrome 款按下变亮、Windows 款变暗、VS Code 款变深）
- [ ] 三款 ElementPlus 的关闭按钮与 Windows 样式用**同一对**红（`#C42B1C` / `#A92316`）

#### 几何与排版

| 样式 | 标题栏高 | 按钮 | 图标位 | 标题 |
| --- | --- | --- | --- | --- |
| `Chrome` | 40 | 最小化 45 / 其余 46，高 39 | 12px | 贴左 |
| `VsCode` | 35 | 46×34 | 12px | 贴左 |
| `Windows` | 31 | 45×31 | 贴左 8px | 贴左 |

**三套样式的标题一律贴左**（图标右侧）。`CaptionTextAlignment` 默认 `MiddleLeft`。

> 历史坑：WinForms 的 `Chrome` 曾是 `MiddleCenter`，而 WPF 模板里标题跟在图标后的横向
> `StackPanel` 中、天然贴左 —— 两库行为曾经不一致。另外 `MiddleCenter` 是在
> 「图标右侧 → 按钮左侧」这个矩形内居中，900px 窗口下比窗口中心**偏左约 55px**，
> 看起来既不像居中也不像贴左。

#### 命中区（`WM_NCHITTEST`，VsCode 样式实测样例）

```
35px 标题栏、窗口位于 (150,150)：
  顶部缩放带  y150        (1px, HTTOP)
  关闭按钮    y151..184   (h34, HTCLOSE)
  按钮横排    MIN 46 | MAX 46 | CLOSE 46（从右往左）
  标题栏之下  HTCLIENT
```

#### 最大化

| 项 | 期望 |
| --- | --- |
| 顶线 | **不画**（画了会在屏幕顶端多一条） |
| 按钮 | 从客户区**第 0 行**铺满整条标题栏 |
| 客户区 | 正好等于工作区（`extFrameBounds == work area`） |
| 窗口矩形外那 8px | `HTCAPTION`（不是 `HTTOP`） |

---

## 2. 人工目视清单

自动化测不到的交互手感，需要人或人眼看：

- [ ] **拖动标题栏**移动窗口；**双击**标题栏最大化/还原
- [ ] 窗口左上角**图标单击**弹系统菜单、**双击**关闭窗口
- [ ] 悬停**最大化按钮**弹出 Windows 11 **Snap Layouts**
- [ ] **四边 + 四角**缩放：鼠标移到窗口外侧约 8px（视觉上在阴影里）出现缩放光标；
      窗口矩形之外是普通箭头（不声明别人的像素）
- [ ] 缩放时**客户区内容跟手**，不出现内容比客户区大的错位
- [ ] **相邻窗口**贴近时不会被对方的 resize 热区压住
- [ ] **阴影**四边连续，四角是系统圆角，无硬边/黑边
- [ ] 最大化后**任务栏**在屏幕任意边缘时都正确铺满
- [ ] **多显示器**（含负坐标）居中与工作区约束正确
- [ ] **DPI 100% / 125% / 150% / 200%** 下标题栏几何与文字清晰度正常
- [ ] 深色系统主题下，`Chrome` / `Windows` 样式取深色配色、`VsCode` 保持自己的深色
      （`VsCode` 不应受系统明暗影响）

### 针对本库特有行为的专项

- [ ] `ShowDefaultTitleBar = false` 的完全自定义窗口：**顶线仍在**（它是窗口边框的一部分），
      需要去掉时设 `ShowTopBorderLine = false`
- [ ] 继承 `ChromeFrame` 的窗口：在 `OnPaint` 里调一次 `DrawTopBorderLine(graphics)`，
      顶线与 `ChromeForm` 画出的**颜色一致**
- [ ] 最大化 ↔ 还原切换时，顶线的出现/消失与按钮的铺满/让位**同一帧**发生
      （不得出现"线没了但按钮还少一行"）

---

## 3. 验证记录模板

每次回归后把结果记在下面（或另开一节），便于回溯。

```
日期：
改动摘要：
构建：0 error / 0 warning
单测：WinForms __/16   WPF __/35
VM   ：Win10-Test 已登录？ Y/N
       三套样式 × 聚焦/失焦 顶线 ：
       标题对齐（应全为贴左）：
       hover / pressed ：
       命中区扫描 ：
人工 ：
遗留：
```

**最近一次完整回归**（本文档创建时，Win10-Test 客户机）：

```
构建：0 error / 0 warning
单测：WinForms 16/16   WPF 35/35
VM  ：顶线 6/6        三套样式 × 普通/最大化，普通态有线、最大化无线
      标题贴左 6/6    三套样式 × 两种状态，全部 MiddleLeft
      标题栏配色 6/6  Chrome (255,255,255)/(241,243,244)、VsCode (25,26,27)×2、
                      Windows (255,255,255)/(241,243,244)
      hover/pressed  用按钮角落采样，8/8 精确命中期望值
      焦点切换       四步循环 × 4 轮全部正确，Refresh() 前后一致（无陈旧重绘）
人工 ：未完整执行
备注 ：同轮里按钮中心采样的探针（fullcheck.exe）在 Chrome 样式的
      hover MIN 与 norm FOCUS 两项上读出误导值，均确认为探针缺陷 ——
      详见 1.3 节的两条采样 gotcha。产品侧无问题。
```

---

## 4. 相关文档

- 技术方案与踩坑记录：[`native-frame-model.md`](native-frame-model.md)
- 项目总览与 API 用法：[`README.md`](../README.md)
