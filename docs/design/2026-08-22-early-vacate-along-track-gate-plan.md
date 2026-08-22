# Early-Vacate Along-Track Gate — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the landing-rollout early-vacate retarget recognise a vacate onto a *different* exit that lies within 1,000 ft of the planned one, instead of silently routing the pilot to the exit they skipped.

**Architecture:** Replace the branch's straight-line distance test with an **along-track** one, as a pure predicate on `Navigation/RolloutExitGate.cs`. The threshold (350 ft) is derived from the 5 m overlap between the exit-node corridor (`halfWidth + 15 m`) and the runway-clear boundary (`halfWidth + 10 m`), and is only valid because the caller already requires the aircraft to be laterally clear. Two tasks: the pure rule with its tests, then the caller and the docs.

**Tech Stack:** .NET 10, C# 13, xUnit. Design: [2026-08-22-early-vacate-along-track-gate-design.md](2026-08-22-early-vacate-along-track-gate-design.md).

## Global Constraints

- **Build the SOLUTION, never the bare csproj:** `dotnet build MSFSBlindAssist.sln -c Debug`. A bare `dotnet build` on the `.csproj` defaults to `Platform=AnyCPU` and writes to a different folder than the x64 run path.
- **Test command:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- **Single-test filter:** append `--filter "FullyQualifiedName~ClassName"`.
- **Suite is currently green at 3304 tests.** It must stay green.
- **Branch:** `fix/landing-exit-early-turn` (PR #204). Never commit to `main`.
- **Do NOT change** `TurnWindowFeet` (1000.0), `ExitSideMinBearingDeg` (3.0), `RunwayClearMarginM` (10.0), `EarlyVacateForwardSlackFeet` (600.0), `EarlyVacateMaxPassedFeet` (1400.0), `TurnBegunHeadingDeg` (15.0), or `TurnMaxGroundSpeedKts` (90.0).
- **Do NOT touch `HasKnownExitSide` or `ExitSide`.** The related finding about turnaround exits was reviewed and deliberately closed as accepted on 2026-08-22 — see the design doc's "Finding 5" paragraph.
- **`RolloutExitGate` must stay free of Services/graph dependencies** — its class doc promises this.
- **No new changelog fragment.** `changelog.d/204-landing-exit-early-turn.fix.md` already covers this PR, and the design doc explains why its existing wording needs no edit: this fix makes an already-written sentence true.
- **Sign convention:** `signedAlongPast*` is POSITIVE when the aircraft is PAST the exit. "Short of the exit" is therefore a NEGATIVE value.
- If MSFSBlindAssist.exe is running the build fails with MSB3021 (file lock). Report it; do not kill the user's app.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `MSFSBlindAssist/Navigation/RolloutExitGate.cs` | Gains `VacatedShortAlongTrackFeet` and `IsVacateAwayFromPlannedExit` — the pure rule | 1 |
| `tests/MSFSBlindAssist.Tests/EarlyVacateAlongTrackGateTests.cs` | **New.** Pins the rule, its boundary, and its geometric realism | 1 |
| `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs` | The caller — `farFromPlannedExit` at line 495 | 2 |
| `CLAUDE.md` | Taxi-guidance invariant list — the early-vacate bullet | 2 |
| `docs/taxi-guidance.md` | Flow step 7's description of the branch entry condition | 2 |

---

### Task 1: The pure along-track rule

**Files:**
- Modify: `MSFSBlindAssist/Navigation/RolloutExitGate.cs` (add a constant and a method in the "Early-vacate matching" region, near `EarlyVacateMaxPassedFeet` ~line 113)
- Test: `tests/MSFSBlindAssist.Tests/EarlyVacateAlongTrackGateTests.cs` (create)

**Interfaces:**
- Produces: `RolloutExitGate.VacatedShortAlongTrackFeet` (`double`, 350.0) and
  `RolloutExitGate.IsVacateAwayFromPlannedExit(bool pastPlannedExit, double signedAlongPastPlannedFeet, double distToPlannedExitFeet) -> bool`. Task 2 calls this.
- Consumes: `RolloutExitGate.TurnWindowFeet` (pre-existing, 1000.0).

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/EarlyVacateAlongTrackGateTests.cs`:

```csharp
// Characterization tests for RolloutExitGate.IsVacateAwayFromPlannedExit — "did the pilot
// leave the runway somewhere OTHER than the exit they picked?"
//
// This is asked ONLY when the aircraft is already laterally clear of the pavement (the
// caller's own conjunct), and that is what makes the 350 ft threshold sound rather than
// tuned. GetLandingExits refuses any exit node more than halfWidth + 15 m off the axis,
// while "clear of the runway" is halfWidth + 10 m — so the node corridor extends exactly
// 5 m past the clear boundary. An aircraft off the pavement on its OWN exit, leaving the
// axis at angle theta, can therefore be at most 5 m / tan(theta) short of that exit's
// node: 313 ft at the 3-degree floor, 61 ft at 15 degrees. Distinct turnoffs are measured
// 430-970 ft apart, so the two populations do not overlap.
//
// signedAlongPastPlannedFeet is POSITIVE when the aircraft is PAST the exit, so "short of
// the exit" is NEGATIVE.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class EarlyVacateAlongTrackGateTests
{
    // The motivating case: exits ~800 ft apart on a parallel-taxiway layout, pilot vacates
    // at the neighbour. Before this rule the straight-line 1,000 ft gate said "not far
    // enough" and the handoff re-routed to the exit that was skipped.
    [Fact]
    public void VacateEightHundredFeetShort_IsAwayFromThePlannedExit()
    {
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -800.0,
            distToPlannedExitFeet: 805.0));
    }

    // A genuine turn onto the PLANNED exit. Once laterally clear, a 15-degree exit's node
    // is at most 61 ft ahead — nowhere near 350 — so the branch must not be entered and the
    // planned exit is kept.
    [Fact]
    public void GenuineTurnOntoThePlannedExit_IsNotAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -61.0,
            distToPlannedExitFeet: 70.0));
    }

    // The worst admissible case: a 3-degree exit, the shallowest angle that has a side at
    // all, puts its own node at most 313 ft ahead. Still inside the threshold.
    [Fact]
    public void ShallowestAdmissibleExit_AtItsGeometricLimit_IsNotAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -313.0,
            distToPlannedExitFeet: 320.0));
    }

    // Boundary, both sides. Asserted at the next representable double so a strict/inclusive
    // mutation is caught.
    [Fact]
    public void BoundaryIsThreeHundredAndFiftyFeetShort()
    {
        double atThreshold = -RolloutExitGate.VacatedShortAlongTrackFeet;

        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, atThreshold, 360.0));
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            false, Math.BitDecrement(atThreshold), 360.0));
    }

    // Past the exit short-circuits regardless of the other two arguments — the overshoot
    // detector owns that case, not the early-vacate retarget.
    [Fact]
    public void PastThePlannedExit_IsNeverAnEarlyVacate()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(true, -5000.0, 5000.0));
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(true, 900.0, 900.0));
    }

    // A POSITIVE along-track value means past the exit, and must never read as "short of"
    // it even when the caller has not set pastPlannedExit.
    [Fact]
    public void PositiveAlongTrack_NeverReadsAsShortOfTheExit()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, 800.0, 805.0));
    }

    // The straight-line clause is NOT redundant. An aircraft that has driven a long way off
    // the side reads a small along-track distance while being nowhere near the exit; the
    // 1,000 ft straight-line test still catches it.
    [Fact]
    public void FarOffToTheSide_IsCaughtByTheStraightLineClause()
    {
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -100.0,   // barely short along the runway
            distToPlannedExitFeet: 1200.0));      // but 1,200 ft away in a straight line
    }

    // Sanity: with both clauses false the answer is false, so the branch stays closed on an
    // ordinary near-exit handoff.
    [Fact]
    public void NearTheExitAndOnAxis_IsNotAVacateAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, -20.0, 25.0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EarlyVacateAlongTrackGateTests"`

Expected: **compile error** — `'RolloutExitGate' does not contain a definition for 'IsVacateAwayFromPlannedExit'` (and for `VacatedShortAlongTrackFeet`).

- [ ] **Step 3: Add the constant and the predicate**

In `MSFSBlindAssist/Navigation/RolloutExitGate.cs`, in the `// ---- Early-vacate matching.`
region, immediately AFTER the `EarlyVacateMaxPassedFeet` declaration (~line 113) and before
the `// ---- Handoff route reachability.` comment, insert:

```csharp
    /// <summary>
    /// How far SHORT of the planned exit, measured ALONG the runway, the aircraft must be
    /// before leaving the pavement counts as vacating somewhere else.
    ///
    /// <para>Derived, not fitted. <c>TaxiGraph.GetLandingExits</c> refuses any exit node more
    /// than <c>halfWidth + 15 m</c> off the runway axis, while
    /// <see cref="IsLaterallyClearOfRunway"/> puts the pavement boundary at
    /// <c>halfWidth + <see cref="RunwayClearMarginM"/></c> (10 m) — so the node corridor
    /// extends exactly 5 m beyond the clear boundary. An aircraft that is laterally clear, on
    /// an exit path leaving the axis at angle θ, gains lateral offset at <c>tan θ</c> per unit
    /// of along-track, so its OWN exit's node can be at most <c>5 m / tan θ</c> ahead:
    /// 95.4 m = 313 ft at the <see cref="ExitSideMinBearingDeg"/> (3°) floor, 61 ft at the
    /// 15° minimum <see cref="IsExitTurnBegun"/> can fire for. 350 rounds up the worst case.</para>
    ///
    /// <para>The empirical companion: distinct turnoffs are measured 430–970 ft apart (266
    /// runway directions across 39 airports; median 672, p95 968). The "own exit" population
    /// tops out at 313 ft and the "different exit" population starts around 430 ft, so this is
    /// a separation between two populations rather than a threshold tuned to one case.</para>
    ///
    /// <para>This does NOT contradict <see cref="TurnWindowFeet"/>. That 558 ft derivation is
    /// for an aircraft ON THE CENTRELINE, which is where <see cref="IsExitTurnBegun"/> fires;
    /// this gate only ever runs once the aircraft is OFF the pavement, which has already
    /// consumed all but 5 m of the same corridor. Two lateral states, two correct numbers.</para>
    /// </summary>
    public const double VacatedShortAlongTrackFeet = 350.0;

    /// <summary>
    /// Did the pilot leave the runway somewhere OTHER than the exit they picked?
    ///
    /// <para>The caller must additionally require the aircraft to be laterally CLEAR of the
    /// runway. That conjunct is not incidental — it is what makes
    /// <see cref="VacatedShortAlongTrackFeet"/>'s derivation valid, and it is independently
    /// what keeps a legitimate turn begun 800 ft out ON the runway from reaching this rule at
    /// all.</para>
    ///
    /// <para>The straight-line clause is kept beside the along-track one and is NOT redundant:
    /// the caller places no upper bound on lateral offset, so an aircraft that has driven a
    /// long way off the side can read a small along-track distance while being nowhere near
    /// the exit.</para>
    /// </summary>
    /// <param name="pastPlannedExit">
    /// True once the aircraft is beyond the planned exit along the runway. The overshoot
    /// detector owns that case, so this rule always answers false for it.
    /// </param>
    /// <param name="signedAlongPastPlannedFeet">
    /// Along-runway distance from the planned exit to the aircraft, FEET, POSITIVE when the
    /// aircraft is PAST the exit. "Short of the exit" is therefore negative.
    /// </param>
    /// <param name="distToPlannedExitFeet">Straight-line distance to the planned exit, feet.</param>
    public static bool IsVacateAwayFromPlannedExit(
        bool pastPlannedExit,
        double signedAlongPastPlannedFeet,
        double distToPlannedExitFeet)
        => !pastPlannedExit
           && (distToPlannedExitFeet > TurnWindowFeet
               || -signedAlongPastPlannedFeet > VacatedShortAlongTrackFeet);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~EarlyVacateAlongTrackGateTests"`

Expected: **PASS**, 8 tests.

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: 3312 passed, 0 failed (3304 + 8).

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Navigation/RolloutExitGate.cs tests/MSFSBlindAssist.Tests/EarlyVacateAlongTrackGateTests.cs
git commit -m "feat(landing-exit): along-track rule for vacating away from the planned exit

Asks how far SHORT of the planned exit the aircraft is ALONG the runway, rather
than how far away it is in a straight line. Valid only under the caller's
laterally-clear conjunct, which is what bounds it: the exit-node corridor
extends 5 m beyond the runway-clear boundary, so an aircraft off the pavement on
its OWN exit can be at most 5 m / tan(theta) short of that exit's node -- 313 ft
at the shallowest admissible angle. Distinct turnoffs are measured 430-970 ft
apart, so the populations do not overlap.

Not wired up yet.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Wire it into the handoff, and correct the docs

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs:495-496` (and the comment above it, lines ~486-493)
- Modify: `CLAUDE.md:367` (the early-vacate invariant bullet)
- Modify: `docs/taxi-guidance.md:730` (Flow step 7's branch-entry description)

**Interfaces:**
- Consumes: `RolloutExitGate.IsVacateAwayFromPlannedExit(bool, double, double)` and `RolloutExitGate.VacatedShortAlongTrackFeet` from Task 1.
- Produces: nothing new.

**Context the implementer needs:** both arguments already exist in `UpdateLandingRollout`
above the handoff block — `distToExitFeet` (~line 305) and `signedAlongPastFt` (line 324).
Do not recompute either. `pastExit` (~line 328) is `signedAlongPastFt > 0.0`.

- [ ] **Step 1: Replace the gate at the call site**

In `MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs`, replace lines 495-496:

```csharp
            bool farFromPlannedExit = !pastExit
                && distToExitFeet > Navigation.RolloutExitGate.TurnWindowFeet;
```

with:

```csharp
            bool farFromPlannedExit = Navigation.RolloutExitGate.IsVacateAwayFromPlannedExit(
                pastExit, signedAlongPastFt, distToExitFeet);
```

- [ ] **Step 2: Correct the comment above it**

Immediately above `bool offRunwayAtHandoff` (~line 494) sits this comment block. Replace its
last three lines — exactly these:

```
            // it; and the distance gate reuses the same window as IsExitTurnBegun, because
            // ROLLOUT_NEAR_EXIT_FT (500) would classify a legitimate turn begun 800 ft out
            // as an early vacate.
```

with:

```
            // it; and the distance gate is now ALONG-TRACK (RolloutExitGate.
            // IsVacateAwayFromPlannedExit), not straight-line: measured along the runway and
            // read under the lateral conjunct above, an aircraft on its OWN exit's pavement
            // can be at most ~313 ft short of that exit's node, while distinct turnoffs sit
            // 430-970 ft apart. The old straight-line TurnWindowFeet test alone left a 700 ft
            // band in which a vacate onto a NEIGHBOURING exit re-routed to the planned one.
            // The 500 ft tightening rejected earlier does not apply here — it reasoned about a
            // turn begun ON the runway, which the lateral conjunct already excludes.
```

Keep the rest of the comment block (the part explaining why the lateral gate is load-bearing)
exactly as it is.

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: 3312 passed, 0 failed — unchanged from Task 1, since this task adds no test.

(No unit test in this step by design: `UpdateLandingRollout` needs a live
`TaxiGuidanceManager`, SimConnect state and the announcer, so it is sim-facing. The repo's
CLAUDE.md requires a written in-sim plan for such paths instead, and one is in the design doc.
Do not invent a test that constructs a `TaxiGuidanceManager`.)

- [ ] **Step 4: Update the CLAUDE.md invariant**

`CLAUDE.md:367` currently reads:

```
- After an early vacate the handoff must never re-route to the PLANNED exit — with no runway edges in the graph, A* routes between two exits the long way round (KSEA: 1,678 m up the parallel taxiway and back). Retarget or conclude. → [taxi-guidance.md](docs/taxi-guidance.md)
```

Replace it with:

```
- After an early vacate the handoff must never re-route to the PLANNED exit — with no runway edges in the graph, A* routes between two exits the long way round (KSEA: 1,678 m up the parallel taxiway and back). Retarget or conclude. "Early vacate" is ALONG-TRACK (`RolloutExitGate.IsVacateAwayFromPlannedExit`, 350 ft short) under a laterally-clear conjunct, never straight-line distance alone: the exit-node corridor runs just 5 m past the runway-clear boundary, so an aircraft off the pavement on its OWN exit is at most ~313 ft short of it, while distinct turnoffs sit 430-970 ft apart. A straight-line `TurnWindowFeet` test alone left a 700 ft band in which vacating onto a NEIGHBOURING exit re-routed to the planned one. → [taxi-guidance.md](docs/taxi-guidance.md)
```

- [ ] **Step 5: Update docs/taxi-guidance.md**

At `docs/taxi-guidance.md:730`, Flow step 7 contains the phrase:

```
entered only when the aircraft is both laterally off the runway and still more than `TurnWindowFeet` from the planned exit
```

Replace that phrase with:

```
entered only when the aircraft is both laterally off the runway and more than `RolloutExitGate.VacatedShortAlongTrackFeet` (350 ft) short of the planned exit **measured along the runway** (`IsVacateAwayFromPlannedExit`, which also keeps the original straight-line `TurnWindowFeet` test for an aircraft that has driven far off to the side)
```

Leave the rest of that paragraph unchanged. Then, in the same paragraph, after the sentence
ending "…if none matches.", add:

```
The along-track form is what makes the branch fire for a vacate onto a NEIGHBOURING exit: exits commonly sit 430-970 ft apart, so a straight-line 1,000 ft test read those as the planned exit's own turn and re-routed to the exit the pilot had skipped. It is sound because the branch already requires the aircraft to be laterally clear — the exit-node corridor (`halfWidth + 15 m`) runs only 5 m past the runway-clear boundary (`halfWidth + 10 m`), so an aircraft off the pavement on its OWN exit can be at most `5 m / tan(exitAngle)` short of that exit's node: 313 ft at the 3° floor, 61 ft at 15°.
```

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.Rollout.cs CLAUDE.md docs/taxi-guidance.md
git commit -m "fix(landing-exit): recognise a vacate onto a neighbouring exit

Exits commonly sit 430-970 ft apart, so the straight-line 1,000 ft gate read a
turn onto a NEIGHBOURING exit as the planned exit's own turn and re-routed to the
exit the pilot had just skipped -- the long-way-round this PR exists to prevent,
inside a window where the code did not recognise the vacate as a vacate. Neither
existing safeguard caught it: the reachability guard measures only the first
segment, and the overshoot monitor clears itself on the next frame.

The gate is now along-track under the existing laterally-clear conjunct.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Final verification

- [ ] **Full build:** `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`, 0 errors, no new warnings.
- [ ] **Full suite:** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → 3312 passed.
- [ ] **No stray old gate:** `grep -n "distToExitFeet > Navigation.RolloutExitGate.TurnWindowFeet" MSFSBlindAssist/` returns nothing.
- [ ] **Finding 5 untouched:** `git diff main...HEAD -- MSFSBlindAssist/Navigation/RolloutExitGate.cs | grep -c "HasKnownExitSide"` shows no change to that method's body.
- [ ] Push, and add the in-sim scenarios below to the PR body.

## In-sim test plan (for the PR body — the repo owner runs this)

1. **Vacate ~800 ft short onto a mapped neighbouring exit.** EGLL 09R S5W/N5E is a mapped ~900 ft pair. Expect the retarget announcement naming both taxiways, then guidance along the taxiway actually taken — never a route back toward the planned exit.
2. **Vacate ~400 ft short onto pavement carrying no mapped exit.** Expect *"You have left the runway short of Taxiway X. Exit guidance ended…"*. **This is the new behaviour** — previously you were silently routed to the planned exit. Confirm it is not startling in the air.
3. **Normal turn onto the planned exit.** Expect no change at all: no retarget, no closure, guidance continues to the planned exit.
4. **KSEA 34L, the original >1,000 ft case.** Expect unchanged behaviour.

Attach `%APPDATA%\MSFSBlindAssist\logs\landing_exit.log` and `taxi_guidance.log` for each run.
