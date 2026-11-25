using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    private void ApplyResizeMode()
    {
        _input.PrepareResizeModeChange();
        CommandManager.InvalidateRequerySuggested();
        // ResizeMode 会改变缩放能力与最小尺寸，而 WPF 改样式位是异步落在原生窗口上的 ——
        // 这里安排一次 frame 重算，保证命中带、最小尺寸与新状态同源。
        _frame?.ScheduleNativeFrameRefresh();
    }

    protected override void OnFrameDpiChanged() => UpdateDpiVisuals();

    /// <summary>
    /// 样式位变化后重算 frame：<c>ResizeMode</c> 会改变缩放能力，
    /// 而命中判定与最小尺寸都读 <see cref="ChromeFrame.IsResizable"/>。
    /// </summary>
    protected override void OnFrameNativeStyleChanged()
    {
        ApplyResizeMode();
        UpdateDpiVisuals();
        base.OnFrameNativeStyleChanged();
    }

    /// <summary>
    /// 三个标题栏按钮**实际**占用的宽度。
    ///
    /// 必须按各自的真实宽度累加：Chrome 样式的最小化按钮是 45、其余是 46，
    /// 原先用「统一宽度 × 按钮数」漏掉了 <see cref="ChromeWindow.MinimizeButtonWidth"/>。
    /// 那 1px 的差恰好偏小（安全方向），但一旦把最小化按钮调宽，最小窗口宽度就会不够，
    /// 关闭按钮被挤出客户区右侧。
    ///
    /// 另外按模板实况算：<c>NoResize</c> 时模板把最小化/最大化按钮折叠掉了，
    /// 此时只剩关闭按钮，宽度也应随之收窄。
    /// </summary>
    protected override double CaptionButtonsWidth =>
        (_minimize is null ? 0d : EffectiveMinimizeButtonWidth)
        + (_maximize is null ? 0d : CaptionButtonWidth)
        + CaptionButtonWidth;

    protected override double CaptionHeight => TitleBarHeight;

    /// <summary>
    /// 图标列的宽度，用于计算最小窗口宽度：
    /// <c>CaptionIconBoxMargin.Left + SM_CXSMSIZE + CaptionIconBoxMargin.Right</c>。
    ///
    /// 要按**模板里那一列的真实宽度**算，而不是图标的墨迹宽度（12 边距 + 16 图标 = 28）。
    /// 那一列装的是系统菜单命中盒子（96dpi 下 22×22），盒子的左边距才是列的起点：
    /// Chrome / VsCode / Windows 分别是 9 + 22 = 31、7 + 22 = 29、5 + 22 = 27。
    ///
    /// 这不是理论值 —— 图标原先在弹性列里，缩到最窄时会被裁掉几个像素，掩盖了这个差值；
    /// 独立成列后列宽不肯收缩，少算的宽度就会把三个按钮挤出客户区右侧。
    ///
    /// <c>Right</c> 也要算进来：它在模板里是命中盒的外边距，**列宽的一部分**
    /// （实测 Right=20 时列宽 31→51）。漏掉它时把窗口缩到 MinWidth，
    /// 关闭按钮会跑到客户区外面（实测 Right=20 时右缘 188 &gt; 客户区 185）。
    /// </summary>
    protected override double CaptionLeadingWidth =>
        ShowTitleBarIcon
            ? CaptionIconBoxMargin.Left + EffectiveSystemMenuBoxSize + CaptionIconBoxMargin.Right
            : 0d;

    protected override void OnFrameAttached()
    {
        // 先刷新输入状态，再刷新图标和延迟居中。
        ApplyResizeMode();
        RefreshEffectiveTitleBarIcon();
        if (_centerWhenInitialized)
        {
            _centerWhenInitialized = false;
            CenterOnTargetMonitor();
        }
        else if (_constrainWhenInitialized)
        {
            _constrainWhenInitialized = false;
            ConstrainToWorkArea();
        }
    }

    protected override void OnFrameStateChanged()
    {
        // 窗口状态变化（最大化 / 还原 / 最小化）一定会移动三个标题栏按钮的位置，
        // 而 hover 是**按旧几何**记录下来的部件值。若不先清掉，下面这次
        // CommitVisualState 会把过期的 hover 画出来 —— 表现就是「鼠标已经不在
        // 按钮上，按钮却仍然亮着」，要等下一次 WM_NCMOUSEMOVE 才恢复。
        //
        // 用无通知的重置：紧接着的 CommitVisualState 会一次性提交，避免同一帧里
        // 连续两次刷新视觉。
        _input.ClearPointerState();
        UpdateDpiVisuals();
        CommitVisualState();
        base.OnFrameStateChanged();
        ApplyResizeMode();

        // 清空之后要按**新几何**重新判定一次，否则会出现相反的问题：鼠标恰好停在
        // 移动后的按钮上，按钮却不亮，直到用户动一下鼠标。
        //
        // 不能在这里同步重算：此刻布局还没跑完。实测最大化按钮在 StateChanged 触发时
        // 报出的屏幕位置是中间态（692,1），到布局完成才变成最终位置（2491,20），
        // 按中间态命中会得到错误结果。
        //
        // Loaded 优先级晚于布局与渲染，此时按钮位置已经确定。
        SchedulePointerReevaluation();
    }

    /// <summary>布局完成后再按光标位置重算一次 hover；同一轮内多次状态变化只排一次。</summary>
    private void SchedulePointerReevaluation()
    {
        if (_pointerReevaluationPending)
            return;
        _pointerReevaluationPending = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                _pointerReevaluationPending = false;
                _input.RefreshPointerStateFromCursor();
            })
        );
    }

    protected override void OnFrameClosed()
    {
        Activated -= OnActivationChanged;
        Deactivated -= OnActivationChanged;
    }

    protected override void OnDisplayConfigurationChanged() =>
        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
}
