namespace SoftwareHub.DesktopAgent.Runtime;

/// <summary>记录一个窗口当前拥有的未结束直接子模态窗口数量。</summary>
internal sealed class ModalWindowBlockState
{
    public int Count { get; private set; }

    public bool IsBlocked => Count > 0;

    /// <summary>增加一个直接模态子窗口；仅在首次进入阻塞状态时返回 true。</summary>
    public bool Add()
    {
        Count++;
        return Count == 1;
    }

    /// <summary>移除一个直接模态子窗口；仅在最后一个子窗口结束时返回 true。</summary>
    public bool Remove()
    {
        if (Count == 0)
        {
            return false;
        }

        Count--;
        return Count == 0;
    }

    public bool Reset()
    {
        var changed = Count > 0;
        Count = 0;
        return changed;
    }
}
