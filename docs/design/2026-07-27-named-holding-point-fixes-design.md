# Named holding point resolver + terminator UI — review fixes

**Date:** 2026-07-27
**Branch:** `feat/named-holding-points` (PR #164)
**Scope:** follow-up fixes to the "Hold at named holding point" Progressive Taxi terminator

## Background

PR #164 adds published NAMED holding points (VIKAS, HANLI, N2E, A11…) as a fifth
Progressive Taxi terminator type. A code review of that PR raised eight items. This
document records which of them survive contact with real data, what changes as a
result, and — importantly — which proposed "fixes" were **measured and rejected**, so
a future session does not re-attempt them.

## Evidence base

All numbers below come from the repo owner's `fs2024.sqlite` navdata joined against
live Overpass `node[aeroway=holding_position]` data for six airports (EGLL, EDDF,
LOWW, LFPG, EHAM, KJFK). The probe replicated `TaxiGraph` node construction (1.5 m
merge radius, `Normal < HoldShort < ILSHoldShort` type upgrade, equirectangular
distance) and `NamedHoldingPointResolver.Resolve` exactly.

### The snap policy is correct as written — do not change it

The review hypothesised that `holding_position:type=runway`/`ILS` points could fall
back to a plain centerline vertex on the **runway side** of the painted hold line.
Measured, that does not happen: at EGLL every runway- and ILS-kind point already
snaps to a designated HS/IHS node. Two candidate hardenings were tested and both are
worse:

| policy | EGLL | EDDF | LOWW | EHAM |
| --- | --- | --- | --- | --- |
| current (ship this) | 85 resolved | 69 | 40 | 22 |
| require designated for runway/ILS | 85 | **55** | 40 | **19** |
| reject runway-ward snaps | **74** | 67 (designated 53 → 31) | **29** | 22 |

- **Requiring a designated node for runway/ILS kinds** loses 14 real points at EDDF
  and 3 at EHAM, and gains nothing anywhere.
- **Rejecting any snap that moves the target closer to a runway centerline** rejects
  *correct designated nodes*: navdata's HS node routinely sits up to 14 m runway-ward
  of OSM's painted line (EDDF), so the guard fires on the very matches the design
  depends on. Designated snaps collapse 53 → 31 at EDDF and 22 → 6 at LOWW.

**Widening `DESIGNATED_SNAP_M` to the full `MAX_SNAP_M` is actively dangerous.**
Coverage is identical at all six airports, but of the 7 points that change target,
4 jump onto a *different* hold line (a fifth, EDDF Y5, moves 15 m runway-ward; the
remaining two move harmlessly away from the runway):

| point | kind | current | widened |
| --- | --- | --- | --- |
| EDDF M15 | runway | plain @ 2.0 m, 218 m from centerline | HS @ 23.7 m, **126 m** from centerline |
| EDDF P16 | ILS | plain @ 10.3 m, 201 m | HS @ 16.5 m, **95 m** |
| EDDF P20 | ILS | plain @ 15.2 m, 206 m | HS @ 17.3 m, **95 m** |
| EHAM 18L-36R | runway | plain @ 3.7 m, 154 m | HS @ 15.7 m, **86 m** |

The 15 m designated preference is tight precisely so it can only select the hold line
the online point actually sits on. Under the current policy the 30 m cap bounds how far
a target can move runway-ward — 26.5 m worst case across the six airports, and every
case at an intermediate or untagged hold far from any runway.

**Decision: the snap policy ships unchanged.** Review finding #2 is withdrawn.

### Disconnected taxi-graph islands are common

Component counts: LOWW 6, KJFK 6, EHAM 4, GCLP 2 (the known 13-node S5 island);
EGLL, EDDF, LFPG, KSFO 1 each. Islands of 2–13 nodes are routine, so a named hold
resolving onto one the aircraft cannot reach is a real possibility.

### `holding_position:type` spellings

Only `runway`, `ILS`, `intermediate` and untagged were observed across all six
airports (436 named nodes). Case normalization is defensive only.

## Changes

### 1. `Navigation/NamedHoldingPointResolver.cs`

**`SnappedToDesignatedNode` reports the truth.** Today the flag means "won via the
≤15 m designated path", so a designated node selected through the `plain` tracker
(>15 m out) is reported as a plain snap. It becomes derived from the chosen node's
actual type.

**This must not change duplicate-name ranking.** `Beats` uses the flag today, so
making it truthful would promote a designated node at 20 m over a plain node at 18 m —
reintroducing the EDDF M15 hazard for duplicate names. The two concepts therefore
split:

- `SnappedToDesignatedNode` (public, diagnostic) — describes the chosen node.
- a private "won the ≤15 m designated preference" key — the only thing `Beats` reads.

Ranking behaviour is unchanged, and a test pins that.

**`Kind` normalization.** Trimmed on construction; `DisplayLabel` matches
case-insensitively. No behavioural change on observed data.

**Unchanged:** `DESIGNATED_SNAP_M` (15 m), `MAX_SNAP_M` (30 m), the parking-node
exclusion, the plain-node fallback, the drop rule, and the alphabetical ordering.

### 2. `Forms/TaxiAssistForm.cs`

**Component guard.** `case 4` verifies the resolved node's `ComponentId` matches the
aircraft's `destComponentId` (already computed for the sibling terminator cases). On
mismatch it announces *"Cannot taxi to VIKAS from your position. Check your entry."*,
mirrors it to `lblStatus`, and returns — the same shape as the existing
`destNode < 0` errors. The point stays listed in the combo; a complete list plus a
specific refusal beats silently omitting a point the pilot is looking for.

**Repopulate after an airport load.** `LoadAirportData` clears
`cmbTerminatorHoldPoint.Items` and re-resolves `_namedHoldingPoints`, but never
refills the combo — so after switching ICAO the combo reads as empty to a screen
reader until the dropdown is opened (arrowing a `DropDownList` does not fire
`DropDown`). `PopulateTerminatorHoldPointList()` is called immediately after
`ResolveNamedHoldingPoints()`, matching how `RebuildHoldShortRunwayCombo` is already
re-run for `cmbTerminatorRunway`.

**Re-resolve latch.** `PopulateTerminatorHoldPointList` currently re-runs the
O(points × nodes) scan whenever the list is empty. When the online source has named
points but all of them fail the 30 m test, that rescans on every dropdown open and
every taxiway row add/remove. A `_namedHoldingPointsResolved` flag — set only when the
raw source actually had points, cleared per airport load — gates the retry:

| raw points | resolved | latch | retry on populate |
| --- | --- | --- | --- |
| 0 (fetch not landed, or none published) | 0 | false | yes — cheap, early-returns without scanning |
| >0 | >0 | true | no |
| >0 | 0 (all dropped) | true | no — this is the case that rescans today |

The late-background-fetch retry the current code exists to support is preserved.

**Dead branch.** The `destNode < 0` message composer gains an explicit index-4 branch
so it cannot render `runway ` with an empty target if the early return is ever relaxed.

**Diagnostics.** `ResolveNamedHoldingPoints` logs through `Log.Channel("taxi_router")`:
one summary line (raw / distinct / resolved counts plus dropped names) and one line per
resolved point (name, node id, snap distance, designated, kind). Bounded at ~100 lines
and, with the latch, once per airport load. This is exactly the data that would have
answered today's questions from a user's log instead of an ad-hoc probe.

### 3. Tests

New cases in `tests/MSFSBlindAssist.Tests/NamedHoldingPointResolverTests.cs`:

- Parking nodes never match — currently the only resolver safety rule with no test.
- `SnappedToDesignatedNode` is true when a designated node is chosen through the plain
  path (>15 m).
- Duplicate-name ranking is unchanged by that fix: a plain node at 18 m still beats a
  designated node at 20 m.
- `Kind` casing and surrounding whitespace produce the correct `DisplayLabel`.

### 4. Documentation

- `docs/taxi-guidance.md` — record the component guard, and record the rejected snap
  policies with the EDDF M15 number so they are not re-attempted.
- `CLAUDE.md` — one invariant bullet in the taxi-guidance list:
  never widen `DESIGNATED_SNAP_M` toward the search radius, and never require a
  designated node for runway/ILS kinds.

## Out of scope

- The snap radii, the fallback, and the drop rule (measured; see above).
- `HoldShortNodeResolver` / `InsertRunwayCrossingHoldShorts` — the CLAUDE.md deferral
  on feeding OSM holding positions into hold-short *derivation* stands untouched.
- Filtering unreachable points out of the combo (rejected: silent omission).

## Verification

1. `dotnet build MSFSBlindAssist.sln -c Debug` (x64; app closed — the exe is file-locked).
2. `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
3. Re-run the six-airport probe against the changed resolver logic and confirm resolved
   counts are identical to the table above — the flag fix must not move any target.

In-sim testing stays with the repo owner; the PR's existing test plan is unchanged, plus
one addition: switch ICAO with the terminator type already set to "Hold at named holding
point" and confirm the combo is populated without opening the dropdown.
