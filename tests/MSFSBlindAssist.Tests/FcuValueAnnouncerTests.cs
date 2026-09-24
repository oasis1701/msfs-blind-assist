// FcuValueAnnouncer decides WHEN a hardware-dialled FCU value is spoken on the FlyByWire jets (PR #140).
// The definitions hand it a phrase per delivery — null for dashes, FcuValuePhrases.Unavailable when the
// FCU itself is off — and speak what OnBatchDelivered releases once the whole sample has arrived.
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuValueAnnouncerTests
{
    private const string Hdg = "HDG";
    private const string Spd = "SPD";
    private const string Alt = "ALT";
    private static readonly string Off = FcuValuePhrases.Unavailable;

    private static FcuValueAnnouncer Seeded(string key, string? phrase)
    {
        var a = new FcuValueAnnouncer();
        a.Observe(key, phrase, muted: false, nowMs: 0);
        Assert.Empty(a.OnBatchDelivered(1));
        return a;
    }

    private static IReadOnlyList<string> Deliver(FcuValueAnnouncer a, string key, string? phrase, long nowMs = 0, bool muted = false)
    {
        a.Observe(key, phrase, muted, nowMs);
        return a.OnBatchDelivered(1);
    }

    private static void Batches(FcuValueAnnouncer a, int count)
    {
        for (int i = 0; i < count; i++) Assert.Empty(a.OnBatchDelivered(1));
    }

    // ---- Speaking ----

    [Fact]
    public void The_first_sample_of_a_key_is_a_silent_baseline() =>
        Assert.Empty(Deliver(new FcuValueAnnouncer(), Hdg, "Heading 250 degrees"));

    [Fact]
    public void A_changed_value_is_spoken_when_its_batch_has_finished_dispatching()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0);
        Assert.Equal(new[] { "Heading 260 degrees" }, a.OnBatchDelivered(1));
        Assert.Empty(a.OnBatchDelivered(1));                       // released once
    }

    [Fact]
    public void Any_batch_releases_what_was_staged()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0);
        Assert.Equal(new[] { "Heading 260 degrees" }, a.OnBatchDelivered(2));
    }

    [Fact]
    public void Staged_phrases_are_released_in_arrival_order_and_the_latest_sample_of_a_key_wins()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Spd, "Speed 250 knots", muted: false, nowMs: 0);   // baseline
        Assert.Empty(a.OnBatchDelivered(1));
        a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0);
        a.Observe(Spd, "Speed 260 knots", muted: false, nowMs: 0);
        a.Observe(Hdg, "Heading 270 degrees", muted: false, nowMs: 0);   // a missed release: latest wins
        Assert.Equal(new[] { "Speed 260 knots", "Heading 270 degrees" }, a.OnBatchDelivered(1));
    }

    [Fact]
    public void An_unchanged_value_is_not_repeated() =>
        Assert.Empty(Deliver(Seeded(Hdg, "Heading 250 degrees"), Hdg, "Heading 250 degrees"));

    [Fact]
    public void A_dashed_window_says_nothing_and_cancels_a_staged_value()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 0);
        a.Observe(Hdg, null, muted: false, nowMs: 0);
        Assert.Empty(a.OnBatchDelivered(1));
    }

    [Fact]
    public void Pulling_out_of_dashes_speaks_the_value_even_when_it_matches_the_last_selection()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Empty(Deliver(a, Hdg, null));
        Assert.Equal(new[] { "Heading 250 degrees" }, Deliver(a, Hdg, "Heading 250 degrees"));
    }

    [Fact]
    public void A_key_first_seen_dashed_speaks_its_first_selection() =>
        Assert.Equal(new[] { "Heading 270 degrees" }, Deliver(Seeded(Hdg, null), Hdg, "Heading 270 degrees"));

    [Fact]
    public void A_muted_change_is_recorded_so_unmuting_does_not_replay_it()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Assert.Empty(Deliver(a, Hdg, "Heading 260 degrees", muted: true));
        Assert.Empty(Deliver(a, Hdg, "Heading 260 degrees"));
        Assert.Equal(new[] { "Heading 270 degrees" }, Deliver(a, Hdg, "Heading 270 degrees"));
    }

    // ---- Echo window ----

    [Fact]
    public void An_echo_window_mutes_only_the_keys_it_names()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Spd, "Speed 250 knots", muted: false, nowMs: 0);
        a.OnBatchDelivered(1);
        a.SuppressEcho(new[] { Hdg }, nowMs: 1_000);
        a.Observe(Hdg, "Heading 260 degrees", muted: false, nowMs: 1_100);
        a.Observe(Spd, "Speed 260 knots", muted: false, nowMs: 1_100);
        Assert.Equal(new[] { "Speed 260 knots" }, a.OnBatchDelivered(1));
    }

    [Fact]
    public void An_echo_window_absorbs_every_change_until_it_expires()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.SuppressEcho(new[] { Hdg }, nowMs: 1_000);
        Assert.Empty(Deliver(a, Hdg, "Heading 260 degrees", nowMs: 1_100));
        Assert.Empty(Deliver(a, Hdg, "Heading 265 degrees", nowMs: 1_000 + FcuValueAnnouncer.EchoWindowMs - 1));
        Assert.Equal(new[] { "Heading 270 degrees" },
            Deliver(a, Hdg, "Heading 270 degrees", nowMs: 1_000 + FcuValueAnnouncer.EchoWindowMs));
    }

    [Fact]
    public void An_echoed_change_is_absorbed_not_spoken_late()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.SuppressEcho(new[] { Hdg }, nowMs: 0);
        Assert.Empty(Deliver(a, Hdg, "Heading 300 degrees", nowMs: 100));
        Assert.Empty(Deliver(a, Hdg, "Heading 300 degrees", nowMs: 10_000));
    }

    [Fact]
    public void A_queued_write_re_arms_the_echo_it_was_armed_with_when_it_is_finally_sent()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.SuppressEcho(new[] { Hdg }, nowMs: 0, forEvent: "A32NX.FCU_HDG_SET");
        a.RearmEcho("A32NX.FCU_HDG_SET", nowMs: 60_000);
        Assert.Empty(Deliver(a, Hdg, "Heading 270 degrees", nowMs: 60_500));
    }

    [Fact]
    public void Re_arming_an_event_that_was_never_armed_mutes_nothing()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.RearmEcho("A32NX.FCU_AP_1_PUSH", nowMs: 60_000);
        Assert.Equal(new[] { "Heading 270 degrees" }, Deliver(a, Hdg, "Heading 270 degrees", nowMs: 60_500));
    }

    // ---- FCU availability (power transitions) ----

    [Fact]
    public void A_source_going_unavailable_is_silent() =>
        Assert.Empty(Deliver(Seeded(Alt, "Altitude 5000 feet"), Alt, Off));

    [Fact]
    public void A_source_coming_back_from_unavailable_is_silent_and_starts_a_settle()
    {
        var a = Seeded(Alt, Off);                                   // FCU off at connect
        Assert.Empty(Deliver(a, Alt, "Altitude 100 feet"));         // power-up: the FCU's first value
        Assert.True(a.IsSettling);
        Assert.Empty(Deliver(a, Hdg, "Heading 000 degrees"));       // start-up churn absorbed
    }

    [Fact]
    public void A_source_leaving_unavailable_for_dashes_starts_a_settle()
    {
        const string Vs = "VS";
        var a = Seeded(Vs, Off);                                    // FCU off at connect
        Assert.Empty(Deliver(a, Vs, null));                         // power-up: the FCU's first sample is dashes
        Assert.True(a.IsSettling);
        Assert.Empty(Deliver(a, Vs, "Vertical speed 0 feet per minute"));   // still absorbed, not released
    }

    [Fact]
    public void A_power_up_from_the_health_var_settles_for_only_the_quiet_deliveries()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.ObserveFcuHealth(false);
        Assert.Empty(a.OnBatchDelivered(1));
        a.ObserveFcuHealth(true);
        Assert.Empty(a.OnBatchDelivered(1));                        // the flip's own delivery: not quiet
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_power_up_from_a_source_leaving_unavailable_settles_for_only_the_quiet_deliveries()
    {
        var a = Seeded(Alt, Off);                                   // FCU off at connect
        Assert.Empty(Deliver(a, Alt, "Altitude 100 feet"));         // the flip's own delivery: not quiet
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void Health_coming_back_drops_what_the_same_batch_staged_and_settles()
    {
        // The A32NX shims sort BEFORE the health var in the batch, so the power-up sample stages them
        // first; the health flip later in the same batch must still win.
        var a = Seeded(Hdg, null);
        a.ObserveFcuHealth(false);
        Assert.Empty(a.OnBatchDelivered(1));
        a.Observe(Hdg, "Heading 000 degrees", muted: false, nowMs: 0);
        a.ObserveFcuHealth(true);
        Assert.Empty(a.OnBatchDelivered(1));
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void Health_going_away_drops_what_the_same_batch_staged_and_blocks_later_callouts()
    {
        // A380 battery-off: the zeroed heading shim sorts before A32NX_FCU_AFS_CP_ACTIVE.
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.ObserveFcuHealth(true);
        Assert.Empty(a.OnBatchDelivered(1));
        a.Observe(Hdg, "Heading 000 degrees", muted: false, nowMs: 0);
        a.ObserveFcuHealth(false);
        Assert.Empty(a.OnBatchDelivered(1));
        Assert.False(a.IsFcuAvailable);
        Assert.Empty(Deliver(a, Hdg, "Heading 010 degrees"));
    }

    [Fact]
    public void A_health_var_that_never_reads_true_blocks_nothing()
    {
        // An airframe that does not publish the health var reads 0 forever (Headwind A330 risk).
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.ObserveFcuHealth(false);
        Assert.True(a.IsFcuAvailable);
        Assert.Equal(new[] { "Heading 260 degrees" }, Deliver(a, Hdg, "Heading 260 degrees"));
    }

    [Fact]
    public void Health_reported_unhealthy_twice_stays_unavailable()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.ObserveFcuHealth(true);
        Assert.Empty(a.OnBatchDelivered(1));
        a.ObserveFcuHealth(false);
        Assert.False(a.IsFcuAvailable);
        a.ObserveFcuHealth(false);                                  // reported unhealthy again: no re-trigger
        Assert.False(a.IsFcuAvailable);
        Assert.Empty(Deliver(a, Hdg, "Heading 260 degrees"));
    }

    [Fact]
    public void A_never_seen_key_reads_unavailable_while_the_fcu_is_unavailable()
    {
        var a = new FcuValueAnnouncer();
        a.ObserveFcuHealth(true);
        a.ObserveFcuHealth(false);
        Assert.Equal(FcuWindowState.Unavailable, a.StateOf(Hdg));   // unavailability outranks "never seen"
    }

    [Fact]
    public void The_window_state_says_dashes_value_unavailable_or_unknown()
    {
        var a = new FcuValueAnnouncer();
        Assert.Equal(FcuWindowState.Unknown, a.StateOf(Hdg));
        Deliver(a, Hdg, null);
        Assert.Equal(FcuWindowState.Dashes, a.StateOf(Hdg));
        Deliver(a, Hdg, "Heading 250 degrees");
        Assert.Equal(FcuWindowState.Value, a.StateOf(Hdg));
        Deliver(a, Alt, Off);
        Assert.Equal(FcuWindowState.Unavailable, a.StateOf(Alt));
        a.ObserveFcuHealth(true);
        a.ObserveFcuHealth(false);
        Assert.Equal(FcuWindowState.Unavailable, a.StateOf(Hdg));
    }

    [Fact]
    public void A_rebaseline_records_silently_and_cancels_a_staged_phrase()
    {
        var a = Seeded(Alt, "Altitude 10000 feet");
        a.Rebaseline(Alt, "Altitude 3048 meters");                 // MTRS flipped: same altitude, new words
        Assert.Empty(Deliver(a, Alt, "Altitude 3048 meters"));
        a.Observe(Alt, "Altitude 3100 meters", muted: false, nowMs: 0);
        a.Rebaseline(Alt, "Altitude 3100 meters");
        Assert.Empty(a.OnBatchDelivered(1));
    }

    // ---- Settle ----

    [Fact]
    public void A_change_during_the_settle_is_recorded_silently()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Assert.Empty(Deliver(a, Hdg, "Heading 123 degrees"));
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void A_value_the_settle_absorbed_is_not_spoken_once_it_ends()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 123 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.False(a.IsSettling);
        Assert.Empty(Deliver(a, Hdg, "Heading 123 degrees"));
        Assert.Equal(new[] { "Heading 130 degrees" }, Deliver(a, Hdg, "Heading 130 degrees"));
    }

    [Fact]
    public void The_settle_ends_after_the_quiet_deliveries_once_the_aircraft_has_published()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 123 degrees");                     // carries the change: not quiet
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_value_the_sim_itself_writes_is_not_evidence_the_aircraft_published()
    {
        var a = Seeded(Alt, "Altitude 10000 feet");
        a.BeginSettle();
        a.Observe(Alt, "Altitude 5000 feet", muted: false, nowMs: 0, countsAsLoadEvidence: false);
        Batches(a, 1 + FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.True(a.IsSettling);
        Batches(a, FcuValueAnnouncer.SettleMaxDeliveries);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void Stillness_alone_does_not_end_the_settle()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Batches(a, FcuValueAnnouncer.SettleMaxDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_redelivered_unchanged_value_is_not_evidence_after_a_flight_load()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 250 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void After_a_cache_clear_every_re_fire_is_evidence_so_the_settle_ends_in_seconds()
    {
        // SimConnect drop: the cache was cleared on the way down, so the reconnect re-fires every var —
        // the aircraft publishing, even when the value equals the kept baseline (Md11SeedGate's rule).
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle(refireIsEvidence: true);
        Deliver(a, Hdg, "Heading 250 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_first_ever_sample_during_a_settle_is_not_evidence()
    {
        // A profile switched mid-load baselines the pre-publish values; only the later publish may end
        // the settle, never the switch's own first samples.
        var a = new FcuValueAnnouncer();
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 000 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries + 1);
        Assert.True(a.IsSettling);
        Deliver(a, Hdg, "Heading 250 degrees");                     // the aircraft publishes
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_first_ever_key_during_a_re_fire_settle_is_evidence()
    {
        // Unlike a plain settle, a re-fire settle counts a never-seen key's own first delivery: the
        // reconnect re-fires every var, so seeing it at all is the aircraft publishing.
        var a = new FcuValueAnnouncer();
        a.BeginSettle(refireIsEvidence: true);
        Deliver(a, Hdg, "Heading 250 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void A_flight_load_during_a_reconnect_settle_keeps_the_re_fire_rule()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle(refireIsEvidence: true);
        a.BeginSettle();                                            // AircraftLoaded on the reconnect
        Deliver(a, Hdg, "Heading 250 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void Only_the_first_batch_is_counted()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 123 degrees");
        for (int i = 0; i < 10; i++) { a.OnBatchDelivered(2); a.OnBatchDelivered(3); }
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void A_later_change_restarts_the_quiet_count()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.Observe(Spd, "Speed 100 knots", muted: false, nowMs: 0);
        a.OnBatchDelivered(1);
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 123 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);    // one short of ending
        Deliver(a, Spd, "Speed 140 knots");                         // carries a change
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        Assert.True(a.IsSettling);
        a.OnBatchDelivered(1);
        Assert.False(a.IsSettling);
    }

    [Fact]
    public void Deliveries_before_a_settle_do_not_count_toward_it()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        Batches(a, 50);
        a.BeginSettle();
        a.OnBatchDelivered(1);
        Assert.True(a.IsSettling);
    }

    [Fact]
    public void A_second_reset_starts_the_settle_over()
    {
        var a = Seeded(Hdg, "Heading 250 degrees");
        a.BeginSettle();
        Deliver(a, Hdg, "Heading 123 degrees");
        Batches(a, FcuValueAnnouncer.SettleQuietDeliveries - 1);
        a.BeginSettle();
        a.OnBatchDelivered(1);
        Assert.True(a.IsSettling);
    }
}
