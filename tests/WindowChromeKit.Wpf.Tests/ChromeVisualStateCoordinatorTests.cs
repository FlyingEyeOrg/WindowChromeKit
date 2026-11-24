using WindowChromeKit.Wpf.Internal;
using Xunit;

namespace WindowChromeKit.Wpf.Tests;

public sealed class ChromeVisualStateCoordinatorTests
{
    [Fact]
    public void InputSnapshotsAreCommittedThroughTheSingleVisualSink()
    {
        var commits = 0;
        var coordinator = new ChromeVisualStateCoordinator(() => commits++);
        var input = new ChromeInputState(
            ChromeHitTestRole.MaximizeButton,
            ChromeHitTestRole.MaximizeButton);

        coordinator.CommitInput(input);

        Assert.Equal(1, commits);
        Assert.Equal(input, coordinator.Input);
    }

    [Fact]
    public void WpfStateChangesKeepTheLatestInputSnapshot()
    {
        var commits = 0;
        var coordinator = new ChromeVisualStateCoordinator(() => commits++);
        var input = new ChromeInputState(
            ChromeHitTestRole.CloseButton,
            ChromeHitTestRole.Default);
        coordinator.CommitInput(input);

        // 激活、窗口状态、模板与配色变化只提交视觉，不清空输入状态。
        coordinator.Commit();
        coordinator.Commit();

        Assert.Equal(3, commits);
        Assert.Equal(input, coordinator.Input);
    }
}
