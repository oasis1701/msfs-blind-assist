// The C172's navigation and display keys, silent until 2026-10-18: Ctrl+W, D, Shift+D and the waypoint-
// passing call (the shared G1000Waypoints, the DA40's code), Alt+E (the CAS list, G1000CasWatcherTests),
// Alt+I (the standby instruments) and input Ctrl+N (both NAV standbys in one window).

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CessnaC172NavigationTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Alt_I_reads_the_standby_instruments_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            // Parked on the G1000, 2026-10-18: the pitch SimVar reads nose up negative.
            Assert.Equal("Standby airspeed 0 knots. Altitude 805 feet. Compass 134 degrees. " +
                "Attitude 2 degrees nose up, wings level. Vacuum 4.3 inches.",
                CessnaC172G1000Definition.DescribeStandbyInstruments(0, 805.2, 133.63, -2.254, -0.057, 4.3457));
            // Bank reads left positive, as PLANE BANK DEGREES does (VG-7).
            Assert.Equal("Standby airspeed 95 knots. Altitude 3500 feet. Compass 360 degrees. " +
                "Attitude 3 degrees nose down, 15 degrees bank right. Vacuum 5.0 inches, green.",
                CessnaC172G1000Definition.DescribeStandbyInstruments(95.4, 3500.4, 0.2, 3.1, -15.2, 5.0));
            Assert.Equal("Standby airspeed 70 knots. Attitude nose level, 1 degree bank left.",
                CessnaC172G1000Definition.DescribeStandbyInstruments(70, null, null, 0.2, 1.2, null));
            Assert.Equal("Standby instruments not available",
                CessnaC172G1000Definition.DescribeStandbyInstruments(null, null, null, null, null, null));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>
    /// Ctrl+N sets each field that holds a frequency in its band, NAV 1 first, parsed as typed
    /// (CORE-17): a comma is a decimal on any PC, an empty or refused field sets nothing.
    /// </summary>
    [Fact]
    public void Ctrl_N_sets_each_nav_standby_typed_in_its_band()
    {
        var both = CessnaC172BaseDefinition.PlanNavStandbys(new[] { "110.50", "113,90" });
        Assert.Equal(new[] { ("NAV 1", 110.50), ("NAV 2", 113.90) }, both.Select(s => (s.Radio.Label, s.Mhz)));

        var second = CessnaC172BaseDefinition.PlanNavStandbys(new[] { "", "109.5" });
        Assert.Equal(new[] { ("NAV 2", 109.5) }, second.Select(s => (s.Radio.Label, s.Mhz)));

        Assert.Empty(CessnaC172BaseDefinition.PlanNavStandbys(new[] { "120", "abc" }));
    }

    [Theory]
    [InlineData("108.00", true)]
    [InlineData("117,95", true)]
    [InlineData("118", false)]
    [InlineData("1,500", false)]
    [InlineData("", false)]
    public void A_ctrl_n_field_is_checked_against_its_band(string typed, bool valid)
    {
        var (isValid, message) = CessnaC172BaseDefinition.NavStandbyValidator(CessnaC172BaseDefinition.NavRadios[0], typed);
        Assert.Equal(valid, isValid);
        if (!valid) Assert.Contains("NAV", message);
    }

    /// <summary>
    /// The passing call has a Ctrl+M row (its own tracker checks it); the VNAV readouts are silent and
    /// have none (VAR-9). All three are swallowed in ProcessSimVarUpdate, so a flight plan activating
    /// never says "Waypoint Passing Call: 1".
    /// </summary>
    [Fact]
    public void The_passing_call_has_a_ctrl_m_row_and_the_vnav_readouts_none()
    {
        var def = new CessnaC172G1000Definition();
        var rows = MonitorRowBuilder.Build(def.GetVariables()).Select(r => r.Key).ToHashSet();

        Assert.Contains(CessnaC172G1000Definition.WaypointPassingKey, rows);
        Assert.DoesNotContain(CessnaC172G1000Definition.VnavTodDistanceKey, rows);
        Assert.DoesNotContain(CessnaC172G1000Definition.VnavTodLegKey, rows);

        foreach (var key in new[] { CessnaC172G1000Definition.WaypointPassingKey,
                     CessnaC172G1000Definition.VnavTodDistanceKey, CessnaC172G1000Definition.VnavTodLegKey })
        {
            Assert.True(CessnaC172G1000Definition.IsNavigationReadout(key), key);
            Assert.True(def.GetVariables().ContainsKey(key), key);
        }
    }
}
