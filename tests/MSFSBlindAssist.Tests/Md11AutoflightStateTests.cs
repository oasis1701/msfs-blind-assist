using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The autoflight wording, pinned once: the FCP window, the Shift+H/S/A/V read-outs, the
/// Ctrl+H/S/A/V dialogs and the Autoflight Status panel all read from Md11AutoflightState, so
/// these strings are what a pilot hears everywhere. The engagement rules come from TFDi's Forward
/// Panel page (speed window dashed = FMS speed, heading window dashed = NAV, V/S window dashed =
/// V/S or FPA not engaged) and from a live cruise on 2026-09-06.
/// </summary>
public class Md11AutoflightStateTests
{
    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "AP 1")]
    [InlineData(2, "AP 2")]
    [InlineData(3, "AP 1 and 2")]
    public void Autopilot_FollowsTheDocumentedEnum(double state, string expected)
        => Assert.Equal(expected, Md11AutoflightState.Autopilot(state));

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "on")]   // measured live with the autothrottle engaged
    [InlineData(2, "on")]   // never observed; treated as on
    public void Autothrottle_IsOnAboveZero(double state, string expected)
        => Assert.Equal(expected, Md11AutoflightState.Autothrottle(state));

    [Theory]
    [InlineData(1, 1, "AP 1, ATS on")]
    [InlineData(0, 1, "AP off, ATS on")]
    [InlineData(1, 0, "AP 1, ATS off")]
    [InlineData(3, 1, "AP 1 and 2, ATS on")]
    [InlineData(0, 0, "off")]
    public void Autoflight_NamesBothHalves_TheButtonEngagesBoth(double ap, double ats, string expected)
        => Assert.Equal(expected, Md11AutoflightState.Autoflight(ap, ats));

    [Theory]
    [InlineData(-999, true, "dashed, FMS speed engaged")]
    [InlineData(0.81999999, true, "Mach 0.820")]  // float32 round trip: format, never compare
    [InlineData(0.825, true, "Mach 0.825")]       // three decimals, the FCP's own "%0.3f" — never "0.83"
    [InlineData(0.82, true, "Mach 0.820")]
    [InlineData(250, false, "250 knots")]
    [InlineData(-9999, false, "-9999 knots")]     // the V/S window's sentinel is a value on this one
    public void SpeedValue_ReadsMachOrKnots_OrTheDashes(double spd, bool mach, string expected)
        => Assert.Equal(expected, Md11AutoflightState.SpeedValue(spd, mach));

    [Theory]
    [InlineData(-999, "dashed, NAV engaged")]
    [InlineData(-9999, "-9999")]                  // the V/S window's sentinel is a value on this one
    [InlineData(5, "005")]
    [InlineData(123, "123")]
    public void HeadingValue_IsThreeDigits_OrTheDashes(double hdg, string expected)
        => Assert.Equal(expected, Md11AutoflightState.HeadingValue(hdg));

    [Theory]
    [InlineData(36000, false, "36000 feet")]
    [InlineData(11000, true, "11000 metres")]
    [InlineData(-999, false, "dashed")]
    [InlineData(-9999, false, "dashed")]          // TFDi document no altitude sentinel; neither is a legal altitude
    public void AltitudeValue_CarriesItsUnit(double alt, bool metres, string expected)
        => Assert.Equal(expected, Md11AutoflightState.AltitudeValue(alt, metres));

    [Theory]
    [InlineData(-9999, false, "dashed, not engaged")]
    [InlineData(-9999, true, "dashed, not engaged")]
    [InlineData(-1500, false, "-1500 feet per minute")]
    [InlineData(-1000, false, "-1000 feet per minute")]   // the round number that the old <= -999 rule called dashed
    [InlineData(-500, false, "-500 feet per minute")]   // a real descent is never "dashed"
    [InlineData(-999, false, "-999 feet per minute")]   // the speed/heading sentinel is a selected V/S here
    [InlineData(-3, true, "-3.0 degrees")]
    public void VerticalValue_NeverClaimsProf(double vs, bool fpa, string expected)
        => Assert.Equal(expected, Md11AutoflightState.VerticalValue(vs, fpa));

    [Fact]
    public void Engagement_ComesFromTheDashedWindows()
    {
        Assert.True(Md11AutoflightState.NavEngaged(-999));
        Assert.False(Md11AutoflightState.NavEngaged(123));
        Assert.False(Md11AutoflightState.NavEngaged(-9999));   // the V/S window's sentinel is not the heading window's
        Assert.True(Md11AutoflightState.FmsSpeedEngaged(-999));
        Assert.False(Md11AutoflightState.FmsSpeedEngaged(250));
        Assert.Equal("engaged", Md11AutoflightState.Engaged(true));
        Assert.Equal("not engaged", Md11AutoflightState.Engaged(false));
    }

    /// <summary>
    /// After a SimConnect drop the cache is empty, and the still-refreshing FCP window read
    /// "Autopilot: off / Speed: 0 knots" — confident facts about an aircraft nobody had read. An
    /// unread value is "not available" in every composer, and a window's engagement is not guessed
    /// from a window that was never read.
    /// </summary>
    [Fact]
    public void AnUnreadValue_IsNotAvailable_NeverAZeroReading()
    {
        Assert.Equal("not available", Md11AutoflightState.NotAvailable);
        Assert.Equal("not available", Md11AutoflightState.Autopilot(null));
        Assert.Equal("not available", Md11AutoflightState.Autothrottle(null));
        Assert.Equal("not available", Md11AutoflightState.Autoflight(null, 1));
        Assert.Equal("not available", Md11AutoflightState.Autoflight(1, null));
        Assert.Equal("not available", Md11AutoflightState.SpeedValue(null, mach: true));
        Assert.Equal("not available", Md11AutoflightState.HeadingValue(null));
        Assert.Equal("not available", Md11AutoflightState.AltitudeValue(null, metres: false));
        Assert.Equal("not available", Md11AutoflightState.VerticalValue(null, fpa: false));
        Assert.Null(Md11AutoflightState.NavEngaged(null));
        Assert.Null(Md11AutoflightState.FmsSpeedEngaged(null));
        Assert.Equal("not available", Md11AutoflightState.Engaged(Md11AutoflightState.NavEngaged(null)));
        Assert.Equal("Speed: not available", Md11AutoflightState.Row(Md11AutoflightState.Speed, Md11AutoflightState.SpeedValue(null, mach: false)));
    }

    /// <summary>The four window modes, named from the aircraft's own 0/1 mode vars — and not guessed when unread.</summary>
    [Theory]
    [InlineData(0.0, "IAS", "Heading", "V/S", "feet")]
    [InlineData(1.0, "Mach", "Track", "FPA", "metres")]
    public void ModeWords_FollowTheAircraftsOwnModeVars(double mode, string speed, string heading, string vertical, string altitude)
    {
        Assert.Equal(speed, Md11AutoflightState.SpeedMode(mode));
        Assert.Equal(heading, Md11AutoflightState.HeadingMode(mode));
        Assert.Equal(vertical, Md11AutoflightState.VerticalMode(mode));
        Assert.Equal(altitude, Md11AutoflightState.AltitudeUnit(mode));
    }

    [Fact]
    public void ModeWords_AreNotAvailable_WhileTheModeVarIsUnread()
    {
        Assert.Equal("not available", Md11AutoflightState.SpeedMode(null));
        Assert.Equal("not available", Md11AutoflightState.HeadingMode(null));
        Assert.Equal("not available", Md11AutoflightState.VerticalMode(null));
        Assert.Equal("not available", Md11AutoflightState.AltitudeUnit(null));
    }

    [Fact]
    public void Renderers_ReadTheSameFactsThreeWays()
    {
        var s = Md11AutoflightState.Speed;
        var h = Md11AutoflightState.Heading;
        Assert.Equal("Heading: dashed, NAV engaged", Md11AutoflightState.Row(h, Md11AutoflightState.HeadingValue(-999)));
        Assert.Equal("Track: 123", Md11AutoflightState.Row(Md11AutoflightState.HeadingNoun(track: true), Md11AutoflightState.HeadingValue(123)));
        Assert.Equal("FPA: -3.0 degrees", Md11AutoflightState.Row(Md11AutoflightState.VerticalNoun(fpa: true), Md11AutoflightState.VerticalValue(-3, fpa: true)));
        Assert.Equal("Selected heading dashed, NAV engaged", Md11AutoflightState.Selected(h, Md11AutoflightState.HeadingValue(-999)));
        Assert.Equal("Selected speed Mach 0.820", Md11AutoflightState.Selected(s, Md11AutoflightState.SpeedValue(0.82, mach: true)));
        Assert.Equal("Selected track 123", Md11AutoflightState.Selected(Md11AutoflightState.Track, "123"));
        // FPA is an acronym and must never be string-lowered to "fPA".
        Assert.Equal("Selected FPA dashed, not engaged", Md11AutoflightState.Selected(Md11AutoflightState.Fpa, Md11AutoflightState.VerticalValue(-9999, fpa: true)));
        Assert.Equal("123", Md11AutoflightState.PanelValue(h, Md11AutoflightState.HeadingNoun(track: false), "123"));
        Assert.Equal("track 123", Md11AutoflightState.PanelValue(h, Md11AutoflightState.HeadingNoun(track: true), "123"));
        Assert.Equal("FPA -3.0 degrees", Md11AutoflightState.PanelValue(Md11AutoflightState.VerticalSpeed, Md11AutoflightState.VerticalNoun(fpa: true), "-3.0 degrees"));
        Assert.Equal("Mach 0.82", Md11AutoflightState.PanelValue(s, s, "Mach 0.82"));
    }

    [Fact]
    public void TheLiveCruiseSnapshot_ComposesTheFcpWindow()
    {
        // 2026-09-06, FL360: SPD -999, HDG -999, ALT 36000, VS -9999, AP 1, ATS 1; Mach mode,
        // heading mode, feet, V/S mode. NAV, PROF and FMS SPD were engaged in the aircraft.
        Assert.Equal("Speed: dashed, FMS speed engaged", Md11AutoflightState.Row(Md11AutoflightState.Speed, Md11AutoflightState.SpeedValue(-999, mach: true)));
        Assert.Equal("Heading: dashed, NAV engaged", Md11AutoflightState.Row(Md11AutoflightState.HeadingNoun(false), Md11AutoflightState.HeadingValue(-999)));
        Assert.Equal("Altitude: 36000 feet", Md11AutoflightState.Row(Md11AutoflightState.Altitude, Md11AutoflightState.AltitudeValue(36000, false)));
        Assert.Equal("Vertical speed: dashed, not engaged", Md11AutoflightState.Row(Md11AutoflightState.VerticalNoun(false), Md11AutoflightState.VerticalValue(-9999, false)));
        Assert.Equal("AP 1, ATS on", Md11AutoflightState.Autoflight(1, 1));
    }
}
