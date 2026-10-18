// The Cessna 172 G1000's COM, NAV and Transponder panels, rebuilt on 2026-10-18 from measurements on
// MSFS 2024's stock aircraft (docs/invariants/c172.md C172-13 to C172-16). Marshall's FS2020 panels
// showed the frequencies with no way to set one, and sent a typed squawk as a plain number (the
// owner's 2300 became 0074).

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CessnaC172RadiosTests
{
    private static readonly CessnaC172G1000Definition Def = new();

    private static CessnaC172BaseDefinition.Radio RadioFor(string label) =>
        CessnaC172BaseDefinition.Radios.Single(r => r.Label == label);

    // C172-14: measured, each event set or swapped exactly its own radio.
    [Theory]
    [InlineData("COM 1", "COM_STBY_RADIO_SET_HZ", "COM_STBY_RADIO_SWAP")]
    [InlineData("COM 2", "COM2_STBY_RADIO_SET_HZ", "COM2_RADIO_SWAP")]
    [InlineData("NAV 1", "NAV1_STBY_SET_HZ", "NAV1_RADIO_SWAP")]
    [InlineData("NAV 2", "NAV2_STBY_SET_HZ", "NAV2_RADIO_SWAP")]
    public void Each_radio_is_set_and_swapped_by_its_own_measured_event(string label, string setEvent, string swapEvent)
    {
        var r = RadioFor(label);
        Assert.Equal(setEvent, r.SetEvent);
        Assert.Equal(swapEvent, r.SwapEvent);
        Assert.Equal(swapEvent, Def.GetVariables()[r.SwapKey].Name);
        Assert.True(Def.GetVariables()[r.SwapKey].RenderAsButton);
    }

    [Theory]
    [InlineData(121.705, 121705000u)]
    [InlineData(118.185, 118185000u)]   // 8.33 kHz channels land exactly
    [InlineData(124.85, 124850000u)]
    [InlineData(113.3, 113300000u)]
    [InlineData(109.5, 109500000u)]
    public void A_standby_is_written_in_whole_hertz(double mhz, uint hz)
    {
        Assert.Equal(hz, CessnaC172BaseDefinition.StandbyHz(mhz));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Standby_confirmations_and_refusals_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var com1 = RadioFor("COM 1");
            var nav2 = RadioFor("NAV 2");

            Assert.Equal("COM 1 standby 121.705", CessnaC172BaseDefinition.DescribeStandby(com1, 121.705));
            Assert.Equal("NAV 2 standby 109.50", CessnaC172BaseDefinition.DescribeStandby(nav2, 109.5));
            Assert.Null(CessnaC172BaseDefinition.StandbyRefusal(com1, 118.0));
            Assert.Null(CessnaC172BaseDefinition.StandbyRefusal(com1, 136.99));
            Assert.Equal("140.000 is outside the COM band, 118.000 to 136.990. Not set.",
                CessnaC172BaseDefinition.StandbyRefusal(com1, 140));
            Assert.Equal("107.90 is outside the NAV band, 108.00 to 117.95. Not set.",
                CessnaC172BaseDefinition.StandbyRefusal(nav2, 107.9));
            Assert.Equal("Enter a COM frequency, 118.000 to 136.990.",
                CessnaC172BaseDefinition.StandbyRefusal(com1, double.NaN));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_typed_standby_is_a_field_the_definition_handles_and_a_typo_is_never_zero()
    {
        foreach (var r in CessnaC172BaseDefinition.Radios)
        {
            var d = Def.GetVariables()[r.SetKey];
            Assert.Contains("_SET", r.SetKey);
            Assert.True(d.UnparseableTextAsNaN, r.SetKey);
            Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);
        }
    }

    [Fact]
    public void An_out_of_band_standby_is_refused_aloud_and_nothing_is_sent()
    {
        var speech = new GatedSpeechCapture();
        var com2 = RadioFor("COM 2");

        // Nothing reaches SendEvent, which this test host cannot load: refused before any write.
        Assert.True(Def.HandleUIVariableSet(com2.SetKey, 99, Def.GetVariables()[com2.SetKey],
            new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { "99.000 is outside the COM band, 118.000 to 136.990. Not set." }, speech.All);
    }

    [Fact]
    public void The_panels_set_and_swap_and_their_status_lists_show_active_and_standby()
    {
        var controls = Def.GetPanelControls();
        var display = Def.GetPanelDisplayVariables();

        Assert.Equal(new[] { "C172_COM1_STANDBY_SET", "C172_COM1_SWAP", "C172_COM2_STANDBY_SET", "C172_COM2_SWAP" }, controls["COM"]);
        Assert.Equal(new[] { "C172_COM1_ACTIVE", "C172_COM1_STANDBY", "C172_COM2_ACTIVE", "C172_COM2_STANDBY" }, display["COM"]);
        Assert.Equal(new[] { "C172_NAV1_STANDBY_SET", "C172_NAV1_SWAP", "C172_NAV2_STANDBY_SET", "C172_NAV2_SWAP" }, controls["NAV"]);
        Assert.Equal(new[] { "C172_NAV1_ACTIVE", "C172_NAV1_STANDBY", "C172_NAV2_ACTIVE", "C172_NAV2_STANDBY" }, display["NAV"]);
        Assert.Equal(new[] { "TRANSPONDER_CODE_SET", "C172_XPNDR_MODE", "C172_XPNDR_IDENT" }, controls["Transponder"]);
        Assert.Equal(new[] { "C172_XPNDR_CODE" }, display["Transponder"]);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void The_status_lists_show_megahertz_and_a_four_digit_squawk_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.True(Def.TryGetDisplayOverride("C172_COM1_ACTIVE", 124.85, out string com));
            Assert.Equal("124.850 MHz", com);
            Assert.True(Def.TryGetDisplayOverride("C172_NAV2_STANDBY", 113.9, out string nav));
            Assert.Equal("113.90 MHz", nav);
            Assert.True(Def.TryGetDisplayOverride("C172_XPNDR_CODE", 74, out string squawk));
            Assert.Equal("0074", squawk);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // C172-13: the squawk goes through MainForm's TRANSPONDER_CODE_SET path, which BCD16-encodes it;
    // the definition must return false, or its generic event path sends the plain number.
    [Fact]
    public void The_squawk_is_left_to_mainforms_bcd_path()
    {
        var d = Def.GetVariables()["TRANSPONDER_CODE_SET"];
        Assert.Equal("XPNDR_SET", d.Name);
        Assert.False(Def.HandleUIVariableSet("TRANSPONDER_CODE_SET", 2300, d, new SimConnectManager(IntPtr.Zero),
            new GatedSpeechCapture()));
    }

    // C172-15: TRANSPONDER STATE:1 measured to set and hold 0, 1, 2, 3, 4 and 5. The G1000's XPDR modes
    // are offered; Off is listed only so the combo never keeps a stale pick at 0 (a combo keeps its last
    // selection for a value it does not list); Test (2) is no G1000 mode and never spoken as a number.
    // The first build set OnlyAnnounceValueDescriptionMatches for that, which made the row a read-only
    // status box: the owner could not change the mode (2026-10-18).
    [Fact]
    public void The_transponder_mode_is_a_combo_of_the_g1000_modes_and_off_that_follows_the_sim()
    {
        var d = Def.GetVariables()["C172_XPNDR_MODE"];
        Assert.Equal("TRANSPONDER STATE:1", d.Name);
        Assert.Equal(new[] { "Off", "Standby", "On", "Altitude", "Ground" },
            d.ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal(new double[] { 0, 1, 3, 4, 5 }, d.ValueDescriptions.Keys.Order());
        Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
        Assert.True(d.IsAnnounced);
        Assert.False(MSFSBlindAssist.Utils.PanelRowRules.IsReadOnlyStatusRow(d));
        Assert.False(d.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void Test_mode_is_never_spoken_and_a_listed_mode_takes_the_generic_path()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate("C172_XPNDR_MODE", 2, speech));
        // false: MainForm moves the combo and speaks a mode changed outside MSFSBA.
        Assert.False(def.ProcessSimVarUpdate("C172_XPNDR_MODE", 4, speech));
        Assert.False(def.ProcessSimVarUpdate("C172_XPNDR_MODE", 0, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Picking_off_is_refused_aloud_and_never_written()
    {
        var speech = new GatedSpeechCapture();
        var mode = Def.GetVariables()["C172_XPNDR_MODE"];
        try
        {
            Assert.True(Def.HandleUIVariableSet("C172_XPNDR_MODE", 0, mode, new SimConnectManager(IntPtr.Zero), speech));
        }
        catch (FileNotFoundException ex) when (ex.FileName?.Contains("SimConnect", StringComparison.Ordinal) == true)
        {
            // The forced re-read reaches SimConnectManager, which this test host cannot load; the
            // refusal was spoken before it.
        }
        Assert.Equal(new[] { "The G1000 transponder has no Off key. Pick Standby, On, Altitude or Ground." }, speech.All);
    }

    // C172-16: ELECTRICAL MAIN BUS VOLTAGE and BUS VOLTAGE:n read 0 on this aircraft; the buses are
    // read as the G1000's engine page reads them, ELECTRICAL BUS VOLTAGE by its bus names, beside
    // the main battery's own voltage.
    [Fact]
    public void The_electrical_readouts_are_the_named_buses_and_the_main_battery()
    {
        var vars = Def.GetVariables();
        Assert.Equal("ELECTRICAL BATTERY VOLTAGE:2", vars["C172_BATTERY_VOLTAGE"].Name);
        Assert.Equal("ELECTRICAL BUS VOLTAGE:'CROSS_FEED_BUS'_n", vars["C172_MAIN_BUS_VOLTS"].Name);
        Assert.Equal("ELECTRICAL BUS VOLTAGE:'ESSENTIAL_BUS'_n", vars["C172_ESS_BUS_VOLTS"].Name);
        Assert.Equal(new[] { "C172_MAIN_BUS_VOLTS", "C172_ESS_BUS_VOLTS", "C172_BATTERY_VOLTAGE" },
            Def.GetPanelDisplayVariables()["Battery and Alternator"]);
        Assert.DoesNotContain(vars.Values, v => v.Name.StartsWith("ELECTRICAL MAIN BUS VOLTAGE", StringComparison.Ordinal)
                                                || v.Name.StartsWith("BUS VOLTAGE", StringComparison.Ordinal));
    }
}
