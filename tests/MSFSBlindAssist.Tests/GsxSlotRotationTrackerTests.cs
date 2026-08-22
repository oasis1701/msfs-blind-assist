// GSX's ground-crew narration must survive a service that publishes a quantity.
//
// Reported live 2026-08-17 by a blind pilot who could no longer follow a refuel or a
// boarding. Confirmed from that pilot's own gsx.log: across nine `performing` windows of
// Refueling / Boarding / Deboarding, SEVEN carried not one spoken message-slot line, and
// the total gated time was 1 h 00 m 49 s. The 08-17 refuel went dark from 22:04:12 to
// 22:07:48 — the window holding "Operator walking to pump" and "Lowering platform".
//
// The cause was a BLANKET stand-down in AnnounceMessageIfChanged: while ANY performing
// service published pax/bags/fuel, the whole slot was discarded. That was written to stop
// GSX's rotating progress ticker ("80/155 passengers boarded" -> "…loading Fuel: 776 USGAL
// (2360 kg)" -> "Baggage loading progress 83%" -> blank, every few seconds) from becoming
// continuous speech, and it does — but it also discards the crew narration of the very
// service it is gating, because refuel's own prose rides the same slot as refuel's figures.
//
// The slot cannot be split by SERVICE (it is one shared field with nothing naming its
// writer), and it cannot be split by TEXT (the obvious "drop anything with a digit" breaks
// on "Waiting for your action: open R Entry 5", an instruction the pilot must act on).
//
// It CAN be split structurally. A rotating ticker re-shows a phrase only after OTHER
// phrases have intervened; a GSX action nag re-shows the SAME phrase with nothing in
// between. That is the whole rule below, and it is why the existing single-predecessor
// GsxPhraseGate could not catch the ticker on its own: under rotation, each line's
// immediate predecessor is a DIFFERENT line, so every one reads as news.

using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxSlotRotationTrackerTests
{
    // A fixed instant; the tracker takes time as a parameter so these are deterministic.
    private static readonly DateTime T0 = new(2026, 8, 17, 22, 4, 12, DateTimeKind.Utc);

    private static GsxSlotRotationTracker Tracker() => new();

    /// <summary>Offer <paramref name="phrase"/> at <paramref name="at"/>, mirroring the
    /// caller's order of operations: ask, then record REGARDLESS of the answer.
    ///
    /// Recording every phrase GSX offers — not only the ones that reached speech — is what
    /// makes the second lap of a rotation visible. Record only what was spoken and the
    /// suppressed lines leave no trace, so the third line of lap two sits next to the third
    /// line of lap one and reads as a nag rather than a rotation.</summary>
    private static bool Offer(GsxSlotRotationTracker t, string phrase, DateTime at)
    {
        bool speaks = !t.IsRotation(phrase, at);
        t.Record(phrase, at);
        return speaks;
    }

    [Fact]
    public void The_rotating_progress_ticker_is_silenced_after_one_full_cycle()
    {
        // The exact cycle captured live and quoted in GsxServiceState.PublishesTypedProgress.
        var t = Tracker();
        Assert.True(Offer(t, "80/155 passengers boarded", T0));
        Assert.True(Offer(t, "The airplane system is loading Fuel: 776 USGAL (2360 kg)", T0.AddSeconds(3)));
        Assert.True(Offer(t, "Baggage loading progress 83%", T0.AddSeconds(6)));

        // Second time round the numbers have moved on, so nothing is an exact repeat and
        // the single-predecessor rule cannot see it — but each line's own last showing is
        // separated from it by the other two, which is what makes this a rotation.
        Assert.False(Offer(t, "95/155 passengers boarded", T0.AddSeconds(9)));
        Assert.False(Offer(t, "The airplane system is loading Fuel: 812 USGAL (2470 kg)", T0.AddSeconds(12)));
        Assert.False(Offer(t, "Baggage loading progress 91%", T0.AddSeconds(15)));
    }

    [Theory]
    [InlineData("Operator walking to pump")]
    [InlineData("Lowering platform")]
    [InlineData("Raising platform")]
    [InlineData("Fuel Truck is in position")]
    [InlineData("Waiting for your action: open R Entry 5")]
    public void Crew_narration_speaks_even_while_the_ticker_is_cycling(string narration)
    {
        // The regression this exists to stop: these are the lines the blanket gate discarded.
        var t = Tracker();
        Offer(t, "80/155 passengers boarded", T0);
        Offer(t, "The airplane system is loading Fuel: 776 USGAL (2360 kg)", T0.AddSeconds(3));
        Offer(t, "Baggage loading progress 83%", T0.AddSeconds(6));

        Assert.True(Offer(t, narration, T0.AddSeconds(9)));
    }

    [Fact]
    public void A_repeated_action_nag_is_not_treated_as_rotation()
    {
        // Live gsx.log, 08-17 21:19:53 and 21:20:03 — GSX re-showed the same prompt ten
        // seconds later and BOTH were spoken. Nothing intervened, so it is a nag, not a
        // rotation; the tracker must stay out of the way and let the caller's existing
        // blank-slot rescue decide.
        var t = Tracker();
        Offer(t, "Waiting for your action: Remove PMDG Chocks", T0);

        Assert.False(t.IsRotation("Waiting for your action: Remove PMDG Chocks", T0.AddSeconds(10)));
    }

    [Fact]
    public void A_phrase_returning_after_the_window_has_expired_speaks_again()
    {
        // A second refuel an hour later must narrate in full. Without expiry the first
        // refuel's lines would suppress the second one's for the rest of the session.
        var t = Tracker();
        Offer(t, "Fuel Truck is on its way", T0);
        Offer(t, "Operator walking to pump", T0.AddSeconds(5));

        Assert.True(t.IsRotation("Fuel Truck is on its way", T0.AddSeconds(30)));
        Assert.False(t.IsRotation("Fuel Truck is on its way", T0 + GsxSlotRotationTracker.Window.Add(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void History_is_bounded_so_a_long_service_cannot_grow_it_without_limit()
    {
        // A refuel publishes for minutes at ~1 Hz. The tracker keeps a fixed number of
        // entries; the oldest fall out and stop suppressing.
        var t = Tracker();
        Offer(t, "first", T0);
        for (int i = 0; i < GsxSlotRotationTracker.MaxEntries; i++)
            Offer(t, $"filler {i} text", T0.AddSeconds(i + 1));

        Assert.False(t.IsRotation("first", T0.AddSeconds(GsxSlotRotationTracker.MaxEntries + 2)));
    }

    [Fact]
    public void Clear_drops_the_history_for_a_new_session()
    {
        var t = Tracker();
        Offer(t, "80/155 passengers boarded", T0);
        Offer(t, "Baggage loading progress 83%", T0.AddSeconds(3));
        Assert.True(t.IsRotation("90/155 passengers boarded", T0.AddSeconds(6)));

        t.Clear();

        Assert.False(t.IsRotation("90/155 passengers boarded", T0.AddSeconds(6)));
    }

    [Fact]
    public void IsRotation_does_not_mutate_the_history()
    {
        // A predicate with a side effect is how a gate silently changes its own answer.
        // Recording is the caller's separate, explicit step.
        var t = Tracker();
        Offer(t, "80/155 passengers boarded", T0);
        Offer(t, "Baggage loading progress 83%", T0.AddSeconds(3));

        Assert.True(t.IsRotation("90/155 passengers boarded", T0.AddSeconds(6)));
        Assert.True(t.IsRotation("90/155 passengers boarded", T0.AddSeconds(6)));
    }

    [Fact]
    public void A_blank_slot_is_never_a_rotation_and_is_never_recorded()
    {
        // GSX blanks the slot between ticker lines; that blank must not clear or pollute
        // the history, or the rotation it separates becomes invisible again.
        var t = Tracker();
        Offer(t, "80/155 passengers boarded", T0);
        Assert.False(t.IsRotation("", T0.AddSeconds(1)));
        Assert.False(t.IsRotation("   ", T0.AddSeconds(2)));

        Offer(t, "Baggage loading progress 83%", T0.AddSeconds(3));
        Assert.True(t.IsRotation("90/155 passengers boarded", T0.AddSeconds(4)));
    }
}
