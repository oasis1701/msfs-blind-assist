// Pins the SHAPE of the C172 definition that the fleet-wide tests cannot express: every panel has a
// controls entry (a missing one makes MainForm's panel build throw), the magneto combo is never
// requested (a delivered 0 from a nonexistent var would snap it to Off), cache-only feeds earn no
// Ctrl+M row, and the guidance numbers are the spec's.
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.C172;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Cessna172DefinitionShapeTests
{
    private static readonly Cessna172Definition Def = new();

    [Fact]
    public void Identity()
    {
        Assert.Equal("C172", Def.AircraftCode);
        Assert.Equal("Cessna 172 Skyhawk (G1000)", Def.AircraftName);
    }

    [Fact]
    public void Every_panel_in_the_structure_has_a_controls_entry_even_if_empty()
    {
        var controls = Def.GetPanelControls();
        foreach (var panel in Def.GetPanelStructure().Values.SelectMany(p => p))
            Assert.True(controls.ContainsKey(panel), $"panel '{panel}' has no BuildPanelControls entry");
    }

    [Fact]
    public void Every_display_variable_is_registered()
    {
        var vars = Def.GetVariables();
        foreach (var key in Def.GetPanelDisplayVariables().Values.SelectMany(k => k))
            Assert.True(vars.ContainsKey(key), $"display key '{key}' is not in GetVariables()");
    }

    [Fact]
    public void The_magneto_combo_is_an_action_control_that_is_never_requested()
    {
        var def = Def.GetVariables()["C172_MAGNETOS"];
        Assert.Equal(UpdateFrequency.Never, def.UpdateFrequency);
        Assert.Equal(4, def.ValueDescriptions.Count);
        Assert.DoesNotContain(Cessna172Magnetos.Start, def.ValueDescriptions.Keys.Select(k => (int)k));
    }

    [Fact]
    public void The_start_engine_and_ident_buttons_are_buttons_that_are_never_requested()
    {
        foreach (var key in new[] { "C172_ENGINE_START", "C172_XPNDR_IDENT" })
        {
            var def = Def.GetVariables()[key];
            Assert.True(def.RenderAsButton, key);
            Assert.Equal(UpdateFrequency.Never, def.UpdateFrequency);
            Assert.False(def.IsAnnounced, key);
        }
    }

    [Fact]
    public void Cache_only_feeds_are_continuous_and_hidden_from_ctrl_m()
    {
        var vars = Def.GetVariables();
        foreach (var key in Cessna172Definition.CacheOnlyVariables)
        {
            Assert.Equal(UpdateFrequency.Continuous, vars[key].UpdateFrequency);
            Assert.True(vars[key].IsAnnounced, key);
            Assert.True(vars[key].ExcludeFromMonitorManager, key);
        }
    }

    [Theory]
    [InlineData("C172_STALL", "Stall warning")]
    [InlineData("C172_OVERSPEED", "Overspeed warning")]
    [InlineData("C172_COMBUSTION", "Engine stopped warning")]
    [InlineData("C172_LOW_FUEL_LEFT", "Low fuel warning, left tank")]
    [InlineData("C172_LOW_FUEL_RIGHT", "Low fuel warning, right tank")]
    [InlineData("C172_LOW_VOLTAGE", "Low voltage warning")]
    [InlineData("C172_OIL_PRESSURE_WARN", "Oil pressure warning")]
    [InlineData("C172_OIL_TEMP_WARN", "Oil temperature warning")]
    [InlineData("C172_MAG_LEFT", "Magneto position")]
    [InlineData("C172_COM1_ACTIVE", "COM1 active")]
    [InlineData("TRANSPONDER_CODE_SET", "Squawk")]
    public void Every_background_callout_has_a_ctrl_m_row(string key, string label)
    {
        var rows = MonitorRowBuilder.Build(Def.GetVariables());
        Assert.Contains(rows, r => r.Key == key && r.Label == label);
    }

    [Fact]
    public void The_squawk_is_read_as_bco16()
    {
        var def = Def.GetVariables()["TRANSPONDER_CODE_SET"];
        Assert.Equal("Bco16", def.Units);
        Assert.Equal("TRANSPONDER CODE:1", def.Name);
    }

    [Fact]
    public void No_calculator_path_probe_is_registered()
        => Assert.False(Def.GetVariables().ContainsKey("MSFSBA_BRIDGE_PROBE"));

    [Fact]
    public void The_guidance_numbers_are_the_specs()
    {
        var p = Def.GetVisualGuidanceProfile();
        Assert.Equal(65.0, p.ReferenceVrefKnots);
        Assert.Equal(5.0, p.TypicalApproachAoaDeg);
        Assert.Equal(4.0, p.MaxPitchRateDegPerSec);
        Assert.Equal(6.0, p.MaxBankRateDegPerSec);
        Assert.Equal(10.0, p.TonePitchRangeDeg);
        Assert.Equal(15.0, p.ToneBankRangeDeg);
        Assert.Equal(5.0, p.GlideslopeAltitudeBiasFt);
        Assert.Equal(12.0, p.FlareTriggerWheelHeightFt);
        Assert.Equal(8.0, p.FlareTargetPitchDeg);
        Assert.Equal(3.0, p.FlareAltitudeBiasFt);
        Assert.Equal(0.5, Def.TaxiTurnLeadSeconds);
    }

    [Fact]
    public void The_magneto_display_row_relabels_on_any_of_its_three_inputs()
    {
        var def = Def.GetVariables()["C172_MAG_LEFT"];
        Assert.NotNull(def.StateVariables);
        Assert.Equal(new[] { "C172_MAG_LEFT", "C172_MAG_RIGHT", "C172_STARTER" }, def.StateVariables!);
    }
}
