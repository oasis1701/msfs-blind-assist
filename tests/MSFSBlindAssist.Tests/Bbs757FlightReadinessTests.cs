using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using R = MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's full-flight readiness work (plan 01, task 10b) at the definition: what is registered and
/// where it shows, what is spoken on change and what is not, the Ctrl+M rows, the readouts and sets
/// with no simulator (each says it is unavailable, never nothing); and task 11's first-flight fixes:
/// the trim in BlueBird's units and the EXEC light. The MCP windows' tests (the autopilot window, A/T
/// Arm, the value windows' buttons) are in Bbs757McpWindowTests.
/// Whether the aircraft moves is checked in the sim.
/// </summary>
public class Bbs757FlightReadinessTests
{
    private static readonly BBS757Definition Def = new();

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; }
        public readonly HashSet<string> Muted = new(StringComparer.Ordinal);
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig()
        {
            Def = new BBS757Definition { NestRunning = () => true, NowMs = () => Now, IsMonitorMuted = Muted.Contains };
        }

        public void Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);
    }

    // ---- Registration -------------------------------------------------------------------------

    [Theory]
    [InlineData(R.FlapsHandleKey, "FLAPS HANDLE INDEX", "Flaps")]
    [InlineData(R.SpeedbrakeKey, "B757_FC_SPOILER_LEVER", "Speedbrake")]
    [InlineData(R.ParkingBrakeKey, "B757_FC_PARK_BRAKE", "Parking Brake")]
    [InlineData(R.GearNoseKey, "GEAR CENTER POSITION", "Gear")]
    public void The_state_rows_stream_and_keep_a_monitor_row(string key, string name, string label)
    {
        var def = Def.GetVariables()[key];

        Assert.Equal(name, def.Name);
        Assert.Equal(label, def.DisplayName);
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced);
        Assert.False(def.ExcludeFromMonitorManager);
        Assert.True(def.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void Shift_G_reads_blueBirds_gear_lever_never_the_stock_handle()
    {
        // Measured on a flight of 2026-10-28: the stock GEAR HANDLE POSITION read up with BlueBird's lever at OFF
        // and the gear still down.
        var row = Def.GetVariables()[R.GearHandleKey];
        Assert.Equal("B757_GEAR_HANDLE_POS", row.Name);
        Assert.Equal(SimVarType.LVar, row.Type);
        Assert.Equal(UpdateFrequency.OnRequest, row.UpdateFrequency);
    }

    [Fact]
    public void The_parking_brake_is_blueBirds_own_variable_never_the_stock_one()
    {
        // Measured: the stock BRAKE PARKING POSITION read 0 with BlueBird's brake set.
        Assert.DoesNotContain(Def.GetVariables().Values, v => v.Name == "BRAKE PARKING POSITION");
        Assert.Equal(SimVarType.LVar, Def.GetVariables()[R.ParkingBrakeKey].Type);
        Assert.Equal("Set", Def.GetVariables()[R.ParkingBrakeKey].ValueDescriptions[1]);
    }

    [Fact]
    public void The_flap_handle_row_names_the_detents()
    {
        Assert.Equal(new[] { "UP", "1", "5", "15", "20", "25", "30" },
            Def.GetVariables()[R.FlapsHandleKey].ValueDescriptions.OrderBy(d => d.Key).Select(d => d.Value));
    }

    [Fact]
    public void The_main_gear_legs_speak_under_the_one_gear_row()
    {
        var keys = MonitorRowBuilder.Build(Def.GetVariables()).Select(r => r.Key).ToList();

        Assert.Contains(R.GearNoseKey, keys);
        Assert.DoesNotContain(R.GearLeftKey, keys);
        Assert.DoesNotContain(R.GearRightKey, keys);
        // They speak the gear row's call-out, so MainForm leaves them out of its mute wrap ([VAR-8]).
        Assert.True(Def.IsMuteWrapExempt(R.GearNoseKey));
        Assert.True(Def.IsMuteWrapExempt(R.GearLeftKey));
        Assert.True(Def.IsMuteWrapExempt(R.GearRightKey));
        Assert.False(Def.IsMuteWrapExempt(R.FlapsHandleKey));
    }

    [Fact]
    public void Ctrl_M_lists_every_mcp_light_and_the_flight_controls()
    {
        var rows = MonitorRowBuilder.Build(Def.GetVariables()).ToList();
        var keys = rows.Select(r => r.Key).ToList();

        Assert.All(Bbs757McpLights.All, l => Assert.Contains(l.Key, keys));
        Assert.Contains(R.FlapsHandleKey, keys);
        Assert.Contains(R.SpeedbrakeKey, keys);
        Assert.Contains(R.ParkingBrakeKey, keys);
        // Silent rows earn no checkbox that mutes nothing ([VAR-9]).
        Assert.DoesNotContain(R.FlapsAngleKey, keys);
        Assert.All(R.EngineRows, e => Assert.DoesNotContain(e.Key, keys));
        Assert.Contains(Bbs757Squawk.CodeKey, keys);   // spoken on change since plan 14, so it has its checkbox
        Assert.Contains(rows, r => r.Key == Bbs757McpLights.ForNode("B757_MCP_CMD_3_BUTTON")!.Key);
    }

    [Fact]
    public void The_mcp_lights_are_their_own_lvars_under_keys_that_are_not_the_buttons()
    {
        var vars = Def.GetVariables();
        Assert.All(Bbs757McpLights.All, light =>
        {
            var def = vars[light.Key];
            Assert.Equal(light.LightVar, def.Name);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(light.Label, def.DisplayName);
            // The button keeps its own key and its press variable.
            Assert.Equal(light.LightVar + "_PRESS", vars[light.Node].Name);
        });
    }

    [Fact]
    public void Engine_rows_are_read_only_and_silent()
    {
        var vars = Def.GetVariables();
        Assert.All(R.EngineRows, row =>
        {
            var def = vars[row.Key];
            Assert.Equal(row.SimVar, def.Name);
            Assert.Equal(row.Units, def.Units);
            Assert.False(def.IsAnnounced);
            Assert.True(def.ExcludeFromMonitorManager);
            Assert.True(def.RenderAsReadOnlyStatus);
        });
    }

    [Fact]
    public void The_readouts_reads_are_on_request()
    {
        var vars = Def.GetVariables();
        foreach (string key in Bbs757Altimeters.KohlsmanKeys.Append(Bbs757Altimeters.DecisionHeightKey)
                     .Append(R.TotalWeightKey).Append(R.GearHandleKey))
        {
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.True(vars[key].ExcludeFromMonitorManager);
        }
        Assert.Equal("KOHLSMAN SETTING MB:3", vars[Bbs757Altimeters.Kohlsman3Key].Name);
        Assert.Equal("DECISION HEIGHT", vars[Bbs757Altimeters.DecisionHeightKey].Name);
        Assert.Equal("TOTAL WEIGHT", vars[R.TotalWeightKey].Name);
    }

    [Fact]
    public void The_squawk_field_is_a_typed_box_whose_empty_text_is_not_0000()
    {
        var field = Def.GetVariables()[Bbs757Squawk.SetKey];

        Assert.Contains("_SET", Bbs757Squawk.SetKey);
        Assert.Equal(SimVarType.Event, field.Type);   // no data definition
        Assert.True(field.UnparseableTextAsNaN);
        Assert.DoesNotMatch("^Set ", field.DisplayName);   // MainForm adds its own words
        Assert.Equal("TRANSPONDER CODE:1", Def.GetVariables()[Bbs757Squawk.CodeKey].Name);
        Assert.Equal("BCO16", Def.GetVariables()[Bbs757Squawk.CodeKey].Units);
    }

    // ---- Panels -------------------------------------------------------------------------------

    [Fact]
    public void The_engines_panel_shows_both_engines_beside_its_switches()
    {
        // R45: an engine start (selector GND, watch N2, fuel control RUN, EGT) happens in one panel; the start
        // valves (plan 06) follow the engine values there.
        Assert.Equal(R.EngineRows.Select(r => r.Key).Concat(new[] { "BBS757_L_START_VALVE", "BBS757_R_START_VALVE" }),
            Def.GetPanelDisplayVariables()[BBS757Definition.EnginesPanel]);
        Assert.Contains("BBS_fuelcut_PW1", Def.GetPanelControls()[BBS757Definition.EnginesPanel]);
        Assert.DoesNotContain(BBS757Definition.EnginesPanel, Def.GetPanelStructure()[BBS757Definition.StatusSection]);
    }

    [Fact]
    public void The_mcp_panel_shows_its_lights()
    {
        Assert.Equal(Bbs757McpLights.All.Select(l => l.Key), Def.GetPanelDisplayVariables()[BBS757Definition.McpPanel]);
    }

    [Fact]
    public void The_transponder_panel_has_the_squawk_field_after_the_mode_knob_and_shows_the_code()
    {
        var controls = Def.GetPanelControls()[BBS757Definition.TransponderPanel];

        Assert.Equal(BBS757Definition.TransponderModeNode, controls[0]);
        Assert.Equal(Bbs757Squawk.SetKey, controls[1]);
        Assert.Single(controls, Bbs757Squawk.SetKey);
        Assert.Equal(new[] { Bbs757Squawk.CodeKey }, Def.GetPanelDisplayVariables()[BBS757Definition.TransponderPanel]);
    }

    // ---- Spoken on change -----------------------------------------------------------------------

    [Fact]
    public void Flaps_speak_a_new_detent_but_not_the_first_reading()
    {
        var rig = new Rig();

        rig.Deliver(R.FlapsHandleKey, 0);
        rig.Deliver(R.FlapsHandleKey, 2);
        rig.Deliver(R.FlapsHandleKey, 2);
        rig.Deliver(R.FlapsHandleKey, 3);

        Assert.Equal(new[] { "Flaps 5", "Flaps 15" }, rig.Speech.All);
    }

    [Fact]
    public void The_gear_speaks_in_transit_then_down_once_all_three_legs_are_down()
    {
        var rig = new Rig();
        foreach (string leg in new[] { R.GearNoseKey, R.GearLeftKey, R.GearRightKey }) rig.Deliver(leg, 0);

        rig.Deliver(R.GearNoseKey, 0.3);
        rig.Deliver(R.GearLeftKey, 0.4);
        rig.Deliver(R.GearNoseKey, 1);
        rig.Deliver(R.GearLeftKey, 1);
        rig.Deliver(R.GearRightKey, 1);

        Assert.Equal(new[] { "Gear in transit", "Gear down" }, rig.Speech.All);
    }

    [Fact]
    public void A_muted_gear_row_silences_all_three_legs()
    {
        var rig = new Rig();
        rig.Muted.Add(R.GearNoseKey);
        foreach (string leg in new[] { R.GearNoseKey, R.GearLeftKey, R.GearRightKey }) rig.Deliver(leg, 1);

        rig.Deliver(R.GearRightKey, 0.5);
        rig.Deliver(R.GearLeftKey, 0.5);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Speedbrake_and_parking_brake_speak_their_changes()
    {
        var rig = new Rig();
        rig.Deliver(R.SpeedbrakeKey, 0);
        rig.Deliver(R.ParkingBrakeKey, 1);

        rig.Deliver(R.SpeedbrakeKey, 10);
        rig.Deliver(R.ParkingBrakeKey, 0);
        rig.Deliver(R.SpeedbrakeKey, 100);

        Assert.Equal(new[] { "Speedbrake armed", "Parking brake released", "Speedbrake up" }, rig.Speech.All);
    }

    [Fact]
    public void An_mcp_light_speaks_on_and_off()
    {
        var rig = new Rig();
        var cmdR = Bbs757McpLights.ForNode("B757_MCP_CMD_3_BUTTON")!;
        rig.Deliver(cmdR.Key, 0);

        rig.Deliver(cmdR.Key, 1);
        rig.Deliver(cmdR.Key, 0);

        Assert.Equal(new[] { "CMD R on", "CMD R off" }, rig.Speech.All);
    }

    [Fact]
    public void A_ctrl_m_muted_light_is_silenced_by_mainforms_wrap()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var light = Bbs757McpLights.ForNode("B757_MCP_LNAV_BUTTON")!;
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { light.Key } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, light.Key, 0);
        MuteWrap.Deliver(def, speech, settings, light.Key, 1);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 0);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 1);

        Assert.Equal(new[] { "Flaps 1" }, speech.All);
    }

    [Fact]
    public void A_flight_load_makes_the_next_reading_a_baseline_again()
    {
        var rig = new Rig();
        rig.Deliver(R.FlapsHandleKey, 0);

        rig.Def.OnSimContextReset();
        rig.Deliver(R.FlapsHandleKey, 6);
        rig.Def.ResetAnnouncementBaselines();
        rig.Deliver(R.FlapsHandleKey, 0);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_silent_rows_are_left_to_the_generic_path()
    {
        var rig = new Rig();

        Assert.False(rig.Def.ProcessSimVarUpdate(R.FlapsAngleKey, 5, rig.Speech));
        Assert.False(rig.Def.ProcessSimVarUpdate(R.EngineRows[0].Key, 20, rig.Speech));
        Assert.True(rig.Def.ProcessSimVarUpdate(Bbs757Squawk.CodeKey, 0x4743, rig.Speech));   // plan 14: a baseline, silent
        Assert.True(rig.Def.ProcessSimVarUpdate(R.FlapsHandleKey, 0, rig.Speech));
        Assert.Empty(rig.Speech.All);
    }

    // ---- The trim in BlueBird's units (task 11c, ruling R35) ------------------------------------------

    private const string TrimKey = "MON_ElevatorTrim";   // the shared trim row: stock ELEVATOR TRIM POSITION, degrees

    private static void InCulture(string name, Action body)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
            body();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_trim_is_spoken_in_blueBirds_units_while_it_moves()
    {
        var rig = new Rig();

        rig.Deliver(TrimKey, 2.3);      // the load's 2.95 units: a silent baseline
        rig.Deliver(TrimKey, 2.381);    // five ELEV_TRIM_UP later (session 7): 3.01 units
        rig.Deliver(TrimKey, 2.381);    // still: nothing
        rig.Deliver(TrimKey, 2.6);      // 3.18 units

        Assert.Equal(new[] { "Trim 3.0", "Trim 3.2" }, rig.Speech.All);
        // The 737s' one rule, from BlueBird's strip.
        Assert.Equal(StabTrimUnitsCallout.Phrase(R.StabTrimUnits(2.6)), rig.Speech.All[1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_trim_is_spoken_with_a_dot_under_a_comma_decimal_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var rig = new Rig();

            rig.Deliver(TrimKey, 2.3);
            rig.Deliver(TrimKey, -10);   // the strip's floor, 0.436

            Assert.Equal(new[] { "Trim 0.4" }, rig.Speech.All);
        });
    }

    [Fact]
    public void The_loads_trim_on_a_tenths_boundary_does_not_flip_with_jitter()
    {
        // 2.3 degrees is 2.949 units, a hair under the 2.95 boundary; 2.31 is 2.957. The shared
        // hysteresis (0.03 degrees) holds the 2.9 the load read, as it does for every aircraft's scale.
        var rig = new Rig();

        rig.Deliver(TrimKey, 2.3);
        rig.Deliver(TrimKey, 2.31);
        rig.Deliver(TrimKey, 2.29);
        rig.Deliver(TrimKey, 2.31);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_trim_keeps_its_one_ctrl_m_row_and_has_no_status_row()
    {
        var rows = MonitorRowBuilder.Build(Def.GetVariables());

        Assert.Equal(new[] { TrimKey }, rows.Where(r => r.Label.Contains("trim", StringComparison.OrdinalIgnoreCase)).Select(r => r.Key));
        Assert.DoesNotContain(TrimKey, Def.GetPanelDisplayVariables().SelectMany(p => p.Value));
    }

    // ---- The EXEC light (task 11c, ruling R36) -------------------------------------------------------

    [Fact]
    public void The_exec_light_speaks_its_changes_after_a_silent_baseline()
    {
        var rig = new Rig();

        rig.Deliver(R.ExecLightKey, 0);   // the cockpit as found
        rig.Deliver(R.ExecLightKey, 1);   // ACTIVATE
        rig.Deliver(R.ExecLightKey, 1);
        rig.Deliver(R.ExecLightKey, 0);   // EXEC

        Assert.Equal(new[] { "EXEC light on", "EXEC light off" }, rig.Speech.All);
    }

    [Fact]
    public void The_exec_light_is_the_captains_cdu_light_with_its_own_ctrl_m_row_and_no_panel()
    {
        var exec = Def.GetVariables()[R.ExecLightKey];

        Assert.Equal("FMC_EXEC_ACTIVE_1", exec.Name);
        Assert.Equal(SimVarType.LVar, exec.Type);
        Assert.Equal(UpdateFrequency.Continuous, exec.UpdateFrequency);
        Assert.True(exec.IsAnnounced);
        Assert.False(exec.ExcludeFromMonitorManager);
        Assert.Contains(MonitorRowBuilder.Build(Def.GetVariables()), r => r.Key == R.ExecLightKey && r.Label == "EXEC light");
        Assert.DoesNotContain(R.ExecLightKey, Def.GetPanelControls().SelectMany(p => p.Value));
        Assert.DoesNotContain(R.ExecLightKey, Def.GetPanelDisplayVariables().SelectMany(p => p.Value));
    }

    [Fact]
    public void A_ctrl_m_muted_exec_light_is_silenced_by_mainforms_wrap()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { R.ExecLightKey } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, R.ExecLightKey, 0);
        MuteWrap.Deliver(def, speech, settings, R.ExecLightKey, 1);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 0);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 1);

        Assert.Equal(new[] { "Flaps 1" }, speech.All);
        Assert.False(def.IsMuteWrapExempt(R.ExecLightKey));
    }

    [Fact]
    public void A_flight_load_makes_the_exec_lights_next_reading_a_baseline_again()
    {
        var rig = new Rig();
        rig.Deliver(R.ExecLightKey, 0);

        rig.Def.OnSimContextReset();
        rig.Deliver(R.ExecLightKey, 1);   // the new flight's light, as found
        rig.Def.ResetAnnouncementBaselines();
        rig.Deliver(R.ExecLightKey, 0);

        Assert.Empty(rig.Speech.All);
    }

    // ---- What a press moves -----------------------------------------------------------------------

    [Theory]
    [InlineData("B757_MCP_CMD_3_BUTTON", "BBS757_MCP_LIGHT_CMD_3")]
    [InlineData("B757_MCP_HOLD_1_BUTTON", "BBS757_MCP_LIGHT_HOLD_1")]
    [InlineData("BBS757_SPOILERARMED_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_SPOILERUP_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_SPOILERDOWN_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_PEDESTAL_Park_Brake_Lever", R.ParkingBrakeKey)]
    [InlineData("ENGINE_Switch_ATDisc_1", null)]
    [InlineData("B757_OVHD_BATTERY_BUTTON", null)]
    public void A_press_reads_back_the_row_it_moves(string node, string? statusKey)
    {
        Assert.Equal(statusKey, BBS757Definition.LinkedStatusKey(node));
    }

    [Theory]
    [InlineData("BBS757_MCP_LIGHT_CMD_3", 1.0, "CMD R on")]
    [InlineData("BBS757_MCP_LIGHT_HOLD_2", 0.0, "ALT HOLD off")]
    [InlineData(R.SpeedbrakeKey, 10.0, "Speedbrake armed")]
    [InlineData(R.ParkingBrakeKey, 1.0, "Parking brake set")]
    public void The_read_back_says_the_state_not_pressed(string statusKey, double value, string phrase)
    {
        Assert.Equal(phrase, BBS757Definition.LinkedPhrase(statusKey, value));
    }

    // ---- Presses and picks through the write seam ----------------------------------------------

    /// <summary>A simulated sim behind the definition's writes: fresh and cached reads by key, the writes made, the resyncs asked.</summary>
    private sealed class FakeSim
    {
        public bool CanWrite = true;
        public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);
        public readonly Dictionary<string, double?> Cache = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();
        public readonly List<string> Resyncs = new();
        public Action<string>? OnWrite;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, ct) => Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null),
            Cached = key => Cache.TryGetValue(key, out double? v) ? v : null,
            Write = rpn => { Writes.Add(rpn); OnWrite?.Invoke(rpn); },
            RequestRead = key => Resyncs.Add(key),
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        };
    }

    private static string LightOf(string node) => Bbs757McpLights.ForNode(node)!.Key;

    [Theory]
    [InlineData("B757_MCP_CMD_3_BUTTON", "BBS757_MCP_LIGHT_CMD_3", 1.0, "CMD R on")]
    [InlineData("BBS757_SPOILERARMED_CLICK", R.SpeedbrakeKey, 10.0, "Speedbrake armed")]
    [InlineData("B757_MCP_LNAV_BUTTON", "BBS757_MCP_LIGHT_LNAV", null, "LNAV pressed")]   // the light unread
    [InlineData("ENGINE_Switch_ATDisc_1", "", null, "TO/GA pressed")]                   // nothing to read back
    public void A_panel_press_says_what_it_did_once(string node, string statusKey, double? readBack, string said)
    {
        var sim = new FakeSim();
        if (statusKey.Length > 0) sim.Fresh[statusKey] = readBack;
        var def = new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();

        Assert.True(def.HandleUIVariableSet(node, 1, def.GetVariables()[node], new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { said }, speech.All);
        Assert.Contains(node, sim.Resyncs);
    }

    [Fact]
    public void A_parking_brake_pick_closes_its_echo_at_the_read_back_so_a_snap_back_is_spoken()
    {
        const string brake = "BBS757_PEDESTAL_Park_Brake_Lever";
        var sim = new FakeSim();
        long now = 0;
        var def = new BBS757Definition { NestRunning = () => true, NowMs = () => now, WriteIo = _ => sim.Io,
            ParkingBrakeRealistic = () => false };   // EASY: the toggle is the whole write
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate(R.ParkingBrakeKey, 0, speech);   // released: the baseline
        sim.Fresh[brake] = 0;                                     // the pick reads the lever released
        sim.Fresh[R.ParkingBrakeKey] = 1;                         // and the row set after the click
        sim.OnWrite = _ => def.ProcessSimVarUpdate(R.ParkingBrakeKey, 1, speech);   // the click's own echo

        Assert.True(def.HandleUIVariableSet(brake, 1, def.GetVariables()[brake], new SimConnectManager(IntPtr.Zero), speech));
        Assert.Single(sim.Writes);
        Assert.Empty(speech.All);            // the screen reader read the pick; the row does not say it again

        now = 3000;                          // inside the five-second echo window the pick armed
        def.ProcessSimVarUpdate(R.ParkingBrakeKey, 0, speech);   // the brake snaps back

        Assert.Equal(new[] { "Parking brake released" }, speech.All);
        Assert.Contains(brake, sim.Resyncs);
    }

    [Fact]
    public async Task The_squawk_box_text_tells_an_empty_box_from_letters()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetSquawkAsync("12a4", sim, speech);
        await def.SetSquawkAsync("", sim, speech);

        Assert.Equal(new[] { Bbs757Squawk.InvalidMessage, Bbs757Squawk.EmptyMessage }, speech.Interrupts);
    }

    [Fact]
    public void The_definition_takes_the_squawk_box_text_and_leaves_other_boxes_to_the_numeric_path()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        Assert.True(def.HandleUITypedText(Bbs757Squawk.SetKey, "12a4", def.GetVariables()[Bbs757Squawk.SetKey], sim, speech));
        Assert.False(def.HandleUITypedText("SOME_OTHER_SET", "12", new SimVarDefinition(), sim, speech));
        Assert.Equal(new[] { Bbs757Squawk.InvalidMessage }, speech.Interrupts);
    }

    // ---- Status display -----------------------------------------------------------------------

    [Fact]
    public void The_status_display_shows_words_not_raw_values()
    {
        var rig = new Rig();
        rig.Deliver(R.GearLeftKey, 1);
        rig.Deliver(R.GearRightKey, 1);

        Assert.True(rig.Def.TryGetDisplayOverride(R.GearNoseKey, 1, out string gear));
        Assert.Equal("Down", gear);
        Assert.True(rig.Def.TryGetDisplayOverride(R.SpeedbrakeKey, 10, out string speedbrake));
        Assert.Equal("Armed", speedbrake);
        Assert.True(rig.Def.TryGetDisplayOverride(Bbs757Squawk.CodeKey, 0x4743, out string squawk));
        Assert.Equal("4743", squawk);
        Assert.True(rig.Def.TryGetDisplayOverride(Bbs757McpLights.All[0].Key, 1, out string light));
        Assert.Equal("On", light);
        Assert.True(rig.Def.TryGetDisplayOverride(R.EngineRows[0].Key, 23.44, out string n1));
        Assert.Equal("23.4 percent", n1);
        Assert.True(rig.Def.TryGetDisplayOverride(R.FlapsAngleKey, 4.96, out string angle));
        Assert.Equal("5.0 degrees", angle);
        Assert.True(rig.Def.TryGetDisplayOverride(R.FlapsHandleKey, 3, out string flaps));
        Assert.Equal("15", flaps);
        Assert.False(rig.Def.TryGetDisplayOverride("B757_OVHD_BATTERY_BUTTON", 1, out _));
    }

    // ---- No simulator: every readout and set says so ---------------------------------------------

    [Theory]
    [InlineData(HotkeyAction.ReadFlaps, "Flaps unavailable")]
    [InlineData(HotkeyAction.ReadGear, "Gear unavailable")]
    [InlineData(HotkeyAction.ReadAltimeter, "Altimeters unavailable. DH unavailable")]
    [InlineData(HotkeyAction.ReadFuelQuantity, "Fuel unavailable")]
    [InlineData(HotkeyAction.ReadFuelInfo, "Fuel unavailable")]
    [InlineData(HotkeyAction.ReadWaypointInfo, "Gross weight unavailable")]
    [InlineData(HotkeyAction.ReadGrossWeightKg, "Gross weight unavailable")]
    public async Task A_readout_with_nothing_read_says_it_is_unavailable(HotkeyAction action, string said)
    {
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Reads(new Dictionary<string, double>()) };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(action, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadFlaps, "Flaps UP")]
    [InlineData(HotkeyAction.ReadAltimeter, "Altimeter: 1020, 30.12. DH 200")]
    [InlineData(HotkeyAction.ReadFuelQuantity, "Fuel 25480 pounds: left 12740, right 12740, center 0")]
    [InlineData(HotkeyAction.ReadFuelInfo, "Fuel 11560 kilograms: left 5780, right 5780, center 0")]
    [InlineData(HotkeyAction.ReadWaypointInfo, "Gross weight 208239 pounds")]
    [InlineData(HotkeyAction.ReadGrossWeightKg, "Gross weight 94456 kilograms")]
    [InlineData(HotkeyAction.ReadGear, "Gear down, lever up")]
    public async Task A_readout_speaks_what_it_reads_at_once(HotkeyAction action, string said)
    {
        var values = new Dictionary<string, double>
        {
            [R.FlapsHandleKey] = 0, [R.FlapsAngleKey] = 0,
            [Bbs757Altimeters.Kohlsman1Key] = 1020, [Bbs757Altimeters.Kohlsman2Key] = 1020,
            [Bbs757Altimeters.Kohlsman3Key] = 1020, [Bbs757Altimeters.DecisionHeightKey] = 200,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.LeftFuelKey] = 1902,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.RightFuelKey] = 1902,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.CenterFuelKey] = 0,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey] = 6.7,
            [R.TotalWeightKey] = 208239.4,
            [R.GearHandleKey] = 0, [R.GearNoseKey] = 1, [R.GearLeftKey] = 1, [R.GearRightKey] = 1,
        };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Reads(values) };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(action, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Fact]
    public async Task A_readout_that_fails_still_says_something()
    {
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => new McpIo
            {
                Read = (_, _) => throw new InvalidOperationException("no link"),
                Write = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(HotkeyAction.ReadAltimeter, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "Altimeters unavailable. DH unavailable" }, speech.Interrupts);
    }

    private static McpIo Reads(IReadOnlyDictionary<string, double> values) => new()
    {
        Read = (key, _) => Task.FromResult(values.TryGetValue(key, out double v) ? v : (double?)null),
        Write = _ => throw new InvalidOperationException("a readout writes nothing"),
        Delay = (_, _) => Task.CompletedTask,
    };

    [Fact]
    public async Task An_altimeter_or_minimums_set_that_cannot_land_is_refused_aloud()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var shown = new List<string>();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetAltimetersAsync(16320, sim, speech, shown.Add);
        await def.SetMinimumsAsync(200, sim, speech, shown.Add);

        Assert.Equal(new[] { "Altimeter unavailable", "DH unavailable" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, shown);   // the window's status box says the same
    }

    [Theory]
    [InlineData("", Bbs757Squawk.EmptyMessage)]
    [InlineData("4783", Bbs757Squawk.InvalidMessage)]
    [InlineData("4743", "Squawk unavailable")]   // valid, but nothing can land
    public async Task A_squawk_that_cannot_be_set_says_why(string typed, string said)
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        await def.SetSquawkAsync(typed, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Fact]
    public void The_squawk_field_is_claimed_by_the_definition()
    {
        var def = new BBS757Definition { NestRunning = () => true };

        Assert.True(def.HandleUIVariableSet(Bbs757Squawk.SetKey, double.NaN, def.GetVariables()[Bbs757Squawk.SetKey],
            new SimConnectManager(IntPtr.Zero), new SpeechCapture()));
    }
}
