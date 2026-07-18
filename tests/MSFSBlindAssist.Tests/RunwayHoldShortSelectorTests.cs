// Characterization tests for MSFSBlindAssist.Navigation.RunwayHoldShortSelector.
//
// Safety-critical: this selector decides WHICH hold-short line a runway-
// destination taxi route stops at (TaxiGuidanceManager.TruncateToHoldShort
// delegates the index choice to it). The matrix pins the PR #162 behaviour:
//
//   • DEFAULT (preferIlsHold == false): the hold CLOSEST to the runway — the
//     full-length line (EGKK A1/M1), matching a normal ATC clearance. The ILS
//     hold is deliberately NOT preferred even when it sits on the same final
//     approach (user decision 2026-07 — do not make the IHS the default again).
//   • CAT III / LVP (preferIlsHold == true): the ILS hold is honoured only when
//     it sits just behind the full-length hold on the SAME final approach
//     (SAME_APPROACH_IHS_MAX_M = 150 m). A transit taxiway's ILS-critical-area
//     hold hundreds of metres away must NOT hijack the truncation (OMDB 30R via
//     N12, fs2024: taxiway N carries IHS nodes ~620 m from N12's real hold).
//
// Index convention (mirrors TruncateToHoldShort's backward scan): ihsIndex /
// hsIndex are the LATEST route-segment index whose ToNode is an ILSHoldShort /
// HoldShort, or -1 when the route has none of that type. A larger index is
// closer to the runway.
//
// This is characterization, not spec verification: if a literal ever disagrees
// with actual output, correct the test to match real output, not vice versa.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayHoldShortSelectorTests
{
    [Fact]
    public void SameApproachGateIs150Metres()
    {
        // Tuned constant documented in docs/taxi-guidance.md — a real CAT II/III
        // hold sits tens of metres behind the CAT I line; a transit hold on the
        // prior taxiway is hundreds of metres away.
        Assert.Equal(150.0, RunwayHoldShortSelector.SAME_APPROACH_IHS_MAX_M);
    }

    // ---- Default mode (preferIlsHold == false): full-length line wins ----

    [Fact]
    public void Default_SameApproachIhsBehindHs_PicksFullLengthHold_Egkk()
    {
        // EGKK 26L via A: the CAT III hold (IHS, index 5) sits ~35 m behind the
        // full-length hold (HS, index 8). The separation is well WITHIN the
        // same-approach gate, so this pins that the gate branch never even runs
        // without the LVP opt-in — default stops at the full-length line.
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 5, hsIndex: 8, holdSeparationMeters: 35.0, preferIlsHold: false);
        Assert.Equal(8, at);
        Assert.Equal(RunwayHoldChoice.PlainHold, choice);
    }

    [Theory]
    [InlineData(7)] // plain hold mid-route
    [InlineData(0)] // plain hold on the very first segment — index 0 is a valid pick
    public void Default_OnlyPlainHold_PicksIt(int hsIndex)
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: -1, hsIndex: hsIndex, holdSeparationMeters: double.MaxValue, preferIlsHold: false);
        Assert.Equal(hsIndex, at);
        Assert.Equal(RunwayHoldChoice.PlainHold, choice);
    }

    [Fact]
    public void Default_OnlyIlsHold_PicksIt()
    {
        // Some airports' only marked hold line is typed IHS — there it IS the
        // runway hold; ignoring IHS nodes in default mode would truncate nothing.
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 4, hsIndex: -1, holdSeparationMeters: double.MaxValue, preferIlsHold: false);
        Assert.Equal(4, at);
        Assert.Equal(RunwayHoldChoice.IlsHold, choice);
    }

    [Fact]
    public void Default_IhsClosestToRunway_PicksIt()
    {
        // IHS LATER in the route than the HS: the latest hold of either type is
        // the full-length line here, and it happens to be ILS-typed.
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 8, hsIndex: 5, holdSeparationMeters: 35.0, preferIlsHold: false);
        Assert.Equal(8, at);
        Assert.Equal(RunwayHoldChoice.IlsHold, choice);
    }

    [Fact]
    public void Default_NoHoldNodes_ReturnsNone()
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: -1, hsIndex: -1, holdSeparationMeters: double.MaxValue, preferIlsHold: false);
        Assert.Equal(-1, at);
        Assert.Equal(RunwayHoldChoice.None, choice);
    }

    // ---- LVP mode (preferIlsHold == true): same-approach ILS hold honoured ----

    [Fact]
    public void Lvp_SameApproachIhsBehindHs_PicksIlsHold_Egkk()
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 5, hsIndex: 8, holdSeparationMeters: 35.0, preferIlsHold: true);
        Assert.Equal(5, at);
        Assert.Equal(RunwayHoldChoice.IlsHold, choice);
    }

    [Fact]
    public void Lvp_TransitIhsFarFromRunwayHold_RejectedByGate_Omdb()
    {
        // OMDB 30R via N12: taxiway N's ILS-critical-area IHS is ~620 m from
        // N12's real hold — even with LVP requested the transit IHS must lose,
        // or the pilot stops a whole taxiway early.
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 5, hsIndex: 8, holdSeparationMeters: 620.0, preferIlsHold: true);
        Assert.Equal(8, at);
        Assert.Equal(RunwayHoldChoice.PlainHold, choice);
    }

    [Theory]
    [InlineData(150.0, 5, RunwayHoldChoice.IlsHold)]   // exactly at the gate: honoured (<=)
    [InlineData(150.1, 8, RunwayHoldChoice.PlainHold)] // just past the gate: full-length
    public void Lvp_SameApproachGateBoundary(double sepM, int expectedAt, RunwayHoldChoice expectedChoice)
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 5, hsIndex: 8, holdSeparationMeters: sepM, preferIlsHold: true);
        Assert.Equal(expectedAt, at);
        Assert.Equal(expectedChoice, choice);
    }

    [Fact]
    public void Lvp_OnlyPlainHold_FallsBackToIt()
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: -1, hsIndex: 7, holdSeparationMeters: double.MaxValue, preferIlsHold: true);
        Assert.Equal(7, at);
        Assert.Equal(RunwayHoldChoice.PlainHold, choice);
    }

    [Fact]
    public void Lvp_OnlyIlsHold_PicksIt()
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 4, hsIndex: -1, holdSeparationMeters: double.MaxValue, preferIlsHold: true);
        Assert.Equal(4, at);
        Assert.Equal(RunwayHoldChoice.IlsHold, choice);
    }

    [Fact]
    public void Lvp_IhsClosestToRunway_PicksIt()
    {
        // The IHS is already the hold closest to the runway — the same-approach
        // gate is never consulted; the ILS hold is simply the pick.
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: 8, hsIndex: 5, holdSeparationMeters: 35.0, preferIlsHold: true);
        Assert.Equal(8, at);
        Assert.Equal(RunwayHoldChoice.IlsHold, choice);
    }

    [Fact]
    public void Lvp_NoHoldNodes_ReturnsNone()
    {
        var (at, choice) = RunwayHoldShortSelector.Select(
            ihsIndex: -1, hsIndex: -1, holdSeparationMeters: double.MaxValue, preferIlsHold: true);
        Assert.Equal(-1, at);
        Assert.Equal(RunwayHoldChoice.None, choice);
    }

    // ---- DescribeLvpOutcome: the route-summary feedback sentence ----
    //
    // Speech-first wording ("CAT three", not the roman numeral "III", which
    // screen readers mispronounce — same convention as the Taxi planner
    // checkbox's AccessibleName). Null when LVP wasn't requested: the default
    // route summary must stay byte-identical to pre-LVP behaviour.

    [Theory]
    [InlineData(RunwayHoldChoice.None)]
    [InlineData(RunwayHoldChoice.PlainHold)]
    [InlineData(RunwayHoldChoice.IlsHold)]
    public void Describe_NotRequested_ReturnsNull(RunwayHoldChoice choice)
    {
        Assert.Null(RunwayHoldShortSelector.DescribeLvpOutcome(preferIlsHold: false, choice));
    }

    [Fact]
    public void Describe_Honoured_ConfirmsCatThreeHold()
    {
        Assert.Equal(
            "Holding at the CAT three ILS hold.",
            RunwayHoldShortSelector.DescribeLvpOutcome(preferIlsHold: true, RunwayHoldChoice.IlsHold));
    }

    [Fact]
    public void Describe_FellBackToFullLength_SaysSo()
    {
        Assert.Equal(
            "No CAT three ILS hold found at this holding point. Holding at the full-length line.",
            RunwayHoldShortSelector.DescribeLvpOutcome(preferIlsHold: true, RunwayHoldChoice.PlainHold));
    }

    [Fact]
    public void Describe_NoHoldNodesAtAll_SaysSo()
    {
        // No marked hold nodes on the route (TruncateToHoldShort's synthetic
        // back-off may still stop the aircraft) — claiming "full-length line"
        // would be wrong, so this wording only reports the missing CAT III hold.
        Assert.Equal(
            "No CAT three ILS hold found before the runway.",
            RunwayHoldShortSelector.DescribeLvpOutcome(preferIlsHold: true, RunwayHoldChoice.None));
    }
}
