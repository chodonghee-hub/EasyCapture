namespace EasyCapture.Core;

public enum FocusVerdict
{
    /// <summary>계속 둔다.</summary>
    Keep,

    /// <summary>포그라운드였다가 연속으로 벗어났다.</summary>
    LostFocus,

    /// <summary>제한 시간 안에 한 번도 포그라운드가 되지 못했다.</summary>
    NeverFocused,
}

/// <summary>오버레이가 포그라운드를 유지하는지 주기적으로 판정한다. 시간은 호출하는 쪽이 넘긴다.</summary>
public sealed class FocusMonitor
{
    /// <summary>포그라운드에서 연속으로 이 횟수만큼 벗어나면 포커스를 잃은 것으로 본다.</summary>
    public const int LostTicksToCancel = 2;

    /// <summary>표시된 뒤 이 시간 안에 한 번도 포그라운드가 되지 못하면 포커스를 얻지 못한 것으로 본다.</summary>
    public static readonly TimeSpan InitialFocusTimeout = TimeSpan.FromMilliseconds(2000);

    private int _lostTicks;

    public bool SeenForeground { get; private set; }

    /// <summary>오버레이가 포그라운드임을 확인했을 때(활성화 직후 등) 호출한다.</summary>
    public void MarkForeground()
    {
        SeenForeground = true;
        _lostTicks = 0;
    }

    public FocusVerdict Tick(bool isForeground, TimeSpan sinceShown)
    {
        if (isForeground)
        {
            MarkForeground();
            return FocusVerdict.Keep;
        }

        if (SeenForeground)
        {
            return ++_lostTicks >= LostTicksToCancel ? FocusVerdict.LostFocus : FocusVerdict.Keep;
        }

        return sinceShown >= InitialFocusTimeout ? FocusVerdict.NeverFocused : FocusVerdict.Keep;
    }
}
