namespace WindowChromeKit.Wpf.Internal;

/// <summary>
/// 窗口视觉状态的**唯一提交入口**。
///
/// <para>输入控制器提交不可变快照；WPF 激活、窗口状态与模板变化也汇聚到这里。
/// 原生窗口过程和输入控制器不持有视觉提交委托，因此不能越过协调器刷新模板。</para>
///
/// <para>提交保持同步，避免给输入与激活反馈额外插入一帧。协调器只负责视觉状态，
/// 不负责窗口几何、鼠标捕获、窗口激活或系统拖动。</para>
/// </summary>
internal sealed class ChromeVisualStateCoordinator
{
    private readonly Action _commit;
    private ChromeInputState _input = ChromeInputState.Default;

    internal ChromeVisualStateCoordinator(Action commit) => _commit = commit;

    internal ChromeInputState Input => _input;

    /// <summary>保存输入快照并提交完整视觉状态。</summary>
    internal void CommitInput(ChromeInputState input)
    {
        _input = input;
        Commit();
    }

    /// <summary>提交 WPF 激活、窗口状态、模板或配色变化。</summary>
    internal void Commit() => _commit();
}
