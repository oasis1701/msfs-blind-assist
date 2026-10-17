using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The engine instruments and the ECAM system pages, one status box each. Every line is a value
/// the A300's own ECAM page prints as a number (read from its drawing routines, v1.0.11), or the
/// simulator's engine value behind the round gauges; needles with no printed number (generator
/// load, hydraulic reservoirs) are left out.
/// </summary>
public class A300EcamPagesTests
{
    private readonly IniA300Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();

    public A300EcamPagesTests()
    {
        _def = new IniA300Definition { Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null };
        _def.Attach(_sim);
    }

    private string Shown(string key, double value)
    {
        Assert.True(_def.TryGetDisplayOverride(key, value, out var text), $"{key} has no display text");
        return text;
    }

    [Fact]
    public void The_pages_follow_the_engine_instruments_in_cockpit_order()
    {
        // The standby instruments are the Instrument section's Standby Instruments panel, the fleet's ISIS
        // panel: its controls and its readings in one place (a panel name can be in only one section). The left
        // ECAM display's memos come before the system pages the right one shows.
        Assert.Equal(new[]
        {
            "PFD", "ND", "Engine Instruments", "ECAM Memos",
            "ECAM Engine", "ECAM Electrical AC", "ECAM Electrical DC", "ECAM Hydraulics", "ECAM Bleed",
            "ECAM Air Conditioning", "ECAM Pressurization", "ECAM Fuel", "ECAM APU",
        }, _def.GetPanelStructure()[A300DisplayPanels.Section]);
    }

    [Fact]
    public void Each_value_is_registered_once_and_shared_between_pages()
    {
        var names = A300EcamPages.Readouts.Select(r => r.Var.ToUpperInvariant()).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains(A300EcamPages.Engine1BleedPressureKey, A300DisplayPanels.Lines["ECAM Engine"]);
        Assert.Contains(A300EcamPages.Engine1BleedPressureKey, A300DisplayPanels.Lines["ECAM Bleed"]);
    }

    [Fact]
    public void Every_page_line_is_registered_on_request()
    {
        var vars = _def.GetVariables();
        foreach (var readout in A300EcamPages.Readouts)
        {
            Assert.Equal(readout.Var, vars[readout.Key].Name);
            Assert.Equal(UpdateFrequency.OnRequest, vars[readout.Key].UpdateFrequency);
        }
    }

    [Theory]
    [InlineData("INI_eng1_oil_pressure", 45.4, "45 psi")]
    [InlineData("INI_eng2_oil_temperature", 17.97, "18 degrees Celsius")]
    [InlineData("INI_eng1_vibration_N1", 1.23, "1.2")]
    [InlineData("INI_elec_gen1_voltage", 115.2, "115 volts")]
    [InlineData("INI_elec_gen2_frequency", 400.4, "400 hertz")]
    [InlineData("INI_BATTERY1_CURR_AMPS", -0.873, "-0.9 amps")]
    [InlineData("INI_TR3_VOLTAGE", 27.99, "28 volts")]
    [InlineData("INI_hyd_yellow_pressure", 3010, "3,010 psi")]
    [InlineData("INI_cabin_alt", 444.8, "445 feet")]
    [InlineData("INI_Delta_PSI", 7.84, "7.8 psi")]
    [InlineData("INI_apu_n1", 99.6, "100 percent")]
    [InlineData("TURB ENG N1:2", 84.27, "84.3 percent")]
    public void Each_page_line_reads_with_its_unit(string var, double value, string text)
    {
        var readout = A300EcamPages.Readouts.Single(r => r.Var == var);
        Assert.Equal(text, Shown(readout.Key, value));
    }

    [Theory]
    [InlineData(1, "2,998 kilograms", "1,200 kilograms per hour", "kilograms")]
    [InlineData(0, "6,609 pounds", "2,646 pounds per hour", "pounds")]
    public void Fuel_reads_in_the_weight_unit_set_on_the_tablet(double metric, string tank, string flow, string unit)
    {
        _cache[A300EcamPages.WeightUnitKey] = metric;
        Assert.Equal(tank, Shown(A300EcamPages.Readouts.Single(r => r.Var == "INI_FUEL_WEIGHT_LEFT_OUTER").Key, 2997.67));
        Assert.Equal(flow, Shown(A300EcamPages.Readouts.Single(r => r.Var == "ENG FUEL FLOW PPH:1").Key, 2645.55));
        Assert.Equal(unit, Shown(A300EcamPages.WeightUnitKey, metric));
    }

    [Fact]
    public void Fuel_with_the_weight_unit_unknown_reads_kilograms_as_stored()
    {
        Assert.Equal("2,998 kilograms", Shown(A300EcamPages.Readouts.Single(r => r.Var == "INI_FUEL_WEIGHT_LEFT_OUTER").Key, 2997.67));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Page_values_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("-0.9 amps", Shown(A300EcamPages.Readouts.Single(r => r.Var == "INI_BATTERY1_CURR_AMPS").Key, -0.873));
            Assert.Equal("3,010 psi", Shown(A300EcamPages.Readouts.Single(r => r.Var == "INI_hyd_yellow_pressure").Key, 3010));
            Assert.Equal("84.3 percent", Shown(A300EcamPages.Readouts.Single(r => r.Var == "TURB ENG N1:2").Key, 84.27));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
