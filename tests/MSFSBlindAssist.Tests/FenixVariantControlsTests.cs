// Pins the Fenix v3 variant controls: the controls only some Fenix airframes have are registered and
// placed once, and the panel placement keeps every touched panel's labels distinct.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixVariantControlsTests
{
    private static readonly FenixA320Definition Fenix = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Fenix.GetVariables();
    private static readonly Dictionary<string, List<string>> Panels = Fenix.GetPanelControls();

    private const string AirPanel = "Air Conditioning and Pressurization";

    private static readonly string[] Lights =
    {
        "I_OH_PNEUMATIC_ECON_FLOW_L", "I_OH_FUEL_ACT_U", "I_OH_FUEL_ACT_L",
        "I_OH_ENG_N1_MODE_1_L", "I_OH_ENG_N1_MODE_2_L",
    };

    private static readonly string[] Switches =
    {
        "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_FUEL_ACT", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT",
    };

    [Fact]
    public void Switches_are_on_request_and_lights_are_announced()
    {
        foreach (var key in new[] { "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_FUEL_ACT" })
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, v.ValueDescriptions);
            Assert.False(v.RenderAsButton);
            Assert.Equal(key, v.Name);
        }

        foreach (var (key, label) in new[] { ("S_OH_CALLS_MID", "Calls Mid"), ("S_OH_CALLS_EXIT", "Calls Exit") })
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.True(v.RenderAsButton);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(key, v.Name);
        }

        foreach (var key in Lights)
        {
            var v = Vars[key];
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced);
            Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, v.ValueDescriptions);
            Assert.Equal(key, v.Name);
        }
    }

    [Fact]
    public void Each_light_is_labelled_from_its_switch()
    {
        Assert.Equal(FenixA320Definition.EconFlowName + " Lower", Vars["I_OH_PNEUMATIC_ECON_FLOW_L"].DisplayName);
        Assert.Equal(FenixA320Definition.ActPumpName + " Upper", Vars["I_OH_FUEL_ACT_U"].DisplayName);
        Assert.Equal(FenixA320Definition.ActPumpName + " Lower", Vars["I_OH_FUEL_ACT_L"].DisplayName);
        Assert.Equal(Vars["S_OH_ENG_N1_MODE_1"].DisplayName + " Lower", Vars["I_OH_ENG_N1_MODE_1_L"].DisplayName);
        Assert.Equal(Vars["S_OH_ENG_N1_MODE_2"].DisplayName + " Lower", Vars["I_OH_ENG_N1_MODE_2_L"].DisplayName);
    }

    [Fact]
    public void Each_switch_sits_once_where_the_plan_puts_it()
    {
        var air = Panels[AirPanel];
        Assert.Equal(air.IndexOf("S_OH_PNEUMATIC_PACK_FLOW") + 1, air.IndexOf("S_OH_PNEUMATIC_ECON_FLOW"));

        var calls = Panels["Calls"].Where(k => k is "S_OH_CALLS_FWD" or "S_OH_CALLS_MID" or "S_OH_CALLS_EXIT" or "S_OH_CALLS_AFT");
        Assert.Equal(new[] { "S_OH_CALLS_FWD", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT", "S_OH_CALLS_AFT" }, calls);

        var fuel = Panels["Fuel"];
        Assert.Equal(fuel.IndexOf("S_OH_FUEL_CENTER_2") + 1, fuel.IndexOf("S_OH_FUEL_ACT"));

        var all = Panels.Values.SelectMany(p => p).ToList();
        foreach (var key in Switches)
            Assert.Single(all, k => k == key);
        foreach (var key in Lights)
            Assert.DoesNotContain(key, all);
    }

    [Fact]
    public void New_cockpit_controls_are_registered()
    {
        void Check(string key, string label, bool button)
        {
            var v = Vars[key];
            Assert.Equal(key, v.Name);
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(button, v.RenderAsButton);
        }

        Check("A320_Sound:Headset_On", "Headset", false);
        Assert.Equal(new Dictionary<double, string> { [0] = "Off", [1] = "On" }, Vars["A320_Sound:Headset_On"].ValueDescriptions);
        Check("S_MIP_CLOCK_CHR", "Chronometer Start/Stop", true);
        Check("S_MIP_CLOCK_RST", "Chronometer Reset", true);
        Check("S_MIP_CLOCK_SET", "Date", true);
        Check("S_MIP_ISFD_BARO_BUTTON", "ISIS Baro Push", true);
        Check("S_MIP_CLOCK_ET", "Elapsed Time", false);
        Assert.Equal(new Dictionary<double, string> { [0] = "Run", [1] = "Stop", [2] = "Reset" }, Vars["S_MIP_CLOCK_ET"].ValueDescriptions);
        Check("S_MIP_CLOCK_UTC", "UTC Selector", false);
        Assert.Equal(new Dictionary<double, string> { [0] = "GPS", [1] = "Internal", [2] = "Set" }, Vars["S_MIP_CLOCK_UTC"].ValueDescriptions);
        Check("A_OH_PNEUMATIC_CARGO_AFT_TEMP", "Aft Cargo Temperature", false);
        Assert.Equal(Vars["A_OH_PNEUMATIC_AFT_TEMP"].ValueDescriptions, Vars["A_OH_PNEUMATIC_CARGO_AFT_TEMP"].ValueDescriptions);
    }

    [Fact]
    public void New_controls_sit_where_the_aircraft_has_them()
    {
        var mip = Fenix.GetPanelStructure()["Main Instrument Panel"];
        Assert.Equal(mip.IndexOf("Landing Gear") + 1, mip.IndexOf("Clock"));
        Assert.Equal(new[] { "S_MIP_CLOCK_CHR", "S_MIP_CLOCK_RST", "S_MIP_CLOCK_ET", "S_MIP_CLOCK_UTC", "S_MIP_CLOCK_SET" },
            Panels["Clock"]);
        Assert.Equal(new[] { "N_MIP_CLOCK_CHRONO", "N_MIP_CLOCK_ELAPSED" }, Fenix.GetPanelDisplayVariables()["Clock"]);

        var isis = Panels["ISIS"];
        Assert.Equal(isis.IndexOf("E_MIP_ISFD_BARO_DEC") + 1, isis.IndexOf("S_MIP_ISFD_BARO_BUTTON"));
        Assert.Equal("A320_Sound:Headset_On", Panels["Cockpit Other"][0]);
        var air = Panels[AirPanel];
        int i = air.IndexOf("S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO");
        Assert.Equal(new[] { "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", "A_OH_PNEUMATIC_CARGO_AFT_TEMP", "S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE" },
            air.Skip(i).Take(3));

        var all = Panels.Values.SelectMany(p => p).ToList();
        foreach (var key in new[] { "A320_Sound:Headset_On", "S_MIP_CLOCK_CHR", "S_MIP_CLOCK_RST", "S_MIP_CLOCK_SET",
                     "S_MIP_CLOCK_ET", "S_MIP_CLOCK_UTC", "S_MIP_ISFD_BARO_BUTTON", "A_OH_PNEUMATIC_CARGO_AFT_TEMP" })
            Assert.Single(all, k => k == key);
        var structure = Fenix.GetPanelStructure().Values.SelectMany(p => p).ToHashSet();
        foreach (var panel in Panels.Keys)
            Assert.Contains(panel, structure);
    }

    [Fact]
    public void Clock_display_texts_use_the_fbw_wording()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            Assert.Equal("Reset", FenixA320Definition.ChronometerText(-1));
            Assert.Equal("0 minutes 0 seconds", FenixA320Definition.ChronometerText(0));
            Assert.Equal("2 minutes 5 seconds", FenixA320Definition.ChronometerText(125));
            Assert.Equal("Reset", FenixA320Definition.ElapsedTimeText(-1));
            Assert.Equal("0 hours 2 minutes", FenixA320Definition.ElapsedTimeText(2));
            Assert.Equal("2 hours 15 minutes", FenixA320Definition.ElapsedTimeText(135));

            var fresh = new FenixA320Definition();
            Assert.True(fresh.TryGetDisplayOverride("N_MIP_CLOCK_CHRONO", 125, out var t));
            Assert.Equal("2 minutes 5 seconds", t);
            Assert.True(fresh.TryGetDisplayOverride("N_MIP_CLOCK_ELAPSED", 135, out var e));
            Assert.Equal("2 hours 15 minutes", e);
            Assert.False(fresh.TryGetDisplayOverride("S_OH_ELEC_BAT1", 1, out _));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Cargo_heat_controls_follow_the_livery()
    {
        var a321 = V(4, 1, 2, true, false, false, false);
        AssertNotFitted(a321 with { HasCargoHeat = false }, "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", "A_OH_PNEUMATIC_CARGO_AFT_TEMP");
        AssertFitted(a321 with { HasCargoHeat = true }, "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", "A_OH_PNEUMATIC_CARGO_AFT_TEMP");
        AssertFitted(a321 with { HasCargoHeat = null }, "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", "A_OH_PNEUMATIC_CARGO_AFT_TEMP");
        foreach (bool? h in new bool?[] { false, true, null })
            AssertFitted(a321 with { HasCargoHeat = h }, "S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE");
    }

    [Theory]
    [InlineData("A320_Sound:Headset_On", 1)]
    [InlineData("S_MIP_CLOCK_UTC", 1)]
    [InlineData("S_MIP_CLOCK_ET", 0)]
    [InlineData("S_MIP_CLOCK_ET", 1)]
    [InlineData("A_OH_PNEUMATIC_CARGO_AFT_TEMP", 0.3)]
    public void New_switches_are_written_plainly(string key, double value)
    {
        var writes = new List<(string, double)>();
        Assert.True(FenixA320Definition.TryWritePlain(key, value, (k, v) => writes.Add((k, v))));
        Assert.Equal(new[] { (key, value) }, writes);
    }

    [Fact]
    public void Elapsed_time_reset_and_buttons_are_not_plain_writes()
    {
        var writes = new List<(string, double)>();
        Assert.False(FenixA320Definition.TryWritePlain("S_MIP_CLOCK_ET", 2, (k, v) => writes.Add((k, v))));
        foreach (var b in new[] { "S_MIP_CLOCK_CHR", "S_MIP_CLOCK_RST", "S_MIP_CLOCK_SET", "S_MIP_ISFD_BARO_BUTTON" })
            Assert.False(FenixA320Definition.TryWritePlain(b, 1, (k, v) => writes.Add((k, v))));
        Assert.Empty(writes);
    }

    [Theory]
    [InlineData(AirPanel)]
    [InlineData("Fuel")]
    [InlineData("Calls")]
    [InlineData("Clock")]
    [InlineData("ISIS")]
    [InlineData("Cockpit Other")]
    public void Touched_panels_repeat_no_label(string panel)
    {
        var names = Panels[panel].Select(k => Vars[k].DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // The measured raw values appear in the tests.
    private static FenixVariant V(int type, int engine, int? act, bool? isis, bool? dcdu, bool? neo, bool? gnadirs)
        => new((FenixAircraftType)type, (FenixEngineFamily)engine, act, isis, dcdu, neo, gnadirs);

    private static void AssertFitted(FenixVariant v, params string[] keys)
    {
        foreach (var k in keys) Assert.True(FenixA320Definition.IsFitted(k, v), $"{k} should be fitted");
    }

    private static void AssertNotFitted(FenixVariant v, params string[] keys)
    {
        foreach (var k in keys) Assert.False(FenixA320Definition.IsFitted(k, v), $"{k} should not be fitted");
    }

    private static readonly string[] Keypad =
        Enumerable.Range(0, 10).Select(i => $"S_OH_ADIRS_KEY_{i}")
            .Concat(new[] { "S_OH_ADIRS_KEY_CLR", "S_OH_ADIRS_KEY_ENT" }).ToArray();

    [Fact]
    public void A321_iae_with_act_as_read_on_2026_10_21()
    {
        var v = V(4, 1, 2, true, false, false, false);
        AssertFitted(v, "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT", "S_OH_FUEL_ACT",
            "S_OH_ENG_N1_MODE_1", "S_OH_ENG_N1_MODE_2", "S_OH_NAV_IR1_SWITCH", "S_MIP_ISFD_BUGS", "S_MIP_ISFD_BARO_BUTTON", "S_OH_ELEC_BAT1");
        AssertNotFitted(v, "S_OH_PNEUMATIC_PACK_FLOW", "S_OH_ADIRS_KEY_0", "S_OH_NAV_DATA_DISP",
            "S_DCDU1_LSK1L", "S_DCDU2_DIM_BRT", "S_STANDBY_ATTITUDE_CAGE");
    }

    [Fact]
    public void A320_cfm_as_read_on_2026_10_21()
    {
        // standby_type read Analogue; ACT 0 because the A320 interiors attach only Fuel_ACT_Disabled.
        var v = V(3, 0, 0, false, false, false, false);
        AssertFitted(v, "S_OH_PNEUMATIC_PACK_FLOW", "S_STANDBY_ATTITUDE_CAGE", "S_OH_NAV_IR2_SWITCH");
        AssertNotFitted(v, "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_CALLS_MID", "S_OH_CALLS_EXIT", "S_OH_FUEL_ACT",
            "S_OH_ENG_N1_MODE_1", "S_OH_ENG_N1_MODE_2", "S_MIP_ISFD_RST", "E_MIP_ISFD_BARO_INC", "S_MIP_ISFD_BARO_BUTTON", "S_OH_ADIRS_KEY_ENT");
    }

    [Fact]
    public void Gnadirs_and_dcdu_fitted()
    {
        var v = V(3, 0, 0, true, true, false, true);
        AssertFitted(v, Keypad);
        AssertFitted(v, "S_OH_NAV_DATA_DISP", "S_OH_NAV_SYS_DISP", "S_DCDU1_DIM_BRT", "S_DCDU1_LSK1L",
            "S_DCDU1_LSK1R", "S_DCDU1_LSK2L", "S_DCDU1_LSK2R", "S_DCDU1_MSGUP", "S_DCDU1_MSGDWN",
            "S_DCDU1_PGUP", "S_DCDU1_PGDN", "S_DCDU1_PRINT", "S_DCDU2_DIM_BRT");
        AssertNotFitted(v, "S_OH_NAV_IR1_SWITCH", "S_OH_NAV_IR2_SWITCH", "S_OH_NAV_IR3_SWITCH");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void A319_and_a318_are_not_a321(int type)
    {
        var v = V(type, 0, 0, false, false, false, false);
        AssertNotFitted(v, "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_CALLS_MID");
        AssertFitted(v, "S_OH_PNEUMATIC_PACK_FLOW");
    }

    [Fact]
    public void Unknown_variant_fits_every_panel_control()
    {
        var fresh = new FenixA320Definition();
        foreach (var key in Panels.Values.SelectMany(p => p).Distinct())
        {
            Assert.True(FenixA320Definition.IsFitted(key, FenixVariant.Unknown), key);
            Assert.True(fresh.IsPanelControlVisible(key, null), key);
        }
    }

    [Fact]
    public void Each_control_reads_only_its_own_field()
    {
        var typeOnly = V(4, -1, null, null, null, null, null);
        AssertFitted(typeOnly, "S_OH_FUEL_ACT", "S_OH_ENG_N1_MODE_1", "S_OH_ENG_N1_MODE_2", "S_OH_ADIRS_KEY_0",
            "S_OH_NAV_IR1_SWITCH", "S_DCDU1_PRINT", "S_MIP_ISFD_LS", "S_STANDBY_ATTITUDE_CAGE");
        AssertNotFitted(typeOnly, "S_OH_PNEUMATIC_PACK_FLOW");

        var actOnly = V(0, 0, 2, null, null, null, null);
        AssertFitted(actOnly, "S_OH_PNEUMATIC_ECON_FLOW", "S_OH_PNEUMATIC_PACK_FLOW");
        AssertNotFitted(actOnly, "S_OH_ENG_N1_MODE_1", "S_OH_ENG_N1_MODE_2");
    }

    [Fact]
    public void Not_fitted_control_is_refused_aloud()
    {
        var a321 = V(4, 1, 2, true, false, false, false);
        var a320 = V(3, 0, 0, false, false, false, false);

        var speech = new SpeechCapture();
        Assert.True(FenixA320Definition.TryRefuseNotFitted("S_OH_PNEUMATIC_PACK_FLOW", a321, speech));
        Assert.Equal(new[] { "Not fitted on this aircraft." }, speech.Interrupts);

        var speech2 = new SpeechCapture();
        Assert.True(FenixA320Definition.TryRefuseNotFitted("S_OH_CALLS_MID", a320, speech2));
        Assert.Equal(new[] { FenixA320Definition.NotFittedMessage }, speech2.Interrupts);

        var speech3 = new SpeechCapture();
        Assert.False(FenixA320Definition.TryRefuseNotFitted("S_OH_PNEUMATIC_ECON_FLOW", a321, speech3));
        Assert.Empty(speech3.All);
    }

    [Fact]
    public void Unknown_variant_refuses_nothing()
    {
        var speech = new SpeechCapture();
        foreach (var key in Panels.Values.SelectMany(p => p).Distinct())
            Assert.False(FenixA320Definition.TryRefuseNotFitted(key, FenixVariant.Unknown, speech), key);
        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData("S_OH_PNEUMATIC_ECON_FLOW", 1)]
    [InlineData("S_OH_FUEL_ACT", 0)]
    public void New_latched_switches_are_written_plainly(string key, double value)
    {
        // The test host has no Microsoft.FlightSimulator.SimConnect.dll, so SetLVar cannot run even on a
        // never-connected manager: the write goes through TryWritePlain with a recording writer.
        var writes = new List<(string, double)>();
        Assert.True(FenixA320Definition.TryWritePlain(key, value, (k, v) => writes.Add((k, v))));
        Assert.Equal(new[] { (key, value) }, writes);
    }

    [Fact]
    public void Other_keys_are_not_plain_writes()
    {
        var writes = new List<(string, double)>();
        Assert.False(FenixA320Definition.TryWritePlain("S_OH_CALLS_MID", 1, (k, v) => writes.Add((k, v))));
        Assert.False(FenixA320Definition.TryWritePlain("S_OH_FUEL_CENTER_2", 1, (k, v) => writes.Add((k, v))));
        Assert.Empty(writes);
    }

    [Fact]
    public void Variant_is_spoken_once_per_distinct_known_variant()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var a321 = V(4, 1, 2, true, false, false, false);
            Assert.Null(FenixA320Definition.VariantSpeech(null, FenixVariant.Unknown));
            Assert.Equal(a321.Describe(), FenixA320Definition.VariantSpeech(null, a321));
            Assert.Null(FenixA320Definition.VariantSpeech(a321.Describe(), a321));
            Assert.Null(FenixA320Definition.VariantSpeech(a321.Describe(), a321 with { HasIsis = false }));
            Assert.NotNull(FenixA320Definition.VariantSpeech(a321.Describe(), a321 with { ActCount = 0 }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Unmeasured_engine_families_keep_n1_mode(int engine)
    {
        var v = V(4, engine, 2, true, false, true, false);
        AssertFitted(v, "S_OH_ENG_N1_MODE_1", "S_OH_ENG_N1_MODE_2");

        // The NEO flag changes no other answer in the A321 case.
        var ceo = V(4, 1, 2, true, false, false, false);
        var neo = ceo with { IsNeo = true };
        foreach (var key in Panels.Values.SelectMany(p => p).Distinct())
            Assert.Equal(FenixA320Definition.IsFitted(key, ceo), FenixA320Definition.IsFitted(key, neo));
    }

    // ---- Panels the livery leaves without a control are not listed (owner's choice, 2026-10-24) ----

    // A definition that has read a Fenix's config L:vars, as the sim delivers them: the aircraft type gates the
    // rest (TryApplyConfigValue), so the type goes first. B_CONFIG_STDBY 1 is the round standby gauges, 0 the
    // ISIS; B_CONFIG_DCDU 0 is no DCDU.
    private static FenixA320Definition Livery(double type = 3, double standby = 0, double dcdu = 1,
        double engine = 0, double act = 0, double adirs = 1, double cargo = 1, double neo = 0)
    {
        var definition = new FenixA320Definition();
        Assert.True(definition.TryApplyConfigValue(FenixConfigVars.AircraftType, type));
        definition.TryApplyConfigValue(FenixConfigVars.EngineType, engine);
        definition.TryApplyConfigValue(FenixConfigVars.ActCount, act);
        definition.TryApplyConfigValue(FenixConfigVars.Standby, standby);
        definition.TryApplyConfigValue(FenixConfigVars.Dcdu, dcdu);
        definition.TryApplyConfigValue(FenixConfigVars.IsNeo, neo);
        definition.TryApplyConfigValue(FenixConfigVars.Adirs, adirs);
        definition.TryApplyConfigValue(FenixConfigVars.CargoHeat, cargo);
        return definition;
    }

    private static List<(string Section, string Panel)> Listed(FenixA320Definition definition)
        => definition.GetPanelStructure()
            .SelectMany(section => section.Value.Select(panel => (section.Key, panel))).ToList();

    [Fact]
    public void An_unread_variant_lists_every_panel()
    {
        var fresh = new FenixA320Definition();
        Assert.Same(FenixVariant.Unknown, fresh.Variant);
        var structure = fresh.GetPanelStructure();

        // The panels that can go are listed, in their place.
        Assert.Contains("DCDU Left", structure["Pedestal"]);
        Assert.Contains("ISIS", structure["Main Instrument Panel"]);
        // Every panel with controls is listed, and every listed panel is a panel with controls.
        var listed = structure.Values.SelectMany(p => p).ToHashSet();
        Assert.True(listed.SetEquals(fresh.GetPanelControls().Keys),
            "listed but without controls: " + string.Join(", ", listed.Except(fresh.GetPanelControls().Keys)) +
            "; with controls but not listed: " + string.Join(", ", fresh.GetPanelControls().Keys.Except(listed)));

        // A type with every other field unread hides nothing either (FNX-11: unknown means shown).
        var typeOnly = new FenixA320Definition();
        typeOnly.TryApplyConfigValue(FenixConfigVars.AircraftType, 4);
        Assert.Equal(Listed(fresh), Listed(typeOnly));
    }

    [Fact]
    public void A_livery_without_a_dcdu_does_not_list_DCDU_Left_and_keeps_the_rest_of_Pedestal_in_order()
    {
        var all = new FenixA320Definition().GetPanelStructure()["Pedestal"];
        Assert.Equal(3, all.IndexOf("DCDU Left"));   // in the middle, so the order below means something

        var noDcdu = Livery(dcdu: 0).GetPanelStructure()["Pedestal"];

        Assert.DoesNotContain("DCDU Left", noDcdu);
        Assert.Equal(all.Where(panel => panel != "DCDU Left"), noDcdu);
    }

    [Fact]
    public void A_livery_with_a_dcdu_lists_DCDU_Left_in_its_place()
    {
        var all = new FenixA320Definition().GetPanelStructure()["Pedestal"];
        Assert.Equal(all, Livery(dcdu: 1).GetPanelStructure()["Pedestal"]);
    }

    [Fact]
    public void Analog_standby_instruments_do_not_list_ISIS_but_Standby_Instruments_stays()
    {
        var all = new FenixA320Definition().GetPanelStructure();
        var gauges = Livery(standby: 1).GetPanelStructure();

        Assert.DoesNotContain("ISIS", gauges["Main Instrument Panel"]);
        Assert.Equal(all["Main Instrument Panel"].Where(panel => panel != "ISIS"), gauges["Main Instrument Panel"]);
        // The compass is on every livery, so the panel stays even though the attitude cage is its only variant control.
        Assert.Equal(all["Cockpit"], gauges["Cockpit"]);
        Assert.Contains("Standby Instruments", gauges["Cockpit"]);
        // The attitude cage is what the round gauges add to it.
        Assert.True(FenixA320Definition.IsFitted("S_STANDBY_ATTITUDE_CAGE", Livery(standby: 1).Variant));
    }

    [Fact]
    public void The_ISIS_livery_lists_ISIS_and_Standby_Instruments()
    {
        var all = new FenixA320Definition().GetPanelStructure();
        var isis = Livery(standby: 0).GetPanelStructure();

        Assert.Contains("ISIS", isis["Main Instrument Panel"]);
        Assert.Contains("Standby Instruments", isis["Cockpit"]);
        Assert.Equal(all["Main Instrument Panel"], isis["Main Instrument Panel"]);
    }

    [Fact]
    public void Only_DCDU_Left_and_ISIS_ever_go_and_each_goes_exactly_when_the_livery_lacks_its_equipment()
    {
        var all = Listed(new FenixA320Definition());

        // Every combination of the config values that decide any control, on the airframes that differ.
        foreach (double type in new[] { 3, 4 })
        foreach (double engine in new[] { 0, 1 })
        foreach (double act in new[] { 0, 2 })
        foreach (double standby in new[] { 0, 1 })
        foreach (double dcdu in new[] { 0, 1 })
        foreach (double adirs in new[] { 0, 1 })
        foreach (double cargo in new[] { 0, 1 })
        {
            var gone = all.Except(Listed(Livery(type, standby, dcdu, engine, act, adirs, cargo))).ToList();

            var expected = new List<(string, string)>();
            if (dcdu == 0) expected.Add(("Pedestal", "DCDU Left"));
            if (standby == 1) expected.Add(("Main Instrument Panel", "ISIS"));
            Assert.True(expected.OrderBy(p => p).SequenceEqual(gone.OrderBy(p => p)),
                $"type {type} engine {engine} act {act} standby {standby} dcdu {dcdu} adirs {adirs} cargo {cargo}: " +
                $"expected {string.Join(", ", expected)}, hidden {string.Join(", ", gone)}");
        }
    }

    [Fact]
    public void A_panel_is_listed_exactly_when_some_control_in_it_is_fitted()
    {
        // The rule itself, for every panel and for variants that hide different things.
        var variants = new[]
        {
            Livery(type: 4, standby: 0, dcdu: 0, engine: 1, act: 2, adirs: 1, cargo: 0),
            Livery(type: 3, standby: 1, dcdu: 1, engine: 0, act: 0, adirs: 0, cargo: 1),
            Livery(type: 2, standby: 1, dcdu: 0, engine: 0, act: 0, adirs: 1, cargo: 0),
        };
        foreach (var definition in variants)
        {
            var controls = definition.GetPanelControls();
            var listed = Listed(definition).Select(p => p.Panel).ToHashSet();
            foreach (var (panel, keys) in controls)
            {
                bool anyFitted = keys.Any(key => definition.IsPanelControlVisible(key, null));
                Assert.True(listed.Contains(panel) == anyFitted,
                    $"{panel} is {(listed.Contains(panel) ? "listed" : "not listed")} but {(anyFitted ? "has" : "lacks")} a fitted control");
            }
        }
    }

    [Fact]
    public void Hiding_a_panel_changes_the_list_only_never_the_sections_or_the_controls()
    {
        var fresh = new FenixA320Definition();
        var bare = Livery(type: 3, standby: 1, dcdu: 0);

        // The sections list is built once, from the keys, so none may go: Pedestal and Main Instrument Panel keep
        // other panels today, and the key stays even if a section were ever emptied.
        Assert.Equal(fresh.GetPanelStructure().Keys, bare.GetPanelStructure().Keys);
        // The controls are the same dictionary whatever the variant: MainForm filters a panel's controls itself.
        Assert.Equal(fresh.GetPanelControls().Keys, bare.GetPanelControls().Keys);
        Assert.Equal(10, bare.GetPanelControls()["DCDU Left"].Count);
        // GetPanelControls is cached by the base class and stays so.
        Assert.Same(bare.GetPanelControls(), bare.GetPanelControls());
    }

    [Fact]
    public void The_list_follows_the_variant_as_it_is_read_and_is_not_frozen_by_the_first_answer()
    {
        var definition = new FenixA320Definition();
        Assert.Contains("DCDU Left", definition.GetPanelStructure()["Pedestal"]);

        // The config arrives: no DCDU, round standby gauges.
        definition.TryApplyConfigValue(FenixConfigVars.Dcdu, 0);
        definition.TryApplyConfigValue(FenixConfigVars.Standby, 1);
        definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3);
        Assert.DoesNotContain("DCDU Left", definition.GetPanelStructure()["Pedestal"]);
        Assert.DoesNotContain("ISIS", definition.GetPanelStructure()["Main Instrument Panel"]);

        // One field flips: only its panel comes back.
        definition.TryApplyConfigValue(FenixConfigVars.Dcdu, 1);
        Assert.Contains("DCDU Left", definition.GetPanelStructure()["Pedestal"]);
        Assert.DoesNotContain("ISIS", definition.GetPanelStructure()["Main Instrument Panel"]);

        // The type back at 0 makes the variant Unknown again, and unknown means shown.
        definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 0);
        Assert.Same(FenixVariant.Unknown, definition.Variant);
        Assert.Contains("ISIS", definition.GetPanelStructure()["Main Instrument Panel"]);
    }
}
