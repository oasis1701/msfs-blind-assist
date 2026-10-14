using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The VHF and ADF panels have two windows and a transfer switch that picks the window in use; it does not swap
/// them (measured 2026-10-10). So a line or a read-back names a window by its role now. Only the ADF windows have
/// knobs on the panel (the VHF knobs are not rows, design decision 2026-10-14).
/// </summary>
public class A300RadiosTests
{
    private static A300Radio Radio(string name) => A300Radios.All.Single(r => r.Name == name);

    [Theory]
    // The VHF transfer switch at 0 uses the first window (INI_COM1_FREQUENCY), at 1 the second.
    [InlineData(0, "122.800", "124.805")]
    [InlineData(1, "124.805", "122.800")]
    public void A_vhf_reads_the_window_its_transfer_switch_picks_as_active(double transfer, string active, string standby)
    {
        var vhf = Radio("VHF 1");
        Assert.Equal((active, standby), (vhf.Active(122800, 124805, transfer), vhf.Standby(122800, 124805, transfer)));
    }

    [Theory]
    // The ADF transfer switch the other way round: at 1 the first window (INI_ADF1_FREQUENCY) is in use.
    [InlineData(1, "900", "990")]
    [InlineData(0, "990", "900")]
    public void An_adf_reads_the_window_its_transfer_switch_picks_as_active(double transfer, string active, string standby)
    {
        var adf = Radio("ADF 1");
        Assert.Equal((active, standby), (adf.Active(900, 990, transfer), adf.Standby(900, 990, transfer)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Frequencies_read_the_same_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            // The ADF units knob steps half a kilohertz.
            Assert.Equal(("121.500", "900.5"), (Radio("VHF 2").Frequency(121500), Radio("ADF 2").Frequency(900.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    // The ADF knobs are the knobs left: each window (1 or 2) against each transfer switch value (0 or 1) is covered, so the
    // role-by-transfer-switch logic stays pinned now that the VHF knobs are not rows.
    [InlineData("A300_CPT_ADF1_BIG", 1, 900, "ADF 1 active 900")]
    [InlineData("A300_CPT_ADF2_MED", 1, 990, "ADF 1 standby 990")]
    [InlineData("A300_CPT_ADF1_MED", 0, 900, "ADF 1 standby 900")]
    [InlineData("A300_CPT_ADF2_SMALL", 0, 990, "ADF 1 active 990")]
    [InlineData("A300_FO_ADF1_SMALL", 0, 900.5, "ADF 2 standby 900.5")]
    [InlineData("A300_FO_ADF2_SMALL", 0, 345, "ADF 2 active 345")]
    public void A_knob_step_reads_back_its_window_by_its_role_now(string knob, double transfer, double window, string phrase)
    {
        var (radio, which) = A300Radios.ByKnob[knob];
        Assert.Equal(phrase, radio.KnobPhrase(which, window, transfer));
    }

    [Fact]
    public void Only_the_adf_knobs_are_knobs_the_vhf_ones_are_not_rows()
    {
        // Design decision, Robin, 2026-10-14: a typed standby box sets a VHF frequency (A300PanelLayout leaves the knobs out).
        Assert.Equal(12, A300Radios.ByKnob.Count);
        Assert.All(A300Radios.ByKnob.Values, knob => Assert.StartsWith("ADF", knob.Radio.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(A300Radios.ByKnob.Keys, key => key.Contains("_VHF", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A300_CPT_VHF_TFR", "VHF 1", 122800, 124805, 1, "VHF 1 active 124.805")]
    [InlineData("A300_FO_VHF_TFR", "VHF 2", 122800, 124805, 0, "VHF 2 active 122.800")]
    [InlineData("A300_ADF1_TFR", "ADF 1", 900, 990, 0, "ADF 1 active 990")]
    [InlineData("A300_ADF2_TFR", "ADF 2", 900, 990, 1, "ADF 2 active 900")]
    public void A_transfer_reads_back_the_frequency_now_in_use(string button, string name, double window1, double window2,
        double transfer, string phrase)
    {
        var radio = A300Radios.ByTransfer[button];
        Assert.Equal(name, radio.Name);
        Assert.Equal(phrase, radio.TransferPhrase(window1, window2, transfer));
    }

    [Fact]
    public void Each_radio_panels_status_display_reads_active_and_standby()
    {
        string[] Lines(string panel) => A300Readouts.All.Where(r => r.Panel == panel).Select(r => r.Name).ToArray();
        Assert.Equal(new[] { "VHF 1 active", "VHF 1 standby", "VHF 2 active", "VHF 2 standby" }, Lines("VHF Radios"));
        Assert.Equal(new[] { "ADF 1 active", "ADF 1 standby", "ADF 1 bearing", "ADF 2 active", "ADF 2 standby", "ADF 2 bearing" },
            Lines("ADF Radios"));
    }

    [Theory]
    // ADF RADIAL is the relative bearing to the station, read only while ADF SIGNAL is not 0.
    [InlineData(0, 90, "no signal")]
    [InlineData(1, 45, "45 degrees right")]
    [InlineData(1, 315, "45 degrees left")]
    [InlineData(1, -30, "30 degrees left")]
    [InlineData(1, 0.3, "ahead")]
    [InlineData(1, 180, "behind")]
    public void An_adf_bearing_reads_where_the_station_is(double signal, double radial, string text) =>
        Assert.Equal(text, A300Radios.Bearing(signal, radial));

    [Fact]
    public void The_status_display_composes_a_line_from_both_windows_and_the_transfer_switch()
    {
        var vhf = Radio("VHF 1");
        var cache = new Dictionary<string, double> { [vhf.Window1Key] = 122800, [vhf.Window2Key] = 124805 };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.True(def.TryGetDisplayOverride(vhf.ActiveKey, 1, out var active));
        Assert.True(def.TryGetDisplayOverride(vhf.StandbyKey, 1, out var standby));
        Assert.Equal(("124.805", "122.800"), (active, standby));

        var vars = def.GetVariables();
        Assert.Equal(vhf.TransferVar, vars[vhf.ActiveKey].Name);
        foreach (var key in new[] { vhf.Window1Key, vhf.Window2Key, vhf.TransferKey })
            Assert.True(vars[key].ExcludeFromBatch && vars[key].ExcludeFromMonitorManager, key);
    }
}
