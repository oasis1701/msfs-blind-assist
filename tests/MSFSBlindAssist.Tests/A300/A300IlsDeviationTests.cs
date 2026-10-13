using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The PFD box's localizer and glideslope deviation, as the A300's PFD draws them (design decision, 2026-10-10: read
/// the deviation, not the marker beacons). From the aircraft's code (1.0.11): AP_LOGIC::updateRadioReceiver
/// stores INI_LOC_DEV and INI_GS_DEV already in dots (the angles divided by 0.8 and 0.4, the FBW's
/// conversion) and INI_LOC_VALID; PFD::DrawPFD draws both scales only while that side's EFIS nav selector is
/// on ILS (2), the localizer pointer while INI_LOC_VALID is 1 and the glideslope pointer while NAV HAS GLIDE
/// SLOPE:3 is 1, each pointer clamped at 2.5 dots. A positive localizer value draws the pointer right of
/// centre (the course is right: the aircraft is left of it); a positive glideslope value draws it below
/// centre (the aircraft is above). Worded as the FBW A320 reads the glideslope ("dots above glideslope").
/// </summary>
public class A300IlsDeviationTests
{
    private const double Ils = 2, Vor = 0;

    [Theory]
    [InlineData(0.0, "on the localizer")]
    [InlineData(0.04, "on the localizer")]
    [InlineData(1.2, "1.2 dots left of localizer")]
    [InlineData(-0.5, "0.5 dots right of localizer")]
    [InlineData(2.5, "2.5 dots left of localizer")]
    [InlineData(-3.7, "more than 2.5 dots right of localizer")]
    public void The_localizer_reads_where_the_aircraft_is(double dots, string text) =>
        Assert.Equal(text, A300IlsDeviation.Localizer(Ils, 1, dots));

    [Theory]
    [InlineData(0.0, "on the glideslope")]
    [InlineData(0.8, "0.8 dots above glideslope")]
    [InlineData(-1.5, "1.5 dots below glideslope")]
    [InlineData(4.0, "more than 2.5 dots above glideslope")]
    public void The_glideslope_reads_where_the_aircraft_is(double dots, string text) =>
        Assert.Equal(text, A300IlsDeviation.Glideslope(Ils, 1, dots));

    [Fact]
    public void Neither_is_shown_unless_the_efis_nav_selector_is_on_ils() =>
        Assert.Equal(("not shown, EFIS not on ILS", "not shown, EFIS not on ILS"),
            (A300IlsDeviation.Localizer(Vor, 1, 0.3), A300IlsDeviation.Glideslope(1, 1, 0.3)));

    [Fact]
    public void Without_a_signal_the_pointer_is_not_drawn() =>
        Assert.Equal(("no signal", "no signal"), (A300IlsDeviation.Localizer(Ils, 0, 0.3), A300IlsDeviation.Glideslope(Ils, 0, 0.3)));

    [Fact]
    public void An_unread_input_gives_no_line() =>
        Assert.Equal((null, null), (A300IlsDeviation.Localizer(null, 1, 0.3), A300IlsDeviation.Glideslope(Ils, null, 0.3)));

    [Fact]
    public void Dots_read_with_a_point_in_a_comma_decimal_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.2 dots left of localizer", A300IlsDeviation.Localizer(Ils, 1, 1.2));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void The_pfd_box_lists_both_after_the_radio_altitude()
    {
        var def = new IniA300Definition();
        var pfd = def.GetPanelDisplayVariables()["PFD"];
        int ra = pfd.IndexOf(A300Readouts.PfdRadioAltitudeKey);
        Assert.Equal(new[] { A300IlsDeviation.LocalizerKey, A300IlsDeviation.GlideslopeKey }, pfd.Skip(ra + 1).Take(2));
    }

    [Fact]
    public void The_deviations_are_read_on_request_and_their_inputs_stream_silently_with_no_ctrl_m_row()
    {
        var def = new IniA300Definition();
        def.Attach(new SimConnectManager(IntPtr.Zero));
        var vars = def.GetVariables();
        Assert.Equal(("INI_LOC_DEV", UpdateFrequency.OnRequest), (vars[A300IlsDeviation.LocalizerKey].Name, vars[A300IlsDeviation.LocalizerKey].UpdateFrequency));
        Assert.Equal(("INI_GS_DEV", UpdateFrequency.OnRequest), (vars[A300IlsDeviation.GlideslopeKey].Name, vars[A300IlsDeviation.GlideslopeKey].UpdateFrequency));
        Assert.Equal(("INI_efis_selected_nav_capt", "INI_LOC_VALID", "NAV HAS GLIDE SLOPE:3"),
            (vars[A300IlsDeviation.NavSelectorKey].Name, vars[A300IlsDeviation.LocalizerValidKey].Name, vars[A300IlsDeviation.GlideslopeReceivedKey].Name));
        var speech = new SpeechCapture();
        foreach (var key in A300IlsDeviation.InputKeys)
        {
            Assert.True(vars[key].ExcludeFromBatch && vars[key].ExcludeFromMonitorManager, key);
            Assert.True(def.ProcessSimVarUpdate(key, 1, speech), key);
        }
        Assert.Equal(SimVarType.SimVar, vars[A300IlsDeviation.GlideslopeReceivedKey].Type);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_status_display_composes_each_line_from_the_cache()
    {
        var cache = new Dictionary<string, double>
        {
            [A300IlsDeviation.NavSelectorKey] = Ils, [A300IlsDeviation.LocalizerValidKey] = 1, [A300IlsDeviation.GlideslopeReceivedKey] = 1,
        };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.True(def.TryGetDisplayOverride(A300IlsDeviation.LocalizerKey, -0.5, out var localizer));
        Assert.True(def.TryGetDisplayOverride(A300IlsDeviation.GlideslopeKey, 0.8, out var glideslope));
        Assert.Equal(("0.5 dots right of localizer", "0.8 dots above glideslope"), (localizer, glideslope));
    }
}
