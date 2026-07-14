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

The "{core}" is the existing `ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement` decoded core (identity, FIR, hazard, extent — raw-key fallback preserved). The distance in the Approach form comes through `BuildLocationPhrase`'s spoken path.

## 3. Geometry — true edge distance

New `AdvisoryGeometry.NearestEdge(vertices, lat, lon)`: minimum point-to-segment distance over the polygon's edges, locally projected (lon scaled by cos lat; adequate ≤ a few hundred nm), **0 when inside**. Rationale: convective outlook polygons have edges long enough that nearest-VERTEX distance is off by tens of nm; the trigger IS the distance, so it must be edge-true. **Amended during implementation (Task 3):** edge distance is used for the box line, the trigger, AND announcements — one number per advisory everywhere within route advisories, so the box and speech can never disagree. `NearestVertex` remains only in the separate Nearby Advisories feature (a different box with its own cross-box-consistency need against `WeatherService.ClosestPoint`); it is no longer used anywhere in the route-advisory location path. Behind-ness uses the bearing to the nearest edge point.

## 4. Facts, not phrases — `RouteAdvisoryLocator` refactor

`ComputeFactsAsync(client, advisories, pos) → Dictionary<string, LocationFact>` becomes the core; the existing `ComputeLocationsAsync` (phrases for the box) becomes a thin wrapper over the facts (via `BuildLocationPhrase`), so the Shift+R box pipeline is unchanged in shape. Geometry per key is cached (WI parse / tier-2 lookup once per key); per-tick work is pure math plus the existing lazy once-per-pass tier-2 feed refresh and ONE positional probe (local HTTP, 5 s cap).

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

## 8. Out of scope (deliberate)

- Expiry-while-inside announcements (churn risk — see §2 table).
- Configurable approach distance.
- Changing the Nearby Advisories (aviationweather.gov) alert semantics — untouched.
- Multi-ring/MultiPolygon beyond the first ring (existing recorded follow-up).
