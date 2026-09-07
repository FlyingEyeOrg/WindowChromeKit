namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>固定 owned window 的释放顺序，避免 Owner 恢复与窗口隐藏产生前台切换间隙。</summary>
internal static class ModalWindowReleaseTransition
{
    public static void Run(Action releaseOwner, Action hideChild, Action restoreForeground, Action detachOwner)
    {
        ArgumentNullException.ThrowIfNull(releaseOwner);
        ArgumentNullException.ThrowIfNull(hideChild);
        ArgumentNullException.ThrowIfNull(restoreForeground);
        ArgumentNullException.ThrowIfNull(detachOwner);

        releaseOwner();
        try
        {
            hideChild();
            restoreForeground();
        }
        finally
        {
            detachOwner();
        }
    }
}
