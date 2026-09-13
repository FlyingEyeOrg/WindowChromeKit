// WindowChromeKit.Native.Sample
//
// 用纯 Win32 复刻 Google Chrome 在 Windows 上的窗口方案。核心三点：
//
//   1. 保留 WS_CAPTION | WS_THICKFRAME：窗口自己拥有原生 frame 区域。
//      DWM 会在这块区域里画出 1px 可见边线 + 7px 不可见 resize 边框，
//      并在窗口矩形之外绘制阴影 —— resize 带因此正好落在"阴影里"，
//      而且这些像素属于窗口本身，不是额外的辅助窗口。
//
//   2. WM_NCCALCSIZE 把客户区从窗口矩形内缩出 frame：客户区 == 可见窗口。
//      普通态内缩 (frameX, 0, frameX, frameY)：客户区顶边直接顶到窗口顶边，
//      顶部一个像素的非客户区都不留。这一点很关键 —— 只要顶部留了非客户区，
//      Windows 10 的 DWM 就会在带 WS_CAPTION 的窗口上画出一条 23px 的原生标题栏，
//      压在自绘标题栏上面（Win11 不会）。Chrome 在两种系统上都是客户区顶到顶边。
//      最大化时四边都内缩 frameY：系统已把最大化的窗口矩形外扩到工作区之外，
//      内缩后客户区正好等于工作区；此时不画顶边线（否则屏幕顶端会多一条深色线）。
//
//   3. WM_NCHITTEST 自己决定命中，优先级与各段宽度均按真实 Chrome 实测值：
//        窗口矩形之外              -> HTNOWHERE（Chrome 从不声明窗口外的像素）
//        左/右/下 8px 与四角       -> HTLEFT/HTRIGHT/HTBOTTOM/HTTOPLEFT/...
//        标题栏按钮                -> HTCLOSE / HTMAXBUTTON / HTMINBUTTON
//        顶部 6px                  -> HTTOP（比其余三边窄；按钮压过它）
//        标题栏空白                -> HTCAPTION（白送拖动、双击最大化、Aero Snap）
//        其余                      -> HTCLIENT
//
// 度量数据来源见同目录 README.md（对真实 Chrome 窗口的 WR_NCHITTEST 逐像素扫描）。

#include <windows.h>
#include <cwchar>
#include <windowsx.h>
#include <commctrl.h>
#include <dwmapi.h>
#include <strsafe.h>
#include <initializer_list>

#ifndef DWMWA_USE_IMMERSIVE_DARK_MODE
#define DWMWA_USE_IMMERSIVE_DARK_MODE 20
#endif

namespace
{

constexpr wchar_t kWindowClass[] = L"WindowChromeKit.Native.Sample";
constexpr wchar_t kWindowTitle[] = L"WindowChromeKit Native Sample";

// —— Chrome 实测的标题栏度量（DIP，使用时按窗口 DPI 缩放）——
constexpr int kCaptionHeightDip = 40; // 标题栏高度
constexpr int kButtonWidthDip = 46;   // 最小化/最大化/关闭按钮宽度（Chrome 实测 45/46/46）
constexpr int kButtonHeightDip = 39;  // 按钮高度，从标题栏顶部 +1px 开始
constexpr int kTopResizeBandDip = 6;  // 顶部缩放带：Chrome 实测 6px，明显窄于其余三边
constexpr int kIconMarginDip = 12;
constexpr int kFontPt = 9;

constexpr int kAppIconResourceId = 101;   // app.rc 中的应用图标
constexpr int kIdOpenWindow = 1001;
constexpr int kIdEdgeButton = 1002;

// —— 标题栏预置样式：与 WindowChromeKit.WinForms / WindowChromeKit.Wpf 的
//    ChromeTitleBarStyle 同一张表（几何 + 配色），三处必须一起改 ——
enum class TitleBarStyle
{
    Chrome,   // Chrome 实测：标题栏 40、按钮 46×39、图标 12px 位、跟随系统明暗
    VsCode,   // VS Code：标题栏 35、按钮 46×34、配色固定深色 #323233
    Windows,  // 贴近 Windows 11 原生：标题栏 32、按钮 44×32、图标贴左 3px 位、跟随系统
};

struct TitleBarStyleSettings
{
    int captionHeightDip;
    int buttonWidthDip;
    int buttonHeightDip;
    int iconMarginDip;
    COLORREF captionActive;
    COLORREF captionInactive;
    COLORREF captionText;
    COLORREF captionTextInactive;
    COLORREF buttonHot;
    COLORREF buttonPressed;
    COLORREF closeHot;
    COLORREF closePressed;
    bool darkFrame;   // DWM 的深色模式是否要打开
};

TitleBarStyleSettings SettingsFor(TitleBarStyle style)
{
    switch (style)
    {
        case TitleBarStyle::VsCode:
            return {35, 46, 34, 12, RGB(0x32, 0x32, 0x33), RGB(0x2D, 0x2D, 0x2D),
                    RGB(0xCC, 0xCC, 0xCC), RGB(0x9D, 0x9D, 0x9D),
                    RGB(0x50, 0x50, 0x50), RGB(0x5F, 0x5F, 0x5F),
                    RGB(0xE8, 0x11, 0x23), RGB(0xF1, 0x70, 0x7A), true};
        case TitleBarStyle::Windows:
            // 原生 WPF Window 实测（96dpi）：标题栏可见高 31、按钮 36×22
            // 关闭按钮红用原生实测值（悬停 #C42B1C）
            return {31, 33, 31, 8, RGB(0xFF, 0xFF, 0xFF), RGB(0xF1, 0xF3, 0xF4),
                    RGB(0x20, 0x21, 0x24), RGB(0x80, 0x86, 0x8B),
                    RGB(0xE8, 0xEA, 0xED), RGB(0xDA, 0xDC, 0xE0),
                    RGB(0xC4, 0x2B, 0x1C), RGB(0xA9, 0x23, 0x16), false};
        default:
            return {40, 46, 39, 12, RGB(0xFF, 0xFF, 0xFF), RGB(0xF1, 0xF3, 0xF4),
                    RGB(0x20, 0x21, 0x24), RGB(0x80, 0x86, 0x8B),
                    RGB(0xE8, 0xEA, 0xED), RGB(0xDA, 0xDC, 0xE0),
                    RGB(0xE8, 0x11, 0x23), RGB(0xF1, 0x70, 0x7A), false};
    }
}

TitleBarStyle g_titleBarStyle = TitleBarStyle::Chrome;
TitleBarStyleSettings g_style = SettingsFor(TitleBarStyle::Chrome);

// 浅色主题（白色标题栏）：文字与按钮描边都是深色，悬停底色是浅灰。
// DWM 的深色模式要同步关掉（见 ApplyChromeFrameAttributes），否则边线与阴影还是深色。
// 顶边线颜色。Win10 的 DWM 只在非客户区画边框，而我们把客户区顶到了窗口顶边，
// 所以顶边这条线必须自己补，颜色取 DWM 画在左/右/下三边的实测值，四条边才一致：
//   激活 rgb(112,112,112) / 失焦 rgb(170,170,170)
// Win11 上 DWM 会在同一行自己画边框覆盖它（纯红探针验证：0 个像素可见），因此对 Win11 无影响。
const COLORREF kTopBorderLineActive = RGB(0x70, 0x70, 0x70);
const COLORREF kTopBorderLineInactive = RGB(0xAA, 0xAA, 0xAA);
const COLORREF kContentBackground = RGB(0xFF, 0xFF, 0xFF);
const COLORREF kContentText = RGB(0x20, 0x21, 0x24);
const COLORREF kHintText = RGB(0x5F, 0x63, 0x68);

struct FrameMetrics
{
    UINT dpi = 96;
    int frameX = 8;       // SM_CXFRAME + SM_CXPADDEDBORDER：左右不可见边框
    int frameY = 8;       // SM_CYFRAME + SM_CXPADDEDBORDER：底部不可见边框
    int captionHeight = 40;
    int buttonWidth = 46;
    int buttonHeight = 39;
    int buttonTop = 1;      // 命中矩形用：按钮相对**窗口矩形**顶部的偏移
    int buttonPaintTop = 1; // 绘制用：按钮相对**客户区**顶部的偏移（比命中矩形少 1 行给顶边线）
    int topBand = 6;
    int iconSize = 16;
    int iconMargin = 12;
    // 系统菜单命中盒子：与原生标题栏同形状（SM_CXSMSIZE × SM_CYSMSIZE，96dpi 下 22×22），
    // 但中心对准画出来的图标，图标左边那点空白仍属于可拖动的标题栏
    int menuBoxLeft = 9;
    int menuBoxTop = 9;
    int menuBoxWidth = 22;
    int menuBoxHeight = 22;
};

struct WindowState
{
    FrameMetrics metrics{};
    HFONT uiFont = nullptr;
    HFONT titleFont = nullptr;
    HWND openWindowButton = nullptr;
    HWND edgeButton = nullptr;
    int hotPart = 0;
    int pressedPart = 0;
    bool trackingNonClient = false;
    int lastHit = HTCLIENT;
    bool lastHitValid = false;
    bool maximized = false;
};

int ScaleDip(int dip, UINT dpi) { return MulDiv(dip, static_cast<int>(dpi), 96); }

// SetProcessDpiAwarenessContext 只有 Windows 10 1703+ 才有。静态导入会让程序在更老的
// 系统上加载阶段就失败，所以这里动态取地址；取不到就依赖 manifest 里的 dpiAwareness 声明。
void EnablePerMonitorDpiAwareness()
{
    using SetProcessDpiAwarenessContextProc = BOOL(WINAPI*)(DPI_AWARENESS_CONTEXT);
    const auto setContext = reinterpret_cast<SetProcessDpiAwarenessContextProc>(
        GetProcAddress(GetModuleHandleW(L"user32.dll"), "SetProcessDpiAwarenessContext"));
    if (setContext != nullptr)
        setContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
}

// Per-Monitor DPI API 全部动态解析：GetDpiForWindow / GetDpiForSystem /
// GetSystemMetricsForDpi 都是 Windows 10 1607 才有的导出，静态导入会让 exe 在更老的
// 系统上加载阶段就报"找不到入口点"。取不到时统一回退到系统 DPI 与按比例缩放的系统度量。
struct DpiApi
{
    using GetDpiForWindowProc = UINT(WINAPI*)(HWND);
    using GetDpiForSystemProc = UINT(WINAPI*)();
    using GetSystemMetricsForDpiProc = int(WINAPI*)(int, UINT);

    GetDpiForWindowProc getDpiForWindow = nullptr;
    GetDpiForSystemProc getDpiForSystem = nullptr;
    GetSystemMetricsForDpiProc getSystemMetricsForDpi = nullptr;

    DpiApi()
    {
        const HMODULE user32 = GetModuleHandleW(L"user32.dll");
        getDpiForWindow = reinterpret_cast<GetDpiForWindowProc>(
            GetProcAddress(user32, "GetDpiForWindow"));
        getDpiForSystem = reinterpret_cast<GetDpiForSystemProc>(
            GetProcAddress(user32, "GetDpiForSystem"));
        getSystemMetricsForDpi = reinterpret_cast<GetSystemMetricsForDpiProc>(
            GetProcAddress(user32, "GetSystemMetricsForDpi"));
    }
};

const DpiApi& Dpi()
{
    static const DpiApi api;
    return api;
}

UINT SystemDpiFallback()
{
    HDC dc = GetDC(nullptr);
    const int dpi = dc != nullptr ? GetDeviceCaps(dc, LOGPIXELSX) : 96;
    if (dc != nullptr)
        ReleaseDC(nullptr, dc);
    return dpi > 0 ? static_cast<UINT>(dpi) : 96;
}

UINT WindowDpi(HWND window)
{
    const DpiApi& api = Dpi();
    const UINT dpi = api.getDpiForWindow != nullptr ? api.getDpiForWindow(window) : 0;
    return dpi != 0 ? dpi : SystemDpiFallback();
}

UINT SystemDpi()
{
    const DpiApi& api = Dpi();
    const UINT dpi = api.getDpiForSystem != nullptr ? api.getDpiForSystem() : 0;
    return dpi != 0 ? dpi : SystemDpiFallback();
}

int SystemMetricForDpi(int index, UINT dpi)
{
    const DpiApi& api = Dpi();
    if (api.getSystemMetricsForDpi != nullptr)
        return api.getSystemMetricsForDpi(index, dpi);
    return MulDiv(GetSystemMetrics(index), static_cast<int>(dpi), 96);
}

struct OsVersionInfo
{
    ULONG size;
    ULONG major;
    ULONG minor;
    ULONG build;
    ULONG platformId;
    WCHAR csd[128];
};

// 诊断用：当前系统构建号（取不到返回 0）。显示在内容区，便于在目标机上确认系统版本。
ULONG OsBuildNumber()
{
    using RtlGetVersionProc = LONG(WINAPI*)(OsVersionInfo*);
    const auto rtlGetVersion = reinterpret_cast<RtlGetVersionProc>(
        GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    OsVersionInfo info{};
    info.size = sizeof(info);
    if (rtlGetVersion != nullptr && rtlGetVersion(&info) == 0)
        return info.build;
    return 0;
}

FrameMetrics ComputeFrameMetrics(HWND window)
{
    FrameMetrics metrics;
    metrics.dpi = WindowDpi(window);
    if (metrics.dpi == 0)
        metrics.dpi = 96;
    metrics.frameX =
        SystemMetricForDpi(SM_CXFRAME, metrics.dpi) +
        SystemMetricForDpi(SM_CXPADDEDBORDER, metrics.dpi);
    metrics.frameY =
        SystemMetricForDpi(SM_CYFRAME, metrics.dpi) +
        SystemMetricForDpi(SM_CXPADDEDBORDER, metrics.dpi);
    metrics.captionHeight = ScaleDip(g_style.captionHeightDip, metrics.dpi);
    metrics.buttonWidth = ScaleDip(g_style.buttonWidthDip, metrics.dpi);
    metrics.buttonHeight = ScaleDip(g_style.buttonHeightDip, metrics.dpi);
    metrics.topBand = ScaleDip(kTopResizeBandDip, metrics.dpi);
    metrics.iconSize = SystemMetricForDpi(SM_CXSMICON, metrics.dpi);
    metrics.iconMargin = ScaleDip(g_style.iconMarginDip, metrics.dpi);
    metrics.menuBoxWidth = SystemMetricForDpi(SM_CXSMSIZE, metrics.dpi);
    metrics.menuBoxHeight = SystemMetricForDpi(SM_CYSMSIZE, metrics.dpi);
    metrics.menuBoxLeft = metrics.iconMargin - (metrics.menuBoxWidth - metrics.iconSize) / 2;
    // 与原生一致：盒子居中，但不高于标题栏内容区的起点（frame 内缩）
    metrics.menuBoxTop = (metrics.captionHeight - metrics.menuBoxHeight) / 2;
    if (metrics.menuBoxTop < metrics.frameY)
        metrics.menuBoxTop = metrics.frameY;

    // 顶边没有任何不可见边框，只有 DWM 画的可见边线。Win11 上 Chrome 让客户区
    // 直接顶到窗口顶部（DWM 把 1px 线画在客户区第一行上）；Win10 上不留这 1px
    // 会让顶边线整条消失，所以要把它留成非客户区。
    // 命中矩形（窗口坐标）：Chrome 实测无论普通态还是最大化，都在窗口矩形内 T+1 起、高 39px。
    // 最大化时窗口矩形比工作区高 8px，所以这条命中带在屏幕第 -7 行就开始（看得见的部分从第 0 行起）。
    // 按钮顶边：贴标题栏底部（留 1px），但不高于第 1 行 ——
    // Chrome 实测按钮顶到第 1 行（40 高 / 39 按钮），原生窄标题栏（31 高 / 22 按钮）
    // 则落在第 8 行，即 frame 内缩那条 content 线上
    if (metrics.buttonHeight >= metrics.captionHeight)
        metrics.buttonTop = 0;            // 铺满标题栏时贴顶
    else
    {
        metrics.buttonTop = metrics.captionHeight - 1 - metrics.buttonHeight;
        if (metrics.buttonTop < 1)
            metrics.buttonTop = 1;
    }
    metrics.buttonPaintTop = metrics.buttonTop > 0 ? metrics.buttonTop - 1 : 0;   // 第 0 行留给顶边线
    // 绘制位置（客户区坐标）：普通态客户区顶边 == 窗口顶边，按钮同样从第 1 行开始；
    // 最大化时客户区顶边落在工作区顶边，Chrome 实测把按钮块画在可见区第 0 行起
    // （字形中心落在屏幕第 19.5 行），所以这里用 0。
    metrics.buttonPaintTop = IsZoomed(window) ? 0 : 1;
    return metrics;
}

WindowState* StateOf(HWND window)
{
    return reinterpret_cast<WindowState*>(GetWindowLongPtrW(window, GWLP_USERDATA));
}

FrameMetrics MetricsOf(HWND window)
{
    const WindowState* state = StateOf(window);
    return state != nullptr ? state->metrics : ComputeFrameMetrics(window);
}

// 标题栏按钮在窗口坐标下的矩形。按钮贴着客户区右边缘排列。
RECT ButtonRect(const RECT& windowRect, const FrameMetrics& metrics, int part)
{
    const int right = windowRect.right - metrics.frameX;
    const int index = part == HTCLOSE ? 0 : (part == HTMAXBUTTON ? 1 : 2);
    RECT rect{};
    rect.right = right - index * metrics.buttonWidth;
    rect.left = rect.right - metrics.buttonWidth;
    rect.top = windowRect.top + metrics.buttonTop;
    rect.bottom = rect.top + metrics.buttonHeight;
    return rect;
}

// 弹出系统菜单，并在选择后把命令发回窗口（行为与原生标题栏图标一致）。
void ShowSystemMenu(HWND window)
{
    const HMENU menu = GetSystemMenu(window, FALSE);
    if (menu == nullptr)
        return;
    POINT cursor{};
    if (!GetCursorPos(&cursor))
        return;
    const UINT command = TrackPopupMenuEx(
        menu,
        TPM_LEFTALIGN | TPM_LEFTBUTTON | TPM_RETURNCMD,
        cursor.x,
        cursor.y,
        window,
        nullptr);
    if (command != 0)
        SendMessageW(window, WM_SYSCOMMAND, static_cast<WPARAM>(command), 0);
}

// 命中优先级：窗口内判断 -> 标题栏按钮 -> 三边与四角 -> 顶部带 -> 标题栏/客户区。
int HitTestFrame(HWND window, POINT screenPoint)
{
    RECT windowRect{};
    if (!GetWindowRect(window, &windowRect))
        return HTCLIENT;
    // Chrome 实测：窗口矩形之外的任何点都返回 HTNOWHERE，绝不声明别人的像素。
    if (!PtInRect(&windowRect, screenPoint))
        return HTNOWHERE;

    const FrameMetrics metrics = MetricsOf(window);
    const bool maximized = IsZoomed(window) != FALSE;

    // 1) 标题栏按钮优先于顶部缩放带（Chrome 实测：按钮覆盖 y = T+1 … T+39，
    //    只有最顶上 1 行让给顶部带）。返回 HTCLOSE/HTMAXBUTTON/HTMINBUTTON
    //    让 Windows 11 的 Snap Layouts 在悬停最大化按钮时正常弹出。
    for (const int part : {HTCLOSE, HTMAXBUTTON, HTMINBUTTON})
    {
        const RECT button = ButtonRect(windowRect, metrics, part);
        if (PtInRect(&button, screenPoint))
            return part;
    }

    // 2) 左/右/下三边与四角：正好是 frame 区域，普通态下它在窗口矩形内、可见窗口外，
    //    视觉上就是"阴影里那一圈"。
    const bool left = screenPoint.x < windowRect.left + metrics.frameX;
    const bool right = screenPoint.x >= windowRect.right - metrics.frameX;
    const bool bottom = screenPoint.y >= windowRect.bottom - metrics.frameY;
    const bool insideTopFrame = screenPoint.y < windowRect.top + metrics.frameY;
    if (left && insideTopFrame)
        return HTTOPLEFT;
    if (right && insideTopFrame)
        return HTTOPRIGHT;
    if (left && bottom)
        return HTBOTTOMLEFT;
    if (right && bottom)
        return HTBOTTOMRIGHT;
    if (left)
        return HTLEFT;
    if (right)
        return HTRIGHT;
    if (bottom)
        return HTBOTTOM;

    // 3) 顶部缩放带。最大化时纵向不能缩放，整块交给标题栏
    //    （Chrome 实测：最大化后顶部返回 HTCAPTION/HTCLIENT，可拖动还原）。
    if (!maximized && screenPoint.y < windowRect.top + metrics.topBand)
        return HTTOP;

    // 4) 自绘标题栏：图标区给系统菜单，其余可拖动；标题栏之下是客户区。
    const int captionBottom = windowRect.top + metrics.captionHeight;
    if (screenPoint.y < captionBottom)
    {
        // 系统菜单盒子（与原生同形状：正方形、竖向居中），中心对准图标
        const int boxLeft = windowRect.left + metrics.frameX + metrics.menuBoxLeft;
        // 纵向不要加 frameX：标题栏是从客户区顶边开始的（左/右才需要 frame 内缩）
        const int boxTop = windowRect.top + metrics.menuBoxTop;
        if (screenPoint.x >= boxLeft && screenPoint.x < boxLeft + metrics.menuBoxWidth
            && screenPoint.y >= boxTop && screenPoint.y < boxTop + metrics.menuBoxHeight)
        {
            return HTSYSMENU;
        }
        return HTCAPTION;
    }
    return HTCLIENT;
}

const wchar_t* HitName(int hit)
{
    switch (hit)
    {
        case HTNOWHERE: return L"HTNOWHERE";
        case HTCLIENT: return L"HTCLIENT";
        case HTCAPTION: return L"HTCAPTION";
        case HTSYSMENU: return L"HTSYSMENU";
        case HTMINBUTTON: return L"HTMINBUTTON";
        case HTMAXBUTTON: return L"HTMAXBUTTON";
        case HTCLOSE: return L"HTCLOSE";
        case HTLEFT: return L"HTLEFT";
        case HTRIGHT: return L"HTRIGHT";
        case HTTOP: return L"HTTOP";
        case HTTOPLEFT: return L"HTTOPLEFT";
        case HTTOPRIGHT: return L"HTTOPRIGHT";
        case HTBOTTOM: return L"HTBOTTOM";
        case HTBOTTOMLEFT: return L"HTBOTTOMLEFT";
        case HTBOTTOMRIGHT: return L"HTBOTTOMRIGHT";
        default: return L"HT?";
    }
}

void ApplyChromeFrameAttributes(HWND window)
{
    // 非客户区（frame）全部交给 DWM 渲染：阴影、可见边线、不可见 resize 边框、顶边那条
    // 1px 线都来自它，我们一条都不自己画 —— 与 Chrome 的配置完全一致。
    const DWMNCRENDERINGPOLICY policy = DWMNCRP_ENABLED;
    DwmSetWindowAttribute(window, DWMWA_NCRENDERING_POLICY, &policy, sizeof(policy));
    // DWM 的边线/阴影跟着标题栏明暗走（VS Code 样式是深色标题栏）
    BOOL dark = g_style.darkFrame ? TRUE : FALSE;
    DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(dark));
}

HFONT CreateUiFont(UINT dpi, int pointSize, int weight)
{
    return CreateFontW(
        -MulDiv(pointSize, static_cast<int>(dpi), 72),
        0,
        0,
        0,
        weight,
        FALSE,
        FALSE,
        FALSE,
        DEFAULT_CHARSET,
        OUT_TT_PRECIS,
        CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE,
        L"Segoe UI");
}

void FillRectColor(HDC dc, const RECT& rect, COLORREF color)
{
    HBRUSH brush = CreateSolidBrush(color);
    FillRect(dc, &rect, brush);
    DeleteObject(brush);
}

// 窗口图标：与原生标题栏/WPF 同样的取法 —— 先问窗口（WM_GETICON），再问窗口类，
// 最后回退系统默认图标。窗口类图标来自本例的 app.rc 资源（相当于 WPF 的 <ApplicationIcon>）。
HICON GetWindowSmallIcon(HWND window)
{
    if (const HICON icon = reinterpret_cast<HICON>(
            SendMessageW(window, WM_GETICON, ICON_SMALL2, 0)))
        return icon;
    if (const HICON icon = reinterpret_cast<HICON>(
            SendMessageW(window, WM_GETICON, ICON_SMALL, 0)))
        return icon;
    if (const HICON icon = reinterpret_cast<HICON>(GetClassLongPtrW(window, GCLP_HICONSM)))
        return icon;
    if (const HICON icon = reinterpret_cast<HICON>(GetClassLongPtrW(window, GCLP_HICON)))
        return icon;
    return LoadIconW(nullptr, IDI_APPLICATION);
}

void DrawCaptionGlyph(HDC dc, const RECT& button, int part, bool maximized, COLORREF color)
{
    const int cx = (button.left + button.right) / 2;
    const int cy = (button.top + button.bottom) / 2;
    const int half = 5;
    HPEN pen = CreatePen(PS_SOLID, 1, color);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    HGDIOBJ oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
    switch (part)
    {
        case HTMINBUTTON:
            MoveToEx(dc, cx - half, cy, nullptr);
            LineTo(dc, cx + half + 1, cy);
            break;
        case HTMAXBUTTON:
            if (maximized)
            {
                // 还原：两个错开的方框
                Rectangle(dc, cx - half, cy - half + 2, cx + half - 1, cy + half + 1);
                MoveToEx(dc, cx - half + 2, cy - half + 2, nullptr);
                LineTo(dc, cx - half + 2, cy - half);
                LineTo(dc, cx + half + 1, cy - half);
                LineTo(dc, cx + half + 1, cy + half - 2);
            }
            else
            {
                Rectangle(dc, cx - half, cy - half, cx + half + 1, cy + half + 1);
            }
            break;
        case HTCLOSE:
            MoveToEx(dc, cx - half, cy - half, nullptr);
            LineTo(dc, cx + half + 1, cy + half + 1);
            MoveToEx(dc, cx + half, cy - half, nullptr);
            LineTo(dc, cx - half - 1, cy + half + 1);
            break;
        default:
            break;
    }
    SelectObject(dc, oldBrush);
    SelectObject(dc, oldPen);
    DeleteObject(pen);
}

// 悬停/按下时填底色，并返回此时应该用的字形颜色；未悬停则原样返回传入的颜色。
COLORREF DrawButton(HDC dc, const RECT& rect, int part, int hotPart, int pressedPart, COLORREF glyph)
{
    const bool hot = hotPart == part;
    const bool pressed = pressedPart == part;
    if (!hot && !pressed)
        return glyph;
    const bool isClose = part == HTCLOSE;
    const COLORREF background = isClose ? (pressed ? g_style.closePressed : g_style.closeHot)
                                        : (pressed ? g_style.buttonPressed : g_style.buttonHot);
    FillRectColor(dc, rect, background);
    return isClose ? RGB(0xFF, 0xFF, 0xFF) : g_style.captionText;
}

void PaintWindow(HWND window, WindowState& state)
{
    PAINTSTRUCT paint{};
    HDC dc = BeginPaint(window, &paint);
    RECT client{};
    GetClientRect(window, &client);
    if (client.right <= 0 || client.bottom <= 0)
    {
        EndPaint(window, &paint);
        return;
    }

    HDC memory = CreateCompatibleDC(dc);
    HBITMAP bitmap = CreateCompatibleBitmap(dc, client.right, client.bottom);
    HGDIOBJ oldBitmap = SelectObject(memory, bitmap);
    const FrameMetrics& metrics = state.metrics;
    const bool active = GetActiveWindow() == window;
    const bool maximized = IsZoomed(window) != FALSE;

    // —— 自绘标题栏（客户区顶部 captionHeight 像素）——
    RECT caption{0, 0, client.right, metrics.captionHeight};
    FillRectColor(memory, caption, active ? g_style.captionActive : g_style.captionInactive);
    // 顶边 1px 边框线：非最大化时补上，让四条边一致（Win10 的 DWM 不会画这一条）。
    // 最大化时客户区正好等于工作区，画了会变成屏幕顶端的一条多余线，Chrome 最大化也没有。
    if (!maximized)
    {
        RECT topLine{0, 0, client.right, 1};
        FillRectColor(
            memory, topLine, active ? kTopBorderLineActive : kTopBorderLineInactive);
    }
    // 图标：取窗口自己的小图标（原生路径），尺寸用 DPI 相关的 SM_CXSMICON。
    // 图标在系统菜单命中盒子内居中（原生就是这么画的）
    const int iconTop = metrics.menuBoxTop + (metrics.menuBoxHeight - metrics.iconSize) / 2;
    if (const HICON icon = GetWindowSmallIcon(window))
    {
        DrawIconEx(
            memory,
            metrics.iconMargin,
            iconTop,
            icon,
            metrics.iconSize,
            metrics.iconSize,
            0,
            nullptr,
            DI_NORMAL);
    }

    // 标题文字
    wchar_t title[256]{};
    GetWindowTextW(window, title, ARRAYSIZE(title));
    RECT titleRect{
        metrics.iconMargin + metrics.iconSize + 8,
        0,
        client.right - metrics.buttonWidth * 3 - 8,
        metrics.captionHeight};
    SetBkMode(memory, TRANSPARENT);
    SetTextColor(memory, active ? g_style.captionText : g_style.captionTextInactive);
    HGDIOBJ oldFont = SelectObject(memory, state.titleFont);
    DrawTextW(memory, title, -1, &titleRect, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);

    // 三个按钮：位置与命中矩形一致（按钮贴着客户区右边缘）
    for (const int part : {HTMINBUTTON, HTMAXBUTTON, HTCLOSE})
    {
        const int index = part == HTCLOSE ? 0 : (part == HTMAXBUTTON ? 1 : 2);
        RECT button{};
        button.right = client.right - index * metrics.buttonWidth;
        button.left = button.right - metrics.buttonWidth;
        button.top = metrics.buttonPaintTop;
        button.bottom = button.top + metrics.buttonHeight;
        const COLORREF glyph = DrawButton(
            memory,
            button,
            part,
            state.hotPart,
            state.pressedPart,
            active ? g_style.captionText : g_style.captionTextInactive);
        DrawCaptionGlyph(memory, button, part, maximized, glyph);
    }

    // —— 内容区（标题栏之下）——
    // 客户区纯白、不画任何边框：窗口边缘那一圈（DWM 边框 + 自绘顶边线）要能一眼看清。
    RECT content{0, metrics.captionHeight, client.right, client.bottom};
    FillRectColor(memory, content, kContentBackground);

    RECT windowRect{};
    GetWindowRect(window, &windowRect);
    wchar_t text[1024]{};
    int y = content.top + 16;
    const int lineHeight = ScaleDip(20, metrics.dpi);
    SetTextColor(memory, kContentText);
    SelectObject(memory, state.uiFont);

    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"Google Chrome frame model — 保留 WS_THICKFRAME，客户区自绘标题栏");
    RECT line{content.left + 16, y, content.right - 16, y + lineHeight};
    DrawTextW(memory, text, -1, &line, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    y += lineHeight + 4;

    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"frame = SM_CXFRAME+SM_CXPADDEDBORDER = %d px    顶部缩放带 = %d px    客户区顶到窗口顶边",
        metrics.frameX,
        metrics.topBand);
    line = RECT{content.left + 16, y, content.right - 16, y + lineHeight};
    DrawTextW(memory, text, -1, &line, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    y += lineHeight;

    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"windowRect = %d,%d,%d,%d  (%dx%d)   ← 阴影画在它之外",
        windowRect.left,
        windowRect.top,
        windowRect.right,
        windowRect.bottom,
        windowRect.right - windowRect.left,
        windowRect.bottom - windowRect.top);
    line = RECT{content.left + 16, y, content.right - 16, y + lineHeight};
    DrawTextW(memory, text, -1, &line, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    y += lineHeight;

    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"clientRect = %d,%d,%d,%d  (%dx%d)   ← 客户区就是可见窗口，左右下各让出 %d px 给 frame",
        client.left,
        client.top,
        client.right,
        client.bottom,
        client.right - client.left,
        client.bottom - client.top,
        metrics.frameX);
    line = RECT{content.left + 16, y, content.right - 16, y + lineHeight};
    DrawTextW(memory, text, -1, &line, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    y += lineHeight;

    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"dpi = %u    OS build = %u    最大化 = %s    光标处 WM_NCHITTEST = %s",
        metrics.dpi,
        OsBuildNumber(),
        maximized ? L"是" : L"否",
        state.lastHitValid ? HitName(state.lastHit) : L"-");
    line = RECT{content.left + 16, y, content.right - 16, y + lineHeight};
    DrawTextW(memory, text, -1, &line, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    y += lineHeight + 8;

    SetTextColor(memory, kHintText);
    StringCchPrintfW(
        text,
        ARRAYSIZE(text),
        L"把鼠标移到窗口边缘 %d px 内：上面这行会变成 HTLEFT / HTRIGHT / HTBOTTOM / HTTOP / HTTOPLEFT…\n"
        L"这些像素在窗口矩形之内、可见窗口之外，正是阴影所在的那一圈。窗口矩形之外一律 HTNOWHERE。\n"
        L"拖动标题栏移动窗口，双击标题栏最大化/还原，悬停最大化按钮可看到 Windows 11 Snap Layouts。",
        metrics.frameX);
    line = RECT{content.left + 16, y, content.right - 16, content.bottom - 16};
    DrawTextW(memory, text, -1, &line, DT_WORDBREAK | DT_NOPREFIX);

    SelectObject(memory, oldFont);
    BitBlt(dc, 0, 0, client.right, client.bottom, memory, 0, 0, SRCCOPY);

    SelectObject(memory, oldBitmap);
    DeleteObject(bitmap);
    DeleteDC(memory);
    EndPaint(window, &paint);
}

void LayoutChildren(HWND window, WindowState& state)
{
    RECT client{};
    GetClientRect(window, &client);
    const int unit = ScaleDip(8, state.metrics.dpi);
    if (state.openWindowButton != nullptr)
    {
        const int width = ScaleDip(180, state.metrics.dpi);
        const int height = ScaleDip(32, state.metrics.dpi);
        SetWindowPos(
            state.openWindowButton,
            nullptr,
            unit * 2,
            client.bottom - height - unit * 2,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }
    if (state.edgeButton != nullptr)
    {
        // 刻意紧贴客户区右下角：证明客户区最外侧的像素仍然完全可交互
        // （它们在窗口矩形内 8px 处，frame 的 resize 带完全在客户区之外）。
        const int width = ScaleDip(200, state.metrics.dpi);
        const int height = ScaleDip(28, state.metrics.dpi);
        SetWindowPos(
            state.edgeButton,
            nullptr,
            client.right - width,
            client.bottom - height,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }
}

void ExecuteCaptionCommand(HWND window, int part)
{
    switch (part)
    {
        case HTMINBUTTON:
            SendMessageW(window, WM_SYSCOMMAND, SC_MINIMIZE, 0);
            break;
        case HTMAXBUTTON:
            SendMessageW(window, WM_SYSCOMMAND, IsZoomed(window) ? SC_RESTORE : SC_MAXIMIZE, 0);
            break;
        case HTCLOSE:
            SendMessageW(window, WM_SYSCOMMAND, SC_CLOSE, 0);
            break;
        default:
            break;
    }
}

void CreateSampleWindow(HWND owner);

LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    WindowState* state = StateOf(window);

    switch (message)
    {
        case WM_NCCREATE:
        {
            // 状态在窗口创建前分配，保证 WM_NCCALCSIZE 阶段就可用。
            auto* created = static_cast<WindowState*>(
                reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(created));
            state = created;
            ApplyChromeFrameAttributes(window);
            state->metrics = ComputeFrameMetrics(window);
            return DefWindowProcW(window, message, wParam, lParam);
        }

        case WM_NCCALCSIZE:
        {
            if (lParam == 0)
                break;
            const FrameMetrics metrics = MetricsOf(window);
            const bool maximized = IsZoomed(window) != FALSE;
            RECT* target = wParam != FALSE
                ? &reinterpret_cast<NCCALCSIZE_PARAMS*>(lParam)->rgrc[0]
                : reinterpret_cast<RECT*>(lParam);
            // 客户区 = 窗口矩形内缩出 frame 区域。frame 区域就是 DWM 画
            // 可见边线 + 不可见 resize 边框的地方，也是阴影带的来源。
            target->left += metrics.frameX;
            target->right -= metrics.frameX;
            target->bottom -= metrics.frameY;
            target->top += maximized ? metrics.frameY : 0;
            return 0;
        }

        case WM_NCHITTEST:
        {
            const POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
            const int hit = HitTestFrame(window, point);
            if (state != nullptr && (!state->lastHitValid || state->lastHit != hit))
            {
                state->lastHit = hit;
                state->lastHitValid = true;
                InvalidateRect(window, nullptr, FALSE);
            }
            return hit;
        }

        case WM_NCMOUSEMOVE:
        {
            if (state == nullptr)
                break;
            const int part = static_cast<int>(wParam);
            if (!state->trackingNonClient)
            {
                TRACKMOUSEEVENT track{sizeof(track), TME_LEAVE | TME_NONCLIENT, window, 0};
                state->trackingNonClient = TrackMouseEvent(&track) != FALSE;
            }
            if (state->pressedPart == 0 && state->hotPart != part)
            {
                state->hotPart = part;
                InvalidateRect(window, nullptr, FALSE);
            }
            break;
        }

        case WM_NCMOUSELEAVE:
            if (state != nullptr)
            {
                state->trackingNonClient = false;
                if (state->pressedPart == 0 && state->hotPart != 0)
                {
                    state->hotPart = 0;
                    InvalidateRect(window, nullptr, FALSE);
                }
            }
            break;

        case WM_NCLBUTTONDOWN:
        {
            if (state == nullptr)
                break;
            const int part = static_cast<int>(wParam);
            if (part == HTSYSMENU)
            {
                // 命中盒子是自绘的（以图标为中心），系统内部那句"只在贴标题栏左缘的
                // 系统菜单盒子里才弹菜单"会让盒子右侧点了毫无反应 —— 这里自己弹。
                ShowSystemMenu(window);
                return 0;
            }
            if (part == HTMINBUTTON || part == HTMAXBUTTON || part == HTCLOSE)
            {
                // 自己接管按压状态，行为与原生按钮一致：抬起时仍在同一按钮上才执行。
                state->pressedPart = part;
                state->hotPart = part;
                SetCapture(window);
                InvalidateRect(window, nullptr, FALSE);
                return 0;
            }
            break;
        }

        case WM_MOUSEMOVE:
        {
            if (state == nullptr)
                break;
            POINT cursor{};
            GetCursorPos(&cursor);
            const int hit = HitTestFrame(window, cursor);
            if (!state->lastHitValid || state->lastHit != hit)
            {
                state->lastHit = hit;
                state->lastHitValid = true;
                InvalidateRect(window, nullptr, FALSE);
            }
            if (state->pressedPart != 0)
            {
                const int part = hit;
                if (state->hotPart != part)
                {
                    state->hotPart = part;
                    InvalidateRect(window, nullptr, FALSE);
                }
            }
            break;
        }

        case WM_LBUTTONUP:
        {
            if (state == nullptr || state->pressedPart == 0)
                break;
            POINT cursor{};
            GetCursorPos(&cursor);
            const int released = HitTestFrame(window, cursor);
            const int pressed = state->pressedPart;
            state->pressedPart = 0;
            state->hotPart = released;
            ReleaseCapture();
            InvalidateRect(window, nullptr, FALSE);
            if (released == pressed)
                ExecuteCaptionCommand(window, pressed);
            return 0;
        }

        case WM_CAPTURECHANGED:
            if (state != nullptr && state->pressedPart != 0)
            {
                state->pressedPart = 0;
                state->hotPart = 0;
                InvalidateRect(window, nullptr, FALSE);
            }
            break;

        case WM_CREATE:
        {
            if (state == nullptr)
                break;
            state->maximized = IsZoomed(window) != FALSE;
            state->uiFont = CreateUiFont(state->metrics.dpi, kFontPt, FW_NORMAL);
            state->titleFont = CreateUiFont(state->metrics.dpi, kFontPt, FW_SEMIBOLD);
            state->openWindowButton = CreateWindowExW(
                0,
                WC_BUTTONW,
                L"打开第二个窗口（拖到旁边对齐）",
                WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
                0,
                0,
                10,
                10,
                window,
                reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdOpenWindow)),
                nullptr,
                nullptr);
            state->edgeButton = CreateWindowExW(
                0,
                WC_BUTTONW,
                L"贴客户区右下角（可点）",
                WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON,
                0,
                0,
                10,
                10,
                window,
                reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdEdgeButton)),
                nullptr,
                nullptr);
            for (HWND button : {state->openWindowButton, state->edgeButton})
            {
                if (button != nullptr)
                    SendMessageW(button, WM_SETFONT, reinterpret_cast<WPARAM>(state->uiFont), TRUE);
            }
            LayoutChildren(window, *state);
            return 0;
        }

        case WM_COMMAND:
            if (LOWORD(wParam) == kIdOpenWindow)
            {
                CreateSampleWindow(window);
                return 0;
            }
            if (LOWORD(wParam) == kIdEdgeButton)
            {
                RECT client{};
                GetClientRect(window, &client);
                RECT windowRect{};
                GetWindowRect(window, &windowRect);
                wchar_t info[512]{};
                StringCchPrintfW(
                    info,
                    ARRAYSIZE(info),
                    L"这个按钮紧贴客户区的右下角。\n\n"
                    L"客户区右下角在窗口矩形内 %d px 处，外面那圈 %d px 是原生 frame，\n"
                    L"也就是阴影里的 resize 带 —— 它属于本窗口，但压在客户区之外，\n"
                    L"所以不会挡住任何内容点击。",
                    MetricsOf(window).frameX,
                    MetricsOf(window).frameX);
                MessageBoxW(window, info, L"贴边按钮", MB_OK | MB_ICONINFORMATION);
                return 0;
            }
            break;

        case WM_SIZE:
            if (state != nullptr)
            {
                const bool maximized = IsZoomed(window) != FALSE;
                if (maximized != state->maximized)
                {
                    state->maximized = maximized;
                    // 按钮绘制偏移跟最大化状态相关，重算一次
                    state->metrics = ComputeFrameMetrics(window);
                }
                LayoutChildren(window, *state);
            }
            break;

        case WM_DPICHANGED:
        {
            if (state == nullptr)
                break;
            const RECT* suggested = reinterpret_cast<const RECT*>(lParam);
            SetWindowPos(
                window,
                nullptr,
                suggested->left,
                suggested->top,
                suggested->right - suggested->left,
                suggested->bottom - suggested->top,
                SWP_NOZORDER | SWP_NOACTIVATE);
            state->metrics = ComputeFrameMetrics(window);
            DeleteObject(state->uiFont);
            DeleteObject(state->titleFont);
            state->uiFont = CreateUiFont(state->metrics.dpi, kFontPt, FW_NORMAL);
            state->titleFont = CreateUiFont(state->metrics.dpi, kFontPt, FW_SEMIBOLD);
            for (HWND button : {state->openWindowButton, state->edgeButton})
            {
                if (button != nullptr)
                    SendMessageW(button, WM_SETFONT, reinterpret_cast<WPARAM>(state->uiFont), TRUE);
            }
            LayoutChildren(window, *state);
            InvalidateRect(window, nullptr, FALSE);
            return 0;
        }

        case WM_ACTIVATE:
        case WM_DWMCOMPOSITIONCHANGED:
        case WM_THEMECHANGED:
            if (state != nullptr)
                state->metrics = ComputeFrameMetrics(window);
            InvalidateRect(window, nullptr, FALSE);
            break;

        case WM_ERASEBKGND:
            return 1; // 全部在 WM_PAINT 里双缓冲绘制

        case WM_PAINT:
            if (state != nullptr)
                PaintWindow(window, *state);
            break;

        case WM_NCDESTROY:
        {
            if (state != nullptr)
            {
                SetWindowLongPtrW(window, GWLP_USERDATA, 0);
                if (state->uiFont != nullptr)
                    DeleteObject(state->uiFont);
                if (state->titleFont != nullptr)
                    DeleteObject(state->titleFont);
                delete state;
            }
            return DefWindowProcW(window, message, wParam, lParam);
        }

        case WM_CLOSE:
            DestroyWindow(window);
            return 0;

        case WM_DESTROY:
            if (GetWindow(window, GW_OWNER) == nullptr)
                PostQuitMessage(0);
            break;

        default:
            break;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

void CreateSampleWindow(HWND owner)
{
    // 级联摆放，方便把两个窗口拖到一起比较各自的 frame。
    RECT ownerRect{};
    int x = CW_USEDEFAULT;
    int y = CW_USEDEFAULT;
    if (owner != nullptr && GetWindowRect(owner, &ownerRect))
    {
        x = ownerRect.left + ScaleDip(32, WindowDpi(owner));
        y = ownerRect.top + ScaleDip(32, WindowDpi(owner));
    }

    auto* state = new WindowState();
    const UINT dpi = owner != nullptr ? WindowDpi(owner) : SystemDpi();
    const int frameX =
        SystemMetricForDpi(SM_CXFRAME, dpi) + SystemMetricForDpi(SM_CXPADDEDBORDER, dpi);
    const int frameY =
        SystemMetricForDpi(SM_CYFRAME, dpi) + SystemMetricForDpi(SM_CXPADDEDBORDER, dpi);
    const int width = ScaleDip(760, dpi) + frameX * 2;
    const int height = ScaleDip(520, dpi) + frameY;

    HWND window = CreateWindowExW(
        0,
        kWindowClass,
        kWindowTitle,
        // 与 Chrome 完全一致的顶层样式集合：保留 caption 与 thick frame，
        // 系统菜单 / 最小化 / 最大化 / Snap / 动画 / 阴影都来自这里。
        WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
        x,
        y,
        width,
        height,
        nullptr,
        nullptr,
        GetModuleHandleW(nullptr),
        state);
    if (window == nullptr)
    {
        delete state;
        return;
    }
    ShowWindow(window, SW_SHOW);
    UpdateWindow(window);
}

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, LPWSTR commandLine, int)
{
    EnablePerMonitorDpiAwareness();

    // --style=chrome|vscode|windows：切换标题栏预置样式。
    // 这张表与 WindowChromeKit.WinForms / WindowChromeKit.Wpf 的 ChromeTitleBarStyle 一致。
    if (commandLine != nullptr)
    {
        if (wcsstr(commandLine, L"vscode") != nullptr)
            g_titleBarStyle = TitleBarStyle::VsCode;
        else if (wcsstr(commandLine, L"windows") != nullptr)
            g_titleBarStyle = TitleBarStyle::Windows;
        g_style = SettingsFor(g_titleBarStyle);
    }

    WNDCLASSEXW windowClass{};
    windowClass.cbSize = sizeof(windowClass);
    windowClass.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
    windowClass.lpfnWndProc = WindowProcedure;
    windowClass.hInstance = instance;
    // 窗口图标来自 app.rc（原生用法，等价于 WPF 的 <ApplicationIcon>）
    windowClass.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(kAppIconResourceId));
    windowClass.hIconSm = reinterpret_cast<HICON>(LoadImageW(
        instance,
        MAKEINTRESOURCEW(kAppIconResourceId),
        IMAGE_ICON,
        GetSystemMetrics(SM_CXSMICON),
        GetSystemMetrics(SM_CYSMICON),
        LR_DEFAULTCOLOR));
    windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    windowClass.lpszClassName = kWindowClass;
    if (RegisterClassExW(&windowClass) == 0)
        return 1;

    CreateSampleWindow(nullptr);

    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0)
    {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return static_cast<int>(message.wParam);
}
