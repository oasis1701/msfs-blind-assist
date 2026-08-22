# Test Wave 2 — Navigation/Taxi pure-logic cluster

Branch: `worktree-agent-ac46b7e878ff133ce` (isolated worktree, already checked out at session start).

## Summary

- Build: `dotnet build MSFSBlindAssist.sln -c Debug` → **0 Warning(s), 0 Error(s)**.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
  → **721 passed, 0 failed** (baseline 619 + **102 new tests**).
- Zero production *logic* edits. Three access-modifier promotions only (all in
  `Navigation/TaxiRouter.cs`), documented below.

## Per-target results

### 1. HoldShortNodeResolver (`Navigation/HoldShortNodeResolver.cs`) — SAFETY-CRITICAL

File: `tests/MSFSBlindAssist.Tests/HoldShortNodeResolverTests.cs` (15 tests).

Built a synthetic `TaxiGraph` via `TaxiGraph.Build(paths, [], [])` with a runway
running due TRUE NORTH so along-track = pure latitude offset and cross-track =
pure longitude offset (hand-verifiable geometry that still exercises the real
`RunwayFrame` projection). Covered:

- Tier 1 (designated node on the cleared taxiway, closest-to-runway wins).
- The runway-name gate on `HoldShortName`: accepts the reciprocal designator
  ("runway 27" matches target "09"), rejects a mismatched designator ("runway
  18"), falls through to the plain node when rejected.
- Tier 3 (plain node on the cleared taxiway, no HS candidate there).
- Tier 4 (no candidate on the cleared taxiway at all, but a designated node
  sits at the SAME junction as the legacy geometric pick — designated wins).
- Tier 5 (a designated node 200 m further down the runway — outside the 75 m
  junction window — must NOT out-rank the nearby plain node; pins the exact
  "far-along HSND can't hijack the natural hold point" invariant from the
  class doc).
- `HeadingExitSideSign` (east/west/parallel-heading cases).
- `LegacySetbackMetres` (the deliberate ft-read-as-m quirk, including the
  15 m floor clamp).
- `TrueHalfWidthMetres` (correct ft→m conversion + default fallback).

**Promotions:** none — `ResolveNearSide`, `HeadingExitSideSign`,
`LegacySetbackMetres`, `TrueHalfWidthMetres` are already `public static`.

**Gotcha hit and fixed during characterization:** two early test fixtures
placed a plain node's lateral distance exactly on the `legacyMinLateralM`
(100 m) boundary. The resolver's `RunwayFrame` and the test's own coordinate
helper compute their east/west metres-per-degree scale from two very slightly
different reference latitudes (`aircraftLat` vs a fixed constant), so the
node's *computed* cross-track landed a few micrometres either side of exactly
100 — enough to flip the `< legacyMinLateralM` boundary check and pick the
wrong node. Fixed by moving both fixtures' lateral values safely clear of the
boundary (105 m instead of 100 m). This is a test-fixture-precision issue, not
a production bug — the resolver's own boundary literal (100.0) is untouched.

**Explicitly out of scope (Services, not Navigation):** the "reciprocal-aware
crossing test (the KSFO 'D' same-named-taxiway-past-a-crossing case)" lives in
`ApplyUserRunwayHoldShorts` in `Services/TaxiGuidanceManager.Routing.cs` /
`Forms/TaxiAssistForm.cs`, not in `Navigation/*`. Skipped per the cluster
boundary in the task brief. The "user 'end of taxiway' label never
overwritten" invariant is already pinned by the existing
`RouteRunwayCrossingsTests.ComposeCrossingLabel_preserves_a_user_end_of_taxiway_hold`
test (pre-existing file, not touched).

### 2. NavigationCalculator (`Navigation/NavigationCalculator.cs`)

File: `tests/MSFSBlindAssist.Tests/NavigationCalculatorTests.cs` (30 tests,
several `[Theory]`-driven).

Confirmed the class has **no** dependency on `MSFSBlindAssist.SimConnect` — it
is pure math (bearing, distance, magnetic variation, touchdown aim point,
cross-track error, intercept headings, glideslope deviation w/ Earth-curvature
correction, ILS/glideslope range checks, `IsApproachingFromBehind` incl. the
0.1 NM safety cutoff, `IsOnLocalizer`, extension heading). All hand-computable
at round cardinal-direction coordinates (north/south/east/west bearings, 1°
latitude ≈ 60.04 NM, on-centerline branches of the two intercept-heading
functions, threshold-distance-zero glideslope deviation, etc.).

**Promotions:** none — every target method is already `public static`.

**Skipped:** `CalculateThreeInterceptHeadings` (thin wrapper around the
already-pinned `CalculateAngledInterceptHeading`, three fixed angle constants
— low marginal value for the effort of hand-deriving three simultaneous
oblique intercept headings).

### 3. TaxiGraph statics (`Navigation/TaxiGraph.cs`)

File: `tests/MSFSBlindAssist.Tests/TaxiGraphStaticsTests.cs` (18 tests).

`EdgeCrossesRunwayStatic` (strict proper-intersection: perpendicular crossing
→ true, parallel non-crossing → false, touching-only-at-a-threshold-endpoint
→ false), `MatchHoldShortRunwayName` (closer-end designator selection,
null-beyond-tolerance, null-with-no-centerlines), `PerpendicularDistanceMetersStatic`
(on-segment zero, perpendicular offset, clamp-to-endpoint beyond the segment),
`FastDistanceMeters` (1° latitude ≈ 111132 m, zero for identical points),
`GetNodesOnTaxiway` (case-insensitive lookup via a synthetic `TaxiGraph.Build`,
empty list for unknown/blank name). Also added `ResolveTaxiwayName`
alias/collision/ambiguity guard tests (bare-alias resolution, the
never-remap-a-real-taxiway-name collision guard, the leave-unresolved
ambiguous-bare-alias guard, and the exact-disambiguated-label resolution path
"B (K)" / "B (M)") — this lives on `TaxiGraph`, not a separate file.

**Promotions:** none — all five target statics plus `ResolveTaxiwayName` are
already `public`.

### 4. RunwayCenterlineTracker (`Navigation/RunwayCenterlineTracker.cs`)

File: `tests/MSFSBlindAssist.Tests/RunwayCenterlineTrackerTests.cs` (9 tests).

Pinned the documented sign-convention "bug magnet": `CrossTrackFeet` is
INVERTED relative to the underlying `NavigationCalculator.CalculateCrossTrackError`
(positive = LEFT, negative = RIGHT here). Covered left/right sign, `AbsCrossTrackFeet`
always non-negative and matching `|CrossTrackFeet|`, heading-error zero-and-wrap
normalization, threshold distance, and `LeftRightLabel`'s tolerance boundary
(±25 ft).

**Promotions:** none — `Compute` and `LeftRightLabel` are already `public static`.

### 5. TaxiLeadIn (`Navigation/TaxiLeadIn.cs`)

File: `tests/MSFSBlindAssist.Tests/TaxiLeadInTests.cs` (9 tests).

`Extract` (no lead-in when already on the cleared taxiway, leading-run
collection, consecutive-name dedup with repeat-after-break, whole-route lead-in
when the cleared taxiway never appears), `IsAcceptable` (fallback-reason
rejection, the ratio×gap+pad budget boundary), `Clause` (empty, single-taxiway
"onto", multi-taxiway "via ... and ..." Oxford-and phrasing).

**Promotions:** none — all three are already `public static`; `LeadInInfo` is
a `public readonly struct` with `init` properties, directly constructible in
tests.

**Note:** `ResolveTaxiwayName` (also named as a TaxiLeadIn-adjacent target in
the brief) actually lives on `TaxiGraph`, not `TaxiLeadIn` — covered in
TaxiGraphStaticsTests.cs (see #3 above).

### 6. TaxiRouter (`Navigation/TaxiRouter.cs`)

File: `tests/MSFSBlindAssist.Tests/TaxiRouterTests.cs` (21 tests, several
`[Theory]`-driven).

Built a small synthetic "L"-shaped `TaxiGraph` (two taxiways sharing a
junction node, plus a fully disconnected island) via `TaxiGraph.Build`.
Covered: `FindBestIntersection` returns the correct shared-name node when one
exists, returns **-1** (no Euclidean fallback) when two taxiway names share no
node; `ComputeGraphDistancesFrom` (zero at source, correct Dijkstra distance to
a neighbor — matched against `TaxiGraph.CalculateDistanceMeters`, the same
haversine formula `Build()` uses for edge distances, NOT the equirectangular
`FastDistanceMeters` approximation), unreachable-component exclusion, empty
dict for an unknown source; `FindShortestPath` returns null across disconnected
components (the underlying mechanism behind the "component-filtered start
candidates" invariant — the actual *filtering* logic lives in
`Services/TaxiGuidanceManager`, out of scope); `FindConstrainedPath` follows a
two-taxiway clearance end-to-end with no fallback reason; `GetTurnDirection`
angle-bucket theory across all four bands (straight/slight/normal, both signs,
including the 20°/60° boundaries).

**Promotions (only ones made — all bare keyword swaps, zero logic changes):**
- `private int FindBestIntersection(...)` → `internal int FindBestIntersection(...)`
- `private Dictionary<int, double> ComputeGraphDistancesFrom(int sourceId)` → `internal ...`
- `private static string GetTurnDirection(double angle)` → `internal static ...`

**Skipped (fixture-heavy, deep routing scenarios — noted in the test file's
header comment):** runway-bridge fallback (`FindRunwayBridge`), off-route
recalculation fallback-to-shortest-path, and lead-in snapping onto a far first
taxiway. These need much larger, more realistic graphs to exercise
meaningfully and are already covered end-to-end against real navdata by
`tools/ProgressiveTaxiProbe`.

## Skipped targets (out of Navigation/* scope)

- `ApplyUserRunwayHoldShorts` and its reciprocal-aware KSFO "D" crossing test
  — lives in `Services/TaxiGuidanceManager.cs`, `Services/TaxiGuidanceManager.Routing.cs`,
  and `Forms/TaxiAssistForm.cs` (Services/Forms cluster).

## No bugs found

No genuine production bugs were uncovered during characterization — the one
test failure hit along the way (the tier-5 boundary case) was traced to a
floating-point precision mismatch in the *test fixture's* coordinate helper,
not the resolver itself; fixed by adjusting the fixture, not the source.

## Commit

`test(navigation): characterization tests for hold-short/router/geometry pure logic`
