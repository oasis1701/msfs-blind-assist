// FcuValueAnnouncer decides WHEN a hardware-dialled FCU value is spoken on the FlyByWire jets
// (777-MCP parity, PR #140). The definitions hand it a phrase per delivery — null when the FCU
// window shows dashes or the value is not a selection — and speak whatever it returns.
//
// What these pin, each against a way the first version went wrong:
//  * the first sample of a key is a baseline, never spoken (no burst on aircraft load);
//  * a dashed window says nothing but IS recorded, so pulling back out of managed speaks the value
//    even when it equals the last selection, and a key first seen dashed still speaks its first
//    selection (an FMS departure);
//  * a muted or echoed change is absorbed, not deferred;
//  * after a flight load or a reconnect, changes are absorbed until the aircraft has published and
//    gone quiet — counted in continuous-batch deliveries, never a wall clock.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuValueAnnouncerTests
{
    private const string Hdg = "HDG";
    private const string Spd = "SPD";

    private static FcuValueAnnouncer Seeded(string key, string? phrase)
    {
        var a = new FcuValueAnnouncer();
        Assert.Null(a.Observe(key, phrase, muted: false, nowMs: 0));
        return a;
    }

    [Fact]
    public void The_first_sample_of_a_key_is_a_silent_baseline()
    {
        var a = new FcuValueAnnouncer();
        Assert.Null(a.Observe(Hdg, "Heading 250 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void A_changed_value_is_spoken()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Equal("Heading 260 degrees", a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void An_unchanged_value_is_not_repeated()
    {
        // A panel's 1 Hz force-read re-delivers the same value; it must never re-speak it.
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Null(a.Observe(Hdg, "Heading 250 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void A_dashed_window_says_nothing()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Null(a.Observe(Hdg, null, muted: false, nowMs: 0));
    }

    [Fact]
    public void Pulling_out_of_dashes_speaks_the_value_even_when_it_matches_the_last_selection()
    {
        // Selected 250, pushed to managed (dashes), pulled back at 250: the value reappearing in
        // the window is news, so the baseline must have tracked the dashes in between.
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Null(a.Observe(Hdg, null, muted: false, nowMs: 0));
        Assert.Equal("Heading 250 degrees", a.Observe(Hdg, "Heading 250 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void A_key_first_seen_dashed_speaks_its_first_selection()
    {
        // An FMS departure: the window is dashed from the moment the app connects, so the pilot's
        // first selection of the flight must not be taken as the silent baseline.
        var a = Seeded(Hdg, null);
        Assert.Equal("Heading 270 degrees", a.Observe(Hdg, "Heading 270 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void A_muted_change_is_recorded_so_unmuting_does_not_replay_it()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Null(a.Observe(Hdg, "Heading 260 degrees", muted: true, nowMs: 0));
        Assert.Null(a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0));
        Assert.Equal("Heading 270 degrees", a.Observe(Hdg, "Heading 270 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void An_echo_window_mutes_only_the_keys_it_names()
    {
        // Setting the altitude in a dialog must not swallow a hardware heading turn made in the
        // same moment — the bug a single shared deadline had.
        var a = new FcuValueAnnouncer();
        a.Observe(Hdg, "Heading 250 degrees", muted: false, nowMs: 0);
        a.Observe(Spd, "Speed 250 knots", muted: false, nowMs: 0);

        a.SuppressEcho(new[] { Hdg }, nowMs: 1_000);

        Assert.Null(a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 1_100));
        Assert.Equal("Speed 260 knots", a.Observe(Spd, "Speed 260 knots", muted: false, nowMs: 1_100));
    }

    [Fact]
    public void An_echo_window_expires()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.SuppressEcho(new[] { Hdg }, nowMs: 1_000);

        Assert.Null(a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 1_000 + FcuValueAnnouncer.EchoWindowMs - 1));
        Assert.Equal("Heading 270 degrees",
            a.Observe(Hdg, "Heading 270 degrees", muted: false, nowMs: 1_000 + FcuValueAnnouncer.EchoWindowMs));
    }

    [Fact]
    public void An_echoed_change_is_absorbed_not_spoken_late()
    {
        // The set method already spoke its own confirmation; the echo must not surface a beat later.
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.SuppressEcho(new[] { Hdg }, nowMs: 0);
        Assert.Null(a.Observe(Hdg, "Heading 300 degrees", muted: false, nowMs: 100));
        Assert.Null(a.Observe(Hdg, "Heading 300 degrees", muted: false, nowMs: 10_000));
    }

    // ---- Context reset: a flight load or a reconnect ----

    [Fact]
    public void A_change_during_the_settle_is_recorded_silently()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();

        Assert.Null(a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0));
        Assert.True(a.IsSettling);
    }

    private static void Deliver(FcuValueAnnouncer a, int count)
    {
        for (int i = 0; i < count; i++) a.OnBatchDelivered(1);
    }

    [Fact]
    public void A_value_the_settle_absorbed_is_not_spoken_once_it_ends()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0);
        Deliver(a, 1 + FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.False(a.IsSettling);

        Assert.Null(a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0));
        Assert.Equal("Heading 130 degrees", a.Observe(Hdg, "Heading 130 degrees", muted: false, nowMs: 0));
    }

    [Fact]
    public void The_settle_ends_after_the_quiet_deliveries_once_the_aircraft_has_published()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0);

        a.OnBatchDelivered(1);   // carries the change: not quiet
        Deliver(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_value_the_sim_itself_writes_is_not_evidence_the_aircraft_published()
    {
        // The A380's FCU altitude is a stock SimVar the sim core can restore from the flight file
        // before the FBW WASM has run at all; taking it as "the aircraft has published" ended the
        // settle early and let the loaded heading and speed be called out as dial turns.
        const string Alt = "ALT";
        var a = Seeded(Alt, "Altitude 10000 feet");
        a.BeginSettle();
        Assert.Null(a.Observe(Alt, "Altitude 5000 feet", muted: false, nowMs: 0, countsAsLoadEvidence: false));

        Deliver(a, 1 + FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.True(a.IsSettling);

        Deliver(a, FcuValueAnnouncer.SettleMaxDeliveries);
        Assert.False(a.IsSettling);
        Assert.Null(a.Observe(Alt, "Altitude 5000 feet", muted: false, nowMs: 0));   // absorbed, not replayed
    }

    [Fact]
    public void Stillness_alone_does_not_end_the_settle()
    {
        // AircraftLoaded fires before the new aircraft publishes, and batches keep arriving with the
        // OLD values meanwhile: quiet without a change is not evidence the load has landed.
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();

        for (int i = 0; i < FcuValueAnnouncer.SettleMaxDeliveries - 1; i++) a.OnBatchDelivered(1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_redelivered_unchanged_value_is_not_evidence()
    {
        // A reconnect re-fires every variable with the value it already had; nothing moved.
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 250 degrees", muted: false, nowMs: 0);

        Deliver(a, 1 + FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void Only_the_first_batch_is_counted()
    {
        // Aircraft run one to five batches a second; counting every one would shrink the quiet
        // period to a fraction of a second on a multi-batch aircraft.
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0);

        for (int i = 0; i < 10; i++) { a.OnBatchDelivered(2); a.OnBatchDelivered(3); }
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void A_later_change_restarts_the_quiet_count()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0);
        Deliver(a, FcuValueAnnouncer.SettleQuietDeliveries);         // one short of ending
        a.Observe(Spd, "Speed 140 knots", muted: false, nowMs: 0);   // a first sample moves too
        a.OnBatchDelivered(1);   // carries it
        Deliver(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void Deliveries_before_a_settle_do_not_count_toward_it()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        for (int i = 0; i < 50; i++) a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);

        a.BeginSettle();
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void A_second_reset_starts_the_settle_over()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        a.Observe(Hdg, "Heading 123 degrees", muted: false, nowMs: 0);
        Deliver(a, FcuValueAnnouncer.SettleQuietDeliveries);   // one delivery short of ending

        // A second load: the evidence and the quiet count both start again, so the delivery that
        // would have ended the first settle does not end this one.
        a.BeginSettle();
        a.OnBatchDelivered(1);
        Assert.True(a.IsSettling);
    }
}
