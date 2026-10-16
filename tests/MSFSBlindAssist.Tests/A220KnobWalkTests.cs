using System.Globalization;
using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220Afdx;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The FCP knob walks' pure decisions and speech (the walks themselves are sim paths, checked
/// live). Which knob may stand on a stock var when the aircraft's own block is missing is the
/// [A220-8] / [A220-13] decision; every walk ends in one spoken line, never silence.
/// </summary>
public class A220KnobWalkTests
{
    // ---- [A220-8] / [A220-13]: the stock-var fallback ----------------------------------

    // FcpKnob is internal, so the theories name it as text (a public test method cannot take it).
    [Theory]
    [InlineData("Heading", true)]    // AUTOPILOT HEADING LOCK DIR tracks the bug (2026-09-21, -24)
    [InlineData("Altitude", true)]   // AUTOPILOT ALTITUDE LOCK VAR tracks the selector (2026-09-21)
    [InlineData("Vertical", true)]   // today's code; the V/S read-back is Part 9b's (R3)
    [InlineData("Speed", false)]     // 445 kt read with 142 in the window (2026-09-24)
    [InlineData("StabTrim", false)]  // [A220-13]: never walk a stabilizer on the animation ratio
    public void OnlyKnobsWhoseStockVarTracksMayFallBackToIt(string knob, bool expected)
        => Assert.Equal(expected, KnobWalkMayUseStockVar(Enum.Parse<FcpKnob>(knob)));

    [Fact]
    public void EveryKnobHasAFallbackDecision()
        => Assert.Equal(5, Enum.GetValues<FcpKnob>().Length);

    [Fact]
    public void StabTrim_RefusesWithTheLinkDown_SayingWhyAndWhatToDo()
    {
        Assert.Equal(StabTrimLinkDownText, KnobWalkRefusalText(FcpKnob.StabTrim, "Stabilizer trim"));
        Assert.Equal("Stabilizer trim not set — the display link is down, so the trim cannot be read back. "
            + "Try again when the link is back.", StabTrimLinkDownText);
    }

    [Theory]
    [InlineData("Speed", "Speed", "Speed not set: the aircraft is not reporting its selected speed right now. Try again in a moment.")]
    [InlineData("Heading", "Heading", "Heading not set: the aircraft is not reporting its selected heading right now. Try again in a moment.")]
    [InlineData("Vertical", "Vertical speed", "Vertical speed not set: the aircraft is not reporting its selected vertical speed right now. Try again in a moment.")]
    public void NoReadingBeforeTheFirstClick_RefusesWithoutAGuess(string knob, string name, string expected)
        => Assert.Equal(expected, KnobWalkRefusalText(Enum.Parse<FcpKnob>(knob), name));

    // ---- One spoken line on every way out (R6, R7) ------------------------------------

    [Theory]
    [InlineData("Heading", "Not connected to the simulator — heading not changed.")]
    [InlineData("Stabilizer trim", "Not connected to the simulator — stabilizer trim not changed.")]
    public void NotConnected_RefusesBeforeAnyClick(string name, string expected)
        => Assert.Equal(expected, KnobWalkNotConnectedText(name));

    [Theory]
    [InlineData("Altitude", "Altitude not confirmed — the aircraft stopped reporting it. Check the altitude.")]
    [InlineData("Flight path angle", "Flight path angle not confirmed — the aircraft stopped reporting it. Check the flight path angle.")]
    public void LostReadBack_IsNotConfirmed_NeverAGuessedValue(string name, string expected)
        => Assert.Equal(expected, KnobWalkNotConfirmedText(name));

    [Theory]
    [InlineData("Speed", "Speed walk stopped — check the speed.")]
    [InlineData("Vertical speed", "Vertical speed walk stopped — check the vertical speed.")]
    public void AFailedWalk_SaysSo_InTheStandbyBaroPattern(string name, string expected)
        => Assert.Equal(expected, KnobWalkFailedText(name));

    // ---- The trim walk's spoken numbers -------------------------------------------------

    private static FlightControlBlock Fc(double? dn = null, double? up = null)
        => new() { pitch_trim_dn = dn, pitch_trim_up = up };

    [Theory]
    [InlineData(4.6, "4.6 units")]
    [InlineData(5.0, "5.0 units")]
    [InlineData(0.04, "0.0 units")]
    public void StabTrimUnits_AreTheEicasFigure(double units, string expected)
        => Assert.Equal(expected, StabTrimUnitsText(units));

    [Fact]
    public void StabTrimBandNote_QuotesTheTakeoffBand_OnlyWhenTheAircraftPublishesOne()
    {
        Assert.Equal(", in takeoff range", StabTrimWalkBandNote(5.0, Fc(4.2, 6.8)));
        Assert.Equal(", still OUTSIDE takeoff range 4.2 to 6.8", StabTrimWalkBandNote(3.1, Fc(4.2, 6.8)));
        Assert.Equal("", StabTrimWalkBandNote(5.0, null));
        Assert.Equal("", StabTrimWalkBandNote(5.0, Fc(dn: 4.2)));
        Assert.Equal("", StabTrimWalkBandNote(5.0, Fc(5.0, 5.0)));   // equal limits: the EICAS hides the band
    }

    // ---- The speed and FPA walks' spoken numbers ----------------------------------------

    [Theory]
    [InlineData(78, "Mach 0.78")]   // the Mach walk counts the knob in hundredths
    [InlineData(80, "Mach 0.80")]
    public void MachWalkValue_IsSpokenAsMach(double hundredths, string expected)
        => Assert.Equal(expected, MachHundredthsText(hundredths));

    [Theory]
    [InlineData(-3.0, "-3.0 degrees")]
    [InlineData(2.5, "2.5 degrees")]
    public void FpaWalkValue_IsSpokenInDegrees(double degrees, string expected)
        => Assert.Equal(expected, FpaDegreesText(degrees));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void WalkSpeech_IsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("4.6 units", StabTrimUnitsText(4.6));
            Assert.Equal(", still OUTSIDE takeoff range 4.2 to 6.8", StabTrimWalkBandNote(3.1, Fc(4.2, 6.8)));
            Assert.Equal("Mach 0.78", MachHundredthsText(78));
            // sv-SE writes a minus as U+2212: the reader must hear the ASCII sign MSFSBA speaks everywhere.
            Assert.Equal("-3.0 degrees", FpaDegreesText(-3.0));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
