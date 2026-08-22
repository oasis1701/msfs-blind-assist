namespace MSFSBlindAssist.Tests.IFly;

using MSFSBlindAssist.Forms.IFly737;

public class CduScratchpadAnnouncerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstPoll_SeedsSilently()
    {
        var a = new CduScratchpadAnnouncer();
        Assert.Null(a.OnPoll("2500", T0));
        Assert.Null(a.OnPoll("2500", T0.AddSeconds(1)));
    }

    [Fact]
    public void ChangeInsideSuppressionWindow_IsAnnouncedOnFirstPollAfterExpiry()
    {
        // The PR #163 M1 scenario: the entry settles while suppressed; the next
        // poll sees an unchanged screen but must STILL read the entry back.
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("", T0);
        a.SuppressUntil = T0.AddMilliseconds(720);
        Assert.Null(a.OnPoll("2500", T0.AddMilliseconds(400)));   // suppressed
        Assert.Equal("2500", a.OnPoll("2500", T0.AddMilliseconds(800))); // unchanged text, window expired
    }

    [Fact]
    public void ClearedScratchpad_SaysCleared()
    {
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("2500", T0);
        Assert.Equal("Cleared", a.OnPoll("", T0.AddSeconds(1)));
    }

    [Fact]
    public void ChangeThatRevertsWhileSuppressed_StaysSilent()
    {
        // type + Enter + LSK before the window expires: the entry left the
        // scratchpad again — announcing it later would be out-of-context noise.
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("", T0);
        a.SuppressUntil = T0.AddMilliseconds(720);
        a.OnPoll("2500", T0.AddMilliseconds(400));
        Assert.Null(a.OnPoll("", T0.AddMilliseconds(800)));
    }

    [Fact]
    public void Reset_ReseedsSilently()
    {
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("OLD", T0);
        a.Reset();
        Assert.Null(a.OnPoll("NEW", T0.AddSeconds(2)));
    }

    [Fact]
    public void UnchangedText_NeverReAnnounces()
    {
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("", T0);
        Assert.Equal("2500", a.OnPoll("2500", T0.AddSeconds(1)));
        Assert.Null(a.OnPoll("2500", T0.AddSeconds(2)));
    }
}
