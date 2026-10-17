// The Cessna 172 G1000's controls, as audited live on MSFS 2024's stock aircraft (2026-10-16,
// docs/invariants/c172.md). Every switch writes its cockpit input event with an explicit value, so
// each one written here must exist in the aircraft's input-event catalog as measured; every panel
// control must reach the definition's write path (the old Standby Battery Toggle was a plain event
// button, which MainForm sends itself, and it switched off the MAIN battery); and the reads the
// audit corrected are pinned so a later "tidy-up" cannot put the old ones back.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CessnaC172G1000ControlsTests
{
    /// <summary>The input events the G1000 definition writes, from the catalog the app dumped for the
    /// stock "C172SP G1000" on 2026-10-16 (logs/input_events.txt, 235 events).</summary>
    private static readonly HashSet<string> MeasuredCatalog = new(StringComparer.Ordinal)
    {
        "ELECTRICAL_BATTERY_1", "ELECTRICAL_BATTERY_STBY_2", "ELECTRICAL_ALTERNATOR_1",
        "ELECTRICAL_LINE_BUS_1_TO_AVIONICS_BUS_1", "ELECTRICAL_LINE_BUS_2_TO_AVIONICS_BUS_2",
        "ENGINE_MAGNETO_1", "FUEL_PUMP_1", "FUEL_VALVE_SHUTOFFVALVE", "FUEL_SELECTOR_1",
        "LIGHTING_NAV_0", "LIGHTING_BEACON_1", "LIGHTING_STROBE_0", "LIGHTING_LANDING_1", "LIGHTING_TAXI_1",
        "DEICE_PITOT_1", "LANDING_GEAR_PARKINGBRAKE", "SAFETY_ALTERNATE_STATIC_AIR",
        "UNKNOWN_COVER_ENGINE", "UNKNOWN_COVER_PITOT", "UNKNOWN_COVER_STATIC_PORT",
        "AS1000_AUTOPILOT_AP_PFD", "AS1000_AUTOPILOT_FD_PFD", "AS1000_AUTOPILOT_HEADING_PFD",
        "AS1000_AUTOPILOT_ALTITUDE_PFD", "AS1000_AUTOPILOT_NAVIGATION_PFD",
        "AS1000_AUTOPILOT_VERTICAL_NAVIGATION_PFD", "AS1000_AUTOPILOT_APPROACH_PFD",
        "AS1000_AUTOPILOT_BACKCOURSE_PFD", "AS1000_AUTOPILOT_VERTICALSPEED_PFD",
        "AS1000_AUTOPILOT_FLIGHTLEVELCHANGE_PFD", "AS1000_AUTOPILOT_VERTICALSPEED_UP_PFD",
        "AS1000_AUTOPILOT_VERTICALSPEED_DOWN_PFD",
    };

    private static readonly CessnaC172G1000Definition Def = new();

    private static IEnumerable<string> PanelKeys() => Def.GetPanelControls().Values.SelectMany(k => k).Distinct();

    [Fact]
    public void Every_input_event_the_definition_uses_is_in_the_measured_catalog()
    {
        var used = CessnaC172G1000Definition.SwitchInputEvents.Values
            .Concat(CessnaC172G1000Definition.AutopilotKeys.Values)
            .Concat(CessnaC172G1000Definition.CoverInputEvents.Values)
            .Concat(Def.GetVariables().Values.Where(v => v.Type == SimVarType.InputEvent).Select(v => v.Name))
            .Distinct();

        Assert.All(used, name => Assert.Contains(name, MeasuredCatalog));
    }

    [Fact]
    public void Every_mapped_control_is_a_variable_on_a_panel()
    {
        var onPanels = PanelKeys().ToHashSet();
        var mapped = CessnaC172G1000Definition.SwitchInputEvents.Keys
            .Concat(CessnaC172G1000Definition.AutopilotKeys.Keys)
            .Concat(CessnaC172G1000Definition.CoverInputEvents.Keys);

        Assert.All(mapped, key =>
        {
            Assert.True(Def.GetVariables().ContainsKey(key), key);
            Assert.Contains(key, onPanels);
        });
    }

    [Fact]
    public void No_panel_control_is_a_plain_event_button_that_bypasses_the_definition()
    {
        // MainForm's last branch sends an Event control's event itself, never asking the definition:
        // an Event that is not a button, has no combo positions and is not a typed _SET entry.
        var vars = Def.GetVariables();
        var bypassing = PanelKeys().Where(key =>
        {
            var d = vars[key];
            return d.Type == SimVarType.Event && !d.RenderAsButton && d.ValueDescriptions.Count <= 1
                   && !(key.Contains("_SET") && !d.PreventTextInput);
        });

        Assert.Empty(bypassing);
    }

    [Fact]
    public void Every_settable_panel_control_reaches_a_write_path()
    {
        var vars = Def.GetVariables();
        var unconnected = new SimConnectManager(IntPtr.Zero);   // never connected: every write is a no-op
        var speech = new GatedSpeechCapture();

        foreach (string key in PanelKeys())
        {
            var d = vars[key];
            bool settable = d.RenderAsButton || d.ValueDescriptions.Count > 1;
            if (!settable) continue;
            double value = d.ValueDescriptions.Count > 0 ? d.ValueDescriptions.Keys.First() : 1;
            Assert.True(ReachesWritePath(key, value, d, unconnected, speech), $"{key} has no write path");
        }
    }

    /// <summary>
    /// True when the definition handled the set. A base-class control that sends a stock event gets as
    /// far as SimConnectManager.SendEvent, which cannot load the SimConnect library this test host does
    /// not ship: reaching it IS the write path, so that failure counts as handled.
    /// </summary>
    private static bool ReachesWritePath(string key, double value, SimVarDefinition d,
        SimConnectManager simConnect, GatedSpeechCapture speech)
    {
        try { return Def.HandleUIVariableSet(key, value, d, simConnect, speech); }
        catch (FileNotFoundException ex) when (ex.FileName?.Contains("SimConnect", StringComparison.Ordinal) == true)
        {
            return true;
        }
    }

    [Fact]
    public void A_switch_written_before_the_catalog_arrives_is_refused_aloud()
    {
        var unconnected = new SimConnectManager(IntPtr.Zero);
        var speech = new GatedSpeechCapture();
        var battery = Def.GetVariables()["C172_BATTERY_STATE"];

        Assert.True(Def.HandleUIVariableSet("C172_BATTERY_STATE", 1, battery, unconnected, speech));
        Assert.Contains("Battery Master is not available yet. Try again in a few seconds.", speech.All);
    }

    [Fact]
    public void The_main_battery_is_index_2()
    {
        Assert.Equal("ELECTRICAL MASTER BATTERY:2", Def.GetVariables()["C172_BATTERY_STATE"].Name);
        Assert.Equal("ELECTRICAL_BATTERY_1", CessnaC172G1000Definition.SwitchInputEvents["C172_BATTERY_STATE"]);
    }

    [Theory]
    [InlineData("C172_FUEL_PUMP_STATE", "FUELSYSTEM PUMP SWITCH:1")]
    [InlineData("C172_FUEL_VALVE_STATE", "FUELSYSTEM VALVE SWITCH:1")]
    [InlineData("C172_FUEL_SELECTOR", "FUELSYSTEM JUNCTION SETTING:1")]
    [InlineData("C172_PITOT_HEAT_STATE", "PITOT HEAT SWITCH:1")]
    [InlineData("C172_NAV_LIGHT_STATE", "LIGHT NAV")]
    [InlineData("C172_BEACON_STATE", "LIGHT BEACON")]
    [InlineData("C172_STROBE_STATE", "LIGHT STROBE")]
    [InlineData("C172_LANDING_LIGHT_STATE", "LIGHT LANDING")]
    [InlineData("C172_TAXI_LIGHT_STATE", "LIGHT TAXI")]
    [InlineData("C172G_COVER_ENGINE", "COVER ON:1")]
    [InlineData("C172G_COVER_PITOT", "COVER ON:2")]
    [InlineData("C172G_COVER_STATIC", "COVER ON:3")]
    public void Each_switch_reads_what_it_drives(string key, string simVar)
    {
        Assert.Equal(simVar, Def.GetVariables()[key].Name);
    }

    [Theory]
    [InlineData("C172_AVIONICS1_STATE")]
    [InlineData("C172_AVIONICS2_STATE")]
    [InlineData("C172G_STBY_BATTERY_STATE")]
    [InlineData("C172_MAGNETO")]
    public void Switches_no_simvar_reports_are_read_from_their_input_event(string key)
    {
        var d = Def.GetVariables()[key];
        Assert.Equal(SimVarType.InputEvent, d.Type);
        Assert.True(SimConnectManager.IsInputEventRead(d));
    }

    [Fact]
    public void The_fuel_selector_positions_are_as_measured_by_tank_drain()
    {
        var positions = CessnaC172G1000Definition.FuelSelectorPositions();
        Assert.Equal("Left", positions[1]);
        Assert.Equal("Both", positions[2]);
        Assert.Equal("Right", positions[3]);
    }

    [Fact]
    public void The_standby_battery_is_arm_or_off()
    {
        var positions = Def.GetVariables()["C172G_STBY_BATTERY_STATE"].ValueDescriptions;
        Assert.Equal("Arm", positions[0]);
        Assert.Equal("Off", positions[1]);
    }

    // The nose wheel's read-back. PIT is the mode the GFC 700 engages in, and AUTOPILOT PITCH HOLD REF
    // is negative for nose up (measured -3.7 at 3.7 nose up). Tenths keep a dot under a comma-decimal
    // culture, as the trim announcer writes them, and a descent is a word, never a minus sign.
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void The_nose_wheel_targets_read_the_same_in_every_culture(string culture)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);

            Assert.Equal("Pitch up 3.7 degrees", CessnaC172G1000Definition.DescribePitchTarget(-3.7));
            Assert.Equal("Pitch down 1.5 degrees", CessnaC172G1000Definition.DescribePitchTarget(1.5));
            Assert.Equal("Pitch level", CessnaC172G1000Definition.DescribePitchTarget(0.04));
            Assert.Equal("Vertical speed 200 feet per minute up", CessnaC172G1000Definition.DescribeVerticalSpeedTarget(200));
            Assert.Equal("Vertical speed 500 feet per minute down", CessnaC172G1000Definition.DescribeVerticalSpeedTarget(-500));
            Assert.Equal("Vertical speed level", CessnaC172G1000Definition.DescribeVerticalSpeedTarget(0));
            Assert.Equal("Airspeed 85 knots", CessnaC172G1000Definition.DescribeAirspeedTarget(84.6));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    // The Starter is one turn of the magneto key to START (4), which springs back by itself: nothing
    // reachable holds it there (C172-6), so it is a button, never a hold.
    [Fact]
    public void The_starter_is_a_button_that_turns_the_key_to_start()
    {
        var d = Def.GetVariables()["C172_STARTER_SET"];
        Assert.Equal(SimVarType.InputEvent, d.Type);
        Assert.Equal("ENGINE_MAGNETO_1", d.Name);
        Assert.True(d.RenderAsButton);
        Assert.False(SimConnectManager.IsInputEventRead(d));
    }

    [Fact]
    public void The_backup_start_lands_after_the_bounce_and_before_the_spring_back()
    {
        // Measured: a single START sometimes bounced to BOTH 150-180 ms in; START springs back 3.0-3.6 s in.
        Assert.InRange(CessnaC172G1000Definition.StarterBackupMs, 181, 2999);
    }

    [Fact]
    public void A_start_before_the_catalog_arrives_is_refused_aloud()
    {
        var speech = new GatedSpeechCapture();

        Assert.True(Def.HandleUIVariableSet("C172_STARTER_SET", 1, Def.GetVariables()["C172_STARTER_SET"],
            new SimConnectManager(IntPtr.Zero), speech));
        Assert.Contains("Starter is not available yet. Try again in a few seconds.", speech.All);
    }

    [Fact]
    public void The_autopilot_is_the_gfc_700_and_the_kap140_panel_is_gone()
    {
        Assert.Equal(new[] { "GFC 700" }, Def.GetPanelStructure()["Autopilot"]);
        Assert.False(Def.GetPanelControls().ContainsKey("KAP140"));
        Assert.Contains("C172G_AP_FLC_STATE", Def.GetPanelControls()["GFC 700"]);
    }
}
