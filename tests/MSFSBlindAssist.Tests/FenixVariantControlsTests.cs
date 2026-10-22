// Pins Fenix v3 plan 06 (variants): the controls only some Fenix airframes have are registered and
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
}
