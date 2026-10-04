using System.Windows;
using System.Windows.Input;
using WindowChromeKit.Wpf.Internal;

namespace WindowChromeKit.Wpf;

public partial class ChromeWindow
{
    private void ApplyResizeMode()
    {
        _input.PrepareResizeModeChange();
        CommandManager.InvalidateRequerySuggested();
    }

    protected override void OnFrameDpiChanged() => UpdateDpiVisuals();

    protected override double CaptionButtonsWidth => CaptionButtonWidth * VisibleCaptionButtonCount;

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
            ? CaptionIconBoxMargin.Left + SystemMenuBoxSizeDip + CaptionIconBoxMargin.Right
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
        UpdateDpiVisuals();
        ApplyVisualState();
        base.OnFrameStateChanged();
        ApplyResizeMode();
    }

    protected override void OnFrameClosed()
    {
        Activated -= OnActivationChanged;
        Deactivated -= OnActivationChanged;
    }

    protected override void OnDisplayConfigurationChanged() =>
        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
}
