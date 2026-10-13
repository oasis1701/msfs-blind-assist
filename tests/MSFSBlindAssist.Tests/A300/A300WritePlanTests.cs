using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300WritePlanTests
{
    private static A300Control Control(string kind, string positions = "0=OFF;1=ON", double? scale = null,
        double? press = null, double? rest = null, Dictionary<string, string>? values = null)
    {
        var c = new A300Control
        {
            Id = "AIRLINER_TEST",
            Key = "A300_TEST",
            Kind = kind,
            Event = "AIRLINER_Test_Set",
            StateVar = "L:INI_TEST",
            Scale = scale,
            Press = press,
            Rest = rest,
            Values = values,
        };
        foreach (var part in positions.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            c.Positions[kv[0]] = kv[1];
        }
        return c;
    }

    private static string[] Rpns(A300Plan plan) => plan.Steps.OfType<A300CalcStep>().Select(s => s.Rpn).ToArray();

    [Fact]
    public void A_toggle_in_an_unknown_position_is_refused()
    {
        var plan = A300WritePlan.ForSet(Control(A300Kinds.Toggle), 1, current: null);
        Assert.Equal(A300WritePlan.UnknownPositionRefusal, plan.Refusal);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_toggle_already_there_sends_nothing()
    {
        var plan = A300WritePlan.ForSet(Control(A300Kinds.Toggle), 1, current: 1);
        Assert.Null(plan.Refusal);
        Assert.True(plan.IsEmpty);
    }

    [Theory]
    [InlineData(0, 1, "0 (>B:AIRLINER_Test_Set)")]
    [InlineData(1, 0, "1 (>B:AIRLINER_Test_Set)")]
    public void A_toggle_that_differs_fires_its_set_event_once(double target, double current, string expected)
    {
        Assert.Equal(new[] { expected }, Rpns(A300WritePlan.ForSet(Control(A300Kinds.Toggle), target, current)));
    }

    [Fact]
    public void A_command_follows_the_toggle_rules()
    {
        Assert.Equal(A300WritePlan.UnknownPositionRefusal, A300WritePlan.ForSet(Control(A300Kinds.Command), 1, null).Refusal);
        Assert.Single(Rpns(A300WritePlan.ForSet(Control(A300Kinds.Command), 1, 0)));
    }

    [Fact]
    public void A_switch_reading_between_its_positions_is_not_ready()
    {
        // A flip planned from a value that is no position could go the wrong way round: a valve or switch in
        // transit reads between its positions, and that is not a position it is in.
        var toggle = A300ControlMap.Load().FindByKey("A300_BATT_1")!;
        Assert.Equal(A300WritePlan.UnknownPositionRefusal, A300WritePlan.ForSet(toggle, 1, 0.6).Refusal);
    }

    [Theory]
    [InlineData(A300Kinds.Toggle)]
    [InlineData(A300Kinds.Command)]
    public void A_flipping_kind_reading_between_its_positions_is_refused_and_sends_nothing(string kind)
    {
        var plan = A300WritePlan.ForSet(Control(kind), 1, 0.5);
        Assert.Equal(A300WritePlan.UnknownPositionRefusal, plan.Refusal);
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void A_selector_reading_between_its_positions_is_still_set_absolutely()
    {
        // Only a flip needs to know where the switch is; a selector's Set n is position n whatever it held.
        var c = Control(A300Kinds.Selector, "0=OFF;1=NAV;2=ATT");
        Assert.Equal(new[] { "2 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForSet(c, 2, current: 0.5)));
    }

    [Fact]
    public void A_value_that_is_not_a_position_is_refused()
    {
        Assert.Equal(A300WritePlan.NotAPositionRefusal, A300WritePlan.ForSet(Control(A300Kinds.Toggle), 7, 0).Refusal);
        Assert.Equal(A300WritePlan.NotAPositionRefusal, A300WritePlan.ForSet(Control(A300Kinds.Selector, "0=A;1=B"), 5, 0).Refusal);
    }

    [Fact]
    public void A_selector_is_absolute_even_when_its_position_is_unknown()
    {
        var c = Control(A300Kinds.Selector, "0=OFF;1=NAV;2=ATT");
        Assert.Equal(new[] { "2 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForSet(c, 2, current: null)));
        Assert.True(A300WritePlan.ForSet(c, 2, current: 2).IsEmpty);
    }

    [Fact]
    public void A_selector_whose_state_values_differ_sends_the_position()
    {
        var c = Control(A300Kinds.Selector, "0=A;1=B", values: new() { ["0"] = "10", ["1"] = "20" });
        Assert.Equal(new[] { "1 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForSet(c, 20, current: 10)));
    }

    [Fact]
    public void A_spring_is_held_at_the_picked_side_then_returned_to_rest()
    {
        var plan = A300WritePlan.ForSet(Control(A300Kinds.Spring, "0=DOWN;1=NEUTRAL;2=UP", rest: 1), 2, current: 1);
        Assert.Collection(plan.Steps,
            s => Assert.Equal("2 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn),
            s => Assert.Equal(A300WritePlan.SpringHoldMs, Assert.IsType<A300DelayStep>(s).Milliseconds),
            s => Assert.Equal("1 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn));
    }

    [Fact]
    public void The_cargo_door_switch_is_held_for_the_doors_whole_travel()
    {
        // The door moves only while its switch is held: 0 to 145 degrees or back to locked took about 30 s
        // (2026-10-10); it stops by itself at the picked angle or locked, and a pick of Neutral stops it.
        var door = Control(A300Kinds.Spring, "0=OPEN;1=NEUTRAL;2=CLOSE", rest: 1);
        door.Id = "AIRLINER_MAIN_CARGO_DOOR_SWITCH";
        var plan = A300WritePlan.ForSet(door, 0, current: 1);
        Assert.Equal(35000, Assert.IsType<A300DelayStep>(plan.Steps[1]).Milliseconds);
        Assert.Equal(35000, A300WritePlan.SpringHoldMsFor(door));
    }

    [Fact]
    public void Picking_a_springs_rest_just_returns_it()
    {
        var plan = A300WritePlan.ForSet(Control(A300Kinds.Spring, "0=DOWN;1=NEUTRAL;2=UP", rest: 1), 1, current: 2);
        Assert.Equal(new[] { "1 (>B:AIRLINER_Test_Set)" }, Rpns(plan));
    }

    [Theory]
    [InlineData(1.0, 42, "42 (>B:AIRLINER_Test_Set)")]
    [InlineData(0.1, 4.2, "42 (>B:AIRLINER_Test_Set)")]
    [InlineData(1.0, 150, "100 (>B:AIRLINER_Test_Set)")]
    [InlineData(1.0, -5, "0 (>B:AIRLINER_Test_Set)")]
    public void A_knob_sends_its_value_unscaled_and_clamped(double scale, double target, string expected)
    {
        var plan = A300WritePlan.ForSet(Control(A300Kinds.Knob, "", scale: scale), target, current: null);
        Assert.Equal(new[] { expected }, Rpns(plan));
    }

    [Fact]
    public void A_knob_always_sends_its_value()
    {
        // A knob row is read when its panel opens, so its cache goes stale after MSFSBA's own step: stepping back
        // to the opening value read as "already there" and sent nothing (2026-10-10). A knob's Set is absolute.
        Assert.Equal(new[] { "42 (>B:AIRLINER_Test_Set)" },
            Rpns(A300WritePlan.ForSet(Control(A300Kinds.Knob, "", scale: 0.1), 4.2, current: 4.2)));
    }

    [Fact]
    public void A_button_press_is_one_set()
    {
        Assert.Equal(new[] { "1 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForPress(Control(A300Kinds.Button, ""))));
        Assert.Equal(new[] { "3 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForPress(Control(A300Kinds.Button, "", press: 3))));
    }

    [Fact]
    public void A_hold_button_is_pressed_held_and_released()
    {
        var plan = A300WritePlan.ForPress(Control(A300Kinds.Hold, ""));
        Assert.Collection(plan.Steps,
            s => Assert.Equal("2 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn),
            s => Assert.Equal(A300WritePlan.HoldMs, Assert.IsType<A300DelayStep>(s).Milliseconds),
            s => Assert.Equal("0 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn));
    }

    [Theory]
    [InlineData("AIRLINER_ENG1_FIRE_PUSH2", 7000)]   // engine 1 loop test
    [InlineData("AIRLINER_ENG2_FIRE_PUSH2", 7000)]   // engine 2 loop test
    [InlineData("AIRLINER_APU_FIRE_PUSH2", 5000)]    // APU loop test
    [InlineData("AIRLINER_ENG1_FIRE_PUSH1", 3000)]   // engine 1 squib test
    [InlineData("AIRLINER_ENG2_FIRE_PUSH1", 3000)]   // engine 2 squib test
    [InlineData("AIRLINER_APU_FIRE_PUSH1", 3000)]    // APU squib test
    public void A_fire_test_is_held_until_its_fire_warning_can_be_heard(string id, int ms)
    {
        // Airbus_A300_Checklist.xml holds each loop test 5.0 s and each squib test 3.0 s; 250 ms
        // never finished the APU loop test, whose fire handle light comes at about 3 s (2026-10-04). An
        // engine loop test's fire warning comes at about 4.4 s and lasts while it is held (2026-10-09), so
        // 5 s lit the handle about half a second, inside the lights' gather window: 7 s gives about 2.5 s.
        var c = Control(A300Kinds.Hold, "");
        c.Id = id;
        Assert.Collection(A300WritePlan.ForPress(c).Steps,
            s => Assert.Equal("2 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn),
            s => Assert.Equal(ms, Assert.IsType<A300DelayStep>(s).Milliseconds),
            s => Assert.Equal("0 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn));
    }

    [Theory]
    [InlineData("AIRLINER_TO_CONFIG_TEST")]   // takeoff config test
    [InlineData("AIRLINER_LDG_TEST")]         // landing gear warning test
    [InlineData("AIRLINER_SMOKE_TEST")]       // smoke test
    public void A_warning_test_is_held_five_seconds(string id)
    {
        // The ground probe (2026-10-04): a 250 ms takeoff config press reported nothing even in
        // landing configuration, while a 5 s hold warned at about 3.7 s; the gear warning test's master
        // warning came at about 3.5 s and the smoke test's ECAM warning at about 3.5 s.
        var c = Control(A300Kinds.Hold, "");
        c.Id = id;
        Assert.Collection(A300WritePlan.ForPress(c).Steps,
            s => Assert.Equal("2 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn),
            s => Assert.Equal(5000, Assert.IsType<A300DelayStep>(s).Milliseconds),
            s => Assert.Equal("0 (>B:AIRLINER_Test_Set)", Assert.IsType<A300CalcStep>(s).Rpn));
    }

    [Fact]
    public void Every_held_test_is_a_hold_button_on_the_shipped_map()
    {
        var map = A300ControlMap.Load();
        Assert.NotEmpty(A300WritePlan.TestHoldMs);
        foreach (var id in A300WritePlan.TestHoldMs.Keys)
            Assert.Equal(A300Kinds.Hold, map.Find(id)?.Kind);
    }

    [Fact]
    public void An_encoder_steps_one_click_either_way()
    {
        var c = Control(A300Kinds.Encoder, "");
        Assert.Equal(new[] { "1 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForStep(c, increase: true)));
        Assert.Equal(new[] { "-1 (>B:AIRLINER_Test_Set)" }, Rpns(A300WritePlan.ForStep(c, increase: false)));
    }

    [Fact]
    public void A_kind_that_cannot_be_driven_is_refused()
    {
        Assert.Equal(A300WritePlan.NotSettableRefusal, A300WritePlan.ForSet(Control(A300Kinds.Cover), 1, 0).Refusal);
        Assert.Equal(A300WritePlan.NotSettableRefusal, A300WritePlan.ForPress(Control(A300Kinds.Toggle)).Refusal);
        Assert.Equal(A300WritePlan.NotSettableRefusal, A300WritePlan.ForStep(Control(A300Kinds.Knob, ""), true).Refusal);
    }

    [Fact]
    public void A_switch_not_ready_is_said_alone_and_any_other_refusal_after_the_row_name()
    {
        Assert.Equal("Switch not ready, please try again in a moment.",
            A300WritePlan.SpokenRefusal("Battery 1", A300WritePlan.UnknownPositionRefusal));
        Assert.Equal("Battery 1: not a position of this control",
            A300WritePlan.SpokenRefusal("Battery 1", A300WritePlan.NotAPositionRefusal));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Numbers_are_written_with_a_point_under_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var plan = A300WritePlan.ForSet(Control(A300Kinds.Knob, "", scale: 1.0), 42.5, current: null);
            Assert.Equal(new[] { "42.5 (>B:AIRLINER_Test_Set)" }, Rpns(plan));
            Assert.Equal("0.0001", A300WritePlan.Format(0.0001));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
