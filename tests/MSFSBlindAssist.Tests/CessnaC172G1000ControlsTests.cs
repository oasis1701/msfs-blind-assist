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
        "ELECTRICAL_BREAKER_BTN_BREAKER_ALT_FLD", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN1",
        "ELECTRICAL_BREAKER_BTN_BREAKER_AVN1_ADC", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN1_FIS",
        "ELECTRICAL_BREAKER_BTN_BREAKER_AVN1_NAV1", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN1_PFD",
        "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2_AUDIO",
        "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2_AUTOPILOT", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2_COMM2",
        "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2_MDF", "ELECTRICAL_BREAKER_BTN_BREAKER_AVN2_NAV2",
        "ELECTRICAL_BREAKER_BTN_BREAKER_BCN_LT", "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_ADC",
        "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_COMM1", "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_NAV1",
        "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_PFD", "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_STBY_BATT",
        "ELECTRICAL_BREAKER_BTN_BREAKER_ESS_STBY_IND", "ELECTRICAL_BREAKER_BTN_BREAKER_FLAP",
        "ELECTRICAL_BREAKER_BTN_BREAKER_FUELPUMP", "ELECTRICAL_BREAKER_BTN_BREAKER_LAND_LT",
        "ELECTRICAL_BREAKER_BTN_BREAKER_LTS_PWR", "ELECTRICAL_BREAKER_BTN_BREAKER_NAV_LTS",
        "ELECTRICAL_BREAKER_BTN_BREAKER_PANELS_LTS", "ELECTRICAL_BREAKER_BTN_BREAKER_PITOT_HEAT",
        "ELECTRICAL_BREAKER_BTN_BREAKER_RECOG", "ELECTRICAL_BREAKER_BTN_BREAKER_STROBE_LTS",
        "ELECTRICAL_BREAKER_BTN_BREAKER_WARN", "ELECTRICAL_BREAKER_BTN_BREAKER_XPNDR",
        "PNEUMATICS_IE_PNEUMATICS_LEVER_VALVE_HOTAIRVALVE", "PNEUMATICS_IE_PNEUMATICS_LEVER_VALVE_COCKPITVALVE",
        "SAFETY_ELT_1", "AIRCRAFT_DOOR_WINDOW_PILOT", "AIRCRAFT_DOOR_WINDOW",
    };

    private static readonly CessnaC172G1000Definition Def = new();

    private static IEnumerable<string> PanelKeys() => Def.GetPanelControls().Values.SelectMany(k => k).Distinct();

    [Fact]
    public void Every_input_event_the_definition_uses_is_in_the_measured_catalog()
    {
        var used = CessnaC172G1000Definition.SwitchInputEvents.Values
            .Concat(CessnaC172G1000Definition.AutopilotKeys.Values)
            .Concat(CessnaC172G1000Definition.CoverInputEvents.Values)
            .Concat(CessnaC172G1000Definition.BreakerInputEvents.Values)
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
            .Concat(CessnaC172G1000Definition.CoverInputEvents.Keys)
            .Concat(CessnaC172G1000Definition.BreakerInputEvents.Keys);

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

    // The 30 circuit breakers of the measured catalog: each a direct set, 1 Pulled and 0 In, read from
    // its own input event (C172-7), on four panels laid out by bus.
    [Fact]
    public void Every_breaker_in_the_catalog_is_an_in_or_pulled_combo_read_from_the_breaker()
    {
        var catalogBreakers = MeasuredCatalog.Where(n => n.StartsWith("ELECTRICAL_BREAKER_", StringComparison.Ordinal));
        Assert.Equal(catalogBreakers.Order(), CessnaC172G1000Definition.BreakerInputEvents.Values.Order());
        Assert.Equal(30, CessnaC172G1000Definition.BreakerInputEvents.Count);

        foreach (var (key, inputEvent) in CessnaC172G1000Definition.BreakerInputEvents)
        {
            var d = Def.GetVariables()[key];
            Assert.Equal(inputEvent, d.Name);
            Assert.True(SimConnectManager.IsInputEventRead(d), key);
            Assert.False(d.RenderAsButton, key);
            Assert.Equal("In", d.ValueDescriptions[0]);
            Assert.Equal("Pulled", d.ValueDescriptions[1]);
        }
    }

    [Fact]
    public void The_breaker_panels_list_every_breaker_once_by_bus()
    {
        var panels = Def.GetPanelStructure()["Circuit Breakers"];
        Assert.Equal(new[] { "Essential Bus Breakers", "Avionics Bus 1 Breakers", "Avionics Bus 2 Breakers",
            "Systems and Lights Breakers" }, panels);

        var listed = panels.SelectMany(p => Def.GetPanelControls()[p]).ToList();
        Assert.Equal(CessnaC172G1000Definition.BreakerInputEvents.Keys.Order(), listed.Order());
        // Measured: the transponder's circuit is on avionics bus 2, and RECOG cuts the taxi light.
        Assert.Contains("C172G_CB_XPNDR", Def.GetPanelControls()["Avionics Bus 2 Breakers"]);
        Assert.Equal("Taxi Light", Def.GetVariables()["C172G_CB_RECOG"].DisplayName);
    }

    [Fact]
    public void Breaker_names_are_unique_within_each_panel()
    {
        foreach (var panel in Def.GetPanelStructure()["Circuit Breakers"])
        {
            var names = Def.GetPanelControls()[panel].Select(k => Def.GetVariables()[k].DisplayName).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    // A breaker that pops by itself is spoken; the first reading after a load is where it stands.
    [Theory]
    [InlineData("C172G_CB_FUELPUMP", true, "Fuel Pump breaker popped")]
    [InlineData("C172G_CB_FUELPUMP", false, "Fuel Pump breaker in")]
    [InlineData("C172G_CB_ESS_PFD", true, "Essential Bus PFD breaker popped")]
    [InlineData("C172G_CB_AVN1_PFD", true, "Avionics Bus 1 PFD breaker popped")]
    [InlineData("C172G_CB_AVN1_NAV1", false, "Avionics Bus 1 Nav 1 breaker in")]
    [InlineData("C172G_CB_RECOG", true, "Taxi Light breaker popped")]
    public void A_breaker_change_is_spoken_by_name_and_bus_where_names_repeat(string key, bool pulled, string spoken)
    {
        Assert.Equal(spoken, CessnaC172G1000Definition.DescribeBreaker(key, pulled));
    }

    [Fact]
    public void Only_a_change_after_the_first_reading_is_spoken_and_the_combo_still_refreshes()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();

        Assert.False(def.ProcessSimVarUpdate("C172G_CB_XPNDR", 0, speech));   // false: MainForm refreshes the combo
        Assert.False(def.ProcessSimVarUpdate("C172G_CB_XPNDR", 0, speech));
        Assert.Empty(speech.All);

        def.ProcessSimVarUpdate("C172G_CB_XPNDR", 1, speech);
        def.ProcessSimVarUpdate("C172G_CB_XPNDR", 1, speech);
        def.ProcessSimVarUpdate("C172G_CB_XPNDR", 0, speech);
        Assert.Equal(new[] { "Transponder breaker popped", "Transponder breaker in" }, speech.All);

        def.OnSimContextReset();
        def.ProcessSimVarUpdate("C172G_CB_XPNDR", 1, speech);   // a new flight's first reading
        Assert.Equal(2, speech.All.Count);
    }

    [Fact]
    public void The_pilots_own_pull_is_silent_and_a_later_pop_still_speaks()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();
        def.ProcessSimVarUpdate("C172G_CB_FLAP", 0, speech);

        speech.Suppressed = true;   // as MainForm sets it while the pilot's combo set echoes back (CORE-8)
        def.ProcessSimVarUpdate("C172G_CB_FLAP", 1, speech);
        speech.Suppressed = false;
        def.ProcessSimVarUpdate("C172G_CB_FLAP", 0, speech);

        Assert.Equal(new[] { "Flaps breaker in" }, speech.All);
    }

    // C172-9: the cabin levers are written and delivered by SimConnect in percent (live: 50 sent,
    // 50 delivered). Keyed on the 0-1 fraction the Coherent B: read shows, every delivery read as
    // past fully open and the combo snapped back to Open after each pick.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]       // what SimConnect delivered after a Half open pick
    [InlineData(75, 75)]
    [InlineData(30, 25)]       // a lever left between steps reads its nearest step
    [InlineData(90, 100)]
    [InlineData(120, 100)]
    public void A_cabin_lever_reads_in_percent_to_the_nearest_quarter(double percent, double position)
    {
        Assert.Equal(position, CessnaC172G1000Definition.LeverPosition(percent));
        Assert.Equal(position, Def.GetVariables()["C172G_CABIN_HEAT"].DescriptionKeyFor(percent));
    }

    [Fact]
    public void A_cabin_lever_step_is_written_as_the_percent_it_reads_back()
    {
        var heat = Def.GetVariables()["C172G_CABIN_HEAT"];
        Assert.Equal(new double[] { 0, 25, 50, 75, 100 }, heat.ValueDescriptions.Keys.Order());
        Assert.All(heat.ValueDescriptions.Keys, step => Assert.Equal(step, heat.DescriptionKeyFor(step)));
        Assert.Equal("PNEUMATICS_IE_PNEUMATICS_LEVER_VALVE_HOTAIRVALVE",
            CessnaC172G1000Definition.SwitchInputEvents["C172G_CABIN_HEAT"]);
    }

    [Fact]
    public void The_windows_read_where_they_are_and_the_elt_offers_on_and_arm()
    {
        var vars = Def.GetVariables();
        Assert.Equal("INTERACTIVE POINT OPEN:4", vars["C172G_PILOT_WINDOW"].Name);
        Assert.Equal(SimVarType.SimVar, vars["C172G_PILOT_WINDOW"].Type);
        Assert.Equal(1, vars["C172G_PASSENGER_WINDOW"].DescriptionKeyFor(0.98));
        Assert.Equal(0, vars["C172G_PASSENGER_WINDOW"].DescriptionKeyFor(0.04));
        Assert.Equal(new[] { "On", "Arm" }, vars["C172G_ELT"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
    }

    // C172-8: writing the cabin door or its handle put the pilot outside the aircraft.
    [Fact]
    public void The_cabin_door_and_its_handle_are_never_written()
    {
        var written = CessnaC172G1000Definition.SwitchInputEvents.Values
            .Concat(CessnaC172G1000Definition.CoverInputEvents.Values)
            .Concat(CessnaC172G1000Definition.BreakerInputEvents.Values)
            .Concat(Def.GetVariables().Values.Select(v => v.Name));

        Assert.DoesNotContain("INSTRUMENT_DOOR_LEFT", written);
        Assert.DoesNotContain("UNKNOWN_DOOR_HANDLE_1", written);
    }

    [Fact]
    public void The_autopilot_is_the_gfc_700_and_the_kap140_panel_is_gone()
    {
        Assert.Equal(new[] { "GFC 700" }, Def.GetPanelStructure()["Autopilot"]);
        Assert.False(Def.GetPanelControls().ContainsKey("KAP140"));
        Assert.Contains("C172G_AP_FLC_STATE", Def.GetPanelControls()["GFC 700"]);
    }
}
