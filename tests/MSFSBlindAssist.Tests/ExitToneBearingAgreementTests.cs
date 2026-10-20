// Pins TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord — the gate that stops
// the post-turn-now Normal-exit steering tone from switching onto an ExitBearingTrue
// that sits on the OPPOSITE bank from the turn direction just spoken.
//
// The spoken word comes from the junction → ApronNodeId bearing (the side the route
// actually vacates to); the tone target comes from the exit's first-edge bearing.
// The 2026-08-26 whole-DB consistency sweep found ~2,000 Normal exits where the two
// disagree in SIGN — crossing exits whose best-edge tie-break picked the far bank
// (CYYZ D3/H/N/R/S/T) and reverse-angled RETs whose first edge is a near-parallel
// stub pointing the other way (KCLT W2/W9/E6-E10). Ungated, the pilot heard
// "Turn left now" while the tone pulled right and SATURATED as they obeyed the
// verbal. When the sources contradict, the tone stays on runway heading (silent
// while straight) — silence over a confident wrong pull.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ExitToneBearingAgreementTests
{
    // Runway heading 90 (due east): bearings < 90 are LEFT of the landing
    // direction, > 90 are RIGHT.

    [Theory]
    [InlineData("left", 0.0)]     // bearing due north = 90 deg left of the runway
    [InlineData("left", 45.0)]
    [InlineData("right", 180.0)]  // due south = 90 deg right
    [InlineData("right", 100.0)]  // shallow right
    public void AgreeingSides_AreTrusted(string word, double bearing)
    {
        Assert.True(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord(word, bearing, 90.0));
    }

    [Theory]
    [InlineData("left", 180.0)]   // spoken left, bearing right — CYYZ D3 shape
    [InlineData("right", 350.0)]  // spoken right, bearing left
    [InlineData("left", 100.0)]   // KCLT shape: shallow stub bearing opposite the word
    public void ContradictingSides_AreNotTrusted(string word, double bearing)
    {
        Assert.False(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord(word, bearing, 90.0));
    }

    [Fact]
    public void NoSpokenDirection_HasNothingToContradict()
    {
        // ResolveExitTurnDirection dropped the word (both sources under 10 deg) —
        // today's ExitBearingTrue behaviour is kept.
        Assert.True(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord(null, 180.0, 90.0));
    }

    [Fact]
    public void UncomputedBearing_IsTrusted()
    {
        // ExitBearingTrue == 0 means "no edge found" — the tone never switches to it
        // anyway (the caller's > 0 gate), so the predicate must not veto.
        Assert.True(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord("left", 0.0, 90.0));
    }

    [Fact]
    public void DueNorthBearing_IsStoredAs360_AndStillCompares()
    {
        // LandingExit stores a due-north edge as 360 to keep 0 unambiguous. For a
        // runway heading 90, due north is LEFT.
        Assert.True(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord("left", 360.0, 90.0));
        Assert.False(TaxiGuidanceManager.ExitToneBearingAgreesWithTurnWord("right", 360.0, 90.0));
    }
}

/// <summary>
/// Pins TaxiGuidanceManager.ResolveTurnToneTarget — the post-turn-now tone target
/// for Normal exits. The tone must ACTIVELY guide the commanded turn: the measured
/// ExitBearingTrue when trustworthy, the junction→apron bearing (the route's own
/// direction, same source as the spoken word) when it is not, a pause only when no
/// usable source exists, and runway-heading hold only when the exit direction is
/// genuinely unknown.
/// </summary>
public class ResolveTurnToneTargetTests
{
    // Runway heading 90 (due east) throughout: < 90 is LEFT, > 90 is RIGHT.

    [Fact]
    public void TrustworthyBearing_IsUsed()
    {
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("right", 180.0, 170.0, 90.0);
        Assert.False(pause);
        Assert.Equal(180.0, target);
    }

    [Fact]
    public void WrongBankBearing_FallsToApron()
    {
        // CYYZ/KCLT shape: bearing on the far bank from the spoken word — the apron
        // bearing (the route's own direction) takes over so the tone pulls WITH the
        // verbal instead of against it.
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("left", 180.0, 20.0, 90.0);
        Assert.False(pause);
        Assert.Equal(20.0, target);
    }

    [Fact]
    public void StubBearing_FallsToApron()
    {
        // KDTW Y3 shape: first edge ~4° off the runway axis (agrees in sign but
        // meaningless in magnitude — the tone would pull BACK during an 81° turn),
        // while the apron sits a real 80° off. The apron wins.
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("left", 86.0, 10.0, 90.0);
        Assert.False(pause);
        Assert.Equal(10.0, target);
    }

    [Fact]
    public void WrongBankBearing_NoApron_Pauses()
    {
        // Word spoken, bearing contradicts it, no apron node: the verbal owns the
        // turn — a runway-heading hold would pull against it, so the tone pauses
        // until the 15° turnBegun handoff.
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("left", 180.0, null, 90.0);
        Assert.True(pause);
        Assert.Equal(0.0, target);
    }

    [Fact]
    public void NoSources_HoldsRunwayHeading()
    {
        // Nothing spoken, nothing measured — pre-existing behaviour: hold the
        // centreline heading.
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget(null, 0.0, null, 90.0);
        Assert.False(pause);
        Assert.Equal(0.0, target);
    }

    [Fact]
    public void NearParallelApron_DoesNotQualify()
    {
        // An apron bearing within 10° of the runway axis carries no direction
        // information — with a word spoken the tone pauses rather than steering to
        // a near-parallel target.
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("left", 180.0, 95.0, 90.0);
        Assert.True(pause);
        Assert.Equal(0.0, target);
    }

    [Fact]
    public void DueNorthApron_SurvivesAs360()
    {
        var (target, pause) = TaxiGuidanceManager.ResolveTurnToneTarget("left", 180.0, 360.0, 90.0);
        Assert.False(pause);
        Assert.Equal(360.0, target);
    }
}
