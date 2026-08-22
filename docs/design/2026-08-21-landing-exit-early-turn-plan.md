# Landing-Exit Early-Turn Handoff Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the landing-exit rollout from reading a wrong-way or far-from-the-exit heading drift as "the pilot has begun the exit turn", give the silent phase of the rollout a centreline-keeping tone, and never steer at a taxiway the aircraft is not already on.

**Architecture:** All four rules land as pure static functions in a new `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, following the existing `LandingExitDestination` / `RunwayVacateResolver` pattern, so the xUnit suite can pin every one. `TaxiGuidanceManager.Rollout.cs` and `TaxiGuidanceManager.cs` then call them. Tasks 1–4 are pure logic + tests; tasks 5–7 wire them in; task 8 is the changelog fragment.

**Tech Stack:** .NET 10, C# 13, Windows Forms, xUnit.

**Design doc:** [docs/design/2026-08-21-landing-exit-early-turn-design.md](2026-08-21-landing-exit-early-turn-design.md). Read it before starting.

## Global Constraints

- **Build the solution, never the bare csproj.** `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build MSFSBlindAssist/MSFSBlindAssist.csproj` defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path.
- **Run tests as:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- **Close MSFSBlindAssist before building.** The exe is file-locked while it runs (MSB3021).
- **Never commit directly to main.** The branch `fix/landing-exit-early-turn` already exists and is checked out.
- **No new logging paths.** Diagnostics in this area go through the existing `RolloutDiag(...)` helper only.
- **Distances:** `LandingExit.DistanceFromThresholdFeet`, `Runway.Width`, `Runway.Length` and `TaxiRouteSegment.PathWidth` are FEET. `SignedAlongRunwayMeters` / `AbsLateralFromRunwayMeters` / `TaxiGraph.PerpendicularDistanceMetersStatic` return METRES. `METERS_TO_FEET` is already defined in `TaxiGuidanceManager`.
- **Do not touch** `LandingExitDestination.cs`, `RunwayVacateResolver.cs`, `TryEarlyExitHandoff`, `UpdateRunwayEndCountdown`, or the backtrack-departure path.
- **Every `[Fact]` gets a comment naming what it pins.** This suite is characterization-style; read `tests/MSFSBlindAssist.Tests/RunwayVacateResolverTests.cs` for the house style before writing tests.

---

### Task 1: `RolloutExitGate` skeleton and the tone-mode selector

**Files:**
- Create: `MSFSBlindAssist/Navigation/RolloutExitGate.cs`
- Test: `tests/MSFSBlindAssist.Tests/RolloutToneModeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MSFSBlindAssist.Navigation.RolloutToneMode` (enum: `Silent`, `DriftCorrection`, `ExitBearing`); `MSFSBlindAssist.Navigation.RolloutExitGate.SelectToneMode(double groundSpeedKts, double distToExitFeet) -> RolloutToneMode`; and the public constants `ToneActiveBelowGroundSpeedKts`, `ExitToneArmFeet`, `TurnBegunHeadingDeg`, `TurnMaxGroundSpeedKts`, `TurnWindowFeet`, `ExitSideMinBearingDeg`, `DriftToneSilentDeg`, `DriftToneActivationDeg`, `DriftToneMaxPanDeg`, `EarlyVacateForwardSlackFeet`, `EarlyVacateMaxPassedFeet`, `HandoffReachMarginM`, `HandoffReachDefaultHalfWidthM` — all `public const double`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/RolloutToneModeTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.SelectToneMode — which of the three
// rollout steering-tone behaviours applies on a given frame.
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The rollout tone is silent until the
// aircraft is within 300 ft of the selected exit. At 2,232 ft to go the pilot had no
// cue at all while drifting 15° off the centreline, and the tone's FIRST utterance
// after the handoff was a 79° hard pan. DriftCorrection fills that silent gap.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutToneModeTests
{
    // Above 50 kt the tone stays silent regardless of distance: the existing comment
    // in TaxiGuidanceManager.Rollout.cs warns that autopilot crab / crosswind
    // alignment produces confusing pan during the high-speed phase.
    [Fact]
    public void AboveFiftyKnots_IsSilent()
    {
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(50.1, 2232.0));
        Assert.Equal(RolloutToneMode.Silent, RolloutExitGate.SelectToneMode(149.1, 100.0));
    }

    // At or below 50 kt and inside the 300 ft arm distance the exit-bearing tone owns
    // the frame — today's behaviour, unchanged.
    [Fact]
    public void InsideArmDistance_IsExitBearing()
    {
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(50.0, 300.0));
        Assert.Equal(RolloutToneMode.ExitBearing, RolloutExitGate.SelectToneMode(19.7, 12.0));
    }

    // The gap this fix exists to fill: slowed down, but the exit is still far away.
    [Fact]
    public void BelowFiftyKnotsAndOutsideArmDistance_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(29.7, 2349.0));
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(50.0, 300.1));
    }

    // KSEA regression: 19.7 kt, 2,232 ft to go — the exact frame the old handoff fired
    // on. The pilot must have had a drift-correction tone here, not silence.
    [Fact]
    public void Ksea34L_AtTheOldHandoffFrame_IsDriftCorrection()
    {
        Assert.Equal(RolloutToneMode.DriftCorrection, RolloutExitGate.SelectToneMode(19.7, 2232.0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~RolloutToneModeTests
```

Expected: build failure — `The type or namespace name 'RolloutToneMode' could not be found`.

- [ ] **Step 3: Write the minimal implementation**

Create `MSFSBlindAssist/Navigation/RolloutExitGate.cs`:

```csharp
namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which steering-tone behaviour applies on a landing-rollout frame.
/// </summary>
public enum RolloutToneMode
{
    /// <summary>Tone paused — too fast for a heading cue to mean anything.</summary>
    Silent,
    /// <summary>Steer back to the runway heading. Owns the long silent middle of the rollout.</summary>
    DriftCorrection,
    /// <summary>Steer at the exit junction. Owns the last 300 ft before the exit.</summary>
    ExitBearing
}

/// <summary>
/// The pure decision rules of the landing-exit rollout: when the steering tone speaks
/// and what it steers at, whether a heading deviation is the exit turn, which exit the
/// pilot actually vacated at, and whether a handoff route is one the aircraft can reach.
///
/// <para>Deliberately free of SimConnect, form and graph dependencies so the whole set is
/// unit-testable, following <see cref="LandingExitDestination"/> and
/// <see cref="RunwayVacateResolver"/>. <c>TaxiGuidanceManager</c> supplies the geometry.</para>
///
/// <para>Origin: KSEA ILS 34L, 2026-08-21. A 15.1° LEFT drift at 19.7 kt, 2,232 ft short of
/// the selected exit — on a runway whose every mapped exit is to the RIGHT — satisfied a
/// turn gate that tested only <c>Math.Abs(headingDelta) >= 15</c>. See
/// docs/design/2026-08-21-landing-exit-early-turn-design.md.</para>
/// </summary>
public static class RolloutExitGate
{
    // ---- Tone gating (values moved here from TaxiGuidanceManager so there is one source
    // ---- of truth; the private consts there now initialise from these).

    /// <summary>Above this ground speed the rollout tone is silent — crab/crosswind pan.</summary>
    public const double ToneActiveBelowGroundSpeedKts = 50.0;

    /// <summary>Distance to the exit at which the exit-bearing tone takes over.</summary>
    public const double ExitToneArmFeet = 300.0;

    // ---- Drift-correction tone thresholds.

    /// <summary>
    /// Below this heading deviation the drift tone is silent. 2.0° is the codebase's
    /// existing floor for a heading deviation that means anything — see the
    /// <c>Math.Max(2.0, ExitAngleDegrees * 0.7)</c> term in <c>alignedWithExit</c>.
    /// Cross-check against the KSEA capture: the normal rollout phase ran at 0.4–1.7°
    /// throughout, and the drift episode read 6.1° then 14.4°.
    /// </summary>
    public const double DriftToneSilentDeg = 2.0;

    /// <summary>One degree above the silent floor — the tone is fully active here.</summary>
    public const double DriftToneActivationDeg = 3.0;

    /// <summary>Full-pan saturation. Matches every other steering tone in the rollout file.</summary>
    public const double DriftToneMaxPanDeg = 15.0;

    // ---- Exit-turn gating.

    /// <summary>Heading deviation from the runway that counts as an exit turn.</summary>
    public const double TurnBegunHeadingDeg = 15.0;

    /// <summary>Above this ground speed a heading deviation is touchdown yaw, not a turn.</summary>
    public const double TurnMaxGroundSpeedKts = 90.0;

    /// <summary>
    /// How close to the exit a turn must begin to count as taking it.
    ///
    /// <para>Derived, not fitted. An exit node can sit forward of its actual pavement
    /// junction by up to <c>lateralTolerance / tan(exitAngle)</c>, where lateralTolerance is
    /// <c>halfWidth + 15 m</c> (see <c>TaxiGraph.GetLandingExits</c>). This gate can only fire
    /// for an exit the aircraft can deviate 15° onto, so exitAngle ≥ 15°; the worst case is a
    /// 200 ft runway: (30.5 + 15) / tan(15°) = 170 m = 558 ft. Add the app's own notion of
    /// "at the exit" — the 300 ft tone-arm distance plus the 150 ft "turn now" cue — for
    /// 858 ft, rounded to 1,000.</para>
    ///
    /// <para>Do NOT tighten this to <c>ROLLOUT_NEAR_EXIT_FT</c> (500): that would block
    /// legitimate turns at shallow-RET airports whose exits derive from hold-short nodes.</para>
    /// </summary>
    public const double TurnWindowFeet = 1000.0;

    /// <summary>
    /// Below this relative bearing an exit has no meaningful side and the direction test is
    /// skipped. Matches the existing <c>ExitAngleDegrees >= 3.0</c> gate in
    /// <c>alignedWithExit</c>: below 3° an exit is geometrically indistinguishable from
    /// straight ahead. <c>ExitBearingTrue == 0.0</c> — the "unknown" sentinel used throughout
    /// the rollout code — normalises into this band, which is the intended degradation.
    /// </summary>
    public const double ExitSideMinBearingDeg = 3.0;

    // ---- Early-vacate matching.

    /// <summary>
    /// How far AHEAD of the aircraft an exit node may read and still count as one the pilot
    /// has already reached. Same 558 ft node-displacement figure as
    /// <see cref="TurnWindowFeet"/>, rounded: a hold-short-marker exit node can read forward
    /// of the pavement junction the pilot actually turned at.
    /// </summary>
    public const double EarlyVacateForwardSlackFeet = 600.0;

    /// <summary>
    /// How far BEHIND the aircraft an exit may be and still be the one vacated at. This is
    /// the same value as <c>EXIT_COVERAGE_GAP_FT</c> in <c>TaxiGraph.GetLandingExits</c>,
    /// which that comment records as measured across 266 runway directions at 39 airports as
    /// the distance beyond which two nodes stop describing the same physical turnoff. That
    /// constant is method-local and cannot be referenced; keep the two in step.
    /// </summary>
    public const double EarlyVacateMaxPassedFeet = 1400.0;

    // ---- Handoff route reachability.

    /// <summary>
    /// Buffer added to a taxiway's half-width before refusing a handoff route. Reuses the
    /// same 15 m that <c>lateralToleranceM</c> in <c>TaxiGraph.GetLandingExits</c> adds to a
    /// runway half-width for "geometrically within this corridor".
    /// </summary>
    public const double HandoffReachMarginM = 15.0;

    /// <summary>
    /// Half-width assumed when a segment carries no <c>PathWidth</c>. Deliberately generous:
    /// this guard ENDS guidance, so missing navdata width must never cause a false refusal.
    /// </summary>
    public const double HandoffReachDefaultHalfWidthM = 25.0;

    /// <summary>
    /// Which steering-tone behaviour applies this frame.
    ///
    /// <para><see cref="RolloutToneMode.Silent"/> and <see cref="RolloutToneMode.ExitBearing"/>
    /// reproduce the pre-2026-08 behaviour exactly.
    /// <see cref="RolloutToneMode.DriftCorrection"/> is new and fills the gap that was silent:
    /// slowed down, but the exit is still far away.</para>
    /// </summary>
    public static RolloutToneMode SelectToneMode(double groundSpeedKts, double distToExitFeet)
    {
        if (groundSpeedKts > ToneActiveBelowGroundSpeedKts) return RolloutToneMode.Silent;
        if (distToExitFeet <= ExitToneArmFeet) return RolloutToneMode.ExitBearing;
        return RolloutToneMode.DriftCorrection;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~RolloutToneModeTests
```

Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs tests/MSFSBlindAssist.Tests/RolloutToneModeTests.cs
git commit -m "feat(landing-exit): rollout tone-mode selector with a drift-correction mode"
```

---

### Task 2: The signed, distance-gated exit-turn test

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (append two methods)
- Test: `tests/MSFSBlindAssist.Tests/RolloutExitTurnGateTests.cs`

**Interfaces:**
- Consumes: the constants from Task 1.
- Produces: `RolloutExitGate.IsExitTurnBegun(double headingDeltaSignedDeg, double groundSpeedKts, double distToExitFeet, bool pastExit, double exitRelativeBearingDeg) -> bool` and `RolloutExitGate.IsTurnTowardExit(double headingDeltaSignedDeg, double exitRelativeBearingDeg) -> bool`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/RolloutExitTurnGateTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.IsExitTurnBegun — "has the pilot begun the
// turn onto the selected exit?".
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The old gate was
// `hdgDeltaAbs >= 15 && gs < 90` — an ABSOLUTE heading deviation with no reference to
// where the exit is or how far away it lies. A 15.1° LEFT drift at 19.7 kt, 2,232 ft
// short of an exit that lies 13.6° to the RIGHT, satisfied it.
//
// Sign convention: headingDelta and exitRelativeBearing are both signed relative to the
// runway heading, POSITIVE = right. So KSEA's drift is -15.1 and exit Z is +13.6.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutExitTurnGateTests
{
    // THE regression. Wrong side AND far outside the turn window — either alone is
    // disqualifying, and this case has both.
    [Fact]
    public void Ksea34L_LeftDriftTowardsARightHandExit_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: -15.1,
            groundSpeedKts: 19.7,
            distToExitFeet: 2232.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // The ordinary case this gate exists for: a hard turn onto a right-hand exit,
    // at the exit, at taxi speed.
    [Fact]
    public void RightTurnAtARightHandExit_IsATurn()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: 16.0,
            groundSpeedKts: 20.0,
            distToExitFeet: 150.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // Same turn, same exit, but 2,232 ft short of it. You cannot be turning onto an
    // exit that is still 2,232 ft away — this is what the window rejects.
    [Fact]
    public void RightTurnFarShortOfTheExit_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(
            headingDeltaSignedDeg: 16.0,
            groundSpeedKts: 20.0,
            distToExitFeet: 2232.0,
            pastExit: false,
            exitRelativeBearingDeg: 13.6));
    }

    // Window boundary: 1,000 ft is derived from a 558 ft worst-case exit-node
    // displacement plus the app's own 450 ft "at the exit" range. Inclusive.
    [Fact]
    public void TurnWindowBoundaryIsInclusiveAtOneThousandFeet()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 1000.0, false, 13.6));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 1000.1, false, 13.6));
    }

    // pastExit bypasses the window entirely: an overshooting aircraft is beyond the
    // exit, so distance-to-exit is growing and would fail the window forever.
    [Fact]
    public void PastExit_BypassesTheDistanceWindow()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 5000.0, true, 13.6));
    }

    // Left-hand exits are the mirror image; nothing here is right-hand-specific.
    [Fact]
    public void LeftTurnAtALeftHandExit_IsATurn()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, -30.0));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 150.0, false, -30.0));
    }

    // The existing 15° and 90 kt gates are unchanged.
    [Fact]
    public void BelowFifteenDegreesOrAboveNinetyKnots_IsNotATurn()
    {
        Assert.False(RolloutExitGate.IsExitTurnBegun(14.9, 20.0, 150.0, false, 13.6));
        Assert.True(RolloutExitGate.IsExitTurnBegun(15.0, 20.0, 150.0, false, 13.6));
        Assert.False(RolloutExitGate.IsExitTurnBegun(16.0, 90.0, 150.0, false, 13.6));
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 89.9, 150.0, false, 13.6));
    }

    // ExitBearingTrue == 0.0 is the rollout code's "unknown bearing" sentinel and
    // normalises into the sub-3° band. Unknown side must NOT block the handoff —
    // degrade to the old direction-blind behaviour rather than stranding the pilot.
    [Fact]
    public void UnknownExitSide_SkipsTheDirectionTest()
    {
        Assert.True(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, 0.0));
        Assert.True(RolloutExitGate.IsExitTurnBegun(16.0, 20.0, 150.0, false, 2.9));
        // At 3.0° the exit has a side again and the wrong-way turn is rejected.
        Assert.False(RolloutExitGate.IsExitTurnBegun(-16.0, 20.0, 150.0, false, 3.0));
    }

    // IsTurnTowardExit is exposed separately for the post-handoff overshoot monitor,
    // which needs the direction test WITHOUT the distance window.
    [Fact]
    public void IsTurnTowardExit_IsDirectionOnly()
    {
        Assert.True(RolloutExitGate.IsTurnTowardExit(20.0, 13.6));
        Assert.False(RolloutExitGate.IsTurnTowardExit(-20.0, 13.6));
        Assert.True(RolloutExitGate.IsTurnTowardExit(-20.0, -13.6));
        Assert.True(RolloutExitGate.IsTurnTowardExit(-20.0, 0.0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~RolloutExitTurnGateTests
```

Expected: build failure — `'RolloutExitGate' does not contain a definition for 'IsExitTurnBegun'`.

- [ ] **Step 3: Write the minimal implementation**

Append to `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, inside the class:

```csharp
    /// <summary>
    /// Has the pilot begun the turn onto the selected exit?
    ///
    /// <para>Every argument is signed relative to the runway heading, POSITIVE = RIGHT.</para>
    ///
    /// <para>The direction and distance clauses are the 2026-08 fix. The gate used to be
    /// <c>Math.Abs(headingDelta) >= 15 &amp;&amp; gs &lt; 90</c>, which at KSEA 34L read a
    /// 15.1° LEFT deceleration drift, 2,232 ft short of an exit lying 13.6° to the RIGHT,
    /// as the exit turn. The handoff that followed pointed the steering tone at a graph node
    /// 54 m away and 17.8 m outside the runway edge.</para>
    ///
    /// <para>A genuine early turn-off at a DIFFERENT exit is not this method's job and is not
    /// lost by tightening it: <c>exitedLaterally</c> catches that from position, which no
    /// heading test can fake.</para>
    /// </summary>
    /// <param name="exitRelativeBearingDeg">
    /// <c>NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)</c>. The
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel lands inside
    /// <see cref="ExitSideMinBearingDeg"/> and disables the direction test.
    /// </param>
    public static bool IsExitTurnBegun(
        double headingDeltaSignedDeg,
        double groundSpeedKts,
        double distToExitFeet,
        bool pastExit,
        double exitRelativeBearingDeg)
    {
        if (Math.Abs(headingDeltaSignedDeg) < TurnBegunHeadingDeg) return false;
        if (groundSpeedKts >= TurnMaxGroundSpeedKts) return false;
        if (!pastExit && distToExitFeet > TurnWindowFeet) return false;
        return IsTurnTowardExit(headingDeltaSignedDeg, exitRelativeBearingDeg);
    }

    /// <summary>
    /// Is a heading deviation on the same side as the exit?
    ///
    /// <para>Exposed separately from <see cref="IsExitTurnBegun"/> because the post-handoff
    /// overshoot monitor needs the direction test WITHOUT the distance window — it runs when
    /// the aircraft is already near or past the exit, where a window would be wrong.</para>
    ///
    /// <para>Returns true when the exit has no meaningful side, so an unknown bearing degrades
    /// to the old direction-blind behaviour rather than stranding the pilot. Callers always
    /// pass a deviation of at least <see cref="TurnBegunHeadingDeg"/>, so
    /// <c>Math.Sign(headingDeltaSignedDeg)</c> is never zero here.</para>
    /// </summary>
    public static bool IsTurnTowardExit(double headingDeltaSignedDeg, double exitRelativeBearingDeg)
    {
        if (Math.Abs(exitRelativeBearingDeg) < ExitSideMinBearingDeg) return true;
        return Math.Sign(headingDeltaSignedDeg) == Math.Sign(exitRelativeBearingDeg);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~RolloutExitTurnGateTests
```

Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs tests/MSFSBlindAssist.Tests/RolloutExitTurnGateTests.cs
git commit -m "feat(landing-exit): exit-turn gate now tests turn direction and proximity"
```

---

### Task 3: Early-vacate exit matching

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (append one method)
- Test: `tests/MSFSBlindAssist.Tests/EarlyVacateExitMatcherTests.cs`

**Interfaces:**
- Consumes: the constants from Task 1; `MSFSBlindAssist.Navigation.LandingExit` (fields used: `NodeId`, `ExitSide`, `TaxiwayName`).
- Produces: `RolloutExitGate.MatchEarlyVacateExit(IReadOnlyList<LandingExit> allExits, LandingExit plannedExit, Func<LandingExit, double> signedAlongPastFeet, double aircraftLateralSignedMetres) -> LandingExit?`.

**Why a `Func` and not coordinates:** the caller owns the geometry. Passing precomputed along-track values keeps this method pure and — critically — means no threshold reference is involved. See the design doc's displaced-threshold note; do not reintroduce a comparison against `DistanceFromThresholdFeet`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/EarlyVacateExitMatcherTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.MatchEarlyVacateExit — "which exit did the
// pilot actually turn onto?" when the handoff fires away from the planned one.
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The handoff re-routed to the PLANNED
// exit's node even though the aircraft had left the runway 2,232 ft short of it. With no
// runway edges in the taxi graph, A* produced the only route that exists between the two:
// 1,678 m up the east-side parallel taxiway T and back down Z toward the runway.
//
// signedAlongPast is POSITIVE when the aircraft is PAST that exit. Sign of the lateral
// argument is POSITIVE = right of the runway direction, matching ExitSide "Right".

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class EarlyVacateExitMatcherTests
{
    private static LandingExit Exit(int nodeId, string name, string side) => new LandingExit
    {
        NodeId = nodeId,
        TaxiwayName = name,
        ExitSide = side
    };

    // KSEA 34L, as flown. Planned exit Z; the aircraft vacated right, 810 ft past J's
    // throat. E is 800 ft AHEAD, beyond the 600 ft forward slack, so it is excluded even
    // though it is geometrically nearer in a straight line. N is 1,452 ft behind, beyond
    // the 1,400 ft cap.
    [Fact]
    public void Ksea34L_PicksTheLastExitActuallyPassed()
    {
        var q = Exit(1, "Q", "Right");
        var p = Exit(2, "P", "Right");
        var n = Exit(3, "N", "Right");
        var j = Exit(4, "J", "Right");
        var e = Exit(5, "E", "Right");
        var z = Exit(6, "Z", "Right");
        var all = new[] { q, p, n, j, e, z };

        var passed = new Dictionary<int, double>
        {
            [1] = 3550.0, [2] = 2625.0, [3] = 1452.0, [4] = 810.0, [5] = -800.0, [6] = -2232.0
        };

        var match = RolloutExitGate.MatchEarlyVacateExit(
            all, z, ex => passed[ex.NodeId], aircraftLateralSignedMetres: 51.2);

        Assert.Same(j, match);
    }

    // You cannot vacate at an exit you have not reached. An exit further ahead than the
    // forward slack is never a candidate.
    [Fact]
    public void AnExitStillAhead_IsNotACandidate()
    {
        var ahead = Exit(1, "E", "Right");
        var planned = Exit(2, "Z", "Right");
        var passedM = new Dictionary<int, double> { [1] = -800.0, [2] = -2232.0 };

        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { ahead, planned }, planned, ex => passedM[ex.NodeId], 51.2));
    }

    // The forward slack exists because a hold-short-marker exit node can read forward of
    // the pavement junction the pilot actually turned at. 600 ft is the boundary.
    [Fact]
    public void ForwardSlackBoundaryIsSixHundredFeet()
    {
        var near = Exit(1, "J", "Right");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(near, RolloutExitGate.MatchEarlyVacateExit(
            new[] { near, planned }, planned, _ => -600.0, 51.2));
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { near, planned }, planned, _ => -600.1, 51.2));
    }

    // Beyond 1,400 ft behind, an exit is no longer the same physical turnoff.
    [Fact]
    public void MaxPassedBoundaryIsFourteenHundredFeet()
    {
        var behind = Exit(1, "N", "Right");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(behind, RolloutExitGate.MatchEarlyVacateExit(
            new[] { behind, planned }, planned, _ => 1400.0, 51.2));
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { behind, planned }, planned, _ => 1400.1, 51.2));
    }

    // A runway with exits on both sides: the side the aircraft actually moved to decides.
    [Fact]
    public void ExitsOnBothSides_TheAircraftsOwnSideWins()
    {
        var right = Exit(1, "J", "Right");
        var left = Exit(2, "K", "Left");
        var planned = Exit(3, "Z", "Right");
        var all = new[] { right, left, planned };
        var passedM = new Dictionary<int, double> { [1] = 810.0, [2] = 700.0, [3] = -2232.0 };

        Assert.Same(left, RolloutExitGate.MatchEarlyVacateExit(
            all, planned, ex => passedM[ex.NodeId], aircraftLateralSignedMetres: -51.2));
        Assert.Same(right, RolloutExitGate.MatchEarlyVacateExit(
            all, planned, ex => passedM[ex.NodeId], aircraftLateralSignedMetres: 51.2));
    }

    // A blank ExitSide (bearing unknown at graph-build time) must not be excluded —
    // it is ranked on distance alone. Excluding it would strand the pilot at exactly
    // the airports whose navdata is already thin.
    [Fact]
    public void BlankExitSide_IsRankedNotRejected()
    {
        var blank = Exit(1, "J", "");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(blank, RolloutExitGate.MatchEarlyVacateExit(
            new[] { blank, planned }, planned, _ => 810.0, 51.2));
    }

    // The planned exit is never its own early-vacate match.
    [Fact]
    public void ThePlannedExit_IsNeverTheMatch()
    {
        var planned = Exit(1, "Z", "Right");

        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { planned }, planned, _ => 100.0, 51.2));
    }

    // Nearest wins when several qualify — the last one reached, not the first in the list.
    [Fact]
    public void NearestQualifyingExitWins()
    {
        var far = Exit(1, "N", "Right");
        var near = Exit(2, "J", "Right");
        var planned = Exit(3, "Z", "Right");
        var passedM = new Dictionary<int, double> { [1] = 1300.0, [2] = 200.0, [3] = -2232.0 };

        Assert.Same(near, RolloutExitGate.MatchEarlyVacateExit(
            new[] { far, near, planned }, planned, ex => passedM[ex.NodeId], 51.2));
    }

    // Empty and null inputs degrade to "no match", which the caller turns into a spoken
    // closure rather than a route.
    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        var planned = Exit(1, "Z", "Right");
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            Array.Empty<LandingExit>(), planned, _ => 100.0, 51.2));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~EarlyVacateExitMatcherTests
```

Expected: build failure — `'RolloutExitGate' does not contain a definition for 'MatchEarlyVacateExit'`.

- [ ] **Step 3: Write the minimal implementation**

Append to `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, inside the class:

```csharp
    /// <summary>
    /// Which exit did the pilot actually turn onto, when the handoff fires away from the
    /// planned one?
    ///
    /// <para>Returns null when nothing qualifies. The caller must then CONCLUDE exit guidance
    /// with a spoken closure — never fall back to the planned exit. At KSEA 34L that fallback
    /// produced a 1,678 m route up the parallel taxiway and back down toward the runway,
    /// because the taxi graph carries no runway edges and that is the only path between the
    /// two exits.</para>
    ///
    /// <para>Selection is "the last exit actually reached": you cannot vacate at an exit you
    /// have not got to yet. <see cref="EarlyVacateForwardSlackFeet"/> of tolerance allows for
    /// an exit node that reads forward of its own pavement junction.</para>
    /// </summary>
    /// <param name="signedAlongPastFeet">
    /// Along-runway distance from each exit to the aircraft, in FEET, POSITIVE when the
    /// aircraft is PAST that exit. Supplied by the caller — usually
    /// <c>SignedAlongRunwayMeters(aircraftLat, aircraftLon, exit.Latitude, exit.Longitude,
    /// runwayHeadingTrue) * METERS_TO_FEET</c>.
    ///
    /// Measured PER EXIT and never against a threshold, which is what makes this immune to
    /// displaced thresholds. <c>LandingExit.DistanceFromThresholdFeet</c> is measured from the
    /// LANDING threshold including <c>ThresholdOffset</c> (KJFK 13R 2,055 ft, KJFK 22R
    /// 3,438 ft, EGLL 27R 1,004 ft), while the natural way to compute an aircraft's
    /// along-runway position measures from the physical runway start; comparing the two picks
    /// the wrong exit at every displaced-threshold runway. Do not reintroduce it.
    /// </param>
    /// <param name="aircraftLateralSignedMetres">
    /// Signed lateral offset of the aircraft from the runway axis, POSITIVE = right of the
    /// runway direction, matching <c>LandingExit.ExitSide</c> == "Right".
    /// </param>
    public static LandingExit? MatchEarlyVacateExit(
        IReadOnlyList<LandingExit> allExits,
        LandingExit plannedExit,
        Func<LandingExit, double> signedAlongPastFeet,
        double aircraftLateralSignedMetres)
    {
        if (allExits == null || plannedExit == null || signedAlongPastFeet == null) return null;

        string side = aircraftLateralSignedMetres >= 0.0 ? "Right" : "Left";

        LandingExit? best = null;
        double bestRank = double.MaxValue;

        foreach (var candidate in allExits)
        {
            if (candidate == null) continue;
            if (candidate.NodeId == plannedExit.NodeId) continue;

            // A blank ExitSide means the graph could not determine a side, NOT that the exit
            // is on the wrong one. Rank it on distance instead of dropping it — excluding it
            // would strand the pilot at exactly the airports whose navdata is already thin.
            if (candidate.ExitSide.Length > 0
                && !string.Equals(candidate.ExitSide, side, StringComparison.OrdinalIgnoreCase))
                continue;

            double passed = signedAlongPastFeet(candidate);
            if (passed < -EarlyVacateForwardSlackFeet) continue;  // still ahead of the aircraft
            if (passed > EarlyVacateMaxPassedFeet) continue;      // too far behind to be this turnoff

            double rank = Math.Abs(passed);
            if (best == null || rank < bestRank)
            {
                best = candidate;
                bestRank = rank;
            }
        }

        return best;
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~EarlyVacateExitMatcherTests
```

Expected: `Passed! - Failed: 0, Passed: 9`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs tests/MSFSBlindAssist.Tests/EarlyVacateExitMatcherTests.cs
git commit -m "feat(landing-exit): match the exit the pilot actually vacated at"
```

---

### Task 4: Handoff route reachability

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (append one method)
- Test: `tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs`

**Interfaces:**
- Consumes: the constants from Task 1.
- Produces: `RolloutExitGate.IsHandoffRouteReachable(bool aircraftOffRunway, double crossTrackToFirstSegmentMetres, double firstSegmentPathWidthFeet) -> bool`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.IsHandoffRouteReachable — "is the route the
// handoff just built one the aircraft is actually on?".
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The handoff re-route's first segment lay on
// taxiway J's diagonal, 53.9 m of cross-track away, with the aircraft 17.8 m outside the
// runway's east edge. The steering tone — silent until that instant — panned hard right at
// 79° and the pilot followed it across ~60 m of unmapped ground.
//
// This tests proximity to the TARGET TAXIWAY, not the presence of pavement. Navdata carries
// only runway and taxi_path polygons and cannot prove there is asphalt underfoot.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HandoffRouteReachabilityTests
{
    private const double JWidthFt = 82.0;   // KSEA taxiway J — half-width 12.5 m

    // A handoff taken while still on the runway is the normal case for every exit type
    // and is never refused, however far the first segment is.
    [Fact]
    public void OnTheRunway_IsAlwaysReachable()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(
            aircraftOffRunway: false, crossTrackToFirstSegmentMetres: 200.0,
            firstSegmentPathWidthFeet: JWidthFt));
    }

    // KSEA regression: off the runway, 53.9 m from an 82 ft segment. Threshold is
    // 12.5 + 15 = 27.5 m, so this is refused and guidance concludes instead of panning.
    [Fact]
    public void Ksea34L_OffTheRunwayAndFiftyFourMetresFromTaxiwayJ_IsNotReachable()
    {
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(
            aircraftOffRunway: true, crossTrackToFirstSegmentMetres: 53.9,
            firstSegmentPathWidthFeet: JWidthFt));
    }

    // Already on the exit taxiway — the ordinary early-vacate case that must keep working.
    [Fact]
    public void OffTheRunwayButOnTheTaxiway_IsReachable()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 5.0, JWidthFt));
    }

    // Boundary: half-width (12.5 m) + margin (15 m) = 27.5 m, inclusive.
    [Fact]
    public void BoundaryIsHalfWidthPlusFifteenMetres()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 27.5, JWidthFt));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, 27.6, JWidthFt));
    }

    // Missing PathWidth falls back to a GENEROUS 25 m half-width. This guard ENDS
    // guidance, so thin navdata must never cause a false refusal.
    [Fact]
    public void MissingPathWidth_UsesTheGenerousFallback()
    {
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 40.0, 0.0));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, 40.1, 0.0));
        // Negative width is treated the same as absent.
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, 40.0, -1.0));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~HandoffRouteReachabilityTests
```

Expected: build failure — `'RolloutExitGate' does not contain a definition for 'IsHandoffRouteReachable'`.

- [ ] **Step 3: Write the minimal implementation**

Append to `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, inside the class:

```csharp
    /// <summary>
    /// Is the route the handoff just built one the aircraft can actually follow from where
    /// it is standing?
    ///
    /// <para>Refusing means CONCLUDING exit guidance with a spoken closure, which is why the
    /// test is deliberately permissive: an on-runway handoff is never refused, and a segment
    /// with no width gets a generous fallback.</para>
    ///
    /// <para>This tests proximity to the TARGET TAXIWAY, not the presence of pavement. Navdata
    /// carries only runway and taxi_path polygons and cannot prove there is asphalt underfoot.
    /// What it does guarantee is that the steering tone is never pointed at a taxiway the
    /// aircraft is not essentially already on — the KSEA 34L failure, where the tone panned
    /// 79° right at a segment 53.9 m away with the aircraft 17.8 m outside the runway edge.</para>
    /// </summary>
    /// <param name="crossTrackToFirstSegmentMetres">
    /// Distance from the aircraft to the nearest point ON the route's first segment (clamped
    /// to the segment, so endpoints count) — <c>TaxiGraph.PerpendicularDistanceMetersStatic</c>.
    /// </param>
    public static bool IsHandoffRouteReachable(
        bool aircraftOffRunway,
        double crossTrackToFirstSegmentMetres,
        double firstSegmentPathWidthFeet)
    {
        if (!aircraftOffRunway) return true;

        double halfWidthM = firstSegmentPathWidthFeet > 0.0
            ? firstSegmentPathWidthFeet * 0.3048 * 0.5
            : HandoffReachDefaultHalfWidthM;

        return crossTrackToFirstSegmentMetres <= halfWidthM + HandoffReachMarginM;
    }
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FullyQualifiedName~HandoffRouteReachabilityTests
```

Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs
git commit -m "feat(landing-exit): refuse a handoff route the aircraft is not on"
```

---

### Task 5: Wire the drift-correction tone into the rollout

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (constants ~965–990, field ~826)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` (~24, ~806–892)

**Interfaces:**
- Consumes: `RolloutExitGate.SelectToneMode`, `RolloutToneMode`, and the drift-tone constants from Task 1.
- Produces: nothing new for later tasks.

There is no unit test for this task — it mutates `TaxiGuidanceManager`, which owns SimConnect state and cannot be constructed in the test suite. The decision logic it calls is already pinned by Task 1. Verification is a clean build plus the full suite still passing.

- [ ] **Step 1: Point the existing constants at the new single source of truth**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, replace the two existing literals so `RolloutExitGate` is the only place the values live (a `const` may initialise from another `const`, so no call site changes):

```csharp
    private const double ROLLOUT_EXIT_TONE_ARM_FT = Navigation.RolloutExitGate.ExitToneArmFeet;
    private const double ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS = Navigation.RolloutExitGate.ToneActiveBelowGroundSpeedKts;
    private const double ROLLOUT_TURN_BEGAN_HDG_DEG = Navigation.RolloutExitGate.TurnBegunHeadingDeg;
    private const double ROLLOUT_TURN_MAX_GS_KTS = Navigation.RolloutExitGate.TurnMaxGroundSpeedKts;
```

Keep each constant's existing XML/`//` comment above it.

- [ ] **Step 2: Replace the `_rolloutExitToneArmed` latch with a tone-mode field**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` around line 826, delete:

```csharp
    private bool _rolloutExitToneArmed = false;
```

and add in its place:

```csharp
    // Which steering-tone behaviour the last rollout frame used. A change resets the
    // heading-error smoother so a DriftCorrection residual never leaks into the sharp
    // exit-bearing pan, and vice versa. Replaces the old _rolloutExitToneArmed latch,
    // which reset the smoother on exit-tone entry only — the drift tone needs the same
    // treatment in both directions.
    private Navigation.RolloutToneMode _rolloutToneMode = Navigation.RolloutToneMode.Silent;
```

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` at line 24, replace:

```csharp
        _rolloutExitToneArmed = false;
```

with:

```csharp
        _rolloutToneMode = Navigation.RolloutToneMode.Silent;
```

- [ ] **Step 3: Add the drift-tone constants**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, immediately after `ROLLOUT_EXIT_TONE_MAX_PAN_DEG` (~line 975):

```csharp
    // Drift-correction tone thresholds — the rollout phase that used to be silent.
    // See Navigation/RolloutExitGate for where 2.0 comes from (it is the codebase's
    // existing floor for a meaningful heading deviation) and why the max pan matches
    // every other steering tone rather than inventing a wider one.
    private const double ROLLOUT_DRIFT_TONE_SILENT_DEG = Navigation.RolloutExitGate.DriftToneSilentDeg;
    private const double ROLLOUT_DRIFT_TONE_ACTIVATION_DEG = Navigation.RolloutExitGate.DriftToneActivationDeg;
    private const double ROLLOUT_DRIFT_TONE_MAX_PAN_DEG = Navigation.RolloutExitGate.DriftToneMaxPanDeg;
```

- [ ] **Step 4: Restructure the tone block**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, replace the whole `if (groundSpeedKts > ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS) { … } else if (distToExitFeet <= ROLLOUT_EXIT_TONE_ARM_FT) { … } else { _steeringTone.Pause(); }` chain (starting at the `if` after the long "Rollout steering tone — exit-only design" comment block, ending at the closing brace of the final `else`) with:

```csharp
        var toneMode = Navigation.RolloutExitGate.SelectToneMode(groundSpeedKts, distToExitFeet);
        if (toneMode != _rolloutToneMode)
        {
            // Start every mode from a clean filter so the pan is sharp and immediate rather
            // than ramping out of the previous mode's residual.
            _headingErrorInitialized = false;
            _rolloutToneMode = toneMode;
        }

        if (toneMode == Navigation.RolloutToneMode.Silent)
        {
            _steeringTone.Pause();
            _headingErrorInitialized = false;
        }
        else
        {
            double desiredHeading;
            double toneSilentDeg;
            double toneActivationDeg;
            double toneMaxPanDeg;

            if (toneMode == Navigation.RolloutToneMode.ExitBearing)
            {
                // Exception: once the "turn now" callout has fired for a Normal exit
                // (50–110°), switch to ExitBearingTrue as the desired heading.
                // Bearing-to-junction fights the turn at this range — the junction is
                // still ahead, so as the pilot turns off the runway the heading error
                // flips toward the wrong side. ExitBearingTrue correctly decreases as
                // the pilot aligns with the exit, telling them how much more to turn.
                if (_rolloutTurnNowAnnounced && _rolloutExit!.ExitType == "Normal"
                    && _rolloutExit.ExitBearingTrue > 0.0)
                {
                    desiredHeading = _rolloutExit.ExitBearingTrue;
                }
                else
                {
                    const double MPD = 111132.0;
                    double midLatRad = (lat + _rolloutExit!.Latitude) * 0.5 * Math.PI / 180.0;
                    double bN = (_rolloutExit.Latitude - lat) * MPD;
                    double bE = (_rolloutExit.Longitude - lon) * MPD * Math.Cos(midLatRad);
                    desiredHeading = (Math.Atan2(bE, bN) * 180.0 / Math.PI + 360.0) % 360.0;
                }

                toneSilentDeg = ROLLOUT_EXIT_TONE_SILENT_DEG;
                toneActivationDeg = ROLLOUT_EXIT_TONE_ACTIVATION_DEG;
                toneMaxPanDeg = ROLLOUT_EXIT_TONE_MAX_PAN_DEG;
            }
            else
            {
                // DriftCorrection — the phase that used to be silent. Desired heading is
                // the runway itself, so the tone reads "steer back to the centreline".
                // KSEA 34L 2026-08-21: the pilot drifted to 15.1° with no cue at all, and
                // the tone's first utterance was a 79° hard pan after the handoff.
                desiredHeading = _rolloutRunwayHeadingTrue;
                toneSilentDeg = ROLLOUT_DRIFT_TONE_SILENT_DEG;
                toneActivationDeg = ROLLOUT_DRIFT_TONE_ACTIVATION_DEG;
                toneMaxPanDeg = ROLLOUT_DRIFT_TONE_MAX_PAN_DEG;
            }

            double rawError = NormalizeAngle(desiredHeading - headingTrue);
            _smoothedHeadingError = _headingErrorInitialized
                ? _smoothedHeadingError * (1 - HEADING_ERROR_FILTER_ALPHA) + rawError * HEADING_ERROR_FILTER_ALPHA
                : rawError;
            _headingErrorInitialized = true;

            if (!_steeringToneSuppressed)
            {
                _steeringTone.Resume();
                _steeringTone.UpdateHeadingErrorWithThresholds(
                    _smoothedHeadingError, toneSilentDeg, toneActivationDeg, toneMaxPanDeg);
            }
        }
```

Then update the long comment block immediately above it: its opening line reads "Rollout steering tone — exit-only design:" and the second paragraph claims "Before 300 ft the tone stays off — no centreline steering during high-speed rollout". Rewrite those two claims to describe the three modes. Leave the `NOTE: ExitBearingTrue is NOT used here` paragraph intact — it still explains the `ExitBearing` branch.

- [ ] **Step 5: Build and run the full suite**

```bash
dotnet build MSFSBlindAssist.sln -c Debug
```

Expected: `Build succeeded`, 0 errors. If MSB3021 appears, close MSFSBlindAssist and retry.

```bash
dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: `Failed: 0`. Note the passing count — later tasks must not reduce it.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs
git commit -m "feat(landing-exit): steer the pilot back to the centreline during rollout"
```

---

### Task 6: Wire the signed turn gate into both handoff monitors

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` (~296–320)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (~1636–1639)

**Interfaces:**
- Consumes: `RolloutExitGate.IsExitTurnBegun`, `RolloutExitGate.IsTurnTowardExit`.
- Produces: nothing new for later tasks.

- [ ] **Step 1: Replace `turnBegun` in `UpdateLandingRollout`**

`MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` line 296 already computes the SIGNED deviation as `hdgDelta`; only its magnitude is currently used. Do not add a second local.

First, fix the now-wrong comment at lines 293–295. Replace:

```csharp
        // Heading deviation from runway centerline. Positive sign matters
        // less than magnitude here — the question is whether the pilot has
        // started turning yet.
```

with:

```csharp
        // Heading deviation from the runway centreline, signed, POSITIVE = RIGHT.
        // The SIGN is load-bearing: a deviation away from the exit's own side is drift,
        // not the exit turn (KSEA 34L 2026-08-21). hdgDeltaAbs is still what the lateral
        // and overshoot gates below want.
```

Then replace the `turnBegun` assignment at line 314 (the two-line `hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS` expression) with:

```csharp
        // Relative bearing of the chosen exit from the runway heading, same sign convention.
        // ExitBearingTrue == 0.0 is the "unknown" sentinel and normalises into the sub-3°
        // band that disables the direction test — the intended degradation.
        double exitRelBearingDeg = _rolloutExit.ExitBearingTrue != 0.0
            ? NormalizeAngle(_rolloutExit.ExitBearingTrue - _rolloutRunwayHeadingTrue)
            : 0.0;

        // Speed-gated: above ROLLOUT_TURN_MAX_GS_KTS a heading deviation is touchdown yaw /
        // crab alignment, not a deliberate runway exit turn. Direction- and proximity-gated
        // since 2026-08: see Navigation/RolloutExitGate.IsExitTurnBegun.
        bool turnBegun = Navigation.RolloutExitGate.IsExitTurnBegun(
            hdgDelta, groundSpeedKts, distToExitFeet, pastExit, exitRelBearingDeg);
```

`distToExitFeet` (line 289) and `pastExit` (line 311) are both already in scope above this point.

- [ ] **Step 2: Add the direction test to the post-handoff monitor**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` at the `turnBegunPH` computation, replace:

```csharp
            bool turnBegunPH = hdgDeltaAbsPH >= ROLLOUT_TURN_BEGAN_HDG_DEG
                               && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS;
```

with:

```csharp
            double hdgDeltaSignedPH = NormalizeAngle(headingTrue - _rolloutRunwayHeadingTrue);
            double exitRelBearingPH = _rolloutExit.ExitBearingTrue != 0.0
                ? NormalizeAngle(_rolloutExit.ExitBearingTrue - _rolloutRunwayHeadingTrue)
                : 0.0;
            // Direction test only, NOT the distance window IsExitTurnBegun applies. This
            // block runs AFTER handoff, when the aircraft is already near or past the exit,
            // so a proximity gate would be wrong here. But a wrong-way turn clearing the
            // overshoot monitor is the same hole one level down: turnBegunPH sets
            // _rolloutHandoffActive = false under the comment "Pilot has taken the exit".
            bool turnBegunPH = hdgDeltaAbsPH >= ROLLOUT_TURN_BEGAN_HDG_DEG
                               && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS
                               && Navigation.RolloutExitGate.IsTurnTowardExit(
                                      hdgDeltaSignedPH, exitRelBearingPH);
```

The existing line immediately above computes `hdgDeltaAbsPH`; leave it in place.

- [ ] **Step 3: Build and run the full suite**

```bash
dotnet build MSFSBlindAssist.sln -c Debug && dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: `Build succeeded`, `Failed: 0`, passing count not below Task 5's.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs
git commit -m "fix(landing-exit): a drift away from the exit is no longer read as taking it"
```

---

### Task 7: Early-vacate retarget, reachability guard, and the closure callout

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs` (add one helper)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (field ~278, arrival branch ~2838, reset ~2938)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs` (~455)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` (handoff block ~439–539)

**Interfaces:**
- Consumes: `RolloutExitGate.MatchEarlyVacateExit`, `RolloutExitGate.IsHandoffRouteReachable`, `TaxiGraph.PerpendicularDistanceMetersStatic`, the existing `IsWithinRolloutRunwayLaterally`, `SignedAlongRunwayMeters`, `ResolveExitHandoffDestination`.
- Produces: nothing new.

- [ ] **Step 1: Add a signed lateral helper**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs`, add beside `AbsLateralFromRunwayMeters`:

```csharp
    /// <summary>
    /// Signed perpendicular offset (metres) of a point from the runway axis, POSITIVE =
    /// RIGHT of the runway direction. Companion to <see cref="SignedAlongRunwayMeters"/>,
    /// using the perpendicular component of the same equirectangular projection.
    ///
    /// <para>The sign convention matches <c>LandingExit.ExitSide</c>: the graph assigns
    /// "Right" when <c>NormalizeAngle(exitBearingTrue - runwayHeadingTrue) &gt;= 0</c>, which
    /// for a due-north runway puts a right-hand exit east of the axis — positive here.</para>
    /// </summary>
    internal static double SignedLateralFromRunwayMeters(
        double pointLat, double pointLon,
        double refLat, double refLon,
        double runwayHeadingTrueDeg)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (pointLat + refLat) * 0.5 * Math.PI / 180.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);
        double dN = (pointLat - refLat) * METERS_PER_DEG_LAT;
        double dE = (pointLon - refLon) * metersPerDegLon;
        double hdgRad = runwayHeadingTrueDeg * Math.PI / 180.0;
        return dE * Math.Cos(hdgRad) - dN * Math.Sin(hdgRad);
    }
```

- [ ] **Step 2: Add the early-vacate flag and its resets**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, next to `private bool _landingExitMissed = false;`:

```csharp
    // Set when the handoff concludes because the aircraft left the runway somewhere other
    // than the planned exit and no exit could be matched to where it actually went — or
    // the route that was built is not one the aircraft is on. HandleArrival renders a
    // distinct closure: this is the OPPOSITE failure to _landingExitMissed, which means
    // "you rolled past the vacate point", so the two must never share wording.
    private bool _landingExitVacatedEarly = false;
```

Reset it at BOTH existing reset sites, beside the `_landingExitMissed = false;` line — `TaxiGuidanceManager.cs` (~2938) and `TaxiGuidanceManager.Routing.cs` (~455):

```csharp
        _landingExitVacatedEarly = false;
```

- [ ] **Step 3: Add the closure wording**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, in `HandleArrival`'s `if (_isLandingExitRoute)` branch, add a new FIRST arm before `if (_landingExitMissed)`:

```csharp
            if (_landingExitVacatedEarly)
            {
                // The aircraft is off the runway but not at the planned exit, and nothing
                // could be matched to where it actually went. Never route back to the
                // planned exit from here: at KSEA 34L that produced a 1,678 m loop up the
                // parallel taxiway and back down toward the runway, because the taxi graph
                // carries no runway edges and that is the only path between two exits.
                AnnounceInstruction(
                    $"You have left the runway short of {exitName}. Exit guidance ended. " +
                    $"Stop and hold position, then open the taxi planner to set a route " +
                    $"to your gate.");
            }
            else if (_landingExitMissed)
```

`exitName` is already in scope from the line above (`string exitName = _route?.DestinationName ?? "the exit";`).

- [ ] **Step 4: Add the retarget and guard to the handoff block**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, inside `if (turnBegun || exitedLaterally || alignedWithExit || speedNearExitHandoff || trulyStopped)`, immediately after `_rolloutHandoffActive = true;` and BEFORE `bool handoffRerouted = false;`, insert:

```csharp
            // Early-vacate retarget. Entered only when the aircraft is BOTH laterally off
            // the runway AND far from the planned exit. Both gates are load-bearing: the
            // lateral gate is what "vacated" physically means, so a trulyStopped handoff on
            // the centreline 2,000 ft short of the exit keeps the planned exit and taxis to
            // it; and the distance gate reuses the same window as IsExitTurnBegun, because
            // ROLLOUT_NEAR_EXIT_FT (500) would classify a legitimate turn begun 800 ft out
            // as an early vacate.
            bool offRunwayAtHandoff = !IsWithinRolloutRunwayLaterally(lat, lon);
            bool farFromPlannedExit = !pastExit
                && distToExitFeet > Navigation.RolloutExitGate.TurnWindowFeet;

            if (offRunwayAtHandoff && farFromPlannedExit && _rolloutExit != null)
            {
                double lateralSignedM = SignedLateralFromRunwayMeters(
                    lat, lon, _rolloutRunway!.StartLat, _rolloutRunway.StartLon,
                    _rolloutRunwayHeadingTrue);

                var vacatedAt = Navigation.RolloutExitGate.MatchEarlyVacateExit(
                    _rolloutAllExits, _rolloutExit,
                    ex => SignedAlongRunwayMeters(
                              lat, lon, ex.Latitude, ex.Longitude,
                              _rolloutRunwayHeadingTrue) * METERS_TO_FEET,
                    lateralSignedM);

                if (vacatedAt != null)
                {
                    RolloutDiag($"Early vacate: left the runway {distToExitFeet:F0} ft short of " +
                        $"'{_rolloutExit.TaxiwayName}' (lateral {lateralSignedM:F0} m) — " +
                        $"retargeting to '{vacatedAt.TaxiwayName}' node={vacatedAt.NodeId}");
                    // Swap the exit so the destination, the post-handoff overshoot monitor
                    // and the arrival callout all name the taxiway the pilot is on.
                    _rolloutExit = vacatedAt;
                }
                else
                {
                    RolloutDiag($"Early vacate: left the runway {distToExitFeet:F0} ft short of " +
                        $"'{_rolloutExit.TaxiwayName}' (lateral {lateralSignedM:F0} m) and no " +
                        $"exit matched — concluding rather than routing back to the planned exit");
                    _landingExitVacatedEarly = true;
                    _rolloutHandoffActive = false;
                    SetState(TaxiGuidanceState.Taxiing);
                    HandleArrival();
                    return;
                }
            }
```

Then, immediately after the existing `RolloutDiag(rerouteErr == null ? … : …);` call and still inside the `if (_rolloutExit != null && _dataProvider != null && _graph != null)` block, append the reachability guard:

```csharp
                // Reachability guard: never hand the steering tone a target the aircraft is
                // not already essentially on. KSEA 34L 2026-08-21: the first segment lay
                // 53.9 m of cross-track away with the aircraft 17.8 m outside the runway
                // edge, and the tone — silent until that instant — panned 79° right.
                if (handoffRerouted && _route != null && _route.Segments.Count > 0)
                {
                    var firstSeg = _route.Segments[0];
                    double crossToFirstM = TaxiGraph.PerpendicularDistanceMetersStatic(
                        lat, lon,
                        firstSeg.FromNode.Latitude, firstSeg.FromNode.Longitude,
                        firstSeg.ToNode.Latitude, firstSeg.ToNode.Longitude);

                    if (!Navigation.RolloutExitGate.IsHandoffRouteReachable(
                            offRunwayAtHandoff, crossToFirstM, firstSeg.PathWidth))
                    {
                        RolloutDiag($"Handoff route unreachable: {crossToFirstM:F0} m from the " +
                            $"first segment (width {firstSeg.PathWidth:F0} ft) with the aircraft " +
                            $"off the runway — concluding rather than steering across it");
                        _landingExitVacatedEarly = true;
                        _rolloutHandoffActive = false;
                        SetState(TaxiGuidanceState.Taxiing);
                        HandleArrival();
                        return;
                    }
                }
```

`SetState(TaxiGuidanceState.Taxiing)` before `HandleArrival()` matches how the existing missed-vacate backstop concludes: `HandleArrival` sets `Arrived` itself, and calling it straight from `LandingRollout` would skip the state the arrival path expects.

- [ ] **Step 5: Build and run the full suite**

```bash
dotnet build MSFSBlindAssist.sln -c Debug && dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64
```

Expected: `Build succeeded`, `Failed: 0`.

- [ ] **Step 6: Verify `HandleArrival` is reachable from `LandingRollout`**

Read `HandleArrival` and confirm it does not early-return when `_state` is `Taxiing` and `_route` is non-null, and that the `_isLandingExitRoute` branch is reached without `_hasLineupTarget`. A landing-exit route never carries lineup data, so the "No lineup data — just stop" path is the one that runs. If any guard blocks it, note the exact line in the commit message rather than working around it.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs
git commit -m "fix(landing-exit): retarget or conclude when the pilot vacates short of the exit"
```

---

### Task 8: Documentation and changelog fragment

**Files:**
- Modify: `docs/taxi-guidance.md`
- Modify: `CLAUDE.md` (the taxi-guidance invariant list)
- Create: `changelog.d/<pr>-landing-exit-early-turn.fix.md`

- [ ] **Step 1: Add the invariants to `docs/taxi-guidance.md`**

Add to the landing-exit section:

```markdown
- The rollout exit-turn gate (`RolloutExitGate.IsExitTurnBegun`) tests the SIGNED heading
  deviation against the exit's own side, and requires the turn to begin within
  `TurnWindowFeet` (1,000 ft) of the exit or past it. Never restore the bare
  `Math.Abs(hdgDelta) >= 15` form: at KSEA 34L a 15.1° LEFT deceleration drift, 2,232 ft
  short of an exit lying 13.6° to the RIGHT, read as the exit turn, and the handoff then
  panned the steering tone 79° right at a graph node 54 m away and 17.8 m outside the
  runway edge. The 1,000 ft window is derived (558 ft worst-case exit-node displacement
  at a 15° exit on a 200 ft runway, plus the app's own 450 ft "at the exit" range) — do
  not tighten it to `ROLLOUT_NEAR_EXIT_FT`.
- The rollout steering tone has THREE modes, not two (`RolloutExitGate.SelectToneMode`):
  silent above 50 kt, exit-bearing within 300 ft of the exit, and drift-correction —
  steer back to the runway heading — in between. The middle phase used to be silent, so
  a pilot drifting toward the runway edge had no cue and the tone's first utterance was a
  hard pan.
- After an early vacate the handoff must NEVER re-route to the planned exit. The taxi
  graph carries no runway edges, so A* routes between two exits the long way round: at
  KSEA that was 1,678 m up the parallel taxiway T and back down Z toward the runway.
  Retarget to the exit actually vacated at (`MatchEarlyVacateExit`) or conclude with the
  "left the runway short of X" closure.
- `MatchEarlyVacateExit` must measure along-track PER EXIT, never against
  `DistanceFromThresholdFeet`. That field is measured from the LANDING threshold including
  `ThresholdOffset` (KJFK 13R 2,055 ft), while an aircraft's along-runway position is
  naturally measured from the physical runway start; comparing the two picks the wrong
  exit at every displaced-threshold runway.
```

- [ ] **Step 2: Add condensed one-liners to `CLAUDE.md`**

In the "### Taxi guidance" bullet group, add:

```markdown
- The rollout exit-turn gate must stay SIGNED (deviation on the exit's own side) and proximity-gated (within 1,000 ft of the exit or past it) — the bare `Math.Abs(hdgDelta) >= 15` form read a 15° LEFT drift 2,232 ft short of a RIGHT-hand exit as the exit turn (KSEA 34L). → [taxi-guidance.md](docs/taxi-guidance.md)
- The rollout steering tone has THREE modes (silent >50 kt / exit-bearing ≤300 ft / drift-correction in between) — never restore the two-mode "exit-only" design that left the middle of the rollout with no cue at all. → [taxi-guidance.md](docs/taxi-guidance.md)
- After an early vacate the handoff must never re-route to the PLANNED exit — with no runway edges in the graph, A* routes between two exits the long way round (KSEA: 1,678 m up the parallel taxiway and back). Retarget or conclude. → [taxi-guidance.md](docs/taxi-guidance.md)
- `MatchEarlyVacateExit` measures along-track PER EXIT and must never compare against `DistanceFromThresholdFeet` — that is measured from the LANDING threshold and breaks at every displaced-threshold runway. → [taxi-guidance.md](docs/taxi-guidance.md)
```

- [ ] **Step 3: Commit the docs**

```bash
git add docs/taxi-guidance.md CLAUDE.md
git commit -m "docs(taxi): record the rollout exit-turn and drift-tone invariants"
```

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin fix/landing-exit-early-turn
```

Then open the PR with `gh pr create`. **Read the PR number from the URL it prints — never guess it.** GitHub draws issue and PR numbers from one shared sequence, so any issue or PR filed in between shifts it.

- [ ] **Step 5: Add the changelog fragment**

Only now, with the real number in hand, create `changelog.d/<pr>-landing-exit-early-turn.fix.md`. Write for a pilot, not a reviewer — say what is different when they fly:

```markdown
Landing exit guidance no longer mistakes a drift for the exit turn. Sliding
off the centreline while slowing down — especially away from the exit you
picked — used to hand you off early and then pan the steering tone hard at a
taxiway you could not reach without leaving the pavement. You now get a
steady tone that steers you back to the centreline through the whole rollout,
and the turn only counts once you are actually near your exit and turning its
way. If you do come off at a different taxiway, guidance either follows you
onto it or tells you plainly that it has ended, instead of quietly routing you
the long way back to the exit you skipped.
```

```bash
git add changelog.d/<pr>-landing-exit-early-turn.fix.md
git commit -m "docs(changelog): fragment for the landing-exit early-turn fixes (PR #<pr>)"
git push
```

---

## In-sim test plan (for the PR body)

Sim-facing behaviour cannot be unit-tested. The repository owner runs these:

1. **KSEA ILS 34L, exit Z (the reported case).** Land, decelerate normally, let the aircraft
   drift left of the centreline. Expect: a tone panning RIGHT from about 3° of drift, no
   handoff, and the 1,500 ft / 500 ft / "turn now" callouts for Z arriving normally.
2. **KSEA 34L, deliberately take taxiway J.** Turn off at J's throat. Expect: guidance follows
   onto J and concludes there, naming J — not Z, and no route up taxiway T.
3. **KSEA 34L, cut across the infield as before.** Expect: "You have left the runway short of
   Taxiway Z. Exit guidance ended…", tone stops, no route.
4. **A normal 90° exit at any airport.** Expect: unchanged behaviour — exit tone at 300 ft,
   "turn now" at 150 ft, handoff on the turn, route onto the exit.
5. **A high-speed RET (e.g. EIDW 28L → S5).** Expect: unchanged — `TryEarlyExitHandoff` still
   fires at ≤300 ft.
6. **A left-hand exit runway.** Expect: the direction test mirrors correctly; a left turn onto
   a left exit hands off normally.
7. **A runway with no exit selected (runway-end countdown).** Expect: unchanged.
8. **Landing at a displaced-threshold runway (e.g. KJFK 13R).** Expect: exit callouts and any
   early-vacate matching name the correct taxiway.

## Self-review notes

- Spec coverage: `SelectToneMode` → Task 1/5; `IsExitTurnBegun` → Task 2/6; `turnBegunPH`
  direction test → Task 6; `MatchEarlyVacateExit` → Task 3/7; `IsHandoffRouteReachable` →
  Task 4/7; new closure wording → Task 7; constants table → Task 1; docs → Task 8.
- The design doc's `EXIT_COVERAGE_GAP_FT` reuse is a documented duplication, not a
  reference: that constant is method-local in `TaxiGraph.GetLandingExits` and cannot be
  referenced from another type. Task 1's XML comment records the obligation to keep them
  in step.
- `_rolloutExitToneArmed` is deleted in Task 5, not left orphaned — it had exactly four
  uses, all of them the smoother reset that `_rolloutToneMode` now performs in both
  directions.
