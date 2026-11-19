using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MSFSBlindAssist.FirstOfficer.BBS757;
using MSFSBlindAssist.SimConnect;
using Xunit;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using E = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoStateEvaluator;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 10, task 1: the 757 First Officer's synthetic fields. A field is NaN until every input it
/// needs has been read, never 0 (FO-20): an unread switch must never tick, untick or pass a guard.
/// Where the variants differ, the variant is the aircraft TITLE's (" PW" / " RR") first, then
/// BlueBird's <c>L:B757_ENG_VARIANT</c>, believed only at 1 (PW): an unwritten L:var reads 0 too.
/// </summary>
public class Bbs757FoStateEvaluatorTests
{
    private const string Title = E.TitleVariantKey;
    private const string Pw = "BlueBird 757-200 PW N/W";
    private const string Rr = "BlueBird 757-200 RR Scimitar/W";

    private static Func<string, double> Read(Dictionary<string, double> d)
        => k => d.TryGetValue(k, out var v) ? v : double.NaN;

    private static double C(string field, Dictionary<string, double> d, int takeoffFlaps = -1)
        => E.Compute(field, Read(d), takeoffFlaps);

    private static Dictionary<string, double> D(params (string K, double V)[] values)
        => values.ToDictionary(x => x.K, x => x.V);

    /// <summary>A PW (by its L:var, no title) or an RR (by its title: an L:var 0 says nothing), plus <paramref name="extra"/>.</summary>
    private static Dictionary<string, double> Variant(bool pw, params (string K, double V)[] extra)
    {
        var d = D(extra);
        if (pw) d[K.EngineVariant] = 1;
        else d[Title] = 0;
        return d;
    }

    private static Dictionary<string, double> Without(Dictionary<string, double> d, string key)
    {
        var copy = new Dictionary<string, double>(d);
        copy.Remove(key);
        return copy;
    }

    // ------------------------------------------------------------------ NaN until read (FO-20)

    [Fact]
    public void Synthetic_fields_are_nan_until_read()
    {
        Assert.NotEmpty(E.SyntheticKeys);
        foreach (var field in E.SyntheticKeys)
            Assert.True(double.IsNaN(C(field, new())), $"{field} reads a number with nothing read");
    }

    /// <summary>
    /// For each field, a complete reading gives a number, and dropping ANY one of the inputs that
    /// reading needed gives NaN, never 0 or 1 — under two different fillings, so a field that
    /// happened to return the right "0" by accident is caught.
    /// </summary>
    public static IEnumerable<object[]> FieldsAndInputs() => new[]
    {
        new object[] { "FO_GEAR_DOWN", new[] { K.GearLever }.Concat(K.GearLegs).ToArray() },
        new object[] { "FO_GEAR_UP_OFF", new[] { K.GearLever }.Concat(K.GearLegs).ToArray() },
        new object[] { "FO_DOORS_CLOSED", K.Doors.Concat(K.CargoDoors).ToArray() },
        new object[] { "FO_IRS_ALL_NAV", K.IrsSwitches },
        new object[] { "FO_IRS_ALL_OFF", K.IrsSwitches },
        new object[] { "FO_IRS_ALIGNED", K.IrsSwitches.Concat(K.IrsAlignLights).Append(K.Battery).ToArray() },
        new object[] { "FO_FUEL_CONTROLS_CUTOFF", new[] { K.EngineVariant, K.LeftFuelPw, K.RightFuelPw } },
        new object[] { "FO_FUEL_CONTROLS_RUN", new[] { K.EngineVariant, K.LeftFuelPw, K.RightFuelPw } },
        new object[] { "FO_ENGINES_RUNNING", new[] { K.EngineVariant, "FO_ENG1_N2", "FO_ENG2_N2" } },
        new object[] { "FO_ENGINES_STOPPED", new[] { "FO_ENG1_N2", "FO_ENG2_N2" } },
        new object[] { "FO_ENG1_FUEL_POINT", new[] { K.EngineVariant, "FO_ENG1_N2" } },
        new object[] { "FO_AT_ARMED", new[] { K.AutothrottleArmedState } },
        new object[] { "FO_HYD_PANEL_SET", new[] { K.LeftEngHyd, K.RightEngHyd, K.LeftElecHyd, K.C1ElecHyd, K.C2ElecHyd, K.RightElecHyd } },
        new object[] { "FO_FUEL_PANEL_SET", new[] { K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump,
            K.CenterLeftPump, K.CenterRightPump, K.XfeedFwd, K.XfeedAft, K.CenterFuelGallons } },
        new object[] { "FO_SPEEDBRAKE_ARMED", new[] { K.Speedbrake } },
        new object[] { "FO_FLAPS_UP", new[] { K.FlapsHandle } },
        new object[] { "FO_FLAPS_LANDING", new[] { K.FlapsHandle } },
        new object[] { "FO_APU_ON_LINE", new[] { K.ApuState, K.ApuGen } },
        new object[] { "FO_APU_OFF", new[] { K.ApuSelector } },
        new object[] { "FO_PARKED_LIGHTS_OFF", new[] { K.WingLights, K.LogoLights, K.TaxiLight } },
        new object[] { "FO_PREFLIGHT_LIGHTS_OFF", new[] { K.BeaconLight, K.StrobeLights, K.WingLights, K.LeftLanding, K.RightLanding,
            K.LeftTurnoff, K.RightTurnoff, K.TaxiLight } },
        new object[] { "FO_GROUND_LIGHTS_OFF", new[] { K.LeftTurnoff, K.RightTurnoff, K.TaxiLight } },
        new object[] { "FO_GROUND_LIGHTS_ON", new[] { K.LeftTurnoff, K.RightTurnoff, K.TaxiLight } },
    };

    [Theory]
    [MemberData(nameof(FieldsAndInputs))]
    public void Any_one_input_unread_reads_nan(string field, string[] inputs)
    {
        foreach (double fill in new[] { 0.0, 1.0, 2.0 })
        {
            var full = inputs.ToDictionary(k => k, _ => fill);
            if (full.ContainsKey(K.EngineVariant)) full[K.EngineVariant] = 1;   // a PW: the switches read are its own
            Assert.False(double.IsNaN(C(field, full)), $"{field} with every input read at {fill} should read a number");
            foreach (var input in inputs)
                Assert.True(double.IsNaN(C(field, Without(full, input))), $"{field}: {input} unread, filled {fill}, should be NaN");
        }
    }

    // ------------------------------------------------------------------ gear (FO-13)

    private static Dictionary<string, double> Gear(double lever, double nose, double left, double right)
        => D((K.GearLever, lever), (K.GearLegs[0], nose), (K.GearLegs[1], left), (K.GearLegs[2], right));

    [Fact]
    public void Gear_down_needs_lever_and_legs()
    {
        Assert.Equal(1, C("FO_GEAR_DOWN", Gear(1, 1, 1, 1)));
        Assert.Equal(0, C("FO_GEAR_DOWN", Gear(1, 1, 0.5, 1)));     // one leg in transit
        Assert.Equal(0, C("FO_GEAR_DOWN", Gear(1, 0.95, 1, 1)));    // not past 95 %
        Assert.Equal(0, C("FO_GEAR_DOWN", Gear(0, 1, 1, 1)));       // lever UP over legs still down
        Assert.Equal(0, C("FO_GEAR_DOWN", Gear(0.5, 1, 1, 1)));     // lever OFF
    }

    [Fact]
    public void Gear_up_then_off_needs_the_lever_at_off_and_every_leg_up()
    {
        Assert.Equal(1, C("FO_GEAR_UP_OFF", Gear(0.5, 0, 0, 0)));
        Assert.Equal(0, C("FO_GEAR_UP_OFF", Gear(0, 0, 0, 0)));     // still UP, not yet OFF
        Assert.Equal(0, C("FO_GEAR_UP_OFF", Gear(0.5, 0, 0.1, 0))); // a leg not up
        Assert.Equal(0, C("FO_GEAR_UP_OFF", Gear(1, 0, 0, 0)));
    }

    // ------------------------------------------------------------------ variant

    /// <summary>Every title the installed package ships (its ten aircraft.cfg files, read 2026-10-28) names its variant.</summary>
    [Theory]
    [InlineData("BlueBird 757-200 PW", 1.0)]
    [InlineData("BlueBird 757-200 PW AWE", 1.0)]
    [InlineData("BlueBird 757-200 PW NO CABIN", 1.0)]
    [InlineData("BlueBird 757-200 PW N/W", 1.0)]
    [InlineData("BlueBird 757-200 PW N/W AWE", 1.0)]
    [InlineData("BlueBird 757-200 RR", 0.0)]
    [InlineData("BlueBird 757-200 RR AWE", 0.0)]
    [InlineData("BlueBird 757-200 RR N/W", 0.0)]
    [InlineData("BlueBird 757-200 RR N/W 4 Door", 0.0)]
    [InlineData("BlueBird 757-200 RR Scimitar/W", 0.0)]
    public void Every_shipped_title_names_its_variant(string title, double expected)
        => Assert.Equal(expected, E.TitleVariant(title));

    [Theory]
    [InlineData(Pw, 1.0)]
    [InlineData(Rr, 0.0)]
    [InlineData("BlueBird 757-200 PW RR", double.NaN)]   // both: no guess
    [InlineData("BlueBird 757-200", double.NaN)]
    [InlineData("BlueBird 757-200 PWX", double.NaN)]      // a whole word only
    [InlineData("", double.NaN)]
    [InlineData(null, double.NaN)]
    public void The_title_names_the_variant(string? title, double expected)
        => Assert.Equal(expected, E.TitleVariant(title));

    [Fact]
    public void The_title_decides_before_the_lvar()
    {
        // An unwritten L:var reads 0 (RR): a PW title must still win.
        Assert.Equal(1, C("FO_VARIANT_PW", D((Title, 1), (K.EngineVariant, 0))));
        Assert.Equal(0, C("FO_VARIANT_PW", D((Title, 0), (K.EngineVariant, 1))));
        Assert.Equal(1, C("FO_VARIANT_PW", D((Title, 1))));                       // the L:var unread
    }

    /// <summary>Without a title only the L:var's 1 is believed: an unwritten L:var reads 0 as well, so 0 is unknown, never RR.</summary>
    [Theory]
    [InlineData(1.0, 1.0)]              // PW, measured
    [InlineData(0.0, double.NaN)]       // RR, or never written: unknown
    [InlineData(2.0, double.NaN)]
    [InlineData(0.6, double.NaN)]
    public void Without_a_title_only_the_lvars_pw_is_believed(double lvar, double expected)
        => Assert.Equal(expected, C("FO_VARIANT_PW", D((K.EngineVariant, lvar))));

    [Fact]
    public void An_lvar_0_with_no_title_leaves_every_variant_field_unknown()
    {
        var d = D((K.EngineVariant, 0), (K.LeftFuelRr, 2), (K.RightFuelRr, 2), (K.LeftFuelPw, 0), (K.RightFuelPw, 0));
        Assert.True(double.IsNaN(C("FO_FUEL_CONTROLS_CUTOFF", d)));
        d[Title] = 0;                                                    // the title names RR
        Assert.Equal(1, C("FO_FUEL_CONTROLS_CUTOFF", d));
    }

    [Fact]
    public void With_neither_every_variant_field_is_nan()
    {
        var d = D((K.LeftFuelPw, 0), (K.RightFuelPw, 0), (K.LeftFuelRr, 2), (K.RightFuelRr, 2),
            ("FO_ENG1_N2", 60), ("FO_ENG2_N2", 60), (K.LeftEgt, 20), (K.RightEgt, 20));
        foreach (var f in new[] { "FO_FUEL_CONTROLS_CUTOFF", "FO_FUEL_CONTROLS_RUN", "FO_FUEL_CONTROL_1_RUN",
                     "FO_FUEL_CONTROLS_RICH", "FO_ENGINES_RUNNING", "FO_ENG1_RUNNING", "FO_ENG1_FUEL_POINT", "FO_ENGINE_CONTROLS_ON" })
            Assert.True(double.IsNaN(C(f, d)), f);
    }

    // ------------------------------------------------------------------ fuel controls by variant

    [Fact]
    public void Fuel_controls_by_variant_pw()
    {
        // PW: BBS_fuelcut_PW1/2, 0 CUTOFF, 2 RUN. The RR pair is never read.
        var cutoff = Variant(true, (K.LeftFuelPw, 0), (K.RightFuelPw, 0));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_CUTOFF", cutoff));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_RUN", cutoff));

        var run = Variant(true, (K.LeftFuelPw, 2), (K.RightFuelPw, 2));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_CUTOFF", run));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_RUN", run));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_RICH", run));             // a PW has no RICH

        var split = Variant(true, (K.LeftFuelPw, 2), (K.RightFuelPw, 0));
        Assert.Equal(1, C("FO_FUEL_CONTROL_1_RUN", split));
        Assert.Equal(0, C("FO_FUEL_CONTROL_2_RUN", split));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_CUTOFF", split));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_RUN", split));
    }

    [Fact]
    public void Fuel_controls_by_variant_rr()
    {
        // RR: BBS_fuelcut_RR1/2, 0 RUN, 1 RICH, 2 CUTOFF.
        var cutoff = Variant(false, (K.LeftFuelRr, 2), (K.RightFuelRr, 2));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_CUTOFF", cutoff));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_RUN", cutoff));

        var run = Variant(false, (K.LeftFuelRr, 0), (K.RightFuelRr, 0));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_CUTOFF", run));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_RUN", run));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_RICH", run));

        var rich = Variant(false, (K.LeftFuelRr, 1), (K.RightFuelRr, 0));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_RUN", rich));             // RICH is a running position
        Assert.Equal(1, C("FO_FUEL_CONTROLS_RICH", rich));
        Assert.Equal(1, C("FO_FUEL_CONTROL_1_RUN", rich));
    }

    [Fact]
    public void Fuel_controls_read_only_the_variants_own_switches()
    {
        // The PW pair read CUTOFF on an RR aircraft says nothing about its fuel: unread.
        Assert.True(double.IsNaN(C("FO_FUEL_CONTROLS_CUTOFF", Variant(false, (K.LeftFuelPw, 0), (K.RightFuelPw, 0)))));
        Assert.True(double.IsNaN(C("FO_FUEL_CONTROLS_CUTOFF", Variant(true, (K.LeftFuelRr, 2), (K.RightFuelRr, 2)))));
        // The same values mean opposite things: 0 is CUTOFF on a PW and RUN on an RR.
        Assert.Equal(1, C("FO_FUEL_CONTROLS_CUTOFF", Variant(true, (K.LeftFuelPw, 0), (K.RightFuelPw, 0), (K.LeftFuelRr, 0), (K.RightFuelRr, 0))));
        Assert.Equal(0, C("FO_FUEL_CONTROLS_CUTOFF", Variant(false, (K.LeftFuelPw, 0), (K.RightFuelPw, 0), (K.LeftFuelRr, 0), (K.RightFuelRr, 0))));
    }

    [Fact]
    public void Fuel_controls_follow_the_title_over_the_lvar()
    {
        var d = D((Title, 1), (K.EngineVariant, 0), (K.LeftFuelPw, 0), (K.RightFuelPw, 0));
        Assert.Equal(1, C("FO_FUEL_CONTROLS_CUTOFF", d));
    }

    [Fact]
    public void Engine_controls_are_the_limiters_on_a_pw_and_eec_too_on_an_rr()
    {
        var limitersOn = new[] { (K.LeftLimiter, 1.0), (K.RightLimiter, 1.0) };
        Assert.Equal(1, C("FO_ENGINE_CONTROLS_ON", Variant(true, limitersOn)));
        Assert.True(double.IsNaN(C("FO_ENGINE_CONTROLS_ON", Variant(false, limitersOn))));   // EEC unread
        Assert.Equal(0, C("FO_ENGINE_CONTROLS_ON", Variant(false, limitersOn.Concat(new[] { (K.LeftEec, 1.0), (K.RightEec, 0.0) }).ToArray())));
        Assert.Equal(1, C("FO_ENGINE_CONTROLS_ON", Variant(false, limitersOn.Concat(new[] { (K.LeftEec, 1.0), (K.RightEec, 1.0) }).ToArray())));
    }

    // ------------------------------------------------------------------ doors, IRS

    [Fact]
    public void Doors_closed_at_the_ends_the_door_rows_speak()
    {
        Dictionary<string, double> Doors(double pax, double cargo)
            => K.Doors.Select(k => (k, pax)).Concat(K.CargoDoors.Select(k => (k, cargo))).ToDictionary(x => x.k, x => x.Item2);
        Assert.Equal(1, C("FO_DOORS_CLOSED", Doors(0, 0)));
        Assert.Equal(1, C("FO_DOORS_CLOSED", Doors(E.DoorClosedAt, E.CargoDoorClosedAt)));
        Assert.Equal(0, C("FO_DOORS_CLOSED", Doors(E.DoorClosedAt + 1, 0)));
        Assert.Equal(0, C("FO_DOORS_CLOSED", Doors(0, E.CargoDoorClosedAt + 1)));
        var one = Doors(0, 0);
        one[K.Doors[4]] = 100;
        Assert.Equal(0, C("FO_DOORS_CLOSED", one));
    }

    [Fact]
    public void Irs_all_nav_needs_all_three_at_nav()
    {
        Assert.Equal(1, C("FO_IRS_ALL_NAV", D((K.IrsSwitches[0], 2), (K.IrsSwitches[1], 2), (K.IrsSwitches[2], 2))));
        Assert.Equal(0, C("FO_IRS_ALL_NAV", D((K.IrsSwitches[0], 2), (K.IrsSwitches[1], 1), (K.IrsSwitches[2], 2))));
        Assert.Equal(1, C("FO_IRS_ALL_OFF", D((K.IrsSwitches[0], 0), (K.IrsSwitches[1], 0), (K.IrsSwitches[2], 0))));
    }

    [Fact]
    public void Irs_aligned_needs_the_battery_on_and_every_align_light_out()
    {
        var d = K.IrsSwitches.Select(k => (k, 2.0)).Concat(K.IrsAlignLights.Select(k => (k, 0.0))).Append((K.Battery, 1.0))
            .ToDictionary(x => x.Item1, x => x.Item2);
        Assert.Equal(1, C("FO_IRS_ALIGNED", d));
        d[K.Battery] = 0;                                   // unpowered: every light reads out
        Assert.Equal(0, C("FO_IRS_ALIGNED", d));
        d[K.Battery] = 1;
        d[K.IrsAlignLights[1]] = 1;
        Assert.Equal(0, C("FO_IRS_ALIGNED", d));
    }

    // ------------------------------------------------------------------ engines

    [Theory]
    [InlineData(true, 54.9, 0.0)]
    [InlineData(true, 55.0, 1.0)]
    [InlineData(false, 47.9, 0.0)]
    [InlineData(false, 48.0, 1.0)]
    public void Engine_running_thresholds_by_variant(bool pw, double n2, double expected)
    {
        Assert.Equal(pw ? E.PwRunningN2 : E.RrRunningN2, pw ? 55.0 : 48.0);
        Assert.Equal(expected, C("FO_ENG1_RUNNING", Variant(pw, ("FO_ENG1_N2", n2))));
        Assert.Equal(expected, C("FO_ENGINES_RUNNING", Variant(pw, ("FO_ENG1_N2", n2), ("FO_ENG2_N2", 70))));
        Assert.Equal(0, C("FO_ENGINES_RUNNING", Variant(pw, ("FO_ENG1_N2", 70), ("FO_ENG2_N2", 20))));
    }

    [Fact]
    public void The_start_selector_latch_is_n2_47_on_both_variants()
    {
        Assert.Equal(47.0, E.EngineRunningN2);
        Assert.False(Bbs757FoChecklistDefinitions.EngineLit(46.9));
        Assert.True(Bbs757FoChecklistDefinitions.EngineLit(47.0));
        Assert.False(Bbs757FoChecklistDefinitions.EngineLit(double.NaN));
    }

    [Fact]
    public void Engines_stopped_below_10_percent()
    {
        Assert.Equal(1, C("FO_ENGINES_STOPPED", D(("FO_ENG1_N2", 9.9), ("FO_ENG2_N2", 0))));
        Assert.Equal(0, C("FO_ENGINES_STOPPED", D(("FO_ENG1_N2", 10), ("FO_ENG2_N2", 0))));
    }

    [Theory]
    [InlineData(25.0, 99.0, 1.0)]
    [InlineData(25.0, 100.0, 0.0)]       // a hot core is motored first
    [InlineData(24.9, 20.0, 0.0)]
    [InlineData(40.0, -10.0, 1.0)]
    public void Rr_fuel_point_needs_n2_25_and_egt_below_100(double n2, double egt, double expected)
        => Assert.Equal(expected, C("FO_ENG1_FUEL_POINT", Variant(false, ("FO_ENG1_N2", n2), (K.LeftEgt, egt))));

    [Fact]
    public void Rr_fuel_point_is_nan_on_an_unread_egt()
        => Assert.True(double.IsNaN(C("FO_ENG2_FUEL_POINT", Variant(false, ("FO_ENG2_N2", 30)))));

    [Fact]
    public void Pw_fuel_point_is_n2_25_and_needs_no_egt()
    {
        Assert.Equal(1, C("FO_ENG2_FUEL_POINT", Variant(true, ("FO_ENG2_N2", 25))));
        Assert.Equal(0, C("FO_ENG2_FUEL_POINT", Variant(true, ("FO_ENG2_N2", 24.9))));
        Assert.Equal(1, C("FO_ENG2_FUEL_POINT", Variant(true, ("FO_ENG2_N2", 30), (K.RightEgt, 400))));   // no hot-start rule on a PW
    }

    // ------------------------------------------------------------------ autothrottle, speedbrake, flaps

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(0.0, 0.0)]
    public void Autothrottle_armed_reads_bluebirds_own_flag(double flag, double expected)
    {
        Assert.Equal("BBS757_AT_ARMED", K.AutothrottleArmedState);
        Assert.Equal(expected, C("FO_AT_ARMED", D((K.AutothrottleArmedState, flag))));
        // The MCP switch alone is not the A/T's state (R40).
        Assert.True(double.IsNaN(C("FO_AT_ARMED", D((K.AutothrottleArm, K.AutothrottleArmed)))));
    }

    [Theory]
    [InlineData(0.0, 0.0, 1.0, 0.0)]
    [InlineData(10.0, 1.0, 0.0, 0.0)]
    [InlineData(4.9, 0.0, 1.0, 0.0)]
    [InlineData(15.0, 1.0, 0.0, 0.0)]
    [InlineData(50.0, 0.0, 0.0, 1.0)]
    [InlineData(100.0, 0.0, 0.0, 1.0)]
    public void Speedbrake_bands(double lever, double armed, double down, double extended)
    {
        var d = D((K.Speedbrake, lever));
        Assert.Equal(armed, C("FO_SPEEDBRAKE_ARMED", d));
        Assert.Equal(down, C("FO_SPEEDBRAKE_DOWN", d));
        Assert.Equal(extended, C("FO_SPEEDBRAKE_EXTENDED", d));
    }

    [Fact]
    public void Takeoff_flaps_follow_the_plan_or_any_takeoff_detent()
    {
        Assert.Equal(1, C("FO_FLAPS_TAKEOFF", D((K.FlapsHandle, 2)), takeoffFlaps: 5));
        Assert.Equal(0, C("FO_FLAPS_TAKEOFF", D((K.FlapsHandle, 3)), takeoffFlaps: 5));
        Assert.Equal(1, C("FO_FLAPS_TAKEOFF", D((K.FlapsHandle, 3))));        // no plan: 1, 5, 15 or 20
        Assert.Equal(0, C("FO_FLAPS_TAKEOFF", D((K.FlapsHandle, 0))));
        Assert.Equal(0, C("FO_FLAPS_TAKEOFF", D((K.FlapsHandle, 5))));        // 25: a landing flap
        Assert.True(double.IsNaN(C("FO_FLAPS_TAKEOFF", new())));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(4.0, 0.0)]       // "20": a takeoff flap
    [InlineData(5.0, 1.0)]       // "25"
    [InlineData(6.0, 1.0)]       // "30"
    [InlineData(5.5, 0.0)]       // between detents
    public void Landing_flaps_are_25_or_30(double handle, double expected)
        => Assert.Equal(expected, C("FO_FLAPS_LANDING", D((K.FlapsHandle, handle))));

    // ------------------------------------------------------------------ lights

    [Fact]
    public void Preflight_lights_off_includes_the_runway_turnoff_lights()
    {
        var off = new[] { K.BeaconLight, K.StrobeLights, K.WingLights, K.LeftLanding, K.RightLanding, K.LeftTurnoff, K.RightTurnoff, K.TaxiLight }
            .ToDictionary(k => k, _ => 0.0);
        Assert.Equal(1, C("FO_PREFLIGHT_LIGHTS_OFF", off));
        off[K.RightTurnoff] = 1;
        Assert.Equal(0, C("FO_PREFLIGHT_LIGHTS_OFF", off));
    }

    [Fact]
    public void Preflight_lights_off_is_false_with_only_the_taxi_light_on()
    {
        var d = new[] { K.BeaconLight, K.StrobeLights, K.WingLights, K.LeftLanding, K.RightLanding, K.LeftTurnoff, K.RightTurnoff }
            .ToDictionary(k => k, _ => 0.0);
        d[K.TaxiLight] = 1;
        Assert.Equal(0, C("FO_PREFLIGHT_LIGHTS_OFF", d));
    }

    [Fact]
    public void Ground_lights_off_needs_both_turnoffs_and_the_taxi_light_off()
    {
        Assert.Equal(1, C("FO_GROUND_LIGHTS_OFF", D((K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 0))));
        Assert.Equal(0, C("FO_GROUND_LIGHTS_OFF", D((K.LeftTurnoff, 1), (K.RightTurnoff, 0), (K.TaxiLight, 0))));
        Assert.Equal(0, C("FO_GROUND_LIGHTS_OFF", D((K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 1))));
    }

    [Fact]
    public void Ground_lights_on_needs_both_turnoffs_and_the_taxi_light_on()
    {
        Assert.Equal(1, C("FO_GROUND_LIGHTS_ON", D((K.LeftTurnoff, 1), (K.RightTurnoff, 1), (K.TaxiLight, 1))));
        Assert.Equal(0, C("FO_GROUND_LIGHTS_ON", D((K.LeftTurnoff, 1), (K.RightTurnoff, 1), (K.TaxiLight, 0))));
        Assert.Equal(0, C("FO_GROUND_LIGHTS_ON", D((K.LeftTurnoff, 0), (K.RightTurnoff, 1), (K.TaxiLight, 1))));
    }

    [Fact]
    public void Parked_lights_are_wing_logo_and_taxi_off_whatever_the_position_lights()
    {
        var d = D((K.WingLights, 0), (K.LogoLights, 0), (K.TaxiLight, 0), (K.PositionLights, 1));
        Assert.Equal(1, C("FO_PARKED_LIGHTS_OFF", d));
        d[K.TaxiLight] = 1;
        Assert.Equal(0, C("FO_PARKED_LIGHTS_OFF", d));
    }

    // ------------------------------------------------------------------ power

    [Theory]
    [InlineData(2.0, 1.0, 1.0)]       // running, generator control ON
    [InlineData(2.0, 0.0, 0.0)]       // running, generator control OFF
    [InlineData(1.0, 1.0, 0.0)]       // still starting
    [InlineData(0.0, 1.0, 0.0)]
    public void The_apu_is_on_line_running_with_its_generator_control_on(double apu, double gen, double expected)
        => Assert.Equal(expected, C("FO_APU_ON_LINE", D((K.ApuState, apu), (K.ApuGen, gen))));

    // ------------------------------------------------------------------ the center tank in pounds (plan 29)

    /// <summary>The center tank in pounds for the other Boeings' policy: BlueBird's density when read, 6.7 lb/gal until then.</summary>
    [Fact]
    public void Center_fuel_pounds_fall_back_to_6_7_per_gallon()
    {
        string density = MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey;
        Assert.Equal(6.7, E.FallbackPoundsPerGallon);
        Assert.Equal(2010, C("FO_CENTER_FUEL_LBS", D((K.CenterFuelGallons, 300))), 6);
        Assert.Equal(2016, C("FO_CENTER_FUEL_LBS", D((K.CenterFuelGallons, 300), (density, 6.72))), 6);
        Assert.Equal(2010, C("FO_CENTER_FUEL_LBS", D((K.CenterFuelGallons, 300), (density, 0))), 6);
        Assert.True(double.IsNaN(C("FO_CENTER_FUEL_LBS", D((density, 6.7)))));
        Assert.Contains(density, E.PollFieldList);
    }

    /// <summary>Before Start's center pumps use the policy's own empty threshold (1,000 lb), so no write ever meets the gate.</summary>
    [Fact]
    public void Center_pumps_for_start_follow_the_pound_threshold()
    {
        string density = MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey;
        Assert.Equal(0, C("FO_CENTER_FUEL_FOR_PUMPS", D((K.CenterFuelGallons, 149))));                   // 998 lb
        Assert.Equal(1, C("FO_CENTER_FUEL_FOR_PUMPS", D((K.CenterFuelGallons, 160))));                   // 1,072 lb
        Assert.Equal(0, C("FO_CENTER_FUEL_FOR_PUMPS", D((K.CenterFuelGallons, 160), (density, 6.0))));   // 960 lb
    }

    /// <summary>
    /// Plan 29 (owner, 2026-11-08: "use the same rules as the 737s"): "APU: OFF" is the APU switch at OFF, as the 737s'
    /// <c>APU_Selector &lt; 0.5</c>, never the APU's running state: BlueBird runs the APU on through its cool-down, so a
    /// hand tick judged by the state was un-ticked with "Unable to complete" while the APU was on its way off.
    /// </summary>
    [Fact]
    public void Apu_off_is_the_selector_even_while_the_apu_cools_down()
    {
        Assert.Equal(1, C("FO_APU_OFF", D((K.ApuSelector, 0), (K.ApuState, 2))));   // switched off, cooling down
        Assert.Equal(1, C("FO_APU_OFF", D((K.ApuSelector, 0), (K.ApuState, 0))));
        Assert.Equal(1, C("FO_APU_OFF", D((K.ApuSelector, 0))));                    // the state unread: the switch decides
        Assert.Equal(0, C("FO_APU_OFF", D((K.ApuSelector, 1), (K.ApuState, 3))));   // the switch ON
        Assert.True(double.IsNaN(C("FO_APU_OFF", D((K.ApuState, 0)))));             // the switch unread
    }

    [Fact]
    public void Apu_or_external_power_one_lit_source_is_enough()
    {
        Assert.Equal(1, C("FO_APU_OR_EXT_POWER_ON", D((K.ExtPwrOn, 1))));        // APU unread
        Assert.Equal(1, C("FO_APU_OR_EXT_POWER_ON", D((K.ApuState, 2))));        // EXT PWR unread
        Assert.Equal(0, C("FO_APU_OR_EXT_POWER_ON", D((K.ExtPwrOn, 0), (K.ApuState, 1))));
        Assert.True(double.IsNaN(C("FO_APU_OR_EXT_POWER_ON", D((K.ExtPwrOn, 0)))));
    }

    [Fact]
    public void Fuel_panel_center_pumps_follow_the_center_tank()
    {
        (string, double)[] Panel(double centerPumps, double gallons) => new[]
        {
            (K.LeftFwdPump, 1.0), (K.LeftAftPump, 1.0), (K.RightFwdPump, 1.0), (K.RightAftPump, 1.0),
            (K.CenterLeftPump, centerPumps), (K.CenterRightPump, centerPumps), (K.XfeedFwd, 0.0), (K.XfeedAft, 0.0),
            (K.CenterFuelGallons, gallons),
        };
        Assert.Equal(1, C("FO_FUEL_PANEL_SET", D(Panel(0, 0))));
        Assert.Equal(0, C("FO_FUEL_PANEL_SET", D(Panel(1, 0))));
        Assert.Equal(1, C("FO_FUEL_PANEL_SET", D(Panel(1, 2000))));
        Assert.Equal(0, C("FO_FUEL_PANEL_SET", D(Panel(0, 2000))));
        // Plan 29: judged in pounds against the policy's 1,000 lb: 160 gallons at 6.0 lb/gal is 960 lb, an empty tank.
        var light = Panel(1, 160).Append((MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey, 6.0)).ToArray();
        Assert.Equal(0, C("FO_FUEL_PANEL_SET", D(light)));
    }

    // ------------------------------------------------------------------ through the live evaluator

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "currentAircraftTitle")]
    private static extern ref string AircraftTitle(SimConnectManager manager);

    private static E Live(string title, params (string Key, double Value)[] cache)
    {
        var sc = SeededSimConnectCache.With(cache);
        AircraftTitle(sc) = title;
        var e = new E();
        e.SetSimConnect(sc);
        return e;
    }

    [Fact]
    public void The_live_evaluator_takes_the_variant_from_the_aircraft_title()
    {
        // The L:var reads 0 (unwritten, or RR) but the title says PW.
        var e = Live(Pw, (K.EngineVariant, 0), (K.LeftFuelPw, 0), (K.RightFuelPw, 0));
        Assert.True(e.IsPrattAndWhitney);
        Assert.Equal(1, e.GetValue("FO_FUEL_CONTROLS_CUTOFF"));

        var rr = Live(Rr, (K.EngineVariant, 1), (K.LeftFuelRr, 2), (K.RightFuelRr, 2));
        Assert.False(rr.IsPrattAndWhitney);
        Assert.Equal(1, rr.GetValue("FO_FUEL_CONTROLS_CUTOFF"));
    }

    [Fact]
    public void The_live_evaluator_falls_back_to_the_lvars_pw_and_then_to_unknown()
    {
        Assert.True(Live("", (K.EngineVariant, 1)).IsPrattAndWhitney);
        Assert.Null(Live("", (K.EngineVariant, 0)).IsPrattAndWhitney);      // 0 is also "never written"
        Assert.Null(Live("").IsPrattAndWhitney);
        Assert.False(Live(Rr, (K.EngineVariant, 0)).IsPrattAndWhitney);
    }

    [Fact]
    public void The_live_evaluator_serves_the_windows_n2_and_nan_before_it()
    {
        var e = Live(Pw);
        Assert.True(double.IsNaN(e.GetValue("FO_ENG1_N2")));
        Assert.True(double.IsNaN(e.EngineN2(2)));
        e.SetEngineN2(60, 21);
        Assert.Equal(60, e.GetValue("FO_ENG1_N2"));
        Assert.Equal(21, e.EngineN2(2));
        Assert.Equal(1, e.GetValue("FO_ENG1_RUNNING"));
        Assert.Equal(0, e.GetValue("FO_ENGINES_RUNNING"));
        Assert.True(double.IsNaN(e.EngineN2(3)));
    }

    [Fact]
    public void The_live_evaluator_reads_on_ground_from_the_cache()
    {
        Assert.True(Live(Pw, ("SIM_ON_GROUND", 1)).OnGround);
        Assert.False(Live(Pw, ("SIM_ON_GROUND", 0)).OnGround);
        Assert.Null(Live(Pw).OnGround);
    }
}
