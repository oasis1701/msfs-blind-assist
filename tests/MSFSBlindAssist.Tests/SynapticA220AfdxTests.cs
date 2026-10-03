using MSFSBlindAssist.Aircraft.A220;
using static MSFSBlindAssist.Aircraft.A220.A220Afdx;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the pure A220 AFDX formatting (A220Afdx): flight-director state/command text,
/// stabilizer + rudder trim rendering, and the trim announcer's settle gate.
///
/// The SIGN and SCALE assertions here are the point of the file. Every one of these
/// values was decoded from the aircraft's own rendering maths (a wrong sign in a
/// guidance readout is confidently wrong, not obviously broken), so each is written
/// down as a test rather than a comment that can quietly drift:
///   cmd_lateral        + = bank RIGHT
///   cmd_vertical_fpa   + = flight path UP
///   cmd_vertical_pitch + = nose UP
///   rudder_trim        + = nose RIGHT, and |1| = full scale (so it reads as percent)
/// </summary>
public class SynapticA220AfdxTests
{
    private static AutoflightBlock Af(bool l = false, bool r = false, int? lat = null, int? vert = null,
        double? bank = null, double? fpa = null, double? pitch = null)
        => new()
        {
            l_fd = l, r_fd = r, lateral = lat, vertical = vert,
            cmd_lateral = bank, cmd_vertical_fpa = fpa, cmd_vertical_pitch = pitch
        };

    private static FlightControlBlock Fc(double? trim = null, double? dn = null, double? up = null, double? rud = null)
        => new() { pitch_trim = trim, pitch_trim_dn = dn, pitch_trim_up = up, rudder_trim = rud };

    // ---- flight director state --------------------------------------------

    [Fact]
    public void FdState_UnknownWhenLinkDown_NeverReadsAsOff()
    {
        // The whole reason this exists: the FD L:var is a momentary press pulse, so
        // "no data" must never be rendered as the plausible-looking "Off".
        Assert.Equal("On", FdStateText(true));
        Assert.Equal("Off", FdStateText(false));
        Assert.Contains("Unknown", FdStateText(null));
    }

    [Fact]
    public void FlightDirector_NoBlock_SaysUnavailable_NotOff()
    {
        string text = FormatFlightDirector(null);
        Assert.Contains("not available", text);
        Assert.DoesNotContain("Flight directors off", text);
    }

    [Theory]
    [InlineData(true, true, "Flight directors on, both sides")]
    [InlineData(true, false, "Left flight director on, right off")]
    [InlineData(false, true, "Right flight director on, left off")]
    [InlineData(false, false, "Flight directors off")]
    public void FlightDirector_ReportsEachSide(bool l, bool r, string expected)
        => Assert.StartsWith(expected, FormatFlightDirector(Af(l, r)));

    [Fact]
    public void FlightDirector_BothOff_SaysWhyThereAreNoCommandBars()
    {
        string text = FormatFlightDirector(Af(false, false, bank: 5, fpa: 3));
        Assert.Contains("No command bars", text);
        Assert.DoesNotContain("Commanding", text);
    }

    // ---- flight director commands (sign conventions) -----------------------

    [Fact]
    public void FlightDirector_PositiveBankIsRight_NegativeIsLeft()
    {
        Assert.Contains("5 degrees bank right", FormatFlightDirector(Af(true, false, bank: 5)));
        Assert.Contains("5 degrees bank left", FormatFlightDirector(Af(true, false, bank: -5)));
    }

    [Fact]
    public void FlightDirector_NearZeroBankSpeaksAsWingsLevel()
    {
        // "0 degrees bank right" reads as a broken value, not as a command.
        Assert.Contains("wings level", FormatFlightDirector(Af(true, false, bank: 0.2)));
        Assert.DoesNotContain("degrees bank", FormatFlightDirector(Af(true, false, bank: 0.2)));
    }

    [Fact]
    public void FlightDirector_PositiveFpaIsUp_AndNearZeroIsLevel()
    {
        Assert.Contains("flight path 3.0 degrees up", FormatFlightDirector(Af(true, false, fpa: 3)));
        Assert.Contains("flight path 3.0 degrees down", FormatFlightDirector(Af(true, false, fpa: -3)));
        Assert.Contains("flight path level", FormatFlightDirector(Af(true, false, fpa: 0.05)));
    }

    [Fact]
    public void FlightDirector_PositivePitchTargetIsNoseUp()
    {
        Assert.Contains("pitch target 15.0 degrees nose up", FormatFlightDirector(Af(true, false, pitch: 15)));
        Assert.Contains("pitch target 2.0 degrees nose down", FormatFlightDirector(Af(true, false, pitch: -2)));
    }

    // ---- FG modes ----------------------------------------------------------

    [Fact]
    public void FlightDirector_TakeoffModeIsNamed()
    {
        // TOGA guidance is the case that has to be recognisable by ear.
        string text = FormatFlightDirector(Af(true, true, LateralTakeoff, VerticalTakeoff, bank: 0, pitch: 15));
        Assert.Contains("Takeoff mode", text);
        Assert.Contains("pitch target 15.0 degrees nose up", text);
    }

    [Fact]
    public void FlightDirector_GoAroundModeIsNamed()
        => Assert.Contains("Go-around mode",
            FormatFlightDirector(Af(true, true, LateralGoAround, VerticalGoAround)));

    [Fact]
    public void FlightDirector_OtherModesReadByName()
    {
        string text = FormatFlightDirector(Af(true, true, 0, 2));   // HDG / FLC
        Assert.Contains("HDG lateral", text);
        Assert.Contains("FLC vertical", text);
    }

    [Fact]
    public void ModeTables_MatchTheAircraftsOwnEnums()
    {
        // Verbatim from src/avionics/lib/afdx/ap.ts — an off-by-one here renames every
        // mode, so the anchors are pinned rather than trusted.
        Assert.Equal("TO", LateralModeName(LateralTakeoff));
        Assert.Equal("GA", LateralModeName(LateralGoAround));
        Assert.Equal("TO", VerticalModeName(VerticalTakeoff));
        Assert.Equal("GA", VerticalModeName(VerticalGoAround));
        Assert.Equal("ROLLOUT", LateralModeName(14));
        Assert.Equal("FLARE", VerticalModeName(16));
        Assert.Null(LateralModeName(99));
        Assert.Null(VerticalModeName(-1));
        // The AT table is NOT positional — it starts at 2 in the aircraft's source.
        Assert.Equal("SPD", AutothrottleModeName(2));
        Assert.Equal("THRUST", AutothrottleModeName(0));
    }

    // ---- knob-walk aim math ------------------------------------------------

    [Fact]
    public void AimClicks_AimsAtTheNearestReachableValue_NotTheFloor()
    {
        // The live case: altitude selector in metres, ~492 ft per click, 915 ft to go.
        // Flooring gives 1 click (423 ft short, then a creep that straddles the target
        // forever); rounding gives 2 and lands on the closest value the knob can select.
        Assert.Equal(2, AimClicks(915, 492));
        Assert.Equal(1, AimClicks(600, 492));
    }

    [Fact]
    public void AimClicks_ZeroMeansAlreadyOnTheClosestSelectableValue()
    {
        // Less than half a step away — any click makes it worse, so the walk must stop
        // and report, not keep hunting.
        Assert.Equal(0, AimClicks(200, 492));
        Assert.Equal(0, AimClicks(-200, 492));
    }

    [Fact]
    public void AimClicks_IsDirectionAgnostic_TheCallerChoosesTheEvent()
        => Assert.Equal(AimClicks(915, 492), AimClicks(-915, 492));

    [Fact]
    public void AimClicks_CappedAndSafeOnAZeroStep()
    {
        Assert.Equal(220, AimClicks(1_000_000, 1));
        Assert.Equal(0, AimClicks(500, 0));      // never divide by a bad measurement
    }

    // ---- grid-snap click math (the altitude selector's real behavior) ------

    [Fact]
    public void GridClicks_OnGrid_IsPlainDivision()
    {
        Assert.Equal(4, GridClicks(2000, 6000, 1000));
        Assert.Equal(4, GridClicks(6000, 2000, 1000));
        Assert.Equal(3, GridClicks(6700, 6400, 100));
        Assert.Equal(0, GridClicks(6000, 6000, 1000));
    }

    [Fact]
    public void GridClicks_OffGrid_FirstClickSnapsToTheGrid()
    {
        // The live 2026-08-10 proof: 2625 ft + one coarse inc lands on 3000 (snap),
        // NOT 3625 (offset). So 2625 -> 6000 is 1 snap click + 3 whole steps.
        Assert.Equal(4, GridClicks(2625, 6000, 1000));
        // And downward the first click snaps DOWN: 2625 -> 2000 is one snap to 2000.
        Assert.Equal(1, GridClicks(2625, 2000, 1000));
        // The metres-leftover case the old model could never solve: 5085 -> 6000 on
        // the fine ring = snap to 5100 (1 click) + 9 hundreds.
        Assert.Equal(10, GridClicks(5085, 6000, 100));
    }

    [Fact]
    public void GridClicks_SafeOnBadStep()
        => Assert.Equal(0, GridClicks(2000, 6000, 0));

    /// <summary>
    /// The coarse stage used to exit at |delta| &lt; step/2 — half a THOUSAND feet —
    /// while the walk's final verdict demands 50 ft, so an off-grid start within 500 ft
    /// of a round thousand fired zero clicks and then announced "Altitude did not reach
    /// 5000 — selector reads 5085 feet" for a target ONE click away. These pin that the
    /// grid arithmetic has always been able to make that one click; only the stage's
    /// exit tolerance was wrong.
    /// </summary>
    [Theory]
    [InlineData(5085, 5000)]   // metres-mode leftover, 85 ft from the thousand
    [InlineData(2300, 2000)]   // 300 ft from the thousand below
    [InlineData(2700, 3000)]   // 300 ft from the thousand above
    public void GridClicks_ReachesARoundThousandLessThanHalfAStepAway(double cur, int target)
        => Assert.Equal(1, GridClicks(cur, target, 1000));

    // ---- fine / coarse ring detection --------------------------------------

    /// <summary>
    /// The two altitude rings differ by 10x (100 ft fine vs 1000 ft coarse, measured
    /// live 2026-09-21), so a burst that moved a tenth of the expected step means the
    /// ring write did not take — not that the read-back was noisy.
    /// </summary>
    [Fact]
    public void StepIsWrongRing_SpotsTheOtherRing()
    {
        Assert.True(StepIsWrongRing(perClick: 100, expectedStep: 1000));   // still fine
        Assert.True(StepIsWrongRing(perClick: 1000, expectedStep: 100));   // still coarse
    }

    /// <summary>
    /// A first click that SNAPS to the grid legitimately moves a fraction of a step, and
    /// a burst the sim partly swallowed also reads low — neither may be mistaken for the
    /// wrong ring, or the walk would flip the ring mid-stage for no reason.
    /// </summary>
    [Fact]
    public void StepIsWrongRing_ToleratesSnapAndSwallowedClicks()
    {
        Assert.False(StepIsWrongRing(perClick: 700, expectedStep: 1000));   // 2300 -> 3000
        Assert.False(StepIsWrongRing(perClick: 925, expectedStep: 1000));   // 2300 -> 6000 / 4
        Assert.False(StepIsWrongRing(perClick: 1000, expectedStep: 1000));
        Assert.False(StepIsWrongRing(perClick: 100, expectedStep: 100));
    }

    // ---- walk endgame: straddle vs under-delivery ---------------------------

    /// <summary>
    /// A straddle is a SIGN CHANGE in the remaining delta — the burst carried the knob
    /// past the target, so the value it is on is the closest reachable one.
    /// </summary>
    [Fact]
    public void WalkStraddled_TrueWhenTheBurstWentPastTheTarget()
    {
        Assert.True(WalkStraddled(delta: -69, prevDelta: 915));   // metres altitude, 5085 -> 6069
        Assert.True(WalkStraddled(delta: 0.4, prevDelta: -0.6));  // 1 deg heading step
    }

    /// <summary>
    /// The "it often stops short" bug: a round that merely UNDER-delivers — clicks lost in
    /// transport, or a step estimate that was too large — keeps the delta's SIGN, and must
    /// cost one more round rather than ending the walk tens of degrees out. The old test
    /// (|delta| >= |prevDelta|) could not tell these apart.
    /// </summary>
    [Theory]
    [InlineData(-18.0, -108.0)]   // asked 108 deg, ~90 clicks landed
    [InlineData(-108.0, -108.0)]  // a round that delivered nothing at all
    [InlineData(-120.0, -108.0)]  // and one that somehow went backwards
    public void WalkStraddled_FalseWhenTheRoundOnlyUnderDelivered(double delta, double prevDelta)
        => Assert.False(WalkStraddled(delta, prevDelta));

    [Fact]
    public void WalkStraddled_FalseOnTheFirstRoundAndOnAnExactLanding()
    {
        Assert.False(WalkStraddled(delta: -108, prevDelta: double.NaN));  // nothing to compare
        Assert.False(WalkStraddled(delta: 0, prevDelta: 5));              // landed ON it
    }

    [Fact]
    public void StepIsWrongRing_SafeOnAZeroOrMissingMeasurement()
    {
        Assert.False(StepIsWrongRing(perClick: 0, expectedStep: 1000));
        Assert.False(StepIsWrongRing(perClick: 100, expectedStep: 0));
    }

    // ---- FMA phrases for the FCP readouts ----------------------------------

    [Fact]
    public void LateralStatus_NullWithoutABlock_SoTheCallerFallsBack()
        => Assert.Null(LateralStatusPhrase(null));

    [Fact]
    public void LateralStatus_NamesTheEngagedModeAndTheArmedMode()
    {
        Assert.Equal("HDG mode", LateralStatusPhrase(new AutoflightBlock { lateral = 0 }));
        Assert.Equal("FMS1 lateral, LOC armed",
            LateralStatusPhrase(new AutoflightBlock { lateral = 4, lateral_arm = 8 }));
        Assert.Equal("takeoff mode", LateralStatusPhrase(new AutoflightBlock { lateral = LateralTakeoff }));
    }

    [Fact]
    public void VerticalStatus_SpeaksTheRealFmaMode()
    {
        // The point of the change: ] A must say FLC / ALTS / PATH like the PFD, not
        // an annunciator L:var's approximation.
        Assert.Equal("FLC", VerticalStatusPhrase(new AutoflightBlock { vertical = 2 }));
        Assert.Equal("FLC, ALTS armed",
            VerticalStatusPhrase(new AutoflightBlock { vertical = 2, vertical_arm_vert = 8 }));
        Assert.Equal("go-around mode", VerticalStatusPhrase(new AutoflightBlock { vertical = VerticalGoAround }));
        Assert.Null(VerticalStatusPhrase(null));
    }

    [Fact]
    public void VerticalStatus_ArmedSameAsEngaged_NotRepeated()
        => Assert.Equal("ALT", VerticalStatusPhrase(new AutoflightBlock { vertical = 7, vertical_arm_vert = 7 }));

    [Fact]
    public void AutothrottleStatus_UsesTheNonPositionalTable()
    {
        Assert.Equal("autothrottle SPD", AutothrottleStatusPhrase(new AutoflightBlock { at_mode = 2 }));
        Assert.Null(AutothrottleStatusPhrase(new AutoflightBlock()));
        Assert.Null(AutothrottleStatusPhrase(null));
    }

    // ---- stabilizer trim ---------------------------------------------------

    [Fact]
    public void StabTrim_NoBlock_SaysUnavailable()
        => Assert.Contains("Not available", FormatStabTrim(null));

    [Fact]
    public void StabTrim_ReportsUnitsAsTheEicasPrintsThem()
        => Assert.Equal("5.4 units", FormatStabTrim(Fc(trim: 5.42)));

    [Fact]
    public void StabTrim_InsideTakeoffBand_SaysSo()
        => Assert.Equal("5.4 units, in takeoff range", FormatStabTrim(Fc(trim: 5.4, dn: 4.2, up: 6.8)));

    [Fact]
    public void StabTrim_OutsideTakeoffBand_QuotesTheBand()
    {
        // This is the answer to a CONFIG STAB TRIM warning: not just "wrong", but where to put it.
        string text = FormatStabTrim(Fc(trim: 3.1, dn: 4.2, up: 6.8));
        Assert.Contains("OUTSIDE takeoff range", text);
        Assert.Contains("4.2 to 6.8", text);
    }

    [Fact]
    public void StabTrim_BandOmittedWhenTheAircraftPublishesNone()
    {
        // The EICAS hides the green line when the limits are absent or equal; quoting a
        // zero-width "range" would invent a limit the aircraft never showed.
        Assert.Equal("5.4 units", FormatStabTrim(Fc(trim: 5.4)));
        Assert.Equal("5.4 units", FormatStabTrim(Fc(trim: 5.4, dn: 5.0, up: 5.0)));
    }

    [Fact]
    public void StabTrim_BandEdgesCountAsInRange()
    {
        Assert.Contains("in takeoff range", FormatStabTrim(Fc(trim: 4.2, dn: 4.2, up: 6.8)));
        Assert.Contains("in takeoff range", FormatStabTrim(Fc(trim: 6.8, dn: 4.2, up: 6.8)));
    }

    // ---- rudder trim -------------------------------------------------------

    [Fact]
    public void RudderTrim_PositiveIsNoseRight_AsPercentOfFullTravel()
    {
        Assert.Equal("40 percent nose right", FormatRudderTrim(Fc(rud: 0.4)));
        Assert.Equal("40 percent nose left", FormatRudderTrim(Fc(rud: -0.4)));
        Assert.Equal("100 percent nose right", FormatRudderTrim(Fc(rud: 1.0)));
    }

    [Fact]
    public void RudderTrim_ZeroReadsAsCentred()
    {
        // Centred is the takeoff-config condition; "0 percent nose right" hides that.
        Assert.Equal("Centred", FormatRudderTrim(Fc(rud: 0)));
        Assert.Equal("Centred", FormatRudderTrim(Fc(rud: 0.001)));
    }

    [Fact]
    public void RudderTrim_NoBlock_SaysUnavailable()
        => Assert.Contains("Not available", FormatRudderTrim(null));

    // ---- trim announcement + settle gate -----------------------------------

    [Fact]
    public void TrimAnnouncement_NullWithoutStabTrim()
        => Assert.Null(TrimAnnouncement(Fc(rud: 0.4)));

    [Fact]
    public void TrimAnnouncement_AddsRudderOnlyWhenOffCentre()
    {
        Assert.Equal("Stab trim 5.4 units", TrimAnnouncement(Fc(trim: 5.4, rud: 0)));
        Assert.Contains("rudder trim 40 percent nose right", TrimAnnouncement(Fc(trim: 5.4, rud: 0.4)));
    }

    [Fact]
    public void TrimSettled_SilentWhileTheTrimIsStillMoving()
        => Assert.False(TrimSettled(spoken: 3.0, previous: 4.0, current: 5.0));

    [Fact]
    public void TrimSettled_FiresOnceTheValueStopsAndDiffersFromWhatWasSpoken()
        => Assert.True(TrimSettled(spoken: 3.0, previous: 5.0, current: 5.0));

    [Fact]
    public void TrimSettled_SilentWhenItSettlesBackOnTheSpokenValue()
        => Assert.False(TrimSettled(spoken: 5.0, previous: 5.0, current: 5.0));

    [Fact]
    public void TrimSettled_SilentWithoutTwoSamples()
    {
        Assert.False(TrimSettled(spoken: 3.0, previous: null, current: 5.0));
        Assert.False(TrimSettled(spoken: 3.0, previous: 5.0, current: null));
    }

    [Fact]
    public void TrimSettled_FirstEverValueSpeaksOnceSettled()
        => Assert.True(TrimSettled(spoken: null, previous: 5.0, current: 5.0));
}
