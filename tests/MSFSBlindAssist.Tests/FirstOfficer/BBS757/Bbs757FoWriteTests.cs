using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 10, task 4: the definition's silent, awaitable write path for the First Officer
/// (<c>FoSetAsync</c>, <c>FoPressAsync</c>, <c>FoWriteNow</c>). It reuses the panel's own decisions
/// and sim access (<c>WriteIo</c>, here a <see cref="Bbs757FakeIo"/>), returns true only when the
/// control reads its target afterwards, refuses when a write cannot land, and speaks nothing.
/// </summary>
public class Bbs757FoWriteTests
{
    private const string Press = "(L:X,bool) ! (>L:X,bool)";
    private const string ButtonPress = "1 (>L:B)";

    private static Bbs757Control Toggle() => new()
    {
        Node = "X", Label = "Thing", Kind = Bbs757Kinds.Toggle, StateVar = "X",
        Positions = { new(0, "OFF"), new(1, "ON") }, Press = Press,
    };

    private static Bbs757Control Button() => new()
    {
        Node = "B", Label = "Button", Kind = Bbs757Kinds.Button, Press = ButtonPress,
    };

    private const string LightsState = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK";
    private const string LightsGuardNode = "B757_OVHD_SWITCH_GAURD";
    private const string LightsGuardPress = "(L:B757_OVHD_SWITCH_GAURD, bool) ! (>L:B757_OVHD_SWITCH_GAURD, bool)";

    private static Bbs757Control Lights() => new()
    {
        Node = LightsState, Label = "Emergency Lights", Kind = Bbs757Kinds.Switch, StateVar = LightsState,
        Positions = { new(0, "OFF"), new(1, "ARMED"), new(2, "ON") },
        Guard = LightsGuardNode, GuardClosed = 0, CloseSets = 1,
    };

    private static Bbs757Control LightsGuard() => new()
    {
        Node = LightsGuardNode, Label = "Emergency Lights Guard", Kind = Bbs757Kinds.Guard, StateVar = LightsGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = LightsGuardPress,
    };

    /// <summary>A 757 definition built from in-memory controls with a fake sim behind its writes.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly BBS757Definition Def;
        public readonly SimConnectManager Manager = new(IntPtr.Zero);

        public Rig(params Bbs757Control[] controls) : this((bool?)null, (Func<long>?)null, controls) { }

        public Rig(bool? parkingBrakeRealistic, params Bbs757Control[] controls) : this(parkingBrakeRealistic, (Func<long>?)null, controls) { }

        public Rig(Func<long> nowMs, params Bbs757Control[] controls) : this((bool?)null, nowMs, controls) { }

        private Rig(bool? parkingBrakeRealistic, Func<long>? nowMs, params Bbs757Control[] controls)
        {
            var map = new Bbs757ControlMap();
            foreach (var c in controls) map.Controls.Add(c);
            Def = new BBS757Definition(map)
            {
                NestRunning = () => true, WriteIo = _ => Sim.Io, ParkingBrakeRealistic = () => parkingBrakeRealistic,
                NowMs = nowMs ?? (() => Environment.TickCount64),
            };
        }

        /// <summary>The toggle's press flips what it reads, as BlueBird's does.</summary>
        public Rig TogglingOnPress()
        {
            Sim.OnWrite = rpn =>
            {
                if (rpn == Press) Sim.Fresh["X"] = Sim.Fresh["X"] == 1 ? 0 : 1;
            };
            return this;
        }

        public Task<bool> Set(string node, double target) => Def.FoSetAsync(Manager, node, target, CancellationToken.None);
        public Task<bool> PressNode(string node) => Def.FoPressAsync(Manager, node, CancellationToken.None);
    }

    private const string ParkNode = Bbs757ParkingBrake.Node;
    private const string ParkPress = "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)";

    private static Bbs757Control ParkBrake() => new()
    {
        Node = ParkNode, Label = "Parking Brake", Kind = Bbs757Kinds.Toggle, StateVar = "B757_FC_PARK_BRAKE",
        Positions = { new(0, "Released"), new(1, "Set") }, Press = ParkPress,
    };

    private static Rig ParkRig(bool? realistic)
    {
        var rig = new Rig(realistic, ParkBrake());
        rig.Sim.Fresh[ParkNode] = 0;
        return rig;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData(true)]
    public async Task Parking_brake_the_lever_sets_gets_the_lever_alone(bool? realistic)
    {
        var rig = ParkRig(realistic);
        rig.Sim.OnWrite = rpn => { if (rpn == ParkPress) rig.Sim.Fresh[ParkNode] = 1; };

        Assert.True(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task Parking_brake_in_realistic_mode_is_retried_with_full_braking_and_the_pedals_let_go()
    {
        // REALISTIC mode (measured 2026-10-28): the lever alone does nothing; full braking in the same frame sets it.
        var rig = ParkRig(realistic: true);
        rig.Sim.OnWrite = rpn => { if (rpn == Bbs757ParkingBrake.FullBrakingCode(true)) rig.Sim.Fresh[ParkNode] = 1; };

        Assert.True(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress, Bbs757ParkingBrake.FullBrakingCode(true), Bbs757ParkingBrake.PedalsReleaseCode }, rig.Sim.Writes);
        Assert.Contains("(>K:BRAKES)", Bbs757ParkingBrake.FullBrakingCode(true));
    }

    [Fact]
    public async Task Parking_brake_in_easy_mode_is_never_retried_with_braking()
    {
        var rig = ParkRig(realistic: false);

        Assert.False(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task Parking_brake_that_does_not_set_fails_and_still_lets_the_pedals_go()
    {
        var rig = ParkRig(realistic: true);

        Assert.False(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(Bbs757ParkingBrake.PedalsReleaseCode, rig.Sim.Writes.Last());
    }

    [Fact]
    public async Task Parking_brake_already_set_or_unwritable_sends_nothing()
    {
        var set = ParkRig(realistic: true);
        set.Sim.Fresh[ParkNode] = 1;
        Assert.True(await set.Def.FoSetParkingBrakeCoreAsync(set.Sim.Io, CancellationToken.None));
        Assert.Empty(set.Sim.Writes);

        var refused = ParkRig(realistic: true);
        refused.Sim.CanWrite = false;
        Assert.False(await refused.Def.FoSetParkingBrakeCoreAsync(refused.Sim.Io, CancellationToken.None));
        Assert.Empty(refused.Sim.Writes);
    }

    // ------------------------------------------------------------------ FoSetAsync

    [Fact]
    public async Task A_set_that_lands_returns_true()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;

        Assert.True(await rig.Set("X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
        Assert.Equal(1, rig.Sim.Fresh["X"]);
    }

    /// <summary>The control reads 0 until the press, then answers the given reads in order (the last repeats).</summary>
    private static Rig AnsweringAfterPress(params double[] afterPress)
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 0;
        bool pressed = false;
        int next = 0;
        rig.Sim.OnWrite = _ => pressed = true;
        rig.Sim.OnFreshRead = key =>
        {
            if (!pressed) return rig.Sim.Fresh[key];
            return afterPress[Math.Min(next++, afterPress.Length - 1)];
        };
        return rig;
    }

    [Fact]
    public async Task A_switch_that_lands_at_once_is_done_in_two_reads()
    {
        var rig = AnsweringAfterPress(1);

        Assert.True(await rig.Set("X", 1));
        Assert.Equal(new[] { BBS757Definition.FoSettleMs, BBS757Definition.FoPollMs }, rig.Sim.Delays);
    }

    [Fact]
    public async Task A_switch_that_lands_late_is_polled_until_it_holds()
    {
        var rig = AnsweringAfterPress(0, 0, 1, 1);

        Assert.True(await rig.Set("X", 1));
        Assert.Equal(new[] { 150, 250, 250, 250 }, rig.Sim.Delays);
    }

    [Fact]
    public async Task A_target_read_once_then_lost_is_not_landed()
    {
        var rig = AnsweringAfterPress(1, 0);   // then 0 for ever

        Assert.False(await rig.Set("X", 1));
        Assert.True(rig.Sim.Delays.Sum() <= BBS757Definition.FoLandMaxMs);
    }

    [Fact]
    public async Task A_switch_that_never_lands_gives_up_at_three_seconds()
    {
        var rig = AnsweringAfterPress(0);

        Assert.False(await rig.Set("X", 1));
        int sum = rig.Sim.Delays.Sum();
        Assert.True(sum <= BBS757Definition.FoLandMaxMs);
        Assert.True(sum + BBS757Definition.FoPollMs > BBS757Definition.FoLandMaxMs);
        Assert.Equal(BBS757Definition.FoSettleMs, rig.Sim.Delays[0]);
        Assert.All(rig.Sim.Delays.Skip(1), d => Assert.Equal(BBS757Definition.FoPollMs, d));
    }

    [Fact]
    public async Task A_poll_whose_reads_take_real_time_stops_early_with_a_false_verdict()
    {
        long clock = 0;
        var rig = new Rig(() => clock, Toggle());
        rig.Sim.Fresh["X"] = 0;
        bool pressed = false;
        rig.Sim.OnWrite = _ => pressed = true;
        rig.Sim.OnFreshRead = key =>
        {
            if (!pressed) return rig.Sim.Fresh[key];
            clock += 1200;   // each read takes 1.2 s of real time; the fake waits take none
            return 0;
        };

        Assert.False(await rig.Set("X", 1));
        // Unclocked, the waits alone would run 12 times (150 + 11 x 250 ms); the real time ends it after 3.
        Assert.Equal(new[] { 150, 250, 250 }, rig.Sim.Delays);
    }

    [Fact]
    public async Task The_parking_brake_keeps_its_fixed_settle()
    {
        var rig = ParkRig(realistic: false);
        rig.Sim.OnWrite = rpn => { if (rpn == ParkPress) rig.Sim.Fresh[ParkNode] = 1; };

        Assert.True(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { 1500 }, rig.Sim.Delays);
    }

    [Fact]
    public async Task A_set_already_there_sends_nothing_and_returns_true()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 1;

        Assert.True(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_set_the_aircraft_does_not_take_returns_false()
    {
        var rig = new Rig(Toggle());          // the press goes, the switch never moves
        rig.Sim.Fresh["X"] = 0;

        Assert.False(await rig.Set("X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
    }

    [Fact]
    public async Task A_read_back_that_cannot_be_read_returns_false()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 0;
        rig.Sim.OnWrite = _ => rig.Sim.Fresh["X"] = null;

        Assert.False(await rig.Set("X", 1));
    }

    [Fact]
    public async Task A_set_is_refused_when_nothing_can_land()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;
        rig.Sim.CanWrite = false;

        Assert.False(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task An_unread_control_is_never_pressed()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();     // nothing in Fresh: unread

        Assert.False(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_target_that_is_no_position_is_refused()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;

        Assert.False(await rig.Set("X", 2));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task An_unknown_node_or_a_button_is_refused_by_set()
    {
        var rig = new Rig(Toggle(), Button());
        Assert.False(await rig.Set("NOT_IN_THE_MAP", 1));
        Assert.False(await rig.Set("B", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_canceled_set_returns_false()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(await rig.Def.FoSetAsync(rig.Manager, "X", 1, cts.Token));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_guarded_set_goes_through_the_guard_plan_and_reads_back()
    {
        var rig = new Rig(Lights(), LightsGuard());
        rig.Sim.Fresh[LightsState] = 0;
        rig.Sim.Fresh[LightsGuardNode] = 0;     // closed
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == LightsGuardPress) rig.Sim.Fresh[LightsGuardNode] = rig.Sim.Fresh[LightsGuardNode] == 0 ? 1 : 0;
            else if (rpn.EndsWith($" (>L:{LightsState})", StringComparison.Ordinal))
                rig.Sim.Fresh[LightsState] = double.Parse(rpn.Split(' ')[0], CultureInfo.InvariantCulture);
        };

        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: 0, target: 1);
        Assert.False(plan.Refused);

        Assert.True(await rig.Set(LightsState, 1));    // ARMED
        Assert.Equal(plan.Calls, rig.Sim.Writes);      // the panel's own guarded plan, call for call
        Assert.Equal(1, rig.Sim.Fresh[LightsState]);
    }

    [Fact]
    public async Task A_guarded_set_with_its_guard_unread_is_refused()
    {
        var rig = new Rig(Lights(), LightsGuard());
        rig.Sim.Fresh[LightsState] = 0;            // the guard is not read

        Assert.False(await rig.Set(LightsState, 1));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ FoPressAsync

    [Fact]
    public async Task A_press_is_sent_and_returns_true()
    {
        var rig = new Rig(Button());
        Assert.True(await rig.PressNode("B"));
        Assert.Equal(new[] { ButtonPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task A_press_is_refused_when_nothing_can_land()
    {
        var rig = new Rig(Button());
        rig.Sim.CanWrite = false;
        Assert.False(await rig.PressNode("B"));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_press_of_a_positional_control_or_unknown_node_is_refused()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 0;
        Assert.False(await rig.PressNode("X"));
        Assert.False(await rig.PressNode("NOT_IN_THE_MAP"));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ the preflight tests (plan 19, task 8)

    private const string HeldDown = "1 (>L:H)";
    private const string HeldUp = "0 (>L:H)";

    private static Bbs757Control HeldButton() => new()
    {
        Node = "H", Label = "Oxygen Test", Kind = Bbs757Kinds.Held, StateVar = "H", Press = HeldDown, Release = HeldUp, HoldMs = 3000,
    };

    [Fact]
    public async Task A_held_press_given_a_hold_waits_that_hold_between_down_and_up()
    {
        var rig = new Rig(HeldButton());

        Assert.True(await rig.Def.FoPressCoreAsync(rig.Def.ControlFor("H")!, rig.Sim.Io, CancellationToken.None, holdMs: 10_000));

        Assert.Equal(new[] { Bbs757FakeIo.WriteEvent(HeldDown), Bbs757FakeIo.DelayEvent(10_000), Bbs757FakeIo.WriteEvent(HeldUp) }, rig.Sim.Events);
    }

    [Fact]
    public async Task A_held_press_with_no_hold_given_keeps_the_maps_and_the_definition_passes_the_hold_on()
    {
        var rig = new Rig(HeldButton());

        Assert.True(await rig.PressNode("H"));
        Assert.True(await rig.Def.FoPressAsync(rig.Manager, "H", CancellationToken.None, 10_000));

        Assert.Equal(new[] { 3000, 10_000 }, rig.Sim.Delays);
        Assert.Equal(new[] { HeldDown, HeldUp, HeldDown, HeldUp }, rig.Sim.Writes);
    }

    private const string ConfigLVar = "BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW";
    private const string TakeoffConfigHold = "0 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)";
    private const string ConfigRelease = "1 (>L:BBS757_TO_AND_LAND_CONFIG_P61_TEST_SW)";
    private static Bbs757SpringButton TakeoffConfig => Bbs757SpringControls.ForButtonKey("BBS757_P61_TAKEOFF_CONFIG_TEST")!;
    private static readonly SimConnectManager Unconnected = new(IntPtr.Zero);

    /// <summary>A definition over an empty map, its writes on <paramref name="sim"/>, its clock still (the hold is counted by its waits).</summary>
    private static BBS757Definition SpringDef(Bbs757FakeIo sim)
        => new(new Bbs757ControlMap()) { NestRunning = () => true, WriteIo = _ => sim.Io, NowMs = () => 0 };

    [Fact]
    public async Task A_P61_test_holds_its_position_every_30_ms_for_3_s_releases_and_is_true_when_it_rests()
    {
        var sim = new Bbs757FakeIo();
        sim.Fresh[ConfigLVar] = 1;   // neutral once let go
        var def = SpringDef(sim);

        Assert.True(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None));

        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, 100).Append(ConfigRelease), sim.Writes);
        Assert.Equal(Enumerable.Repeat(30, 100).Append(250), sim.Delays);
        Assert.Equal(0, sim.CanceledDelays);
    }

    [Fact]
    public async Task A_P61_test_refuses_a_trim_button_a_write_that_cannot_land_and_a_stop_already_given()
    {
        var sim = new Bbs757FakeIo();
        sim.Fresh[ConfigLVar] = 1;
        var def = SpringDef(sim);

        Assert.False(await def.FoSpringTestCoreAsync(Bbs757SpringControls.ForButtonKey("BBS757_RUDDER_TRIM_RIGHT")!, sim.Io, CancellationToken.None));
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        Assert.False(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, stopped.Token));
        sim.CanWrite = false;
        Assert.False(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None));

        Assert.Empty(sim.Writes);
        Assert.Empty(sim.Delays);
    }

    /// <summary>One hold per switch, shared with the panel: while the First Officer holds the config switch, neither its
    /// other test nor the panel's press of it starts a hold.</summary>
    [Fact]
    public async Task A_P61_test_is_refused_while_its_switch_is_holding()
    {
        var sim = new Bbs757FakeIo();
        sim.Fresh[ConfigLVar] = 1;
        sim.Hold = ordinal => ordinal == 0;
        var def = SpringDef(sim);
        var speech = new SpeechCapture();

        var first = def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None);
        Assert.False(await def.FoSpringTestCoreAsync(Bbs757SpringControls.ForButtonKey("BBS757_P61_LANDING_CONFIG_TEST")!, sim.Io, CancellationToken.None));
        await def.PressSpringAsync("BBS757_P61_LANDING_CONFIG_TEST", Unconnected, speech).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(new[] { TakeoffConfigHold }, sim.Writes);
        Assert.Empty(speech.All);

        sim.Release(0);
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(30)));

        // Let go: the switch holds again.
        Assert.True(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None));
        Assert.Equal(200, sim.Writes.Count(w => w == TakeoffConfigHold));
    }

    /// <summary>
    /// A switch that does not rest after its three releases is released again in the background, silently (no announcer
    /// anywhere on this path), and keeps its hold until that ends; the test reports false.
    /// </summary>
    [Fact]
    public async Task A_P61_test_whose_switch_does_not_rest_is_false_and_released_again_in_the_background()
    {
        var sim = new Bbs757FakeIo();   // the switch never read: never known to rest
        var def = SpringDef(sim);

        Assert.False(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None));

        // The waits take no time here, so the background release (every 2 s for 60 s) has run out already.
        Assert.Equal(Enumerable.Repeat(TakeoffConfigHold, 100).Concat(Enumerable.Repeat(ConfigRelease, 3 + 30)), sim.Writes);
        Assert.Equal(30, sim.Delays.Count(d => d == Bbs757SpringControls.ReleaseRetryIntervalMs));

        // Once it has ended, the switch may be held again.
        sim.Fresh[ConfigLVar] = 1;
        Assert.True(await def.FoSpringTestCoreAsync(TakeoffConfig, sim.Io, CancellationToken.None));
    }

    // ------------------------------------------------------------------ FoWriteNow (the window's close)

    [Fact]
    public void WriteNow_plans_from_the_cache_with_no_read_back()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Cache["X"] = 0;
        Assert.True(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
    }

    [Fact]
    public void WriteNow_already_there_sends_nothing()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Cache["X"] = 1;
        Assert.True(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public void WriteNow_refuses_unread_guarded_buttons_and_no_landing()
    {
        var rig = new Rig(Toggle(), Button(), Lights(), LightsGuard());
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "X", 1));                     // not cached
        rig.Sim.Cache["X"] = 0;
        rig.Sim.Cache[LightsState] = 0;
        Assert.False(rig.Def.FoWriteNow(rig.Manager, LightsState, 1));             // guarded
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "B", 1));                     // a button
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "NOT_IN_THE_MAP", 1));
        rig.Sim.CanWrite = false;
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ reads

    [Fact]
    public async Task Reads_go_through_the_same_sim_access()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 1;
        rig.Sim.Cache["Y"] = 7;
        Assert.Equal(1, await rig.Def.FoReadAsync(rig.Manager, "X", CancellationToken.None));
        Assert.Null(await rig.Def.FoReadAsync(rig.Manager, "UNREAD", CancellationToken.None));
        Assert.Equal(7, rig.Def.FoCached(rig.Manager, "Y"));
        Assert.Null(rig.Def.FoCached(rig.Manager, "UNREAD"));
    }

    [Fact]
    public void The_map_lookups_the_executor_relies_on()
    {
        var rig = new Rig(Toggle(), Button());
        Assert.True(rig.Def.HasControl("X"));
        Assert.False(rig.Def.HasControl("NOT_IN_THE_MAP"));
        Assert.Same(rig.Def.ControlFor("B"), rig.Def.ControlFor("B"));
        Assert.Null(rig.Def.ControlFor("NOT_IN_THE_MAP"));
    }

    // ------------------------------------------------------------------ silence

    /// <summary>
    /// The First Officer's write path speaks nothing: none of its methods takes an announcer, so a
    /// refusal or a write that did not land can only be reported by its return value (the flow, or
    /// FO-9's "Unable to complete", speaks for it). The panel path's "{label} did not move" is not reachable.
    /// </summary>
    [Fact]
    public void The_write_path_has_no_announcer_to_speak_through()
    {
        var methods = typeof(BBS757Definition).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name.StartsWith("Fo", StringComparison.Ordinal)
                        && (m.Name.Contains("Set") || m.Name.Contains("Press") || m.Name.Contains("Write") || m.Name.Contains("Read")
                            || m.Name.Contains("Cached") || m.Name.Contains("Spring")))
            .ToList();
        Assert.Contains(methods, m => m.Name == "FoSetAsync");
        Assert.Contains(methods, m => m.Name == "FoPressAsync");
        Assert.Contains(methods, m => m.Name == "FoSpringTestAsync");       // plan 19, task 8: the P61 tests, silent
        Assert.Contains(methods, m => m.Name == "FoSpringTestCoreAsync");
        foreach (var m in methods)
            Assert.DoesNotContain(m.GetParameters(), p => typeof(ScreenReaderAnnouncer).IsAssignableFrom(p.ParameterType));
    }
}
