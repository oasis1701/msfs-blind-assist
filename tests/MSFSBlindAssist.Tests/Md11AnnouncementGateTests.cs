using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Who gets to speak a control's state and when (spec §3.6–3.7): a background lamp change
/// speaks once per new text; the pilot's own press is confirmed once by the feedback path and
/// the lamps it lights must not repeat it.
/// </summary>
public class Md11AnnouncementGateTests
{
    [Fact]
    public void BackgroundChange_SpeaksOnce_ThenStaysQuietForTheSameText()
    {
        var g = new Md11AnnouncementGate();
        Assert.True(g.ShouldSpeakBackground("EXT", "On", 10_000));
        Assert.False(g.ShouldSpeakBackground("EXT", "On", 11_000));
        Assert.True(g.ShouldSpeakBackground("EXT", "Available", 12_000));
    }

    [Fact]
    public void LampChange_InsideTheEchoWindowOfAPress_IsSwallowed()
    {
        var g = new Md11AnnouncementGate();
        g.NotePress("GEN1", 50_000);
        Assert.False(g.ShouldSpeakBackground("GEN1", "Off", 50_000 + Md11AnnouncementGate.EchoWindowMs - 1));
        Assert.True(g.ShouldSpeakBackground("GEN1", "Off", 50_000 + Md11AnnouncementGate.EchoWindowMs + 1));
    }

    [Fact]
    public void Feedback_AlwaysSpeaks_AndSeedsTheDedup()
    {
        var g = new Md11AnnouncementGate();
        Assert.Equal("Armed", g.Feedback("GEN1", "Armed"));
        Assert.Equal("Armed", g.Feedback("GEN1", "Armed"));               // an inert press repeats the unchanged state on purpose
        Assert.False(g.ShouldSpeakBackground("GEN1", "Armed", 99_000));   // the lamp echo of that press stays quiet
    }

    [Fact]
    public void Feedback_ClosesTheEchoWindow_SoALaterCorrectionIsStillSpoken()
    {
        // A guarded press spends 550 ms lifting its cover before the button is written, so its
        // lamp can land after the feedback has already spoken the OLD state. That later lamp is
        // the correction, not an echo: it must speak. Same text still stays quiet.
        var g = new Md11AnnouncementGate();
        g.NotePress("HYD_TEST", 50_000);
        g.Feedback("HYD_TEST", "Off");                                     // spoke the stale state
        Assert.True(g.ShouldSpeakBackground("HYD_TEST", "Test", 50_100));  // the real lamp, inside the old window
        Assert.False(g.ShouldSpeakBackground("HYD_TEST", "Test", 50_200)); // and only once
    }

    [Fact]
    public void Feedback_StillSwallowsTheEchoOfItsOwnPress()
    {
        var g = new Md11AnnouncementGate();
        g.NotePress("GEN1", 50_000);
        g.Feedback("GEN1", "Off");
        Assert.False(g.ShouldSpeakBackground("GEN1", "Off", 50_100));   // same text — the echo
    }

    [Fact]
    public void DarkTransition_WhileUnpowered_IsSilent()
    {
        // The normal shutdown order (external power off, battery still on) takes every OFF-legend
        // lamp on the DC busses dark at once. Speaking each one's dark meaning would narrate the
        // panel losing power a control at a time: "Tank 1 Fuel Pumps: On", "Pack 1: On", …
        var g = new Md11AnnouncementGate();
        Assert.Null(g.SpeakDarkTransition("FUEL_PUMP_1", "On", poweredNow: false, nowMs: 10_000));
    }

    [Fact]
    public void DarkTransition_ThatLosesPowerBeforeItFires_IsDropped()
    {
        // The lamps ride two SimConnect batches, so a batch-2 lamp can go dark while the gate's
        // batch-1 DC bus lamp still says "powered". The verdict is taken at fire time, not at the
        // moment the lamp went out — by then the gate has caught up.
        var g = new Md11AnnouncementGate();
        Assert.Null(g.SpeakDarkTransition("PACK_1", "On", poweredNow: false, nowMs: 11_500));
        Assert.True(g.ShouldSpeakBackground("PACK_1", "On", 20_000));   // and nothing was recorded
    }

    [Fact]
    public void DarkTransition_StillPoweredAtFireTime_SpeaksOnce()
    {
        var g = new Md11AnnouncementGate();
        Assert.Equal("On", g.SpeakDarkTransition("PACK_1", "On", poweredNow: true, nowMs: 11_500));
        Assert.Null(g.SpeakDarkTransition("PACK_1", "On", poweredNow: true, nowMs: 13_000));
    }

    [Fact]
    public void DarkTransition_OfALampThatRelitFirst_DoesNotRepeatTheRelight()
    {
        // Lamp out, then back on inside the settle: the relight speaks immediately, and the
        // deferred verdict re-composes the CURRENT (lit) state, which the dedup already holds.
        var g = new Md11AnnouncementGate();
        Assert.True(g.ShouldSpeakBackground("PACK_1", "Off", 10_500));
        Assert.Null(g.SpeakDarkTransition("PACK_1", "Off", poweredNow: true, nowMs: 11_500));
    }

    [Fact]
    public void DarkTransition_WithNothingToSay_IsSilent()
    {
        var g = new Md11AnnouncementGate();
        Assert.Null(g.SpeakDarkTransition("X", null, poweredNow: true, nowMs: 1));
        Assert.Null(g.SpeakDarkTransition("X", "", poweredNow: true, nowMs: 2));
    }

    [Fact]
    public void Reset_ForgetsEverything()
    {
        var g = new Md11AnnouncementGate();
        g.Feedback("X", "On");
        g.Reset();
        Assert.True(g.ShouldSpeakBackground("X", "On", 1));
    }

    /// <summary>
    /// The press feedback MUST speak before the echo window closes. The window is stamped when the
    /// press is dispatched (NotePress) and is closed either by <see cref="Md11AnnouncementGate.Feedback"/>
    /// or by <see cref="Md11AnnouncementGate.EchoWindowMs"/> elapsing — and if it elapses first the
    /// press's own lamp echo passes ShouldSpeakBackground and is spoken, then the feedback (which
    /// seeds the dedup but never consults it) says the same thing again. So the slowest live path —
    /// a GUARDED press: the guard's decision read, the cover's settle, the bus backlog the press is
    /// queued behind, the lamps' settle, and the latch's own fresh read — has to fit inside it.
    /// This is why the latch read is bounded by the short guard ceiling rather than the walker's.
    ///
    /// The chain absorbs the bus backlog TWICE — once in EnsureGuardOpenAsync's cover settle and
    /// again in the feedback's own lamp settle — and each term is Pending × MinGapMs, which is
    /// bounded only by the queue (256 ids ⇒ 15 s). A guarded press queued behind a real burst (an
    /// MCDU scratchpad send is ~24 CEVENTs, 1.4 s) therefore cannot be budgeted for by fixed
    /// numbers; both rows below use one, and the feedback's own wait is what gives, clamped by
    /// TFDiMD11Definition.FeedbackDelayMs to whatever the window has left.
    /// </summary>
    [Theory]
    // Idle queue: nothing is truncated — the feedback still waits the full lamp settle.
    [InlineData(0, TFDiMD11Definition.PressSettleMs)]
    // One MCDU scratchpad send ahead of the guard lift AND another ahead of the press.
    [InlineData(24 * Md11EventBus.MinGapMs, -1)]
    public void AGuardedPressFeedback_FitsInsideTheEchoWindow(int backlogMs, int expectedWait)
    {
        long beforeTheWait = TFDiMD11Definition.GuardReadTimeoutMs   // the guard's decision read
                           + TFDiMD11Definition.GuardOpenSettleMs    // the cover's settle, from the click's write
                           + backlogMs;                              // …which absorbs the backlog once
        int wait = TFDiMD11Definition.FeedbackDelayMs(backlogMs, beforeTheWait);   // …and the lamps' settle again
        long worstCase = beforeTheWait + wait + TFDiMD11Definition.GuardReadTimeoutMs;   // the latch's fresh read

        if (expectedWait >= 0)
            Assert.Equal(expectedWait, wait);
        Assert.True(worstCase < Md11AnnouncementGate.EchoWindowMs,
            $"A guarded press's feedback can take {worstCase} ms behind a {backlogMs} ms backlog, which does not " +
            $"fit inside the {Md11AnnouncementGate.EchoWindowMs} ms echo window — the lamp echo would be spoken too.");
    }

    /// <summary>
    /// The budget is what the window has LEFT, never negative: a chain that has already overrun
    /// speaks at once (reading the state early, which the lamp's later correction covers — that is
    /// why Feedback closes the window) rather than waiting past the window and doubling the sentence.
    /// </summary>
    [Theory]
    [InlineData(0, 0, Md11AnnouncementGate.EchoWindowMs)]
    [InlineData(500, 300, Md11AnnouncementGate.EchoWindowMs - 800)]
    [InlineData(Md11AnnouncementGate.EchoWindowMs, 300, 0)]
    [InlineData(99_000, 300, 0)]
    public void TheFeedbackBudget_IsWhatTheWindowHasLeft(long elapsedMs, int reserveMs, int expected)
    {
        Assert.Equal(expected, Md11AnnouncementGate.RemainingFeedbackBudgetMs(elapsedMs, reserveMs));
    }
}
