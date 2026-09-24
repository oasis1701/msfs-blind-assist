// QueueMovementPolicy — "Delta A320 ahead is moving." and the "Move up" nudge.
// PR #247 review R15 (a departure seen only as a one-sample speed edge was lost when outranked or when
// another sweep moved the previous speed on), L2 (a stale stopped-gap baseline fired a false call
// later), R5 ("Move up" fired at gates, on runways, with traffic still in front, and while creeping).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class QueueMovementPolicyTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private static QueueMoverObservation Ahead(double distFt, double gs) => new(true, distFt, gs);

    private static QueueMoverState Run(params (QueueMoverObservation Obs, double OwnGs)[] steps)
    {
        var s = QueueMoverState.Initial;
        foreach (var (obs, own) in steps) s = QueueMovementPolicy.Step(s, obs, own);
        return s;
    }

    // ── the mover ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Stopped_ahead_records_the_closest_gap()
    {
        var s = Run((Ahead(300, 0), 0), (Ahead(250, 0.5), 0), (Ahead(260, 0), 0));
        Assert.Equal(QueueMoverPhase.StoppedAhead, s.Phase);
        Assert.Equal(250.0, s.StoppedGapFt);
    }

    [Fact]
    public void A_speed_edge_departs_and_the_departure_latches_until_announced()
    {
        var s = Run((Ahead(250, 0), 0), (Ahead(252, 3), 0));
        Assert.Equal(QueueMoverPhase.Departed, s.Phase);
        // Outranked for several evaluations: still Departed (R15).
        s = QueueMovementPolicy.Step(s, Ahead(270, 4), 0);
        s = QueueMovementPolicy.Step(s, Ahead(300, 5), 0);
        Assert.Equal(QueueMoverPhase.Departed, s.Phase);
        Assert.Equal(QueueMoverPhase.Announced, QueueMovementPolicy.MarkAnnounced(s).Phase);
    }

    [Fact]
    public void A_creeper_departs_on_the_opening_gap()
    {
        // 1.8 kt: above the 1.5 kt "stopped" gate, below the 2 kt speed edge — only the gap can tell.
        var s = Run((Ahead(400, 0), 0), (Ahead(430, 1.8), 0));
        Assert.Equal(QueueMoverPhase.StoppedAhead, s.Phase);   // 30 ft: not yet
        s = QueueMovementPolicy.Step(s, Ahead(470, 1.8), 0);
        Assert.Equal(QueueMoverPhase.Departed, s.Phase);         // 70 ft: departed
    }

    [Fact]
    public void Traffic_never_seen_stopped_ahead_is_not_a_departure()
        => Assert.Equal(QueueMoverPhase.None, Run((Ahead(300, 3), 0), (Ahead(330, 3), 0)).Phase);

    [Fact]
    public void The_pilot_taxiing_resets_the_baseline()
    {
        // L2: stopped ahead at 250, the pilot rolls off at 8 kt, later slows with the same leader
        // still taxiing at 330 ft — that is not a departure.
        var s = Run((Ahead(250, 0), 0), (Ahead(260, 0), 8), (Ahead(330, 3), 0));
        Assert.Equal(QueueMoverPhase.None, s.Phase);
    }

    [Fact]
    public void Leaving_the_cone_resets()
    {
        var s = Run((Ahead(250, 0), 0), (Ahead(252, 3), 0));
        s = QueueMovementPolicy.Step(s, new QueueMoverObservation(false, 400, 5), 0);
        Assert.Equal(QueueMoverPhase.None, s.Phase);
    }

    [Fact]
    public void An_announced_mover_that_stops_again_is_rebaselined()
    {
        var s = QueueMovementPolicy.MarkAnnounced(Run((Ahead(250, 0), 0), (Ahead(252, 3), 0)));
        s = QueueMovementPolicy.Step(s, Ahead(330, 3), 0);
        Assert.Equal(QueueMoverPhase.Announced, s.Phase);
        s = QueueMovementPolicy.Step(s, Ahead(340, 0), 0);
        Assert.Equal(QueueMoverPhase.StoppedAhead, s.Phase);
        Assert.Equal(340.0, s.StoppedGapFt);
        s = QueueMovementPolicy.Step(s, Ahead(342, 2.5), 0);
        Assert.Equal(QueueMoverPhase.Departed, s.Phase);
    }

    [Theory]
    [InlineData(true, 260.0, 250.0, true)]     // moving off along the route
    [InlineData(false, 400.0, 250.0, false)]   // moving, but not along the route (toward / crossing)
    [InlineData(null, 270.0, 250.0, true)]     // no route: the gap has grown
    [InlineData(null, 255.0, 250.0, false)]    // no route: not yet
    public void ShouldArmNudge(bool? along, double dist, double gap, bool expected)
        => Assert.Equal(expected, QueueMovementPolicy.ShouldArmNudge(along, dist, gap));

    // ── the nudge ───────────────────────────────────────────────────────────────────────

    private static string Ft(double ft) => $"{Math.Round(ft)} feet";

    private static NudgeDecision Nudge(NudgeState s, bool allowed = true, double ownGs = 0,
        double? nearest = 400, int secondsLater = 21)
        // Tests that don't distinguish the two distances (K1) pass the same value for both.
        => QueueMovementPolicy.EvaluateNudge(s, allowed, ownGs, nearest, nearest, T0.AddSeconds(secondsLater), Ft);

    [Fact]
    public void A_disarmed_nudge_does_nothing()
        => Assert.Equal(NudgeAction.None, Nudge(NudgeState.Disarmed).Action);

    [Fact]
    public void The_nudge_speaks_after_twenty_seconds_with_the_gap_to_the_nearest_aircraft()
    {
        var d = Nudge(NudgeState.ArmedAt(T0));
        Assert.Equal(NudgeAction.Speak, d.Action);
        Assert.Equal("Move up. 400 feet to the traffic ahead.", d.Text);
    }

    [Fact]
    public void With_nothing_ahead_the_traffic_has_taxied_on()
        => Assert.Equal("Move up. The traffic ahead has taxied on.",
            Nudge(NudgeState.ArmedAt(T0), nearest: null).Text);

    [Fact]
    public void Too_soon_waits()
        => Assert.Equal(NudgeAction.None, Nudge(NudgeState.ArmedAt(T0), secondsLater: 19).Action);

    [Theory]
    [InlineData(false, 0.0, 400.0)]   // at a gate, a hold, on a runway, arrived: not a queue prompt
    [InlineData(true, 2.0, 400.0)]    // the pilot is rolling
    [InlineData(true, 0.0, 200.0)]    // something else is already close in front
    public void The_nudge_disarms(bool allowed, double ownGs, double nearest)
        => Assert.Equal(NudgeAction.Disarm,
            Nudge(NudgeState.ArmedAt(T0), allowed, ownGs, nearest).Action);

    [Fact]
    public void Creeping_between_one_and_two_knots_neither_speaks_nor_disarms()
        => Assert.Equal(NudgeAction.None, Nudge(NudgeState.ArmedAt(T0), ownGs: 1.5).Action);

    [Fact]
    public void Three_prompts_is_the_most()
    {
        var s = NudgeState.ArmedAt(T0);
        for (int i = 0; i < QueueMovementPolicy.NudgeMax; i++)
            s = QueueMovementPolicy.AfterSpoken(s, T0);
        Assert.Equal(3, s.Count);
        Assert.Equal(NudgeAction.Disarm, Nudge(s).Action);
    }

    [Fact]
    public void AfterSpoken_restarts_the_interval()
    {
        var s = QueueMovementPolicy.AfterSpoken(NudgeState.ArmedAt(T0), T0.AddSeconds(21));
        Assert.Equal(NudgeAction.None, QueueMovementPolicy.EvaluateNudge(s, true, 0, 400, 400, T0.AddSeconds(30), Ft).Action);
        Assert.Equal(NudgeAction.Speak, QueueMovementPolicy.EvaluateNudge(s, true, 0, 400, 400, T0.AddSeconds(42), Ft).Action);
    }

    // ── the leader is not "something else ahead" (PR #247 final review H1) ────────────────
    // The owner's rule is "never with anything ELSE within 250 ft ahead". The aircraft whose departure
    // armed the nudge reaches 2 kt a few feet from where it sat, so one second later it was still within
    // 250 ft and, counted as the nearest aircraft ahead, disarmed the nudge it had just armed.
    // ...but only while it is still MOVING (PR #247 re-review M3): a leader that crept 20-40 ft and
    // stopped again stayed exempt, and "Move up. 200 feet to the traffic ahead." was spoken into a gap
    // NudgeMinGapFt calls nothing to move up into, inside the Warning distance.

    [Fact]
    public void The_leader_moving_within_250_ft_is_not_counted()
        => Assert.Null(QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 180.0, 3.0) }, leaderId: 7u));

    [Fact]
    public void The_leader_stopped_again_within_250_ft_is_counted()
        => Assert.Equal(200.0, QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 200.0, 0.5) }, leaderId: 7u));

    [Theory]
    [InlineData(GroundTrafficLogic.QueueStoppedGs, 200.0)]          // at the "stopped" line: stopped, counted
    [InlineData(GroundTrafficLogic.QueueStoppedGs + 0.01, null)]    // just above it: still moving, exempt
    public void The_leader_is_exempt_only_above_the_queue_stopped_speed(double leaderGs, double? expected)
        => Assert.Equal(expected, QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 200.0, leaderGs) }, leaderId: 7u));

    [Fact]
    public void Another_aircraft_ahead_is_counted_beside_the_leader()
        => Assert.Equal(200.0, QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 180.0, 3.0), (9u, 200.0, 0.0) }, leaderId: 7u));

    [Fact]
    public void With_no_leader_the_plain_nearest_is_counted()
        => Assert.Equal(180.0, QueueMovementPolicy.NearestOtherAheadFt(new[] { (9u, 200.0, 0.0), (7u, 180.0, 3.0) }, leaderId: null));

    [Fact]
    public void Nothing_ahead_is_null()
        => Assert.Null(QueueMovementPolicy.NearestOtherAheadFt(Array.Empty<(uint, double, double)>(), leaderId: 7u));

    [Fact]
    public void The_leader_pulling_away_no_longer_disarms_the_nudge()
    {
        double? other = QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 180.0, 3.0) }, leaderId: 7u);
        Assert.Equal(NudgeAction.Speak, Nudge(NudgeState.ArmedAt(T0), nearest: other).Action);
    }

    [Fact]
    public void The_leader_stopped_again_within_250_ft_disarms_the_nudge()
    {
        double? other = QueueMovementPolicy.NearestOtherAheadFt(new[] { (7u, 200.0, 0.5) }, leaderId: 7u);
        Assert.Equal(NudgeAction.Disarm, Nudge(NudgeState.ArmedAt(T0), nearest: other).Action);
    }

    // ── the two distances are separate (PR #247 B5 follow-up K1) ──────────────────────────
    // EvaluateNudge takes nearestOtherAheadFt (leader excluded, disarm only) and nearestAheadFt
    // (leader included, spoken text only). Before this split, the disarm-only distance also drove the
    // text, so a leader that stopped again a short way ahead — with nothing else around — was reported
    // as "The traffic ahead has taxied on." instead of naming the real distance to it.

    [Fact]
    public void The_leader_stopped_again_nearby_is_still_named_in_the_move_up_text()
    {
        // nearestOtherAheadFt: null (nothing else ahead), nearestAheadFt: 300 (the leader, 300 ft ahead).
        var d = QueueMovementPolicy.EvaluateNudge(NudgeState.ArmedAt(T0), true, 0,
            null, 300, T0.AddSeconds(21), Ft);
        Assert.Equal(NudgeAction.Speak, d.Action);
        Assert.Equal("Move up. 300 feet to the traffic ahead.", d.Text);
    }

    [Fact]
    public void Another_aircraft_200_feet_ahead_disarms()
        // nearestOtherAheadFt: 200 — something other than the leader, inside the 250 ft gap.
        => Assert.Equal(NudgeAction.Disarm,
            QueueMovementPolicy.EvaluateNudge(NudgeState.ArmedAt(T0), true, 0,
                200, 200, T0.AddSeconds(21), Ft).Action);

    [Fact]
    public void The_leader_alone_205_feet_ahead_does_not_disarm()
    {
        // nearestOtherAheadFt: null (the 205 ft aircraft IS the leader, excluded), nearestAheadFt: 205.
        var d = QueueMovementPolicy.EvaluateNudge(NudgeState.ArmedAt(T0), true, 0,
            null, 205, T0.AddSeconds(21), Ft);
        Assert.Equal(NudgeAction.Speak, d.Action);
        Assert.Equal("Move up. 205 feet to the traffic ahead.", d.Text);
    }
}
