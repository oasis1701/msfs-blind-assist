# Route-Advisory Proximity Announcements — Design (2026-07-14)

**Problem.** The en-route advisory announcements (spec 2026-07-12 §4/§5) are too spammy in practice. Two compounding mechanisms: (1) the 15-minute `ClearAnnouncedKeys` reminder re-announces EVERY still-active advisory — including areas far behind the aircraft; (2) US convective SIGMETs are re-issued hourly under new numbers (live-observed: 68E → 78E), and the seen-set tracker announces every never-seen key regardless of position. Net effect reported by Robin: whenever the advisory list changes, everything re-announces, including advisories already behind.

**Goal (Robin, 2026-07-14).** Replace key-novelty + reminder semantics with proximity events, mirroring the nearby-SIGMET alerts' spirit: announce an en-route advisory when the aircraft comes within **100 nm** of its area, when **entering** the area, and when **leaving** it. Nothing else repeats.

**Decisions confirmed with Robin:**
- No-geometry advisories (no WI polygon, no tier-2 match): **announce once at first sight** (never-drop safety rule), no reminders. — chosen over box-only silence.
- A new advisory appearing far ahead (>100 nm): **silent until the 100 nm ring** — pure proximity; this is also what silences the hourly re-issue churn. The Shift+R box remains the planning surface.
- The 100 nm threshold is a **fixed constant** (no setting).
- Weather Radar box location line wording: **"Inside"** when inside the area; **"…behind"** (not "behind you") when behind. Spoken announcement phrasing keeps the natural forms.

## 1. What is removed

- `Services/RouteAdvisoryTracker.cs` (seen-set + baseline latch) — deleted, replaced by the zone tracker below.
- The 15-minute reminder in `MainForm.Announcers.CheckRouteAdvisoriesAsync` (`_routeAdvisoryTracker.ClearAnnouncedKeys()` + `_routeAdvisoryKeysClearedAt`) — deleted. Route advisories no longer share the `_announcedSigmetKeys` reminder cadence (that cadence is correct for the nearby alerts because they are proximity-filtered *before* keying; route advisories are not).
- `RouteAdvisoriesTests` covering the old tracker — replaced by the new tracker's suite.

## 2. Zone state machine — `Services/RouteAdvisoryProximityTracker.cs` (new, pure)

Per advisory key (OrdinalIgnoreCase), a zone: **Far** (>100 nm), **Near** (≤100 nm), **Inside**. Input per tick per key: `LocationFact { bool HasGeometry, bool Inside, double? DistanceNm, bool Behind }`. Output: a list of announcement events (enum + key), consumed by the announcer.

Constants: `APPROACH_NM = 100`, `REARM_NM = 110` (hysteresis band), `LEAVE_CONFIRM_TICKS = 2` (~1 min at the 30 s cadence; a boundary graze cannot flap).

Transitions and events:

| From | To | Condition | Event / spoken form |
|---|---|---|---|
| first sight | Far | distance > 100 | silent (kills hourly re-issue churn) |
| first sight | Near | distance ≤ 100 AND NOT behind | **Approach**: "Route advisory, {n} nautical miles ahead: {core}." |
| first sight | Near | distance ≤ 100 AND behind | silent, approach latched off (treated as announced) |
| first sight | Inside | inside | **AtPosition**: "Route advisory at your position: {core}." |
| Far | Near | distance ≤ 100 (edge) | Approach (same behind-suppression as first sight) |
| Near | Far | distance > 110 | silent; re-arms Approach |
| Far/Near | Inside | inside (edge) | **Enter**: "Entering advisory area: {core}." — fires regardless of bearing |
| Inside | Near/Far | NOT inside for 2 consecutive ticks | **Leave**: "Left advisory area: {identity-or-key}." — brief, no full decode |
| any | (pruned) | key absent from feed | **silent**, even while Inside — announcing expiry would resurrect the hourly-churn double-announce (old number expires + new number appears). Deliberate leave; the box shows currency. |

Latches: once a key has been Inside, Approach is permanently latched off for it (enter/leave remain — re-entry is a real event). `Reset()` on connect / aircraft switch, as today.

No-geometry keys (`HasGeometry == false`, probe not matched): **AnnounceOnce** event at first sight — the existing wording ("Route advisory: {core}.") unchanged — and the key is tracked in a distance-less zone (no Far/Near transitions possible). The one transition it can still make: if the positional probe later matches it (`Inside` becomes true), **Enter** fires; it can never make Leave (probe-match loss ≠ outside, see §4).

**Post-design note (2026-07-14, turnaround liftoff reset, Robin's turnaround question).**
`Reset()` above was originally called only at connect / aircraft switch. Robin then asked what
happens on a same-session turnaround (flight 1 lands, taxis in, flight 2 departs without an
aircraft switch or reconnect): if an advisory key survives unchanged in the ActiveSky feed across
the turnaround (same SIGMET number, still on the loaded route), its `EverInside` latch from flight
1 would silently suppress flight 2's 100 nm Approach call for that same key. Fix: a third reset
trigger, `Services/TurnaroundLiftoffDetector.cs` (pure, clock supplied by the caller) — a
touchdown edge followed by at least 5 minutes on the ground, then a liftoff edge, calls
`_routeAdvisoryProximity.Reset()` from `MainForm.Announcers.cs`'s `SIM_ON_GROUND` handler.
Deliberately **armed on the touchdown edge, not on ground dwell alone**: that means a genuine
landing is required before the dwell timer even starts, so (a) the session's FIRST departure never
fires it (no touchdown has ever been observed — the tracker is already fresh from `Reset()` at
connect, so there is nothing to protect against and, more importantly, a long taxi-out before that
first departure must not double-announce an advisory already approached during taxi), and (b)
touch-and-goes and oleo-bounce flickers (dwell &lt; 5 min) never fire it either. The detector is
itself `Reset()` at both the connect and aircraft-switch sites so a stale touchdown timestamp from
before either event can't produce a bonus reset later. Tests:
`tests/MSFSBlindAssist.Tests/TurnaroundLiftoffDetectorTests.cs`.

The "{core}" is the existing `ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement` decoded core (identity, FIR, hazard, extent — raw-key fallback preserved). The distance in the Approach form comes through `BuildLocationPhrase`'s spoken path.

## 3. Geometry — true edge distance

New `AdvisoryGeometry.NearestEdge(vertices, lat, lon)`: minimum point-to-segment distance over the polygon's edges, locally projected (lon scaled by cos lat; adequate ≤ a few hundred nm), **0 when inside**. Rationale: convective outlook polygons have edges long enough that nearest-VERTEX distance is off by tens of nm; the trigger IS the distance, so it must be edge-true. **Amended during implementation (Task 3):** edge distance is used for the box line, the trigger, AND announcements — one number per advisory everywhere within route advisories, so the box and speech can never disagree. **Correction (2026-07-14, post-review):** the sentence originally here claimed `NearestVertex` "remains in the separate Nearby Advisories feature" — that was never true. `NearestVertex` was deleted as dead code (it had zero production callers, including before this refactor); the Nearby Advisories box has always used its own independent vertex scan in `WeatherService.ClosestPointGeometry`, not `AdvisoryGeometry.NearestVertex`. Behind-ness uses the bearing to the nearest edge point.

## 4. Facts, not phrases — `RouteAdvisoryLocator` refactor

`ComputeFactsAsync(client, advisories, pos) → Dictionary<string, LocationFact>` becomes the core; the existing `ComputeLocationsAsync` (phrases for the box) becomes a thin wrapper over the facts (via `BuildLocationPhrase`), so the Shift+R box pipeline is unchanged in shape. Per-tick work is pure math plus the existing lazy once-per-pass tier-2 feed refresh and ONE positional probe (local HTTP, 5 s cap).

**Implementation note (2026-07-14, post-review, M2):** this section originally claimed "Geometry per key is cached (WI parse / tier-2 lookup once per key)" — that was never built. `AdvisoryGeometry.ParseWiPolygon` re-parses each advisory's WI body from scratch every tick, and the tier-2 lookup re-scans the already-fetched feed per advisory that needs it, every tick. Only the tier-2 SIGMET *feed fetch itself* is cached, and only for the duration of one pass (`WeatherService.RefreshAndGetSigmetFeedsAsync`'s lazy once-per-pass refresh, populated on first use and reused for the rest of that tick) — there is no cross-tick, per-key geometry cache. The cost is trivial in practice (a flight plan carries at most a handful of route advisories, re-parsed every 30 s), so this was correctly left as pure per-tick recomputation rather than adding a key-keyed cache; the sentence above is corrected to describe the actual behavior.

Probe semantics (unchanged bundling rule — only the FIRST advisory of a positional hit is matched): a probe match can only **strengthen** an inside verdict (`Inside |= probeMatched`), never weaken one — probe-match loss does not mean "outside" (another overlapping advisory may simply have become the response's first block). Consequently a no-geometry advisory can gain Enter via the probe but can never produce Leave.

All existing degradation invariants hold: additive-only, never drops or blocks an announcement path, every failure yields "no fact" → the key is treated as no-geometry (announce-once), never an exception.

## 5. Announcer rewiring — `MainForm.Announcers.CheckRouteAdvisoriesAsync`

Per tick (cadence and gates unchanged: `AnnounceRouteAdvisoriesEnabled` + the central AS gate): fetch → parse → `ComputeFactsAsync` for ALL advisories (not just new keys) → `tracker.Observe(facts)` → announce each event (queued `Announce`, UI thread as today). The location suffix inside Approach/AtPosition announcements derives from the same fact (no second geometry pass).

## 6. Weather Radar box wording (Robin, this session)

`BuildLocationPhrase` box forms change: inside → **"Inside"** (was "at your position (inside the area)"); behind → **"{n} nm behind"** / "less than one nautical mile behind" (was "…behind you"). Ahead forms unchanged. Spoken forms unchanged ("at your position", "{n} nautical miles behind you"). Existing pins updated.

## 7. Testing

- `RouteAdvisoryProximityTrackerTests` (new): first-sight zone matrix (Far silent / Near-ahead announces / Near-behind silent / Inside announces), hysteresis flap at 100/110, behind-suppression only on Approach, Enter regardless of bearing, 2-tick Leave confirm, Inside-latch kills later Approach, re-entry announces Enter again, silent prune (incl. while Inside), no-geometry announce-once + probe-Enter + never-Leave, Reset re-baselines.
- `AdvisoryGeometryTests`: `DistanceToPolygonNm` — point off a long edge (vertex distance ≫ edge distance), inside → 0, degenerate polygon, antimeridian non-goal documented as before.
- Locator: facts wrapper equivalence (phrases unchanged for the box — existing tests pin), probe-strengthens-never-weakens.
- Formatting: new/updated `BuildLocationPhrase` pins ("Inside", "…nm behind").
- Sim-facing: the announcer wiring — in-sim plan: one convective-day flight; expect exactly: one "…nautical miles ahead" per advisory crossed inward through 100 nm, one "Entering…", one "Left…" per area, silence from hourly re-issues beyond 100 nm, silence from areas behind.
- **Amendment (2026-07-14, post-review):** the hourly-re-issue silence promise above is scoped to
  advisories still beyond 100 nm. An hourly convective re-issue (new SIGMET number) whose area is
  WITHIN the 100 nm ring, or that the aircraft is already inside, is NOT silenced — the new number
  is a brand-new tracker key indistinguishable from any other new key, so it speaks once per
  re-issue (the old key's silent prune, §2's "any / (pruned)" row, plus the new key's own
  first-sight Approach or AtPosition). Only the >100 nm case is quiet across a re-issue.
- **Empty-feed flap guard (2026-07-14, post-review, M3):** a single successfully-fetched empty
  feed ("No airmet/sigmet…") no longer prunes every tracked zone in one tick.
  `MainForm._emptyRouteFeedTicks` requires two consecutive empty ticks before
  `CheckRouteAdvisoriesAsync` lets the prune reach `Observe`; any tick that returns advisories
  resets the counter, and it is also reset at both `RouteAdvisoryProximityTracker.Reset()` call
  sites. See `docs/weather.md` §12 for the rationale — symmetric with `LeaveConfirmTicks`, a
  1-tick feed flap (e.g. ActiveSky reloading its flight plan) must not wipe zone state and
  re-announce everything nearby as first-sight.

## 8. Out of scope (deliberate)

- Expiry-while-inside announcements (churn risk — see §2 table).
- Configurable approach distance.
- Changing the Nearby Advisories (aviationweather.gov) alert semantics — untouched.
- Multi-ring/MultiPolygon beyond the first ring (existing recorded follow-up).
