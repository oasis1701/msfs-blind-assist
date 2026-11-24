using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What a panel pick sends to the 757, decided from a fresh reading of the control. The rules the
/// definition must keep around <see cref="Bbs757Rpn"/>: an unread or unknown reading never presses
/// (a toggle's press flips it, whatever it reads), a control with no click code is written through
/// its state var (ruling R8 of plan 01), and a value that is not one of the control's positions is
/// refused.
/// </summary>
public class Bbs757WriteTests
{
    private const string Press = "(L:X,bool) ! (>L:X,bool)";
    private const string Cw = "(L:S, enum) 1 + 2 min (>L:S)";
    private const string Ccw = "(L:S, enum) 1 - 0 max (>L:S)";

    private static List<Bbs757Position> Positions(params double[] values)
        => values.Select(v => new Bbs757Position(v, "P" + v.ToString(CultureInfo.InvariantCulture))).ToList();

    private static Bbs757Control Toggle(string? press = Press) => new()
    {
        Node = "X", Label = "Thing", Kind = Bbs757Kinds.Toggle, StateVar = "X", Positions = Positions(0, 1), Press = press,
    };

    private static Bbs757Control Switch() => new()
    {
        Node = "S", Label = "Selector", Kind = Bbs757Kinds.Switch, StateVar = "S", Positions = Positions(0, 1, 2), Cw = Cw, Ccw = Ccw,
    };

    private static Bbs757Control CodelessSwitch(string? stateVar = "S") => new()
    {
        Node = "S", Label = "Heater", Kind = Bbs757Kinds.Switch, StateVar = stateVar, Positions = Positions(0, 0.5, 1),
    };

    [Fact]
    public void A_toggle_away_from_its_target_sends_its_press()
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current: 0, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { Press }, plan.Calls);
    }

    [Fact]
    public void A_toggle_already_there_sends_nothing()
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current: 1, target: 1);

        Assert.Equal(Bbs757WriteOutcome.AtPosition, plan.Outcome);
        Assert.Empty(plan.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unread_reading_never_presses(double? current)
    {
        var plan = Bbs757Write.ToPosition(Toggle(), current, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Refused, plan.Outcome);
        Assert.Empty(plan.Calls);
    }

    [Fact]
    public void A_reading_that_is_no_position_never_presses()
    {
        // Mid-travel or an unexpected encoding: one press could land anywhere.
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0.5, target: 1).Outcome);
    }

    [Fact]
    public void A_target_that_is_no_position_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0, target: 2).Outcome);
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Toggle(), current: 0, target: double.NaN).Outcome);
    }

    [Fact]
    public void A_switch_steps_by_position()
    {
        var plan = Bbs757Write.ToPosition(Switch(), current: 0, target: 2);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { $"{Cw} {Cw}" }, plan.Calls);
    }

    [Fact]
    public void A_switch_reading_off_its_positions_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(Switch(), current: 7, target: 1).Outcome);
    }

    [Fact]
    public void A_switch_with_no_click_code_writes_its_state_var()
    {
        var plan = Bbs757Write.ToPosition(CodelessSwitch(), current: 0, target: 1);

        Assert.Equal(Bbs757WriteOutcome.Writes, plan.Outcome);
        Assert.Equal(new[] { "1 (>L:S)" }, plan.Calls);
    }

    [Fact]
    public void A_codeless_write_is_absolute_so_any_finite_reading_will_do()
    {
        Assert.Equal(new[] { "0 (>L:S)" }, Bbs757Write.ToPosition(CodelessSwitch(), current: 7, target: 0).Calls);
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(CodelessSwitch(), current: null, target: 0).Outcome);
    }

    [Fact]
    public void A_codeless_switch_already_there_sends_nothing()
    {
        Assert.Equal(Bbs757WriteOutcome.AtPosition, Bbs757Write.ToPosition(CodelessSwitch(), current: 0.5, target: 0.5).Outcome);
    }

    [Fact]
    public void A_codeless_write_is_invariant_under_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(new[] { "0.5 (>L:S)" }, Bbs757Write.ToPosition(CodelessSwitch(), current: 0, target: 0.5).Calls);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_codeless_control_without_a_state_var_is_refused()
    {
        Assert.Equal(Bbs757WriteOutcome.Refused, Bbs757Write.ToPosition(CodelessSwitch(stateVar: null), current: 0, target: 1).Outcome);
    }

    [Fact]
    public void A_toggle_without_a_press_writes_its_state_var()
    {
        // ToggleTo answers null for a missing press as well as for "already there": Press is checked first.
        var plan = Bbs757Write.ToPosition(Toggle(press: null), current: 0, target: 1);

        Assert.Equal(new[] { "1 (>L:X)" }, plan.Calls);
    }

    [Fact]
    public void The_battery_from_the_map_replays_its_own_click()
    {
        var battery = Bbs757ControlMap.Load().Controls.Single(c => c.Node == "B757_OVHD_BATTERY_BUTTON");

        var plan = Bbs757Write.ToPosition(battery, current: 0, target: 1);

        Assert.Equal(new[] { battery.Press! }, plan.Calls);
    }

    [Fact]
    public void A_button_press_is_held_briefly_then_released()
    {
        var button = new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button, Press = "1 (>L:B)", Release = "0 (>L:B)" };

        var press = Bbs757Write.PressFor(button);

        Assert.NotNull(press);
        Assert.Equal("1 (>L:B)", press!.Down);
        Assert.Equal("0 (>L:B)", press.Up);
        Assert.Equal(Bbs757Write.ButtonHoldMs, press.HoldMs);
        Assert.Equal(200, Bbs757Write.ButtonHoldMs);
    }

    [Fact]
    public void A_held_button_is_held_for_its_own_time()
    {
        var held = new Bbs757Control { Node = "H", Kind = Bbs757Kinds.Held, Press = "1 (>L:H)", Release = "0 (>L:H)", HoldMs = 3000 };
        Assert.Equal(3000, Bbs757Write.PressFor(held)!.HoldMs);

        held.HoldMs = 0;
        Assert.Equal(1500, Bbs757Write.PressFor(held)!.HoldMs);
    }

    [Fact]
    public void A_button_with_no_press_code_has_no_press()
    {
        Assert.Null(Bbs757Write.PressFor(new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button }));
    }

    [Fact]
    public void A_button_without_named_positions_has_no_readable_state()
    {
        // The MCP buttons pulse a _PRESS var: a state var, but nothing a pilot would call a position.
        Assert.False(Bbs757Write.HasReadableState(new Bbs757Control { Kind = Bbs757Kinds.Button, StateVar = "B_PRESS" }));
        Assert.False(Bbs757Write.HasReadableState(new Bbs757Control { Kind = Bbs757Kinds.Button }));
        Assert.True(Bbs757Write.HasReadableState(Toggle()));
    }

    // ---- fire handle unlocks lock again (task 3) ------------------------------------------------

    // BlueBird's own unlock click (BBS_752_BASE_INTERIOR.xml, the fire handle unlocks): it sets the
    // L:var and starts an O: timer, whose update loop relocks the handle 1.5 s later. The calculator
    // cannot start that timer, so the app relocks it itself.
    private const string EngineOneUnlockPress =
        "1 (>L:BBS757_FIREUNLOCK1,bool) 1 (>O:FIRE_HANDLE_1_TIMER_ACTIVE, bool) (E:ABSOLUTE TIME, seconds) (>O:FIREHANDLE_UNLOCK_Timer)";

    private static Bbs757Control Unlock(string node = "BBS757_FIREUNLOCK1", string? stateVar = "BBS757_FIREUNLOCK1",
        string label = "Engine 1 Fire Handle Unlock", string kind = Bbs757Kinds.Button) => new()
    {
        Node = node, Label = label, Kind = kind, StateVar = stateVar, Press = EngineOneUnlockPress,
    };

    [Theory]
    [InlineData("BBS757_FIREUNLOCK1", "BBS757_FIREUNLOCK1", "0 (>L:BBS757_FIREUNLOCK1)")]
    [InlineData("BBS757_FIREUNLOCK2", "BBS757_FIREUNLOCK2", "0 (>L:BBS757_FIREUNLOCK2)")]
    [InlineData("B757_FIREUNLOCK3", "BBS757_FIREUNLOCK3", "0 (>L:BBS757_FIREUNLOCK3)")]   // the APU unlock's node has no BBS prefix
    public void Unlock_buttons_relock(string node, string stateVar, string relock)
    {
        string? write = Bbs757Write.RelockAfterUnlock(Unlock(node, stateVar), out int delayMs);

        Assert.Equal(relock, write);
        Assert.Equal(1500, delayMs);
        Assert.Equal(Bbs757Write.FireHandleRelockMs, delayMs);
    }

    [Fact]
    public void Other_buttons_do_not()
    {
        var plain = new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button, StateVar = "B_PRESS", Press = "1 (>L:B_PRESS)" };
        // The fire handle itself: a switch on another var (it reads the unlock, it is not the unlock).
        var handle = new Bbs757Control { Node = "BBS757_FIREHANDLE1_CLICK", Kind = Bbs757Kinds.Switch, StateVar = "B757_ENGFIRE_1_SWITCH", Positions = Positions(0, 1) };
        var noState = new Bbs757Control { Node = "B", Kind = Bbs757Kinds.Button, Press = "1 (>L:B)" };
        var otherCase = Unlock(stateVar: "bbs757_fireunlock1");

        foreach (var control in new[] { plain, handle, noState, otherCase })
        {
            Assert.Null(Bbs757Write.RelockAfterUnlock(control, out int delayMs));
            Assert.Equal(0, delayMs);
        }
    }

    [Theory]
    [InlineData(Bbs757Kinds.Toggle)]
    [InlineData(Bbs757Kinds.Switch)]
    [InlineData(Bbs757Kinds.Guard)]
    [InlineData(Bbs757Kinds.Select)]
    public void A_control_that_is_no_momentary_button_is_never_relocked_even_on_an_unlock_state_var(string kind)
    {
        // A toggle on the unlock's state var would be flipped to 0 by a relock the pilot never asked for.
        Assert.Null(Bbs757Write.RelockAfterUnlock(Unlock(kind: kind), out int delayMs));
        Assert.Equal(0, delayMs);
    }

    [Fact]
    public void The_shipped_maps_three_unlock_buttons_are_the_only_controls_that_relock()
    {
        var relocking = Bbs757ControlMap.Load().Controls
            .Where(c => Bbs757Write.RelockAfterUnlock(c, out _) != null)
            .Select(c => c.Node)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "B757_FIREUNLOCK3", "BBS757_FIREUNLOCK1", "BBS757_FIREUNLOCK2" }, relocking);
    }

    /// <summary>A 757 definition holding one control, with a fake sim behind its writes and a speech capture.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly BBS757Definition Def;
        private readonly SimConnectManager _manager = new(IntPtr.Zero);

        public Rig(Bbs757Control control, bool? parkingBrakeRealistic = null)
        {
            var map = new Bbs757ControlMap();
            map.Controls.Add(control);
            Def = new BBS757Definition(map)
            {
                NestRunning = () => true, WriteIo = _ => Sim.Io, ParkingBrakeRealistic = () => parkingBrakeRealistic,
            };
        }

        /// <summary>The panel handler setting <paramref name="node"/> to <paramref name="value"/>.</summary>
        public bool Set(string node, double value) => Def.HandleUIVariableSet(node, value, Def.GetVariables()[node], _manager, Speech);

        /// <summary>The panel handler's answer to a press of <paramref name="node"/>.</summary>
        public bool Press(string node) => Def.HandleUIVariableSet(node, 1, Def.GetVariables()[node], _manager, Speech);
    }

    private const string ParkPress = "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)";

    private static Bbs757Control ParkBrake() => new()
    {
        Node = Bbs757ParkingBrake.Node, Label = "Parking Brake", Kind = Bbs757Kinds.Toggle, StateVar = "B757_FC_PARK_BRAKE",
        Positions = Positions(0, 1), Press = ParkPress,
    };

    [Theory]
    [InlineData(false)]   // EASY
    [InlineData(null)]    // the setting unread
    [InlineData(true)]    // REALISTIC, where the toggle happened to work
    public async Task A_parking_brake_the_toggle_moves_gets_the_toggle_alone(bool? realistic)
    {
        var rig = new Rig(ParkBrake(), realistic);
        rig.Sim.Fresh[Bbs757ParkingBrake.Node] = 0;
        rig.Sim.OnWrite = rpn => { if (rpn == ParkPress) rig.Sim.Fresh[Bbs757ParkingBrake.Node] = 1; };

        Assert.True(rig.Set(Bbs757ParkingBrake.Node, 1));
        await rig.Sim.ResyncsReached(1);

        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task In_realistic_mode_a_parking_brake_the_toggle_did_not_move_is_retried_with_full_braking(int target)
    {
        // REALISTIC (measured 2026-10-28): the toggle does nothing; full braking with the lever in one write
        // sets and releases it.
        bool set = target == 1;
        var rig = new Rig(ParkBrake(), parkingBrakeRealistic: true);
        rig.Sim.Fresh[Bbs757ParkingBrake.Node] = set ? 0 : 1;
        rig.Sim.OnWrite = rpn => { if (rpn == Bbs757ParkingBrake.FullBrakingCode(set)) rig.Sim.Fresh[Bbs757ParkingBrake.Node] = target; };

        Assert.True(rig.Set(Bbs757ParkingBrake.Node, target));
        await rig.Sim.ResyncsReached(1);

        Assert.Equal(new[] { ParkPress, Bbs757ParkingBrake.FullBrakingCode(set), Bbs757ParkingBrake.PedalsReleaseCode }, rig.Sim.Writes);
    }

    [Fact]
    public async Task In_easy_mode_a_parking_brake_the_toggle_did_not_move_is_never_retried_with_braking()
    {
        var rig = new Rig(ParkBrake(), parkingBrakeRealistic: false);
        rig.Sim.Fresh[Bbs757ParkingBrake.Node] = 0;

        Assert.True(rig.Set(Bbs757ParkingBrake.Node, 1));
        await rig.Sim.ResyncsReached(1);

        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Fact]
    public void An_app_unlock_relocks_after_the_vendor_delay()
    {
        var unlock = Unlock();
        var rig = new Rig(unlock);

        Assert.True(rig.Press(unlock.Node));

        string relock = "0 (>L:BBS757_FIREUNLOCK1)";
        Assert.Equal(new[] { unlock.Press!, relock }, rig.Sim.Writes);
        // The vendor's 1.5 s is awaited between the press and the relock (the read-back wait that
        // follows is a 1.5 s wait too, so it is the order that tells them apart).
        Assert.Equal(new[] { Bbs757FakeIo.WriteEvent(unlock.Press!), Bbs757FakeIo.DelayEvent(1500), Bbs757FakeIo.WriteEvent(relock) },
            rig.Sim.Events.Take(3));
        // The press said so once; the relock is the cockpit's own behaviour and says nothing.
        Assert.Equal(new[] { "Engine 1 Fire Handle Unlock pressed" }, rig.Speech.All);
    }

    [Fact]
    public async Task An_unlock_stays_unlocked_until_the_delay_has_passed()
    {
        var unlock = Unlock();
        var rig = new Rig(unlock);
        rig.Sim.Hold = ordinal => ordinal == 0;   // the relock's wait

        Assert.True(rig.Press(unlock.Node));

        Assert.Equal(new[] { unlock.Press! }, rig.Sim.Writes);
        Assert.Equal(new[] { 1500, 1500 }, rig.Sim.Delays);   // the relock's wait (held), then the read-back's

        rig.Sim.Release(0);
        await rig.Sim.WritesReached(2);
        Assert.Equal(new[] { unlock.Press!, "0 (>L:BBS757_FIREUNLOCK1)" }, rig.Sim.Writes);
    }

    [Fact]
    public void Switching_away_before_the_relock_sends_nothing_more()
    {
        var unlock = Unlock();
        var rig = new Rig(unlock);
        rig.Sim.Hold = ordinal => ordinal == 0;   // the relock's wait

        Assert.True(rig.Press(unlock.Node));
        Assert.Equal(new[] { unlock.Press! }, rig.Sim.Writes);
        Assert.Equal(new[] { 1500, 1500 }, rig.Sim.Delays);   // the relock's wait (held) is pending beside the read-back's

        rig.Def.OnSwitchedAway();   // the pilot picks another aircraft profile: the handle stays as the cockpit would leave it
        rig.Sim.Release(0);         // a no-op: the switch already ended the wait

        Assert.Equal(1, rig.Sim.CanceledDelays);   // the held wait ended because its token was canceled
        Assert.Equal(new[] { unlock.Press! }, rig.Sim.Writes);
    }

    [Fact]
    public void A_button_that_is_no_unlock_schedules_no_relock()
    {
        var button = new Bbs757Control { Node = "B", Label = "Thing", Kind = Bbs757Kinds.Button, StateVar = "B_PRESS", Press = "1 (>L:B_PRESS)" };
        var rig = new Rig(button);

        Assert.True(rig.Press(button.Node));

        Assert.Equal(new[] { "1 (>L:B_PRESS)" }, rig.Sim.Writes);
        Assert.Equal(new[] { 1500 }, rig.Sim.Delays);   // only the read-back's wait
    }
}
