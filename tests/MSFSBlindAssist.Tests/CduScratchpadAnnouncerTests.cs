using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The poll-driven CDU scratchpad read-back shared by the iFly 737 CDU window and the MD-11 MCDU
/// window (it moved out of the iFly's folder when the MD-11 adopted it). The first six tests are
/// the iFly's originals, unchanged; the last two pin the one thing the MD-11 needed added — its
/// own wording for an emptied scratchpad.
/// </summary>
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

    /// <summary>
    /// The MD-11 MCDU window says "Scratchpad cleared"; the iFly keeps "Cleared", the default,
    /// exactly as it shipped.
    /// </summary>
    [Fact]
    public void ClearedText_IsTheCallersWording()
    {
        var a = new CduScratchpadAnnouncer("Scratchpad cleared");
        a.OnPoll("KJFK", T0);
        Assert.Equal("Scratchpad cleared", a.OnPoll("", T0.AddSeconds(1)));
    }

    /// <summary>The wording is the ONLY thing the parameter changes: seeding, verbatim text and suppression are untouched.</summary>
    [Fact]
    public void ClearedText_ChangesOnlyTheEmptiedCase()
    {
        var a = new CduScratchpadAnnouncer("Scratchpad cleared");
        Assert.Null(a.OnPoll("", T0));                                      // still seeds silently
        Assert.Equal("KJFK", a.OnPoll("KJFK", T0.AddSeconds(1)));           // text still read verbatim
        a.SuppressUntil = T0.AddSeconds(3);
        Assert.Null(a.OnPoll("", T0.AddSeconds(2)));                        // still held while suppressed
        Assert.Equal("Scratchpad cleared", a.OnPoll("", T0.AddSeconds(4))); // read once the hold ends
    }

    [Fact]
    public void Two_stable_polls_ignore_a_one_poll_flicker()
    {
        var a = new CduScratchpadAnnouncer("Scratchpad cleared", stablePolls: 2);
        a.OnPoll("KJFK", T0);
        Assert.Null(a.OnPoll("", T0.AddMilliseconds(250)));      // a redraw frame
        Assert.Null(a.OnPoll("KJFK", T0.AddMilliseconds(500)));  // back: nothing happened
        Assert.Null(a.OnPoll("KJFK", T0.AddMilliseconds(750)));
    }

    [Fact]
    public void Two_stable_polls_announce_a_change_held_for_two_polls_once()
    {
        var a = new CduScratchpadAnnouncer("Scratchpad cleared", stablePolls: 2);
        a.OnPoll("KJFK", T0);
        Assert.Null(a.OnPoll("", T0.AddMilliseconds(250)));
        Assert.Equal("Scratchpad cleared", a.OnPoll("", T0.AddMilliseconds(500)));
        Assert.Null(a.OnPoll("", T0.AddMilliseconds(750)));
    }

    [Fact]
    public void One_stable_poll_is_the_shipped_behaviour()
    {
        var a = new CduScratchpadAnnouncer();
        a.OnPoll("KJFK", T0);
        Assert.Equal("Cleared", a.OnPoll("", T0.AddMilliseconds(250)));
    }
}
