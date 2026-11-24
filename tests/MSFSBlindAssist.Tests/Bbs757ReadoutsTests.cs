using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's readouts that need no simulator: the flap detents and their surface angles,
/// the gear, the speedbrake and the parking brake words, the fuel and weight sentences, the engine
/// rows, the trim in units, the EXEC light, the MCP button lights and the change tracker that decides
/// what is spoken on change. CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch
/// a comma or a U+2212 minus.
/// </summary>
public class Bbs757ReadoutsTests
{
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

    // ---- Flaps ------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "UP")]
    [InlineData(1, "1")]
    [InlineData(2, "5")]
    [InlineData(3, "15")]
    [InlineData(4, "20")]
    [InlineData(5, "25")]
    [InlineData(6, "30")]
    public void The_flap_handle_index_names_the_757_detent(double index, string name)
    {
        Assert.Equal(name, Bbs757Readouts.FlapDetentName(index));
        Assert.Equal($"Flaps {name}", Bbs757Readouts.FlapsPhrase(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public void An_index_that_is_no_detent_names_nothing(double index)
    {
        Assert.Null(Bbs757Readouts.FlapDetentName(index));
        Assert.Equal("Flaps unavailable", Bbs757Readouts.FlapsPhrase(index));
    }

    [Fact]
    public void An_unread_handle_is_unavailable()
    {
        Assert.Equal("Flaps unavailable", Bbs757Readouts.FlapsPhrase(null));
        Assert.Equal("Flaps unavailable", Bbs757Readouts.FlapsReadout(null, 5, moving: null));
    }

    [Fact]
    public void The_detent_angles_are_blueBirds_flight_model_angles()
    {
        // flight_model.cfg [FLAPS.0] flaps-position.0..6: 0, 1, 5, 11, 20, 25, 34 degrees. Flaps 15
        // is 11 degrees and flaps 30 is 34 on this aircraft, so the detent name is not the angle.
        Assert.Equal(new double[] { 0, 1, 5, 11, 20, 25, 34 }, Bbs757Readouts.FlapDetentAngles);
    }

    [Theory]
    [InlineData(2, 5.0)]
    [InlineData(2, 4.7)]     // within 0.4 of the detent's angle
    [InlineData(3, 11.0)]    // flaps 15 sits at 11 degrees
    [InlineData(6, 34.0)]
    [InlineData(0, 0.2)]
    [InlineData(1, 1.0)]
    [InlineData(1, 0.7)]
    public void Surfaces_at_the_detents_angle_add_nothing(double index, double angle)
    {
        Assert.Equal(Bbs757Readouts.FlapsPhrase(index), Bbs757Readouts.FlapsReadout(index, angle, moving: false));
        Assert.Equal(Bbs757Readouts.FlapsPhrase(index), Bbs757Readouts.FlapsReadout(index, angle, moving: null));
    }

    [Fact]
    public void The_tolerance_is_below_half_the_smallest_gap_between_detents()
    {
        // Detent 1 is 1 degree: a tolerance of a whole degree let a surface still at 0 pass as "Flaps 1".
        double smallestGap = Bbs757Readouts.FlapDetentAngles.Zip(Bbs757Readouts.FlapDetentAngles.Skip(1), (a, b) => b - a).Min();
        Assert.True(Bbs757Readouts.FlapAngleTolerance < smallestGap / 2);
        Assert.Equal(0.4, Bbs757Readouts.FlapAngleTolerance);
    }

    [Fact]
    public void Flaps_1_with_the_surfaces_still_up_says_so()
    {
        // The measured no-hydraulics case: FLAPS_INCR moved the lever to 1, the surfaces stayed at 0.
        Assert.Equal("Flaps 1, surfaces at 0 degrees", Bbs757Readouts.FlapsReadout(1, 0, moving: false));
        Assert.Equal("Flaps 1, surfaces at 0 degrees", Bbs757Readouts.FlapsReadout(1, 0, moving: null));
        Assert.Equal("Flaps 5, surfaces at 4 degrees", Bbs757Readouts.FlapsReadout(2, 4.3, moving: false));
    }

    [Fact]
    public async Task Flaps_1_while_the_surfaces_travel_says_moving()
    {
        Assert.Equal("Flaps 1, moving, 0 degrees",
            await Bbs757Readouts.ReadFlapsAsync(FlapIo(1, 0.0, 0.3), CancellationToken.None));
    }

    [Theory]
    [InlineData(1.0, "Flaps UP, surfaces at 1 degree")]
    [InlineData(0.6, "Flaps UP, surfaces at 1 degree")]    // rounds to 1
    [InlineData(1.4, "Flaps UP, surfaces at 1 degree")]
    [InlineData(1.6, "Flaps UP, surfaces at 2 degrees")]
    public void One_degree_is_singular(double angle, string phrase)
    {
        Assert.Equal(phrase, Bbs757Readouts.FlapsReadout(0, angle, moving: false));
        Assert.Equal(phrase.Replace("surfaces at", "moving,"), Bbs757Readouts.FlapsReadout(0, angle, moving: true));
    }

    [Fact]
    public void Moving_surfaces_say_moving_and_the_angle()
    {
        Assert.Equal("Flaps 5, moving, 3 degrees", Bbs757Readouts.FlapsReadout(2, 2.6, moving: true));
    }

    [Fact]
    public void Surfaces_that_are_off_the_detent_and_still_say_where_they_are()
    {
        // No hydraulics: the lever moves, the surfaces do not (measured on the ground).
        Assert.Equal("Flaps 5, surfaces at 0 degrees", Bbs757Readouts.FlapsReadout(2, 0, moving: false));
        Assert.Equal("Flaps 5, surfaces at 0 degrees", Bbs757Readouts.FlapsReadout(2, 0, moving: null));
    }

    [Fact]
    public void An_unread_angle_leaves_the_detent_alone()
    {
        Assert.Equal("Flaps 30", Bbs757Readouts.FlapsReadout(6, null, moving: null));
    }

    [Fact]
    public void The_flap_angle_is_spoken_whole_and_invariant()
    {
        InCulture("de-DE", () =>
            Assert.Equal("Flaps 30, moving, 17 degrees", Bbs757Readouts.FlapsReadout(6, 16.6, moving: true)));
    }

    [Theory]
    [InlineData(5.0, 5.0, false)]
    [InlineData(5.0, 5.3, true)]
    [InlineData(5.0, 4.8, true)]
    [InlineData(5.0, 5.05, false)]
    public void Surfaces_move_when_two_reads_differ(double first, double second, bool moving)
    {
        Assert.Equal(moving, Bbs757Readouts.FlapsMoving(first, second));
    }

    private static McpIo FlapIo(double? index, params double?[] angles)
    {
        int reads = 0;
        return new McpIo
        {
            Read = (key, _) => Task.FromResult(key == Bbs757Readouts.FlapsHandleKey ? index
                : key == Bbs757Readouts.FlapsAngleKey ? angles[Math.Min(reads++, angles.Length - 1)] : null),
            Write = _ => throw new InvalidOperationException("a readout writes nothing"),
            Delay = (_, _) => Task.CompletedTask,
        };
    }

    [Fact]
    public async Task L_at_the_detent_reads_the_angle_once()
    {
        Assert.Equal("Flaps 15", await Bbs757Readouts.ReadFlapsAsync(FlapIo(3, 11.0, 99.0), CancellationToken.None));
    }

    [Fact]
    public async Task L_off_the_detent_reads_again_and_says_moving_when_the_angle_changed()
    {
        Assert.Equal("Flaps 5, moving, 3 degrees", await Bbs757Readouts.ReadFlapsAsync(FlapIo(2, 2.0, 2.8), CancellationToken.None));
    }

    [Fact]
    public async Task L_off_the_detent_and_still_says_where_the_surfaces_are()
    {
        Assert.Equal("Flaps 5, surfaces at 0 degrees", await Bbs757Readouts.ReadFlapsAsync(FlapIo(2, 0.0, 0.0), CancellationToken.None));
    }

    [Fact]
    public async Task L_with_nothing_read_is_unavailable()
    {
        Assert.Equal("Flaps unavailable", await Bbs757Readouts.ReadFlapsAsync(FlapIo(null, new double?[] { null }), CancellationToken.None));
        Assert.Equal("Flaps 20", await Bbs757Readouts.ReadFlapsAsync(FlapIo(4, new double?[] { null }), CancellationToken.None));
    }

    // ---- Gear -------------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 1.0, 1.0, Bbs757GearState.Down)]
    [InlineData(0.0, 0.0, 0.0, Bbs757GearState.Up)]
    [InlineData(0.5, 1.0, 1.0, Bbs757GearState.Transit)]
    [InlineData(1.0, 0.0, 0.0, Bbs757GearState.Transit)]
    [InlineData(0.995, 0.999, 1.0, Bbs757GearState.Down)]
    [InlineData(0.005, 0.0, 0.0, Bbs757GearState.Up)]
    public void The_legs_say_down_up_or_in_transit(double nose, double left, double right, Bbs757GearState state)
    {
        Assert.Equal(state, Bbs757Readouts.GearLegs(nose, left, right));
    }

    [Fact]
    public void A_leg_not_read_leaves_the_gear_unknown()
    {
        Assert.Null(Bbs757Readouts.GearLegs(null, 1, 1));
        Assert.Equal("Gear unavailable", Bbs757Readouts.GearPhrase(null));
    }

    [Theory]
    [InlineData(Bbs757GearState.Down, "Gear down")]
    [InlineData(Bbs757GearState.Up, "Gear up")]
    [InlineData(Bbs757GearState.Transit, "Gear in transit")]
    public void The_gear_announcement_is_one_of_three_phrases(Bbs757GearState state, string phrase)
    {
        Assert.Equal(phrase, Bbs757Readouts.GearPhrase(state));
    }

    // The lever is BlueBird's L:B757_GEAR_HANDLE_POS: 0 UP, 0.5 OFF, 1 DOWN (a flight of 2026-10-28).
    [Theory]
    [InlineData(1.0, 1.0, "Gear down, lever down")]
    [InlineData(0.0, 0.0, "Gear up, lever up")]
    [InlineData(0.5, 0.0, "Gear up, lever off")]       // the 757's cruise setting
    [InlineData(0.5, 1.0, "Gear down, lever off")]     // flight 2: the lever stepped to OFF before the gear moved
    [InlineData(1.0, 0.5, "Gear in transit, lever down")]
    [InlineData(0.0, 0.5, "Gear in transit, lever up")]
    [InlineData(0.0, 1.0, "Gear down, lever up")]      // on the ground with no hydraulics, or a disagreement
    [InlineData(1.0, 0.0, "Gear up, lever down")]
    public void Shift_G_always_names_the_lever_off_included(double handle, double legs, string expected)
    {
        Assert.Equal(expected, Bbs757Readouts.GearReadout(handle, legs, legs, legs));
    }

    [Theory]
    [InlineData(0.2, "lever up")]
    [InlineData(0.3, "lever off")]
    [InlineData(0.7, "lever off")]
    [InlineData(0.8, "lever down")]
    public void A_lever_between_detents_is_said_at_the_nearest(double handle, string lever)
    {
        Assert.EndsWith(lever, Bbs757Readouts.GearReadout(handle, 1, 1, 1));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Shift_G_reads_the_same_under_a_comma_culture(string culture)
    {
        Assert.Equal("Gear down, lever off", TestCulture.Run(culture, () => Bbs757Readouts.GearReadout(0.5, 1, 1, 1)));
    }

    [Fact]
    public void Shift_G_with_the_lever_unread_says_the_legs()
    {
        Assert.Equal("Gear down", Bbs757Readouts.GearReadout(null, 1, 1, 1));
        Assert.Equal("Gear in transit", Bbs757Readouts.GearReadout(null, 0.5, 0.5, 0.5));
        Assert.Equal("Gear unavailable", Bbs757Readouts.GearReadout(1, null, 1, 1));
    }

    // ---- Speedbrake and parking brake -------------------------------------------------------

    [Theory]
    [InlineData(0, "Speedbrake down")]
    [InlineData(10, "Speedbrake armed")]
    [InlineData(100, "Speedbrake up")]
    [InlineData(50, "Speedbrake up 50 percent")]
    [InlineData(47, "Speedbrake up 50 percent")]   // nearest ten
    [InlineData(31, "Speedbrake up 30 percent")]
    [InlineData(2, "Speedbrake down")]
    [InlineData(12, "Speedbrake armed")]
    [InlineData(18, "Speedbrake up 20 percent")]
    public void The_speedbrake_lever_says_down_armed_up_or_how_far_up(double lever, string phrase)
    {
        Assert.Equal(phrase, Bbs757Readouts.SpeedbrakePhrase(lever));
    }

    [Fact]
    public void An_unread_speedbrake_is_unavailable()
    {
        Assert.Equal("Speedbrake unavailable", Bbs757Readouts.SpeedbrakePhrase(null));
        Assert.Equal("Speedbrake unavailable", Bbs757Readouts.SpeedbrakePhrase(double.NaN));
    }

    [Theory]
    [InlineData(0, "Down")]
    [InlineData(10, "Armed")]
    [InlineData(100, "Up")]
    [InlineData(60, "Up 60 percent")]
    public void The_speedbrake_status_row_shows_the_same_words(double lever, string words)
    {
        Assert.Equal(words, Bbs757Readouts.SpeedbrakeDisplay(lever));
    }

    [Theory]
    [InlineData(1, "Parking brake set")]
    [InlineData(0, "Parking brake released")]
    public void The_parking_brake_says_set_or_released(double value, string phrase)
    {
        Assert.Equal(phrase, Bbs757Readouts.ParkingBrakePhrase(value));
    }

    [Fact]
    public void An_unread_parking_brake_is_unavailable()
    {
        Assert.Equal("Parking brake unavailable", Bbs757Readouts.ParkingBrakePhrase(null));
    }

    // ---- Fuel and weight --------------------------------------------------------------------

    [Fact]
    public void F_says_the_total_and_each_tank_in_pounds_rounded_to_ten()
    {
        // 1,902 gal per wing at 6.70 lb/gal (session 5's load): 12,743.4 lb each.
        Assert.Equal("Fuel 25480 pounds: left 12740, right 12740, center 0",
            Bbs757Readouts.FuelPhrase(1902.0, 1902.0, 0.0, 6.7, kilograms: false));
    }

    [Fact]
    public void Shift_F_says_the_same_in_kilograms()
    {
        // 12,743.4 lb = 5,780.4 kg per wing.
        Assert.Equal("Fuel 11560 kilograms: left 5780, right 5780, center 0",
            Bbs757Readouts.FuelPhrase(1902.0, 1902.0, 0.0, 6.7, kilograms: true));
    }

    [Fact]
    public void The_fuel_total_is_the_sum_of_the_spoken_tanks()
    {
        string said = Bbs757Readouts.FuelPhrase(1000.5, 1000.5, 2973.0, 6.7, kilograms: false);

        // 6,703.35 lb per wing → 6,700; 19,919.1 lb center → 19,920; total of the parts 33,320.
        Assert.Equal("Fuel 33320 pounds: left 6700, right 6700, center 19920", said);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Fuel_and_weight_are_spoken_without_the_pcs_separators(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("Fuel 11560 kilograms: left 5780, right 5780, center 0",
                Bbs757Readouts.FuelPhrase(1902.0, 1902.0, 0.0, 6.7, kilograms: true));
            Assert.Equal("Gross weight 208239 pounds", Bbs757Readouts.WeightPhrase(208239.4, kilograms: false));
            // 208,239.4 lb / 2.20462 = 94,455.9 kg.
            Assert.Equal("Gross weight 94456 kilograms", Bbs757Readouts.WeightPhrase(208239.4, kilograms: true));
        });
    }

    [Fact]
    public void Fuel_with_a_tank_or_the_density_unread_is_unavailable()
    {
        Assert.Equal("Fuel unavailable", Bbs757Readouts.FuelPhrase(null, 1902, 0, 6.7, kilograms: false));
        Assert.Equal("Fuel unavailable", Bbs757Readouts.FuelPhrase(1902, 1902, 0, null, kilograms: false));
        Assert.Equal("Fuel unavailable", Bbs757Readouts.FuelPhrase(1902, 1902, 0, double.NaN, kilograms: true));
    }

    [Fact]
    public void An_unread_weight_is_unavailable()
    {
        Assert.Equal("Gross weight unavailable", Bbs757Readouts.WeightPhrase(null, kilograms: false));
        Assert.Equal("Gross weight unavailable", Bbs757Readouts.WeightPhrase(double.NaN, kilograms: true));
    }

    [Fact]
    public void A_fuel_reading_a_hair_below_zero_is_never_minus_zero()
    {
        Assert.Equal("Fuel 0 pounds: left 0, right 0, center 0",
            Bbs757Readouts.FuelPhrase(-0.0001, 0, 0, 6.7, kilograms: false));
    }

    // ---- Engines ----------------------------------------------------------------------------

    [Fact]
    public void Ten_engine_rows_two_engines_five_values_each()
    {
        var rows = Bbs757Readouts.EngineRows;

        Assert.Equal(10, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());
        Assert.Equal(rows.Count, rows.Select(r => r.Label).Distinct().Count());
        foreach (int engine in new[] { 1, 2 })
        {
            Assert.Contains(rows, r => r.SimVar == $"TURB ENG N1:{engine}");
            Assert.Contains(rows, r => r.SimVar == $"TURB ENG N2:{engine}");
            Assert.Contains(rows, r => r.SimVar == $"GENERAL ENG EXHAUST GAS TEMPERATURE:{engine}" && r.Units == "celsius");
            Assert.Contains(rows, r => r.SimVar == $"ENG FUEL FLOW PPH:{engine}" && r.Units == "pounds per hour");
            Assert.Contains(rows, r => r.SimVar == $"TURB ENG PRESSURE RATIO:{engine}");
        }
        // The discriminator first (VAR-1): "Left N1", never "N1 Left".
        Assert.All(rows, r => Assert.Matches("^(Left|Right) ", r.Label));
    }

    [Theory]
    [InlineData("N1", 23.44, "23.4 percent")]
    [InlineData("N2", 61.06, "61.1 percent")]
    [InlineData("EGT", 425.4, "425 degrees C")]
    [InlineData("FF", 1250.6, "1251 pounds per hour")]
    [InlineData("EPR", 1.0249, "1.02")]
    public void Engine_values_are_shown_invariant(string kind, double value, string text)
    {
        var row = Bbs757Readouts.EngineRows.First(r => r.Kind == kind);
        InCulture("de-DE", () => Assert.Equal(text, Bbs757Readouts.EngineText(row, value)));
    }

    // ---- Stabilizer trim (task 11c, ruling R35) -----------------------------------------------

    [Theory]
    [InlineData(2.3, 2.9)]       // at load: 2.3 degrees, 2.95 units on the strip (session 7)
    [InlineData(2.381, 3.0)]     // five ELEV_TRIM_UP later
    [InlineData(-10.0, 0.4)]     // far nose down: the strip stops at 0.436
    public void The_trim_is_blueBirds_indicator_strip_in_units(double degrees, double units)
    {
        Assert.Equal(units, Math.Round(Bbs757Readouts.StabTrimUnits(degrees), 1));
    }

    [Fact]
    public void The_units_are_the_strips_own_formula()
    {
        // max(0.436, -0.002148 d² + 0.803152 d + 1.11308), the cockpit XML's strip.
        Assert.Equal(2.94897, Bbs757Readouts.StabTrimUnits(2.3), 5);
        Assert.Equal(1.11308, Bbs757Readouts.StabTrimUnits(0), 5);
        Assert.Equal(0.436, Bbs757Readouts.StabTrimUnits(-0.9), 5);
        Assert.Equal(0.436, Bbs757Readouts.StabTrimUnits(-30), 5);
    }

    [Theory]
    [InlineData(5.3, "Trim 5.3")]
    [InlineData(2.94897, "Trim 2.9")]
    [InlineData(0.436, "Trim 0.4")]
    [InlineData(10.0, "Trim 10.0")]
    public void The_trim_words_are_the_737s_one_rule_with_a_dot(double units, string phrase)
    {
        // One rule for every airframe that speaks units (the PMDG 737, the iFly and the 757): StabTrimUnitsCallout.
        InCulture("de-DE", () => Assert.Equal(phrase, StabTrimUnitsCallout.Phrase(units)));
    }

    [Fact]
    public void More_nose_up_trim_never_reads_fewer_units()
    {
        // The shared trim call-out keys its debounce on the units, so they must never fall as the degrees rise.
        double last = double.MinValue;
        for (int hundredths = -2000; hundredths <= 2000; hundredths++)
        {
            double units = Bbs757Readouts.StabTrimUnits(hundredths / 100.0);
            Assert.True(units >= last, $"{hundredths / 100.0} degrees");
            last = units;
        }
    }

    // ---- The EXEC light (task 11c, ruling R36) --------------------------------------------------

    [Theory]
    [InlineData(1, "EXEC light on")]
    [InlineData(0, "EXEC light off")]
    [InlineData(0.9, "EXEC light on")]
    public void The_exec_light_says_on_or_off(double value, string phrase)
    {
        Assert.Equal(phrase, Bbs757Readouts.ExecLightPhrase(value));
    }

    [Fact]
    public void An_unread_exec_light_is_unavailable()
    {
        Assert.Equal("EXEC light unavailable", Bbs757Readouts.ExecLightPhrase(null));
        Assert.Equal("EXEC light unavailable", Bbs757Readouts.ExecLightPhrase(double.NaN));
    }

    // ---- MCP lights -------------------------------------------------------------------------

    [Fact]
    public void Fourteen_mcp_lights_in_the_measured_names()
    {
        var names = new[] { "THR", "SPD", "LNAV", "VNAV", "FLCH", "HOLD_1", "VS", "HOLD_2", "BCRS_1", "LOC", "APP", "CMD_1", "CMD_2", "CMD_3" };

        Assert.Equal(names.Select(n => $"B757_MCP_{n}_BUTTON"), Bbs757McpLights.All.Select(l => l.LightVar));
        // The light var is the node's own name, so the variable key must differ from it.
        Assert.All(Bbs757McpLights.All, l => Assert.NotEqual(l.Node, l.Key));
        Assert.Equal(14, Bbs757McpLights.All.Select(l => l.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("B757_MCP_CMD_1_BUTTON", "CMD L")]
    [InlineData("B757_MCP_CMD_2_BUTTON", "CMD C")]
    [InlineData("B757_MCP_CMD_3_BUTTON", "CMD R")]
    [InlineData("B757_MCP_HOLD_1_BUTTON", "HDG HOLD")]
    [InlineData("B757_MCP_HOLD_2_BUTTON", "ALT HOLD")]
    public void The_cmd_buttons_are_left_center_and_right(string node, string label)
    {
        Assert.Equal(label, Bbs757McpLights.ForNode(node)!.Label);
    }

    [Fact]
    public void Every_light_is_named_as_its_button_is_on_the_panel()
    {
        var map = Bbs757ControlMap.Load();

        Assert.All(Bbs757McpLights.All, light =>
        {
            var control = map.Controls.Single(c => c.Node == light.Node);
            Assert.Equal(control.Label, light.Label);
            Assert.Equal(Bbs757Kinds.Button, control.Kind);
        });
    }

    [Theory]
    [InlineData(1, "CMD R on")]
    [InlineData(0, "CMD R off")]
    [InlineData(0.9, "CMD R on")]
    public void A_light_reads_on_or_off(double value, string phrase)
    {
        Assert.Equal(phrase, Bbs757McpLights.Phrase("CMD R", value));
    }

    [Fact]
    public void An_unread_light_is_unavailable()
    {
        Assert.Equal("CMD R unavailable", Bbs757McpLights.Phrase("CMD R", null));
        Assert.Equal("unavailable", Bbs757McpLights.State(null));
        Assert.Equal("On", Bbs757McpLights.State(1));
        Assert.Equal("Off", Bbs757McpLights.State(0));
    }

    [Theory]
    [InlineData(0.0, 0.0, "CMD L did not engage")]
    [InlineData(1.0, 1.0, "CMD L did not disengage")]
    [InlineData(0.0, 1.0, null)]   // it engaged: the caption change says so
    [InlineData(1.0, 0.0, null)]
    [InlineData(null, 0.0, null)]  // nothing known before: no claim
    [InlineData(0.0, null, null)]
    public void A_window_press_the_aircraft_refused_says_so(double? before, double? after, string? phrase)
    {
        Assert.Equal(phrase, Bbs757McpLights.RefusedPhrase("CMD L", before, after));
    }

    [Fact]
    public void A_node_with_no_light_has_none()
    {
        Assert.Null(Bbs757McpLights.ForNode("B757_MCP_AP_SPD_MACH_SW"));
        Assert.Null(Bbs757McpLights.ForKey("B757_MCP_THR_BUTTON"));
        Assert.NotNull(Bbs757McpLights.ForKey(Bbs757McpLights.All[0].Key));
    }

    // ---- The change tracker -----------------------------------------------------------------

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        var t = new Bbs757ChangeTracker();

        Assert.Null(t.Observe("gear", "Gear down", 0));
        Assert.Null(t.Observe("gear", "Gear down", 1000));
        Assert.Equal("Gear in transit", t.Observe("gear", "Gear in transit", 2000));
        Assert.Equal("Gear up", t.Observe("gear", "Gear up", 3000));
    }

    [Fact]
    public void A_change_inside_an_echo_window_is_tracked_not_spoken()
    {
        var t = new Bbs757ChangeTracker();
        t.Observe("light", "CMD R off", 0);

        t.ArmEcho("light", untilMs: 5000);
        Assert.Null(t.Observe("light", "CMD R on", 1000));     // the press's own read-back speaks it
        Assert.Null(t.Observe("light", "CMD R on", 6000));     // unchanged after the window
        Assert.Equal("CMD R off", t.Observe("light", "CMD R off", 7000));
    }

    [Fact]
    public void A_rebaseline_after_a_read_back_keeps_the_same_state_silent()
    {
        var t = new Bbs757ChangeTracker();
        t.Observe("brake", "Parking brake released", 0);

        t.Rebaseline("brake", "Parking brake set");

        Assert.Null(t.Observe("brake", "Parking brake set", 100));
    }

    [Fact]
    public void A_reset_makes_the_next_reading_a_baseline_again()
    {
        var t = new Bbs757ChangeTracker();
        t.Observe("flaps", "Flaps UP", 0);
        t.ArmEcho("flaps", 10_000);

        t.Reset();

        Assert.Null(t.Observe("flaps", "Flaps 5", 100));
        Assert.Equal("Flaps 15", t.Observe("flaps", "Flaps 15", 200));
    }

    [Fact]
    public void A_row_the_flight_load_did_not_change_speaks_its_first_change_after_the_quiet()
    {
        // The batch delivers a value only when it changes, so a row the new flight leaves as it was is never
        // delivered again: its baseline has to survive the load, or its first real change would be taken for one.
        var t = new Bbs757ChangeTracker();
        t.Observe("valve", "Left start valve closed", 0);

        t.BeginLoadQuiet();
        t.EndLoadQuiet();

        Assert.Equal("Left start valve open", t.Observe("valve", "Left start valve open", 61_000));
    }

    [Fact]
    public void A_change_the_flight_load_brings_is_a_silent_baseline()
    {
        var t = new Bbs757ChangeTracker();
        t.Observe("flaps", "Flaps UP", 0);
        t.ArmEcho("brake", 500_000);

        t.BeginLoadQuiet();

        Assert.Null(t.Observe("flaps", "Flaps 5", 30_000));             // the new flight's flaps: no change
        t.EndLoadQuiet();
        Assert.Null(t.Observe("flaps", "Flaps 5", 61_000));
        Assert.Equal("Flaps 15", t.Observe("flaps", "Flaps 15", 62_000));
        t.Observe("brake", "Parking brake set", 61_000);
        Assert.Equal("Parking brake released", t.Observe("brake", "Parking brake released", 62_000));   // the old flight's echo is gone
    }

    [Fact]
    public void A_reset_ends_a_load_quiet()
    {
        var t = new Bbs757ChangeTracker();
        t.BeginLoadQuiet();

        t.Reset();

        Assert.Null(t.Observe("flaps", "Flaps UP", 100));
        Assert.Equal("Flaps 5", t.Observe("flaps", "Flaps 5", 200));
    }
}
