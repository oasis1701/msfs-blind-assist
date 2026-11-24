using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757-200 profile: its variables and panels come from the generated control map, so
/// what is pinned here is the translation (one variable per control, keyed by node; the status
/// rows; the probe; the panel order) and the parts of the definition that run without a sim (the
/// BBS Nest warning, the refusals). Whether a write moves the aircraft is checked in the sim.
/// </summary>
public class Bbs757DefinitionTests
{
    private static readonly BBS757Definition Def = new();
    private static readonly Bbs757ControlMap Map = Bbs757ControlMap.Load();

    [Fact]
    public void Names_the_aircraft_and_its_profile_code()
    {
        Assert.Equal("BBS_757", Def.AircraftCode);
        Assert.Equal("BlueBird 757-200", Def.AircraftName);
    }

    [Fact]
    public void Every_panel_control_is_a_registered_variable()
    {
        var vars = Def.GetVariables();
        var missing = Def.GetPanelControls().SelectMany(p => p.Value).Where(k => !vars.ContainsKey(k)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_map_control_is_on_a_panel()
    {
        var onPanels = Def.GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);
        Assert.All(Map.Controls, c => Assert.Contains(c.Node, onPanels));
    }

    [Fact]
    public void The_battery_is_its_own_lvar_with_off_and_on()
    {
        var battery = Def.GetVariables()["B757_OVHD_BATTERY_BUTTON"];

        Assert.Equal("B757_OVHD_BATTERY_BUTTON", battery.Name);
        Assert.Equal(SimVarType.LVar, battery.Type);
        Assert.Equal(UpdateFrequency.OnRequest, battery.UpdateFrequency);
        Assert.Equal("Battery", battery.DisplayName);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, battery.ValueDescriptions);
        Assert.False(battery.RenderAsButton);
    }

    [Fact]
    public void The_bridge_probe_is_registered_quiet_and_on_request()
    {
        // The registration opts the 757 into the calc-path probe; every control write here is a
        // calculator write, so CalcWriteCanLand needs the probe's verdict ([MD11-16]'s pattern).
        var probe = Def.GetVariables()["MSFSBA_BRIDGE_PROBE"];

        Assert.Equal("MSFSBA_BRIDGE_PROBE", probe.Name);
        Assert.Equal(SimVarType.LVar, probe.Type);
        Assert.Equal(UpdateFrequency.OnRequest, probe.UpdateFrequency);
        Assert.False(probe.IsAnnounced);
    }

    [Fact]
    public void The_status_panel_lists_the_apu_state()
    {
        // The engine values are on the Pedestal's Engines panel (R45), so the Status section has one panel.
        Assert.Equal(new[] { "Status" }, Def.GetPanelStructure()["Status"]);
        Assert.Contains("B757_APU_STATE", Def.GetPanelDisplayVariables()["Status"]);
        // A display-only panel still needs its (empty) controls entry, or the panel renders nothing ([HS-5]).
        Assert.Empty(Def.GetPanelControls()["Status"]);
    }

    [Fact]
    public void Every_status_row_is_on_the_status_display_then_the_flight_controls()
    {
        // The map's rows, then the flight controls; a later plan's rows may follow.
        var expected = Map.Status.Select(s => s.Key).Concat(new[]
        {
            Bbs757Readouts.FlapsHandleKey, Bbs757Readouts.FlapsAngleKey, Bbs757Readouts.GearNoseKey,
            Bbs757Readouts.SpeedbrakeKey, Bbs757Readouts.ParkingBrakeKey,
        }).ToList();
        Assert.Equal(expected, Def.GetPanelDisplayVariables()["Status"].Take(expected.Count));
    }

    [Fact]
    public void An_announced_status_row_streams_and_keeps_its_monitor_row()
    {
        var apu = Def.GetVariables()["B757_APU_STATE"];

        Assert.Equal(UpdateFrequency.Continuous, apu.UpdateFrequency);
        Assert.True(apu.IsAnnounced);
        Assert.False(apu.ExcludeFromMonitorManager);
        Assert.True(apu.RenderAsReadOnlyStatus);
        Assert.Equal("APU", apu.DisplayName);
        Assert.Equal("Running", apu.ValueDescriptions[2]);
    }

    [Fact]
    public void A_silent_status_row_has_no_monitor_row()
    {
        var rpm = Def.GetVariables()["B757_APU_RPM_PRC"];

        Assert.False(rpm.IsAnnounced);
        Assert.True(rpm.ExcludeFromMonitorManager);
        Assert.True(rpm.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void Ctrl_M_lists_the_announced_status_rows_and_not_the_silent_ones()
    {
        var keys = MonitorRowBuilder.Build(Def.GetVariables()).Select(r => r.Key).ToList();

        Assert.Contains("B757_APU_STATE", keys);
        Assert.Contains("BBS_CA_MASTER_WARNING", keys);
        Assert.Contains("BBS_CA_MASTER_CAUTION", keys);
        Assert.DoesNotContain("B757_APU_RPM_PRC", keys);
        Assert.DoesNotContain("B757_OVHD_BATTERY_BUTTON", keys);   // a panel control, read on request
    }

    [Fact]
    public void Sections_and_panels_follow_the_map_order()
    {
        // In map order; the app's own panels and fields (the squawk field, a later plan's) may sit between.
        var sections = Map.Controls.Select(c => c.Section).Distinct().Append("Status").ToList();
        AssertInOrder(sections, Def.GetPanelStructure().Keys);

        foreach (var section in Map.Controls.Select(c => c.Section).Distinct())
        {
            var panels = Map.Controls.Where(c => c.Section == section).Select(c => c.Panel).Distinct();
            AssertInOrder(panels, Def.GetPanelStructure()[section]);
        }

        foreach (var panel in Map.Controls.Select(c => c.Panel).Distinct())
        {
            var nodes = Map.Controls.Where(c => c.Panel == panel).Select(c => c.Node);
            AssertInOrder(nodes, Def.GetPanelControls()[panel]);
        }
    }

    /// <summary>Every one of <paramref name="expected"/> is in <paramref name="actual"/>, in the same order (a subsequence).</summary>
    private static void AssertInOrder(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var rest = actual.ToList();
        int at = 0;
        foreach (string item in expected)
        {
            int found = rest.IndexOf(item, at);
            Assert.True(found >= 0, $"\"{item}\" is missing, or out of order, in [{string.Join(", ", rest)}]");
            at = found + 1;
        }
    }

    // ---- Task 11d: the panels in BlueBird's preflight-flow order ----------------------------------

    [Fact]
    public void The_pedestal_starts_with_the_control_stand_the_engines_and_the_transponder()
    {
        // Among the map's panels: a later plan's own panel may sit between them.
        var mapPanels = Map.Controls.Select(c => c.Panel).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(new[] { "Control stand", "Engines", "TCAS and transponder" },
            Def.GetPanelStructure()["Pedestal"].Where(mapPanels.Contains).Take(3));
    }

    [Fact]
    public void The_engines_panel_holds_the_start_selectors_and_the_fuel_control_switches_in_start_order()
    {
        // Plan 12 task 10 (the owner: "I wanted engine 1 start then fuel, then engine 2's controls") replaced ruling R37's
        // order here: Ignition, then each engine's start selector followed by its fuel control switches.
        var vars = Def.GetVariables();

        Assert.Equal(
            new[]
            {
                "Ignition", "L ENG Start", "L Fuel Control (PW)", "L Fuel Control (RR)", "R ENG Start",
                "R Fuel Control (PW)", "R Fuel Control (RR)",
            },
            Def.GetPanelControls()["Engines"].Select(key => vars[key].DisplayName));
    }

    [Fact]
    public void The_overhead_has_no_engine_start_panel_and_the_pedestal_no_fuel_control_panel()
    {
        Assert.DoesNotContain("Engine start", Def.GetPanelStructure()["Overhead"]);
        Assert.DoesNotContain("Fuel control", Def.GetPanelStructure()["Pedestal"]);
        Assert.DoesNotContain("Engine start", Def.GetPanelControls().Keys);
        Assert.DoesNotContain("Fuel control", Def.GetPanelControls().Keys);
    }

    [Fact]
    public void The_squawk_field_follows_the_transponder_mode_knob()
    {
        var vars = Def.GetVariables();
        var transponder = Def.GetPanelControls()[BBS757Definition.TransponderPanel];

        Assert.Equal(BBS757Definition.TransponderModeNode, transponder[0]);
        Assert.Equal(Bbs757Squawk.SetKey, transponder[1]);
        Assert.Equal(new[] { "TCAS Mode", "Squawk Code" }, transponder.Take(2).Select(key => vars[key].DisplayName));
    }

    [Fact]
    public void A_map_without_the_mode_knob_leaves_the_squawk_field_at_the_end_of_the_transponder_panel()
    {
        var map = new Bbs757ControlMap
        {
            Controls = Map.Controls.Where(c => c.Node != BBS757Definition.TransponderModeNode).ToList(),
        };
        var transponder = new BBS757Definition(map).GetPanelControls()[BBS757Definition.TransponderPanel];

        Assert.Equal(Bbs757Squawk.SetKey, transponder.Last());
        Assert.DoesNotContain(BBS757Definition.TransponderModeNode, transponder);
    }

    [Fact]
    public void A_panel_is_in_one_section_only_and_every_panel_has_a_controls_entry()
    {
        // GetPanelControls is keyed by panel name: a name in two sections would let one panel's entry replace the
        // other's (the Status section's engine values must never be named like the Pedestal's Engines).
        var names = Def.GetPanelStructure().SelectMany(section => section.Value).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(names.Order(StringComparer.Ordinal), Def.GetPanelControls().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_control_variable_is_an_on_request_lvar_named_by_its_state_var()
    {
        var vars = Def.GetVariables();
        Assert.All(Map.Controls, c =>
        {
            var def = vars[c.Node];
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.False(def.IsAnnounced);
            Assert.Equal(c.StateVar ?? c.Node, def.Name);
            Assert.Equal(c.Label, def.DisplayName);
        });
    }

    [Fact]
    public void Momentary_buttons_render_as_buttons_and_hide_their_resting_state()
    {
        var vars = Def.GetVariables();
        var momentary = Map.Controls.Where(c => c.Kind is Bbs757Kinds.Button or Bbs757Kinds.Held).ToList();

        Assert.NotEmpty(momentary);
        Assert.Contains(momentary, c => c.StateVar == null);   // the "harmless read of the node name" case is real
        Assert.All(momentary, c =>
        {
            Assert.True(vars[c.Node].RenderAsButton);
            Assert.True(vars[c.Node].SuppressRestingButtonState);
        });
    }

    [Fact]
    public void Positional_controls_render_as_combos_with_the_map_positions()
    {
        var vars = Def.GetVariables();
        Assert.All(Map.Controls.Where(c => c.Kind is not (Bbs757Kinds.Button or Bbs757Kinds.Held)), c =>
        {
            var def = vars[c.Node];
            Assert.False(def.RenderAsButton);
            Assert.Equal(c.Positions.ToDictionary(p => p.Value, p => p.Label), def.ValueDescriptions);
        });
    }

    [Fact]
    public void Variables_stay_under_the_data_definition_ceiling()
    {
        // Every registered var is a native SimConnect data definition (or a batch datum), and a
        // connection caps at about 1000 (docs/architecture.md, "data-definition ceiling").
        Assert.True(Def.GetVariables().Count < 900, $"{Def.GetVariables().Count} variables");
    }

    [Fact]
    public void The_757_makes_no_take_off_callouts_of_its_own()
    {
        // Prototype default 7: the aircraft calls 80 knots, V1 and rotate itself.
        Assert.Null(Def.TakeoffCalloutFeedKey);
    }

    // ---- BBS Nest --------------------------------------------------------------------------

    [Fact]
    public void Nest_is_not_running_when_no_process_matches()
    {
        Assert.False(Bbs757Nest.IsRunning(_ => false));
    }

    [Fact]
    public void Nest_is_found_by_its_process_name()
    {
        Assert.True(Bbs757Nest.IsRunning(name => name == "bbs_nest"));
        Assert.False(Bbs757Nest.IsRunning(name => name == "bbs_nest.exe"));
    }

    [Fact]
    public void A_missing_nest_is_said_once_per_connection()
    {
        int checks = 0;
        var def = new BBS757Definition { NestRunning = () => { checks++; return false; } };
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        def.ProcessSimVarUpdate("B757_APU_STATE", 0, speech);
        def.ProcessSimVarUpdate("B757_APU_STATE", 1, speech);

        Assert.Equal(new[] { BBS757Definition.NestMissingWarning }, speech.All);
        Assert.Equal(1, checks);   // the process list is read once, not on every delivery
    }

    [Fact]
    public void The_nest_warning_says_what_to_do()
    {
        Assert.Equal("BBS Nest is not running. The 757's systems will not respond until it runs and the flight is reloaded.",
            BBS757Definition.NestMissingWarning);
    }

    [Fact]
    public void A_running_nest_is_never_mentioned()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        def.ProcessSimVarUpdate("B757_APU_STATE", 0, speech);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_flight_reload_or_reconnect_checks_nest_again()
    {
        bool running = false;
        var def = new BBS757Definition { NestRunning = () => running };
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        def.OnSimContextReset();
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);   // still missing: said again
        running = true;
        def.OnSimContextReset();
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);   // started and reloaded: silent

        Assert.Equal(new[] { BBS757Definition.NestMissingWarning, BBS757Definition.NestMissingWarning }, speech.All);
    }

    [Fact]
    public void A_muted_delivery_defers_the_nest_warning_to_the_next_one()
    {
        // MainForm wraps ProcessSimVarUpdate in announcer.Suppressed for a Ctrl+M-muted row (and the
        // UI-set echo); the warning is no row's, so a wrapped delivery must not use it up.
        var def = new BBS757Definition { NestRunning = () => false };
        var speech = new GatedSpeechCapture();
        var settings = new MSFSBlindAssist.Settings.UserSettings { BBS757DisabledMonitorVariables = new() { "B757_APU_STATE" } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, "B757_APU_STATE", 1);
        Assert.Empty(speech.All);

        MuteWrap.Deliver(def, speech, settings, "SIM_ON_GROUND", 1);
        Assert.Equal(new[] { BBS757Definition.NestMissingWarning }, speech.All);
    }

    [Fact]
    public void The_nest_check_rides_a_batch_that_arrives_even_when_nothing_changes()
    {
        // A cold and dark cockpit can go minutes without a value change; the 1 Hz continuous batch
        // is delivered every second regardless, and MainForm calls OnDeferredFlushBatchDelivered at
        // the end of the batch carrying the watch variable. It must therefore be batch-covered.
        var def = new BBS757Definition { NestRunning = () => false };

        string? watch = def.DeferredFlushWatchVariable;
        Assert.Equal("SIM_ON_GROUND", watch);
        var watched = def.GetVariables()[watch!];
        Assert.Equal(UpdateFrequency.Continuous, watched.UpdateFrequency);
        Assert.True(watched.IsAnnounced);
        Assert.False(watched.ExcludeFromBatch);
    }

    [Fact]
    public void The_first_batch_speaks_a_missing_nest_once_and_stops_watching()
    {
        var def = new BBS757Definition { NestRunning = () => false };
        var speech = new SpeechCapture();

        def.OnDeferredFlushBatchDelivered(speech);
        Assert.Null(def.DeferredFlushWatchVariable);   // the hook costs nothing once settled
        def.OnDeferredFlushBatchDelivered(speech);

        Assert.Equal(new[] { BBS757Definition.NestMissingWarning }, speech.All);
    }

    [Fact]
    public void A_running_nest_settles_silently_on_the_first_batch()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        def.OnDeferredFlushBatchDelivered(speech);

        Assert.Empty(speech.All);
        Assert.Null(def.DeferredFlushWatchVariable);
    }

    [Fact]
    public void A_reload_rearms_the_batch_check()
    {
        var def = new BBS757Definition { NestRunning = () => false };
        var speech = new SpeechCapture();

        def.OnDeferredFlushBatchDelivered(speech);
        def.OnSimContextReset();
        Assert.Equal("SIM_ON_GROUND", def.DeferredFlushWatchVariable);
        def.OnDeferredFlushBatchDelivered(speech);

        Assert.Equal(new[] { BBS757Definition.NestMissingWarning, BBS757Definition.NestMissingWarning }, speech.All);
    }

    [Fact]
    public void A_delivery_and_a_batch_between_them_speak_the_warning_once()
    {
        var def = new BBS757Definition { NestRunning = () => false };
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate("B757_APU_STATE", 0, speech);
        def.OnDeferredFlushBatchDelivered(speech);

        Assert.Equal(new[] { BBS757Definition.NestMissingWarning }, speech.All);
    }

    [Fact]
    public void A_suppressed_announcer_defers_the_batch_warning()
    {
        var def = new BBS757Definition { NestRunning = () => false };
        var speech = new GatedSpeechCapture { Suppressed = true };

        def.OnDeferredFlushBatchDelivered(speech);
        Assert.Empty(speech.All);
        Assert.NotNull(def.DeferredFlushWatchVariable);

        speech.Suppressed = false;
        def.OnDeferredFlushBatchDelivered(speech);
        Assert.Equal(new[] { BBS757Definition.NestMissingWarning }, speech.All);
    }

    // ---- Writes that cannot run without a sim -------------------------------------------------

    [Fact]
    public void An_unknown_key_is_left_to_the_generic_path()
    {
        var def = new BBS757Definition();
        var speech = new SpeechCapture();

        bool handled = def.HandleUIVariableSet("NOT_A_757_CONTROL", 1, new SimVarDefinition(),
            new SimConnectManager(IntPtr.Zero), speech);

        Assert.False(handled);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_press_that_cannot_land_is_refused_aloud()
    {
        // Never connected: CalcWriteCanLand is false, so nothing may be written and the pilot is told.
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var button = Map.Controls.First(c => c.Kind == Bbs757Kinds.Button);

        bool handled = def.HandleUIVariableSet(button.Node, 1, def.GetVariables()[button.Node],
            new SimConnectManager(IntPtr.Zero), speech);

        Assert.True(handled);
        Assert.Equal(new[] { $"{button.Label} unavailable" }, speech.All);
    }

    [Fact]
    public async Task Arrowing_through_a_combo_that_cannot_land_is_refused_once()
    {
        // A closed combo commits every row arrowed over; the refusal is judged on the row it settles on.
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var selector = Map.Controls.First(c => c.Kind == Bbs757Kinds.Switch && c.Positions.Count >= 3);
        var varDef = def.GetVariables()[selector.Node];

        Assert.True(def.HandleUIVariableSet(selector.Node, selector.Positions[1].Value, varDef, sim, speech));
        Assert.True(def.HandleUIVariableSet(selector.Node, selector.Positions[2].Value, varDef, sim, speech));

        await UntilAsync(() => !def.IsPickPending(selector.Node));
        Assert.Equal(new[] { $"{selector.Label} unavailable" }, speech.All);
    }

    [Fact]
    public async Task A_control_with_a_pick_in_flight_has_its_deliveries_consumed()
    {
        // The pre-write fresh read would otherwise move the focused combo back to the old position.
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var battery = def.GetVariables()["B757_OVHD_BATTERY_BUTTON"];

        def.HandleUIVariableSet("B757_OVHD_BATTERY_BUTTON", 1, battery, new SimConnectManager(IntPtr.Zero), speech);

        Assert.True(def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));
        Assert.True(def.ProcessSimVarUpdate("B757_OVHD_BATTERY_BUTTON", 0, speech));

        await UntilAsync(() => !def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));
        Assert.False(def.ProcessSimVarUpdate("B757_OVHD_BATTERY_BUTTON", 0, speech));
        Assert.Equal(new[] { "Battery unavailable" }, speech.All);
    }

    [Fact]
    public async Task A_profile_switch_stops_a_pending_pick_silently()
    {
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var battery = def.GetVariables()["B757_OVHD_BATTERY_BUTTON"];

        def.HandleUIVariableSet("B757_OVHD_BATTERY_BUTTON", 1, battery, new SimConnectManager(IntPtr.Zero), speech);
        def.OnSwitchedAway();

        await UntilAsync(() => !def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task A_flight_reload_drops_a_pick_in_flight()
    {
        // A pick armed before a flight load must never write into the flight that follows.
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var battery = def.GetVariables()["B757_OVHD_BATTERY_BUTTON"];

        def.HandleUIVariableSet("B757_OVHD_BATTERY_BUTTON", 1, battery, new SimConnectManager(IntPtr.Zero), speech);
        Assert.True(def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));

        def.OnSimContextReset();

        Assert.False(def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));
        await Task.Delay(700);   // past the 300 ms settle: the dropped pick says and sends nothing
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task After_a_profile_switch_a_pick_starts_nothing()
    {
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var battery = def.GetVariables()["B757_OVHD_BATTERY_BUTTON"];
        def.OnSwitchedAway();

        Assert.True(def.HandleUIVariableSet("B757_OVHD_BATTERY_BUTTON", 1, battery, new SimConnectManager(IntPtr.Zero), speech));

        Assert.False(def.IsPickPending("B757_OVHD_BATTERY_BUTTON"));
        await Task.Delay(700);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Switching_away_twice_is_harmless()
    {
        // A switch that threw part-way can call it again at exit: the second call must not throw
        // on the lifetime source the first one disposed.
        var def = new BBS757Definition();
        def.OnSwitchedAway();

        var thrown = Record.Exception(def.OnSwitchedAway);

        Assert.Null(thrown);
    }

    [Fact]
    public void The_757_plays_its_own_take_off_speed_callouts()
    {
        Assert.True(new BBS757Definition().PlaysOwnTakeoffSpeedCallouts);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (int waited = 0; waited < 5000 && !condition(); waited += 25) await Task.Delay(25);
        Assert.True(condition(), "timed out");
    }
}
