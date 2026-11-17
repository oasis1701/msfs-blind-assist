using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What a pick on a guarded 757 control sends: the calls that open or close the guard and write the
/// control's state var, decided from fresh readings of both. The rules (BlueBird, spec 3.3): a guarded
/// control moves only with its guard open; closing the guard writes the control's <c>CloseSets</c>
/// position (emergency lights ARMED, stab trim cutouts NORMAL), and the app writes that position
/// itself after closing so the control lands whether or not the guard's own click wrote it; a reading
/// that is unread, not a number, or a pick that is none of the control's positions sends nothing.
/// The definition's half (task 2): a pick on a control with a <c>Guard</c> is settled, planned from
/// fresh reads of the control and its guard, sent a pause apart, read back, and says
/// "{label} did not move" only when it did not land. Controls are built in memory with BlueBird's own
/// click code, never from the shipped map.
/// </summary>
public class Bbs757GuardedTests
{
    private const string LightsState = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK";
    private const string LightsGuardNode = "B757_OVHD_SWITCH_GAURD";
    private const string LightsGuardPress =
        "(L:B757_OVHD_SWITCH_GAURD, bool) ! (>L:B757_OVHD_SWITCH_GAURD, bool) (L:B757_OVHD_SWITCH_GAURD, bool) 0 == if{ 1 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK, number) }";

    private const string TrimState = "B757_Stab_trim_C_toggle_switch_CLICK";
    private const string TrimGuardNode = "B757_AISLE_STAND_STAB_1_GAURD_SWITCH";
    private const string TrimGuardPress =
        "(L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) ! (>L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) (L:B757_AISLE_STAND_STAB_1_GAURD_SWITCH, bool) 0 == if{ 0 (>L:B757_Stab_trim_C_toggle_switch_CLICK, number) }";

    private const double Closed = 0;
    private const double Open = 1;

    private static Bbs757Control Lights() => new()
    {
        Node = LightsState, Label = "Emergency Lights", Kind = Bbs757Kinds.Switch, StateVar = LightsState,
        Positions = { new(0, "OFF"), new(1, "ARMED"), new(2, "ON") },
        Guard = LightsGuardNode, GuardClosed = 0, CloseSets = 1,
    };

    private static Bbs757Control LightsGuard(string? press = LightsGuardPress) => new()
    {
        Node = LightsGuardNode, Label = "Emergency Lights Guard", Kind = Bbs757Kinds.Guard, StateVar = LightsGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = press,
    };

    private static Bbs757Control TrimC() => new()
    {
        Node = TrimState, Label = "Stab Trim C", Kind = Bbs757Kinds.Toggle, StateVar = TrimState,
        Positions = { new(0, "NORMAL"), new(1, "CUTOUT") },
        Guard = TrimGuardNode, GuardClosed = 0, CloseSets = 0,
    };

    private static Bbs757Control TrimGuard() => new()
    {
        Node = TrimGuardNode, Label = "Stab Trim C Guard", Kind = Bbs757Kinds.Guard, StateVar = TrimGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = TrimGuardPress,
    };

    private static string LightsWrite(double value) => $"{value.ToString("0.######", CultureInfo.InvariantCulture)} (>L:{LightsState})";

    private static string TrimWrite(double value) => $"{value.ToString("0.######", CultureInfo.InvariantCulture)} (>L:{TrimState})";

    private static void AssertRefused(Bbs757GuardedPlan plan)
    {
        Assert.True(plan.Refused);
        Assert.Empty(plan.Calls);
    }

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- the brief's cases -------------------------------------------------------------------

    [Fact]
    public void Off_from_armed_opens_the_guard_and_writes_zero()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 0);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { LightsGuardPress, "0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void Armed_from_off_closes_the_guard_and_writes_one()
    {
        // Closing the guard is BlueBird's own way to ARMED: the app writes 1 as well, so the control
        // lands even if the guard's click did not write it.
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: Open, target: 1);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { LightsGuardPress, "1 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void On_with_the_guard_open_writes_two()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Open, target: 2);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { "2 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void On_with_the_guard_closed_opens_it_first()
    {
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 2);

        Assert.Equal(new[] { LightsGuardPress, LightsWrite(2) }, plan.Calls);
    }

    [Fact]
    public void Already_there_sends_nothing()
    {
        // Not a refusal: the pick landed already, so the pilot hears nothing at all.
        var open = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 2, guardState: Open, target: 2);
        var closedAtCloseSets = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 1);

        Assert.False(open.Refused);
        Assert.Empty(open.Calls);
        Assert.False(closedAtCloseSets.Refused);
        Assert.Empty(closedAtCloseSets.Calls);
    }

    [Fact]
    public void Cutout_opens_and_writes_one()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 0, guardState: Closed, target: 1);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { TrimGuardPress, "1 (>L:B757_Stab_trim_C_toggle_switch_CLICK)" }, plan.Calls);
    }

    [Fact]
    public void Normal_closes_the_guard_and_writes_zero()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 1, guardState: Open, target: 0);

        Assert.False(plan.Refused);
        Assert.Equal(new[] { TrimGuardPress, "0 (>L:B757_Stab_trim_C_toggle_switch_CLICK)" }, plan.Calls);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(1.0, null)]
    [InlineData(null, null)]
    public void Unread_state_refuses(double? current, double? guardState)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current, guardState, target: 0));
    }

    [Fact]
    public void Calls_are_invariant_under_de_DE()
    {
        InCulture("de-DE", () =>
        {
            Assert.Equal(new[] { LightsGuardPress, "0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" },
                Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 0).Calls);

            // A fractional position is the case a comma culture would break: "2,5" is not RPN.
            var fractional = Lights();
            fractional.Positions.Add(new Bbs757Position(2.5, "HALF"));
            Assert.Equal(new[] { "2.5 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" },
                Bbs757Guarded.Plan(fractional, LightsGuard(), current: 1, guardState: Open, target: 2.5).Calls);
        });
    }

    // ---- the other refusals (ruling R2) --------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(1.0, double.NaN)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void A_reading_that_is_not_a_number_refuses(double current, double guardState)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current, guardState, target: 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_control_with_no_state_var_refuses(string? stateVar)
    {
        var control = Lights();
        control.StateVar = stateVar;

        AssertRefused(Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 2));
    }

    [Theory]
    [InlineData(3.0)]
    [InlineData(0.5)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_target_that_is_no_position_refuses(double target)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Open, target));
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target));
    }

    [Fact]
    public void A_control_with_no_guard_closed_value_refuses()
    {
        var control = Lights();
        control.GuardClosed = null;

        AssertRefused(Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_guard_with_no_press_refuses(string? press)
    {
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(press), current: 1, guardState: Open, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), LightsGuard(press), current: 1, guardState: Closed, target: 2));
    }

    [Fact]
    public void A_guard_with_no_open_position_refuses()
    {
        // Only a Closed position (or none): there is no value to toggle the guard to.
        var onlyClosed = LightsGuard();
        onlyClosed.Positions.RemoveAt(1);
        var noPositions = LightsGuard();
        noPositions.Positions.Clear();

        AssertRefused(Bbs757Guarded.Plan(Lights(), onlyClosed, current: 1, guardState: Open, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), onlyClosed, current: 1, guardState: Closed, target: 2));
        AssertRefused(Bbs757Guarded.Plan(Lights(), noPositions, current: 1, guardState: Closed, target: 2));
    }

    [Fact]
    public void An_open_value_the_closed_reading_cannot_tell_apart_refuses()
    {
        // The guard's "open" position is within the read-back slack of the closed reading: toggling
        // "to" it would be no toggle at all, so nothing is sent rather than a state write behind a closed guard.
        var guard = LightsGuard();
        guard.Positions[1] = new Bbs757Position(0.0011, "Open");

        AssertRefused(Bbs757Guarded.Plan(Lights(), guard, current: 1, guardState: 0.0009, target: 2));
    }

    // ---- guard and target interplay ------------------------------------------------------------

    [Fact]
    public void A_guard_closed_with_a_pick_that_is_not_close_sets_opens_it_and_writes()
    {
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 1, guardState: Closed, target: 1);

        // Cutout already reads 1 behind a closed guard (an odd pair of readings): the plan still opens
        // the guard first, since a target other than CloseSets always needs it open.
        Assert.Equal(new[] { TrimGuardPress, TrimWrite(1) }, plan.Calls);
    }

    [Fact]
    public void A_pick_that_is_close_sets_with_the_guard_already_closed_writes_only_when_it_differs()
    {
        var differs = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: Closed, target: 1);
        var same = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: Closed, target: 1);

        Assert.Equal(new[] { LightsWrite(1) }, differs.Calls);
        Assert.Empty(same.Calls);
    }

    [Fact]
    public void A_pick_that_is_close_sets_with_the_guard_open_closes_it_even_when_it_already_reads_it()
    {
        // The guard is still open, so the pilot's pick is "cover the switch": close it, then the write.
        var plan = Bbs757Guarded.Plan(TrimC(), TrimGuard(), current: 0, guardState: Open, target: 0);

        Assert.Equal(new[] { TrimGuardPress, TrimWrite(0) }, plan.Calls);
    }

    [Fact]
    public void A_control_with_no_close_sets_never_closes_the_guard()
    {
        var control = Lights();
        control.CloseSets = null;

        var open = Bbs757Guarded.Plan(control, LightsGuard(), current: 0, guardState: Open, target: 1);
        var closed = Bbs757Guarded.Plan(control, LightsGuard(), current: 0, guardState: Closed, target: 1);
        var there = Bbs757Guarded.Plan(control, LightsGuard(), current: 1, guardState: Open, target: 1);

        Assert.Equal(new[] { LightsWrite(1) }, open.Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(1) }, closed.Calls);
        Assert.Empty(there.Calls);
    }

    [Fact]
    public void Readings_within_the_read_back_slack_count_as_the_position()
    {
        // 1.0004 is the open guard and the ON switch; 0.0004 is the closed guard.
        Assert.Empty(Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 2.0004, guardState: 1.0004, target: 2).Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(2) },
            Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 1, guardState: 0.0004, target: 2).Calls);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(1) },
            Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: 1.0004, target: 1).Calls);
    }

    [Fact]
    public void A_state_write_goes_out_whatever_finite_value_the_control_reads()
    {
        // The write is absolute (SetLVar), so a switch reading off its positions is still written to.
        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 7, guardState: Open, target: 2);

        Assert.Equal(new[] { LightsWrite(2) }, plan.Calls);
    }

    [Fact]
    public void Steps_are_a_pause_apart()
    {
        // The guard's click and animation must land before the state write follows it.
        Assert.Equal(300, Bbs757Guarded.StepGapMs);
    }

    // ---- picks through the definition (task 2) -------------------------------------------------

    /// <summary>A 757 definition built from in-memory controls, with a fake sim behind its writes and a speech capture.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly BBS757Definition Def;
        private readonly SimConnectManager _manager = new(IntPtr.Zero);

        public Rig(params Bbs757Control[] controls)
        {
            var map = new Bbs757ControlMap();
            foreach (var control in controls) map.Controls.Add(control);
            Def = new BBS757Definition(map) { NestRunning = () => true, WriteIo = _ => Sim.Io };
        }

        /// <summary>The panel handler's answer to a pick of <paramref name="node"/>.</summary>
        public bool Pick(string node, double value)
            => Def.HandleUIVariableSet(node, value, new SimVarDefinition(), _manager, Speech);

        /// <summary>The aircraft takes the app's writes: a state write is what the control reads afterwards, a guard press flips the guard.</summary>
        public Rig TakingWrites(Bbs757Control control, Bbs757Control guard)
        {
            string suffix = $" (>L:{control.StateVar})";
            Sim.OnWrite = rpn =>
            {
                if (rpn == guard.Press)
                    Sim.Fresh[guard.Node] = Sim.Fresh[guard.Node] == Closed ? Open : Closed;
                else if (rpn.EndsWith(suffix, StringComparison.Ordinal))
                    Sim.Fresh[control.Node] = double.Parse(rpn[..^suffix.Length], CultureInfo.InvariantCulture);
            };
            return this;
        }

        /// <summary>What the cockpit reads before the pick.</summary>
        public Rig Reading(Bbs757Control control, double controlAt, Bbs757Control guard, double guardAt)
        {
            Sim.Fresh[control.Node] = controlAt;
            Sim.Fresh[guard.Node] = guardAt;
            return this;
        }
    }

    [Fact]
    public void Guarded_pick_sends_the_plan()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);

        Assert.True(rig.Pick(LightsState, 0));

        var calls = new[] { LightsGuardPress, "0 (>L:B757_OVHD_PASS_OXY_Toggle_Switch_CLICK)" };
        Assert.Equal(calls, rig.Sim.Writes);
        // Nothing but the settle comes before the first call, and one step gap lies between the two.
        var events = rig.Sim.Events;
        int first = events.IndexOf(Bbs757FakeIo.WriteEvent(calls[0]));
        Assert.All(events.Take(first), e => Assert.StartsWith("delay", e));
        Assert.Equal(new[] { Bbs757FakeIo.WriteEvent(calls[0]), Bbs757FakeIo.DelayEvent(Bbs757Guarded.StepGapMs), Bbs757FakeIo.WriteEvent(calls[1]) },
            events.Skip(first).Take(3));
        // The screen reader read the combo, and the control reads back as picked: nothing more to say.
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Guarded_pick_that_does_not_land_says_so()
    {
        var lights = Lights();
        var guard = LightsGuard();
        // The aircraft takes no write: the switch still reads ARMED at the read-back.
        var rig = new Rig(lights, guard).Reading(lights, 1, guard, Closed);

        Assert.True(rig.Pick(LightsState, 0));

        Assert.Equal(new[] { LightsGuardPress, LightsWrite(0) }, rig.Sim.Writes);
        Assert.Equal(new[] { "Emergency Lights did not move" }, rig.Speech.All);
    }

    [Fact]
    public void A_read_back_that_cannot_be_read_counts_as_not_landed()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.OnWrite = rpn => { if (rpn == LightsWrite(0)) rig.Sim.Fresh[LightsState] = null; };

        Assert.True(rig.Pick(LightsState, 0));

        Assert.Equal(new[] { "Emergency Lights did not move" }, rig.Speech.All);
    }

    [Fact]
    public async Task Two_quick_picks_send_only_the_last()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal < 2;   // both picks sit in their settle

        rig.Pick(LightsState, 2);   // arrowing past ON ...
        rig.Pick(LightsState, 0);   // ... on to OFF
        Assert.Equal(1, rig.Sim.CanceledDelays);   // the first pick's settle ended when the second arrived
        Assert.Empty(rig.Sim.Writes);

        rig.Sim.Release(1);          // the second pick's settle
        await rig.Sim.ResyncsReached(2);

        Assert.Equal(new[] { LightsGuardPress, LightsWrite(0) }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);   // the first pick says nothing, not even that it was dropped
    }

    [Fact]
    public async Task A_newer_pick_does_not_cut_a_sequence_short()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal is 1 or 2;   // the first pick's gap between its calls, the second pick's settle

        rig.Pick(LightsState, 0);   // sends the guard's press, then waits out the step gap
        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);
        rig.Pick(LightsState, 2);   // supersedes it while it waits

        rig.Sim.Release(1);
        await rig.Sim.WritesReached(2);
        // The guard is not left open with no state write behind it.
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(0) }, rig.Sim.Writes);

        rig.Sim.Release(2);
        await rig.Sim.ResyncsReached(2);
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(0), LightsWrite(2) }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_superseded_pick_that_finished_its_calls_leaves_the_speaking_to_the_newer_pick()
    {
        var lights = Lights();
        var guard = LightsGuard();
        // The aircraft takes no write, so the first pick's read-back would say "did not move" if it ran.
        var rig = new Rig(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal is 1 or 2;

        rig.Pick(LightsState, 0);
        rig.Pick(LightsState, 2);
        rig.Sim.Release(1);
        await rig.Sim.WritesReached(2);
        rig.Sim.Release(2);
        await rig.Sim.ResyncsReached(2);

        Assert.Equal(new[] { LightsGuardPress, LightsWrite(0), LightsGuardPress, LightsWrite(2) }, rig.Sim.Writes);
        Assert.Equal(new[] { "Emergency Lights did not move" }, rig.Speech.All);   // once, the newer pick's
        Assert.Equal(new[] { LightsState, LightsGuardNode }, rig.Sim.Resyncs);     // and only it re-reads
    }

    [Fact]
    public async Task A_flight_load_in_the_step_gap_stops_the_sequence()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal == 1;   // the step gap between the two calls

        var pick = rig.Def.SetGuardedAsync(lights, guard, 0, rig.Sim.Io, rig.Speech);
        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);   // the guard's press has gone, the gap is held

        rig.Def.OnSimContextReset();   // a flight load: the pick is dropped
        rig.Sim.Release(1);
        Assert.False(await pick.WaitAsync(TimeSpan.FromSeconds(30)));

        // The state write does not go out into the flight that follows; nothing is said or re-read.
        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);
        Assert.False(rig.Def.IsPickPending(LightsState));
    }

    [Fact]
    public async Task A_newer_pick_after_a_flight_load_does_not_revive_the_older_sequence()
    {
        // The newer pick arrives after the load and before the gap ends: it replaces the older pick's
        // entry, so only the load count can tell the older one it was dropped, not superseded.
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal == 1;

        var older = rig.Def.SetGuardedAsync(lights, guard, 0, rig.Sim.Io, rig.Speech);
        rig.Def.OnSimContextReset();
        var newer = rig.Def.SetGuardedAsync(lights, guard, 2, rig.Sim.Io, rig.Speech);   // not held: runs to its end
        Assert.True(await newer.WaitAsync(TimeSpan.FromSeconds(30)));
        rig.Sim.Release(1);
        Assert.False(await older.WaitAsync(TimeSpan.FromSeconds(30)));

        // The guard's press, then the newer pick's calls (the guard already reads open after the press): no state write 0.
        Assert.Equal(new[] { LightsGuardPress, LightsWrite(2) }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task Switching_away_in_the_step_gap_stops_the_sequence()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.Hold = ordinal => ordinal == 1;

        var pick = rig.Def.SetGuardedAsync(lights, guard, 0, rig.Sim.Io, rig.Speech);
        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();   // the pilot picks another aircraft profile
        rig.Sim.Release(1);         // a no-op if the switch already ended the wait
        Assert.False(await pick.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Unread_state_says_unavailable_and_writes_nothing(bool controlUnread, bool guardUnread)
    {
        var rig = new Rig(Lights(), LightsGuard());
        rig.Sim.Fresh[LightsState] = controlUnread ? null : 1;
        rig.Sim.Fresh[LightsGuardNode] = guardUnread ? null : Closed;

        Assert.True(rig.Pick(LightsState, 0));

        Assert.Equal(new[] { "Emergency Lights unavailable" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Writes);
        // Both rows are asked again, so the combos show where the aircraft really is.
        Assert.Equal(new[] { LightsState, LightsGuardNode }, rig.Sim.Resyncs);
    }

    [Fact]
    public void A_pick_with_no_write_path_says_unavailable_and_writes_nothing()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).Reading(lights, 1, guard, Closed);
        rig.Sim.CanWrite = false;

        Assert.True(rig.Pick(LightsState, 0));

        Assert.Equal(new[] { "Emergency Lights unavailable" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public void A_guard_missing_from_the_map_says_unavailable()
    {
        var orphan = Lights();
        orphan.Guard = "B757_NO_SUCH_GUARD";
        var rig = new Rig(orphan);   // the guard's row is not in the map
        rig.Sim.Fresh[LightsState] = 1;
        rig.Sim.Fresh[LightsGuardNode] = Closed;

        Assert.True(rig.Pick(LightsState, 0));

        Assert.Equal(new[] { "Emergency Lights unavailable" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Writes);
        Assert.Equal(new[] { LightsState }, rig.Sim.Resyncs);   // the combo goes back to the aircraft's position
    }

    [Fact]
    public void Both_rows_are_read_back_after_a_write()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 1, guard, Closed);

        rig.Pick(LightsState, 2);

        Assert.Equal(new[] { LightsState, LightsGuardNode }, rig.Sim.Resyncs);
    }

    [Fact]
    public void A_pick_that_is_already_there_says_nothing_and_writes_nothing()
    {
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).Reading(lights, 2, guard, Open);

        Assert.True(rig.Pick(LightsState, 2));

        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public void A_stab_trim_cutout_pick_opens_its_guard_first()
    {
        // The route is the control's Guard field, not its kind: a toggle goes the same way.
        var trim = TrimC();
        var guard = TrimGuard();
        var rig = new Rig(trim, guard).TakingWrites(trim, guard).Reading(trim, 0, guard, Closed);

        Assert.True(rig.Pick(TrimState, 1));

        Assert.Equal(new[] { TrimGuardPress, TrimWrite(1) }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    // ---- a pick on a guard's own row -----------------------------------------------------------

    [Fact]
    public void Picking_a_guard_row_rereads_the_control_it_covers()
    {
        // Closing the guard makes BlueBird write CloseSets to the control it covers (measured: the
        // emergency lights go to ARMED). The covered control is OnRequest, read only when its panel is
        // built, so without its own re-read the pilot would read "Emergency Lights: OFF" over an ARMED switch.
        var lights = Lights();
        var guard = LightsGuard();
        var rig = new Rig(lights, guard).TakingWrites(lights, guard).Reading(lights, 0, guard, Open);

        Assert.True(rig.Pick(LightsGuardNode, Closed));

        Assert.Equal(new[] { LightsGuardPress }, rig.Sim.Writes);
        Assert.Equal(2, rig.Sim.Resyncs.Count);
        Assert.Equal(new[] { LightsGuardNode, LightsState }.Order(StringComparer.Ordinal),
            rig.Sim.Resyncs.Order(StringComparer.Ordinal));
        Assert.Empty(rig.Speech.All);   // the screen reader read the guard's combo; the re-read speaks nothing itself
    }

    [Fact]
    public void Picking_an_unguarding_row_rereads_only_itself()
    {
        // A control that guards nothing has no dependants: only its own node is read again, even
        // with a guarded pair in the same map.
        var plain = new Bbs757Control
        {
            Node = "B757_PLAIN_TOGGLE", Label = "Plain Toggle", Kind = Bbs757Kinds.Toggle, StateVar = "B757_PLAIN_TOGGLE",
            Positions = { new(0, "Off"), new(1, "On") },
            Press = "(L:B757_PLAIN_TOGGLE, bool) ! (>L:B757_PLAIN_TOGGLE, bool)",
        };
        var rig = new Rig(Lights(), LightsGuard(), plain);
        rig.Sim.Fresh[plain.Node] = 0;
        rig.Sim.Fresh[LightsState] = 0;
        rig.Sim.Fresh[LightsGuardNode] = Closed;

        Assert.True(rig.Pick(plain.Node, 1));

        Assert.Equal(new[] { plain.Press! }, rig.Sim.Writes);
        Assert.Equal(new[] { plain.Node }, rig.Sim.Resyncs);
    }
}

/// <summary>
/// A simulated sim behind the 757 definition's panel writes (<c>WriteIo = _ =&gt; fake.Io</c>): fresh and
/// cached reads by key, every write and wait in the order they happened (<see cref="Events"/>), and the
/// resyncs asked. A wait ends at once unless <see cref="Hold"/> keeps it open, so a pick with nothing
/// held finishes inside <c>HandleUIVariableSet</c> and a test needs no clock; to interleave two picks it
/// holds the waits it wants, lets them go one at a time with <see cref="Release"/> and awaits
/// <see cref="WritesReached"/> or <see cref="ResyncsReached"/>. A held wait ends canceled when its
/// token is canceled, as a real one does.
/// </summary>
internal sealed class Bbs757FakeIo
{
    private readonly object _lock = new();
    private readonly Dictionary<int, TaskCompletionSource> _held = new();
    private readonly List<(Func<bool> Done, TaskCompletionSource Signal)> _waiters = new();
    private int _canceledDelays;

    /// <summary>What <c>CanWrite</c> answers.</summary>
    public bool CanWrite = true;

    /// <summary>The fresh read of a key; a missing key or a null value is an unread variable.</summary>
    public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);

    /// <summary>When set, answers a fresh read before <see cref="Fresh"/> is consulted, so a test can answer a sequence.</summary>
    public Func<string, double?>? OnFreshRead;

    /// <summary>The cached read of a key.</summary>
    public readonly Dictionary<string, double?> Cache = new(StringComparer.Ordinal);

    /// <summary>The calculator writes made, in order.</summary>
    public readonly List<string> Writes = new();

    /// <summary>The forced re-reads asked, in order.</summary>
    public readonly List<string> Resyncs = new();

    /// <summary>Every wait asked for, in order, in milliseconds.</summary>
    public readonly List<int> Delays = new();

    /// <summary>Every write and wait in the order they happened: <see cref="WriteEvent"/> and <see cref="DelayEvent"/> strings.</summary>
    public readonly List<string> Events = new();

    /// <summary>Called after each write is recorded, so a test can make the aircraft follow it (a fresh read that changes).</summary>
    public Action<string>? OnWrite;

    /// <summary>True for the 0-based ordinals of the waits that stay open until <see cref="Release"/>; null holds none.</summary>
    public Func<int, bool>? Hold;

    /// <summary>How many held waits ended because their token was canceled.</summary>
    public int CanceledDelays => Volatile.Read(ref _canceledDelays);

    public static string WriteEvent(string rpn) => "write " + rpn;

    public static string DelayEvent(int milliseconds) => "delay " + milliseconds.ToString(CultureInfo.InvariantCulture);

    public Bbs757SimIo Io => new()
    {
        CanWrite = () => CanWrite,
        ReadFresh = (key, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (OnFreshRead != null) return Task.FromResult(OnFreshRead(key));
            return Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null);
        },
        Cached = key => Cache.TryGetValue(key, out double? v) ? v : null,
        Write = rpn =>
        {
            lock (_lock) { Writes.Add(rpn); Events.Add(WriteEvent(rpn)); }
            OnWrite?.Invoke(rpn);
            Signal();
        },
        RequestRead = key =>
        {
            lock (_lock) Resyncs.Add(key);
            Signal();
        },
        Delay = Wait,
    };

    /// <summary>Lets the held wait with this 0-based ordinal go.</summary>
    public void Release(int ordinal)
    {
        TaskCompletionSource? gate;
        lock (_lock) _held.Remove(ordinal, out gate);
        gate?.TrySetResult();
    }

    /// <summary>Completes when at least <paramref name="count"/> writes have been made.</summary>
    public Task WritesReached(int count) => Reached(() => Writes.Count >= count);

    /// <summary>Completes when at least <paramref name="count"/> re-reads have been asked.</summary>
    public Task ResyncsReached(int count) => Reached(() => Resyncs.Count >= count);

    private Task Wait(int milliseconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int ordinal;
        lock (_lock)
        {
            ordinal = Delays.Count;
            Delays.Add(milliseconds);
            Events.Add(DelayEvent(milliseconds));
        }
        if (Hold?.Invoke(ordinal) != true) return Task.CompletedTask;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock) _held[ordinal] = gate;
        ct.Register(() =>
        {
            if (gate.TrySetCanceled(ct)) Interlocked.Increment(ref _canceledDelays);
        });
        return gate.Task;
    }

    private Task Reached(Func<bool> done)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            if (done()) signal.SetResult();
            else _waiters.Add((done, signal));
        }
        // The timeout only keeps a broken test from hanging; no passing test waits on it.
        return signal.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private void Signal()
    {
        lock (_lock)
        {
            for (int i = _waiters.Count - 1; i >= 0; i--)
            {
                if (!_waiters[i].Done()) continue;
                _waiters[i].Signal.TrySetResult();
                _waiters.RemoveAt(i);
            }
        }
    }
}
