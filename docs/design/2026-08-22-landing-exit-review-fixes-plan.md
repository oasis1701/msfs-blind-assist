# Landing-Exit Review Follow-Up Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the four correctness defects and three duplications a high-effort review found in PR #204's landing-exit rollout guidance.

**Architecture:** Every defect is the same shape — a rule that exists once in the pure `Navigation/RolloutExitGate.cs` module is *also* expressed, slightly differently, in `Services/TaxiGuidanceManager*.cs`, and the two copies disagree. Each task makes the pure module the single authority for one rule, then rewires the manager to call it. Pure rules are unit-tested first; manager sequencing is sim-facing and gets a written in-sim test plan instead.

**Tech Stack:** .NET 10, C# 13, xUnit. Design: [2026-08-22-landing-exit-review-fixes-design.md](2026-08-22-landing-exit-review-fixes-design.md).

## Global Constraints

- **Build the solution, never the bare csproj.** `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build` on the `.csproj` defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path.
- **Close MSFSBlindAssist before building** — the exe is file-locked while it runs (MSB3021).
- **Test command:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- **Single-test filter:** append `--filter "FullyQualifiedName~ClassName"`.
- **Branch:** work on `fix/landing-exit-early-turn` (PR #204). Never commit to `main`.
- **No new changelog fragment.** `changelog.d/204-landing-exit-early-turn.fix.md` already exists for this PR and is amended in Task 8.
- **Do not re-tune** `ExitSideMinBearingDeg` (3.0), `TurnWindowFeet` (1000.0), `TurnBegunHeadingDeg` (15.0), `TurnMaxGroundSpeedKts` (90.0), or `DriftToneSilentDeg` (2.0). CLAUDE.md pins all five.
- **`RolloutExitGate` must stay free of Services/graph dependencies** — it declares this in its class doc. Give it a private `NormalizeAngle` rather than reaching into `TaxiGuidanceManager`.

## File Structure

| File | Responsibility | Tasks |
|---|---|---|
| `MSFSBlindAssist/Navigation/RolloutExitGate.cs` | Pure rollout decision rules — gains the clearance predicate, the width cap, and the bearing decoder | 1, 2, 3, 5 |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs` | Geometry helpers — `IsWithinRolloutRunwayLaterally` delegates; `AbsLateral` delegates to `SignedLateral` | 1, 4 |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` | Manager state, constants, `HandleArrival`, post-handoff monitor | 1, 2, 3, 6 |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` | The rollout frame loop and handoff block | 1, 3, 6, 7 |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs` | `LoadRoute` fresh-route reset | 6 |
| `MSFSBlindAssist/Navigation/TaxiGraph.cs` | `GetLandingExits` — its local 1400 ft const initialises from the gate | 5 |
| `tests/MSFSBlindAssist.Tests/RolloutLateralClearanceTests.cs` | **New.** Pins the shared clearance predicate | 1 |
| `tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs` | Extended with the width cap | 2 |
| `tests/MSFSBlindAssist.Tests/ExitRelativeBearingTests.cs` | **New.** Pins the sentinel decode | 3 |
| `tests/MSFSBlindAssist.Tests/TaxiMathUtilsTests.cs` | Extended with `Abs == \|Signed\|` | 4 |
| `changelog.d/204-landing-exit-early-turn.fix.md` | Amended wording | 8 |

---

### Task 1: One definition of "off the runway"

Closes defect **D1**, the 0.856 m dead band. `exitedLaterally` trips at `halfRunwayWidthFt + 30.0` ft (9.144 m) while `IsWithinRolloutRunwayLaterally` reports "still on the runway" up to `halfWidthM + 10.0` m. The handoff frame usually lands in the gap, which skips `MatchEarlyVacateExit` *and* makes `IsHandoffRouteReachable` return true through its `!aircraftOffRunway` early exit.

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (add constants + predicate after `HandoffReachDefaultHalfWidthM`, ~line 123)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs:401` (`RUNWAY_CLEAR_MARGIN_M`)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs:211-224` (`IsWithinRolloutRunwayLaterally`)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:404-407` (`exitedLaterally`)
- Test: `tests/MSFSBlindAssist.Tests/RolloutLateralClearanceTests.cs` (create)

**Interfaces:**
- Produces: `RolloutExitGate.RunwayClearMarginM` (`double`, 10.0), `RolloutExitGate.DefaultRunwayWidthFeet` (`double`, 200.0), `RolloutExitGate.IsLaterallyClearOfRunway(double absLateralMetres, double runwayWidthFeet) -> bool`.
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/RolloutLateralClearanceTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.IsLaterallyClearOfRunway — the ONE answer to
// "has the aircraft left the runway pavement?".
//
// Regression pinned: PR #204 review, 2026-08-22. `exitedLaterally` tripped at
// halfWidth + 30 ft (9.144 m) while IsWithinRolloutRunwayLaterally still reported the
// aircraft as ON the runway up to halfWidth + 10 m. The handoff fired inside that 0.856 m
// band, so `offRunwayAtHandoff` read false, the early-vacate retarget was skipped, and the
// reachability guard passed through its !aircraftOffRunway early exit — re-routing to the
// planned exit, the exact KSEA long-way-round PR #204 exists to prevent.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RolloutLateralClearanceTests
{
    private const double WidthFt = 150.0;                      // half-width 22.86 m
    private const double HalfWidthM = WidthFt * 0.3048 * 0.5;

    // The old dead band: 9.144 m (30 ft) past the half-width. The lateral handoff trigger
    // fired here, so this MUST read as still-on-the-runway=false... i.e. NOT clear, which is
    // what makes the trigger and the guards agree once both use this predicate.
    [Fact]
    public void InsideTheOldDeadBand_IsNotClear()
    {
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(HalfWidthM + 9.144, WidthFt));
    }

    // Boundary: half-width + 10 m, exclusive. Asserted at the next representable double so a
    // strict/non-strict inequality mutation is actually caught.
    [Fact]
    public void BoundaryIsHalfWidthPlusTenMetres()
    {
        double threshold = HalfWidthM + RolloutExitGate.RunwayClearMarginM;

        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(threshold, WidthFt));
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(Math.BitIncrement(threshold), WidthFt));
    }

    [Fact]
    public void WellOutsideThePavement_IsClear()
    {
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(HalfWidthM + 40.0, WidthFt));
    }

    [Fact]
    public void OnTheCentreline_IsNotClear()
    {
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(0.0, WidthFt));
    }

    // A runway with no recorded width falls back to 200 ft, matching the manager's own
    // long-standing default. Half-width 30.48 m + 10 m margin = 40.48 m.
    [Fact]
    public void MissingWidth_UsesTheTwoHundredFootDefault()
    {
        double fallbackThreshold =
            RolloutExitGate.DefaultRunwayWidthFeet * 0.3048 * 0.5 + RolloutExitGate.RunwayClearMarginM;

        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(fallbackThreshold, 0.0));
        Assert.True(RolloutExitGate.IsLaterallyClearOfRunway(Math.BitIncrement(fallbackThreshold), 0.0));
        // Negative width is treated the same as absent.
        Assert.False(RolloutExitGate.IsLaterallyClearOfRunway(fallbackThreshold, -1.0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RolloutLateralClearanceTests"`

Expected: **compile error** — `'RolloutExitGate' does not contain a definition for 'IsLaterallyClearOfRunway'` (and for `RunwayClearMarginM` / `DefaultRunwayWidthFeet`).

- [ ] **Step 3: Add the constants and predicate to RolloutExitGate**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, immediately after the
`HandoffReachDefaultHalfWidthM` declaration (~line 123) and before the `SelectToneMode`
doc comment, insert:

```csharp
    // ---- Runway lateral clearance.

    /// <summary>
    /// Margin beyond a runway's half-width inside which the aircraft still counts as being
    /// ON the pavement.
    ///
    /// <para>Canonical here so there is exactly ONE definition of "off the runway".
    /// <c>TaxiGuidanceManager.RUNWAY_CLEAR_MARGIN_M</c> initialises from this. Before
    /// 2026-08-22 the rollout's lateral handoff trigger carried its own <c>+30 ft</c>
    /// (9.144 m) spelling of the same idea, leaving a 0.856 m band in which the handoff
    /// fired while every guard still read the aircraft as on the runway.</para>
    /// </summary>
    public const double RunwayClearMarginM = 10.0;

    /// <summary>
    /// Width assumed for a runway whose navdata carries none. Matches the long-standing
    /// fallback in the rollout code.
    /// </summary>
    public const double DefaultRunwayWidthFeet = 200.0;

    /// <summary>
    /// Has the aircraft left the runway pavement laterally?
    ///
    /// <para>The single authority for that question. Both the rollout's lateral handoff
    /// trigger and the early-vacate / reachability guards route through it, so they cannot
    /// disagree about the same aircraft position.</para>
    ///
    /// <para>Strictly greater-than, so "exactly at the margin" is still ON the runway —
    /// the conservative direction for every caller.</para>
    /// </summary>
    /// <param name="absLateralMetres">
    /// Absolute perpendicular offset from the runway axis, metres — from
    /// <c>AbsLateralFromRunwayMeters</c> measured against a point ON the centreline
    /// (the runway start), never against an exit node.
    /// </param>
    /// <param name="runwayWidthFeet">
    /// The runway's width in FEET. Zero or negative means "not recorded" and falls back to
    /// <see cref="DefaultRunwayWidthFeet"/>.
    /// </param>
    public static bool IsLaterallyClearOfRunway(double absLateralMetres, double runwayWidthFeet)
    {
        double widthFt = runwayWidthFeet > 0.0 ? runwayWidthFeet : DefaultRunwayWidthFeet;
        double halfWidthM = widthFt * 0.3048 * 0.5;
        return absLateralMetres > halfWidthM + RunwayClearMarginM;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RolloutLateralClearanceTests"`

Expected: **PASS**, 5 tests.

- [ ] **Step 5: Point the manager's constant at the gate**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, replace line 401:

```csharp
    private const double RUNWAY_CLEAR_MARGIN_M = 10.0;
```

with:

```csharp
    // One source of truth — see Navigation/RolloutExitGate.RunwayClearMarginM, which the
    // lateral handoff trigger and the early-vacate guards also read through
    // IsLaterallyClearOfRunway.
    private const double RUNWAY_CLEAR_MARGIN_M = Navigation.RolloutExitGate.RunwayClearMarginM;
```

- [ ] **Step 6: Delegate `IsWithinRolloutRunwayLaterally` to the predicate**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs`, replace the body of
`IsWithinRolloutRunwayLaterally` (lines 211-224) with:

```csharp
    private bool IsWithinRolloutRunwayLaterally(double lat, double lon)
    {
        // Null runway is the only "not set" test: the runway and its heading are always
        // assigned together, and 0.0 is a legitimate heading (a due-north runway), so
        // treating it as a sentinel would report every frame as off the pavement.
        if (_rolloutRunway == null) return false;

        double lateralM = AbsLateralFromRunwayMeters(
            lat, lon, _rolloutRunway.StartLat, _rolloutRunway.StartLon,
            _rolloutRunwayHeadingTrue);
        return !Navigation.RolloutExitGate.IsLaterallyClearOfRunway(
            lateralM, _rolloutRunway.Width);
    }
```

Leave the XML doc comment above it unchanged except for one added paragraph before the
closing `</summary>`:

```csharp
    /// The half-width/margin comparison itself lives in
    /// <see cref="Navigation.RolloutExitGate.IsLaterallyClearOfRunway"/> so the rollout's
    /// lateral handoff trigger cannot spell it differently — it did, and the 0.856 m
    /// disagreement silently disabled the early-vacate retarget (PR #204 review).
```

- [ ] **Step 7: Point `exitedLaterally` at the same predicate**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, replace lines 404-407:

```csharp
        bool exitedLaterally = lateralFromCenterlineFt >= halfRunwayWidthFt + 30.0
                               && (distToExitFeet <= 250.0
                                   || hdgDeltaAbs >= 8.0
                                   || pastExit);
```

with:

```csharp
        // The lateral term is the SHARED predicate, not a local threshold. It used to be
        // `lateralFromCenterlineFt >= halfRunwayWidthFt + 30.0` (9.144 m), which sat 0.856 m
        // inside IsWithinRolloutRunwayLaterally's 10 m margin — so the handoff fired on a
        // frame that every downstream guard still read as ON the runway, skipping the
        // early-vacate retarget and passing the reachability guard unconditionally
        // (PR #204 review). The trigger now moves 0.856 m later, the conservative direction.
        bool exitedLaterally = !IsWithinRolloutRunwayLaterally(lat, lon)
                               && (distToExitFeet <= 250.0
                                   || hdgDeltaAbs >= 8.0
                                   || pastExit);
```

Leave `lateralFromCenterlineFt` and `halfRunwayWidthFt` in place — the diagnostics at line
452 and the overshoot gate below both still read them.

- [ ] **Step 8: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all tests pass, no regressions.

- [ ] **Step 9: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs MSFSBlindAssist/Services/TaxiGuidanceManager.cs MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs tests/MSFSBlindAssist.Tests/RolloutLateralClearanceTests.cs
git commit -m "fix(landing-exit): one definition of off-the-runway closes the handoff dead band

The lateral handoff trigger tripped at halfWidth + 30 ft while every guard
downstream read the aircraft as still on the runway up to halfWidth + 10 m. The
handoff frame usually landed in that 0.856 m band, which skipped the
early-vacate retarget and let the reachability guard pass through its
!aircraftOffRunway early exit -- re-routing to the planned exit.

Both now call RolloutExitGate.IsLaterallyClearOfRunway.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Cap the width the reachability guard trusts

Closes defect **D3**. `IsHandoffRouteReachable` derives its corridor from raw
`firstSegmentPathWidthFeet`. Navdata reports thousands of feet on apron-tagged rows — the
codebase already caps the same field at 300 ft for off-route detection
(`TaxiGuidanceManager.cs:2199`).

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (add const near `HandoffReachMarginM` ~line 117; modify `IsHandoffRouteReachable` ~line 325)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs:513` (`OFF_ROUTE_PERP_WIDTH_CAP_FT`)
- Test: `tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs` (extend)

**Interfaces:**
- Produces: `RolloutExitGate.MaxTrustedPathWidthFeet` (`double`, 300.0). `IsHandoffRouteReachable`'s signature is unchanged.
- Consumes: nothing from Task 1.

- [ ] **Step 1: Write the failing test**

Append these two tests inside the `HandoffRouteReachabilityTests` class in
`tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs`, before the closing brace:

```csharp
    // Some navdata rows report absurd widths (thousands of feet, aprons mis-tagged as taxi
    // paths). Uncapped, a 4,000 ft row bought a ~625 m corridor and the guard passed at any
    // cross-track -- defeating itself on exactly the airports with the dirtiest navdata.
    // The cap is the same 300 ft the off-route perpendicular check has always applied.
    [Fact]
    public void AbsurdPathWidth_IsCappedAtThreeHundredFeet()
    {
        double cappedThreshold =
            RolloutExitGate.MaxTrustedPathWidthFeet * 0.3048 * 0.5 + RolloutExitGate.HandoffReachMarginM;

        // A 4,000 ft row must behave exactly like a 300 ft one.
        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, cappedThreshold, 4000.0));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, Math.BitIncrement(cappedThreshold), 4000.0));

        // The KSEA regression must still be refused even if the row were mis-tagged wide.
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, 53.9, 4000.0));
    }

    // The cap is one-sided: a width at or below it is used as-is, so ordinary taxiways are
    // completely unaffected.
    [Fact]
    public void WidthBelowTheCap_IsUsedUnchanged()
    {
        double jThreshold = JWidthFt * 0.3048 * 0.5 + RolloutExitGate.HandoffReachMarginM;

        Assert.True(RolloutExitGate.IsHandoffRouteReachable(true, jThreshold, JWidthFt));
        Assert.False(RolloutExitGate.IsHandoffRouteReachable(true, Math.BitIncrement(jThreshold), JWidthFt));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~HandoffRouteReachabilityTests"`

Expected: **compile error** — `'RolloutExitGate' does not contain a definition for 'MaxTrustedPathWidthFeet'`.

- [ ] **Step 3: Add the constant and clamp**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, after the `HandoffReachMarginM`
declaration (~line 117), insert:

```csharp
    /// <summary>
    /// Widest <c>PathWidth</c> this guard will believe, in FEET.
    ///
    /// <para>Some navdata rows report absurd widths — thousands of feet where an apron or a
    /// combined surface is mis-tagged as a taxi path. Uncapped, one such row on a handoff
    /// route's first segment bought a ~625 m acceptance corridor, so the guard passed at any
    /// cross-track and defeated itself on exactly the airports with the dirtiest navdata.</para>
    ///
    /// <para>Canonical here; <c>TaxiGuidanceManager.OFF_ROUTE_PERP_WIDTH_CAP_FT</c>, which has
    /// applied the same 300 ft cap to off-route detection since long before this guard
    /// existed, initialises from it.</para>
    /// </summary>
    public const double MaxTrustedPathWidthFeet = 300.0;
```

Then in `IsHandoffRouteReachable`, replace the half-width computation:

```csharp
        double halfWidthM = firstSegmentPathWidthFeet > 0.0
            ? firstSegmentPathWidthFeet * 0.3048 * 0.5
            : HandoffReachDefaultHalfWidthM;
```

with:

```csharp
        // Clamp before trusting: a mis-tagged apron row would otherwise widen the corridor
        // until this guard could never refuse anything. One-sided — a narrow width is used
        // as-is, and an absent one still gets the generous default below, because refusing
        // ENDS guidance and thin navdata must never cause a false refusal.
        double trustedWidthFt = Math.Min(firstSegmentPathWidthFeet, MaxTrustedPathWidthFeet);
        double halfWidthM = trustedWidthFt > 0.0
            ? trustedWidthFt * 0.3048 * 0.5
            : HandoffReachDefaultHalfWidthM;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~HandoffRouteReachabilityTests"`

Expected: **PASS**, 7 tests (5 pre-existing + 2 new).

- [ ] **Step 5: Point the manager's cap at the gate**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, replace line 513:

```csharp
    private const double OFF_ROUTE_PERP_WIDTH_CAP_FT = 300.0;
```

with:

```csharp
    private const double OFF_ROUTE_PERP_WIDTH_CAP_FT =
        Navigation.RolloutExitGate.MaxTrustedPathWidthFeet;
```

Leave the explanatory comment above it in place.

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs MSFSBlindAssist/Services/TaxiGuidanceManager.cs tests/MSFSBlindAssist.Tests/HandoffRouteReachabilityTests.cs
git commit -m "fix(landing-exit): cap the navdata width the reachability guard trusts

A taxi_path row mis-tagged at 4,000 ft gave the guard a 625 m acceptance
corridor, so it passed at any cross-track and could not refuse the hard-pan it
exists to stop. Clamped to the same 300 ft the off-route perpendicular check has
always applied.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: One owner for the exit-bearing sentinel, and correct its docs

Closes reuse finding **R3**. The guarded ternary
`ExitBearingTrue != 0.0 ? NormalizeAngle(ExitBearingTrue - runwayHeading) : 0.0` is
duplicated character-for-character at two sites, and three doc comments claim the
sentinel "normalises into" the unknown band — which is **false**: the formula they
prescribe yields `NormalizeAngle(-runwayHeadingTrue)`, i.e. +90° on a 270° runway, which
`HasKnownExitSide` accepts as a real side.

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (add `NormalizeAngle` + `ExitRelativeBearingDeg`; fix docs at ~lines 86-88, 171-173, 193-197)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:313-318`
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs:1660-1665`
- Test: `tests/MSFSBlindAssist.Tests/ExitRelativeBearingTests.cs` (create)

**Interfaces:**
- Produces: `RolloutExitGate.ExitRelativeBearingDeg(double exitBearingTrue, double runwayHeadingTrue) -> double`.
- Consumes: `RolloutExitGate.HasKnownExitSide` and `ExitSideMinBearingDeg` (both pre-existing).

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/ExitRelativeBearingTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.ExitRelativeBearingDeg — the ONE decoder for
// LandingExit.ExitBearingTrue's "unknown" sentinel.
//
// Regression pinned: PR #204 review, 2026-08-22. Three doc comments claimed
// `ExitBearingTrue == 0.0` "normalises into" the sub-3-degree unknown band. It does not:
// the prescribed formula NormalizeAngle(0 - runwayHeadingTrue) yields -20 on a 020 runway
// and +90 on a 270 runway, both of which HasKnownExitSide accepts as a real side. Only the
// callers' undocumented `!= 0.0` guard made the degradation work, and it was copy-pasted.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ExitRelativeBearingTests
{
    // The sentinel must decode to a bearing with NO knowable side, on every runway heading.
    [Theory]
    [InlineData(20.0)]
    [InlineData(90.0)]
    [InlineData(270.0)]
    [InlineData(337.0)]
    [InlineData(344.0)]
    [InlineData(0.0)]
    public void UnknownSentinel_HasNoKnowableSide_OnEveryRunwayHeading(double runwayHeadingTrue)
    {
        double rel = RolloutExitGate.ExitRelativeBearingDeg(0.0, runwayHeadingTrue);

        Assert.Equal(0.0, rel);
        Assert.False(RolloutExitGate.HasKnownExitSide(rel));
    }

    // The bug the sentinel guard prevents: the naive formula fabricates a side.
    [Fact]
    public void NaiveFormulaWouldFabricateASide_OnATwoSeventyRunway()
    {
        // What the old doc comments prescribed, evaluated for the sentinel on runway 27.
        double naive = 90.0;   // NormalizeAngle(0.0 - 270.0)
        Assert.True(RolloutExitGate.HasKnownExitSide(naive));

        // What the decoder actually returns.
        Assert.False(RolloutExitGate.HasKnownExitSide(
            RolloutExitGate.ExitRelativeBearingDeg(0.0, 270.0)));
    }

    // A real bearing is the normalised difference, POSITIVE = right of the runway heading.
    [Fact]
    public void RealBearing_IsTheNormalisedDifference()
    {
        // KSEA 34L, runway heading 337.0 true, exit lying 13.6 to the RIGHT.
        Assert.Equal(13.6, RolloutExitGate.ExitRelativeBearingDeg(350.6, 337.0), 6);
        Assert.True(RolloutExitGate.HasKnownExitSide(13.6));
    }

    [Fact]
    public void RealBearing_IsNegativeForALeftHandExit()
    {
        Assert.Equal(-13.6, RolloutExitGate.ExitRelativeBearingDeg(323.4, 337.0), 6);
    }

    // Wrapping across north must not flip the side.
    [Fact]
    public void RealBearing_WrapsAcrossNorth()
    {
        // A 010-degree exit off a 350-degree runway is 20 degrees RIGHT, not -340.
        Assert.Equal(20.0, RolloutExitGate.ExitRelativeBearingDeg(10.0, 350.0), 6);
        // And the reciprocal case.
        Assert.Equal(-20.0, RolloutExitGate.ExitRelativeBearingDeg(350.0, 10.0), 6);
    }

    // An exit whose real bearing happens to equal the runway heading has no side either --
    // it is geometrically straight ahead, which is the same answer the sentinel gives.
    [Fact]
    public void ExitStraightAhead_HasNoKnowableSide()
    {
        double rel = RolloutExitGate.ExitRelativeBearingDeg(337.0, 337.0);
        Assert.False(RolloutExitGate.HasKnownExitSide(rel));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ExitRelativeBearingTests"`

Expected: **compile error** — `'RolloutExitGate' does not contain a definition for 'ExitRelativeBearingDeg'`.

- [ ] **Step 3: Add the decoder to RolloutExitGate**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, add at the END of the class (after
`IsHandoffRouteReachable`, before the class's closing brace):

```csharp
    /// <summary>
    /// Decode a <c>LandingExit.ExitBearingTrue</c> into a bearing relative to the runway,
    /// POSITIVE = RIGHT, handling the <c>0.0</c> "unknown" sentinel.
    ///
    /// <para>The ONE owner of that sentinel. A plain
    /// <c>NormalizeAngle(exitBearingTrue - runwayHeadingTrue)</c> does NOT degrade safely:
    /// for the sentinel it yields <c>NormalizeAngle(-runwayHeadingTrue)</c> — −20° on a 020°
    /// runway, +90° on a 270° runway — which <see cref="HasKnownExitSide"/> accepts as a real
    /// side, so the direction test then compares a live heading against a fabricated one.
    /// Returning 0.0 puts it inside <see cref="ExitSideMinBearingDeg"/>, which is what
    /// actually disables the direction test.</para>
    /// </summary>
    public static double ExitRelativeBearingDeg(double exitBearingTrue, double runwayHeadingTrue)
        => exitBearingTrue != 0.0
            ? NormalizeAngle(exitBearingTrue - runwayHeadingTrue)
            : 0.0;

    /// <summary>
    /// Fold an angle into [−180, 180]. Private to keep this module free of the Services and
    /// graph dependencies its class doc promises — the same choice
    /// <see cref="LandingExitDestination"/> and <see cref="RunwayVacateResolver"/> make.
    /// </summary>
    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ExitRelativeBearingTests"`

Expected: **PASS**, 11 tests (6 theory cases + 5 facts).

- [ ] **Step 5: Correct the three false doc comments**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`:

**(a)** In the `ExitSideMinBearingDeg` doc (~lines 86-88), replace:

```csharp
    /// <c>straight ahead. <c>ExitBearingTrue == 0.0</c> — the "unknown" sentinel used throughout
    /// the rollout code — normalises into this band, which is the intended degradation.
```

with:

```csharp
    /// straight ahead. The <c>ExitBearingTrue == 0.0</c> "unknown" sentinel is mapped into
    /// this band by <see cref="ExitRelativeBearingDeg"/> — NOT by the subtraction itself,
    /// which would place it at <c>-runwayHeadingTrue</c> and fabricate a side.
```

(Match the exact existing text when editing; the surrounding lines are unchanged.)

**(b)** In the `HasKnownExitSide` doc (~lines 171-173), replace:

```csharp
    /// floor <see cref="IsTurnTowardExit"/> degrades on? False for the
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel (which normalises to a relative
    /// bearing of 0.0) and for any exit close enough to dead-ahead to be geometrically
```

with:

```csharp
    /// floor <see cref="IsTurnTowardExit"/> degrades on? False for the
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel (which
    /// <see cref="ExitRelativeBearingDeg"/> maps to 0.0) and for any exit close enough to
    /// dead-ahead to be geometrically
```

**(c)** In the `IsExitTurnBegun` `<param>` doc (~lines 193-197), replace:

```csharp
    /// <param name="exitRelativeBearingDeg">
    /// <c>NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)</c>. The
    /// <c>ExitBearingTrue == 0.0</c> "unknown" sentinel lands inside
    /// <see cref="ExitSideMinBearingDeg"/> and disables the direction test.
    /// </param>
```

with:

```csharp
    /// <param name="exitRelativeBearingDeg">
    /// From <see cref="ExitRelativeBearingDeg"/> — never a hand-written
    /// <c>NormalizeAngle(exit.ExitBearingTrue - runwayHeadingTrue)</c>, which does not
    /// degrade the <c>ExitBearingTrue == 0.0</c> "unknown" sentinel and would hand this
    /// method a fabricated exit side on every runway not aligned near 360°.
    /// </param>
```

- [ ] **Step 6: Route both call sites through the decoder**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, replace lines 313-318:

```csharp
        // Relative bearing of the chosen exit from the runway heading, same sign convention.
        // ExitBearingTrue == 0.0 is the "unknown" sentinel and normalises into the sub-3°
        // band that disables the direction test — the intended degradation.
        double exitRelBearingDeg = _rolloutExit.ExitBearingTrue != 0.0
            ? NormalizeAngle(_rolloutExit.ExitBearingTrue - _rolloutRunwayHeadingTrue)
            : 0.0;
```

with:

```csharp
        // Relative bearing of the chosen exit from the runway heading, same sign convention.
        // The decoder owns the ExitBearingTrue == 0.0 "unknown" sentinel — see its doc for
        // why the bare subtraction fabricates a side instead of degrading.
        double exitRelBearingDeg = Navigation.RolloutExitGate.ExitRelativeBearingDeg(
            _rolloutExit.ExitBearingTrue, _rolloutRunwayHeadingTrue);
```

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, find the `exitRelBearingPH`
assignment (~line 1663) in the post-handoff overshoot monitor. It is the same guarded
ternary against `_rolloutExit.ExitBearingTrue` and `_rolloutRunwayHeadingTrue`. Replace the
whole assignment with:

```csharp
                double exitRelBearingPH = Navigation.RolloutExitGate.ExitRelativeBearingDeg(
                    _rolloutExit.ExitBearingTrue, _rolloutRunwayHeadingTrue);
```

preserving the surrounding indentation exactly as found.

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs MSFSBlindAssist/Services/TaxiGuidanceManager.cs tests/MSFSBlindAssist.Tests/ExitRelativeBearingTests.cs
git commit -m "refactor(landing-exit): one owner for the exit-bearing unknown sentinel

The guarded ternary was copy-pasted at both call sites, and three doc comments
claimed the 0.0 sentinel normalises into the unknown band. It does not: the
prescribed formula yields -runwayHeadingTrue, which HasKnownExitSide accepts as
a real side. A caller following the docs would have compared a live heading
against a fabricated one on every runway not aligned near 360.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `AbsLateralFromRunwayMeters` delegates to the signed helper

Closes reuse finding **R1**. The two bodies are byte-identical apart from the final
`Math.Abs`, and they feed gates that must agree about the same aircraft position:
`IsWithinRolloutRunwayLaterally` reads the absolute one, `MatchEarlyVacateExit`'s side
classification reads the signed one.

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs:160-172`
- Test: `tests/MSFSBlindAssist.Tests/TaxiMathUtilsTests.cs` (extend)

**Interfaces:**
- Produces: nothing new. `AbsLateralFromRunwayMeters` keeps its exact signature and results.
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: Write the failing test**

`tests/MSFSBlindAssist.Tests/TaxiMathUtilsTests.cs` already has `using
MSFSBlindAssist.Services;` at the top and reaches these `internal static` helpers as
`TaxiGuidanceManager.AbsLateralFromRunwayMeters(...)`, via
`MSFSBlindAssist/Properties/InternalsVisibleTo.cs`. No new plumbing is needed.

Append inside the existing `TaxiMathUtilsTests` class, before its closing brace:

```csharp
    // The absolute helper must be exactly the magnitude of the signed one. They were two
    // hand-maintained copies of the same equirectangular projection, differing only in a
    // Math.Abs -- and they feed gates that must agree about one aircraft position:
    // IsWithinRolloutRunwayLaterally reads the absolute, the early-vacate side
    // classification reads the signed.
    [Theory]
    [InlineData(47.4400, -122.3000, 47.4400, -122.3088, 337.0)]   // left of a KSEA-like axis
    [InlineData(47.4400, -122.3160, 47.4400, -122.3088, 337.0)]   // right of it
    [InlineData(47.4500, -122.3088, 47.4400, -122.3088, 337.0)]   // along the axis
    [InlineData(51.4700,    0.4500, 51.4775,    0.4614,  90.0)]   // due-east runway
    [InlineData(51.4700,    0.4500, 51.4775,    0.4614,   0.0)]   // due-north runway
    [InlineData(-33.9400, 151.1700, -33.9465, 151.1810, 162.0)]   // southern hemisphere
    public void AbsLateralIsTheMagnitudeOfSignedLateral(
        double pointLat, double pointLon, double refLat, double refLon, double runwayHeadingTrue)
    {
        double abs = TaxiGuidanceManager.AbsLateralFromRunwayMeters(
            pointLat, pointLon, refLat, refLon, runwayHeadingTrue);
        double signed = TaxiGuidanceManager.SignedLateralFromRunwayMeters(
            pointLat, pointLon, refLat, refLon, runwayHeadingTrue);

        Assert.Equal(Math.Abs(signed), abs, 9);
    }
```

This test passes against the current duplicated code — that is intentional. It is a
**characterization test**: it locks the equivalence in place *before* the refactor so the
refactor is provably behaviour-preserving.

- [ ] **Step 2: Run test to verify it passes against the duplicated code**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiMathUtilsTests"`

Expected: **PASS**. If it FAILS, stop — the two copies have already drifted, and that is a
finding to report rather than refactor over.

- [ ] **Step 3: Collapse the duplication**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs`, replace the whole body of
`AbsLateralFromRunwayMeters` (lines 160-172) with:

```csharp
    internal static double AbsLateralFromRunwayMeters(
        double pointLat, double pointLon,
        double refLat, double refLon,
        double runwayHeadingTrueDeg)
        => Math.Abs(SignedLateralFromRunwayMeters(
            pointLat, pointLon, refLat, refLon, runwayHeadingTrueDeg));
```

Keep the existing XML doc comment above it, and add one line to it before `</summary>`:

```csharp
    /// The projection itself lives in <see cref="SignedLateralFromRunwayMeters"/> — the two
    /// were byte-identical copies, and they feed gates that must agree about one position.
```

- [ ] **Step 4: Run test to verify it still passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TaxiMathUtilsTests"`

Expected: **PASS**, same count as Step 2 plus the 6 new theory cases.

- [ ] **Step 5: Build, full suite, commit**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.MathUtils.cs tests/MSFSBlindAssist.Tests/TaxiMathUtilsTests.cs
git commit -m "refactor(taxi): absolute lateral offset delegates to the signed helper

The two bodies were byte-identical apart from a Math.Abs, and they feed gates
that must agree about one aircraft position. Characterization test locks the
equivalence first.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Make the 1,400 ft early-vacate value a single constant

Closes reuse finding **R2**. `RolloutExitGate.EarlyVacateMaxPassedFeet` (1400.0) and
`TaxiGraph.GetLandingExits`'s method-local `EXIT_COVERAGE_GAP_FT` (1400.0) hold the same
measured value, kept in step only by a comment on one side.

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs:101-108` (doc only)
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs:2830`

**Interfaces:**
- Produces: nothing new — `EarlyVacateMaxPassedFeet` keeps its name, type and value.
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: Point TaxiGraph's local const at the gate**

In `MSFSBlindAssist/Navigation/TaxiGraph.cs`, replace line 2830:

```csharp
        const double EXIT_COVERAGE_GAP_FT = 1400.0;
```

with:

```csharp
        // Shared with the early-vacate matcher, which answers the same question from the
        // other direction ("is this exit close enough behind me to be the one I turned at?").
        // A local const initialised from the gate's, so the two cannot drift.
        const double EXIT_COVERAGE_GAP_FT = RolloutExitGate.EarlyVacateMaxPassedFeet;
```

Leave the measurement comment above it (lines 2820-2829) exactly as it is — it is the
provenance of the number and belongs where it was measured.

- [ ] **Step 2: Update the gate's doc to stop asking for manual syncing**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, replace the
`EarlyVacateMaxPassedFeet` doc (lines 101-107):

```csharp
    /// <summary>
    /// How far BEHIND the aircraft an exit may be and still be the one vacated at. This is
    /// the same value as <c>EXIT_COVERAGE_GAP_FT</c> in <c>TaxiGraph.GetLandingExits</c>,
    /// which that comment records as measured across 266 runway directions at 39 airports as
    /// the distance beyond which two nodes stop describing the same physical turnoff. That
    /// constant is method-local and cannot be referenced; keep the two in step.
    /// </summary>
```

with:

```csharp
    /// <summary>
    /// How far BEHIND the aircraft an exit may be and still be the one vacated at.
    ///
    /// <para>Canonical here, and <c>EXIT_COVERAGE_GAP_FT</c> in
    /// <c>TaxiGraph.GetLandingExits</c> initialises from it, so the two cannot drift. The
    /// value was MEASURED there — across 266 runway directions at 39 airports, as the
    /// distance beyond which two nodes stop describing the same physical turnoff — and that
    /// provenance comment stays with the measurement. The direction of the reference is
    /// deliberate: this module promises no dependency on the graph, so the graph reads the
    /// gate rather than the reverse.</para>
    /// </summary>
```

- [ ] **Step 3: Build to prove the reference compiles**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors. A compile error here would mean the const reference
is not legal in that position — report it rather than reverting to a duplicated literal.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass. No new test — divergence is now a compile-time impossibility, which is
strictly stronger than a sync test.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs MSFSBlindAssist/Navigation/TaxiGraph.cs
git commit -m "refactor(landing-exit): single constant for the 1,400 ft early-vacate window

The exit-dedup pass and the early-vacate matcher answer the same question and
held the same measured value in two places, kept in step by a comment on one
side. TaxiGraph's local const now initialises from the gate's, so a re-measure
cannot strand the mirror.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Split the two closure reasons

Closes defect **D4**. The reachability guard sets `_landingExitVacatedEarly`
unconditionally, so a pilot who vacated AT or PAST the planned exit is told they left the
runway *short of* it.

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (field near line 284; `HandleArrival` ~lines 2876-2910; `StopGuidance` reset ~line 2997)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs:456-457` (fresh-route reset)
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:613` (the guard's set site)

**Interfaces:**
- Produces: `_landingExitRouteUnreachable` (`private bool`), read by `HandleArrival`, set by the reachability guard, reset in `LoadRoute` and `StopGuidance`. Task 7 also sets it.
- Consumes: nothing from earlier tasks.

- [ ] **Step 1: Add the field**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, immediately after the
`_landingExitVacatedEarlyPlannedName` declaration (~line 293), insert:

```csharp
    // Set when the handoff concludes because the route that was built is not one the
    // aircraft is on, WITHOUT an early vacate having been established first. Distinct from
    // _landingExitVacatedEarly because that closure claims a POSITION — "short of X" — and
    // this path cannot support that claim: the guard also fires when the aircraft turned off
    // at or beyond the planned exit, where "short of" is simply false. Distinct from
    // _landingExitOffPavement too, whose "Off the runway at X" wording reads as a successful
    // vacate.
    private bool _landingExitRouteUnreachable = false;
```

- [ ] **Step 2: Reset it everywhere its siblings reset**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs`, after line 457
(`_landingExitVacatedEarlyPlannedName = null;`), add:

```csharp
            _landingExitRouteUnreachable = false;
```

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`, after line 2998
(`_landingExitVacatedEarlyPlannedName = null;` inside `StopGuidance`), add:

```csharp
        _landingExitRouteUnreachable = false;
```

Match the surrounding indentation at each site exactly.

- [ ] **Step 3: Add the closure branch**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`'s `HandleArrival`, the chain currently
reads `if (_landingExitVacatedEarly) … else if (_landingExitMissed) … else if
(_landingExitOffPavement) … else …`.

Insert a new branch **between** the `_landingExitMissed` branch and the
`_landingExitOffPavement` branch:

```csharp
            else if (_landingExitRouteUnreachable)
            {
                // Off the runway, but nothing established WHERE along it — the guard fires
                // for an at-the-exit and a past-the-exit vacate as well as an early one. Any
                // positional claim here would be a guess, and a blind pilot told they are
                // somewhere they are not may manoeuvre to "correct" a position that was fine.
                // State only what is certain: guidance has ended and they should hold.
                AnnounceInstruction(
                    "Exit guidance ended: no usable route from here. Stop and hold position, " +
                    "then open the taxi planner to set a route to your gate.");
            }
```

- [ ] **Step 4: Make the guard choose the right reason**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, replace line 613
(`_landingExitVacatedEarly = true;` inside the `IsHandoffRouteReachable` refusal block):

```csharp
                        _landingExitVacatedEarly = true;
```

with:

```csharp
                        // Only claim "left the runway short of X" when an early vacate was
                        // actually established — the captured planned-exit name is the proof.
                        // Otherwise this guard has also fired for a vacate AT or PAST the
                        // planned exit, where "short of" is false.
                        if (_landingExitVacatedEarlyPlannedName != null)
                            _landingExitVacatedEarly = true;
                        else
                            _landingExitRouteUnreachable = true;
```

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass

(No unit test: `HandleArrival` needs a live manager, SimConnect state and the announcer.
Covered by in-sim scenario 3 in the design doc.)

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Routing.cs MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs
git commit -m "fix(landing-exit): stop claiming 'short of X' for an at-or-past-exit vacate

The reachability guard set the vacated-early flag unconditionally, so a pilot
who turned off at or beyond the planned exit was told they had left the runway
short of it. The guard now claims that only when an early vacate was actually
established; otherwise a new closure states what is certain and makes no
positional claim.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Conclude on a failed re-route after a swap, and guard the route guidance resumes on

Closes defect **D2**. Two holes: (a) after `MatchEarlyVacateExit` swaps the exit and
announces the substitute, a failed `LoadRoute` falls through to a re-anchor on the ORIGINAL
route — which still targets the planned exit the pilot just left short of; (b) the
reachability guard is gated on `handoffRerouted`, so the fallback path has no guard at all.

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:471-636` (the handoff block)

**Interfaces:**
- Consumes: `_landingExitRouteUnreachable` from Task 6; `RolloutExitGate.IsHandoffRouteReachable` (Task 2's capped version).
- Produces: nothing new.

**Context the implementer needs:** `LoadRoute`'s fresh-route reset block
(`Routing.cs:449-465`, which nulls `_landingExitVacatedEarlyPlannedName`) sits AFTER every
failure exit. So on a failed `LoadRoute` the captured name survives untouched, and the
conclude path can name the planned exit with no new plumbing. Do **not** add a second
restore for the failure path.

- [ ] **Step 1: Record that the swap happened**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, declare a flag immediately
before the `if (offRunwayAtHandoff && farFromPlannedExit && _rolloutExit != null)` block
(currently line 471):

```csharp
            // Whether the early-vacate branch below repointed _rolloutExit at a substitute.
            // If it did, the touchdown route in _route targets the exit the pilot has just
            // left short of, so it can never be resumed as a fallback.
            bool earlyVacateSwapped = false;
```

Then inside the `if (vacatedAt != null)` branch, immediately after
`_rolloutExit = vacatedAt;` (currently line 519), add:

```csharp
                    earlyVacateSwapped = true;
```

- [ ] **Step 2: Move the reachability guard below the fallback re-anchor**

Currently the guard sits INSIDE the `if (_rolloutExit != null && _dataProvider != null &&
_graph != null)` block, gated on `handoffRerouted`, at lines 599-619. **Delete that whole
`if (handoffRerouted && _route != null && _route.Segments.Count > 0) { … }` block** —
including its `RolloutDiag`, its state writes, its `SetState`, `HandleArrival()` and
`return`. Everything else in the enclosing block (the `LoadRoute` call, the
`handoffRerouted` assignment, the carry-across restores, the re-route `RolloutDiag`) stays
exactly as it is.

- [ ] **Step 3: Conclude when a swap is followed by a failed re-route**

Immediately AFTER the closing brace of the `if (_rolloutExit != null && _dataProvider !=
null && _graph != null)` block (currently line 620) and BEFORE the
`if (!handoffRerouted && _route != null)` re-anchor, insert:

```csharp
            // An early-vacate swap with no route to show for it must CONCLUDE, never fall
            // through to the re-anchor below: _route is still the touchdown route, whose
            // destination is the PLANNED exit the pilot has just been told they left short
            // of. Resuming the tone on it would steer them back toward the exit they
            // skipped, seconds after the substitute announcement said otherwise — and with
            // no runway edges in the graph, that is the 1,678 m KSEA long-way-round.
            if (earlyVacateSwapped && !handoffRerouted)
            {
                RolloutDiag("Early vacate: substitute exit re-route failed — concluding " +
                    "rather than resuming on the route to the planned exit");
                _landingExitVacatedEarly = true;
                _rolloutHandoffActive = false;
                SetState(TaxiGuidanceState.Taxiing);
                HandleArrival();
                return;
            }
```

- [ ] **Step 4: Re-add the guard after the re-anchor, pointed at the live cursor**

Immediately AFTER the `if (!handoffRerouted && _route != null) { … }` re-anchor block
(which ends around line 636) and BEFORE the `if (!handoffRerouted && _route == null)`
no-graph block, insert:

```csharp
            // Reachability guard: never hand the steering tone a target the aircraft is not
            // already essentially on. KSEA 34L 2026-08-21: the first segment lay 53.9 m of
            // cross-track away with the aircraft 17.8 m outside the runway edge, and the
            // tone — silent until that instant — panned 79° right.
            //
            // Placed AFTER the re-anchor and read at _currentSegmentIndex so it tests the
            // segment the tone is ACTUALLY about to steer at. It used to sit inside the
            // re-route block gated on handoffRerouted, which left the fallback path — the
            // one that resumes on the touchdown route — with no guard at all, while
            // CLAUDE.md requires it to gate EVERY landing-exit handoff re-route. For a
            // successful re-route the cursor is 0, so that case is unchanged.
            if (_route != null && _currentSegmentIndex >= 0
                && _currentSegmentIndex < _route.Segments.Count)
            {
                var firstSeg = _route.Segments[_currentSegmentIndex];
                double crossToFirstM = TaxiGraph.PerpendicularDistanceMetersStatic(
                    lat, lon,
                    firstSeg.FromNode.Latitude, firstSeg.FromNode.Longitude,
                    firstSeg.ToNode.Latitude, firstSeg.ToNode.Longitude);

                if (!Navigation.RolloutExitGate.IsHandoffRouteReachable(
                        offRunwayAtHandoff, crossToFirstM, firstSeg.PathWidth))
                {
                    RolloutDiag($"Handoff route unreachable: {crossToFirstM:F0} m from segment " +
                        $"{_currentSegmentIndex} (width {firstSeg.PathWidth:F0} ft) with the " +
                        $"aircraft off the runway — concluding rather than steering across it");
                    // Same reason split as the guard's original site: only claim "short of X"
                    // when an early vacate was actually established.
                    if (_landingExitVacatedEarlyPlannedName != null)
                        _landingExitVacatedEarly = true;
                    else
                        _landingExitRouteUnreachable = true;
                    _rolloutHandoffActive = false;
                    SetState(TaxiGuidanceState.Taxiing);
                    HandleArrival();
                    return;
                }
            }
```

- [ ] **Step 5: Verify the resulting order by reading the block**

Read `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` from the
`if (turnBegun || exitedLaterally || …)` line through its `return;`. Confirm the order is:

1. `RolloutDiag` handoff line
2. `_rolloutHandoffActive = true;`
3. `earlyVacateSwapped` declaration
4. early-vacate block (match → announce + swap + `earlyVacateSwapped = true`; no match → conclude + return)
5. re-route block (`LoadRoute`, carry-across restores, diag) — **no guard inside it any more**
6. conclude-on-swap-with-failed-reroute + return
7. fallback re-anchor of `_currentSegmentIndex`
8. reachability guard (reads `_currentSegmentIndex`) + return
9. no-graph `StopGuidance` block + return
10. `SetState(Taxiing); _steeringTone.Resume(); return;`

If the order differs, fix it — steps 7 and 8 must be adjacent and in that order, or the
guard reads a stale cursor.

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs
git commit -m "fix(landing-exit): never resume on a route to the exit the pilot left short of

Two holes on the handoff's failure paths. After the early-vacate swap announced
a substitute exit, a failed LoadRoute fell through to a re-anchor on the
touchdown route -- which still targets the planned exit -- and the reachability
guard, gated on handoffRerouted, never ran on that path at all.

The swap case now concludes, and the guard moved below the re-anchor to test the
segment at _currentSegmentIndex: the one the tone is actually about to steer at,
whichever route survived.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Correct the changelog and the design doc

Closes finding **R4**. The fragment promises a steady tone through the whole rollout;
`SelectToneMode` deliberately returns `Silent` for a same-side deviation at or above 2°
within the 1,000 ft window, so the stretch before a known-side exit is intentionally quiet.

**Files:**
- Modify: `changelog.d/204-landing-exit-early-turn.fix.md`
- Modify: `docs/design/2026-08-21-landing-exit-early-turn-design.md` (the `SelectToneMode` table)

- [ ] **Step 1: Amend the fragment**

Replace the whole contents of `changelog.d/204-landing-exit-early-turn.fix.md` with:

```markdown
Landing exit guidance no longer mistakes a drift for the exit turn. Sliding off
the centreline while slowing down — especially away from the exit you picked —
used to hand you off early and then pan the steering tone hard at a taxiway you
could not reach without leaving the pavement. You now get a tone steering you
back to the centreline through the quiet middle of the rollout, and the turn
only counts once you are near your exit and turning its way. Close to the exit,
a deviation onto its side goes quiet rather than fighting a turn you are plainly
already making. If you do come off at a different taxiway, guidance says so and
follows you onto it, or tells you plainly that it has ended — instead of quietly
routing you the long way round to the exit you skipped.
```

Do NOT create a new fragment — this one already belongs to PR #204.

- [ ] **Step 2: Add the silent window to the design doc's tone table**

Open `docs/design/2026-08-21-landing-exit-early-turn-design.md` and find the
`SelectToneMode` table (it lists the ground-speed `Silent` row, the `ExitBearing` row and
the `DriftCorrection` row, against the original two-parameter signature).

Add a row for the toward-exit silent window above the `DriftCorrection` row, and note the
signature change, so the table matches the shipped four-parameter method:

```markdown
| `distToExit <= TurnWindowFeet` and the deviation is ≥ `DriftToneSilentDeg` toward a KNOWN exit side | `Silent` | Added after this doc was first written (the drift-tone-conflict fix). Heading alone cannot separate a crosswind drift toward the exit from the pre-turn onto it, so the tone stays quiet rather than opposing a turn `IsExitTurnBegun` is about to accept. Accepted cost: a genuine toward-exit drift in this band is uncued until 15°, the 300 ft `ExitBearing` takeover, or `exitedLaterally`. |
```

Immediately below the table, add:

```markdown
The shipped signature is therefore
`SelectToneMode(groundSpeedKts, distToExitFeet, headingDeltaSignedDeg, exitRelativeBearingDeg)`
— four parameters, not the two this section originally described.
```

- [ ] **Step 3: Commit**

```bash
git add changelog.d/204-landing-exit-early-turn.fix.md docs/design/2026-08-21-landing-exit-early-turn-design.md
git commit -m "docs(landing-exit): describe the toward-exit silent window accurately

The fragment promised a steady tone through the whole rollout, but the tone
deliberately goes quiet for a deviation onto a known exit's side inside the
1,000 ft window rather than fighting a turn the gate is about to accept. The
design doc's tone table predates that window and its two-parameter signature.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Final verification

- [ ] **Full build:** `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`, 0 errors, 0 new warnings.
- [ ] **Full suite:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → all pass.
- [ ] **No stray literals:** `grep -rn "halfRunwayWidthFt + 30" MSFSBlindAssist/` returns nothing; `grep -rn "EXIT_COVERAGE_GAP_FT = 1400" MSFSBlindAssist/` returns nothing; `grep -rn "ExitBearingTrue != 0.0 ? NormalizeAngle" MSFSBlindAssist/` returns nothing.
- [ ] **Push and update the PR body** with the in-sim test plan below.

## In-sim test plan (for the PR body — the repo owner runs this)

Sim-facing paths cannot be unit-tested. These four scenarios cover every behaviour change:

1. **Early vacate, matched substitute.** Land and vacate at an unplanned exit well over
   1,000 ft short of the planned one. Expect: *"Left the runway short of taxiway X. Now
   following taxiway Y."* then guidance along the taxiway actually taken. Never a route
   back toward the planned exit.
2. **Early vacate, no match.** Vacate onto pavement with no mapped exit nearby. Expect the
   *"You have left the runway short of X. Exit guidance ended…"* closure and silence — no
   re-route.
3. **Vacate at the planned exit with an offset first segment.** Expect the new
   *"Exit guidance ended: no usable route from here…"* closure, and specifically **not**
   "left the runway short of X".
4. **Normal exit, no drama.** Confirm the 0.856 m trigger shift changed nothing
   perceptible: the handoff still fires at the same point in the turn, and the tone behaves
   as it did before.

Attach `%APPDATA%\MSFSBlindAssist\logs\landing_exit.log` and `taxi_guidance.log` for each run.
