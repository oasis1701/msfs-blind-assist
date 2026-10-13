using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The Displays section: status boxes that read the A300's screens from its own variables, the
/// way the FlyByWire A320's PFD, ND and ISIS boxes do.
/// </summary>
public class IniA300DisplayPanelTests
{
    private readonly IniA300Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();

    public IniA300DisplayPanelTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
        };
        _def.Attach(_sim);
    }

    private void Var(string var, double value) =>
        _cache[A300FmaSources.All.Single(s => string.Equals(s.Var, var, StringComparison.OrdinalIgnoreCase)).Key] = value;

    private void Flying()
    {
        Var("INI_PITCH_MODE", 8);
        Var("INI_ROLL_MODE", 3);
        Var("INI_at_mode", 1);
        Var("INI_AT_ON", 1);
        Var("INI_autothrottle_master_switch1", 1);
        Var("INI_IRS1_ATTITUDE_ALIGNED", 1);
        Var("INI_pitch_trim2", 1);
        Var("INI_ap1_on", 1);
    }

    private string Shown(string key, double value = 0)
    {
        Assert.True(_def.TryGetDisplayOverride(key, value, out var text), $"{key} has no display text");
        return text;
    }

    [Fact]
    public void No_panel_name_is_in_two_sections()
    {
        // MainForm keys a panel's controls and status box by its name alone.
        var names = _def.GetPanelStructure().Values.SelectMany(p => p).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void A_system_panels_status_box_has_its_lights_then_its_ecam_values()
    {
        // Design decision 2026-10-09, as the FBW A320's panels carry their system's values.
        var electrical = _def.GetPanelDisplayVariables()["Electrical"];
        var ecam = A300DisplayPanels.Lines["ECAM Electrical AC"].Concat(A300DisplayPanels.Lines["ECAM Electrical DC"]).ToList();
        Assert.Equal(ecam, electrical.Skip(electrical.Count - ecam.Count));
        Assert.Contains(A300FaultLights.All.Single(l => l.Var == "INI_elec_gen1_fault").Key, electrical.Take(electrical.Count - ecam.Count));
        Assert.Contains("A300_LT_AC_BUS_1_OFF_LIGHT", electrical.Take(electrical.Count - ecam.Count));
        Assert.Equal(ecam, _def.GetPanelDisplayVariables()["ECAM Electrical AC"].Concat(_def.GetPanelDisplayVariables()["ECAM Electrical DC"]));
    }

    [Theory]
    [InlineData("APU", "ECAM APU", "A300_LT_APU_AVAIL_SEQ1_LIGHT")]
    [InlineData("Hydraulics", "ECAM Hydraulics", "A300_LAMP_HYD_BLUE_LIGHT")]
    [InlineData("Fuel", "ECAM Fuel", "A300_LT_OUT_TK_PMP_L1_SEQ1_LIGHT")]
    [InlineData("Air Conditioning", "ECAM Air Conditioning", "A300_LT_PACK_1_VALVE_IND_003_LIGHT")]
    [InlineData("Bleed", "ECAM Bleed", "A300_LAMP_ISOLATION_VALVE_LEFT_FAULT")]
    [InlineData("Pressurization", "ECAM Pressurization", "A300_LT_PRESS_SYS_1_SEQ1_LIGHT")]
    public void A_system_status_box_has_its_lights_then_its_ecam_page(string panel, string page, string light)
    {
        var box = _def.GetPanelDisplayVariables()[panel];
        var ecam = A300DisplayPanels.Lines[page];
        Assert.Equal(ecam, box.Skip(box.Count - ecam.Count));
        Assert.Contains(light, box.Take(box.Count - ecam.Count));
    }

    [Fact]
    public void The_engine_start_box_has_its_lights_then_the_engine_instruments_and_the_ecam_engine_page()
    {
        // The manual's start: fuel lever on at 20 percent N2, monitor the ECAM ENG page, the blue OPEN light out.
        var box = _def.GetPanelDisplayVariables()["Engine Start"];
        var expected = A300EcamPages.EngineInstruments.Concat(A300DisplayPanels.Lines["ECAM Engine"]).ToList();
        Assert.Equal(expected, box.Skip(box.Count - expected.Count));
        Assert.Contains("A300_LT_ENG_2_START_SEQ1_LIGHT", box.Take(box.Count - expected.Count));
    }

    [Fact]
    public void The_fuel_box_reads_total_fuel_once_in_the_tablets_unit()
    {
        // The ECAM page's total follows the tablet's weight unit; the pounds readout (the fuel hotkey's)
        // reads the same variable and is not repeated in the box.
        var fuel = _def.GetPanelDisplayVariables()["Fuel"];
        Assert.Contains("A300_SD_FUEL_TOTAL", fuel);
        Assert.DoesNotContain(A300Readouts.FuelTotalKey, fuel);
        Assert.True(_def.GetVariables().ContainsKey(A300Readouts.FuelTotalKey));
    }

    [Fact]
    public void The_displays_section_follows_the_instrument_panel()
    {
        var sections = _def.GetPanelStructure().Keys.ToList();
        Assert.Equal(sections.IndexOf("Instrument") + 1, sections.IndexOf("Displays"));
        Assert.Equal("PFD", _def.GetPanelStructure()["Displays"][0]);
    }

    [Fact]
    public void A_display_panel_has_no_controls()
    {
        foreach (var panel in _def.GetPanelStructure()["Displays"])
            Assert.Empty(_def.GetPanelControls()[panel]);
    }

    [Fact]
    public void Every_line_of_every_display_panel_is_a_registered_variable()
    {
        var vars = _def.GetVariables();
        foreach (var panel in _def.GetPanelStructure()["Displays"])
            foreach (var key in _def.GetPanelDisplayVariables()[panel])
                Assert.True(vars.ContainsKey(key), $"{panel}: {key} is not registered");
    }

    [Fact]
    public void The_pfd_box_reads_like_the_a320s()
    {
        Assert.Equal(new[]
        {
            A300FmaSources.ThrustModeKey, A300FmaSources.PitchModeKey, A300FmaSources.RollModeKey, A300FmaSources.ArmedKey,
            A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.AltitudeKey, A300Readouts.VerticalSpeedKey,
            "PLANE_PITCH_DEGREES", "PLANE_BANK_DEGREES", A300Readouts.PfdHeadingKey, A300Readouts.PfdAirspeedKey,
            "INDICATED_ALTITUDE", A300Readouts.PfdVerticalSpeedKey, A300Readouts.PfdRadioAltitudeKey,
            A300IlsDeviation.LocalizerKey, A300IlsDeviation.GlideslopeKey,
            A300TakeoffCallouts.V1Key, A300TakeoffCallouts.VrKey, A300TakeoffCallouts.V2Key,
            A300Readouts.VlsKey, A300Readouts.VmaxKey, A300Readouts.GreenDotKey, A300Readouts.SSpeedKey, A300Readouts.FSpeedKey,
            A300Readouts.VsSpeedKey, A300Readouts.MinimumsKey,
        }, _def.GetPanelDisplayVariables()["PFD"]);
    }

    [Fact]
    public void The_take_off_speeds_read_as_the_mcdu_and_the_fcu_set_them()
    {
        // V1 and VR from the MCDU's TAKEOFF page; 0 is a speed the FMS does not have.
        Assert.Equal("148 knots", Shown(A300TakeoffCallouts.V1Key, 148));
        Assert.Equal("not set", Shown(A300TakeoffCallouts.VrKey, 0));
        // V2 is the FCU speed window ("V2 [FCU]" on the TAKEOFF page), shown only when it is plausibly V2,
        // as the V2 call-out judges it ([A300-11]).
        _cache[A300TakeoffCallouts.VrKey] = 151;
        Assert.Equal("155 knots", Shown(A300TakeoffCallouts.V2Key, 155));
        Assert.Equal(A300TakeoffCallouts.V2NotSet, Shown(A300TakeoffCallouts.V2Key, 250));   // the window left at 250
        _cache.Remove(A300TakeoffCallouts.VrKey);
        Assert.Equal(A300TakeoffCallouts.V2NotSet, Shown(A300TakeoffCallouts.V2Key, 155));   // no VR to judge it by
    }

    [Fact]
    public void The_fma_lines_read_each_column()
    {
        Flying();
        Var("INI_PITCH_MODE_ARM", 6);
        Var("INI_ROLL_MODE_ARM", 1);
        Assert.Equal("Speed", Shown(A300FmaSources.ThrustModeKey));
        Assert.Equal("Vertical speed", Shown(A300FmaSources.PitchModeKey));
        Assert.Equal("Heading hold", Shown(A300FmaSources.RollModeKey));
        Assert.Equal("Altitude, NAV\nAutopilot: CMD 1", Shown(A300FmaSources.ArmedKey));
    }

    [Fact]
    public void A_combined_mode_reads_on_both_pitch_and_roll()
    {
        Flying();
        Var("INI_PITCH_ROLL_MODE", 1);
        Assert.Equal("Land", Shown(A300FmaSources.PitchModeKey));
        Assert.Equal("Land", Shown(A300FmaSources.RollModeKey));
    }

    [Fact]
    public void Blank_columns_read_as_blank_and_nothing_armed_reads_none()
    {
        Flying();
        Var("INI_PITCH_MODE", 0);
        Var("INI_ap1_on", 0);
        Assert.Equal("blank", Shown(A300FmaSources.PitchModeKey));
        Assert.Equal("none\nAutopilot: off", Shown(A300FmaSources.ArmedKey));
    }

    [Fact]
    public void An_unpowered_fma_reads_not_shown()
    {
        Assert.Equal("not shown", Shown(A300FmaSources.ThrustModeKey));
        Assert.Equal("not shown", Shown(A300FmaSources.PitchModeKey));
        Assert.Equal("not shown\nAutopilot: off", Shown(A300FmaSources.ArmedKey));
    }

    [Fact]
    public void Flap_speeds_follow_the_flap_lever()
    {
        _cache[A300Levers.FlapsKey] = 0;
        Assert.Equal("187 knots", Shown(A300Readouts.GreenDotKey, 187.4));
        Assert.Equal(A300DisplayText.NotShown, Shown(A300Readouts.FSpeedKey, 137.2));
        _cache[A300Levers.FlapsKey] = 2;
        Assert.Equal("137 knots", Shown(A300Readouts.FSpeedKey, 137.2));
    }

    [Fact]
    public void Attitude_and_altitude_read_in_words()
    {
        Assert.Equal("5.0 degrees up", Shown("PLANE_PITCH_DEGREES", -5 * Math.PI / 180));
        Assert.Equal("Wings level", Shown("PLANE_BANK_DEGREES", 0));
        Assert.Equal("12,000 feet", Shown("INDICATED_ALTITUDE", 12000));
        Assert.Equal("270", Shown(A300Readouts.PfdHeadingKey, 270));
        Assert.Equal("1,520 feet per minute up", Shown(A300Readouts.PfdVerticalSpeedKey, 1520));
        Assert.Equal("180 knots", Shown(A300Readouts.VlsKey, 179.8));
        Assert.Equal("not set", Shown(A300Readouts.MinimumsKey, 0));
    }

    [Fact]
    public void The_nd_box_follows_the_pfd_and_reads_like_the_a320s_without_the_waypoint_name()
    {
        Assert.Equal(new[] { "PFD", "ND" }, _def.GetPanelStructure()["Displays"].Take(2));
        Assert.Equal(new[]
        {
            "A300_EFIS_MODE_CPT", "A300_EFIS_RANGE_CPT", A300Readouts.WaypointDistanceKey,
            "GROUND_VELOCITY", A300Readouts.TrueAirspeedKey, A300Readouts.WindDirectionKey, A300Readouts.WindSpeedKey,
            A300Readouts.Vor1FrequencyKey, A300Readouts.Dme1Key, A300Readouts.Vor2FrequencyKey, A300Readouts.Dme2Key,
            A300Readouts.IlsFrequencyKey, A300Readouts.LocalizerKey, A300Readouts.GlideslopeKey,
            A300Readouts.Adf1FrequencyKey, A300Readouts.Adf2FrequencyKey,
        }, _def.GetPanelDisplayVariables()["ND"]);
    }

    [Fact]
    public void The_nd_lines_read_in_words()
    {
        Assert.Equal("12.3 nautical miles", Shown(A300Readouts.WaypointDistanceKey, 12.34));
        Assert.Equal("250 knots", Shown("GROUND_VELOCITY", 250.2));
        Assert.Equal("089 true", Shown(A300Readouts.WindDirectionKey, 88.6));
        Assert.Equal("116.55 megahertz", Shown(A300Readouts.Vor1FrequencyKey, 116.55));
        Assert.Equal("no DME", Shown(A300Readouts.Dme2Key, 0));
        Assert.Equal("received", Shown(A300Readouts.LocalizerKey, 1));
        Assert.Equal("890 kilohertz", Shown(A300Readouts.Adf1FrequencyKey, 890));
    }

    [Fact]
    public void The_ils_receiver_is_nav_3()
    {
        var vars = _def.GetVariables();
        Assert.Equal("NAV ACTIVE FREQUENCY:3", vars[A300Readouts.IlsFrequencyKey].Name);
        Assert.Equal("NAV HAS LOCALIZER:3", vars[A300Readouts.LocalizerKey].Name);
        Assert.Equal("NAV HAS GLIDE SLOPE:3", vars[A300Readouts.GlideslopeKey].Name);
    }

    [Fact]
    public void The_standby_instruments_panel_has_its_controls_and_reads_the_standby_instruments()
    {
        // One panel, in the Instrument section, like the FBW Airbuses' ISIS and the Fenix's Standby Instruments.
        Assert.Contains("Standby Instruments", _def.GetPanelStructure()["Instrument"]);
        Assert.Contains("A300_ADI_CAGE", _def.GetPanelControls()["Standby Instruments"]);
        Assert.Equal(new[]
        {
            "PLANE_PITCH_DEGREES", "PLANE_BANK_DEGREES", A300Readouts.PfdAirspeedKey,
            A300Readouts.StandbyAltitudeKey, "A300_RO_BARO_STBY", A300Readouts.StandbyCompassKey,
        }, _def.GetPanelDisplayVariables()["Standby Instruments"]);
        // The standby altimeter has its own baro setting, so its altitude is INDICATED ALTITUDE:3
        // (measured 2026-10-03: baro 3 at 1020 moved it while altimeter 1 stayed put).
        Assert.Equal("INDICATED ALTITUDE:3", _def.GetVariables()[A300Readouts.StandbyAltitudeKey].Name);
        Assert.Equal("5,120 feet", Shown(A300Readouts.StandbyAltitudeKey, 5120));
        Assert.Equal("115", Shown(A300Readouts.StandbyCompassKey, 115.3));
    }
}
