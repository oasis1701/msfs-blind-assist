# Taxi Guidance

Turn-by-turn taxi assistance for blind pilots. Combines a continuous stereo-panned steering tone ("taxiway localizer") with spoken announcements for turns, taxiway crossings, hold-shorts, and arrivals. Works on any airport the user's navdatareader database covers, from major hubs down to small GA fields.

## Overview

**The problem:** Blind MSFS pilots cannot taxi between gates and runways. Before this feature, the app relied on gate-to-runway teleport because there was no accessible way to follow painted centerlines, read taxiway signs, or react to ATC clearances on the fly.

**The solution:** Two synchronized feedback channels driven by the navdatareader taxi graph and live SimConnect position:

1. **Spoken announcements** (tactical) — upcoming turns, taxiway changes, hold-short callouts with distance countdowns, crossing callouts, arrival.
2. **Continuous steering tone** (fine steering) — stereo pan tells the pilot which way to steer to stay aligned with the active segment's bearing. Silent when on-track.

**Key design principle:** Every airport the user's database exposes is in scope — no airport-specific assumptions. Taxiway names like `A`, `K2`, `LINK 53`, `HAWKER` all flow through unchanged from the DB.

## Key Files

- `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` — real-time state machine, announcements, position tracking
- `MSFSBlindAssist/Services/TaxiSteeringTone.cs` — stereo-panned steering tone with hysteresis
- `MSFSBlindAssist/Navigation/TaxiGraph.cs` — graph construction (nodes, edges, taxiway name index)
- `MSFSBlindAssist/Navigation/TaxiRouter.cs` — ATC-constrained A* pathfinding
- `MSFSBlindAssist/Database/Models/TaxiPath.cs`, `TaxiNode.cs`, `TaxiRoute.cs`, `StartPosition.cs`
- `MSFSBlindAssist/Database/LittleNavMapProvider.cs` — `GetTaxiPaths()`, `GetRunwayStarts()` queries + taxiway name normalization
- `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — route entry UI
- `MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs` — user settings dialog
- `MSFSBlindAssist/Forms/LandingExitForm.cs` — pre-touchdown runway-exit picker
- `MSFSBlindAssist/Services/LandingExitPlanner.cs` — touchdown detection + auto-activation
- `MSFSBlindAssist/Navigation/LandingExit.cs` — exit-node model returned by `TaxiGraph.GetLandingExits()`

## Architecture

### Data flow

```
navdatareader SQLite → LittleNavMapProvider.GetTaxiPaths()
                    → TaxiGraph (nodes, edges, name index)
                    → TaxiRouter (ATC-constrained A*)
                    → TaxiRoute (segments with turn angles, hold-shorts, lineup targets)
                    → TaxiGuidanceManager (state machine + position tracking)
                    → TaxiSteeringTone (stereo pan) + ScreenReaderAnnouncer (speech)
```

### Graph construction

- Endpoints within ~1 meter (~0.00001° lat/lon) merge into the same node via spatial hashing.
- Node types: `Normal`, `HoldShort`, `ILSHoldShort`, `Parking`.
- Edges are bidirectional, with `DistanceMeters` (haversine) and `BearingDegrees` (initial bearing along the edge).
- Taxiway name comparisons are `StringComparer.OrdinalIgnoreCase`; names are trimmed and internal whitespace collapsed at DB load.
- The name index (`_taxiwayNodeIndex`) maps each taxiway name to the set of node IDs it touches — used by the router to detect intersections where two named taxiways meet.

**Hold-short name quality:** When a hold-short node has multiple incoming taxiway names, the graph picks the best "connector-style" name via a tiered ranking:

1. **Tier 1** — names containing both a letter and a digit (`A5`, `NB1`, `K2`). These almost always identify the specific connector. Prefer the shortest; break ties alphabetically.
2. **Tier 2** — any other non-empty name. Prefer the longest; break ties alphabetically.

This gives the pilot the connector name (`Hold short taxiway A5`) instead of a generic parallel name (`Hold short taxiway A`) — the connector name is actionable for identifying which hold-line you're at.

### Runway hold-short selection (ILS preference + synthetic back-off)

`TruncateToHoldShort` picks the correct stopping point for a runway-destination route. Two universal-DB refinements keep it reliable across every airport the user's navdatareader build might expose:

1. **Prefer `ILSHoldShort` (IHS/IHSND) over `HoldShort` (HS/HSND) — but only on the *same final approach*.** If the route passes both on the way to the same runway, the ILS hold-short wins: ILS hold lines sit farther back from the runway (ICAO Annex 14: ~90–107.5 m) to protect the localizer critical area, so stopping at the IHS is safe (it's also the correct hold line when low-vis / LVP is in force). Plain HS distances scale with runway code (30 m Code A → 90 m Code E/F). When only one type exists in the DB, whichever is present is used. **The IHS preference is gated on geometric proximity (`SAME_APPROACH_IHS_MAX_M = 150 m`): an IHS that is farther from the runway than the latest HS is honoured only when the two holds are within 150 m of each other** (i.e. the ILS hold genuinely sits just behind the CAT I hold on the same connector). Otherwise the latest (closest-to-runway) hold-short of either type is used. **Why:** the previous unconditional IHS preference broke at airports where the cleared route merely *crosses* an ILS-critical-area hold on a transit taxiway before turning onto the final connector — that transit IHS hijacked the truncation and the manager announced the hold a whole taxiway early (OMDB 30R via N12, fs2024: the route runs down taxiway N, which carries its own IHS nodes ~620 m from N12's hold, then turns onto N12; the N IHS was picked over N12's actual 30R hold).
2. **Synthetic 60 m back-off when no HS/IHS node exists.** Some airports in some DB builds have runways with no hold-short graph nodes at all (older navdata, partial Navigraph merges, small GA fields). Rather than routing the aircraft onto the runway, we walk backwards along the route from the point where it MEETS THE RUNWAY and stop at the first node that is at least 60 m from it — an ICAO-Annex-14-inspired minimum distance. The final segment is marked `IsHoldShortPoint = true` so the guidance manager announces "Hold short" normally.

   **The reference is the route's runway-ENTRY node (`_destinationNodeId`), never the lineup target**, and the index choice is delegated to the pure `RunwayHoldShortSelector.SelectSyntheticBackoff` so the matrix is pinned. For an ordinary full-length departure the two coincide, so this is a no-op there — but they diverge by hundreds of metres on three shapes, and on every one of them the lineup target is the wrong end to measure from: a **full-length backtrack** (the lineup point is the FAR threshold, past the route's actual end), a **named-holding-point departure** (the route is pinned through a stub whose entry sits back from the lineup spot), and **any runway `TaxiGraph.FindRunwayLineupEntryNode` retargets** — measured over the whole DB, 81 of 81 retargeted runway ends put that entrance ≥ 60 m from the lineup point. Measuring from the lineup target on any of them makes the FIRST candidate (the route's last node, which is ON the pavement) clear the back-off, so nothing is truncated and that on-runway node is tagged "Hold short of Runway X" — a blind pilot told to stop ON the runway, which is exactly what the method's own guard four lines below forbids. Real examples in the shipped fs2024 DB: UBSA 04 (42 of 43 stands route with no hold node), EGDL 21 (69 of 69), SVBM 27 (23 of 26).

   **`-1` (nothing far enough back) is a meaningful answer and must stay one.** It happens when the whole route sits inside the 60 m bubble — a stand right beside the runway. `TruncateToHoldShort` then tags NOTHING and `HandleArrival`'s runway-arrival fallback (`_hasLineupTarget && _isRunwayLineup`) owns the stop. Degrading `-1` to "the last segment" is what puts a pilot on the pavement.

The destination name passed to `TruncateToHoldShort` (e.g. `09R`) is stamped onto the final segment's `HoldShortRunway` so the announcement is specific ("Hold short runway 09 Right") even when the hold-short node itself has no runway label.

### ATC-constrained routing

The router follows the pilot's clearance in order, not the globally shortest path:

```
For each taxiway name N_i in sequence:
    intersection = FindNodesOn(N_i) ∩ FindNodesOn(N_{i+1})
    A* from currentNode to any node in intersection
    currentNode = intersectionNode
After last taxiway: A* to destination node
```

This ensures `via Alpha, Bravo, Kilo` really goes A→B→K and not a shortcut via some other taxiway. If no path exists on the given sequence (typo, disconnected taxiways), the router falls back to unconstrained A* and the manager announces that the sequence couldn't be honored.

### State machine

```
Inactive → RouteLoaded → Taxiing → HoldShort → Taxiing → … → LiningUp → Arrived
                             ↓                      ↑
                         (off route) → recalculated─┘
```

- `Inactive` — no route loaded.
- `RouteLoaded` — route calculated, waiting for movement.
- `Taxiing` — position feed drives segment advancement, tone, announcements.
- `HoldShort` — tone paused, movement must stop. User presses continue (Input > `Y`) to resume.
- `LiningUp` — final approach to destination runway or gate; tone drives lineup heading.
- `Arrived` — destination reached; guidance stops.
- `BacktrackingOnRunway` — landing-side backtaxi to the apron after reaching the runway end without exiting (steer on the reciprocal runway heading).
- `BacktrackDeparture` — full-length backtrack departure (see below).

### Full-length backtrack departure (opt-in)

Some runways have **no full-length parallel taxiway** — you enter the runway at an intermediate point, backtrack toward the departure threshold, turn around on a pad/loop, and take off the other way (e.g. iniBuilds EGNM 32 via D1, EGGW). This is **opt-in** via the Taxi planner **"Full-length departure (requires backtrack)"** checkbox (runway destinations only, default OFF). **Every non-checked path is unchanged** — the feature is entirely inert unless the box is ticked.

Flow when checked:
1. **Entrance (geometric, name-independent).** `TaxiAssistForm.OnCalculateClicked` calls `TaxiGraph.FindBacktrackEntryNode(thr, far, halfWidth, aircraft)` to pick the taxiway→runway entrance: the reachable (same connected component) on-runway node **closest to the departure threshold** that has a graph neighbour **off** the runway (a real junction). Measuring "closest" from the departure threshold makes it **direction-aware** — the same airport picks D1 for 32 and A1 for 14. This is deliberately NOT `GetRunwayIntersections`, which keys on taxiway NAMES: backtrack sceneries routinely leave every `taxi_path` name empty (EGNM), so a name-based scan finds nothing. The route's destination node becomes this entrance; the **lineup target stays the full-length threshold**.
2. **Route + hold-short.** The A* route ends at the entrance, held short (`LoadRoute(..., fullLengthBacktrack: true)` → `_backtrackDeparture`). `TruncateToHoldShort`'s synthetic back-off references the **entrance**, not the far full-length threshold (which is hundreds of metres past the route's end).
3. **Backtrack (`BacktrackDeparture`).** On Continue, `UpdateBacktrackDeparture` steers along the runway centreline on the **reciprocal** of the takeoff heading toward the departure threshold, using the same cross-track + intercept model as `UpdateLineup`. **This is geometric** (from `RunwayCenterlines`), never A* — the runway pavement is not continuously in the taxi graph (gaps), so it cannot be routed. Never silent: the tone pans to guide the entry turn and hold the centreline.
4. **Turnaround + handoff.** On reaching the threshold (`BACKTRACK_DEP_HANDOFF_M`), it hands to `LiningUp` at the full-length point. `LiningUp` already pans an arbitrary heading error, so the 180° turnaround, final alignment, "Lined up", and `RequestTakeoffAssistAutoActivate` all reuse the normal full-length-departure machinery. Pad-vs-loop geometry needs no special handling — the pilot uses whichever pad exists; guidance only steers heading + centreline.

**Non-silent turnarounds (both backtracks).** Neither `BacktrackDeparture` nor the landing `BacktrackingOnRunway` goes silent during the 180°. While heading error > 90° the tone tracks the raw error (no low-pass) so it pans hard to whichever side the pilot commits and follows them around; below 90° it low-passes for smooth fine steering as they straighten. The old "silent while > 90° from the backtrack target — direction is ambiguous" gate was removed (user ruling 2026-07: silence reads as "system gave up").

**End of the landing backtrack — the tone must be STOPPED, not just left alone (2026-08-23).** `UpdateBacktracking` reaches the connection node, announces and sets state to `Taxiing` with `_route` still null. `UpdatePosition`'s `_state != Taxiing || _route == null` guard then early-returns before anything touches the tone — so a tone still sounding at that moment never receives another heading-error update and holds its last pan for as long as the pilot keeps taxiing. Reported from CYYZ as "stuck in the right ear"; 68 s of it in the log, from the handoff to the end of the session. The sibling branch a few lines above (no taxiway connection found) has always called `_steeringTone.Stop()` for exactly this reason; the success branch simply never did. **Every path that lands in `Taxiing` with a null route must stop the tone first.** The callout also names the state the pilot is now in — *"Runway vacated. No route set — use the taxi planner for a route to your stand."* — because a status query there answers *"No route loaded."*, which is true but reads as a fault unless they were told.

Pinned by `BacktrackEntryTests` (entrance geometry). Everything past the entrance is sim-facing — verify in the sim (turn side and `BACKTRACK_DEP_HANDOFF_M`/`_APPROACH_M` distances are the most likely tuning points).

### Named holding-point departure (opt-in, 2026-07-26)

ATC clearances name PAINTED holding points ("Runway 28, hold short A2" at LSZH) that often do
not exist as named taxiways anywhere — the designator lives only on the painted line. In OSM
they are `node[aeroway=holding_position]` refs; in navdata they are nothing (and at MK Studios
LSZH the entry stubs aren't even named, so the name-keyed intersection-departure list can't
offer them). Opt-in via the Taxi planner **"Depart from named holding point"** checkbox
(runway destinations only, mirrors the intersection block: `chkHoldingPoint`/`cmbHoldingPoint`,
untick + hide on leaving runway mode, repopulate on runway change, fallback announcement +
auto-untick when the runway has none).

- **Data:** `OsmTaxiSource` fetches `holding_position` nodes (ref, name-tag fallback) into
  `AirportTaxiData.HoldingPoints`; `AugmentingAirportDataProvider.GetHoldingPoints(icao)` serves
  them from the per-ICAO cache (no fetch of its own; empty when augmentation is off/uncached —
  the picker then falls back with a spoken note). OSM-dependent by design: no OSM reachability
  that session → no named holding points.
- **Resolution (`TaxiGraph.ResolveHoldingPointEntries`)** is geometric + name-independent,
  mirroring `FindBacktrackEntryNode`: a point binds to the selected runway within
  `maxPointPerpMeters` (200 m) of its centerline; its entry is the nearest same-component
  on-runway node with an off-runway neighbour within 200 m of the point. **The painted point
  only SELECTS the entry node** — hold-short PLACEMENT on the resulting route stays 100 %
  navdata-authoritative (`TruncateToHoldShort` and the whole tuned pipeline are untouched); this
  deliberately does NOT implement the deferred "holding_position sharpening" item.
- **Corridor pin (2026-08-08 — the EGLL 27R A2/A3 fix).** Selecting the entry node is NOT enough:
  the path TO it is a free A* choice, so the pilot can be routed up a NEIGHBOURING stub that
  merges with the chosen one short of the runway. Reported at EGLL 27R — the pilot picked **A2**
  and was told to hold at **A3**. Reproduced against the owner's navdata: A2 and A3 merge ~130 m
  before the pavement, so **both painted points resolve to the SAME entry node (1099)**; from the
  south-west the free route runs up A3, rejoins A2 for its last ~60 m, and reaches the entry
  exactly as asked — but the only hold line it crosses is A3's (A2's, node 1091, is never on the
  route). `TruncateToHoldShort` takes the LAST hold node ON THE ROUTE, so guidance stopped at and
  correctly named A3. The name was right; the corridor was the bug.
  So `ResolveHoldingPointEntries` now also returns `RunwayIntersection.HoldNodeId` — the painted
  LINE's own graph node, snapped PER POINT via `NamedHoldingPointResolver.SnapToNode` (extracted
  from `Resolve` so the two can never drift; per-point, because `Resolve` collapses duplicate
  names and would answer for the wrong line at an airport with two A4s). `LoadRoute` takes it as
  `holdingPointHoldNodeId` and `ApplyHoldingPointPin` rebuilds the route as
  *start → hold line* (clearance-constrained) + *hold line → entry* (unconstrained; the
  clearance is all behind you by then), joined by `TaxiRouter.Concatenate` — which REBUILDS from
  the combined node list rather than appending segment lists, so cumulative distances and the
  turn at the seam are computed rather than inherited. Measured on the real graph: free route
  422 m holding at A3 node 1123, pinned 620 m holding at A2 node 1091.
  **The pin degrades to a no-op on every doubt** — node missing/unreachable/equal to the
  destination, already on the route, either leg unbuildable, or a detour past
  `HOLD_PIN_MAX_RATIO` (2.0) + `HOLD_PIN_MAX_PAD_M` (500 m, the recalc gate's numbers). It can
  only make the route match the request, never make a working route worse. A pinned route is
  EXPECTED to be longer — that is what choosing a stub means — so do not tighten those bounds
  into a "shortest route wins" rule. The pin is stored in `_holdingPointHoldNodeId` and re-applied
  by `TryRecalculateRoute` (before its sanity gate, so the gate judges the route actually flown),
  for the same reason `_preferIlsHold` persists: a recalc that dropped it would quietly hand the
  pilot back the corridor they didn't choose.
- **Lineup semantics:** entries past the start-table lineup point (+50 m margin) reuse the
  intersection-departure machinery unchanged (destination node = entry, lineup target = entry
  projected onto the centerline → partway lineup, Takeoff Assist auto-activation). Full-length
  entries — deliberately INCLUDED, unlike the intersection list — keep the start-table lineup
  target and just force WHICH stub the route uses (the A2-vs-A1 choice). Precedence at
  Calculate matches the existing pattern: backtrack > intersection > holding point.
- Confirmation is part of the single post-StartGuidance standstill utterance ("Holding point
  A2, runway 28. About … of runway ahead."). (That fix also removed the leftover SECOND
  intersection AnnounceImmediate, which was stomping the joined utterance and silently
  swallowing the reach warning whenever an intersection was chosen.)

Pinned by `HoldingPointEntryTests` (resolver geometry + `HoldNodeId`) + `TaxiRouterTests`
(`Concatenate`) + `OsmHoldingPositionParseTests` (parse). The picker/route flow is sim-facing —
verify in the sim.

#### Anchoring and gates (2026-08-07 — the EGKK 26L pass)

Three defects, all found because EGKK 26L's whole A/M holding area was missing from the picker.

- **`PopulateHoldingPoints` measures against the OUTER ENVELOPE** of the pavement edges
  (`Runway.Start/End`, i.e. `runway_end`) and the start-table departure lineup points —
  `TaxiGraph.ChooseHoldingPointExtent`. Neither source alone works, because navdata disagrees
  with itself in BOTH directions:
  - EGKK 26L departs 406 m BEYOND its `runway_end`, so the edge anchor put A1/A2/A3/M1/M3 at
    −237…−406 m along and the `ptAlong < −30` gate dropped the entire A/M holding area.
  - EGLL 09R and EHAM 36C put the start row 45 m and 480 m INTO the runway, so the lineup
    anchor dropped the full-length holds behind it — measured against the live navdata + OSM,
    that cost EGLL 09R its N1/NB1, EGLL 27L its N8/NB8, and EHAM 36C its CAT III hold.

  The envelope only ever grows the window, so no entry either source would have offered can be
  lost; `LineupAlong` still says where the departure point sits inside it, which is what the
  Partway (intersection-semantics) test compares against. The far lineup point is found BY
  DESIGNATOR (`ReciprocalRunwayName`), never by proximity — EGKK's 08L lineup point is nearer to
  08R's pavement edge than 08R's own is.
- **`MIN_ALONG_M` is −40 m, not +5.** A full-length entry stub meets the runway at (often just
  behind) the lineup point. A positive floor filtered that junction out and the full-length points
  then snapped to the NEXT entrance downfield — at EGKK, M1/M3 resolved to the A entrance 112 m
  in, so picking "M1" silently routed you to a different stub.
- **The point-binding gate is the resolver's own `maxPointPerpMeters` (200 m), NOT
  `HOLDSHORT_RUNWAY_MATCH_M` (150 m).** That constant sizes a different judgement — which runway a
  hold-short node protects — where tightness matters because EGKK's two centerlines run only
  ~200 m apart. Here the runway is already chosen and the entry node still has to sit on this
  centerline, so the gate only has to admit a set-back CAT II/III hold: EGKK A3 is 162 m out.
  A point whose NAME parses as a runway designator (`08L/26R` — how OSM labels a runway-crossing
  hold line; since 2026-08-14 also the dash form `36C-18C`, which is how EHAM maps its runway
  hold lines) is skipped; it is not something ATC clears you to depart from.

#### Behind-threshold holds (2026-08-14 — the EGCC 23L VB1/T1 pass)

Manchester's Runway 2 exposed a THIRD anchoring case: airports that paint their full-length
departure holds on the lead-in taxiways of a holding/queue area **behind the threshold**. EGCC
23L's real, ATC-used holds sit 73 m (VB1) to 430 m (T1) behind the pavement edge — and unlike
EGKK 26L, whose set-back holding area is rescued because its start row sits 406 m back (the
envelope grows), EGCC's 23L start row sits 96 m INTO the runway, so the envelope stays at the
pavement edge and the `ptAlong ≥ −30` gate dropped VB1/VB2/VA1/VA2/T1/S1 wholesale while ATC was
saying "line up 23L via VB1".

A behind-threshold point (−1000 m ≤ ptAlong < −30 m, still within the 200 m perp gate of the
EXTENDED centerline) is now admitted, but ONLY under three gates, all required — the design goal
was explicitly "get EGCC's points without showing false points elsewhere":

1. **Kind vouch (opt-in):** the caller must confirm via `behindThresholdEligible` that the
   point's OSM `holding_position:type` marks a line that GUARDS a runway — `runway` (the normal
   lineup hold: EGCC VB1/VA1/T1) or `ils` (its set-back CAT II/III twin: VB2/VA2). `intermediate`
   queue-ladder holds (EGCC V1–V6) and UNTAGGED points (EGCC S1 — a real hold, accepted loss)
   stay out. Callers without kind data pass null and keep the old behavior byte-for-byte — every
   pinned test and every non-form caller is unchanged.
2. **Ownership guard (`AnotherRunwayClaimsPoint`):** no OTHER runway's centerline
   (`RunwayCenterlines`, self + reciprocal excluded by `runwayName` designator) may be closer to
   the point (5 m margin against float noise on the collinear self-line). EGCC's V-series sit
   almost midway between the parallels (197 m vs ~190 m) — without this they'd migrate between
   lists. The other runway's claim window is its span EXTENDED by the same 1000 m
   behind-threshold band on both ends — the claim window must equal the admission window, or a
   point behind BOTH runways' thresholds falls in neither narrow window and the guard never
   fires (LEMD: Y-1, 36R's hold 82 m from its line, was also admitted to 14L's list at 169 m
   perp through exactly that gap). Needs `runwayName`; null keeps admission off.
3. **Threshold-bound binding:** the entry may bind no farther from the point than the threshold
   anchor itself is (+60 m slack) — `bindCap = max(200, dist(point, thr) + 60)` — so a
   behind-threshold point can structurally only select the full-length entry (or nearer), never
   a junction downfield.

Ties: several behind-threshold holds share the full-length entry and tie exactly on along-track,
so the sort now tie-breaks by the painted line NEAREST its entry (then name, for determinism) —
that line is "the normal clearance" `AnnounceDefaultHoldingPoint` names (EGCC 23L: VB1 at ~90 m
beats T1 at ~430 m).

Measured before/after with the real resolver over the owner's fs2020 DB + live OSM at 12
airports, every runway direction: **zero entries removed anywhere**; additions only at airports
that genuinely paint set-back holds (EGCC 23L +VB1/VB2/VA1/VA2/T1; EDDF's east/west queue areas;
EHAM's polderbaan lines, which the dash-designator fix then filters as unnamed-in-AIP). Pinned by
the behind-threshold block in `HoldingPointEntryTests`.

### Runway centerlines — pairing and bogus start rows (2026-08-07)

`TaxiGraph.RunwayCenterlines` drives "on runway X" in Where-Am-I, runway-led hold-short naming,
and runway-crossing detection. Two independent failures at EGKK left it EMPTY, then wrong.

- **Pairing runs the reciprocal-DESIGNATOR pass FIRST, then heading for the leftovers.**
  Designators are reciprocal by definition and the side letter keeps parallels apart (08L pairs
  only with 26R), whereas `start.heading` is wrong often enough to mis-pair whole airports: EGKK
  stores 08L and 26R both at 257.6° and 08R and 26L both at ~168° (nothing paired — zero
  centerlines), and LEMD stores 0° on runways pointing 322°, which made the heading test read 32R
  as the reciprocal of 18L and 32L as of 18R — two lines drawn DIAGONALLY ACROSS THE AIRFIELD and
  no correct line at all. The bearing between the two start points is the geometry check (±45°,
  wide because designators are magnetic and the measured bearing is true).

  The heading pass is still needed second, and not merely as a safety net: it is what rescues the
  NAME-SWAPPED airports, where the row labelled for one end sits at the other. AYCH's "03" row is
  at the 21 threshold carrying 21's heading, so the designator pass correctly refuses it (the
  a→b bearing is 185° from what "03" claims) while the heading pass still finds the true pair.

  Order measured over the whole fs2020 navdata (41.8 k airports, 47.9 k centerlines): swapping
  changes 10 airports and improves ALL TEN — mis-paired lines fall 662 → 642, LEMD goes from 2
  wrong lines to 4 right ones, EGXT from 4 bogus to 0. Nothing regresses. NOTE a plain
  before/after line COUNT reads those improvements as "losses" (EGXT 7 → 5): score by whether
  `Name2 == ReciprocalRunwayName(Name1)`, not by count.
- **A runway end can have SEVERAL start rows; take the one furthest back**
  (`TaxiGraph.PickFullLengthStart`), never whichever the DB returned first. The full-length
  departure point is by definition the furthest back. 14 airports in the fs2020 navdata carry
  duplicates and 13 runway ends were picking a worse row — EGLL 09R was lining up 342 m down the
  runway with a 67 m row sitting right there (now 59 m from the OSM pavement start), VRMM 18 was
  826 m in versus 28 m, and KBOS 22L/22R/04R/15R were each 200–307 m in.
- **`TaxiGraph.SnapStartToRunwayCenterline` pulls a laterally-bogus start row back onto the
  `runway_end` line, keeping its along-track position.** EGKK has three of four rows 99–122 m to
  the SIDE of their own runway; EGLL, EGCC, EGSS, EGGW, EHAM, LFPG, KJFK, KBOS and LEBL all land
  within 5 m, so this is a no-op on sound data. Projecting rather than rejecting keeps the reason
  the start table is used at all — EGKK 26L projects 406 m behind the landing threshold, exactly
  the full-length point on the starter extension, so **negative along-track is expected**.
  It repairs the LATERAL error only, and only inside a plausible along-track window (from half a
  runway behind the landing threshold to midfield); a row more than 250 m off the line, or outside
  that window, is returned UNCHANGED. Both refusals matter: some airports carry NAME-SWAPPED start
  rows — AYCH's "03" sits at the 21 threshold with 21's heading, URWW's "05" sits 2854 m away at
  the 23 end — and projecting those onto their named runway put both of an airport's rows at
  midfield on top of each other, failing the 200 m separation test and DESTROYING the centerline
  AYCH already had. Refusing leaves that pre-existing data problem exactly as it was.
  `Build`/`BuildAsync` take an optional
  runway list to do this; every production call site passes one, and `PopulateDestinations` snaps
  the same way for the route destination and the **lineup target** — left alone, EGKK 26L aimed
  that target ~109 m north of the pavement.

Pinned by `RunwayCenterlinePairingTests`. Verified against the live EGKK navdata + OSM geometry:
post-fix the centerline endpoints land within 11–37 m of the true runway ends, and every B/C
hold-short node names the runway an independent OSM-derived calculation agrees with (±2 m).

**Regression sweep (2026-08-07, whole fs2020 navdata — 41,804 airports / 97,546 start rows).**
Re-run this before touching any of the above:

- centerlines: **EGKK is the only airport that gains any** (0 → 2); **none lose any**
- snapping: 12,149 rows move at all, only **11** move more than 25 m, and those 11 are confined to
  six airports (EGKK ×3, NZSP ×2, VRMM ×2, LEMD ×2, FQMA, LFUD). The other ~12,140 are sub-25 m
  nudges onto a centerline the row was already essentially on
- the rows this deliberately refuses: 4 beyond the 250 m ceiling (MTDA, C29) and 16 outside the
  along-track window (the name-swapped airports) — all left byte-identical to today
- pre-existing and NOT touched by this pass: the heading pass cross-pairs parallel runways at a
  few GA fields (17OK 20L↔02L, OH45 10R↔28R, KARE 33L↔15L), because their designators read as
  reciprocal within 15° and the separation check passes. Running the designator pass FIRST would
  fix those, but it would change pairing at airports that work today, so it was left alone.

**Picker + lineup verification (EGKK, EGLL, EHAM, LEMD against OSM pavement geometry).**
28 runway ends:

- every lineup target lands **0.3–8.6 m** off the true OSM centerline
- holding-point names lost versus the old runway_end anchor: **zero**, at every runway
- EGKK 08R and 26L each gain 8 names (A1/A2/A3, M1/M3, the J and H/G holds); every other runway
  at all four airports returns an identical name list

**A start row past 40 % of the runway is rejected as a departure point** (`PickFullLengthStart`),
so the lineup target falls back to the pavement edge. LatinVFR's LEMD parks 32R/32L/18L/18R at
the runway MIDPOINT (47–48 %) with no taxiway within 220–660 m and, on the 32s, a heading of 0°
on a runway pointing 322° — selecting 32R as a taxi destination routed to a taxiway a third of
the way down and aimed the lineup mid-field. Post-fix those four line up 4–15 m from their
thresholds. The bar comes from the data, not taste: of 97,488 rows, 93.5 % sit in the first 10 %
of their runway and only 115 (0.12 %) lie past 40 %.

**Still outstanding at LEMD:** its centerlines now pair correctly but are HALF LENGTH (14L/32R is
1787 m of 3550 m), because the pairing consumes the raw start rows and 32R's is still the
mid-runway one. Where-Am-I will therefore not report "on runway" in the far half. Fixing it means
relocating a rejected row to its threshold inside `Build`, which is exactly the move that
destroyed AYCH's centerline once — re-measure before trying.

Re-run `Verify` + `score.py`/`diff2.py` in the scratchpad harness after touching the anchor — the
first attempt at this anchored on the lineup point alone, which passed EGKK and silently cost
EGLL and EHAM their full-length holds.

### Position tracking

`UpdatePosition(lat, lon, headingMag, magVariation, groundSpeedKts)` is called from MainForm's SimConnect position loop.

- Aircraft position projected onto current segment; **perpendicular cross-track distance** (equirectangular projection, clamped to segment endpoints) triggers re-routing if >50 m for >3 s with a 15 s cooldown. Using perpendicular distance rather than great-circle distance to either endpoint means the check correctly ignores along-track position — you only re-route when you're actually off the taxiway, not when you're far along a long segment.
- Segment advancement when aircraft enters `WAYPOINT_CAPTURE_RADIUS_M` (25 m) of the segment endpoint, **except** the last segment where arrival is driven by the arrival radius so the final-approach countdown can fire below 25 m.
- Arrival radii:
  - Runway: 30 m.
  - Gate / parking: 20 ft (~6 m) — prevents gate countdown (50/20/10 ft) from being preempted by the 25 m waypoint-capture logic.

### Off-route suppression (turn-advance grace + width cap)

Raw "perpendicular cross-track >50 m for >3 s" alone caused a cascade during the first turn of a route at EGLL: the aircraft cut the corner, the old segment's perpendicular jumped, the manager recalculated, the new route disagreed with the ATC clearance, and the whole plan unravelled. Three guardrails now apply in `UpdatePosition` before off-route is declared:

1. **Post-turn grace period (`POST_TURN_OFFROUTE_GRACE_SEC = 4.0 s`).** Every time a segment is advanced (`AdvanceSegment` and `AdvanceToNearestSegment`) the manager stamps `_lastSegmentAdvanceTime`. Off-route detection is suppressed for 4 s after any advance, giving the aircraft time to settle onto the new segment's geometry before the cross-track check re-arms.
2. **Turn-imminent suppression.** If the aircraft is already within the speed-scaled "turn now" window of the current segment's endpoint, off-route is suppressed — the cross-track to the current segment will legitimately spike as the aircraft carves the corner.
3. **Width outlier cap (`PavementTolerance.WidthCapFeet = 300.0 ft`).** When the current segment's `PathWidth` looks corrupt (e.g. a stray 999 ft value from bad DB data on an apron taxilane), it is capped at 300 ft before being used to derive the off-route tolerance. Prevents a single bad row from effectively disabling the off-route check for that leg.
4. **Route-joined latch (`_hasJoinedRoute`).** Off-route detection is suppressed entirely until the aircraft has reached the route line at least once (`perp <= perpTolerance` on any frame). The post-pushback taxi from the gate ONTO the first taxiway is legitimately off the route's first segment — the route starts on the taxiway, often 100 m+ from the gate — so without this the slow taxi-out (gs ≥ 2 kt but not yet on the route) reads as off-route and recalcs fire, silently trimming the entered clearance before the pilot has joined it. PHNL 2026-06-13: 4–5 recalcs at 3–6 kt while still on segment 0 cut `Z A L N Z D` down to `Z D`. The `gs ≥ 2 kt` gate alone didn't help (pushback exceeds it). Latch resets on `LoadRoute` / `StopGuidance`; once joined, normal off-route detection applies for the rest of the taxi.

### Recalculation hardening (remaining sequence + diversion guard)

When a recalc does fire, two additional safeguards keep the new route honest to the original ATC clearance:

1. **Position-aware sequence trimming (`FindRemainingSequenceByPosition`).** The ATC taxiway sequence is stored in `_originalTaxiwaySequence`. On recalc the manager walks the sequence from **last to first**, asking the graph "is there a node on this taxiway within 50 m of the aircraft?" The first hit is the latest taxiway the aircraft is physically on; everything before it is dropped before passing to `FindConstrainedPath`. Driving the trim from aircraft position (rather than from the recorded `_currentSegmentIndex`) handles the case where a previous recalc reset the segment index to 0 even though the aircraft is far along the route — observed at LEPA where a recalc near the H2 hold-short produced a route that physically started back at LE, sending the aircraft on a big loop. Aircraft drifted entirely off the cleared route → no sequence-taxiway hit → caller falls back to `FindNearestNodeInDirection` + shortest path.
2. **Diversion guard.** If the router fell back to unconstrained shortest path (`ConstrainedFallbackReason` is non-empty) and the new total distance exceeds `2 × oldRemaining + 500 m`, the manager **rejects the recalc** and announces: `"Off route. Could not follow clearance. <reason>. Continuing on original route."` This prevents a bad recalc from sending the aircraft across the field just because a single intersection node has no bridging edge on the expected taxiway.

### Last cleared taxiway honored as the hold-short terminus

When the LAST taxiway in the clearance branches *off* the runway rather than onto it — i.e. a prior taxiway already reaches the runway and the last one is a hold-short connector beside it — `FindConstrainedPath` used to drop it. The node-picker scores by graph distance to the destination, and for such a connector every node's shortest path to the runway goes *back* through its entry junction, so the picker returns the entry node, the step no-ops, and the final unconstrained leg routes onto the runway via the prior taxiway with **no hold short**. Live case: EIDW clearance `F2 F3 F-OUTER N N2` to 28R — taxiway **N** runs to the 28R threshold (4 m) while **N2** is a ~450 m connector, so N2 was silently dropped and the aircraft was guided straight down N onto the runway. Fix: when the last taxiway's target equals the node we entered it on, force traversal along it and end the route there (skipping the bypass leg). The traversal target is the taxiway node nearest the destination position, or — if THAT is still the entry — the node farthest *along* the taxiway from the entry. The second case is **LFPG `…R, R1` → 26R**: the 26R destination node sits closer to the R/R1 junction than to R1's far end, so nearest-to-position also degenerated to the entry, R1 was dropped, the unconstrained final leg deviated off R1, and the aircraft (correctly on R1) read as off-route → a recalc fired. The fix applies to both the multi-taxiway last step and the single-taxiway path (a recalc trimmed to one taxiway is itself the last). It only triggers when the step would otherwise no-op — a last taxiway that genuinely leads to the runway picks its far end, so ordinary routes are unchanged.
3. **The recalc callout names the new sequence.** When a recalc is accepted, it announces `"Route changed. Now via <taxiway list>. <dist> to <dest>."` (distinct named taxiways of the new route, in order) rather than a generic "Recalculating." A recalc can trim/replace the entered clearance (see the route-joined latch above, and the position-aware trim), and the old generic callout never told the pilot their taxiways had changed — at PHNL the clearance was silently cut from `Z A L N Z D` to `Z D` with no audible indication.
4. **No-op recalc suppression.** Before swapping in the recalculated route, the accept block compares the recalc's distinct taxiway sequence to the CURRENT remaining sequence; if they're identical it returns early — no route swap, no "Route changed" callout, no countdown-latch reset, no steering-tone re-slew. A sharp turn ONTO a cleared taxiway (cutting the corner) laterally offsets the aircraft from the route's next segment long enough to trip the off-route detector, which then re-plans the IDENTICAL tail. Reported live at LFPG as a spurious *"Route changed … super sharp right"* while turning onto N (route unchanged: `N B BD1 D1`). The recalc cooldown is stamped before the accept block, so the no-op path cannot re-fire every frame; a genuine reroute has a different sequence and proceeds.

## Concurrency

`UpdatePosition` runs on the **SimConnect thread** (~30 Hz). Route mutation and teardown calls — `LoadRoute`, `StartGuidance`, `StopGuidance`, `ContinuePastHoldShort`, `GetStatusAnnouncement` — arrive from the **UI thread** (hotkeys, form buttons, landing-exit activation).

A single `_stateLock` in `TaxiGuidanceManager` serializes all of these. Without it, a UI-thread `StopGuidance` can null out `_route` while the SimConnect thread is mid-traversal of `_route.Segments[_currentSegmentIndex]`, producing `NullReferenceException` / `IndexOutOfRangeException`. The critical sections do no I/O — audio is already async through NAudio's mixer — so lock contention is negligible.

`TaxiSteeringTone` has its own `_lock`. `UpdateHeadingError`, `Pause`, `Resume`, `Start`, `Stop` all acquire it so that `SetPan` / `UpdateVolume` can't race with `Dispose` freeing the underlying NAudio buffer. `ClearWhereAmICache` takes `_stateLock` like its twin `OnAirportDataUpdated` — both mutate the same `_whereAmICachedGraph` / `_whereAmICachedIcao` pair, and an unlocked write here could race a locked read/build elsewhere and leave the pair inconsistent (graph set but ICAO stale, or vice versa). `TryGetRunwayLineupReference` likewise takes `_stateLock` — it reads `_state`, `_hasLineupTarget`, `_isRunwayLineup`, and the lineup lat/lon/heading fields, the same fields every other locked accessor protects.

`TaxiGraph` carries a lock of its own, one per graph instance: `_structureLock`. `DescribeLocation`
holds it for its whole run — `Alt+L`'s surroundings lookup runs it on a thread-pool thread, through
`DescribeCurrentLocation`, against the ACTIVE guidance graph whenever the airport matches — and the
painted holding-point projection (`InsertHoldingPointNodeOnEdge` → `SplitEdgeAt`, the only change
`TaxiGraph`'s own code makes to a graph after `Build`), which `TaxiAssistForm` runs on the UI thread
on that same instance, holds it for its whole scan-then-split. `DescribeCurrentLocation` releases
`_stateLock` before it asks the graph, and nothing takes another lock while holding
`_structureLock`, so the two never nest. See **Threading** under "Where Am I implementation".

## Steering Tone

The tone is the "taxiway localizer" — a continuous audio signal that encodes the correction needed to stay aligned with the active segment's bearing.

**Physics:**
- **Stereo pan** = direction to steer. Tone in left ear → steer left. Tone in right ear → steer right. Centered → on track.
- **Silent** when heading error is within the on-track dead-band.
- Frequency and amplitude are constant (440 Hz A4, fixed volume) — only pan moves.

**Hysteresis state machine** (kills threshold-boundary flapping from wheel vibration / GPS jitter):
- `SILENT_THRESHOLD_DEG = 3.0` — while sounding, drop below 3° to go silent.
- `ACTIVATION_THRESHOLD_DEG = 6.0` — while silent, must exceed 6° to start sounding.
- `MAX_PAN_THRESHOLD_DEG = 30.0` — at ±30° or more, fully panned left or right.
- `MIN_SUSTAIN_MS = 400` — once started, tone plays at least 400 ms to prevent nervous chirps.

**Heading-error smoothing:** 1-pole low-pass filter (`HEADING_ERROR_FILTER_ALPHA = 0.25`) on the error fed into the tone. Filters out high-frequency noise while staying responsive to real course changes.

**Width-aware hysteresis scaling:** The three tone thresholds (`SILENT_THRESHOLD_DEG`, `ACTIVATION_THRESHOLD_DEG`, `MAX_PAN_THRESHOLD_DEG`) are scaled per call by the width of the path the aircraft is on. A narrow Alpha-1 connector (≈25 ft) tolerates less heading error before the tone fires than a 150-ft-wide runway. The scale factor is `sqrt(width / 60 ft)`, clamped to `[0.65, 1.40]`. Call sites: normal taxiing uses `currentSeg.PathWidth`; gate lineup uses the baseline (no argument). The takeoff *roll* (`TakeoffAssistManager`) handles the wide-runway case itself with its own tolerances.

**Runway lineup uses explicit thresholds, not width scaling.** The width-scaled minimum (≈1.95° silent / 3.9° activation, even at the 25-ft / `MIN_SCALE = 0.65` clamp) was still too loose: a pilot approaching alignment from a 10° error released rudder pressure when the tone went silent at ~2°, drifted back to ~3°, and the tone STAYED silent because 3° < 3.9° activation — leaving the aircraft sitting 3° off heading with no audio cue. For runway lineup we call `TaxiSteeringTone.UpdateHeadingErrorWithThresholds(error, silent=0.5°, activation=1°, maxPan=15°)` directly, bypassing width scaling. The tone now keeps panning until heading is genuinely centered within half a degree, and resumes immediately if the pilot drifts past 1°. Max-pan at 15° (vs 19.5° at scale 0.65) gives stronger feedback sooner.

**Pan curve:** Non-linear `sqrt` with a 0.25 floor. When the tone first activates at 6° of error, it's already audibly offset from center (not a mushy mid-pan), so the pilot hears the direction immediately.

**Heading bookkeeping:** The manager converts SimConnect's magnetic heading to true heading using `magVariation` before comparing to the segment bearing (bearings from the graph are true). East variation is positive, consistent with MSFS conventions.

**"Turn now" vs. "ahead":** As the aircraft approaches a segment endpoint with a turn, the target node flips from "current segment end" to "next segment end" within the approach distance, so the tone smoothly pre-rotates into the turn — just like the real world's anticipation.

### Runway lineup math (UpdateLineup runway branch)

Runway-lineup steering uses an **intercept-angle controller**, not a bearing-to-threshold blend. The blend was unstable once the aircraft crossed the threshold (which happens during every "line up and wait"): bearing-to-threshold then sat on the ±180° wrap, where sub-degree GPS jitter flipped the sign and produced tiny-right / tiny-left / huge-right corrections. Intercept-angle is independent of threshold position — it's defined purely against the centerline LINE, so it works whether the aircraft is approaching, on top of, or past the threshold.

```
intercept = MAX_INTERCEPT_DEG · sqrt((|cross| − DEADBAND) / (SAT − DEADBAND))   for |cross| > DEADBAND
desiredHeading = runwayHeadingTrue + intercept · sign(crossTrack)
toneHeadingError = NormalizeAngle(desiredHeading − aircraftHeadingTrue)
```

- `LINEUP_NOISE_DEADBAND_FEET = 8` — only purpose is to keep GPS sign-flips near the line from chattering. Above 8 ft of cross-track the curve takes over immediately.
- `LINEUP_INTERCEPT_SAT_FEET = 100` — saturates the intercept angle at this offset.
- `MAX_INTERCEPT_DEG = 30` — saturated intercept (matches ILS-localizer convention).
- **sqrt curve** is the key: a linear ramp through a 50 ft deadband produced a "silent gap" at 50–80 ft cross-track because the resulting heading error stayed below the steering-tone activation threshold. Sqrt makes 15 ft cross-track produce ~12° of correction, well above activation, so the tone speaks early.

**Smoother reset on LiningUp entry.** The taxi-phase low-pass filter (`_smoothedHeadingError`) is reset to 0 at every entry into `LiningUp` (both the Continue-past-hold-short path and the gate `HandleArrival` path). Without this, the residual heading error from the connector taxi turn (often 50–80°) leaked into the lineup tone for ~300 ms and steered the pilot off the runway at low speed before the filter caught up.

**Pulse mode for stopped + misaligned.** When the pilot is essentially stopped on the runway (≤ `LINEUP_PULSE_MAX_GS_KTS = 3` kt) AND still misaligned in EITHER dimension (heading error ≥ `LINEUP_PULSE_MIN_HDG_ERR_DEG = 5°` OR cross-track ≥ `LINEUP_PULSE_MIN_CROSS_FEET = 10` ft), `TaxiSteeringTone.SetPulse(true)` switches the steering tone from continuous to a 3 Hz on/off pulse. Same pan direction, but the rhythmic pulse makes "stopped and not aligned yet" audibly distinct from "moving and tracking" — without speech (lineup pilots have rudder + throttle in hand and can't field verbal callouts). Pulse phase is computed from `DateTime.UtcNow.Ticks` so the cadence is steady across `UpdateHeadingError` jitter; effective volume alternates between configured and 0. Pulse is forced off in the gate-lineup branch (gates aren't lines, pulse would be noise).

**Why the cross-track condition exists.** The intercept-angle controller saturates at ±30° when cross-track is large, so the desired heading is `runwayHeading ± 30°`. A pilot who turns to match that desired heading and then stops sees heading error ≈ 0 (tone silent under the heading-only pulse condition), even though cross-track might still be 100+ ft. Without the cross-track branch here, that pilot gets no audio cue at all and may believe the system has stopped working. The fix: pulse fires when stopped + EITHER dimension is misaligned. The pilot then knows to move forward — at which point cross-track decreases, intercept reduces, and the desired heading rolls smoothly toward the runway heading.

**Pulse → continuous transition: always refresh volume.** `TaxiSteeringTone.SetTone` calls `_toneGenerator.UpdateVolume(EffectiveVolume())` every sounding frame, regardless of `_pulseActive`. The previous "only refresh in pulse mode" optimization left the tone stuck at zero volume during a pulse-off transition: the pilot is stopped-misaligned (pulse fires, volume alternates 0 / configured at 3 Hz), then starts moving (`SetPulse(false)` is called); on the next frame `_pulseActive` is false, the optimization skipped UpdateVolume, and if the previous pulse cycle had set the volume to 0 (silent half) the tone stayed silent in continuous mode until something else triggered a state change (oversteer / going silent / Pause). One UpdateVolume call per ~30 Hz frame is cheap; correctness beats the micro-op.

### Turn rollout anticipation (rate-lead tone, per-aircraft)

The Taxiing-phase steering tone feeds the pilot a **rate-lead projected error**, not the raw smoothed error: `projected = error − yawRate × TurnLeadSeconds` (clamped ±30°, only while |yawRate| ≥ 1°/s — see `GuidanceGeometry.ProjectHeadingError`). The tone therefore **centres BEFORE the nose reaches the target bearing**, absorbing pilot reaction time + airframe yaw inertia: tone centred mid-turn = "this rate of turn lands exactly on the new heading — hold it"; tone panning opposite = "unwind now". Without this, the tone centred at error = 0 and the aircraft yawed 15–25° past during the pilot's reaction — the chronic Airbus turn-overshoot reported 2026-06-10. Lineup, docking, and rollout tone paths are NOT projected — they keep their own precision profiles.

**`TurnLeadSeconds` is per-aircraft** (`IAircraftDefinition.TaxiTurnLeadSeconds`, wired by MainForm at startup + aircraft switch; base default 1.2 s; 0 disables). Measured values (rollout-residual analysis over the pilot's own telemetry — residual error when yaw settles after each ≥30° turn episode):

| Aircraft | Lead | Provenance |
|---|---|---|
| FBW A32NX (A20N) | 1.6 s | Open-loop MEASURED 2026-06-10 (13 turns, median needed 0.95 s → 1.3); closed-loop revalidation 2026-06-11 rolled ~15° LONG — with the cue available the pilot waits for tone-centre instead of self-anticipating, exposing the full reaction+inertia chain → stepped to 1.6; converging |
| Fenix A320/A321 | 1.3 s | Proxy from the FBW measurement; round-2 mid-route turn ON (+2.5°). High Fenix yaw rates (14–15°/s) make low-speed quick turns centre early — a rate-tapered lead is a possible future refinement |
| PMDG 737 | 0.4 s | MEASURED + VALIDATED 2026-06-11 (0.8 prior over-led by 8.7°; at 0.4: 3/3 rollouts ON, median +1.6° — the pilot self-anticipates Boeing rollouts) |
| PMDG 777 | 0.3 s | MEASURED + VALIDATED 2026-06-11 (1.0 prior over-led; at 0.3 across two sessions: n=14, 9 ON, medians +1.2°/+3.6° incl. a 7 km 188-segment KATL taxi) |
| HS 787 | 1.2 s | base default, unmeasured |
| FBW A380 | **1.8 s — ADD AT MERGE** | The definition lives on the FlyByWire branch. MEASURED 2026-06-11 flying the A380 on the A320's 1.3 s: five rollouts went 10–42° LONG at 15–19 kt (the reported "correct left right, left right" oscillation); pilot only coped by slowing below 13 kt. One-line override when the definition lands. |

**"Straighten." cue is yaw-episode based, NOT junction-angle based.** A sustained-yaw episode opens at |yawRate| ≥ 4°/s, accumulates signed heading change, closes below 1.5°/s (direction flip restarts it). The cue fires once per episode when the episode has turned ≥ 35°, the route ahead within 60 m bends < 15° (a steady mid-curve arc holds projected error near zero BY DESIGN — the straightness gate is what prevents false fires inside long curves), and the projected error crosses centre against the yaw direction. **Do not re-introduce a `TurnAngleDegrees ≥ SHARP_TURN_ANGLE_DEG` gate:** real navdata splits 90° turns into 5–15° micro-bends — a measured KSFO route had 107 junctions and only ONE ≥ 60°, which made the original junction-gated cue dead code.

**Curve announcements ("Curving left/right.")** fire once per direction when the cumulative bend over the next `CURVE_SCAN_WINDOW_M = 100 m` reaches ≥ 30° with no single junction ≥ 20° (discrete junctions keep the existing turn callouts). Sign-keyed latch with 15° re-arm hysteresis. **Opposite-yaw deferral:** the cue is suppressed while the aircraft is still yawing ≥ 3°/s AGAINST the announced direction — near a curve's exit the scan window already reaches into the next, opposite curve, and announcing it mid-turn reads as a contradiction (pilot report 2026-06-11). It fires the moment the current turn settles.

**Deferred: user-tunable lead setting (design agreed 2026-06-11, intentionally not implemented).** When pilots want to tune beyond the per-aircraft defaults: `Dictionary<string, double> TaxiTurnLeadOverrides` on `UserSettings` keyed by aircraft code (JSON-serializes in the existing settings file, no migration); one combo in Taxi Guidance Options — "Turn anticipation (this aircraft): Aircraft default (recommended) / Off / 0.2 s … 3.0 s in 0.2 steps" — reading/writing the current aircraft's entry; effective lead = `override ?? aircraft.TaxiTurnLeadSeconds`, recomputed on dialog save + aircraft switch (the two existing MainForm wiring sites). Range rationale: measured fleet spans ~0.0–2.3 s; 3.0 gives headroom; 0 = off. Per-aircraft (not global) because the measured differences BETWEEN aircraft are the point; a global override would poison the rest of the fleet the moment one aircraft is tuned.

## Announcements

| Trigger | Distance / Condition | Announcement |
|---|---|---|
| Route calculated | — | `Taxi to runway 22L via Alpha, Bravo, hold short 13L, Kilo. Total distance 1.2 miles.` |
| Start of taxi (apron first-leg look-ahead) | first movement | `Steering guidance active. Join taxiway Alpha in 200 metres.` (or `Taxiway Alpha. Steering guidance active.` if already on a named taxiway). Distance shown in the active unit — 200 metres or ~650 feet depending on the Distance units setting. |
| Taxiway change | on segment advance | `Taxiway Bravo.` |
| Approaching turn | ~300 ft / ~100 m | `In 100 metres, turn left onto taxiway Bravo.` (metres default) or `In 300 feet, turn left onto taxiway Bravo.` (feet mode). Distance is in the active unit. Direction is computed from the aircraft's CURRENT heading toward the next segment's bearing — see "Verbal turn direction" below — so the spoken cue always agrees with the steering tone's pan. |
| Turn imminent | speed-scaled ~20–75 m / 65–245 ft | `Turn left, taxiway Bravo.` Same heading-based direction as the approaching-turn cue. |
| Crossing taxiway | ~150 ft / ~50 m | `Crossing taxiway Kilo.` (toggle in settings) |
| Hold short countdown | speed-proportional triggers (15 s / 8 s / 4 s lead; floors 300 / 150 / 50 ft, caps 600 / 400 / 200 ft) | `Hold short runway 13 Left in 100 metres.` / `Hold short runway 13 Left in 50 metres. Slow down.` / `Hold short runway 13 Left in 15 metres. Stop.` The **live** distance is spoken (unit-aware via `DistanceFormatter.FromFeet`) — hold-short does **not** use a milestone table, because its trigger distance scales with ground speed. The *Slow down* and *Stop* suffixes fire **unconditionally** (see Speed-aware directives). |
| At hold short | within radius | `Hold short runway 13 Left. Press continue when cleared.` (tone pauses) |
| Continue pressed | — | `Crossing runway 13 Left. Taxiway Kilo.` (tone resumes) |
| Approaching runway destination | ~300 ft / ~100 m | `Runway 22 Left ahead.` |
| Runway lineup achieved | heading <1° AND cross <10 ft | `Lined up, runway 22 Left. Hold position.` Tone pauses. The *Hold position* directive is the LUAW stop cue (FAA AIM 5-2-5 / ICAO Doc 4444 / EASA SERA: align with centerline and remain stationary awaiting further clearance). Convergence target matches what runway-teleport places you at (20 m back from the threshold, aligned with runway heading). |
| Gate countdown | 50 / 20 / 10 ft **or** 15 / 10 / 5 m (per Distance units setting) | `15 metres to gate.` / `10 metres.` / `5 metres. Stop.` (the *Stop* suffix fires unconditionally — see Speed-aware directives). Unit-native spacing via `DistanceMilestones.ParkingArrival`. |
| Arrived at gate | within 20 ft / ~6 m | `Gate Alpha 25 reached.` |
| Off route (recalc accepted) | >50 m for >3 s, after the route is joined | `Route changed. Now via <taxiways>. <dist> to <dest>.` |
| Speed warning | >30 kt straight / >12 kt turn | `Slow down.` (8 s cooldown) |
| Runway incursion | non-route hold-short within 40 m | `Runway crossing ahead. Hold short.` (10 s cooldown) |
| Exit approach (landing rollout) | 1500 / 900 / 500 ft **or** 500 / 300 / 150 m (per Distance units setting) | `Approaching high-speed exit Sierra 5, 500 metres.` / `Sierra 5, 300 metres.` / `Sierra 5, 150 metres. Slow down.` Unit-native spacing via `DistanceMilestones.ExitApproach`. |
| Runway-end countdown (missed last exit, or no usable exit at touchdown) | 1500 / 500 / 100 ft **or** 500 / 150 / 30 m (per Distance units setting) | `Runway end in 500 metres.` / `Runway end in 150 metres. Slow down.` / `Runway end in 30 metres. Stop.` Unit-native spacing via `DistanceMilestones.RunwayEnd`. |
| Ground traffic alert | live distance, unit-aware | `Slow down, traffic ahead, 150 metres.` (metres default) or `Slow down, traffic ahead, 500 feet.` (feet mode). Via `GroundTrafficMonitor`'s private `FormatDistance`, keyed on the independent `GroundTrafficUseMetres` toggle (see gsx.md: never fold it into `GroundDistanceUnit`). |
| On-demand status | Output > `Y` | `Taxiway Bravo. In 400 metres turn right onto Kilo. 0.8 miles to destination.` (distances in active unit; NM used for totals over ~1 NM regardless of unit setting). |
| Repeat last | Output > `Ctrl+Y` | Replays the most recent **actionable instruction** verbatim (turn callout, hold-short, taxiway change, lineup, arrival, distance countdown). Distinct from `Y` (status), which recomputes a snapshot from current position. Useful when the announcement was clipped by another sound. Returns `"No taxi instruction yet."` if guidance is active but nothing has fired; `"No taxi guidance active."` otherwise. Implemented via `TaxiGuidanceManager._lastInstruction`, populated only by `AnnounceInstruction()` — two peripheral sites still call plain `_announcer.Announce` without populating `_lastInstruction`: (a) the LoadRoute route summary, (b) the periodic ground-speed bucket announcer — so the Repeat-Last buffer keeps the most recent actionable callout. |
| Where am I | Output > `Alt+Y` | `Taxiway Bravo at KJFK.` / `Gate A25 at KJFK.` / `Runway 22L at KJFK.` Works with or without active guidance. |
| Look around | Output > `Alt+L` | `Taxiway A at KTIW. Narrows Aviation Hangar, to the right, 80 metres. Control Tower, ahead, 200 metres. Fuel, behind and to the left, 210 metres.` Where you are, the apron or concourse you are in, then the nearest features. Ground-only. |
| Surroundings window | Output > `Ctrl+Shift+L` | Read-only list of everything within 1 km, nearest first, with the airport's fuel and frequencies on the first row. |
| Taxi to a place | Taxi form, destination type **Place** | Lists every FBO, hangar, fuel island, terminal, cargo area the catalog knows that resolves onto a stand (or a taxi node) — "Narrows Aviation, FBO, Parking 12" — and routes there like a gate. |

### Verbal turn direction (heading-based, not route-static)

`ComputeTurnVerbalFromHeading(targetBearing, aircraftHeadingTrue)` derives the spoken "left / slight right / continue" from the angular difference between the aircraft's current true heading and the next segment's bearing — the same input the steering tone uses for its pan, so the tone and verbal cue always agree.

The route's pre-computed `TaxiRouteSegment.TurnDirection` (`nextSeg.bearing − currentSeg.bearing`) assumes the aircraft is exactly on-axis with the current segment. When the aircraft is off-axis — post-pushback rotation, after a wide turn, brief deviation, or sitting at the gate before moving — the actual turn it must make to align with the next segment can be the OPPOSITE direction from the route's intent. Before this fix the verbal cue ("turn left") sometimes contradicted the (correct) tone (panning right). Following the tone was always the right call; the verbal is now correct too.

All three spoken sites use the helper: advance notice (`In 300 feet, turn left onto Kilo`), "now" callout (`Turn left, taxiway Kilo`), and the on-demand status query. The `TurnDirection != "straight"` predicates stay on the static field because they only ask "is there a turn at this junction" (true regardless of aircraft heading); the SIGN of the turn comes from the helper.

### Deduplication

Crossing announcements use a 45-second per-taxiway-name dedup window (`_recentCrossingAnnouncements`). Many airports split what looks like a single intersection into two closely-spaced graph nodes; without the dedup the same name would fire twice in a row ("Crossing taxiway Link 53... Crossing taxiway Link 53").

### Route summary count (implicit destination hold-short excluded)

`BuildRouteSummary` takes an `isRunwayDestination` flag. When the destination is a runway, the final segment is **always** marked `IsHoldShortPoint = true` by `TruncateToHoldShort` — that's the runway hold line. That implicit hold-short is subtracted from the user-facing "N hold short points" count so the summary only advertises intermediate hold-shorts the pilot must act on. Without this, a plain `via A B K to runway 22L` clearance with no intermediate stops would announce "1 hold short point" confusingly.

### Start of taxi — apron look-ahead

If the aircraft is on an unnamed apron taxilane connector at route start, `StartGuidance` walks forward through the route segments to the first one with a non-empty `TaxiwayName`, accumulating the distance. When the distance to join exceeds 10 m the announcement becomes `"Steering guidance active. Join taxiway Alpha in 200 metres."` (or feet, per the Distance units setting) instead of immediately announcing the taxiway name. This matches the real-world sequence after pushback — the aircraft rolls down the stand taxilane to the apron edge before joining the first named taxiway. The distance is formatted via `DistanceFormatter.FromMetres`.

### Convergence target: taxi guidance ends where teleport places you

For both runway and gate destinations, taxi guidance and the teleport hotkeys are designed to converge on the **same final aircraft state**:

| Destination | Teleport places you at | Taxi guidance ends with |
|---|---|---|
| Runway | 20 m back from the threshold, on the centerline, heading = runway heading, on ground (`SimConnectManager.TeleportToRunway`) | Aircraft on the runway centerline, aligned with runway heading within 1° (lineup-aligned hysteresis), `Lined up, runway X. Hold position.` announced, tone paused |
| Gate / parking | At the parking spot lat/lon, heading = gate heading, on ground (`SimConnectManager.TeleportToParkingSpot`) | Aircraft within 20 ft of the parking spot (`GATE_ARRIVAL_RADIUS_FEET`), unit-aware parking countdown announced (50/20/10 ft or 15/10/5 m per Distance units setting), gate-lineup heading tone silent at parking heading |

The "Hold position" wording on runway-aligned matches the FAA AIM 5-2-5 / ICAO Doc 4444 / EASA SERA "line up and wait" procedure — align with the centerline and remain stationary awaiting further clearance from ATC. This is the universal stop point for LUAW *and* the spot where you'd briefly stop before applying takeoff thrust under "cleared for takeoff." Either way, that's the convergence target.

**Parking-listing parity with the gate-teleport dialog.** The taxi-assist form's parking dropdown is built from `Services/ParkingSpotSource.GetSelectableGates(...)` — the same resolution the gate-teleport dialog uses, and the SELECTABLE half of the seam whose NAMING half (`GetNamedSpots`) feeds the graph builds and the SayIntentions parked-at-the-right-stand check; the two shapes deliberately return different lists but agree on every stand's name (see "One name for a stand" below) — and labels each entry with `ParkingSpot.ToString()` (which expands to e.g. `"P 21 - Ramp GA Large (Jetway)"`). Earlier the listing was driven off graph nodes that happened to be tagged with a `ParkingName` during graph build, which silently dropped any parking spot whose lat/lon didn't have a nearby graph node — common in third-party scenery (Colombo, KORD payware, etc.) whose taxi-path data lags the parking layout. A pilot given "Parking 21" by ATC would see "Parking 21" in the gate-teleport dialog but NOT in the taxi-assist form. Now the same set of entries appears in both. Each parking spot's actual lat/lon is the lineup convergence target; routing endpoint is the nearest graph node within 100 m (the `MAX_PARKING_TO_GRAPH_M` floor — beyond that, the spot is dropped because there's no realistic taxi path to reach it).

### Taxiway connectivity in the route form

`TaxiAssistForm`'s "Add Taxiway" dropdown lists **every named taxiway at the airport**, with heuristically-connected taxiways at the top (sorted by aircraft distance) and the rest alphabetically below. The connectivity heuristic is `TaxiGraph.GetConnectedTaxiwayNames(taxiwayName)`, which counts **named-taxiway crossings** (transitions between distinct named taxiways) rather than raw graph hops — walking along the seed taxiway and through unnamed connectors is free; only crossing into a different named taxiway counts toward the budget (`maxCrossings = 2`).

Why crossings, not hops: at KSFO, M5 connects to M1 via 4–6 unnamed connector segments. The previous 4-hop BFS would visit those connector nodes and exhaust its budget before encountering any M1 edge, hiding M1 from the dropdown entirely. The crossings-based metric mirrors how ATC clearances read — "M5 M1 A L" is 3 crossings end-to-end regardless of how many short connectors physically lie between them.

Why also list non-connected taxiways: occasional ATC clearances skip a taxiway the heuristic doesn't surface, and rare scenery has graph quirks our metric misses. Showing the full airport list as a fallback ensures the user can always match what ATC said. The router's constrained-path logic and `FindRunwayBridge` resolve the actual route from any pair of selections — picking a non-connected taxiway just means the router does more work, never that the route silently fails. `GetReachableTaxiwayNames(taxiwayName, maxCrossings)` is public for callers wanting a different budget; `GetAllTaxiwayNames()` provides the full list.

### Navdata coverage caveat (sparse extractions)

`navdatareader` builds taxi_path data from the user's installed scenery. **MSFS 2024's vanilla scenery has measurably sparser taxi_path coverage at some airports than MSFS 2020.** Example at KPHX: fs2020.sqlite has 3939 taxi_path rows and 121 named taxiways; the same DB schema built from MSFS 2024 has only 845 rows and 112 names — taxiway "O" exists in the 2020 build but is absent from the 2024 build. A user whose ATC clearance names a taxiway that isn't in their DB cannot route through it; the form lists every named taxiway the DB knows about, but it cannot invent one. Workaround: rebuild navdata after a sim or scenery update, or use the FS2020 navdata if the airport is known to be richer there. Schemas are 100% identical between fs2020 and fs2024 builds (same atools / Navdatareader version), so swapping DBs is a drop-in operation through `DatabasePathResolver.ResolveExistingDatabasePath(simVer)`.

### Runway crossings and entries

> **⚠ THE DERIVED-CONSTANT TRIPWIRE — read before changing any tolerance in this area or the
> landing rollout.** Several rollout constants are **arithmetic consequences** of two margins, and
> nothing in the code, the compiler or the tests links them:
>
> | Constant | Where | Derived from |
> |---|---|---|
> | `RolloutExitGate.VacatedShortAlongTrackFeet` = 350 | `RolloutExitGate.cs` | the exact **5 m** gap between the exit-node corridor (`halfWidth + HandoffReachMarginM`, 15 m) and the pavement boundary (`halfWidth + RunwayClearMarginM`, 10 m) |
> | `RolloutExitGate.EarlyVacateMaxPassedFeet` = 1400 | `RolloutExitGate.cs` | same gap |
> | the 25 m corridor clamp (`HandoffReachDefaultHalfWidthM`) | handoff reachability | same gap |
> | `RolloutExitGate.RunwayClearMarginM` = 10 | `RolloutExitGate.cs` | the codebase's ONE definition of "off the runway" |
> | `RolloutExitGate.DefaultRunwayWidthFeet` = 200 | `RolloutExitGate.cs` | fallback half-width, **different** from `RunwayShape.DefaultHalfWidthMeters` (75 ft) |
>
> Change a half-width or either margin and those three numbers silently stop being derived. There is
> no compile error, and the boundary tests keep passing because they pin the *old* arithmetic.
> `RunwayVacateResolver` additionally keeps its **own** copy of the 75 ft default and its own
> `SameRunwayLateralM = 30.0`, the latter calibrated against the residual scatter left by
> `TaxiGraph.SnapStartToRunwayCenterline` — so loosening the snap invalidates it too. **Re-derive all
> five before touching any of them, and say so in the commit message.**
>
> **⚠ TWO THINGS IN THIS AREA WERE MEASURED AND DELIBERATELY LEFT ALONE. Do not "fix" either.**
>
> 1. **The narrow lateral band is correct.** It was reported that replacing the fixed 75 ft default
>    with the runway table's real half-width loses off-centreline detection (at 8 m off, 53 of 419
>    runways missed against the old behaviour). Measurement refutes it: the nearest off-runway graph
>    node sits **3.2 m** from a runway centreline at p0 and **5.1 m** at p1 (re-measured on a 2026-09
>    fs2024 build: 4.2 m and 5.8 m over 381 runways), and at SC99 a taxiway node is **4.2 m** from
>    the centreline of a runway whose half-width is **4.0 m**. There is no headroom to widen the band
>    without claiming the adjacent taxiway, and 42,661 of 48,321 runways are narrower than 150 ft, so
>    the strict real-width test is right for most of the database.
>    `RunwayShape.MaxPlausibleHalfWidthMeters` does not disturb this — it only narrows a half-width
>    computed from a MALFORMED `runway.width` row (over 400 ft) and never widens a band.
> 2. **`RouteProgressMeters` returning `0.0` for both "at the route start" and "not near this route"**
>    reads like a bug and is not. The 30 m `RouteJoinMaxCrossTrackMetres` bound exists so that an
>    aircraft stopped at the KORD 04L hold line, 90 m beside a route that starts along the runway, is
>    still treated as the route's first point and keeps its start hold. The "fabricated start holds"
>    originally measured came from displacing the aircraft perpendicular to its own route by up to
>    1,500 m, which production cannot produce because the route is built from the aircraft's position.
>    Separating the two meanings would undo the KORD guard. If you touch it, keep the prepend
>    behaviour identical and change only the naming.

FAA AIM 4-3-18 and ICAO Doc 4444 require an aircraft to hold short of every runway it crosses, with an explicit clearance for each. Guidance holds before every runway a route **crosses or enters**, reports every one of them, and places each stop off the pavement. The rules live in pure code — `Navigation/RunwayShape`, `Navigation/RunwayRouteClassifier`, `Navigation/RouteRunwayCrossings` — pinned by `RunwayShapeTests`, `RunwayRouteClassifierTests`, `RunwayHoldPlacementTests`, `RunwayEventDescriptionTests` and `RunwayMembershipTests`.

**One adoption seam.** A route becomes the live route only through `TaxiGuidanceManager.AdoptRoute`, which runs `ApplyAutoHoldShortPasses` (the pass, its `Route crossings:` log line and the Progressive Taxi cleared-crossing strip) and then assigns `_route`. `LoadRoute` adopts with phase `load`; `TryRecalculateRoute` adopts with phase `recalc`, **below** its no-op guard, so a discarded recalc neither re-tags a route nobody adopts nor logs a line claiming it did. Before 2026-09 only `LoadRoute` ran the pass, and a recalculation silently produced a route with no crossing holds (PHNL 2026-09-03: 26R, 04L and 04R were tagged at build time, a recalc 88 s later dropped all three, and the aircraft crossed them at 13-19 kt with no callout). Assign `_route` there and nowhere else.

**Where a runway is: `RunwayShape`.** Every on-the-runway question reads one accessor, and there is **ONE shape per centreline**, memoised in a weak-keyed table (PR #238 §8b). `RunwayShape.For` runs per runway per classification, per passage for `otherRunways`, per NODE per runway in `IsOnAnyRunway`, per runway per Where-Am-I keypress and once per hold node per candidate runway in `TaxiGraph.Build`'s naming pass — and it used to ALLOCATE TWICE per call, because the pavement-usable test built a throwaway shape purely to reuse `Project` and the verdict then threw it away and built a second. Measured over 300 fs2024 airports, the 171,254 `For` calls of an `IsOnAnyRunway` sweep fell from **48 ms to 9 ms** and from 342,508 allocations to 408. A centreline is immutable once `ApplyPavement` has run (nothing writes those fields elsewhere, and that pass runs before the graph is published), the table's weak keys mean a graph that goes away takes its shapes with it — no cache to invalidate on a database switch — and concurrent callers need no lock. Do not inline a second copy of the projection math instead:


- The runway-table **pavement** ends (`Pavement1` is the end named `Name1`) when they are a sound line for this centerline — all four coordinates finite, neither end (0, 0), at least 1 m long, and both of the centerline's own start rows within half-width + 10 m of the pavement axis. The last test rejects a heading-pass mis-pair that handed a centerline another runway's pavement (EDVQ: 09R/27L was given 09C/27C's); name-swapped rows (AYCH) sit on the axis and pass.
- Otherwise the **start rows** (`Lat1..Lon2`). They come from the navdata `start` table and `SnapStartToRunwayCenterline` repairs them only laterally, so at a displaced threshold they sit far inside the pavement — OMDB 12R's row is 761 m in, and taxiways K5, K6, K7 and M8 cross in that band, where the old start-row test placed no hold at all.
- Half-width: the runway's own width when the pavement is used, the start rows' 75 ft default otherwise; a non-positive value falls back to 22.86 m.
- The **extent** along the runway envelopes the pavement ends and both start rows. The start-row line is NOT contained in the pavement line: a displaced threshold legitimately puts a row outboard of the pavement end, and sub-metre offsets move nodes across the line. **Nothing bounds that envelope, so the rows reaching it are repaired instead** (`TaxiGraph.PullOutboardStartRowOntoPavement`, PR #238 §4): a row more than `MaxOutboardStartRowMetres` (100 m) outboard is slid back along the axis onto the nearer pavement end, keeping its lateral offset. Measured over 812 start rows at 300 fs2024 airports, exactly THREE sat outboard at all — 29.5 m (URWW 05, legitimate and untouched), 471.2 m (KSAW 01) and 799.9 m (LIMC 17L) — and at LIMC that 800 m band claimed three graph nodes on **taxiway AB**: Where-Am-I answered *"Runway 17L"* on a taxiway, `TryGetRunwayAtPosition` seeded takeoff assist there, `RunwayUnder` silently skipped a start hold, and `IsOnAnyRunway` barred those nodes from ever being a hold stop. After the repair: 1 outboard row, 0 claimed nodes, no centreline lost and still 0 detection misses at every start row.
  - The repair is UPSTREAM, where the row enters the graph, because runway-destination lineup anchors on the `start` table and at LIMC 17L the lineup target IS that bogus row — capping the extent could make the lineup point "not on the runway" and break the reach test for that runway. Pulled back, the row becomes the pavement threshold, which is where a 17L departure actually begins.
  - ⚠ It is NOT part of `SnapStartToRunwayCenterline`, which only ever repairs the LATERAL error. Widening THAT into an along-track relocator once projected name-swapped rows onto their named runway, put both of an airport's rows at midfield, failed the 200 m separation test and cost AYCH the centreline it had. A name-swapped row sits AT the other end, i.e. inside the pavement, so it is not outboard and the repair never touches it.
- Projection: equirectangular from end 1, 111,132 m per degree of latitude, cos(mid-latitude) for longitude; `along` unclamped, `lateral` signed.
- **Two sideways thresholds, deliberately different.** ON the runway is `|lateral| <= halfWidth` inside the extent (classification). CLEAR of the runway is `|lateral| > halfWidth + RolloutExitGate.RunwayClearMarginM` (10 m, the codebase's one definition of "off the runway") and decides where a stop may go. Detecting with the margin would hide a real crossing for a route starting just outside the edge (the KATL shape: 25.7 m out on a 150 ft runway, then across).
- **CLEAR is EXTENT-AWARE** (`IsClearOfAt`, PR #238 §3): off the runway means outside the extent OR beyond the lateral band — the exact complement of `ContainsAlongLateral`, which the lateral-only `IsClearOf` was not. A node BEYOND a runway's along-track extent but near its axis used to be neither "on the runway" nor "clear of" it, so hold placement stepped over it and every node behind it and fell through to a **start hold**: *"Stop. Hold short of runway 09"* before moving, hundreds of metres from the real hold line, with no hold where the route actually meets the pavement. Trigger shape: a taxiway running off the end of a runway on or near its extended centreline — a turnpad lead-in, or any approach to a crossing from beyond the end. Measured on 1,649 fs2024 routes, the repair turned **23 of 31 start holds** into a real stop at the node where the route meets the runway, and the number of stops placed on any runway's pavement stayed **0**.
- ⚠ The two walks keep DIFFERENT lateral margins and that is an owner ruling, not an oversight. The scenery-hold-line walk (1) passes margin **0** — the bare half-width — so a painted line hugging the pavement edge is still usable: measured, SC99's line is 7.2 m out on a 4.0 m half-width, and tightening it to the clear margin would reject real hold lines. The fallback walk (2), which invents a stop of its own, passes the full `RunwayClearMarginM`. So a SCENERY hold line may sit inside the 10 m margin; a stop this code chooses for itself may not.
- End naming (`NameAt`): the end nearer the point along the runway — in a plane exactly the closer-end rule — on the effective ends, with an empty name falling back to the other.

**Entry or crossing: `RunwayRouteClassifier`.** Each runway is judged on its own over the route's node list. A node is ON the runway, or CLEAR with a side (the sign of `lateral`; a clear node exactly on the extended axis beyond the extent has none). Onto the runway from a clear node and back off the SAME side is an ENTRY — as is a route that ends on the runway; off the OTHER side is a CROSSING; a route that starts on the runway and leaves meets nothing. Two clear nodes on opposite sides with no node on the pavement between them (one edge spanning the runway: KBOS taxiway C over 04L, both nodes about 35 m out) is a crossing when that stretch meets the centerline inside the extent, and nothing when it passes a runway end. This replaced a strict per-edge segment intersection whose verdict at a node on or centimetres from the line was decided by float placement: it lost crossings through a node exactly on a north-south centerline (P19, KBDN), invented crossings for edges lying along a runway (ESMX, routes starting on the runway) and at end-of-runway exits whose junction sits centimetres over the line (KORD 10R W5), and let one runway's first match hide another's at an intersection (KBDR). Accepted simplification: a route leaving a runway off its very end, where lateral is near zero, may read as entry or crossing by a hair — both get the same hold and both trip the landing guard; only the spoken word differs.

**Where the hold goes: `RouteRunwayCrossings.ResolveHoldStop`.** From the first node on the runway (for a spanning edge, the last clear node) back toward the route start:

1. the first scenery hold node (HS/HSND/IHS/IHSND) within `CrossingHoldLookbackMetres` (150 m) whose name names this runway, its reciprocal or no runway, and which is off the pavement — off by the BARE half-width (see the margin note above), and off by the EXTENT too, so an on-axis hold line beyond the runway end is a real hold line and no longer reads as a node on the pavement. A hold node on the pavement is skipped; one naming a different runway ends this search (EGKK C northbound across 26R: the line behind it is 26L's);
2. otherwise the nearest node at or before the entry that is **clear** of the runway. LEBL 24R via D5 once held 21 m from the centerline of a 60 m runway — 9 m inside the edge, with the painted line 105 m back — and EGKK C's crossing was held on the centerline through a node named for the other runway;
3. otherwise the route's start node: a **start hold** (below).

**Never on any runway.** A stop is never placed on the pavement of ANY runway. A walk that meets another runway's pavement may only end at the stop already placed for that runway, which is shared and names both, or it places none. (LEBL: 20's stop once landed on 06L/24R, 38 m before 20.) One stop can then cover both runways even when the second is some distance beyond the first: the pilot needs clearance for both before entering the first.

**A start hold is one of those already-placed stops.** It tags no segment — it lives on `TaxiRoute.StartHoldRunway` — so `IsExistingStop`, which reads `segments[nodeIndex - 1]`, is false at node 0 by construction and could not see it. `ResolveHoldStop` therefore takes a `startHoldPlaced` flag and treats node 0 as carrying a stop. Without it a route that started held for runway A and then crossed B beyond it latched `crossedOther` on A's pavement, found nothing to share, returned -1, and left B with **no hold and no stop at all**: the pilot heard only *"hold short of runway A"*, pressed Continue, and crossed both. The start hold sits before every node on the route, so sharing it is always safe, and its label then names both ("runway 09 and runway 01"). The same-runway guard is untouched — a second passage of the runway whose own pavement the walk meets still returns -1 and is reported unheld, because that stop is on the far side of the pavement it must stop before.

Neither walk passes a segment that is already a hold-short; reaching one before a clear node shares that stop, whose label then names both runways in route order ("runway 06 and runway 29"). The one exception is an existing stop on another runway's pavement (in practice a pilot's "end of taxiway" stop): like every node there it is never a stop for this runway, so the walk passes it and may still share a stop behind it. Neither walk passes back over an earlier stretch of the same runway: when the route crosses or enters a runway again and no node since it last left that runway is clear of it, the second event gets NO stop — it is reported (summary, `unheld=` in the log) but never placed inside the 10 m margin, and never borrows the stop before the first crossing, which would send the aircraft back across the runway without a hold. Every candidate is at or before the runway and off its pavement, so a stop can only move EARLIER than the old "segment before the crossing edge" — do not "improve" this into a search that can move it forward. The first qualifying hold node is the one nearest the runway, the full-length hold of a normal clearance; the CAT III / ILS preference stays a destination-runway opt-in and does not apply. Only navdata HS/HSND markers are used — not the banned OSM `holding_position` sharpening.

**The automatic pass (`InsertRunwayHoldShorts`)** classifies the adopted route against every centerline, places one hold per event, and records every event on `TaxiRoute.RunwayEvents` (kind, designator, held or not):

- On the destination strip only the route's own ARRIVAL (an entry that ends on it) is skipped. Every other entry or crossing of that strip is held and announced under the designator the pilot selected. Do not restore a blanket name-equality skip: a crossing is named after whichever end is nearer it, and that skip dropped genuine crossings of the active runway (2026-08-24).
- Two crossings of the same runway are two events with two holds (KSFO 2026-07-01: the only route onto Q re-crossed 28R).
- A stop the aircraft has already rolled more than 10 m past, measured along the route from the aircraft's position (`RouteRunwayCrossings.RouteProgressMeters`), is recorded as not held and not placed: a recalculated route is built from the live position, and "Stop. Hold short of runway 26R" to an aircraft already cleared onto 26R is a stop in the worst possible place. It is never judged by projecting onto a hold segment's own axis, which dropped holds far ahead of an aircraft still at its stand (EHAM 18L at E10, 1,448 m away). The measurement counts only within 30 m of the route (`RouteJoinMaxCrossTrackMetres`): an aircraft further away has not joined it, so nothing on it is passed. Without that bound an aircraft stopped at the KORD 04L hold line on G, 90 m beside a route that starts along the runway, read as 27 m along it and lost its start hold.
- Labels (`ComposeCrossingLabel`): a DB name for THIS pavement is kept, a bare holding-point name gains the runway ("runway 15R at N"), a name for a different pavement is corrected, and a user "end of taxiway" label is never touched.
- **Compass-point designators are read.** fs2024 carries 204 runway ends named N/S/E/W/NE/NW/SE/SW, at 21 airports that also have taxi paths, so they reach the taxi graph. The designator pattern matched digits only, so `ExtractRunwayDesignator` returned null for every one of them: `ComposeCrossingLabel` took its "names no runway" branch and prefixed an already-prefixed label a second time — live 3KS4 and RJSSE spoke *"Stop. Hold short of runway N at runway N."* — and `LabelNamesOnlyRunway` was false for such a label, so the Progressive strip could never clear a crossing of one. The numeric branch is matched FIRST and each compass alternative ends at a word boundary, so "runway 15R at N" still reads 15R and "runway North side" is not a designator. `Reciprocal` gains the compass pairs (N↔S, E↔W, NE↔SW, NW↔SE), which is what lets a clearance across "S" clear a stop labelled for its own other end "N".

**The start hold.** When the only stop is the route's start node, the route records `TaxiRoute.StartHoldRunway`. Whether it may is decided by the pass itself, from the aircraft's own state — there is no `allowStartHold` flag any more (PR #238 deferred finding §2): **not while the aircraft stands on or within the clear margin of any runway** (`RunwayWithinClearMargin`: half-width + `RolloutExitGate.RunwayClearMarginM`, along the axis as well as laterally — the same line walk 2 demands of a stop it invents, because a start hold IS a stop where the aircraft stands, and 4 m outside the pavement edge the tail is still over the runway), **not once it has rolled more than 10 m past the start node**, and **not on a recalculation** (`AircraftPosition.MayStartHeld`, set false by `TryRecalculateRoute` alone: that route is built from an aircraft already committed to where it is going, and a stop where it stands would land on top of "Route changed"). A landing-rollout route needs no flag: the clear margin refuses the hold while the aircraft is on or beside the pavement and the 10 m rule once it has rolled on. A ground-speed gate was tried first (PR #243) and withdrawn — the manager's speed is 0 on every fresh Calculate and live only mid-guidance, so the gate was inert exactly where it was meant to bite, and 3 kt is the codebase's "stopped" line, not a "committed" one: a pilot re-importing a clearance at 8 kt thirty metres short of a runway can still stop, and had lost the hold that told them to, while a recalc at 2-3 kt (above `OFF_ROUTE_MIN_GS_KTS`, below the gate) could still start held. `LoadRoute`'s `landingRolloutRoute` parameter only LABELS the crossings log line `phase=touchdown`.

The deleted flag was a second, differently-shaped answer to a question the pass already asks. Both landing-handoff sites computed it as `!IsWithinRolloutRunwayLaterally(lat, lon)` — lateral-only, single-runway, along-track **unbounded**, carrying a 10 m margin and a 200 ft default width — and handed it to a pass that then asked the same question through `RunwayUnder` → `RunwayShape.Contains`, which is extent-bounded, zero-margin and falls back to 75 ft. Three measured divergences on the same lat/lon in the same call chain, each **dropping** a legitimate start hold that PR #238 then announced out loud as *"with no hold short point for runway X"*: an aircraft rolled off the **far end** (short field, overrun, backtrack turnaround) is still inside the infinite strip; one 5 m outside the pavement edge is inside the 10 m margin; and on a width-less centreline the two disagree over a 7.6 m band by construction. The old plumbing also travelled bool → `"load"`/`"touchdown"` string → bool, so a fourth phase or a typo silently disabled start holds with no compile error. `StartGuidance` starts the tone paused in `HoldShort` (entered through `Taxiing`, the transition on which MainForm starts the position feed), skips "Steering guidance active", and publishes the one hold sentence (*"Stop. Hold short of runway 12R. Press continue when cleared."*) as `LastRouteStartHoldCue`. **Every caller of `StartGuidance` must speak it**: MainForm hands guidance no position frames while it holds (its `UpdatePosition` gate lists the per-frame states, and `HoldShort` is not one), so no later frame can. `TaxiAssistForm` folds it LAST into its single standstill utterance on Calculate and speaks it immediately as a Progressive leg's opening instruction. A route adopted while guidance is already running without `StartGuidance` — a landing-exit re-route — enters the hold on its first taxiing frame and speaks the sentence in that same frame, unless the aircraft is already past the start node or stands on a runway's pavement, which logs `Start hold skipped:` with the reason beside the adoption's `Route crossings:` line. Continue clears it and resumes on segment 0; a new route and `StopGuidance` clear the cue. When the form has not already folded the route-start turn cue into its utterance, that cue rides in the resume sentence (*"Continuing. Sharp turn left onto taxiway K."*) instead of interrupting "Continuing." on the next taxiing frame. The aircraft's own position counts as the route's first point, so a route whose first node is already on the runway still starts held. Calculate at a hold line often starts the route at the runway node ahead (KORD 22R at D). The aircraft is taken as the first point only while it has not already rolled past the start: one already more than 10 m along the route is on it, and prepending it would invent a crossing back to node 0. An aircraft standing on any runway's pavement never gets a start hold, whatever the caller.

**Explicit per-row picks (`ApplyUserRunwayHoldShorts` → `ApplyUserRunwayHold`).** A pick for runway X after taxiway Y binds to the FIRST run of segments tagged Y, counting repeats in the entered sequence (KBOS *"N, hold short 15R, N, hold short 22R, N"*), so a same-named taxiway continuing across the runway (KSFO D over 10R/28L) is honoured. It is honoured when the route enters or crosses X at or after that run's start, placed by the same resolver as the automatic pass (one crossing never gets two stop points), labelled as the pilot typed it — also at the pilot's own "end of taxiway" stop, which the automatic pass never relabels — and can start held. **An honoured pick also carries its own `TaxiRouteRunwayEvent`** (`ApplyUserRunwayHold`'s `placed`), merged back in by `AdoptRoute` after the automatic pass — which OWNS the event list and resets it — through `MergeUserPickEvents`, de-duplicated by runway (either end) AND kind so a passage that pass did record is not counted twice. Without it a pick on the DESTINATION STRIP was named nowhere at all: the automatic pass skips the route's own arrival, `DescribeRunwayEvents` then said nothing, and `CountNonRunwayHoldShorts` skipped it too because its label DOES name a runway — so a pilot who picked *"hold short of runway 04R"* on a route to 04R heard no mention of it in the summary and was then stopped by a hold they were never told about (PR #238 §7). The destination-strip skip itself is untouched: it has its own incident history (a blanket same-runway skip once dropped genuine mid-route crossings of the active runway, 2026-08-24) and still skips ONLY the route's own final arrival. The "requested hold-short(s) could not be set" note names each pick the route does not enter or cross after its taxiway ("route does not cross it after taxiway Y") and each pick with no safe place to stop ("no safe place to hold short after taxiway Y": the stop is already passed, there is no clear node or existing stop before the runway, or the start hold is refused — the aircraft is rolling, or stands on a runway). The picks run before the automatic pass, which shares a stop they resolved to.

**One Continue per runway at a shared stop** (`Navigation/RunwayHoldStages`, PR #238 §6). When two runways resolve to the SAME stop the label merges (*"runway 09 and runway 01"*) and one segment is tagged — and guidance has exactly one `HoldShort` state and one `ContinuePastHoldShort` per stop point, so a single Continue used to authorise crossing BOTH, against this manager's own stated rule that explicit crossing clearance is required for EACH runway (controllers issue them one at a time, and an aircraft must have crossed the previous runway before the next clearance is issued). The stop now holds a LIST of designators, read from its own LABEL rather than carried as a second list on the route: the label is already the one place the merge is recorded, it survives every route mutation that preserves the stop, and deriving it keeps the new state to one index in the manager. The hold sentence names every runway as it always has and ends *"Press continue when cleared for runway 09."*; that press answers *"Runway 09 cleared. Still holding. Press continue when cleared for runway 01."* and the aircraft does **not** move — the state stays `HoldShort` and the tone stays paused, which is why the wording is "Still holding" and not "Continuing". The last press resumes exactly as before. Reciprocal designators are folded (both ends of one pavement are one runway and one clearance), and a single-runway stop takes the unchanged path byte for byte — `RunwayHoldStages.From` returns an empty list. ⚠ NOT applied at the DESTINATION hold: a Continue there is a lineup or takeoff clearance handing over to the lineup state machine, a different meaning from a crossing clearance, so a destination stop that also guards another runway keeps its single press — a deliberate limit, recorded rather than guessed at. Measured frequency: 0 of 2,518 sampled fs2024 routes produced a start hold naming more than one runway, and shared segment stops are likewise rare.

**Progressive Taxi "after crossing runway X"** strips a stop, or the start hold, only when every runway its label names is X (reciprocal-aware, `LabelNamesOnlyRunway`); a shared stop that also guards another runway stays. X's recorded **events** go with them. The summary is built from events, not labels, so leaving them behind announced *"crossing runway X"* for a runway the pilot is cleared across — and, once an unheld event is flagged (below), would have read *"with no hold short point for runway X"* over a deliberate clearance. The terminator already names X. A shared stop's other runway keeps both its stop and its event.

**What the pilot hears.** The route summary and the "Route changed" callout build their clause from `TaxiRoute.RunwayEvents`, not from hold labels: *"crossing runway X"* / *"entering runway X"*, reciprocal designators merged as one pavement speaking both names ("10L/28R"), repeats counted ("twice", "3 times"). Each event carries the designator its stop announces — the pilot's own pick, or a scenery hold name for the reciprocal end that the stop keeps — so the summary pre-announces exactly the names the stops will say; an unheld event carries the destination designator or the nearer end. Every entry and crossing is named, held or not — before 2026-09 only held crossings reached either describer. **A runway that could not be held is then named again in a trailing warning** — *"crossing runways 06L and 02, entering runways 24R and 02, with no hold short point for runway 06L"* (a real LEBL route) — each pavement named once however many of its passages went unheld, reciprocals merged. Naming it without the warning was worse than the silence it replaced: an unheld crossing was worded EXACTLY like a held one, so the pilot was told the route crosses 06L, waited for the *"Stop. Hold short of runway 06L"* the tactical callouts would never speak, and rolled across. `Held` reached only the `unheld=` log field. A route whose runways are all held — 2,500 of 2,518 sampled fs2024 routes — gains no extra words. The "N hold short points" count is label-based and ignores holds naming a runway; a runway destination's own countdown rail is excluded (`ShouldExcludeFinalHold`). The log line reads e.g. `Route crossings: phase=load dest="Runway 04R" segments=41 crosses=26R,04L enters=(none) unheld=(none) startHold=(none)`.

**Other runway checks share the shape.** Where-Am-I's centerline scan (`DescribeLocation`, half-width + 5 m), takeoff-assist detection (`TryGetRunwayAtPosition`, strict — no margin), `RunwayVacateResolver.IsOnDifferentRunway` (strict), the reach walk (`RouteEndWalkToRunwayMeters`) and the END a hold node is named after all read `RunwayShape`. **And takeoff assist takes its END from the shape too** (`RunwayShape.DepartureEndFor`, PR #238 §5): the designator, the threshold and the heading come out of ONE frame together. It used to migrate only its MEMBERSHIP test and still pick the end from the centreline's `HeadingDeg1` and `Lat1/Lat2` — the START-ROW frame — while Where-Am-I named it through `NameAt`, the pavement frame. On a **name-swapped** centreline the two are reversed: measured over 405 centrelines at 300 fs2024 airports, standing 25% along, four disagreed outright (AYCH 03/21, OIII 11R/29L, URWW 05/23, EDVQ 27C/09R — the `start` row labelled "03" physically sits at the 21 threshold carrying 21's heading), so a blind pilot asking Where-Am-I was told one runway while the takeoff-assist reference seeded at the same spot carried the other, along with that end's threshold coordinates. After the fix: 4 -> 0. The THRESHOLD is still a `start` row — runway-destination lineup anchors on the start table, which is what accounts for displaced thresholds and starter extensions — but the row paired with that end **by position**, not by name index. At AYCH the shape's end 1 is the pavement 03 end, the row nearest it is the one labelled 21, and the answer is "you are at the 03 end, line up here". ⚠ A remaining disagreement at a runway INTERSECTION is not this: at EGXE a point on 16/34 is also on 03/21, and the two answer different questions about it — Where-Am-I names the end you are NEARER, takeoff assist the end you are DEPARTING FROM — so a heading across the other runway legitimately picks the other end. Hold-node MATCHING (`MatchHoldShortRunwayName`: nearest start-row centerline within 150 m, clamped) is unchanged on purpose — at EGKK the hold nodes between 26L and 26R (647 IHSND, 637 HSND) are 26L's lines, and matching against 26R's pavement would rename them 26R; with the resolver above a misnamed hold node can no longer put a stop on the pavement. `AnotherRunwayClaimsPoint` is unchanged. Membership ends at the extent, on purpose: a point beyond a runway's last pavement end or start row is off the runway, where the old endpoint-clamped test accepted up to a half-width past a start row. Detection at a start row itself is unchanged; the difference shows only past the physical end of a runway whose start row sits outboard of its pavement (now only within the 100 m the repair above allows) or at a mis-paired centerline (EDVQ), where takeoff assist falls back to its synthetic centerline. Do not add an along-runway margin to `ContainsAlongLateral` to "fix" this — the classifier reads the same test, and routes passing just beyond a runway end would start reading as entries.

**The Progressive "Cross at" list is separate.** `TaxiAssistForm.GetTaxiwaysCrossingRunway` builds the Progressive Taxi "After crossing runway" terminator's optional "Cross at taxiway" list from its own sign-change test against `RunwayFrame.For(runway, …)` within `along ∈ [-50 m, length + 50 m]`. It does not feed the per-row "Hold short of runway" picker, which lists runways, not taxiways.

**Runway-NAME association uses the centerline, not the threshold.** A hold-short node is named after its runway by `TaxiGraph.MatchHoldShortRunwayName(lat, lon, RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M = 150 m)` — the nearest runway *centerline* by clamped perpendicular distance, named after the nearer end on `RunwayShape` — which is length-invariant. The previous heuristic (nearest runway *threshold* within 500 m) mislabeled a hold-short where a taxiway crosses a *long* runway far from either threshold with the taxiway name — e.g. KBOS 15R on taxiway N announced "Hold short of N" instead of "runway 15R". The 150 m tolerance sits above a CAT III / code-F holding-position setback (~107 m) yet below major-airport parallel-runway spacing, so a node binds to its own runway; a clamped perpendicular distance also means a node beyond a runway end is not falsely matched. The threshold method remains only as a fallback for navdata with no reciprocal centerline pairs. The matched name is `"runway X at <holdPoint>"` (e.g. `runway 15R at N`), so the callout reads "Stop. Hold short of runway 15R at N." The automatic pass corrects a label naming a different pavement, but the source name still supplies the "at <holdPoint>" locative the callout keeps. Pure-geometry coverage: `tools/ProgressiveTaxiProbe`.

### Ground-speed announcer (configurable interval)

A configurable periodic ground-speed callout in `TaxiGuidanceManager.UpdatePosition`. Off by default; user picks 5 or 10 kt in the Taxi Guidance Options form (`TaxiGuidanceGroundSpeedAnnounceInterval` setting). When enabled, the screen reader announces the current speed rounded to the **nearest** multiple of the interval — 4 / 5 / 6 kt all read as `"5 knots"`, 9 / 10 / 11 read as `"10 knots"`. Implementation: `(int)Math.Round(gs / interval, AwayFromZero)`. The previous floor-bucket implementation (`(int)(gs / interval)`) flipped between "0" and "5" every time the raw value crossed 5.000, producing announcements that bore no resemblance to the actual speed at any given moment.

**Hysteresis**: once a bucket has been announced, the new bucket must be reached with a 0.5 kt margin past its rounding boundary before re-announcing. This kills jitter at the new midpoint (e.g., 7.5 kt with interval=5) — without it, a steady throttle near a boundary alternated `"5 knots"` / `"10 knots"` from frame to frame as raw GS jittered. With the margin, transitioning from 5 → 10 (going up) requires gs ≥ 8; 10 → 5 (going down) requires gs ≤ 7. The 1-kt-wide deadband (gs ∈ (7, 8)) preserves whichever announcement was last spoken.

**Source field**: the GS feed is `taxiData.GroundVelocityKnots` (real GROUND VELOCITY from SimConnect), NOT IndicatedAirspeedKnots. At low taxi speeds (under ~30 kt) IAS reads near zero — pitot pressure differential is below the indicator's working range — and substituting IAS for GS made the announcer say `"0 knots"` at 5-kt actual GS and `"10 knots"` at 15–20 kt. The `TakeoffAssistData` struct now carries both fields; takeoff assist still reads IAS for V-speed callouts (correct — V-speeds are airspeed-relative), the GS announcer reads GS.

First sample after route load establishes baseline silently (`_lastAnnouncedGsBucket = -1` initial, updated with no announcement). Goes through plain `_announcer.Announce` (NOT `AnnounceInstruction`) so a fading "10 knots" callout doesn't displace the most recent actionable instruction in the Repeat-Last buffer.

Active during normal taxiing, lineup, and the takeoff roll — complements the takeoff-assist 80/100/V1/rotate callouts (which use absolute-speed thresholds while this fires at every multiple). Useful for monitoring SOP taxi-speed caps (10 kt turns, 30 kt straight) without pressing the GS hotkey, and for blind pilots tracking acceleration on the takeoff roll.

### Speed-aware directives

Most action directives ("Slow down.", "Stop.") fire **unconditionally** alongside the distance callout — the pilot needs the action cue regardless of current speed, and a stopped-in-zone fallback fires "Stop." when the aircraft has parked between the slow-down and stop tiers. The one remaining speed-aware suffix:

- **Rollout runway-end at 500 ft → "Slow down."** suffix only added if `groundSpeedKts > ROLLOUT_TAXI_GS_KTS` (30 kt). Below taxi speed the directive is noise.

Hold-short (slow-down + stop tiers), parking-arrival ("10 feet. Stop."), and the rollout 100 ft "Stop" callout all fire their action suffix unconditionally — even when the aircraft is already at low / zero ground speed. The previous speed gating tended to leave blind pilots without confirmation of the required action; making the action suffix unconditional matches the safety-critical nature of those callouts. The base distance callout always fires (e.g. `"Hold short runway 13 Left in 150 feet."`) — only the rollout 500 ft "Slow down" remains conditional.

**The lineup tone itself is intentionally NOT speed-dependent.** The user's principle: alignment cues must work regardless of how slowly you're maneuvering. The runway-lineup tone (silent 0.5° / activation 1° / max-pan 15°) and the "Lined up" announcement fire purely on heading + cross-track error, never gated on ground speed. The pulse-mode cue (`SetPulse(true)` at ≤3 kt + ≥5° error) is an *extra* "you're stopped and stuck" signal, not a substitute for the always-on tone.

## Hotkeys

Hotkeys are identical across all supported aircraft.

### Output mode (press `]`)

| Key | Action |
|---|---|
| `Y` | Announce taxi status (current taxiway, next turn, distance to destination) |
| `Ctrl+Y` | Repeat current instruction |
| `Alt+Y` | Where Am I — announces current taxiway, gate, or runway at nearest airport (works any time). On `Alt+Y` rather than `Shift+Y` because `Shift+Y` in output mode is `HOTKEY_STATUS_DISPLAY`. |
| `Alt+L` | Look around — announces where you are, the apron/concourse you are in, then the nearest terminals, hangars, FBOs, tower, fuel and cargo with direction and distance. Ground-only; the whole DB/OSM/scenery lookup runs off the UI thread. |
| `Ctrl+Shift+L` | Surroundings window — opens a read-only list of everything within 1 km, nearest first, reusing `SayIntentionsInfoForm`. |

Both `L` chords are output-mode keys like every other row above: press `]` first, then the
chord. Three aircraft guides list the same chord for a WINDOW-LOCAL function (the FBW A380 MFD's
`Alt+L` = FUEL & LOAD, the iFly FMC's `Alt+L` = LEGS, the TFDi MD-11 MCDU's `Ctrl+Shift+L` =
switch to the left MCDU). Those are focus-scoped WinForms handlers and are unaffected without
`]`; while output mode is armed the global chord wins — the same precedence the already-shipped
`Alt+V` / `Alt+D` / `Alt+E` have.

### Input mode (press `[`)

| Key | Action |
|---|---|
| `Shift+Y` | Open Taxi Assist form |
| `Y` | Continue past current hold-short |
| `Ctrl+Y` | Stop taxi guidance |
| `Shift+X` | Open Landing Exit Planner (pre-select runway exit; auto-activates on touchdown) |

### Hotkey registration

See `MSFSBlindAssist/Hotkeys/HotkeyManager.cs`:
- `HOTKEY_TAXI_STATUS` (output `Y`), `HOTKEY_TAXI_REPEAT` (output `Ctrl+Y`), `HOTKEY_TAXI_WHERE_AM_I` (output `Alt+Y`)
- `HOTKEY_LOOK_AROUND` (output `Alt+L`, id 9219), `HOTKEY_SHOW_SURROUNDINGS` (output `Ctrl+Shift+L`, id 9220) — `HotkeyAction.LookAround` / `HotkeyAction.ShowSurroundings` in `MainForm.Hotkeys.cs`, routing to `AnnounceLookAround()` / `ShowSurroundingsWindow()` in `MainForm.Announcers.cs`
- `HOTKEY_TAXI_FORM` (input `Shift+Y`), `HOTKEY_TAXI_CONTINUE` (input `Y`), `HOTKEY_TAXI_STOP` (input `Ctrl+Y`)
- `HOTKEY_LANDING_EXIT` (input `Shift+X`)

### Where Am I implementation

`HotkeyAction.TaxiWhereAmI` routes to `MainForm.AnnounceWhereAmI()`, which:

1. **Air/ground gate.** Reads the cached `MainForm._lastOnGround` (kept fresh by the `SIM_ON_GROUND` event handler). If airborne, announces `"In flight."` and returns — Where Am I is ground-only by design (the LocationInfo hotkey covers airborne city/terrain queries).
2. Fetches the aircraft position asynchronously.
3. Resolves the airport the aircraft is AT with `CurrentAirport.Resolve` (see "Which airport — `CurrentAirport.Resolve`" below); no airport answers *"No airport nearby."* Idents of any length — every provider lookup matches `icao` OR `ident`. `GetNearbyAirportICAOs` still returns `COALESCE(NULLIF(icao, ''), ident)` for `GateResolver`'s TCAS lookup, which needs 3-char idents — never push a length filter into that SQL.
4. Calls `TaxiGuidanceManager.DescribeCurrentLocation(provider, icao, lat, lon, databaseGeneration)`, the generation read WITH the provider. The manager reuses the active guidance graph when the ICAO matches, otherwise builds and caches a dedicated query graph in `_whereAmICachedGraph` (invalidated via `ClearWhereAmICache()`, which a database switch calls) — but a graph built through a provider captured before a switch still answers the call and is NOT cached (`StoreWhereAmIGraph`), or an Alt+L in flight across the switch would put the previous database's graph straight back.

The actual classification happens in `TaxiGraph.DescribeLocation(lat, lon)`:
1. **Parking node** within 40 m, in every direction away from runways — near one (on runway
   pavement, a "near a runway start" answer in reach, or a hold-short node within 40 m), only a
   stand ALSO inside today's node-hash ring may answer here (see "The node answers' reach" below)
   → `Gate X`.
2. **Runway edge** (PathType starts with `R`) within half-width + 5 m perpendicular → `Runway X`. *(Effectively dead code in current navdatareader DBs — no `taxi_path` row has type R; the centerline scan below covers this case.)*
3. **Runway centerline scan** — for each `TaxiGraph.RunwayCenterline`, the runway shape (`RunwayShape`: the pavement ends and real half-width when usable, else the start rows) within half-width + 5 m and inside its extent → `Runway X` for the nearer end. Pairs are built in `TaxiGraph.Build` from opposing-end start rows (reciprocal designator first, reciprocal heading second, 200–6000 m apart). **This is what makes "Runway 27L" work mid-runway and on a displaced threshold**, not just within 50 m of the threshold node.
4. **Runway threshold node** (ParkingName `Runway …`) within 50 m **and inside the old node-hash ring** (±33 m north-south, ±33·cos(latitude) m east-west — see "The node answers' reach" below) → that name. Catches edge cases where a runway has unpaired start positions.
5. **Taxiway edge** within half-width + 3 m perpendicular → `Taxiway X`.
6. **Nearest node that has a taxiway name**, within 60 m in every direction → `Near taxiway X`.

Distances use equirectangular projection (sub-cm accuracy at taxi scale); the edge scan clamps to segment endpoints, the runway scan tests the runway's extent.

**The node answers' reach.** Steps 1, 4 and 6 read nodes, and each has the reach the owner ruled
for it on the PR #230 review, narrowed once more for the stand on 2026-09-23 (fix round 1, next
bullet):

- **The stand (step 1): true metres, in every direction, at every latitude — AWAY FROM RUNWAYS.**
  Candidates come from the same cell index as the edges — `NodesNear`: every node filed under its
  ~111 m cell, gathered on a ring sized separately in latitude and longitude. They used to come
  from a fixed ±30-cell ring of the 1.1 m node hash, commented "~= 330 m" but really ±33 m
  north-south and ±33·cos(latitude) m east-west: ±20 m at 52°N, ±7 m at ENSB (78°N). At 52°N a
  stand more than about 20 m east or west of the aircraft was never a candidate, so Where-Am-I
  named the taxiway, or nothing, instead (the review measured the gate lost at 17-53 % of positions
  25-35 m east or west of a stand). A point up to 40 m from a stand in any direction — on an apron
  taxilane beside it, say — now names the gate; north and south, the old ring already reached 33 m.
- **Near a runway, the stand keeps exactly the old ring instead (owner decision 2026-09-23, GC-5
  fix round 1).** The first version of this fix let the wider reach above win over a runway answer
  wherever the two now coincided — measured against real fs2024 navdata, about 2,255 hold-short
  nodes at 2,036 airports read a stand instead of "Runway X" (e.g. 00AN's hold for 03 said "Runway
  03", then said "Gate 1"), and runway pavement itself did the same at >= 1,660 more (e.g. 02C's
  runway said "Parking 13") — breaking the original ruling's promise that "nothing said at a hold
  line changes". So a stand found ONLY outside today's ring may answer ONLY away from a runway.
  "Near a runway" is (a) on runway pavement, by the same `RunwayShape` test step 2 uses; (b) a
  "near a runway start" answer in reach (step 4, below); or (c) a hold-short node within the stand
  radius — from the navdata endpoint types `TaxiGraph.Build` records
  (`TaxiGraph._navdataHoldShortNodeIds`), never `TaxiNode.Type`, which the parking pass can
  overwrite to Parking for a node within 100 m of a stand — exactly the nodes this predicate most
  needs to catch. Near a runway, a stand INSIDE today's ring may still answer (the older quirk,
  unchanged — this gate only ever NARROWS which stand is eligible, never widens it); if none
  qualifies there, the next answers apply exactly as before this whole PR.
  `TaxiGraphLocationRadiusTests` pins all three predicates.
- **The fallback (step 6): true metres, in every direction, everywhere — this decision does not
  touch it.** It takes the nearest node that HAS a taxiway name. It used to take the nearest node
  of any kind and answer only if that one was named, so a nearer unnamed node (a stand lead-in
  junction, an unnamed apron connector) silenced it.
- **"Near a runway start" (step 4) keeps EXACTLY the old ring.** `Build` names the node nearest
  each runway start row "Runway X" — usually the entry taxiway's junction, at a small field the
  hold line — and step 4 outranks the taxiway you are on (step 5), so a 50 m reach in every
  direction would say "Runway 09" instead of "Taxiway A" 20-50 m east or west of such a node: a
  change to what is said at a hold line that nobody measured. `RunwayStartReach` (beside
  `GetSpatialHashKey`) replicates the old ring's key arithmetic — the same sums, the same rounding,
  keys compared bit for bit as their strings compared, so "0" and "-0" stay two buckets — and
  filters `NodesNear`'s candidates, whose walk always contains the whole old ring, so the
  runway-start nodes it can name are exactly the old ring's. `TaxiGraphLocationRadiusTests` pins it
  against a verbatim copy of that ring. Do not widen it without asking the owner again.

The index files EVERY node, not only edge endpoints: measured against fs2024, 2 of 2,344,910 graph
nodes carry no edge (LGMG, and one on KSQL's taxiway F) and neither is a stand or a runway start,
but completeness does not rest on that count.

**Threading.** `DescribeLocation` changes nothing a caller can see, but it is not free of shared
state. `Alt+L`'s surroundings lookup runs `DescribeCurrentLocation` on a thread-pool thread, and the
manager hands it the ACTIVE guidance graph when the airport matches — the very instance
`TaxiAssistForm` passed to `LoadRoute` as `prebuiltGraph`, which the form keeps SUBDIVIDING on the UI
thread whenever it projects a painted holding point onto a taxi edge
(`NamedHoldingPointResolver.SnapOrInsert` → `TaxiGraph.InsertHoldingPointNodeOnEdge` → `SplitEdgeAt`;
reached from the holding-point picker, the default-holding-point call-out and the Progressive Taxi
named-holding-point list). Unserialised, the lookup enumerated the adjacency lists while a split
added to them — "Collection was modified", spoken as "Surroundings lookup failed." — or measured
against an edge already removed and not yet replaced. The graph serialises the two with its own lock
(`TaxiGraph._structureLock`): `DescribeLocation` holds it for its whole run, including the lazy build
of its index; `InsertHoldingPointNodeOnEdge` for its whole scan-then-split, and `SplitEdgeAt` takes it
again (re-entrant). UI-thread readers — routing, guidance, the form's own lookups — do not take it,
because the only post-`Build` mutation runs on the UI thread too. A new query reachable from a pool
thread, or a new post-`Build` mutation, must take it — inside `TaxiGraph`, since the lock is private.
The cost: a holding-point pick on the UI thread can wait for one in-flight `Alt+L` query to finish.

### Why `DescribeLocation` finds edges in a cell index, not through nearby nodes

Candidate edges come from `TaxiGraph`'s own edge cell index
(`EnsureCellIndex`/`EdgesNear`), never from the nodes within
`EDGE_SCAN_RADIUS_M`. Gathering them node-first and then skipping any edge whose
from-node was further than that radius gave **every segment longer than 2 x 120 m
a DEAD MIDDLE**: the aircraft stands on the centreline of a named taxiway, both
endpoints are out of range, the edge is never examined, and the method returns
`""` — which `DescribeCurrentLocation` renders as "Not on a known taxiway or ramp
at &lt;ICAO&gt;." for **both `Alt+Y` and `Alt+L`**.

Reported live at EHAM on taxiway Delta 2026-09-22. Replaying the pilot's own
recorded 31 Hz track through the production graph reproduced it exactly: at
52.318294, 4.741688 the aircraft was **1.7 m from the centreline** of
`taxi_path` 998795 — named "D", 98 ft wide — whose endpoints were 172 m and
166 m away. Five such stretches totalled 344 m of a 5,589 m taxi (6.1 %).

Swept over the whole fs2024 database: **20,357 of 2,515,711 segments exceed
240 m, 17,364 of them NAMED, totalling 3,788 km of centreline across 5,610
airports** — worst case ZSPD taxiway S2, a 3,205 m segment with 2,965 m blind.

The index is keyed on each segment's own footprint, so an edge's LENGTH no
longer decides whether it can be found — only its distance from the aircraft,
which the perpendicular test was always meant to be the sole arbiter of. It is
deliberately COARSE (`EDGE_CELL_PRECISION` 3, ~111 m cells) because an edge is
indexed under every cell it crosses: at the node hash's 1.1 m precision one
340 m taxiway would take ~300 entries. As a side effect the scan became a 7x7
ring (49 lookups) instead of the 219x357 (78,183) the old node ring walked at
EHAM's latitude.

The index is built by the first query that needs it and DROPPED — never
recounted — wherever `TaxiGraph`'s own code changes the structure
(`InvalidateCellIndex`, from `AddEdge`, `SplitEdgeAt` and `ResolveNode`'s new-node branch); the next query
rebuilds it. After `Build` the only such change is the painted holding-point
projection (`InsertHoldingPointNodeOnEdge`); routing never splits an edge.
`Nodes` and `Adjacency` are public, and hand-built test graphs and the two
standalone probes (`tools/ProgressiveTaxiProbe`, `tools/StandBridgeSweep`)
write them directly; that drops nothing, and is safe only because none of them
then asks `DescribeLocation` anything. The index used to recount every
adjacency list on every query to notice a change only a mutation can make.

Measured before/after over 600 randomly sampled airports, 70,266 segment
midpoints:

| | before | after |
|---|---|---|
| long (>240 m) names its own taxiway | 2 (0.13 %) | **1,472 (98.99 %)** |
| long returns NOTHING | **1,465 (98.52 %)** | **0 (0.00 %)** |
| short (control) names its own taxiway | 47,478 (69.03 %) | 47,478 (69.03 %) |
| short (control) returns nothing | 1 | 1 |

The control is identical to the digit — the change touches exactly the
population it targets. The 15 long segments that name something else are correct
precedence (a midpoint on runway pavement, or inside `PARKING_RADIUS_M` of a
gate), not failures.

Those figures predate the reach change described under "Where Am I implementation" (PR #230
review, GC-5): a midpoint up to 40 m east or west of a stand now names the gate, where at 52°N one
more than about 20 m to the side never could, and the fallback now answers where a nearer unnamed
node used to silence it. Both are correct precedence, but they move some control cases, so
re-measure before quoting either control row as current.

### One name for a stand — where "Gate X" in that readout comes from

Step 1 above ("Parking node within 40 m → `Gate X`") reads `TaxiNode.ParkingName`, which `TaxiGraph.Build`'s parking pass writes from whatever `List<ParkingSpot>` it was handed. **Every graph build in the app now takes that list from `Services/ParkingSpotSource.GetNamedSpots(dataProvider, gateSource, icao)`**: navdata's own parking list with the concourse letter **corrected in place** from the authoritative gate list (`GsxStandNameOverlay`), then `AugmentingAirportDataProvider.AugmentParking` re-run so this scenery's online aliases resolve against the corrected identity. So a stand is called the same thing in the taxi dialog's destination combo, the gate-teleport list, `gate.select`, Where-Am-I and SayIntentions' "are you at your assigned gate" check.

Before that seam, only the dialogs went through `GateDataSource.GetGates`; everything else called `IAirportDataProvider.GetParkingSpots` and got navdata's name. At KJFK Terminal 4 those disagree — GSX says **B 25** (what a controller and SayIntentions say), navdata says **A 25** — and one consumer was not cosmetic: an aircraft parked exactly at B25 was told *"Aircraft appears near A 25, not assigned gate Terminal 4 Gate B25."* See [gsx.md](gsx.md) for the measurement behind the corrected letter.

Four properties worth knowing before touching this:

- **The list handed to `Build` must be navdata's own SET — same spots, same count, same coordinates, same order.** Correcting names in place rather than swapping in the GSX list is not stylistic. The parking pass writes `node.Type = TaxiNodeType.Parking` as well as the name, and unlike `ParkingName` — read only by `DescribeLocation` — `Type` is read by `NamedHoldingPointResolver` (which **skips** parking nodes when snapping a named holding point, a Progressive-Taxi terminator target), by `HoldShortNodeResolver`, and by the route truncation in `TaxiGuidanceManager.Routing`. A different set of spots marks a different set of nodes, so **a hold-short could move** — and that resolver's snap radii are probe-pinned against real navdata and live OSM at six airports precisely so they are not re-tuned by accident. The GSX list is also *smaller*: it excludes Vehicle/Fuel stands and drops stands with no usable heading (230 of KJFK's 231 survive `GateDataSource`), so swapping it in would strip the Where-Am-I label off stands that have one today. **Naming a stand and deciding which nodes are parking are different jobs, and only the first may change.**
- **`GetNamedSpots` vs `GetSelectableGates`.** The seam has two shapes on purpose. Anything that must *act* on a stand — the destination combo, the gate-teleport list, `gate.select` — needs `GetSelectableGates`, i.e. GSX's own list, because it carries `GsxIdentifier`, the docking stop position, the max wingspan for the fit filter and `TerminalName` for disambiguating identically-named stands. Anything that merely *names* a stand uses `GetNamedSpots`. Both derive the name from the same authority, which is what makes them agree.
- **The four graph-build sites are `TaxiGuidanceManager.DescribeCurrentLocation` / `TryDetectRunwayUnderAircraft` / `LoadRoute` (its no-prebuilt-graph branch) and the two forms (`TaxiAssistForm`, `LandingExitForm`).** The manager's three go through the injectable `TaxiGuidanceManager.ParkingSpotSupplier`, which **defaults to `dataProvider.GetParkingSpots`** when unwired — that default is what keeps the xUnit suite and any non-MainForm caller byte-identical to the pre-seam behaviour. `LandingExitForm` is in scope despite never speaking a stand name: the graph it builds is handed to `LandingExitPlanner.SetExit`, passed to `LoadRoute` as `prebuiltGraph`, becomes `TaxiGuidanceManager._graph`, and `DescribeCurrentLocation` **prefers** that graph — so it supplies the Where-Am-I stand names for the whole rollout and taxi-in.
- **It runs off the UI thread.** Where-Am-I and the takeoff-assist runway probe both reach their graph builds from inside a `RequestAircraftPositionAsync` callback, so MainForm's supplier builds a fresh `GateDataSource` per call rather than sharing one with the UI thread (its per-ICAO caches are plain `Dictionary`). That is affordable only because every call site is a graph build — once per airport, then cached. **Never put the supplier on a position update.**

## Airport surroundings (Look around, the Surroundings window, passing callouts, Place destinations)

Where Am I answers "what is under the aircraft." This feature answers "what is
AROUND it" — the terminal, hangar, FBO, tower, fuel and cargo a sighted pilot
sees on the ramp but a blind pilot has no way to ask about beyond "Taxiway A".
It is built entirely from data the app already had access to and never read,
plus the installed scenery package's own placement data.

### Why (measured coverage, 2026-09-06)

- **Navdata has no building table at all** — terminal/hangar/FBO cannot come
  from navdatareader directly. It DOES carry `parking.type` (fuel, vehicles,
  cargo, GA sizes), `parking.airline_codes` (set on ~1,700 gates), the
  concourse letter on every gate, `has_avgas`/`has_jetfuel`, `helipad`, `apron`
  polygons and `com` — none of it read anywhere in the app before this.
  `airport.tower_lonx/laty` **depends on the simulator the database was built
  from**: NULL on an MSFS 2020 build, but populated for 1,952 of 84,278
  airports on an MSFS 2024 one (measured 2026-09-21). So the tower comes from
  navdata when navdata has it, and from OSM or the scenery scan otherwise —
  which is why `AirportFacilities.TowerLat/TowerLon` are nullable. (An earlier
  version of this document said the column was NULL on every row and the tower
  therefore never came from navdata. That was true of the MSFS 2020 build it
  was measured on and is false on an MSFS 2024 one.) **A position is not a
  tower**: 319 of those 1,952 airports carry `has_tower_object` 0 and no tower
  frequency — their tower position is only the tower-VIEW camera point (KAST's
  sits 319 ft above a 7 ft field, KVUO's 43 ft above a 20 ft one), and read as a
  building it put a phantom "Control tower" in Look around and the passing
  callouts at 319 fields with no tower. The navdata tower is therefore taken
  only where `has_tower_object` is 1 (`AirportFacilities.HasTowerObject`:
  1,633 airports, every one with a tower frequency; measured 2026-09-22). The
  column is `INTEGER NOT NULL` in navdatareader's own schema, which both the
  MSFS 2024 SimConnect build and the MSFS 2020 disk build write; a NULL still
  reads as "no tower". An airport table WITHOUT the column cannot be read by
  the facilities query at all (SQLite refuses a query naming a column the
  table lacks) — exactly as one without `tower_laty`/`tower_lonx` never could,
  so no database that works today breaks.
- **OSM** at KATL names all seven concourses, the North/South/Domestic
  terminals, FedEx/UPS cargo, 15 named aprons, the fire station, the tower and
  201 gates. At KJAC it names the General Aviation Terminal with its FBO
  operator, the Teton Interagency Helibase, the Commercial Ramp and a
  "De-icing pad" apron. At KTIW it names the control tower and **none** of its
  20 hangars — a plain radius query there also catches a Chevron gas station
  on the road outside the airport, which is why the query is area-scoped.
- **Scenery**: the Orbx KTIW package's placement BGL parses to 1,269
  placements, 1,012 resolving to in-package model names — 36 distinct named
  objects (`KTIW_Narrows_Aviation_Hangar_Large`, `KTIW_Cessna_Service_Hanger`,
  `Control_Tower_1`, `Fueltank`, …). imaginesim KATL: 5,324 placements, 2,698
  in-package (concourses A–F, `northwestern_cargo_01`, `tower_01`). Axonos
  KJAC: `KJAC_Hangar_1/2`, `KJAC_Tower`. A one-time index of Asobo's base
  library (to resolve the roughly-half of KATL/KJAC placements that reference
  it by GUID) was built and measured as a spike — see "Rejected: base-library
  index" below.

### Four tiers, and how they rank

`MainForm.BuildSurroundings(icao)` is the cache's `BuildSupplier`. It STARTS
the OSM fetch first (`OnlineFeatureStore.Prefetch`), reads navdata, then GSX,
then the scenery package, and only then collects OSM, waiting for whatever is
left of `OnlineFeatureStore.CatalogWait` (3 s from the prefetch) — so a
first-time scenery scan and a slow mirror overlap instead of adding up, and an
answer that landed during the scan is simply taken. It concatenates them in the
order navdata, GSX, OSM, scenery — the order the merge has always seen, which
matters because its rank sort is stable — and `AirportFeatureCatalog.Build`
merges the result (next section) — so the order below is the RANK order the
merge applies, not the read order.

| Source | What it uniquely contributes |
|---|---|
| OSM (`OsmFeatureClassifier`) | Named terminals/concourses, FBOs with operator, named aprons/de-ice pads, hangars, fire station, tower, cargo |
| Scenery (`SceneryModelNameClassifier`) | What THIS scenery actually models, including hangars OSM leaves unnamed |
| GSX terminals (`GsxTerminalFeatureSource`) | Terminal/concourse names from GSX's own selectable gate list, right where navdata's letter grouping is wrong (measured at KJFK) |
| Navdata (`NavdataFeatureSource`) | Works at every airport with no network and no add-on scenery |

`AirportFeatureCatalog.Rank` weighs the **name first** — a proper name (100)
beats a synthesized label like "Fuel" or "GA ramp" (50) beats none (0) — and
only then the source (OSM 40, scenery 30, GSX 20, navdata 10). So a named
scenery hangar outranks an unnamed OSM one. An unnamed feature is still kept
when its kind is self-describing (a hangar is "a hangar"); an unnamed `Other`
is dropped.

**Navdata is the required base — every other tier is optional and isolated.**
The airport box and the "airport facts" line come from navdata, so its failure
really is the build's. The GSX, OSM and scenery tiers are each read through
`SurroundingsTier.Read(tier, icao, …)`, which turns a throw into one `Log.Warn`
and an empty list. Read in a straight line, one corrupted `census.json` row or
one odd mirror reply failed the WHOLE catalog build — navdata stands and GSX
places included — and the cache then retried that failing build every 60 s for
as long as the airport was current.

- **`NavdataFeatureSource`** infers concourses from gate letters (via
  `MapParkingName`) with a majority-airline `Detail` when ≥ 60 % of the coded
  gates in a group share one airline; groups fuel, CIVIL cargo (type 6) and
  GA-ramp `parking` rows into `Fuel`/`Cargo`/`Apron` features — a military
  cargo stand (type 7) is `ParkingTypes.IsMilitary` and never part of a "Cargo
  ramp"; reads helipads and — where the database
  has a tower OBJECT (`has_tower_object`), never a bare tower position — the
  tower coordinates; and reads `AirportFacilities` (avgas/jet
  flags, `com` frequencies, bounding box, `scenery_local_path`) for the
  window's "airport facts" row. Every group is clustered **in space**
  (`SurroundingsGeometry.SingleLinkage` — `GateLinkMetres` 200 m for gate
  letters and directional ramps, `RampLinkMetres` 80 m for fuel, cargo and GA
  ramps), never one centroid per name: a name-wide centroid put LLBG's "North
  ramp" 904 m from its nearest stand and spread 186 of 314 inferred concourses
  over more than 300 m.
- **`OsmFeatureClassifier`** is deliberately STRICT: a named building is a
  feature only when its own name says aviation (the `FeatureLexicon` FBO or
  cargo vocabulary, and not a landside word like "car park" or "hotel"). The
  earlier "any named building is an office" rule turned EGLL's car parks, bus
  station and escape shafts — and 174 numbered buildings at EDDF — into spoken,
  routable places. Road fuel (`amenity=fuel`) is not aircraft fuel and is not
  matched at all; `ref` stands in for a missing name only on an apron or a
  terminal, and only when it carries a letter and is not a `;`-separated list.
  The name it reads — for speech AND for the kind — is `name:en` when OSM
  carries one, else `name`: OSM's `name` is the local script, so Haneda's
  terminals were spoken as 第1旅客ターミナル and Narita's cargo sheds (第3貨物ビル,
  `name:en` "Cargo Building No.3") were no feature at all, their only aviation
  word being in the English name. Never classify on one and speak the other.
- **`OsmFeatureSource`** owns the query. It is scoped to the
  `aeroway=aerodrome` area carrying the airport's `icao=` tag; when OSM has not
  tagged that area, one `around:3000` fallback runs and its result is kept only
  inside the navdata airport box grown `FallbackBoxMarginMetres` (500 m). A
  bare radius is what let the KTIW Chevron in, and with **no** box the fallback
  result is dropped entirely — fail closed. OSM feature data has its own
  in-memory store (`OnlineFeatureStore`), no disk cache, same ODbL "produced
  work" position as the rest of this pipeline. See "The OSM buildings query"
  below for why it must never be fused back into the taxiway-name query.
- **`SceneryPackageLocator`** opens only the folders
  `airport.scenery_local_path` names — never the whole Community tree — and
  `SceneryPackageCensus` finds the package by where its objects stand when
  navdata names none. `BglPlacementReader` (pure, byte-level BGL parsing, by
  seeking) plus `ModelLibNameReader` (a streamed byte search for `<ModelInfo …
  guid="…" name="…">`) hand named placements to `SceneryModelNameClassifier`,
  which tokenises the raw model name, strips a leading ICAO and vendor token,
  classifies on keywords, drops a stop-list of non-building tokens (fences,
  lights, vehicles, jetways, containers, dollies…), folds part numbers and
  collapses the parts of one multi-part building (`concourse_a_01..03`) onto
  one name. **A raw model name (`KTIW_*`, `concourse_a_02`) never reaches
  speech** — only the classifier's human-text output does. The scan is LAZY: it
  runs on the first catalog build for an airport, on a thread-pool thread,
  never on the UI thread or a position update. See "The scenery tier" below.
- **`GsxTerminalFeatureSource`** groups `ParkingSpotSource.GetSelectableGates`
  entries by their `TerminalName` into features — never from the graph. A bare
  category header ("Parking", "Ramp", "Gates", "Stand"…) is a profile author's
  section divider, not a place, and is skipped; a group of one is skipped too.
  The **kind** is derived from the header text AND the grouped stands' own
  parking types (`KindOf`: cargo words or a 60 % civil-cargo-stand majority →
  Cargo, FBO words → Fbo, concourse words → Concourse, a 60 % majority of
  GA-ramp or military stands → Apron, else Terminal), because the header is
  free text and a cargo ramp typed `Terminal` took the one Terminal slot in the
  Look-around sentence. A military ramp counts as ramp stands for the same
  reason: once a military cargo stand stopped counting as cargo, a section of
  them would otherwise have fallen through to Terminal.

### Merge — `AirportFeatureCatalog.SameFeature`

**Identity needs the NAME and the DISTANCE together.** Features are walked
highest-`Rank` first, so the first one standing in a cluster is the winner, and
the loser's footprint, detail or member list is folded into it when the winner
lacks one *and the geometry describes the winner* (see below).

| Case | Rule |
|---|---|
| Different `FeatureKind` | Never the same feature. |
| Shapes that cannot be one body | Never the same feature — `GeometryMayBeOneBody`, asked ahead of every row below. |
| `Tower` | Distance only, within the merge radius — one airport, one tower ("Control Tower" / "Control Tower 1"). |
| Both carry a proper name | Same name AND within `SameNameRadiusMetres`. |
| One carries a proper name | Within `MergeRadiusMetres` — a real name absorbs a synthesized or missing one. |
| Neither | Within `MergeRadiusMetres`, and the names must match unless one is missing ("Helipad 1" is not "Helipad 2"). |

`MergeRadiusMetres`: Tower 100 m, Terminal/Concourse 150 m, Hangar 40 m,
Fuel 60 m, others 50 m. `SameNameRadiusMetres` doubles that, except
Terminal/Concourse, which take 300 m — a pier is long, but KJFK's two
"Concourse B" piers are 1.3 km apart and must stay two. Distance between two
features honours each one's own footprint/member geometry in both directions,
and the smaller wins.

Distance alone (the rule this replaced) dropped KMSP's Concourse B, 109 m from
A, and 7,236 numbered helipads. Name alone (the old "a Concourse matches
another Concourse by letter at any distance" rule) merged the KJFK pair.

**Geometry is only donated where it DESCRIBES the winner.** Every row above
decides identity from a name and a distance between representative points,
which says nothing about whether one outline really is the other — and a merge
hands the winner the loser's geometry, which `SurroundingsGeometry.Nearest`
then measures to. Four rules, all of them paid for at KTIW:

- A winner that already has `Members` **never adopts a `Footprint`**. Its own
  stands are its geometry, and `Nearest` reads a footprint FIRST, so one
  adopted ring silently replaces them.
- An **unnamed ring and a stand cluster of kind `Apron`/`DeicePad` are
  different features** and never merge. The ring is pavement, the cluster is
  the stands parked on some pavement, and one ring routinely covers several
  rows. Kept apart, the ring stays a polygon `SurroundingsReport.Zone` can put
  the aircraft inside and the cluster stays "GA ramp", measured to its stands.
- **Ring versus ring with neither proper-named** is one body only when one
  CONTAINS the other's representative point — never on a bare radius between
  edges. A shared PROPER name is different evidence and still merges two halves
  of one split OSM way.
- A **stand cluster the other feature does not describe** — some member further
  from it than `SameNameRadiusMetres` (`MembersDescribe`, measured through
  `Nearest`, never centroid to centroid) — is not that feature at all. Refusing
  the donation is not enough, because the merge would still consume the cluster:
  KMEM's cargo rows run 686 m, and a proper-named building 30 m from ONE end
  absorbed the whole row on the strength of that one stand, so a pilot at the far
  end — 600 m away — was left with no cargo area near them. The good case is
  untouched: a cluster whose every member really is within reach still merges
  into ONE feature carrying the proper name and taking the stands as its
  geometry, and the back-fill adopts those stands with no further test, because
  a pair that did not pass this rule never merged at all.

The FIRST rule lives in `Build`'s back-fill, which is where a winner decides
what it may KEEP. The other THREE live in `SameFeature`, through
`GeometryMayBeOneBody`, which is asked LAST — of the few pairs the name and the
distance have already accepted, because it can walk a whole stand cluster
against a ring. Both halves are symmetric, so
`SameFeature(a, b) == SameFeature(b, a)`.

**One accepted residual of the cluster rule: two survivors can now share a
PROPER name.** A navdata `Concourse B` is letter-chained at
`NavdataFeatureSource.GateLinkMetres` (200 m), so it can run well past one pier,
while an OSM `Concourse B` ring may cover only part of it — a member then lies
beyond `SameNameRadiusMetres` of the ring, `GeometryMayBeOneBody` refuses, and
BOTH survive under the same name. The same shape exists for a GSX terminal
header (`GsxTerminalFeatureSource`) that groups remote stands with a pier's.
They merged before. The cost is two identical names in the Ctrl+Shift+L list
and, since `PassingCalloutGate` identity is kind + name + POSITION, possibly two
"Passing Concourse B" callouts on one taxi. Both features are real and both
statements are true, so this is a residual and not a defect — merging them anyway
was rejected because the merged feature would then report ITSELF 0 m from a
stand hundreds of metres from the building, which is exactly the KMEM failure
the rule exists to stop.

Measured at KTIW (11 stands, the 4 unnamed aprons of
`Fixtures/osm-features-area-ktiw.json`): the 6-stand "GA ramp" adopted the
66,471 m² main apron — which contains the OTHER row's 5 stands too — and the
5-stand ramp adopted a 2,335 m² neighbour containing none of its own. Parked on
the southern row, a pilot heard "On the GA ramp." and then the ramp they were
standing on named as somewhere else: P2 74 m to the right, P4 38 m, P6 12 m
ahead, P8 47 m, P10 85 m. Two of those four aprons sit 26.4 m apart and a third
27.9 m from one of them, so with OSM alone the element ORDER decided which
polygon survived — and dropping the 66,471 m² one takes the zone with it. The
mirror case is a proper-named point 30 m from ONE stand of a row that runs
686 m (KMEM cargo), 1,016 m (KLNK GA) or 1,296 m (KSNA GA): inherited whole, it
reports itself 0 m from the far end of the row.

**The supersede pass.** A navdata concourse is a GUESS from the BGL gate-name
enum. When a GSX feature is built from at least half the same stands (within
15 m each), the navdata one is REMOVED outright rather than merged — the two
differ in both kind and name, so `SameFeature` can never reconcile them, and
GSX is the one to believe (measured at KJFK; see [gsx.md](gsx.md)). The GSX
clusters are snapshot before the `RemoveAll`, which compacts the very list its
predicate would otherwise be querying.

**Distance and bearing are always taken to the same point**
(`SurroundingsGeometry.Nearest`): the nearest edge of a footprint, else the
nearest member stand, else the representative point. Pairing
distance-to-the-wall with bearing-to-the-roof-centroid read a pier 60 m to the
left as "ahead, 60 metres". Inside a footprint the distance is 0 and the
bearing is to the centroid.

### The catalog cache — `SurroundingsCatalogCache`

One `AirportFeatureCatalog` per ICAO, plus the airport's facts line, so a
consumer reads it off the catalog instead of a second database lookup.
Staleness is the same shape as the Where-Am-I graph cache: a version token
(`GateDataSource.GetGateListVersion`'s, read through MainForm's
`GateListVersion` — the static `GateDataSource.ComputeGateListVersion` over the
same four GSX signals every `GateDataSource` is built with, because the
passing-callout monitor asks on every position sample it handles, about every
2 s, and a `GateDataSource` built per ask was two concurrent dictionaries and a
`GsxProfileLocator` thrown away) compared through `ShouldRebuildGateList`, plus
`Invalidate(icao)` — whose one production caller
is `OnlineFeatureStore.FeaturesUpdated`, i.e. an OSM answer that landed after
the build gave up on it — and `Clear()` from a database switch or a settings
change.

- **Async and single-flight.** `GetAsync` always hands the build to a
  thread-pool thread — a first-time scenery scan and DB read can make it slow —
  and a second caller for the same airport joins the build already running.
  Its callers (both hotkeys, the passing-callout monitor and the taxi dialog)
  each used to carry an in-flight guard of their own. `TryGetCached` is the
  non-building counterpart for a UI-thread caller that must not itself trigger
  that build.
- **Written back only by the airport's in-flight build.** A finished build is
  stored only if nothing invalidated that airport while it ran — which is
  exactly "it is still that airport's in-flight build": `Invalidate` and
  `Clear` both drop the in-flight entry, and a replacement build is a different
  task, so ONE identity test decides both whether to clear the entry and
  whether to write anything back (a per-ICAO generation counter used to keep
  the same fact a second time, and was never pruned). The old unconditional
  store lost every `Invalidate`/`Clear` that landed mid-build — an OSM-less
  catalog (the fetch gave up, the answer arrived moments later) or an
  old-database one was then served for the rest of the session. The awaiter
  still gets its result; it is simply not cached, so the next `GetAsync`
  rebuilds. A build overtaken by its replacement — even one that finishes
  AFTER the replacement was stored — never overwrites it.
- **Failure memory, under the same check.** A build that threw is remembered
  for `FailureMemory` (60 s) and answered from whatever was cached before, so a
  2 s poll cannot hammer a broken build. Only a build that is still the
  airport's in-flight build records its failure: a database switch pulls the
  provider out from under a running build, which is exactly what makes it
  throw, and remembering THAT failure would blank the airport for a minute on
  the new database.
- **Degraded lifetime.** A build that went WITHOUT an optional tier —
  `SurroundingsBuild.Degraded`, set when `SurroundingsTier.Read` caught an
  exception or when the OSM store answered `Pending`/`Failed` rather than
  `Served` — is fresh only for `DegradedLifetime`, which is
  `OnlineFeatureStore.FailureMemory` (5 minutes) PLUS
  `OnlineFeatureStore.FetchBudget` (60 s), both referenced rather than copied:
  the fetch a build gave up on runs on and can still FAIL up to `FetchBudget`
  later, and the store remembers that failure for `FailureMemory` from THEN, so
  rebuilding any sooner only re-reads a failure the store is still remembering.
  With `FailureMemory` alone it did exactly that — the rebuild came back
  degraded again and the mirror was really asked again only after about ten
  minutes, not five. Everything else here is invalidated by an EVENT, and the one
  event that would cover this, `FeaturesUpdated`, is raised only when a late
  fetch SUCCEEDS — so without the lifetime a tier-less catalog simply became
  the catalog for the session, and nothing asked the store again once its own
  failure memory ran out. Degraded is never inferred from an EMPTY list: an
  airport can legitimately have no mapped buildings. While the rebuild runs,
  `TryGetCached` reports a miss, which costs the monitor a poll or two — the
  same as a first build — and `GetAsync` AWAITS the rebuild rather than serving
  the expired entry; only a rebuild that FAILS falls back to the stored one (a
  stale catalog beats none).
- **Say which happened.** The one debug line a build writes ends `stored`,
  `discarded (invalidated mid-build)` or `discarded (cache cleared mid-build)`,
  plus `, degraded` when it is (`SurroundingsCatalogCache.DescribeOutcome`,
  pinned by a test). A discarded build must never read as though it had been
  cached — that line is how "the OSM buildings never appear" gets diagnosed.
  The epoch `Clear()` bumps is kept for this wording alone; whether a build is
  written back is decided by the in-flight check above.

### Which airport — `CurrentAirport.Resolve`

Everything that needs "which airport am I at" asks `CurrentAirport.Resolve`,
so all of it names the same field: Where Am I (`Alt+Y`), Look Around
(`Alt+L`), the Surroundings window (`Ctrl+Shift+L`), the passing-callout
monitor, the Taxi Assist form `Shift+Y` opens (and with it the Place list),
takeoff assist's under-aircraft runway detection, the Settings "Refresh Taxiway
Names" button, and the SayIntentions import when flight.json names no airport
the navigation database knows. The last four used `GetNearbyAirportICAOs(…)`
filtered to four characters until the PR #230 review and disagreed with Where
Am I at 19,700 of fs2024's 302,142 stands: at 111 of KSNA's the taxi form
opened heliport 10CL, which has no taxi data, and on KSNA's runway 02L takeoff
assist found no runway for the same reason. The answer is
`CurrentAirportResolver.Pick`. Among the
airports the provider lists within 5 NM it makes four passes, each taking the
nearest by TRUE distance to the reference point:

1. an airport **with taxi paths** whose navdata bounding box, grown 300 m
   (`BoxMarginMetres`), contains the aircraft;
2. an airport of **any kind** whose grown box contains it — a strip with
   runways but no taxi paths;
3. the nearest airport with taxi paths within 3 NM;
4. the nearest of any kind within 5 NM.

Idents of any length.

Pass 2 was missing from the first version, and its absence cost exactly the
small fields this was meant to serve: a strip with no taxi paths went to a
taxi-path neighbour, so Where Am I on the runway at 8TX2 Freeman Ranch said
"Not on a known taxiway or ramp at KECU." — an airport 4.4 km away. Measured on
fs2024: 1,552 strips (at least one runway, no taxi paths) that the old
4-character rule named at their own reference point were sent to another
airport, 1,354 of them to a taxi-path field within 3 NM, and so were 3,306
runway ends at 1,790 strips. With pass 2, 1,353 of those reference points and
2,917 of those runway ends name their own strip, and the answer at all 302,142
stands and at all 56,396 runway ends (`runway_end` rows) of airports with taxi
paths is unchanged.
Its place is load-bearing both ways: above pass 1 it would hand 41 of KSNA's
stands — inside heliport 10CL's grown box and nearer its reference point — to
the heliport; below pass 3 the neighbour would still win. What it leaves is a
strip inside a taxi-path airport's own grown box, which pass 1 answers on
purpose (190 reference points, median 103 m apart — mostly two navdata records
for one field, such as UZTT/UTTT), and 133 positions where two grown boxes
overlap and the other airport's reference point is nearer (81 of them a
heliport beside a strip's runway end).

It replaces `GetNearbyAirportICAOs(…)[0]` filtered to 4-character ICAOs, which
is ordered by unscaled |Δlat| + |Δlon| to the reference point. Measured on
fs2024: the old rule sent 2,371 stands at 212 airports to a neighbouring field
(111 of KSNA's 201 stands went to a heliport) and could never resolve the 2,454
fields with a 3-character ident. Replayed over every stand, 301,865 of 302,142
(99.91 %) now resolve to their own airport, against 93.4 % before; 270 of the
remaining 277 are duplicate airport records for one physical field. No
constants were tuned.

The legacy query survives as a fallback for **one** case: a provider that
supplied no candidates at all (the interface's default implementation, a test
double, genuinely nothing within 5 NM). It must never run as a second opinion
on candidates `Pick` considered and declined — it has no true-distance filter,
so it hands back exactly the corner-of-the-box airport 5–7 NM out that `Pick`
had just refused, chosen by the metric this exists to retire.

### Look around — output `]` then `Alt+L`

Ground-only, same `_lastOnGround` gate and "In flight." answer as Where Am I.
One utterance from `SurroundingsReport.Compose` — interrupting
(`AnnounceImmediate`) like Where Am I when it comes within
`SurroundingsLookupNotice.Delay` (1.5 s) of the press, QUEUED (`Announce`) when
it comes later (see the end of "Surroundings window" below):

```
{Where-Am-I line}. {Zone}. {Feature 1}, {direction}, {distance}. … (up to 4)
```

`{Zone}` is where the aircraft IS, in four rungs, best evidence first:

1. a **named** Apron/DeicePad footprint containing it ("On the Commercial
   Ramp.") — a name is what a pilot can act on;
2. else the Apron/DeicePad whose **stands** it is among, nearest member within
   `ZoneMemberMetres` ("On the GA ramp."). A navdata ramp has no outline at all,
   it IS its stands, so without this rung the ramp a pilot is parked on could
   only ever be reported as something nearby — and at KTIW the anonymous OSM
   polygon underneath took its place, so they heard "On the Apron." and then
   their own ramp named 0 m away;
3. else **any** containing footprint, named or not ("On the Apron.");
4. else the nearest Concourse/Terminal within `ZoneNearMetres` ("At Concourse
   B."); omitted when none of the four applies.

`ZoneMemberMetres` is 40 m — about one stand spacing, which is what an aircraft
in the lane between two rows is from the nearest of them. Measured on fs2024,
nearest-neighbour spacing between GA stands: KTIW median 14.0 m / p90 39.2 m,
KSNA (180 stands) 23.9 / 39.3, KLNK (319) 27.1 / 42.4, KJAC 13.8 / 23.4. It also
clears the stands themselves (KTIW's are 23 and 33 m in radius; the median GA
stand in the database is 23 m) and stays under Apron's 50 m merge radius, so it
can never reach further than the catalog would call one ramp. (Rungs 1 and 3
take the FIRST match in catalog order — sorted by kind then name — so with
overlapping aprons the winner is the first of that order, not the smallest;
nothing here measures area.)

Features are nearest-first within 600 m, at most one per kind except Hangar and
Fbo (a GA field is all hangars), capped at 4. Excluded: the zone itself, and any
other Apron/DeicePad the aircraft is **standing on** — after "On the GA ramp."
the pilot must not also hear "Apron, here" about the pavement under it.

**Under a GROUND zone, a second piece of ground has to add something of its
own** (`AddsNothingBesideGroundZone`). Two ways it does not: it is an **Apron
with no name at all**, which speaks as the bare kind word — "Apron, ahead,
12 metres" beside the ramp the aircraft is parked on IS that ramp's pavement, it
names nothing a pilot can act on, and it spends one of the four slots a building
should have; or it carries **the zone's own spoken name** — KTIW's second "GA
ramp" 590 m away is the one-name-two-places confusion.

Everything else beside a ramp still speaks, and the two exclusions are
deliberately narrow. Not "no PROPER name": `NavdataFeatureSource` marks "North
ramp"/"South ramp" and "GA ramp" `NameIsGeneric`, yet each names ONE ramp rather
than all of them and is what a controller calls that pavement. Not the KIND
either, in either direction — an unnamed **de-ice pad** speaks as "De-ice pad",
where the kind word is the whole information, while spending the zone's whole
kind silenced a real "North Apron" 300 m from an anonymous polygon the aircraft
sat in. And with NO ground zone — out on a taxiway, or under a
concourse/terminal zone — nothing has been said about the pavement, so even an
unnamed "Apron, ahead, 200 metres" is the readout doing its job and stays.

**Nothing at zero range gets a direction.** At or below `ZeroRangeMetres` a
feature reads "{name}, here." — the bearing to something the aircraft is
standing on or inside is degenerate, so the side it produces is arbitrary and a
blind pilot has nothing to check it against. The floor is sized from what the
reader would say: `DistanceFormatter` rounds metres under 100 to the nearest 5
(so under 2.5 m reads "0 metres") and feet under 200 to the nearest 25 (so under
12.5 ft reads "0 feet"), and the larger of the two — 12.5 ft, 3.81 m — is the
floor, so neither unit can produce a zero with a side attached to it. The window
takes the same wording; it lists the zone too, being an inventory rather than a
spoken "where am I".

Two or more hangars without a proper name in range — unnamed ones, and ones the
scenery named only by the kind word ("Hangar", `NameIsGeneric`) — collapse to
"Hangars, to the left, 80 metres.", through the same
`AirportFeature.HasProperName` test the passing-callout gate uses. Directions
come from `RelativeDirection.Describe` (see below);
distances from `DistanceFormatter` on the pilot's `GroundDistanceUnit`.
"No surroundings data for {icao}." when the catalog itself is empty; "Nothing
within 600 metres." when it has features but none in range.

`RelativeDirection` (`Services/RelativeDirection.cs`) is what used to be
`GroundTrafficMonitor`'s own `DescribeDirection`, lifted out into a shared
helper — so that name no longer exists to grep — giving one phrasing app-wide
on the same thresholds (20/70/110/160), pinned by a test so the ground-traffic
phrasing cannot drift out from under this feature.

### Surroundings window — output `]` then `Ctrl+Shift+L`

Reuses `SayIntentionsInfoForm` (the sectioned read-only ListBox window,
title parameter set to "Surroundings at {icao}") rather than a new form — the
same reasoning as the flight-information window: a list item brailles as a
discrete unit and announces its position, and item 0 is pre-selected so
tabbing in speaks the section and first row in one utterance. First row is
"Airport" facts (the fuel flags + Tower/Ground/ATIS/CTAF/UNICOM/AWOS/ASOS
frequencies from `com`, Hz converted to MHz) when the catalog carries a facts
line, then "Nearby, N items" — everything within 1 km, nearest first. BOTH fuel
flags together read "Fuel available.", never "Avgas and jet fuel.": on an MSFS
2024 database the two are all-or-nothing (measured 2026-09-21: 17,079 airports
carry both, 67,199 neither, not one carries a single flag), so "both" grades
nothing and the old wording claimed jet fuel at 1,147 fields with no hard runway
and under 2,500 ft of runway — 4II2 "Hangar Fly Ultralight Fly Club" is 965 ft.
One flag alone still names its grade, because a disk-built MSFS 2020 database
sets the two independently. A frequency row whose name says apron, ramp, GATES,
delivery or clearance is not the one the label names: at KMIA the first of nine
`G` rows is "MIAMI GATES", which read out as "Ground 120.35". With neither
facts nor features, nothing is opened: the caller SPEAKS "Nothing within …"
instead of
putting an empty window in front of the pilot, the same rule the flight-info
window follows. Not live-updating; reopen the chord for a fresh snapshot.

Both chords run the whole lookup — which airport, the catalog build, the
compose — inside `Task.Run` and marshal only the speech or the window back to
the UI thread (`MainForm.RunSurroundingsLookup`, the ONE path they share), and
newest-press-wins, so a slow first lookup never speaks after the pilot has
pressed again.

**Every line a chord SPEAKS is timed from the PRESS**
(`SurroundingsLookupNotice.Delivery`) — the look-around answer, "No airport
nearby.", "No surroundings data for …", "Nothing within …", "Surroundings
lookup failed.". Within `Delay` (1.5 s) it interrupts, like any hotkey answer;
from then on it is QUEUED. A cold lookup takes 3-10 s, and in that time the
pilot may have been given a taxi instruction — "Stop. Hold short of runway
27L." — that an interrupting answer cut off mid-word; the queued "Looking
around." notice is ahead of it too, and used to be cut off by it. The one
exception is a SUPPRESSED announcer (a first-detect grace window), which DROPS a
queued line: there a late line still interrupts, because a pilot who pressed a
key must never hear nothing.

### Passing callouts (opt-in, default off)

`AirportSurroundingsMonitor` asks for the aircraft's own position on a 2 s UI
timer (mirrors `GroundTrafficMonitor`'s shape — the taxi position stream is
taxi-scoped and off with no route loaded, so this cannot ride it) and judges
every `AIRCRAFT_POSITION` answer where it lands (`OnPositionReceived`: its own
request's and any other feature made — each a fresh sample), re-resolves
the airport at most every 30 s, and hands the ranked feature list (within
`PassingCalloutGate.RankRadiusMetres`, 350 m) to the pure `PassingCalloutGate`.

**A building is PASSED at its closest point of approach** — the range closed by
at least `MinApproachMetres` (15 m) and has since opened by `OpeningMetres`
(5 m) — when that CLOSEST POINT lies inside its kind's pass radius (Concourse/Terminal
225 m, Tower 300 m, others 150 m — **measured, see below**) and it is announceable (Terminal, Concourse,
Fbo, Tower, Fuel, Cargo, FireStation, and Hangar only with a PROPER name — the
scenery tier labels a model called just "Hangar" with the kind word itself,
marked `NameIsGeneric`, and "Passing Hangar" names nothing a pilot can look
for). It then
fires at most once per building per 5 minutes, once globally per 10 s, and only
while ground speed is 2–40 kt.

**The approach is tracked from the edge of the rank window, never from the
radius**; the radius is applied to the MINIMUM when a pass arms. Tracked only
from inside its radius, a feature's first range was at most the radius itself,
so the most a pass could close was the radius minus its closest point — 2 m for
EHAM's 223 m pier under 225 m, 13 m for LOWI's 137 m hangar under 150 m — and
neither could ever be called, although clearing exactly those two was the
reason for the radii below. A simulated straight pass at 15 kt with the
monitor's 2 s polls now calls both, and a closest point 5 m outside its radius
is still silent. A closest point outside the radius leaves the track unarmed, so
a later, nearer approach to the same building can still be a pass.

**It changes RELEASE too, on purpose.** A pass held back by the 10 s global gap
or by speed used to be dropped once its building left the kind's radius — the
release loop only visited features inside it. The loop now visits the whole
rank window, so a held pass can be spoken while its building is anywhere within
350 m, as long as `PendingExpiry` (20 s from arming) has not run out. Accepted:
it still names the side and range of its OWN closest point, and 20 s at taxi
speed keeps that building beside the aircraft.

**The radii are MEASURED and the rank window moves with them.** At the shipped
150/200/100 the feature said almost nothing on a real taxi: replaying two
RECORDED pilot tracks at 31 Hz through the production catalog, Rank and gate
gave ONE callout on 5.59 km at EHAM and TWO on 4.35 km at LOWI. ⚠ The first
diagnosis — that a pass parallel to a pier can never close `MinApproachMetres`
— was WRONG, and a synthetic sweep over every taxiway at the airport is what
suggested it; on the pilot's own tracks EVERY announceable feature passed was
abeam at its closest point, and the blocker was only the radius. What a taxiing
aircraft goes past clusters just OUTSIDE the old numbers: EHAM's eight nearest
piers at 141-223 m against 150 m (taxiway Bravo is Schiphol's OUTER parallel and
never comes nearer), LOWI's ten nearest hangars at 106-137 m against 100 m.

| radius | EHAM says | LOWI says | per km |
|---|---|---|---|
| 150/200/100 (shipped) | 1 | 2 | 0.2-0.5 |
| **x1.5 = 225/300/150** | **7** | **7** | **1.3-1.6** |
| x2.0 = 300/400/200 | 9 | 9 | 1.6-2.1 |
| x3.0 = 450/600/300 | 11 | 10 | 2.0-2.3 |
| x4.0 = 600/800/400 | 13 | 10 | 2.3 |

x1.5 clears the whole cluster at both fields (225 > 223, 150 > 137); both have
SATURATED by x3, so wider only starts naming buildings the pilot is nowhere
near. ⚠ The table was measured while a feature was still tracked only from
inside its radius, so the furthest of each cluster — the two numbers x1.5 was
chosen to clear — could not arm in it; its counts are what that gate said.
**`PassingCalloutGate.RankRadiusMetres` (350 m) is the ceiling and the monitor
ranks to it** — it used a literal 250 m, so the widened 300 m tower radius would
have been a number the gate could never see, silently capped at the window. It
is also where tracking starts, so a test pins that every kind's radius sits at
least `MinApproachMetres` below it.

**One SENTENCE is not said twice in five minutes, whichever feature carries
it** (`SameNameRepeat`, keyed on spoken name AND side). `PerFeatureRepeat` is
keyed on identity — kind, name AND position — which is right for a building
approached twice and useless when several DISTINCT features carry one name:
each gets its own track and each fires the same words.

Two independent sources of that collision, both measured:

- **Synthesized labels.** `NavdataFeatureSource` makes a "Cargo ramp" per
  single-linkage stand cluster, a "Fuel" per fuel cluster, a "GA ramp" per GA
  cluster. On routed taxis: KATL 12 callouts of which **"Cargo ramp" was five**,
  OMDB 7 with 2 repeated, NZAA 2 with 1.
- **Proper names from the installed scenery — the bigger half, and missed at
  first.** Across this machine's 109 airports with scenery features, **64 (59%)
  carry a repeated announceable name** and **301 of 1,328 (23%) duplicate one**;
  RJFF has **thirty** features called "Fuk City Hangar", EHAM fifteen "Amsterdam
  Hangars East", BIKF thirteen "DS Hangar Military". ⚠ The rule was briefly
  keyed on `NameIsGeneric`, on the strength of four airports where no proper
  name repeated, and that missed all of these.

**The side is part of the sentence.** The key is name + left/right, so
"Passing Fuel, on the left" and "Passing Fuel, on the right" both speak — two
buildings a pilot CAN tell apart. That is what keeps
`Two_same_named_buildings_150_metres_apart_are_tracked_and_announced_separately`
green, and only the SENTENCE is deduplicated: tracking stays per building (kind
+ name + position), which is what prevents the false pass a name-only track key
produced.

After: RJFF 1 callout instead of a row of thirty, BIKF 4, KATL 9 with 8 distinct
names. **KSFO is the control and is untouched — 7 callouts, 7 different
buildings.** Known cost: a genuinely different building sharing a name on the
same side inside the window is dropped (KJFK's two "Concourse B", 1.3 km apart);
it would have been the identical sentence. `Reset()` clears the memory;
**`RebaselineTracks()` deliberately does not** — what the pilot has been told is
a fact about the pilot, not about the catalog.

**There is no baseline and must not be one.** Parked beside a terminal the range
never closes, so nothing is recited. The baseline this replaced was a one-shot
5-minute timestamp that lapsed during any normal preflight, after which the
terminal the aircraft had been parked at all along was announced anyway. ⚠ An
earlier telling added "or pushed back from one" — FALSE for anything measured to
its stands: a navdata Fuel, Cargo ramp or gate-built Concourse IS its stands
(`SurroundingsGeometry.Nearest` measures to the nearest one), so an aircraft
that TAXIED onto one closed the range to a few metres, stood there, and on
leaving heard "Passing Fuel, on the left." with the side read off bearing
noise. That case is the closest-point rule's job (below), not a baseline's.

Six rules the gate cannot lose:

- **A track's identity is kind + name AND POSITION** — an incoming feature
  continues an existing track only within `SameFeatureMetres` (40 m) of that
  track's last-seen position, and the 5-minute repeat memory uses the same
  identity. Two announceable features can legitimately share a name (navdata's
  per-cluster generic "Fuel"/"Cargo", two same-named piers, several same-named
  scenery clutter clusters); keyed on the name alone, the nearer one's minimum
  made the farther one's still-closing range read instantly as "opening" — a
  false "Passing X". **Never widen that 40 m**: safety comes from
  `MergeRadiusMetres`, whose smallest value is Hangar's 40 m, so for a generic
  hangar pair the margin is zero, not "half" of anything.
- **The closest sample must itself have been ABEAM** (45°–135° either side),
  judged at the MINIMUM-range sample and never at the detection sample — at 40 kt
  with a 2 s poll that sample can sit 40+ m past the true closest point, where a
  genuinely abeam building already reads well past 135°. This is also what
  excludes a building approached tail-first during a pushback (the range closes
  backwards, then "opens" as the aircraft taxies away). A non-abeam minimum is
  consumed silently.
- **A closest point reached while STOPPED, or at zero range, is not a pass** —
  consumed silently, exactly like a non-abeam one (`IsSayablePass`). A sample
  below `MinSpeedKts` within `OpeningMetres` of the minimum means the aircraft
  stopped AT its closest point instead of driving through it (a fuel stand, a
  hold beside a hangar); a minimum at or inside
  `SurroundingsReport.ZeroRangeMetres` (3.81 m) has a degenerate bearing, so its
  side would be arbitrary — the Surroundings readout says "here" there for the
  same reason. Speed AT the closest point is the one input that tells a stop
  from a pass: a pass that arms at taxi speed and is only then held below
  `MinSpeedKts` is `PendingExpiry`'s case and still speaks. An unreadable speed
  counts as stopped, which can only withhold a callout.
- **The pass freezes the moment it arms.** `Evaluate` returns the distance and
  bearing the building had at its OWN closest point, not the sample that releases
  it. A pass held back by the 10 s global gap or by ground speed outside the
  band can fire 10–25 s later, through a turn; announcing the current bearing
  would name the wrong side, or a direction that is not a side at all. Because
  a pass only ever arms from an abeam minimum, what comes back is always left
  or right.
- **A pass that cannot fire is given up on**, after `PendingExpiry` (20 s from
  arming) — comfortably past the 10 s global gap and a late sample, short enough
  that what it describes is still beside the aircraft. Pass a building, stop
  inside its radius (below `MinSpeedKts` nothing may fire) and taxi on three
  minutes later, and the held callout named somewhere the aircraft no longer
  was. Checked ABOVE the speed/gap test, because in exactly that case the test
  below it is never reached. An expired pass is consumed silently.
- **A catalog swap re-baselines the tracks.** When the instance
  `TryGetCached` hands back differs from the previous sample's, the monitor calls
  `RebaselineTracks()`: a rebuild can change a feature's geometry BASIS (a
  stand cluster becomes a building outline), so a track carried across it sees
  a range STEP rather than the next sample of an approach — a premature pass
  one way, a lost one the other. It clears the approaches and KEEPS the fired
  memory and the global gap; a full `Reset()` there would let a building
  announced a moment ago be announced again.

Callouts are **silent on runway pavement**. `SuppressCheck` reads Takeoff
Assist, docking and the taxi states (`LandingRollout`, `LiningUp`, `HoldShort`,
`ProgressiveHold`, `BacktrackingOnRunway`/`BacktrackDeparture`), plus
`announcer.Suppressed` — but a takeoff flown without the assist, or a landing
without an exit plan, leaves all of those idle, so `RunwayProbe`
(`TaxiGuidanceManager.IsOnRunwayPavement`) asks the pavement itself. That probe
answers only from geometry that is ALREADY in hand and never builds a graph,
because its caller is the monitor's position handler, on the UI thread; null
means "nothing to ask".

**The probe keeps its runway shapes.** They are memoised by AIRPORT as well as
by graph instance, and answer when neither graph is available —
`RunwayShapeSource` owns the ordering (active graph, Where-Am-I graph, memo).
An Alt+Y or Alt+L at the airport builds the Where-Am-I graph through
`GetTaxiPaths`, which starts the background taxiway-name fetch; when that lands,
`OnAirportDataUpdated` nulls the Where-Am-I graph. (Until the warm-up stopped
building graphs, the probe's OWN warm-up was what started it.) Losing a graph
seconds after it answered is therefore the ORDINARY sequence, and keyed on the
graph instance alone the probe then answered null for the ~60 s until
`ShouldWarmProbe` allowed another warm-up — during which null does not silence
anything, so callouts were permitted on a runway (a landing with no exit plan,
a flight started on the runway). Runway pavement does not depend on taxiway
NAMES, so shapes built before the fetch are still right after it. The memo is
dropped only with the graph cache it shadows (`ClearWhereAmICache`) and
replaced when a graph for a different airport is probed — never by the name
fetch, which is the one invalidation it must outlive. `RefreshDatabaseProvider`
calls `ClearWhereAmICache` for exactly this reason: the same airport can carry
different runway geometry in the two databases.

**A database switch moves the generation.** `ClearWhereAmICache` drops the
Where-Am-I graph and the memo and moves `TaxiGuidanceManager.DatabaseGeneration`,
but deliberately leaves active guidance's own graph alone — a route being flown
keeps its graph. That graph was built from the previous database, and it records
the generation current when its INSTANCE was installed (`_graphGeneration`,
stamped only for a NEW instance — a rollout re-route hands back the same graph
and must not restamp it), so from the switch on the probe neither answers from
it nor re-seeds the memo from it (`RunwayShapeSource.Choose`); it once did both,
and the re-seeded memo — the old database's runways, filed under the new one —
outlived `StopGuidance`. A memo of another generation is never read. The memo
is ONE record, `RunwayShapeMemo` (airport, generation, the graph it came from,
the shapes), and `RunwayShapeSource.Resolve` is the whole probe step — which
source answers and what memo is left behind — pure and pinned by
`RunwayShapeSourceTests`.

A Where-Am-I graph obeys the same generation: `DescribeCurrentLocation` takes
the generation read with its provider as a REQUIRED parameter and caches its
graph only while that generation is current (`StoreWhereAmIGraph`, through the
same `RunwayShapeSource.MayStore` the warm-up's publish uses). Alt+L captures its
provider at the PRESS and builds on a pool thread, and one in flight across a
switch used to write the previous database's graph straight back into the cache
the switch had just cleared — answering later Where-Am-I presses from the old
database and handing the probe old runways to re-seed its memo from.

**The warm-up reads the runway rows, never a taxi graph.**
`TaxiGuidanceManager.PrepareRunwayShapeWarmUp` builds the shapes from the start
and runway tables alone (`RunwayPavement.BuildShapesFromRunwayRows`) and
publishes them to the memo. Centreline pairing in `TaxiGraph.Build` never reads
a taxi path or a stand — an empty graph returns from its parking and bridging
passes untouched — so `Build` with no paths and no parking IS that pairing, and
a test pins the shapes identical to the full graph's. The warm-up used to call
`DescribeCurrentLocation`, which returned "No taxi data" before building
anything at an airport with no taxi paths — 18,737 of fs2024's 41,411 airports
with a runway have none — so the probe stayed null there all session and
building callouts spoke on the runway during an unassisted takeoff or rollout.
Measured with a replica of the two pairing passes over those airports: 18,234
pair a centreline for every land runway from their start rows, 70 some, 393
none (386 of them because every start row lies within 200 m of every other, the
pairing floor), and 40 seaplane bases have no land start row. It also held
`_stateLock` across a whole graph build and, through `GetTaxiPaths`, started the
online taxiway-name fetch; it does neither now. An airport with no runways
publishes an EMPTY list, which answers "not on a runway" (right: there is none);
a runway whose start rows cannot be paired is invisible here exactly as it is to
Where-Am-I. The warm-up is PREPARED on the UI thread in the same turn that read
the provider (`AirportSurroundingsMonitor.PrepareRunwayProbeWarmUp`), so it
carries that provider's database generation, and one that straddled a database
switch is read for nothing and stored nowhere (`RunwayShapeSource.Publish`) —
nor is one that finishes after the monitor has moved on to another airport:
`Publish` keeps a warm-up's shapes only for the airport the probe is being asked
about (`TaxiGuidanceManager._runwayProbeIcao`, recorded by every probe read), so
a late warm-up for the previous airport can never evict the new one's memo and
leave the probe answering null for a minute. A side effect: the first Alt+Y —
and the first Takeoff Assist runway detection — at an airport builds its own
Where-Am-I graph again, exactly as both always did with the passing callouts off
(the default).

**Every `AIRCRAFT_POSITION` answer is a sample, judged where it lands.** The
2 s timer only ASKS (`RequestAircraftPosition`); `OnPositionReceived`, the
monitor's handler on `SimConnectManager.AircraftPositionReceived`, judges the
answer — its own request's, and any other feature made (ground traffic and
TCAS every 3 s, Where Am I, Look Around, the liftoff confirm). Each is a fresh
sample: the tracker measures distance between whatever samples it is given,
and every gate downstream is time- or distance-based, never
sample-count-based. The tick used to read `LastKnownPosition` straight after
asking — the PREVIOUS poll's answer — so every callout came a poll late, and
its ground flag came from yet another sample. The ground flag is now the
sample's own (`SimOnGround`), and the event is raised only by the case-4 frame,
the one that carries the surface fields, so surface, position and flag always
belong together. The monitor is the event's first permanent subscriber, so its
WHOLE handler is guarded: a throw would abort the multicast and every one-shot
`RequestAircraftPositionAsync` behind it (Alt+Y, Alt+L, the liftoff confirm)
would miss the answer. Both callouts are therefore POSTED to the UI-thread
`SynchronizationContext` captured at construction, never spoken synchronously
inside that handler: a one-shot behind it (Where Am I, Look Around) answers the
SAME sample and may call `AnnounceImmediate`, which would cancel a callout this
handler had just queued. Posting lets that interrupting readout speak first and
the callout follow it, exactly as it did before position-judging moved onto
the SimConnect thread. With both callout switches off nothing is asked for, and
an answer another feature asked for is not sampled either.

**The first sample of a flight is only recorded.** The first sample after a
liftoff, a `Reset()` or a pause in sampling (both switches off — turning either
back on forgets the last position, `SurroundingsSampleTracker`) has nothing to
measure it from — no distance for the surface gate, no jump test — so it is
only recorded, and the passing half acts from the next one. A sample whose
position is not a finite number does nothing at all.

`Taxiing` is deliberately NOT suppressed — a pilot under active taxi guidance
is exactly who this is for.

**Neither background job a position sample can start may run at a bad
moment.** One policy, `MayStartBuild(suppressed, groundSpeedKts)`, gates both
the first-time catalog build and the probe's own warm-up. The warm-up used to be
the heavier of the two: it held `TaxiGuidanceManager._stateLock` across the
taxi paths, the parking list, the runway rows and a whole graph build, and
ungated, the first ground tick after a touchdown started it at ~120 kt and the
next tick blocked the UI thread — the SimConnect pump, the queued announcer and
the hotkeys — for the length of it, during the rollout. It now reads two small
tables and takes the lock only to publish, but it still reads the database, and
one policy serves both jobs. Waiting costs at most a late FIRST callout.
**Never "fix" a busy lock with `Monitor.TryEnter` in the probe**: a null answer
does not silence anything, so lock-busy would PERMIT callouts on the runway —
and a Where-Am-I lookup (Alt+Y, Alt+L) still holds that lock across its own
graph build. The probe is read once per position sample (not at all on a suppressed one)
and that single read feeds both the warm-up decision and the silence;
`ShouldWarmProbe` is pure, retries at most once per `ProbeWarmRetry` (60 s)
while the probe still cannot answer, and never starts a second warm-up while
one runs.

The phrase is "Passing {Name}, {left|right}." — side only, no distance, no
advice — always queued (`Announce`), never `AnnounceImmediate`.

The gate forgets every track and every recent fire when MainForm calls
`AirportSurroundingsMonitor.Reset()`, which it does from **four** places — a
sim reconnect and an aircraft switch (`MainForm.AircraftSwitch.cs`), a database
switch (`RefreshDatabaseProvider`), and a **turnaround liftoff**, inside the
`_turnaroundDetector.ObserveEdge(…)` branch in `MainForm.Announcers.cs` beside
`_routeAdvisoryProximity.Reset()`. `Reset()`'s own XML doc lists all four; keep
the two in step.

The monitor also resets the gate itself, on an airport change, once per airborne
episode (a building still closing at rotation would otherwise read as "opening"
on the rollout), and on a position JUMP of more than `JumpMetres` (250 m — six
times the 41 m a 40 kt aircraft covers in one poll, so only a teleport, slew or
flight reload trips it, and the only cost of tripping it anyway is a forgotten
track, never a wrong callout). It re-baselines the tracks (`RebaselineTracks` —
the approaches go, the fired memory and the global gap stay) when the passing
switch is turned back ON: an approach recorded before the switch went off was
not watched while it was off, and read against the first sample after it could
arm a pass nobody saw happen.

### Surface-change callout — "Off the pavement, on grass." (opt-in, default off)

The one surroundings callout with a safety case rather than a convenience one. A
blind pilot cannot see where the taxiway edge is; the tester this was designed
with could not perform the grass half of its own acceptance test for exactly
that reason.

`SurfaceChangeGate` is pure (no clock, no sim access) and is driven from
`AirportSurroundingsMonitor` through `SurroundingsSampleTracker`, pure too,
which owns everything per-sample that needs no sim: the last position, the jump
test, the unreadable-sample guard and the two switches. The monitor judges
every `AIRCRAFT_POSITION` answer itself (`OnPositionReceived`), never
`LastKnownPosition`, so the surface, the position and the ground flag it acts on
are always ONE sample. `SURFACE TYPE` and `SURFACE INFO VALID` ride the
`AIRCRAFT_POSITION` definition. **Their order in that definition and in the
`AircraftPosition` struct is the contract**: last in both, same order, or every
field after the divergence reads from the wrong offset.

**Every OTHER writer of `lastKnownPosition` carries the two fields forward.**
The visual-guidance (505), flare-assist (508), taxi-guidance (507) and
takeoff-assist (506) streams each mirror their own frame into
`lastKnownPosition`, and none of them knows what is under the wheels. Built
field by field, the surface fields defaulted to 0, and `SurfaceInfoValid` 0 is
what the gate reads as "say nothing": while the monitor still read
`LastKnownPosition`, a loaded taxi route — taxi guidance mirrors on every frame,
the case this callout exists for — silenced it with no error and nothing in the
log. The callout no longer reads `lastKnownPosition`, but other features do, so
a new position mirror must still copy both fields from the previous
`lastKnownPosition`, as the 506/507 mirrors do for `Altitude`, and never default
them.

Five rules, each measured rather than chosen:

- **Only a change of FAMILY speaks.** `SurfaceFamilies` folds the sim's enum into
  Paved / Unpaved / Grass / Water / SnowOrIce / Unknown. Asphalt-to-concrete
  happened **four times on one 2.35 km LOWI taxi** (the GA apron is concrete,
  taxiway Alpha is asphalt) and carries nothing a pilot can act on.
- **Confirmation is by DISTANCE travelled ON the new surface** (`ConfirmMetres`,
  12 m), never time. A DA40 steers on differential braking, so a taxi is
  stop-start by nature and a "has held for N seconds" rule fires on a stationary
  aircraft. The distance is measured FROM THE FIRST READING of the new surface:
  whatever distance arrives WITH that reading was driven from where the old
  surface was last read, so none of it is credited. At the monitor's 2 s poll
  that distance is 10–15 m at taxi speed — a whole `ConfirmMetres` — and the
  first version credited it, so ONE reading confirmed: a wheel clipping the grass
  at a corner and coming straight back was announced. Now a single reading never
  confirms, whatever the speed; when nothing else is sampling in between,
  rolling on at 15 kt (15.4 m a poll) confirms on the second reading, at 10 kt
  (10.3 m) on the third — a denser real sample (ground traffic, TCAS, a hotkey)
  only adds readings and confirms at least as fast, never slower, since the
  rule is measured in metres, not readings. A stop on the new surface holds the
  evidence; a non-finite distance or ground speed adds nothing. The tests feed
  the gate at that SPARSEST cadence
  (`AirportSurroundingsMonitor.PollMs`) — the 2 m samples they fed before cannot
  tell the two rules apart.
- **Leaving the pavement is one surface; every other change is per family.**
  While the pilot was last told "pavement", every non-paved family — grass,
  unpaved, water, snow or ice — counts toward ONE excursion from the first such
  reading, and the reading that confirms it names it: mottled ground whose
  readings alternate grass and gravel is still "Off the pavement" (review A3-6;
  kept per family, each change reset the evidence and that drift was never
  announced). Back onto the pavement, and one non-paved family to another, need
  readings of that family, and a reading of the family the pilot was last told
  about resets whatever is pending — so flapping between grass and gravel off
  the pavement stays silent, and a single asphalt reading after a gravel one is
  not "Back on pavement.".
- **An enum value the table does not name is SILENT** and does not disturb what
  the pilot was last told. Only three values are measured live in MSFS 2024 — `0`
  concrete, `1` grass, `4` asphalt, confirmed against LOWI's GA apron, its
  08L/26R grass strip and taxiway Alpha — the rest is the published SDK enum.
- **The first surface of a session is a silent baseline**, as is the first after a
  position jump, a `Reset()`, or the surface switch being turned back ON. An
  aircraft that was PUT on the grass has not driven off anything, and whatever
  was driven while the switch was off was never watched, so it is not news when
  the switch comes back on. Turning the PASSING switch on never touches the
  surface gate: switching the convenience callout on must not cost this one the
  evidence of an excursion the pilot is driving right now. Re-assigning a switch
  the value it already has — the settings dialog assigns both on every OK — is
  not an edge and changes nothing.

**An unreadable sample never confirms.** A position that is not a finite number
is skipped outright — the monitor acts on nothing for that sample, and the last
READABLE position stays the one the next distance is measured from. A surface
type that is not a finite number is no family at all (never `(int)NaN`, which
.NET 9 and later saturate to 0 — concrete — so a NaN would have told a pilot on
the grass they were back on pavement), and a validity flag that is not a finite
number is not valid.

Three departures from how the neighbouring callouts behave, all deliberate:

- **Its own setting** (`SurfaceChangeCalloutsEnabled`), not the passing-callouts
  switch — that one names buildings you go past; losing this because someone
  turned the chatty one off would be the wrong trade.
- **NOT behind `SuppressCheck`.** Every other callout on this tick goes quiet
  during takeoff assist, landing rollout and docking — exactly the states in
  which running off the side matters most.
- **Runs before every airport-dependent guard** — no navdata, no catalog, no
  ICAO, so it still works at a field the database has never heard of.

Queued, not immediate: `AnnounceImmediate` discards whatever is being spoken, and
this codebase has been bitten repeatedly by one callout cutting another off
mid-word.

**Measured and REJECTED beside it (2026-09-22): uphill/downhill callouts.**
`GROUND_ALTITUDE` is an excellent sensor — 9x10^-10 m of drift over 10 s at rest,
and it resolved a 0.19 % apron drainage camber cleanly — but airports are graded
flat by regulation (ICAO Annex 14 caps taxiway longitudinal slope at 1.5 %).
Measured over two real taxis: EHAM 0.50 m of range over 5.59 km, steepest 40 m
grade +0.58 %; **LOWI, in an alpine valley, 0.03 m over 2.35 km, steepest
+0.006 %** — flatter than Schiphol. There is nothing for it to say. **Bridge
detection by elevation was rejected in the same pass**: crossing EHAM's
OSM-tagged taxiway V bridge (within 6 m of the way centre), ground elevation
moved 14 mm, aircraft altitude 17 mm and AGL 0.045 ft — the taxiway is at grade
and the road passes underneath in a cutting, so there is no hump to detect. OSM
`bridge=yes` tags exist on only **289** `aeroway=taxiway|runway` ways worldwide,
which makes bridges garnish where the data happens to exist, not a feature.

### Taxi to a place

The pilot can route to a feature — an FBO or hangar after landing, the fuel
island, a terminal — without the feature ever entering the graph. The Taxi
Assist form's destination-type combo gains **Place** (index 4, beside Deice
Area), and `PlaceListBuilder.Build` (pure, tested) resolves each routable
feature onto navdata pavement, **by position**, in this order:

1. a stand of the **selectable** list within `MaxStandMetres` (150 m);
2. failing that, a stand **only navdata lists** (`EnsureNavdataOnlyStands` —
   GSX's list excludes Vehicle and Fuel stands and drops those with no usable
   heading, which is exactly the kind of spot an FBO, hangar or fuel place ends
   at);
3. failing that, a taxi node within `MaxNodeMetres` (100 m) that
   `TaxiAssistForm.NearestRoutableNode` has cleared of hold-short nodes and
   runway pavement — the "nearest taxiway point" must not be on a runway;
4. failing all three, the place is not routable and is not listed.

Among the stands in range it prefers one that is a MEMBER of the feature (a
concourse resolves to one of its own gates, the most central) and then one
whose type matches the place — GA-ramp or dock types for an FBO, hangar, office
or fire station, FUEL stands for fuel, CIVIL cargo stands for cargo (a
military stand is not a type match), gate types for a
terminal or concourse — else the nearest non-vehicle stand. Routable kinds are
Fbo, Hangar, Fuel, Terminal, Concourse, Cargo, FireStation and Office; never
Tower, Helipad, Apron, DeicePad or Other. (De-ice pads have their own
destination type.)

**Never resolve a place through a `(Name, Number, Suffix)` join.** The version
this replaced resolved onto a navdata stand and then looked for "the same"
stand in the selectable list that way. That identity collides — measured on
fs2024, 104 groups at 34 airports in navdata alone, the widest 4 km apart at
OIIE — so the route, docking and `gate.select` could go to a different stand
than the label named; and it usually found no twin at all, because GSX leaves a
ramp stand's `Name` empty where navdata's `P` maps to "Parking", which sent an
identifier-less spot to `gate.select` and produced "GSX could not prepare this
stand." on nearly every FBO route. Resolving against the selectable list by
position removes the join entirely.

Each entry reads "Narrows Aviation, FBO, Parking 12" — or "…, Gate 7A" for a
lettered gate, "…, Spot 12" for a letterless one, "…, nearest taxiway point"
when only a node was found — the place, its kind, and the stand the pilot will actually
be guided to. The kind word is left out when the name already says it ("Fuel,
Parking"), and a spaced dash in the name becomes a comma because
`RouteReachabilityMessages.SpokenDestinationName` cuts a label at its first
" - ". Duplicate labels get a "(2)" suffix.

It fills the same destination maps the gate branch fills, so Calculate,
`LoadRoute`, docking and the GSX stop offset need no Place-specific code:

- **A stand entry** targets the stand's own position and heading, exactly as
  the Gate / Parking type does for that stand.
- **A node-only entry** writes NO heading and NO lineup target, so arrival
  takes the "no lineup data — just stop" path instead of aligning the nose on a
  building, and docking is cleared.
- **`gate.select` is sent for a Place only when its stand carries a
  `GsxIdentifier`** — `TaxiAssistForm.ShouldSendGateSelect(destinationTypeIndex,
  spot)` is the one pure rule Gate and Place share. A navdata-only stand has no
  identifier and GSX is not asked.
- The same two stand filters the Gate list applies — wingspan fit and
  hide-occupied — apply to a Place, so it cannot route a heavy onto a commuter
  stand or onto an occupied one.

**The list awaits its catalog; there is no "warmed once per ICAO" latch.** A
miss starts one warm-up (`WarmPlaces`), which repopulates once the cache holds
a catalog; the latch that used to stand in for this outlived every cache
invalidation and left the list empty behind a false "No places to route to".
One bounded retry covers the sequence the cache is designed to produce — the
build stopped waiting for OSM, which it collects last, and the answer landed
before its merge finished and invalidated the airport, so the finished build
was discarded rather than cached — and a
build that genuinely failed is not retried at all (the cache's own failure
memory owns that). Only the warm-up that still OWNS the form's Place state may
touch it — by a TICKET minted per warm-up, not by its Task, because
`SurroundingsCatalogCache.GetAsync` is single-flight and hands two warm-ups for
one airport the SAME Task, which a reference test on it let both settle from;
a superseded one logs a line and changes nothing.

**The list follows buildings that arrive LATE.** That one retry fires only when
the OSM answer lands in the build's last moments — after it stopped waiting for
OSM, which it does last, and before its merge finished: milliseconds, whatever
the scenery cache holds — so the ordinary slow-mirror case is an answer that lands
after the list was already built and settled. `OnlineFeatureStore.FeaturesUpdated`
then invalidates the catalog, and MainForm marshals that onto the UI thread as
`TaxiAssistForm.OnSurroundingsInvalidated(icao)`, which re-runs the warm-up when
the form is still in Place mode at that airport with none already in flight.
FBOs and hangars come mainly from OSM, so without this the pilot's FBO could be
missing from the list with no hint that it exists.

That event is the ONLY thing that invokes the forward, and the store raises it
only when a late fetch SUCCEEDS — so a catalog left DEGRADED is covered only
when the retry's own fetch lands. A fetch that refused, or a tier that threw,
raises nothing, and the degraded expiry in `SurroundingsCatalogCache.FreshOrNull`
notifies nobody; such a list is refreshed by the next ordinary rebuild instead
(a destination-type switch, a filter toggle, an airport reload, a gate-token
move).

The refresh is silent at the start — the pilot asked for nothing — and silent at
the end unless the list really changed, in which case the new count is spoken,
queued, only while the dialog is open. One that arrives while the destination
dropdown is OPEN waits for it to close rather than rebuilding the list under the
reading cursor.

**It stops being background the moment the pilot arrives in front of the list.**
Switching the destination type away and back mid-refresh empties the list, finds
nothing cached (the catalog was just invalidated), is refused a second warm-up by
the in-flight guard and has its "no places" line suppressed by that same guard —
and the refresh would then settle on an unchanged count and say nothing either,
leaving the pilot in front of an empty list with not one word spoken.
`DescribePlaceModeEntry` PROMOTES the running warm-up instead: the ordinary
"Loading places for {icao}." is spoken there and then, and its settle reports
like any foreground one. `_placesWarmUpBackground` is a field precisely so the
settle sees the promotion. A silent destination restore promotes nothing.

**A place that was RENAMED is not a place that was lost.** The catalog's own
merge rule lets a proper OSM name absorb a synthesized one, so "GA ramp, Parking
3" can come back as "Narrows Aviation, FBO, Parking 3" with the count unchanged —
and re-seating by label alone then cleared the selection silently, so the next
Calculate aborted with "Please select a destination." for no visible reason. On a
BACKGROUND settle only, and only after the label has failed, the selection is put
back by its routing TARGET (`PlaceTarget`: the destination node plus the stand's
position, or no stand for a node-only place) — the same target is the same route,
the same docking and the same `gate.select` decision, so it is the same choice
under a new name; the first match wins if two entries share one. Where even the
target is gone the pilot hears `PlaceListUpdatedMessage` — *"Places updated.
Please choose the destination again."*, never the GSX sentence, because GSX did
not do this — queued, while the dialog is visible, and it REPLACES that settle's
count line. The gate-source-refresh and restore origins keep their existing label
rule untouched.

**A background settle speaks that sentence for a RESTORE-origin pending too, and
that is deliberate.** A SayIntentions probe fails, `RestoreDestinationState` arms
the pilot's pre-probe place (kept, because a warm-up is in flight), and the
refresh then renames or drops it. The probe's own silence rule — "probing leaves
no mark" — covers the probe's narration, not a background refresh that removed
the destination while it ran: what took it away is the refresh, so the sentence
is true and actionable, and the alternative is a silently cleared destination and
a baffling "Please select a destination." at the next Calculate. A FOREGROUND
settle on the same pending still says nothing.

Which of the two a settling warm-up is comes from `ResolveWarmUpBackground`:
`_placesWarmUpBackground` answers only while the form still names that warm-up's
ticket — the one state in which it could have been promoted, and the one in which
a `false` there means "promoted" rather than "cleared by an airport load" — and
otherwise the warm-up's own start-time parameter is all that is still true about
it. Read blindly, a background refresh outliving a `LoadAirportDataCoreAsync`
reset with no successor announced a count nobody asked for and skipped the rename
fallback.

**A pending selection the rebuilt list no longer carries leaves NOTHING
selected, never item 0** — item 0 plus Calculate would route to, and
`gate.select`, a place the pilot never chose; cleared, Calculate aborts with
"Please select a destination." instead. The pilot's own later pick always wins
over an armed pending, and `PopulateDestinations` does not auto-seat item 0
while a Place pending is armed, so a live selection found at the settle can
only be the pilot's. **A pending that NAMES a destination is never replaced by
one that names none** (`MergePendingPlaceSelection`), and the origin flag
follows the label that survives: a second gate-source refresh during the same
warm-up passes the token check again with the list still empty, so its own
`previous` is null, and unguarded it wiped the label the first one had armed —
the pilot's place was never re-seated and nothing was said about it. Where a
silent restore's label outlives such a request the surviving origin is the
restore's, so a FOREGROUND settle stays silent about the loss: the restore is a
probe undoing itself and the pilot performed no action there.

A loss caused by the gate-source refresh speaks the shared
`GateListUpdatedMessage`, queued, while the form is visible; a loss on a
foreground settle from any other origin — a silent destination restore, a live
pick preserved across a rebuild — says nothing; and when nothing is listed at all only
the list's own line is spoken, because "choose again" would be an instruction
to choose from an empty list. That line says which KIND of empty it is
(`DescribePlaceList`): "No places to route to at {icao}." for a catalog that
exists and yields nothing routable, **"Places could not be loaded for {icao}."**
when no catalog came back at all — a build that failed, or one discarded twice,
where the airport having no places was never actually learned. With no airport
loaded it says nothing rather than "No places to route to at ." (reachable by
selecting Place while pre-planning in the air) or naming the previous airport
during a load.

SayIntentions "taxi to the FBO" as a clearance candidate is deferred until a
live capture shows SI phrasing a place rather than a stand.

### Overpass mirrors — a regional instance must never be in the list

**A REGIONAL Overpass instance — one serving a country extract — answers a query
about anywhere outside its extract with HTTP 200, an empty element list and NO
`remark`.** `OverpassClient.ClassifyBody` cannot tell that from a genuine
"nothing there", because for some queries an empty result really is the right
answer. The damage is downstream: `OnlineFeatureStore` caches it as `Served`
with `Degraded` false, so "this airport has no buildings" stands for the whole
session and the passing callouts go silent, while `OsmTaxiSource` reports a
successful fetch that adopted no names.

`overpass.osm.ch` (Swiss OSM association, Switzerland extract) was in the list
and was removed 2026-09-22 after being measured doing exactly that: EHAM's area
query, EHAM's `around:3000` fallback and KATL's taxiway query all 0 elements and
no remark, against LSZH's 77 — Zurich being inside its extract. Because it
answered in about a SECOND while the planet-wide mirrors were returning 504, the
cooldown map promoted it to FIRST for every later airport in the session. One
process, eight airports: KJFK 0, KATL 0 in 1.1 s, EGLL 0, KORD 0, OMDB 0,
LIRF 0, KTIW 0 — and LSZH 80. A fast wrong answer beats a slow right one every
time, which made it the worst possible member of that list. The pilot's own
`taxi-augment.log` showed `+osm=0 disagree=0` at every airport of that session
against `+osm=363 disagree=40` at OMDB eight days earlier — same airport, same
database.

Two defences, deliberately at different levels: the list no longer carries that
instance, and **`PostAsync` no longer BELIEVES an empty answer until ONE more
FRESH mirror has been asked, returned unconfirmed at once if none remain** —
if that one has elements, it had the region and the first did not — which
covers regional instances nobody has identified yet.
**One, never a sweep:** asking every remaining mirror, cooled-down ones
included, made a genuinely empty small field cost up to six round-trips, which
held the taxiway fetch's `Task.WhenAll` (and the apt.dat names that had already
landed) past the taxi dialog's bounded name wait. So a cooled-down mirror is
never asked just to confirm an empty, a confirmation that fails is not retried,
and with no fresh mirror left the held answer is returned at once. The list
holds planet-wide instances only (pinned by `OverpassRegionalMirrorTests`), so
the confirmation is the backstop, not the defence. The empty answer is then
believed: a strip with no mapped hangar, apron or tower is a real answer, and
failing it would have the store retry it every five minutes for the session.
A caller that gives up DURING the confirmation gets that held answer too, never
null: it is already a well-formed answer from a mirror that worked. For the
taxiway-name fetch that is its whole answer, so an airport OSM genuinely has
nothing for comes back empty rather than as a failed source. The BUILDINGS
fetch gains it only in its FALLBACK query: an AREA query that comes back empty
sends `OsmFeatureSource.FetchAsync` on to the fallback with the same,
already-cancelled token, and a `PostAsync` that has learned nothing still
returns null — so a cancel during the area query's confirmation is still a
failed fetch, remembered for `FailureMemory` and retried. That is right: an
empty area answer does not say the airport has no buildings (an aerodrome OSM
has not tagged `icao=` answers empty every time). A mirror answering empty is
**never blacklisted** for it: that is no evidence the mirror is ill.

**Neither defence touches a query string**, and none ever should: the one change
to a shipped Overpass query in this feature's history (`out tags geom center`)
cost every taxiway name at every airport.

### The OSM buildings query

**The buildings have their own request, store and event, and must never ride
the taxiway-name query again.** Fused into it, the feature cost that query
three ways, all measured:

- `out tags geom center` returned ways with **no geometry**. Overpass honours
  only the LAST geometry modifier, so adding `center` for the building centroids
  silently dropped the vertex arrays every OSM taxiway name is derived from.
  `OsmTaxiSource.BuildQuery` is byte-identical to its pre-feature text and ends
  `out tags geom;` (pinned character for character by a test). A building's
  position comes from its outline — the centroid, when it lies inside — or else
  the centre of its `bounds`, never from `center`; the building queries
  themselves end `out body geom;` (see below).
- A mirror with no area database answered the whole fused query with an empty
  HTTP 200, which was then cached as "this airport has no taxiway names".
  `OverpassClient` now treats a body whose `remark` starts "runtime error" as a
  FAILED mirror and rotates on.
- Taxiway names waited on a second round-trip before being returned.

**Neither `FetchAsync` may throw on a bad body.** `OverpassClient.ClassifyBody` passes
anything that is an object with an `elements` array and no "runtime error"
remark, which is not the same as being parseable: an element with no `type`, a
non-array `geometry`, a coordinate that is not a number. Before `OverpassClient`
was extracted, `Parse` ran inside `OsmTaxiSource`'s per-mirror try, so such a
body simply failed that mirror; extracted, it threw out of `FetchAsync` into
`Task.WhenAll` in `AugmentingAirportDataProvider.FetchCoreAsync` and discarded
the SUCCESSFUL apt.dat result together with the cache write, the name merge and
`AirportDataUpdated` — for pilots who never use the surroundings feature at all.
Both sources now catch, `Log.Warn` once with the ICAO, and return null, which is
what "this source failed" has always meant on both paths. **That is true of the
RETURN VALUE, not of the mirror rotation**, and the difference is deliberate: on
`main` an unparseable body marked THAT MIRROR failed and the next one was tried,
whereas the catch is now outside the mirror loop, so the source gives up with no
retry and no cooldown mark. Rotating would buy nothing — a body shape that
breaks `Parse` is a protocol-level change every mirror shares, not one mirror
being ill — and the apt.dat result surviving is the whole point of the fix.

`OsmFeatureSource.BuildAreaQuery` asks the `aeroway=aerodrome` area carrying the
airport's `icao=` tag. When OSM has not tagged that area — which is common at
smaller fields — `BuildFallbackQuery` reruns without the generic named-building
clauses at `around:3000`, and `KeepInsideBox` keeps only what falls inside the
navdata airport box grown `FallbackBoxMarginMetres` (500 m; the box is the exact
hull of the airport's own records, so at KTIW its own control tower sits 15 m
outside it). **With no box at all the fallback result is dropped entirely** —
fail closed, because a filling station on the road outside the field must never
become "Fuel, ahead". Every embedded coordinate is `InvariantCulture`-formatted:
`.` in a custom numeric format is the decimal-point PLACEHOLDER, so a
comma-decimal locale would emit `around:3000,47,2679,-122,5781`, which every
mirror answers 400 to.

**Both building queries end `out body geom;`, never `out tags geom;`.** The
`tags` verbosity prints ids and tags only — no coordinates, no members — and
`geom` puts coordinates back for nodes and ways but has nothing to hang a
relation's geometry on. So under `out tags geom;` a multipolygon RELATION (a
terminal with a courtyard, an apron whose edge is split across several ways)
arrived as type, id, `bounds` and tags alone — measured live 2026-09-22 against
KATL relation 10189710, "Domestic Terminal" — and could only be measured to the
centre of its bounding box; an apron mapped that way could never contain the
aircraft. Under `body` each way member carries its own `geometry` array, and
`OsmFeatureClassifier` joins the members whose role is `outer`
(`OsmRingAssembler.LargestRing`): open ways end to end, a way whose END meets
the chain walked backwards; a way closed on its own is a ring as it stands and
never joins another; a chain a way's END brings back to a node it already
passed — two outer rings touching there, which OSM allows — splits into two
rings rather than running on as a figure-eight whose shape would depend on
member order; a chain that never closes is dropped. The LARGEST ring is the
footprint; inner ways (courtyards) never count. A way or member with a gap in
its geometry is never joined across it, and no ring at all falls back to the
bounds centre, as before. It is still ONE output statement with ONE geometry
modifier (`body` is a verbosity, not a geometry modifier); the only thing
`body` adds to a way is a `nodes` array of node ids, which no parser reads, and
a node is unchanged. The TAXIWAY query keeps `out tags geom;` byte for byte.

`OnlineFeatureStore` is the tier's cache: per ICAO, in memory only, one fetch in
flight per airport. `BuildSurroundings` STARTS the fetch before its other tiers
(`Prefetch`, which returns at once) and asks `GetAsync` for the answer after the
scenery tier, waiting only for what is left of `CatalogWait` (3 s from the
prefetch; `RemainingWait`, never negative) — so the catalog includes the
buildings when the mirror is quick, or merely answered while the scenery package
was being read, and builds without them when it is not; `FeaturesUpdated` then
invalidates that catalog once the fetch lands. `Prefetch` arms NO
`FeaturesUpdated`: the event is owed only to a caller that GAVE UP waiting, and
armed at the prefetch, an answer landing during the scenery scan would
invalidate — and so discard — the very build about to include it.

- **A failed fetch is not an empty airport.** Every mirror refusing means
  `FetchAsync` returns null, which is remembered for `FailureMemory` (5 minutes)
  and retried. Caching the empty list instead would silence an untagged
  aerodrome — the one that takes the fallback path every time — for the whole
  session. A fallback that ANSWERS with zero elements is an empty, non-null list
  and is cached as such.
- **"Retried" is only true end to end because of the degraded lifetime.**
  `GetAsync` reports an `OnlineFeatureStatus` alongside the features —
  `Served` (a mirror answered, with buildings or with the fact that there are
  none), `Pending` (the caller's wait ran out; the fetch runs on), `Failed`,
  `Disabled` — and `BuildSurroundings` marks the catalog `Degraded` on the
  middle two, so `SurroundingsCatalogCache` expires it after
  `DegradedLifetime` and the next `GetAsync` asks the store again. Without
  that, a null fetch raised no `FeaturesUpdated` (the continuation requires a
  non-empty result), nothing invalidated the airport, and the careful
  null-versus-empty distinction above bought nothing: the OSM-less catalog was
  simply the catalog for the session. A late fetch that SUCCEEDS still
  invalidates at once through `FeaturesUpdated`, unchanged. The status is what
  the caller needs and cannot derive: an empty list is the honest answer for an
  airport with no mapped buildings AND the answer when nobody replied.
- **`Clear()` runs on a database switch**, beside `surroundingsCache.Clear()`,
  because the box a fallback result was filtered against comes from the navdata
  database. It bumps an epoch and drops in-flight entries; a fetch that started
  before the `Clear()` writes nothing back and reports nothing.
- The give-up is `task.WaitAsync(maxWait)`, never
  `WhenAny(task, Task.Delay(…))` — the house rule; the latter arms a timer
  nothing cancels when the fetch wins.

### The scenery tier — readers, clutter nets and caches

**Record layout** (`BglPlacementReader`, measured 2026-09-06 on Orbx KTIW,
imaginesim KATL and Axonos KJAC; the 92-byte variant 2026-09-20). A BGL opens
with magic `0x19920201` and a `0x38`-byte header whose section count sits at
`+0x14`; the section table that follows is 20-byte entries, and only the
SceneryObject section (`0x25`) matters. Its entry gives a subsection count,
offset and size; each subsection entry ends with the offset and size of its
placement data. Inside that data, records are `[id:u16][size:u16]` and a
LibraryObject is id `0x0B`: longitude at `+4`, latitude at `+8`, heading at
`+22`, and **the model GUID is the 16 bytes immediately before the trailing
4-byte scale field, i.e. at `size − 20`**. Two layouts are in the wild — the
classic 64-byte record (GUID at `+44`) and the 92-byte record the MSFS 2024 SDK
writes (GUID at `+72`, where `+44` holds the latitude as a double instead), so a
reader fixed at `+44` reads garbage from the newer one: it resolved 0 of 7,557
placements at iniBuilds LMML. `size − 20` is right for both. Measured across
~35 Community packages, 33 use 64-byte records and 2 (iniBuilds LMML, Glideslope
KMEM) use 92-byte ones.

**Neither reader loads a whole file, and there is no size cap.**
`BglPlacementReader.Read(Stream)` seeks the header, the section table and the
SceneryObject subsections only; `ModelLibNameReader` streams the file looking
for the ASCII bytes `<ModelInfo` and decodes only each tag (≤ 1 KB). The old
whole-file Latin-1 string cost ~3× the file size and forced a 600 MB skip that
dropped the model library — every name — of ten real airport packages. Both are
bounds-checked at every step: a truncated or foreign file yields whatever parsed
cleanly, never an exception. `BglPlacementReader` is additionally bounded by a
CUMULATIVE byte budget per call (`DefaultMaxTotalBytes`, 128 MB), not only per
buffer — a corrupt or hostile file can declare millions of small, individually
in-bounds entries that all point at one region, which would otherwise refill a
64 MB buffer millions of times.

**One parser.** `BglPlacementReader`'s `ReadOnlySpan<byte>` overloads DELEGATE
to the stream ones and exist for the tests. As a second implementation they had
already drifted: they clamped a truncated entry to the file where the stream
reader rejects that entry whole, and they had no per-subsection cap — so tests
written against them were pinning a parser no production caller runs. Never
throwing is not the same as always finishing, either, which is why
`Read(stream, out bool complete)` exists; see the persist rule below.

**Two clutter nets, and both are needed.** No word list can tell a baggage
dolly named after the cargo ramp it serves from a building; no structural rule
can tell a 46-part terminal from a 41-container blob, because both are one dense
cluster.

- *Lexical*, in `SceneryModelNameClassifier`: a stop word ("interior",
  "dolly", "container", "tug"…), a stop-list phrase (fences, lights, vehicles,
  jetways…), or no kind word at all condemns the model. Every rule is pinned by
  a measured package name in `SceneryModelNameClassifierTests` — **extend that
  table first**. Every regex is `static readonly` + `CultureInvariant` (the
  tr-TR dotless-i trap) and none is built per call. The kind table takes its
  concourse, FBO and cargo words from the shared `FeatureLexicon` and tests
  **Fbo and Cargo BEFORE Terminal**, exactly as `OsmFeatureClassifier.TerminalKind`
  and `GsxTerminalFeatureSource.KindOf` do: the catalog never merges across
  kinds, so a "Cargo Terminal" read as Terminal here and Cargo from OSM is one
  building listed twice under two kinds. Hangar stays first — "Narrows Aviation
  Hangar" is a hangar, not an FBO. `ConcoursePierSatelliteTerminal`, which finds
  the keyword TOKEN the spoken name is built from, must carry every word
  `FeatureLexicon.Concourse` matches plus "terminal": a name the kind table
  accepts and that regex does not is classified and then dropped. That pairing
  is pinned by `Every_concourse_word_the_shared_lexicon_knows_can_still_be_named`,
  which reads the LIVE lexicon pattern and runs each word through `Classify`, so
  a word added to the lexicon tomorrow is covered without anyone remembering the
  test exists.
- *Structural*, in `SceneryPackageIndexer`: a name scattered over many separate
  clusters is ground equipment. The cap is picked by kind first —
  `MaxClustersHangar` 40 / `MaxPlacementsHangar` 200 for hangars whatever their
  name (a real field has dozens), else `MaxClustersGeneric` 8 /
  `MaxPlacementsGeneric` 40 for a generic label and `MaxClustersProper` 3 /
  `MaxPlacementsProper` 12 for a proper one. A real building becomes one
  feature **per spatial cluster**, never a package-wide average — that put MK
  Studios BIKF's seven "DS Hangar" buildings, 2.3 km apart, at a single phantom
  point between them.

**`FeatureKind.Terminal` and `FeatureKind.Concourse` are exempt from the
PLACEMENT cap** (`PlacementCapApplies`), and the exemption is **by kind** on
purpose. Authors routinely model a terminal as dozens of separate parts standing
in one place, which the classifier deliberately collapses onto one name — so
counting them as "too many placements" threw away the building a pilot most
wants named. Measured across 34 Community packages: NO terminal or concourse
group was clutter, while the cap had dropped EDDB's "Terminal A"/"B"/"C"
(46/49/49 parts, one cluster each) and its generic "Terminal" (175 parts in one
764 m cluster), KPHX "Terminal L" (21 parts at a single coordinate) and KATL
"Terminal E". Every group the cap legitimately removed was ground equipment
(EIDW's 41 containers in one 252 m blob, KPDX's 130 "Ramp Cargo Fedex", KMEM's
40 "Trailer UPS", ENGM's five "Ground Fuel N" fleets). The CLUSTER cap still
applies to every kind, and EGSS's real 4-cluster "Inflite Jet Centre" is an
accepted, recorded residual. Same measurement, end to end: 308,833 placements →
837 features before the clutter rule → 533 after.

**The cache stores RAW placements (schema 3).** Each model name that could name
a feature, with every point it was placed at — nothing in the cache is
classified. So the airport that ASKS decides the names ("KPWT_Hangar_07" is
Hangar 7 at KPWT and somebody else's building at KTIW), and a change to HOW a
name classifies reaches a pilot whose cache is already warm. One axis is not
free that way: which names are cached is `MightBeFeature`'s verdict at BUILD
time, so **widening the classifier's kind keywords needs a
`CurrentSchemaVersion` bump**. **2 → 3 is that rule firing for the first time:**
moving the classifier's Concourse leg onto the shared `FeatureLexicon.Concourse`
taught it "flugsteig", which the private copy it replaced did not know — so
every schema-2 cache was built with each Flugsteig model already filtered out,
and only a bump can get it back. A document of the CURRENT schema with no
`Models` key at all deserialises to null and is rebuilt; `"Models":[]` is a real
answer and is believed; a document of ANY older schema is rebuilt whole, never
partly believed. Written to a `.tmp` and moved into place, so a crash never
leaves a truncated cache to be read as a package with fewer buildings.

**Which package.** `SceneryPackageLocator` returns the folders
`airport.scenery_local_path` names — never the whole Community tree. An MSFS
2024 navdata build records that column for NO airport, so `SceneryPackageCensus`
answers instead, from where each package's objects stand: a HEADER-ONLY pass per
BGL (section table and placement subsections, never a model library's bulk),
counting placements into 0.005° cells, disk-cached per package on `layout.json`'s
length and mtime. A package scores by the cells that reach the airport box grown
300 m, and needs `MinPlacementsInBox` (20) to count — below that is a livery's
hangar, a city pack's edge, or one static aircraft on the ramp. **Community
only**; Official/OneStore is never scanned. Measured on a real Community folder:
40 scenery packages of 88 (the other 48 carry no `layout.json`, or a
`manifest.json` naming another `content_type`, and no BGL of theirs is opened at
all), 2,443 BGLs, 21.3 MB read, 2.58 s cold and 16 ms warm, and it
found the right package at KATL, EGLL, KJFK, LMML, EDDF, KSEA, EHAM
(`flytampa-amsterdam` — no ICAO in its folder name), OMDB, OMDU, EGSS and KMEM,
and correctly NONE at KTIW and KSNA. The census also runs on an MSFS 2020
database whenever navdata names no package for the airport.

**A row that names nothing is dropped at LOAD, and an incomplete scan is NEVER
PERSISTED — by the census AND by the indexer.** The two caches carry the same
two rules for the same two reasons. A hand-edited or half-corrupted document can
be valid JSON and still hold a census row with no `Path`, or an indexer
`"Models":[null]`; indexed straight it threw out of the build and cost the pilot
the whole catalog, on EVERY call, because the document is memoised. And a file
that could not be read is a MOMENT — an exclusive lock, an antivirus sweep, a
package being updated — not a property of the package: cached, its short answer
is frozen under `layout.json`'s stamp until the package is next updated. For the
census that hides the package; for the indexer it is worse, because every
placement in the unread file resolves to "without a model name", so the package
yields no features at all and reads exactly like an airport with no buildings.
Both serve what they DID read for that call, and the indexer's status line now
carries `, 1 file unreadable` / `, 3 files unreadable` (pluralised, because a
screen reader speaks it) — the only sign a pilot gets. Two causes count:
a file that could not be OPENED, and a read a TRANSIENT I/O error cut halfway,
which `BglPlacementReader.Read(stream, out bool complete)` reports (it never
throws, so nothing else could see it). It reports `IOException` and
`ObjectDisposedException` ONLY: a malformed file, an out-of-bounds entry and a
spent cumulative byte budget are DETERMINISTIC, so they stay cacheable — calling
them incomplete would re-scan that package for the life of the install. The
indexer additionally MEMOISES an incomplete scan for
`SceneryPackageIndexer.IncompleteMemoLifetime` (5 minutes), so a 600 MB model
library is not re-read on every call, and gives the memo up afterwards so the
condition cannot outlive itself.

Both enumerations use the SAME
`EnumerationOptions { RecurseSubdirectories, IgnoreInaccessible,
MatchCasing.CaseInsensitive, AttributesToSkip = 0, MaxRecursionDepth =
SceneryPackageCensus.MaxBglRecursionDepth }`. `IgnoreInaccessible`
because the `SearchOption` overload throws from the ENUMERATOR, outside the
per-file catch; `CaseInsensitive` because packages ship both `modelLib.BGL` and
`objects.bgl`; **`AttributesToSkip = 0` deliberately** — the default skips
Hidden and System files, and reparse points must be FOLLOWED, because add-on
linker tools put whole Community packages behind junctions and a census that
skipped them would find nothing for exactly the pilots with the most scenery —
and the depth bound (12; the deepest real BGL measured sits 4 levels down) is
therefore what ends a junction cycle. It must be on BOTH: the indexer is handed
a package the census found in Community, so bounding the walk in one and not the
other simply moves the cycle.

`MsfsPackagesLocator` is the one resolver of `InstalledPackagesPath`
FOR THE NAVDATA BUILD AND THE CENSUS (four locations, two per simulator;
`NavdataReaderBuilder` delegates to it) — `EFBModPackageManager`,
`AircraftCfgCatalog` and `GsxAirplaneProfile` each still parse `UserCfg.opt`
themselves. It opens the file `FileShare.ReadWrite | FileShare.Delete` and
closes it before its first `Directory.Exists`: it is the SIMULATOR's own
config, and since the census it is read while the simulator is running, where
a reader that permits no writer can make the simulator's own write fail. A
config that EXISTS but cannot be READ — the simulator holding it exclusively
for a moment, an access error — is reported apart from "nothing to read"
(`TryGetCommunityPath`'s `readFailed`), and `BuildSurroundings` marks the
scenery tier SHORT on it, so the catalog is degraded and built again after its
lifetime instead of standing as an airport with no scenery package: a read
that failed says nothing about whether the package is there. A config that
is absent, names no path, names a folder that is not on disk, or a packages
root with no Community folder is not a failure — nothing is owed, and a
rebuild would only find the same absence. Its `IndexOf` match also matches
`InstalledPackagesPathNextBoot`, and that is PRESERVED deliberately rather
than fixed: the navdata database build has always resolved its base path
this way, and changing it is a behaviour change that belongs in its own commit.

### Settings & caching

| Setting | Default | Panel |
|---|---|---|
| `SurroundingsCalloutsEnabled` | off | Taxi Guidance |
| `SceneryIndexEnabled` | on | Taxi Guidance, with a read-only status TextBox — `"{icao}: {n} features from {package} ({n} placements, {n} without a model name)"`, plus `", 1 file unreadable"` / `", 3 files unreadable"` when the scan was short (that scan is not cached) and `" (located by Community scan)"` when the census found the package |
| OSM feature tags | rides the existing `TaxiAugmentEnabled` opt-in | — |

The scenery index is disk-cached under
`%APPDATA%\MSFSBlindAssist\scenery-index\<leaf>-<hash8>.json`, keyed on the
package's `layout.json` length + mtime (rebuilt when either changes) plus the
schema version, and hashed from the FULL package path so two installs sharing a
leaf folder name never share a cache file. The census shares that folder as
`census.json`. This is the user's own local package, so a disk cache raises none
of the licensing questions OSM data does. OSM feature data itself gets **no**
disk cache and stays in memory for the session, same as every other OSM datum
this app fetches.

### Rejected: base-library (Asobo) model-name index

A spike indexed 6,613 BGLs / 1,621 model names from the Official `fs-base*`
packages in ~10 s, to try to resolve the roughly-half of KATL/KJAC placements
that reference the base library by GUID. Of the unresolved placements at
KATL (16), KJAC (121) and KTIW (80), **zero** classify as a building — Asobo's
base library holds no generic airport buildings, only world landmarks
(a telecom tower, a cargo ship, military vehicles). One fact recorded for anyone
re-attempting this: the Asobo libraries are named `Asobo_*.BGL`, not
`modelLib*.bgl`. (The spike's second finding — that two of them are 1.2–1.4 GB,
past what a whole-file read can hold — no longer applies: `ModelLibNameReader`
streams, and the 600 MB skip that went with the whole-file read was removed
because it dropped the model library of ten real Community packages.)

### Invariants

- Surroundings features are **readout only** — never handed to
  `TaxiGraph.Build`, never a node, never a routing/hold-short input. The ONE
  way a place becomes a destination is `PlaceListBuilder`, which resolves it BY
  POSITION onto a selectable stand within 150 m, else a navdata-only stand,
  else a taxi node within 100 m that is neither a hold-short nor on runway
  pavement — the route target is the stand or node, never the building, and
  never a `(Name, Number, Suffix)` join.
- OSM buildings have their **own** request, store and event; never fuse them
  back into the taxiway-name query. OSM data stays in memory; only the scenery
  index (the user's own local files) is disk-cached.
- The OSM query is scoped to the aerodrome AREA, and the `around:3000` fallback
  is box-filtered — with no box it is dropped entirely. A bare radius admits
  road filling stations.
- Feature identity is the NAME **and** the distance together; a navdata
  concourse yields to the GSX feature built from the same stands. Geometry is
  donated in a merge only where it DESCRIBES the winner: a winner with
  `Members` never takes a ring; an unnamed ring and an apron/de-ice stand
  cluster are different features; two unnamed rings are one body only by
  containment; and a cluster with a member further out than
  `SameNameRadiusMetres` is a different feature, not a silently consumed one.
- The zone is the pavement the aircraft is ON — a named apron outline, else the
  ramp whose stands it is among, else any outline, else the nearest
  concourse/terminal — and what it is standing on is never ALSO offered as
  nearby. Under a ground zone the only ground left unsaid is an apron with NO
  name (that ramp's own pavement) and anything carrying the zone's own name; a
  synthesized "North ramp", a de-ice pad and every named place still speak, and
  with no ground zone an unnamed apron in range speaks as before. Nothing at
  zero range is given a direction: "{name}, here.".
- A model name is spoken only after `SceneryModelNameClassifier` has produced
  human text; raw `KTIW_*` / `concourse_a_02` strings never reach speech.
- Passing callouts are queued, fire at the closest point of approach (never one
  reached while stopped or at zero range) with no baseline, are frozen at arm
  time, are given up on after `PendingExpiry`, are
  re-baselined (never `Reset`) when the catalog instance changes, and are
  silent on runway pavement as well as under every guidance phase that already
  speaks. The runway probe keeps its shapes per AIRPORT so the taxiway-name
  fetch cannot blind it, and warms them from the runway rows alone, so it also
  answers at the airports that have no taxi paths.
- The catalog is never built on the UI thread, and neither background job a
  monitor tick can start may run during a rollout.
- The scenery scan opens only the packages `scenery_local_path` names, or the
  ones the Community census identified — never Official/OneStore, and never on
  the UI thread or a position update.

## Taxi Assist Form (route entry)

Opened via Input > `Shift+Y`. Tab order mirrors the way ATC says a clearance.

1. **Airport ICAO** — text input, auto-filled on open with the airport the aircraft is AT (`CurrentAirport.Resolve`, the answer Where Am I speaks); idents of any length.
2. **Destination type** — combo box: `Runway` or `Gate / Parking`.
3. **Destination** — combo box: list of runways or parking spots for the chosen airport, sorted by distance from current position.
4. **First taxiway** — combo box: all taxiways touching the origin node, sorted nearest-first. `(None - calculate shortest path)` entry allows unconstrained routing.
5. **Hold short after this taxiway?** — checkbox next to each taxiway combo. Checking it inserts a hold-short stop at the end of that leg.
6. **Add taxiway** — button spawns another taxiway combo, filtered to only show taxiways that actually connect to the previous selection. Up to 20 additional taxiways.
7. **Calculate & start** — validates, builds the route, announces the summary, hands off to `TaxiGuidanceManager.StartGuidance()`.

**Why ComboBoxes instead of a text field?** Eliminates tokenization bugs with multi-word taxiway names (`LINK 53`, `HAWKER`, etc.). What the DB exposes is what the user can pick. The connection-filtered dropdown also makes typos impossible.

**Origin = current position.** The form does not ask for an origin. The router snaps the aircraft's current lat/lon to the nearest graph node, so guidance can resume from mid-taxiway, a runway exit, or anywhere else — not just from a gate.

**Aircraft position is refreshed at Calculate time.** `OnCalculateClicked` reads `_simConnectManager.LastKnownPosition` immediately before route construction. Without this refresh the route would start from where the aircraft was when the form was OPENED (typically pre-pushback at the gate), and the post-pushback aircraft would already be off-route from the very first frame — the off-route detector fires and recalcs within seconds. Critical for the open-form-then-pushback workflow.

**Mnemonics (every Alt+letter unique on the form):**
- `Alt+A` Airport (ICAO), `Alt+T` Destination type, `Alt+E` D**e**stination, `Alt+F` First taxiway
- `Alt+H` Hold-short checkbox (cycles across all instances)
- `Alt+O` Hold short **o**f runway combo (cycles across first row + every dynamic row)
- `Alt+D` Add Taxiway, `Alt+C` Calculate Route, `Alt+S` Stop Guidance
- `Alt+R` Remove (cycles across all dynamic Remove buttons)
- `Alt+2` .. `Alt+9` jump to Taxiway 2..9 combos (no mnemonic past 9; rare)

**Per-row "Hold short of runway" picker** lets the user explicitly annotate an ATC-instructed runway hold-short between this taxiway and the next. Defaults to `(none)`; the dropdown lists every non-closed runway at the airport. When set, `ApplyUserRunwayHoldShorts` (called from `TaxiGuidanceManager.LoadRoute`) honours the pick when the route enters or crosses that runway at or after this taxiway, placing the hold with the same resolver as the automatic pass (see "Runway crossings and entries"). If the route neither enters nor crosses the requested runway after this taxiway, or does but has no safe place to hold short of it, a warning is appended to the route summary announcement so the pilot knows the explicit pick could not be set — the route still loads. The automatic runway hold pass runs after this and keeps the pilot's label at a stop the pick already took.

**Tab order** is set explicitly: `txtAirport → cmbDestType → cmbDestination → cmbFirstTaxiway → chkFirstHoldShort → btnAddTaxiway → pnlTaxiways → btnCalculate → btnStop`. The `pnlTaxiways` panel needs `TabStop = true` and an explicit `TabIndex` *between* Add and Calculate; without that, its inner controls land at the very end of the form's tab order (after Stop), so Tab from Add jumped past every newly-added taxiway straight to Calculate. Each new dynamic group's Combo / Hold-short / Remove gets sequential tab indices inside the panel as it is added.

## Taxi Guidance Options Form

Opened from the File menu. User-tunable settings:

- **Steering tone waveform** — Sine (default), Square, Triangle, Sawtooth. Picks up from `HandFlyWaveType` to share the hand-fly tone palette.
- **Steering tone volume** — slider, default 0.05 (quiet — intended to sit under ATC chatter).
- **Announce taxiway crossings** — checkbox, default on. Turns off the ~150 ft / ~50 m "Crossing taxiway X" callouts for pilots who find them chatty.
- **Ground speed announcements** — off / 5 kt / 10 kt. Periodic ground-speed bucket callout during all on-ground phases.

All settings persist through `UserSettings`.

### Distance units

`UserSettings.GroundDistanceUnit` (enum `DistanceUnit.Metres` / `DistanceUnit.Feet`, default **Metres**). Controlled by the **"Use feet for distances"** checkbox in the Taxi Guidance Options Form.

**What it governs — every user-facing horizontal ground-distance readout:**

- Taxi turn advance-notice and "turn now" callouts (`"In 100 metres, turn left…"` vs `"In 300 feet, turn left…"`)
- Hold-short countdowns (speed-proportional triggers; the **live** distance is spoken, unit-aware via `DistanceFormatter.FromFeet` — no milestone table)
- Parking / gate arrival countdowns (15/10/5 m **or** 50/20/10 ft; via `DistanceMilestones.ParkingArrival`)
- Landing-exit approach callouts (500/300/150 m **or** 1500/900/500 ft; via `DistanceMilestones.ExitApproach`)
- Runway-end countdown (500/150/30 m **or** 1500/500/100 ft; via `DistanceMilestones.RunwayEnd`)
- On-demand status distances (`"In 400 metres turn right onto Kilo. 1.5 kilometres to destination."`) — totals over ~6000 ft switch to the big unit matching the setting: kilometres in metres mode, nautical miles in feet mode
- Touchdown callouts (`"Touchdown. High-speed exit K2 in 1 800 metres."`)
- Landing exit and runway combo-box labels (`LandingExit.ToString()`, `Runway.ToString()` — short form, e.g. `"K2 — 550 m from threshold"` or `"K2 — 1 800 ft from threshold"`)
- Ground-traffic alerts (`"Slow down, traffic ahead, 150 metres."` vs `"… 500 feet."`)
- Backtracking distance readout in on-demand status

**What it does NOT govern (intentionally out of scope):**

- Altitude callouts (always feet — aviation standard, ICAO Annex 5)
- AGL / vertical-speed callouts (always feet)
- Takeoff-assist / visual-guidance announcements (altitude/AGL — always feet)
- Weather visibility (reported in km / statute miles per METAR convention)
- Internal guidance thresholds — all geometry stays metric; the unit setting is a **display-layer-only** conversion applied at announcement time via `DistanceFormatter` and the unit-native `DistanceMilestones` tables.

**Why Metres is the default:**

ICAO Annex 5 and most non-US airspace use metric for all ground-distance callouts (taxi, RVR, taxiway widths). GSX Pro's gate data uses metres. Feet is the US aviation standard and is available via the checkbox for pilots who prefer it.

**Implementation:** `DistanceFormatter.UnitProvider` is wired to `() => SettingsManager.Current.GroundDistanceUnit` at app startup in `MainForm`. A setting change takes effect on the next callout — no restart required.

## Landing Exit Planner

Before touchdown (during cruise or descent), the pilot picks a runway-exit taxiway. When the aircraft lands, taxi guidance **auto-activates** from the touchdown point to that exit — no scrambling to open a form while decelerating on the rollout.

**Opened via:** Input > `Shift+X`. If an ILS destination runway is already set (from the existing ILS destination selection UI), both the ICAO and runway are pre-filled — the pilot only has to pick the exit. With no ILS destination, the loaded flight plan's arrival airport and runway are pre-filled when it names both (`LandingExitPlannerPreset`); before that the runway box fell back to the airport's first runway, the default issue #234's wrong-runway plan came from. No duplicate runway selection UI.

### Flow

1. Pilot picks destination runway for ILS guidance (existing feature). Alternatively, types ICAO + selects runway directly in the Landing Exit Planner.
2. Planner calls `TaxiGraph.GetLandingExits(runway)` → a distance-sorted list of exit taxiways with classification:
   - **High-speed** — RET-geometry angle ≤ 50°, supports higher rollout speeds.
   - **Normal** — standard perpendicular exit (≤ 110°).
   - **End** — end-of-runway turnoff (near last 15% of runway length).
   - Each exit has its taxiway name, distance from threshold, distance from the 1000-ft touchdown aim point, and the graph node id to route to.
3. Pilot picks an exit, presses `Plan Exit`. The planner stores the selection plus the pre-built `TaxiGraph` (so activation doesn't need to rebuild it).
4. `LandingExitPlanner.ProcessGroundState(onGround, gs, lat, lon, headingTrue)` is fed from every `SIM_ON_GROUND` update in `MainForm.OnSimVarUpdated`. It edge-detects the airborne→on-ground transition and requires ground speed ≥ 40 kt (`LANDING_MIN_GS_KNOTS`) to count as a real touchdown — a teleport or reload at low speed won't trigger it.
5. On touchdown it first checks which runway the aircraft is on (`LandingRunwayMatch`; another runway or the other end is re-planned — see **The landing-exit plan's runway is checked against the runway actually landed on**), then calls `TaxiGuidanceManager.LoadRoute(...)` with the exit node id as the destination, `isRunwayDestination: false`, and the pre-built graph as `prebuiltGraph`. Then `StartGuidance(SettingsManager.Current)`. The route snaps from the aircraft's current (post-touchdown) position through the exit's graph node — shortest path, so it naturally follows the runway centerline until the chosen exit.
6. Announcement: "Touchdown. High-speed exit taxiway K2 in 1800 metres." (metres mode) / "…in 5800 feet." (feet mode). The exit class ("high-speed exit", "exit", "runway-end exit") and unit are determined at runtime from the exit geometry and the user's distance-unit setting (`DistanceFormatter.FromFeet`).
7. **Rollout phase (`TaxiGuidanceState.LandingRollout`).** The steering tone runs in three modes, selected every frame by `RolloutExitGate.SelectToneMode`: **Silent** above `ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS` (50 kt) — a pan cue means nothing at runway speed; **DriftCorrection** below that speed and beyond `ROLLOUT_EXIT_TONE_ARM_FT` (300 ft) of the exit — desired heading is the runway heading itself, steering the pilot back onto the runway heading through the long deceleration that used to be silent. It is a HEADING cue only — there is no cross-track term, so an aircraft that drifts and then re-aligns goes quiet while still laterally displaced, tracking parallel; that is deliberate (a constant offset is not closing on the edge, and a cross-track term would fight the exit turn), so do not describe this mode as steering back to the centerline. And **ExitBearing** inside 300 ft — desired heading is the bearing to the exit junction (or `ExitBearingTrue` once the "turn now" callout has fired for a Normal exit). The drift band carries one exception that makes a fourth outcome: inside `RolloutExitGate.TurnWindowFeet` (1,000 ft) of the exit, a deviation of at least `DriftToneSilentDeg` (2°) toward a KNOWN exit side returns **Silent** instead of DriftCorrection — below the 15° `turnBegun` threshold the two are indistinguishable to a heading test, and silence beats a tone that opposes a turn `IsExitTurnBegun` is about to accept. A known side is required (`HasKnownExitSide`), so where `ExitBearingTrue` is unset the drift tone keeps working. Voice callouts at 1500 ft / 500 ft / turn-now mark approach to the chosen exit throughout, independent of tone mode. Two transitions out of this phase:

   - **Normal handoff to `Taxiing`** fires when EITHER the pilot has begun the turn off the runway, OR BOTH (a) the aircraft is at taxi speed (`< 30 kt`) AND (b) is within 500 ft (`ROLLOUT_NEAR_EXIT_FT`) of the exit — plus a few narrower signals (lateral departure from the runway, exit-bearing alignment, a full stop short of the exit) that catch shallow exits and undershoots; see `TaxiGuidanceManager.Rollout.cs` for the complete condition. "Begun the turn" is `RolloutExitGate.IsExitTurnBegun`: a heading deviation ≥ `ROLLOUT_TURN_BEGAN_HDG_DEG` (15°) off runway centerline that must now also be toward the exit's own side and begin within `RolloutExitGate.TurnWindowFeet` (1,000 ft) of the exit or past it — a bare 15° deviation anywhere on the runway is no longer enough; see the subsection below for why. The conjunctive gate on `nearExit` still prevents the tone from resuming early on long runways where GS drops below 30 kt thousands of feet upfield of the planned exit.

     Once a handoff signal fires, two more checks run before the pilot is committed to a re-route. An **early-vacate retarget** (`RolloutExitGate.MatchEarlyVacateExit`) — entered only when the aircraft is both laterally off the runway and more than `RolloutExitGate.VacatedShortAlongTrackFeet` (350 ft) short of the planned exit **measured along the runway** (`IsVacateAwayFromPlannedExit`, which also keeps the original straight-line `TurnWindowFeet` test for an aircraft that has driven far off to the side) — retargets to whichever exit the pilot actually left the runway at, or concludes guidance with a "left the runway short of X" closure if none matches. The along-track form is what makes the branch fire for a vacate onto a NEIGHBOURING exit: a straight-line 1,000 ft test read a neighbouring turnoff as the planned exit's own turn and re-routed to the exit the pilot had skipped. It is sound because the branch already requires the aircraft to be laterally clear — the exit-node corridor (`halfWidth + 15 m`) runs only 5 m past the runway-clear boundary (`halfWidth + 10 m`), so an aircraft off the pavement on its OWN exit can be at most `5 m / tan θ` short of that exit's node, where θ is the **aircraft's own track angle** away from the axis (not any exit-angle constant — `GetLandingExits` enforces no minimum exit angle for hold-short-derived nodes): 313 ft at a 3° track, 61 ft at 15°. No measured spacing floor between distinct exits is claimed, and 350 must never be raised toward one — `TaxiGraph`'s coverage-gap sweep measures the far ends of RET arcs, i.e. the same physical turnoff, and the one real datum on close distinct exits is the closest same-name pair kept, EGLL 09R S4E at 433 ft. A **reachability guard** (`RolloutExitGate.IsHandoffRouteReachable`) then checks the resulting route's first segment is actually near the aircraft, and likewise concludes guidance rather than steering the tone at a taxiway the aircraft isn't on. That guard runs on **every** landing-exit handoff re-route, not just this one: the same check sits in `TryEarlyExitHandoff` (the ≤50 kt / within-300 ft high-speed path) and on the fallback path where the re-route failed and guidance would otherwise resume on the touchdown route — it is read at `_route.Segments[_currentSegmentIndex]`, the segment the tone is about to steer at, so the path that resumes and the path that concludes are judged by the same rule. Neither check ever falls back to routing at the originally planned exit once the aircraft has left the runway elsewhere — see the subsection below for why that matters.

   - **Overshoot retarget.** If the aircraft has rolled past the chosen exit by ≥ 100 ft (`ROLLOUT_OVERSHOOT_FT`) along the runway centerline without starting the turn, `TaxiGuidanceManager` scans the precomputed exit list for the next downfield exit and `LoadRoute`s to it in place via `RetargetLandingExit`. Approach callouts re-arm for the new exit. Announcement: *"Missed taxiway A6. Retargeting taxiway A7, N feet ahead."* If the precomputed list has nothing downfield, the manager asks the taxi graph directly (`TaxiGraph.FindDownfieldExits`) before giving up — see **Missed-exit rescue scan** below. Only when that also comes back empty does `EnterRunwayEndCountdown` clear the route (steering tone silent, no recalc) and announce *"Missed last exit on runway X."* before handing off to the runway-end countdown described below.

   - **Runway-end countdown.** When the overshoot path finds no downfield exit (or the retarget itself fails), or at touchdown a plan made for another runway finds no usable exit on the runway actually landed on, state stays in `LandingRollout` and `UpdateRunwayEndCountdown` drives three voice callouts as the aircraft approaches the physical end of the runway: *"Runway end in 1500 feet."* / *"Runway end in 500 feet. Slow down."* (suffix suppressed when GS ≤ 30 kt) / *"Runway end in 100 feet. Stop."* (suffix unconditional — the action cue fires regardless of current GS). Tone stays silent — the pilot is on rudder/brakes alone. The countdown ends by position (`RunwayEndCountdownGate`): *"Runway vacated…"* and `Taxiing` (with `_route = null` — no recalc target) once laterally clear of the runway; backtracking when STOPPED within `RolloutExitGate.NearRunwayEndFeet` (500 ft, a guidance constant of its own — not the spoken milestone, which moves with the pilot's feet/metres setting), or after turning around anywhere; one *"Stopped on runway X"* notice for a stop mid-runway. A TURN near the end is deliberately not a backtrack trigger: between 15° and 150° a turn onto the taxiway at the runway end and the start of a turnaround are indistinguishable, and `BacktrackingOnRunway` is a different state whose entry the *"Runway vacated"* arm can never take back — so a pilot leaving the runway correctly was told to turn around, with no way for guidance to correct itself. Distance-to-end comes from `RunwayFrame.DistanceToEnd`, so a `length`-0 runway row (the rows that always reach this countdown, since the exit finders skip them) falls back to threshold-to-threshold rather than counting down from zero. Full rules under **Runway-end countdown after a missed-last-exit** below. Replaces the previous "full silence" behavior so a blind pilot rolling toward the end of an active runway gets real braking information instead of being left to query Where-Am-I repeatedly.

### Rollout exit-turn gate, drift-correction tone & early-vacate handoff (KSEA 34L, 2026-08-21)

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
  hard pan. The drift band is not unconditional, though: inside `TurnWindowFeet` (1,000 ft)
  a deviation of at least `DriftToneSilentDeg` (2°) toward a KNOWN exit side
  (`HasKnownExitSide` + `IsTurnTowardExit`) goes Silent instead — between 2° and the 15°
  `TurnBegunHeadingDeg` a drift and the start of the exit turn look the same to a heading
  test, and an exit node can read up to 558 ft forward of its own pavement junction, so
  DriftCorrection there actively opposes the turn the gate is about to accept. The known-side
  requirement is what keeps the drift tone alive at airports where `ExitBearingTrue` is unset.
- After an early vacate the handoff must NEVER re-route to the planned exit. The taxi
  graph carries no runway edges, so A* routes between two exits the long way round: at
  KSEA that was 1,678 m up the parallel taxiway T and back down Z toward the runway.
  Retarget to the exit actually vacated at (`MatchEarlyVacateExit`) or conclude with the
  "left the runway short of X" closure.
- **A handoff route that RE-CROSSES the runway just landed on is refused at BOTH handoff
  sites** — the `UpdateLandingRollout` handoff and `TryEarlyExitHandoff` — by
  `Navigation/RolloutRunwayReCrossing`. Motivating defect, KATL 26R 2026-08-27: the
  aircraft rolled past its planned exit B1 (south side) without turning, the overshoot
  monitor retargeted to the only remaining exit A at 8,843 ft (NORTH side), and A* — with
  no runway edges to route along — built a 427 m horseshoe: off at B1 to the south, west
  along B, then back north on H across the 08L threshold, 15–20 m inside the runway's own
  threshold, at 22 kt. The pilot heard it as "very windy and curvy" and ended up on the
  wrong side of the field. `RolloutExitGate.IsHandoffRouteReachable` could not catch it: it
  measures only the FIRST segment's cross-track, and B1 started right at the aircraft.
  - **The test is the runway classifier** (`RunwayRouteClassifier`, see "Runway crossings
    and entries"): the route is refused when, from `_currentSegmentIndex`, it enters or
    crosses the landing runway. It judges which side of the runway the route is on before
    and after, so a route that starts on the runway and turns off meets nothing however
    close its exit junction sits to the centerline (the old per-edge intersection declined
    KORD 10R's W5 exit, whose junction is 29 cm over the line), and it is never a
    point-on-pavement test alone, which missed KBOS 33L.
    Reciprocal-aware through `RouteRunwayCrossings.NormalizeDesignator`, so 26R and 08L are
    one piece of pavement (and "8L" and "08L" one string). Judged from
    `_currentSegmentIndex` onward — the segment the tone is about to steer at — because a
    crossing already behind the aircraft is history, not a route it is about to fly.
  - **DECLINE only while still ON the runway with the exit AHEAD** — `signedAlongPastFt < 0`
    **and** `!offRunwayAtHandoff`. Both conjuncts are load-bearing: "keep rolling and the
    exit comes to you" is a claim about an aircraft on the pavement. Off it the overshoot
    detector cannot fire and `!pastExit` holds, so an `exitedLaterally`-triggered decline
    would repeat at the retry floor forever with no closure and no route. Everything else
    CONCLUDES.
  - **The conclude splits on WHERE THE AIRCRAFT IS.** Two of the three landing-exit
    closures order ahead of the still-on-runway one and both say "Stop and hold position",
    which is safe only off the pavement — and unlike the reachability guard (which can only
    conclude off-runway), `turnBegun` and `alignedWithExit` reach this one ON it. An
    on-runway conclude is therefore steered to `HandleArrival`'s final branch, "you may
    still be on the runway — continue ahead until clear".
  - **`TryEarlyExitHandoff`'s decline returns FALSE and must restore `LandingRollout`
    itself** — `LoadRoute` left the machine at `RouteLoaded`, so without the restore it is
    stranded with no tone and "Where am I" reporting no active route. It also discards the
    rejected route (`_route = null`), which is safe precisely because the rollout tone is
    driven from the exit BEARING and never reads `_route`. Its off-runway arm concludes and
    returns TRUE (the caller must stop processing the frame).
  - **The `UpdateLandingRollout` decline SPEAKS, once** —
    `RolloutRunwayReCrossing.ComposeContinueToExit`, e.g. *"Continue rolling to taxiway A,
    900 feet ahead."* It names the exit AND the distance, never says "stop" or "hold", and
    never claims the aircraft is clear of the runway. It is needed because "the rollout tone
    is a live cue" only holds inside `ExitToneArmFeet` (300 ft): beyond it a stopped, aligned
    aircraft gets either the 300–1,000 ft turn-window `Silent` or a sub-`DriftToneSilentDeg`
    `DriftCorrection`, which is zero volume — and `trulyStopped` carries no distance gate, so
    a pilot braking 1,500 ft short would sit stationary on an active runway with no tone and
    no words. Latched (`_rolloutCrossingDeclineAnnounced`), not floored: the decline itself
    repeats at `ROLLOUT_CROSSING_RETRY_FLOOR_SEC` (~1 Hz) and a sentence a second would be
    worse than silence. The latch is re-armed by `RetargetLandingExit` when the targeted exit
    actually changes, so a latch left over from an abandoned exit cannot swallow the new
    one's callout; the retry floor deliberately gets no such reset — a stale floor costs
    under a second, a stale latch withholds information from the pilot.
  - **That sentence is composed as ONE utterance with the callouts it supersedes** —
    `ComposeDeclineUtterance` + `DeclineSupersedesCallout`, never spoken beside them. The
    decline branch RETURNS before the approach/turn-now callout block, so none of that
    block's latches get set; on the very next frame (~16 ms) the crossing retry floor
    suppresses the handoff block, execution reaches the callouts, and one of them fires its
    own `AnnounceImmediate` over a sentence that is one-shot and therefore unrecoverable
    (Ctrl+Y replays the later callout, not this). The collision is STRUCTURAL, not a
    coincidence: `speedNearExitHandoff` requires `distToExitFeet < ROLLOUT_NEAR_EXIT_FT`
    (500 ft) and the 500 ft milestone triggers on that same boundary, so on any rollout
    already at taxi speed at 500 ft — the live 22 kt KATL trace included — both are true on
    ONE frame. This is the sixth instance of this codebase's
    two-announcements-stomp-each-other pattern (`4837e45d`, `6891c0e7`, `86744893`,
    `b772e845`, `c2b69455`) and the house remedy is the same every time: compose one
    utterance rather than let two race.
    - Every superseded APPROACH milestone is **retired, not folded**: each renders as
      "{exit name}, {round number}", and the decline sentence already gives that name with
      a LIVE distance. Only what they uniquely add rides along — the 500 ft cue's
      *"Slow down."* and, inside the turn window, the turn DIRECTION (composed by the one
      `ComposeExitTurnPhrase` the turn-now callout itself uses, so the two can never
      diverge). Milestones still further ahead stay armed and speak normally later, so a
      rolling pilot keeps the countdown.
    - The supersede window is a **time** (`ROLLOUT_DECLINE_CALLOUT_LEAD_SEC`, 4 s)
      converted to distance at the aircraft's own ground speed, not a tuned distance: it
      self-scales across the 0–90 kt range the decline branch spans, and a stopped aircraft
      supersedes nothing ahead of it (it can never reach the next trigger) so it keeps its
      whole countdown. Without the lead half, a decline at 1,501 ft would still be truncated
      — the 1,500 ft milestone is a fifth of a second away.
    - **Turn-now uses the STRICT inside test, no lead window**, because "now" is
      time-critical and a speed-derived lead would speak it up to ~600 ft out at the 90 kt
      `IsExitTurnBegun` permits. The accepted cost is a moving decline between
      `ROLLOUT_TURN_NOW_FT` and roughly twice it, where turn-now comes due mid-sentence and
      truncates it — the one truncation worth having, since *"Turn left now, taxiway A."*
      carries the exit name AND the more urgent instruction, so the pilot is never left
      silent or uninformed, which is the state the announcement exists to prevent.
    - The two alternatives were both worse. Setting the approach latch and saying nothing
      else throws away the *"Slow down."* advice and the turn direction. Suppressing the
      decline and letting the callout carry it throws away the instruction itself:
      *"Taxiway A, 500 feet. Slow down."* tells a pilot who has braked to a stop on an
      active runway neither that the exit is still ahead nor to keep rolling to it.
- `MatchEarlyVacateExit` must measure along-track PER EXIT, never against
  `DistanceFromThresholdFeet`. That field is measured from the LANDING threshold including
  `ThresholdOffset` (KJFK 13R 2,055 ft), while an aircraft's along-runway position is
  naturally measured from the physical runway start; comparing the two picks the wrong
  exit at every displaced-threshold runway.

### Exit detection math (TaxiGraph.GetLandingExits)

- Project each graph node onto the runway axis using equirectangular coordinates relative to the landing threshold, rotated by the runway's true heading.
- **Lateral tolerance** = runway half-width (runway.WidthFeet × 0.3048 / 2) + 15 m; fallback 75 ft half-width if width is missing.
- **Along-runway range:** from `MIN_DIST_FT` (500 ft) after the threshold up to the runway's far end, so we don't emit exits behind the aircraft at touchdown.
- **Touchdown aim point** used for the "distance from touchdown" column: 1000 ft past the threshold.
- Classification uses the angle between the runway axis and the exiting edge (`exitAngle`). Nodes in the last 15% of runway length are classified `End` regardless of angle.
- Dedup: exits within 50 ft that share a taxiway name are collapsed so a single connector doesn't list twice.

### Universal fallback: runways with no HS/IHS nodes

Many runways in real-world navdatareader DBs have **zero** hold-short or ILS-hold-short nodes — small airports, renumbered runways, certain third-party scenery, and notably every runway whose `taxi_path` rows just don't carry HS/IHS markers. Previously this produced an empty exit list with no feedback to the screen reader. Two fixes:

1. **Geometric implicit-exit fallback in `TaxiGraph.GetLandingExits`.** The method first sweeps the runway looking for any HS/IHS node (`hasHoldShortOnRunway`). If none are found, the second pass accepts `Normal` nodes as implicit exits when they have at least one **named taxiway edge whose bearing is ≥ `MIN_FALLBACK_EXIT_ANGLE_DEG = 20°` off the runway axis** (in either direction; folded to 0..90 so reciprocal headings count as parallel). The 20° threshold excludes parallel taxiways whose nodes happen to fall within the runway's lateral tolerance, while picking up real intersections. The downstream lateral / along-runway / dedup filters then cull anything that isn't actually on this runway. **This replaces an earlier check that required a runway-type edge (`PathType` starts with `R`) — that was effectively dead code, since no `taxi_path` row in the navdatareader schema has type R.** Without this geometric fallback, every runway lacking HS/IHS metadata silently returned zero exits.
2. **Screen reader announcement on empty list.** `LandingExitForm.RepopulateExits` calls `_announcer.Announce(...)` when the list is empty, so a blind user hears immediately that the runway has no exits in their DB ("update your navdata") rather than staring at a silent status label. A matching positive announcement fires when exits are found: `"N exit options for runway 09."`

If both the HS/IHS sweep and the geometric fallback return nothing, the announcement distinguishes a genuine data gap (tell the user) from a UI bug (no silent failures).

### Why this is safe

- The planner does **not** depend on runway detection from SimConnect at touchdown — many airports don't expose the landing runway ID on the ground. It checks the touchdown position and heading against the airport's runway geometry instead (`LandingRunwayMatch`): a landing on the planned runway routes to the chosen exit node by its lat/lon as before, and a landing on another runway or the other end is re-planned on the runway actually landed on (see **The landing-exit plan's runway is checked against the runway actually landed on**).
- The 40-kt threshold rejects taxi starts and teleport reloads.
- `_activatedThisLanding` is set once per touchdown — no double-firing if the ground bit bounces at decel.
- If no route is found (`LoadRoute` returns an error), the planner announces `"Landing exit guidance failed: <reason>"` and stays inactive. Normal taxi guidance can be manually requested via the Taxi Assist form.
- `Clear()` / the `Clear Plan` button fully drops the pending selection.

## Integration Points

### MainForm

- `MainForm.OnSimConnectData` feeds position updates into `taxiGuidanceManager.UpdatePosition(...)`.
- `HotkeyAction.TaxiAssist` → opens `TaxiAssistForm`.
- `HotkeyAction.TaxiStatus`, `TaxiRepeat`, `TaxiContinue`, `TaxiStop` → delegate to manager methods.
- `HotkeyAction.LandingExitPlanner` → opens `LandingExitForm`, pre-filling ICAO and runway from `simConnectManager.GetDestinationAirport()` / `GetDestinationRunway()` when an ILS destination is set, otherwise from `flightPlanManager.CurrentFlightPlan.ArrivalICAO` / `ArrivalRunway` (`LandingExitPlannerPreset.Resolve`). The airport load selects the preset runway by normalised designator, only for the preset airport, and a second load of the airport already loading joins the one in flight.
- `OnSimVarUpdated` forwards every `SIM_ON_GROUND` update to `landingExitPlanner.ProcessGroundState(...)` while `HasPendingExit` is true, using `simConnectManager.LastKnownPosition` (updated by the existing SimConnect position loop) for lat/lon/heading/GS.
- Position seeding for takeoff-assist after taxi lineup: when the taxi manager reaches the `LiningUp` state on a runway and the aircraft has arrived, it exposes a reference via `TryGetRunwayLineupReference(...)`. MainForm seeds `TakeoffAssistManager` with that reference **only** when `!takeoffAssistManager.IsActive && !takeoffAssistManager.HasRunwayReference`, so the existing **teleport → takeoff assist** flow is untouched. The teleport dialog's `OnTakeoffRunwayReferenceSet` callback sets the reference directly; once set, the taxi-driven seeding is a no-op.

### Takeoff assist & runway centerline tracker

`Navigation/RunwayCenterlineTracker` is shared between the taxi `LiningUp` state and `TakeoffAssistManager`. The tracker owns centerline projection math; both features consume it to avoid drift between their deviation calculations.

**Heading sanity check at TO Assist activation.** When `TakeoffAssistManager.Toggle` activates with a runway reference (set by the teleport flow OR seeded from `TaxiGuidanceManager.TryGetRunwayLineupReference`), it computes `headingDiff = currentHeadingMagnetic − referenceRunwayHeadingMagnetic` (normalized to ±180). If `|headingDiff| > 3°`, it announces `"Warning: heading X, runway heading Y. Turn left/right to align before rolling."` immediately after the standard activation announcement. Rationale: the centerline tone pans on heading deviation but never speaks (pan-only), and the spoken `"center"` callout reflects **cross-track only** — a pilot who finishes lineup off-heading but on-CL would otherwise hear `"center"` and start the roll without realizing the nose is pointed off-runway. The 3° tolerance matches the taxi-lineup `enterAligned` threshold (so we only warn when lineup wouldn't have considered it aligned either). FAA AIM and standard pilot training require a pre-takeoff heading-vs-runway cross-check; sighted pilots do this visually against the heading indicator, blind pilots need it spoken once.

## Regulatory Alignment (FAA / ICAO / EASA) — where taxi begins

Taxi guidance deliberately does **not** run during pushback. The boundary matters because the DB models taxiways as a graph that starts at the edge of the apron, not inside a stand:

- **ICAO Annex 14 / Doc 9157** — the **movement area** = **manoeuvring area** (runways + taxiways) + **apron**. The apron (stand, taxilanes, pushback area) is explicitly excluded from the manoeuvring area. ATC ground control's taxi clearance applies to the manoeuvring area; the apron is under separate apron management (ramp / apron control at large airports, or under the pilot's responsibility at smaller ones).
- **FAA AIM 4-3-18 / JO 7110.65** — aligns with ICAO. Pushback is an apron operation coordinated with ramp control or ground; the formal taxi phase begins once the aircraft has been pushed onto a taxiway or taxilane and is ready to move under its own power following the taxi clearance.
- **EASA SERA** (Dec 2024 revision) — adopts the ICAO definitions verbatim for member-state airspace.

**Implication for this feature:** the pilot performs pushback with GSX / their pushback add-on. Taxi guidance is started **after** the aircraft is stationary on an apron taxilane or the first assigned taxiway, facing roughly the correct direction. The `StartGuidance` apron look-ahead handles the realistic case where the first route segment is an unnamed stand taxilane: the announcement is `"Steering guidance active. Join taxiway Alpha in 200 feet."` not `"Taxiway Alpha."` The initial-direction mismatch warning catches pushbacks that ended in the wrong direction (120° one-shot threshold).

The feature does **not** attempt to guide pushback itself — pushback instructions come from ATC or ramp in plain English ("push tail south onto Romeo") and depend on stand geometry that isn't always in the DB.

## Universal Airport Support

Everything is driven by the user's local navdatareader database. The code never hardcodes airport-specific taxiway names, parking designators, or runway IDs.

- **Any taxiway name the DB exposes is supported**, including multi-word and vendor-specific names (`LINK 53`, `HAWKER`, `INNER`, etc.). Normalization is limited to trim + whitespace collapse.
- **Parking abbreviations** (`G`, `GA–GZ`, `P`, `NP`, `EP`, and similar navdatareader shorthand) are expanded to full names (`Gate`, `Parking`, `North Parking`, etc.) in `LittleNavMapProvider.MapParkingName`. Custom scenery names fall through unchanged.
- **Small / GA airports** work as long as the DB has taxi paths. Airports with unnamed paths (type `T` with null name) are still routable — announcements fall back to "unnamed taxiway" with a bearing.
- **Stress-testing:** `EGLL` (Heathrow) was used to validate long multi-taxiway routes, hold-shorts, and multi-word names. All behavior must continue to work on small fields.

### Navigraph data

Navigraph taxi data flows in **at database build time**, not at runtime. When the user has a Navigraph subscription and `NavigraphUpdate=true` is set in the navdatareader configuration, navdatareader merges Navigraph's taxiway/parking/runway data into the generated SQLite during the update step. From then on it's just normal rows in `taxi_path` / `parking` / `runway` — `LittleNavMapProvider` reads them identically to sim-native data. There is no runtime Navigraph API call in this feature; if the user's DB was built with Navigraph enabled, the better data is already there.

### Operational-flag filtering (closed / takeoff / landing)

`runway_end` carries `has_closed_markings`, `is_landing`, `is_takeoff` integer flags. Many DB builds (the test build this app was developed on) populate every row with permissive defaults — `LittleNavMapProvider.SafeReadBool` reads them safely with permissive defaults if the column is missing or NULL, so those DBs see every runway. Third-party scenery and certain Navigraph merges DO populate these flags; on those builds:

- **Taxi Assist Form** drops runways with `IsClosed = true` OR `IsTakeoff = false` from the destination dropdown — you can't taxi-to-takeoff a closed or takeoff-prohibited runway.
- **Landing Exit Planner Form** drops runways with `IsClosed = true` OR `IsLanding = false` — you can't pre-plan an exit on a runway you can't legally land on.

Defaults stay permissive so this is purely additive: users on sparse DBs see no behavior change, users on rich DBs get the filtering automatically. Avoids "WTF" moments on VATSIM where a controller would never have cleared the closed runway you somehow picked.

### Wind awareness in the Landing Exit Planner

`LandingExitForm` reads ambient wind once at form open via `SimConnectManager.RequestWindInfo`, caching `WindData.Direction` (degrees, met convention — direction wind is COMING FROM) and `WindData.Speed` (knots).

**Per-runway wind appears inline in the dropdown items themselves**, not just as a post-selection announcement. Each runway combo entry is a `RunwayChoice` wrapper whose `ToString()` returns e.g. `"30R, 12 knot headwind"` / `"09R, 8 knot tailwind"` / plain `"30R"` when `|headwind| < 3 kt` or wind data hasn't arrived yet. `RefreshRunwayItemsWithWind` rebuilds the items both at airport-load time AND when the async wind callback resolves (marshalled to the UI thread via `BeginInvoke` since `RequestWindInfo` may resolve off-thread). The screen reader reads the wind suffix on focus as the user arrow-navigates the dropdown — gives per-runway wind context without queuing a separate announcement per item.

Headwind formula: `speed × cos(windDir − runwayHeading)`. Positive = headwind (shorter rollout, earlier exits viable); negative = tailwind (longer rollout, pick a later exit).

The planner dialog does NOT recommend a specific exit, because that would need aircraft-performance data we don't have (approach speed, weight, brake configuration). Giving the pilot the headwind/tailwind number lets them choose appropriately for whatever airframe they're flying. (When the aircraft lands on another runway or the other end, the touchdown re-plan does pick an exit, screening with a stated comfortable deceleration rather than aircraft performance — see the landing-exit runway check below.) Works with any weather source the user has active — MSFS live weather, ActiveSky, REX, or static — because SimConnect's `AMBIENT WIND DIRECTION` / `AMBIENT WIND VELOCITY` reflect the active weather model.

## GSX Gate Integration

When **GSX Pro** is running and has a profile for the airport, it becomes the
**authoritative** source for gates/stands — GSX's metadata (heavy/jetway/VDGS,
exact positions) is far more accurate than navdata's, so navdata's own
heavy/jetway classification is never shown when GSX can answer. GSX availability
for gate sourcing = GSX running this session — `GsxService.CouatlStarted` (the
Remote API's flag) OR `SimConnectManager.GsxCouatlStartedLVar` (GSX's own
`L:FSDT_GSX_COUATL_STARTED`, which every GSX build publishes, Remote API or not;
see [gsx.md](gsx.md)) — AND a matching profile exists. Never the Remote flag alone:
that silently floored these local-file features at GSX 4.0.1. When GSX is absent,
everything falls back to navdata unchanged.

### Gate source (GSX-authoritative overlay)

`GateDataSource` builds the gate list as an **overlay**: GSX metadata wins;
position comes from the GSX `.ini` `this_parking_pos` when present, else the
matched navdata stand's position; navdata-only stands are appended (nothing
lost). Matching is by number + suffix, disambiguated by concourse (navdata `GC`
→ GSX `C`). Size/heavy is derived from GSX `maxwingspan` → ICAO wingspan code
(A&lt;15 B&lt;24 C&lt;36 D&lt;52 E&lt;65 F≥65 m; heavy = E/F), not the ambiguous
`.ini type` enum. VDGS type (`SafeDockT42`, `Marshaller`, …) and the
`parkingsystem_stopposition` (nose-stop) are carried on `ParkingSpot`
(`VdgsType`, `StopLatitude/StopLongitude`, `MaxWingspanMeters`, `Source`).

Profiles are parsed universally (`GsxProfileParser`) — pure-numeric gates,
suffix glued to number (`218l`), direction-prefixed parking (`w parking 4`),
and letter-before-number-as-concourse vs letter-after-as-suffix are all handled.

### Search / concourse filter

Both the Gate Teleport and Taxi Assist gate pickers have a type-to-filter box
(`GateSearchFilter`) matching on name + number + suffix, with concourse-token
filtering — works with or without GSX.

**Per-ICAO gate-list cache.** `TaxiAssistForm` caches the airport's gate list as
(spot, resolved graph node) pairs per (ICAO, gate-list SOURCE token —
`GateDataSource.GetGateListVersion`, an O(1) compare-only token that moves when GSX
publishes the airport after the list was built); the search box and the fitting
filter then filter **in memory** on each keystroke (matching the `GateTeleportForm`
pattern). The token is re-checked on show and at the top of Calculate, rebuilding only
on an UPGRADE (fallback → API, or a fresh API publish), never on the downgrade a
transient GSX drop causes; a chosen stand the rebuilt list no longer carries leaves
NOTHING selected (never item 0) and is announced. Do not reintroduce per-keystroke
directory enumeration + navdata query + per-spot nearest-node resolution on the UI
thread — the token check is a property read, nothing more.

### "Show only fitting stands" filter

The fitting checkbox uses `ParkingSpot.FitsAircraft(wingspanFeet)`, which is
**source-aware**: GSX stands compare the aircraft's wing span (converted to
metres) against GSX's authoritative `MaxWingspanMeters`; navdata stands use the
physical parking `Radius` (feet) vs half the wing span. A GSX stand with no
`maxwingspan` is treated as fitting (never hidden). (The earlier code compared
GSX's metre-based radius against a feet threshold and hid almost everything —
fixed.)

### Auto-select gate on Calculate Route

Setting `GsxAutoSelectGateOnRoute` (default on). When Taxi Assist calculates a
route to a gate, `GsxRemoteGateSelector` sends GSX's documented `gate.select`
verb over the Couatl Remote API with the stand's own identifier
(`ParkingSpot.GsxIdentifier`, taken verbatim from
`handlerData.airport.parkings` — never a label rebuilt from `Describe()` or
`Name`/`Number`) — one request, one typed response, no menu interaction of any
kind. The selector feature-checks the `gate` token in `hello.capabilities`
first (GSX 4.0.8+); when it's absent, nothing is sent and the pilot still
routes and taxis the aircraft manually, they just have to select the gate in
GSX themselves. That last part is SPOKEN, once per dialog session, but only
when GSX advertised a capability list that simply lacked `gate` — positive
evidence of a connected 4.0.1-4.0.7 build. An empty list says nothing about
the version (usually the Remote API isn't connected at all), so it stays
silent rather than send the pilot after an update they may already have.

GSX's result is interpreted, never guessed. `services_active` (GSX already
committed at a different gate) retries **exactly once** with
`revokeServices: true` and is announced, so the pilot knows the previous
stand's services were torn down. `assigned_to_other` (the stand is AI-occupied)
is announced and **never** auto-`force`d — overriding it silently would put a
blind pilot nose-to-nose with an aircraft they cannot see. `ambiguous` (several
stands matched the identifier) is announced rather than guessed at. A
`too_small` warning on an otherwise-successful selection is always spoken — it
is GSX's own verdict on the real airframe, and there is no other route to that
information. `already_parked`/`already_selected` — GSX had already prepared (or
you are already parked at) that stand — are SPOKEN too ("GSX is already set up at
Gate A12."): the vendor guide's "nothing to do" is about not retrying, not about
not telling the pilot, and silence there is the wrong-stand failure by another
route (see the invariant in CLAUDE.md and [gsx.md](gsx.md)). Every announcement
is QUEUED (`Announce`), never immediate, so it can't interrupt a taxi callout. See `GsxGateSelectAnnouncer` for exactly which of
`gate.select`'s outcomes are spoken and why the rest are deliberately silent.

**Changing gates:** re-running Calculate to a different stand simply sends a
new `gate.select`; the reentrancy guard SERIALIZES overlapping calls (so two
Calculate clicks can't have the second call's `revokeServices` race the
first's) rather than rejecting the second outright — a pilot who spots a wrong
pick and immediately corrects it gets GSX ending on their last request, not a
dropped correction behind a busy message.

Confirmation is immediate and synchronous, straight off `gate.select`'s own
result payload — there is no L:var to poll and no lag. See
[GSX Integration](gsx.md) for the full verb/result shapes, the two
retained `.ini`/`.py`-parsing paths (remote-airport gate lists; the docking
stop position, which the Remote API cannot supply), and
`gsx-gate-select.log`'s per-attempt line (identifier sent, resolved gate,
warnings, outcome).

## VDGS / Marshalling Docking

After a taxi route to a gate is calculated, **docking guidance auto-engages** when
the aircraft is on the ground near the selected gate's stop position — specifically
when ground speed is low (≤ 15 kt), the aircraft is within the engage range of the
stop position, and it is roughly facing the gate (within the 70° cone). Docking
guidance never modifies taxi guidance's route or state; until it actually engages,
taxi runs its normal arrival sequence in full, and once engaged docking owns the
arrival callouts (see *Engage-latched arrival ownership* below).

**Engage range**: For `.ini` gates that carry a `gatedistancethreshold` value (the
distance at which GSX activates the VDGS), that value is used as the engage range
instead of the fixed 50 m default. It is clamped to [20, 70] m. For navdata-only
and `.py` gates (no `gatedistancethreshold`), the fixed 50 m applies.

### What it does

Three simultaneous audio feedback streams activate on engagement:

1. **Lateral steering tone** — reuses `TaxiSteeringTone` (pan left/right) to keep
   the nose on the gate centreline. The tone waveform and volume are the same as
   the taxi steering tone setting; there is no separate waveform/volume for it.

2. **Proximity beeper** (`ProximityBeeper`) — an accelerating click/beep whose
   cadence is analogous to a car parking sensor: slow and separate far out (~60 m),
   progressively faster as the aircraft closes, and solid (continuous) at the stop
   position. The beep sound (waveform) and volume are independently configurable
   in Taxi Guidance Options.

3. **Spoken distance milestones** — at 30 m, 20 m, 10 m, and 5 m: `"30 metres."`,
   `"20 metres."`, `"10 metres."`, `"5 metres."`. The final callout at the stop
   position is **`"GSX docking complete."`** for GSX `.ini` gates that have a
   `parkingsystem_stopposition`, or **`"Stop."`** for deice pads and navdata-only
   gates. If the aircraft overshoots, the system announces `"Stop. You have passed
   the stop position."` immediately. Spoken distances honour the Distance units
   setting (`DistanceFormatter` / `UserSettings.GroundDistanceUnit`).

On engagement, the system announces the gate's VDGS/guidance type once when it is
known from the GSX `.ini` profile's `parkingsystem` key:

| `parkingsystem` family | Spoken phrase |
|---|---|
| `Safedock*` / `SafeDock*` | "SafeDock display" |
| `Marshaller` | "Marshaller" |
| `Agnis*` | "AGNIS" |
| `Apis*` | "APIS" |
| `Rlg*` | "lead-in lights" |
| `VgdsDeIce*` | (none — deice branch) |
| `Vgds*`, `Honeywell*`, `Dummy`, `1` | (none — not actionable) |
| navdata / `.py` gate (no `parkingsystem`) | (none) |

For example: `"Docking guidance. SafeDock display. 45 metres to stop. Jetway on your left."`

On reaching the stop position:
- **GSX `.ini` gate** (has a `parkingsystem_stopposition`) — announces `"GSX docking complete."` and
  silences beeper and tone.
- **Navdata / `.py` gate or deice pad** — announces `"Stop."` as before.

### Target position precedence (universal)

Two different "positions" exist per spot, and they have different priority chains:

**Docking stop target** — `DockingGuidanceManager` reads the preserved
`ParkingSpot.StopLatitude / StopLongitude / StopHeading` fields **directly**
(sourced from the GSX profile's `parkingsystem_stopposition` key parsed by
`GsxProfileParser`) and falls back to the spot's position when they are absent.
The stop position is the most precise docking reference; it matches what the
visual SafeDock/marshaller boards display inside the sim. `StopHeading` is the
gate-facing true heading used as the centreline reference for both lateral
steering and the along-track distance calculation.

**Spot position (display / teleport / routing)** — chosen by `GsxNavdataMerger`
in this order:

1. **GSX parking position** — the `this_parking_pos` coordinate (the actual
   aircraft-datum parking position).
2. **Navdata parking position** — the raw parking spot lat/lon from the
   `LittleNavMapProvider` / navdatareader `parking` table (also an aircraft-datum
   location).
3. **GSX VDGS nose-stop** — LAST resort only. The stop position is a **nose-stop
   reference, not an aircraft-datum spawn**: teleporting at it placed the datum
   metres deep into the stand, sometimes at heading 0 when `StopHeading` was
   absent. (It previously sat second, ahead of navdata, which mis-placed
   stop-position-only gates.) Demoting it loses nothing for docking — the `Stop*`
   fields are carried on the spot separately, so docking still drives to the real
   nose-stop.

**No cross-concourse coordinate borrowing.** When a GSX gate has a concourse, the
merger only borrows a navdata candidate's coordinates if the normalized concourses
match; otherwise the spot is **dropped** rather than silently routing a blind
pilot to the wrong pier (e.g. "A12" listed at B12's position).

### GSX `.py` per-aircraft stop offset

Large airports ship a GSX **Python** (`.py`) profile (≈72 installed, including
EDDF). These are NOT parsed for the gate LIST (`GateDataSource` is `.ini`-only),
but they DO carry per-aircraft stop math: each gate's `customOffset` function
returns a longitudinal/lateral offset (in metres) that GSX's VDGS applies so a
777-300 stops a few metres deeper than an A320 at the same stand. Measured at
EDDF A66: a 777-300 stops **+5.3 m** vs the navdata base.

The offset is applied to **every non-deice gate, including `.ini` gates**. It was
once skipped whenever the gate carried a `StopLatitude` (i.e. `.ini` gates), on the
mistaken assumption that the `.ini` stop was already aircraft-exact. It isn't: the
`.py` `customOffset` is GSX's **per-aircraft** adjustment layered *on top of* the
static `.ini`/navdata base, which is why the same gate yields a different offset per
airframe (EDDF A66: 777 = 5.3 m, A380 = 6.3 m, base = 1.65 m). Without it the 777
parked ~5.3 m short at every `.ini` airport (EDDF included).

When the aircraft id is known, `TaxiAssistForm.ApplyGsxStopOffset` resolves the
offset and feeds it to `DockingGuidanceManager.SetStopOffset`:

- `GsxStopOffsetResolver` locates the airport's `.py` (`GsxProfileLocator.TryFindPyProfile`,
  most-recent match, `_handler.py` companions excluded), parses it with
  `GsxPyProfileReader` (cached by path + last-write-time), and evaluates the gate's
  function with `GsxPyOffsetEvaluator` for the resolved `GsxAircraftId`.
- **Aircraft id resolution is UNIVERSAL** (`GsxAircraftIdMap.TryResolve(icao, wingspanMetres)`):
  the PRIMARY mechanism DERIVES `idMajor`/`idMinor` from the ICAO type designator
  pattern (Boeing `B7Xd` → 707+X·10; Airbus `A3YZ` narrowbody literal / widebody
  300+Y·10; Embraer E-Jets literal) and the ARC code from wingspan (Annex-14:
  A&lt;15 … F≥65 m). A thin exception table holds only genuinely irregular
  designators (B787 bare-minor, A350 idMinor 1000, neo idMinor 1, A220, and the
  **737 MAX family** — `B37M`/`B38M`/`B39M`/`B3XM` → 737 family with
  closest-gauge minor, because the `B3xM` designators break the `B7Xd` pattern
  and resolved idMajor 0, silently losing every idMajor-keyed `.py` stop offset;
  probe asserts lock this). So any
  aircraft — including ones MSFSBA has never seen — resolves to a usable id; the
  raw ICAO is always preserved so ICAO-keyed profile tables hit regardless.
- The evaluator's group fallback tries `"ARC-E"`, bare `"E"`, and `"Heavy"` because
  different scenery authors key their group dicts differently (the `"ARC-X"` form
  dominates the installed profiles).
- **The gate suffix changes the resolved function.** EDDF **A66** (no suffix) →
  777 = **+5.3 m**, but EDDF **A66A** (suffix "A") → 777 = **0 m**: that stand's
  function has no 777 table entry, so it correctly falls to the base 0 (an A320 at
  the same stand gets −2.5 m, proving the function is evaluated rather than parse-
  missed). `tools/GsxOffsetProbe` carries resolver-level asserts that lock this.
- A `STOPOFFSET` diagnostic line (icao / gate# / suffix / `stopLatSet` / aircraft /
  aircraft id / resolved offset) is appended to
  `%APPDATA%\MSFSBlindAssist\logs\docking-aircraft.log` on each route-calculate,
  for one-glance debugging of "offset is 0" reports.

In `DockingGuidanceManager.UpdatePosition`, the stop point is shifted BEFORE any
distance is computed: `LongitudinalMetres` along `StopHeading`, `LateralMetres`
perpendicular (right = +), via an equirectangular metres→degrees conversion. Every
cue (along-track, lineup, milestones) then references the shifted stop. **Deice
areas keep the offset at `GsxOffset.Zero`** (datum-aligned pads). `GsxOffset.Zero`
is the default and a strict no-op — the shift is skipped entirely, so behaviour is
byte-identical to having no offset. Any miss at any layer (no profile, unknown
aircraft, parse error) degrades to `Zero`, never worse than the bare navdata stop.
The applied offset is logged (`stopOffL` / `stopOffLat`) in `docking.log`.

### Geometry (DockingGeometry)

GSX exposes **no docking-distance L-var** — the lateral and longitudinal distance
readouts on the visual SafeDock/marshaller board are internal rendering data with
no SimConnect interface. Guidance is therefore computed geometrically by the
`DockingGeometry` helper class:

- **Along-track distance to stop** — great-circle distance from the aircraft's
  current position to the stop position, projected forward along the gate
  centreline: `d_along = d_gc × cos(headingError)`, where `headingError` is the
  angle between the aircraft's true heading and `StopHeading`.
- **Lateral deviation** — computed via `NavigationCalculator.CalculateCrossTrackError`
  (the same signed cross-track helper used for runway lineup). Positive = right of
  centreline; negative = left.

All internal geometry is in metres. Spoken distances are converted at announcement
time via `DistanceFormatter.FromMetres` to respect the user's Distance units
setting (metres or feet).

### Architecture

`DockingGuidanceManager` is a standalone state machine with four states:

```
Idle → Armed → Docking → Stopped
```

- **Idle** — no gate selected, or guidance is disabled in settings.
- **Armed** — a gate with a known stop position has been selected and the aircraft
  is near the engage range (50 m default, or `gatedistancethreshold` clamped to
  [20, 70] m). Waiting for low ground speed and a roughly gate-facing heading to
  engage.
- **Docking** — all three feedback streams are active. The manager feeds
  `TaxiSteeringTone.UpdateHeadingError`, drives the `ProximityBeeper` cadence from
  along-track distance, and fires spoken distance milestones.
- **Stopped** — along-track distance has reached ~0 (or the aircraft has
  overshot). Lateral tone silent; the beeper holds a solid "docked — hold
  position" tone (except after an overshoot stop, where a "docked" marker over a
  bad park would mislead). **Stopped is escapable** — see *Docking state
  lifecycle* under *Docking precision & GSX stop* below: taxiing away (absolute
  distance > 75 m, any direction) disengages, and backing up > 3 m re-arms to
  Idle so a retry dock re-engages with fresh audio and milestones.

`DockingGuidanceManager.UpdatePosition(lat, lon, headingTrue, groundSpeedKts)` is
called from the same ~30 Hz SimConnect position handler that drives
`TaxiGuidanceManager`. Docking never touches `_route`, `_state`, or any other
taxi-guidance field. The coupling runs the other way only, per frame in MainForm:
`SetSteeringToneSuppressed(IsActive)` (tone mute) and `SetDockingActive(IsActive)`
(arrival-callout ownership) — both driven by docking's engage-latched `IsActive`
snapshot, so taxi's gate-arrival countdown fires normally **until docking
engages** and is suppressed from then on.

### Options (Taxi Guidance Options form)

Two new settings appear in the Taxi Guidance Options form under a "Docking
guidance" group:

- **Docking guidance** — enable/disable toggle (default on). When off, the
  `DockingGuidanceManager` stays in `Idle` and no beep or distance callouts fire.
- **Docking beep sound** — waveform picker (Sine, Square, Triangle, Sawtooth) for
  the `ProximityBeeper`. Independent of the steering-tone waveform setting.
- **Docking beep volume** — slider, independent of the steering-tone volume slider.

The **lateral steering tone** during docking reuses the existing taxi steering-tone
waveform and volume settings (no separate control — same tone the pilot is already
calibrated to from taxiing).

### Pure-logic verification

`tools/DockingProbe` is a console probe (no xUnit, per CLAUDE.md) that exercises
`DockingGeometry` and the `DockingGuidanceManager` state machine against scripted
position sequences:

- Nominal approach: aircraft starts 60 m out, step-closes to 0 → verifies
  milestone firings, beeper cadence steps, and final `"Stop."` announcement.
- Overshoot: aircraft crosses the stop position → verifies the overshoot
  announcement.
- Lateral deviation: aircraft offset from centreline → verifies steering-tone pan
  direction (left = steer left, right = steer right) is consistent with
  `DockingGeometry.CrossTrackMeters` sign.
- Target precedence: verifies GSX stop-position wins over navdata parking when
  both are present.

Run with `dotnet run --project tools/DockingProbe -p:Platform=x64` → expect
`ALL PASS`.

### Datum-aligned stop & door-side cue

The aircraft **DATUM** stops at the parking/stop position. An MSFS parking
position — and a GSX stop position — is where the aircraft *reference* (the model
origin) sits when correctly parked; the scenery jetway is placed to reach the
door for that datum location. Docking guidance therefore drives the datum to the
stop coordinate with **no door correction**: the "Stop" threshold is **0.3 m**
(`StopToleranceMetres`, see *Docking precision & GSX stop* below).

**Do NOT reintroduce a door-offset subtraction.** An earlier build subtracted the
per-aircraft `gsx.cfg` door offset from the along-track distance
(`doorDistanceToStop = alongTrackToStop − doorOffset`). That was wrong — the
`gsx.cfg` `[exit] pos` longitudinal column describes where the door is **on the
airframe**, not a stop offset — and it parked a B777 ~26 m short of the gate.
After the datum-alignment fix the offset survived only as dead telemetry-only
plumbing (`SetDoorOffsetMetres`); that plumbing is now **removed end-to-end**.
Per-airframe stop depth is handled by the GSX `.py` per-aircraft stop offset (see
*GSX `.py` per-aircraft stop offset* above), which shifts the stop **target**
the way GSX's own VDGS does.

**What `gsx.cfg` still feeds: the door SIDE cue.**
`Services/Gsx/GsxAirplaneProfile.cs` reads each aircraft's `gsx.cfg`
`[exit<preferredexit>] pos` lateral (first) column — negative = left — to
announce *"Jetway on your left/right."* (or *"Door on your …"* for stands
without a jetway) at docking engage. The scan covers aircraft package folders
(`…\SimObjects\Airplanes\*\gsx.cfg`, package root from `UserCfg.opt`) and GSX's
per-aircraft profiles (`%APPDATA%\Virtuali\Airplanes\*\gsx.cfg`), runs on a
background thread, and is cached for the session. Hardening: the map build is
**single-flight via `Lazy`** (concurrent multi-second scans can't race), the
directory walk is **depth-bounded (6)** so texture/sound trees are never crawled,
and `UserCfg.opt` parsing **excludes `InstalledPackagesPathNextBoot`** lines
(which could resolve a not-yet-active packages path after a relocation).
`MainForm.OnAircraftIcaoTypeDetected` locks its `_refreshedIcaos` set and
rechecks the ICAO is still current before publishing the door side, so a late
refresh for the previous aircraft cannot clobber the new one. Aircraft with no
`gsx.cfg` simply get no door-side phrase.

**Engage cadence.** Docking guidance auto-engages on the ground within the engage
range of the stop (50 m default, or the gate's `gatedistancethreshold` clamped to
[20, 70] m) at ≤ 15 kt, provided the aircraft is roughly facing the gate.
Distance milestones are unit-native (metres or feet per the Distance units
setting). "Slow down" is announced at 6 m. "Stop" fires at 0.3 m
(`StopToleranceMetres`).

### Docking precision & GSX stop

These refinements tighten the final few metres so the door lands within jetway-
bridge tolerance and the pilot gets an unambiguous "stop here" cue.

- **Docking completion stops taxi guidance.** `DockingGuidanceManager` raises a
  `DockingCompleted` event once on the Docking → Stopped transition (including
  overshoot), fired outside its lock. `MainForm` subscribes and calls
  `taxiGuidanceManager.StopGuidance()` (thread-safe and silent), so the flow ends
  cleanly instead of taxi sitting in LiningUp forever after parking.
- **Engage-latched arrival ownership.** `MainForm` feeds
  `taxiGuidanceManager.SetDockingActive(dockingGuidanceManager.IsActive)`, where
  `IsActive` = the docking state machine is **engaged** (Docking or Stopped) — a
  lock-free volatile snapshot, so the per-frame read costs no lock (and no
  SettingsManager static-lock acquisition). Taxi speaks its FULL arrival
  sequence — parking countdown, "Stop. Hold position.", "Align with X",
  "Destination reached", "Parking brake." — right up until docking actually
  engages; once engaged, docking owns the arrival through Stopped and taxi stays
  quiet so the two never contradict. **Do NOT widen this back to gate-set
  semantics** (the removed `OwnsArrival` = gate set + docking enabled): with that
  design, a navdata gate where docking never engages — approach outside the 70°
  cone, stop beyond engage range, approximate navdata heading — arrived in
  **total verbal silence**. The deliberate trade: a brief sequential overlap is
  possible (taxi's stop callout fires, then docking engages with its own
  countdown a moment later); that overlap is self-correcting and far better than
  silent arrivals. The same `IsActive` read also drives the steering-tone mute
  via `SetSteeringToneSuppressed`.
- **Jetway-precise lateral lineup** (`ComputeLineupError`). The intercept dead-band
  was tightened **8 ft → 1 ft** (the 8 ft band stopped correcting cross-track below
  8 ft, parking the aircraft up to ~2.4 m off centerline), and `SaturationFt` went
  60 → 40 (a small residual still earns a usable correction angle). Cross-track
  convergence is a function of distance travelled, not time
  (`d(cross)/d(forward) = −sin(angle)`), so it closes the same per metre at 1 kt as
  at 5 kt — no slow-speed special-casing — and the continuous sqrt ramp never
  springs a late turn.
- **Final alignment turn completes earlier.** The intercept-fade squares the heading
  to pure gate heading by **2.5 m out** (`FadeStartM`/`FadeEndM` = 6/2.5, was 4/1),
  so an over-rotated gate entry (~5° off the taxi turn) finishes the squaring turn
  with room to creep straight in, instead of cramming it into the final metre and
  stopping ~2° off.
- **Precise, unforgiving stop + persistent "docked" tone.**
  - `StopToleranceMetres` tightened **0.5 → 0.3 m** (drives gate IsStop, the solid
    tone, and the "GSX docking complete." callout), so the pilot lands within
    ~0.3 m of the exact stop.
  - The beep plateau is removed: `BeepNearMetres = StopToleranceMetres`, so the
    accelerating pulse keeps speeding up right to the stop with **no** max-speed
    plateau. The old 2 m plateau made everything from 2 m to the stop sound
    identical, so pilots read "fast beep" as "stop" and parked short, mid-turn.
    Now the rule is simple: accelerating pulse = keep creeping; solid tone = stop.
  - The solid continuous tone was previously dead code — the state machine called
    `_beeper.Stop()` at the same 0.3 m threshold the solid tone begins, so the beep
    just vanished at the stop. Fixed: at IsStop the lateral pan tone stops but the
    beeper is held in its solid mode and keeps sounding through the Stopped state as
    a "docked — hold position" marker, until the pilot ends guidance (Stop taxi
    guidance button → `SetDestinationGate(null)` → `ResetLocked` stops it) or taxis
    or backs away (see the lifecycle bullet below). An **overshoot stop does NOT
    hold the solid tone** (a "docked" marker over a bad park would mislead) and no
    longer disposes the beeper, so a retry dock re-engages with working audio.
- **Docking state lifecycle — Stopped is escapable, every state is exitable.**
  - **Taxi-away disengage uses ABSOLUTE distance, not along-track.** Along-track
    goes **negative** once the stop is behind the aircraft, so the old
    `alongM > 75` check could never fire for a forward taxi-out and the Stopped
    state (with its solid tone) latched forever. Raw distance > 75 m
    (`DisengageRangeMetres`) now disengages in any direction — including a stale
    next-flight gate hundreds of kilometres away.
  - **Backing up > 3 m past the stop re-arms to Idle** (`RearmBackupMetres`): a
    pilot who overshoots (or wants a better park) backs up a few metres and the
    normal Idle/Armed → `ShouldEngage` path re-engages with fresh audio and fresh
    milestones.
  - **Disabling docking (or losing the gate) mid-approach fully resets**
    (`ResetLocked`, not just silence). Leaving `_state` latched at Docking/Stopped
    kept `IsActive` true forever, so MainForm went on muting taxi's steering tone
    every frame and the pilot had no lateral cue for the final gate turn.
  - **Stopped-short closure.** Engaged + sitting still (gs < 0.5 kt) for ≥ 4 s with
    0.3–10 m still to go → *"X to stop. Continue forward."* Taxi's own
    stopped-in-zone "Stop. Hold position." cue is suppressed while docking owns
    the arrival, so docking must provide the verbal closure itself — otherwise the
    pilot gets an endless fast-but-not-solid beep and no explanation.
  - **Stale-gate lifecycle.** Takeoff-assist activation and `LandingRollout` entry
    both clear the docking gate (`SetDestinationGate(null)`) — the previous
    arrival is over. Without this, a stale departure gate could keep `IsActive`
    latched on landing and mute the landing-exit rollout steering tone. (The
    absolute-distance disengage also self-heals this, but takeoff is the
    unambiguous boundary.)
- **Gate-lineup "aligned" band is docking-aware; gate lineup never pulses.** The
  aligned hysteresis in the gate-lineup branch is **tight (enter 1° / 12.5 ft,
  exit 2° / 25 ft) only while docking is engaged** (`_dockingActive` — docking's
  tone and 0.3 m stop own the precision, and taxi's verbal is suppressed anyway,
  so the tight band just keeps the brief pre-mute window from flapping) and
  **forgiving (enter 4° / 25 ft, exit 7° / 40 ft) otherwise**: the synthetic
  centerline runs through the navdata parking point, which is routinely metres
  off the real stand markings, and demanding ~12 ft to a possibly-offset point
  left correctly-parked pilots permanently "not aligned" with no "Parking brake."
  cue. The runway-style stopped-misaligned **pulse was briefly enabled for gate
  lineup and is removed — do not re-add it**: precision parking is docking's job,
  and pulsing 3 Hz at a correctly-parked pilot demanding precision to a wrong
  point is a misfeature. Runway lineup keeps its pulse (its centerline reference
  is authoritative).
- **Hot-path gating.** Docking's far-field telemetry + lineup math run only when
  engaged or within 150 m raw distance (`DetailRangeMetres`) — at taxi distances
  they fed nothing but the `docking.log` line, which cost an open/append/close
  file write twice per second on the SimConnect thread, under the docking lock,
  for the entire taxi. Similarly on the taxi side, the hold-short / parking /
  exit-approach / runway-end callout paths early-out once all their latches have
  fired — don't reintroduce per-frame milestone-table builds.
- **`docking.log` telemetry includes absolute coords.** Lines now carry
  `stopLat`/`stopLon` (the computed stop target) and `acLat`/`acLon` (aircraft
  position) alongside the relative `along`/`crossFt`, enabling a direct comparison
  of docking's target against a known-correct GSX position.
- **SimConnect heading-unit gotcha (live verification).** SimConnect's
  `PLANE_HEADING_DEGREES_TRUE` / `_MAGNETIC` are returned in **radians**, not
  degrees, despite the name — multiply by 57.2958 (180/π) to get degrees (a logged
  5.93 = 339.7°). Latitude/Longitude are in degrees. This matters for any future
  MCP/SimConnect live-verification of docking geometry.
- **GSX operational note: engines OFF for services.** GSX will not offer ground
  services (deboarding, jetway, etc.) while engines are running — it prompts "stop
  engines to request services." This is GSX behaviour, not an MSFSBA docking bug; a
  precise dock with engines running still won't surface service options.
- **Verified live (EDDF A66 / B77W, engines off):** offset 5.30 m applied; docking
  target (50.04691716, 8.56034700) heading 339.9° true; a real dock stopped ~0.6 m
  short and 0.27 m left of centerline, and GSX accepted services — confirming the
  target is GSX-correct.

### Remote deicing guidance

GSX airport profiles describe remote-deicing pads as **`is_deicearea=1`
sections** — "special parking spots" with `this_parking_pos`,
`parkingsystem_stopposition` (lat/lon/heading), `radius`, and
`parkingsystem=VgdsDeIceWall`. These are not ordinary gate stands; they are
dedicated apron pads where aircraft are parked over the pad centre while ground
equipment applies deicing fluid.

**Data pipeline:**
- `GsxProfileParser` recognises `is_deicearea=1` sections and sets
  `ParkingSpot.IsDeiceArea = true` on the resulting record. All other fields
  (`StopLatitude`, `StopLongitude`, `StopHeading`, `Radius`, `UiName`) are parsed
  identically to a normal GSX gate — the stop-position keys use the same
  `parkingsystem_stopposition` format.
- `GateDataSource.GetDeiceAreas(icao)` exposes the deice-pad list for an airport.
  Deice areas are **excluded from the normal gate list** returned by
  `GetParkingSpots` — they would be confusing destinations in the taxi-to-gate
  flow; they are surfaced only through the dedicated Deice Area destination type.

**Taxi Assist — "Deice Area" destination type:**
`TaxiAssistForm`'s Destination type combo box includes a **"Deice Area"** entry
alongside Runway and Gate / Parking. When selected, the Destination combo
populates with the airport's deice pads by `UiName` (e.g., `"De-Ice Pad 1"`). If
no GSX profile is active or the profile has no `is_deicearea=1` sections, the
combo shows `"No deicing areas at this airport"` and the Calculate button is
disabled. Routing to the chosen pad, hold-short insertion, and off-route
recalculation work identically to any other gate destination — the pad's
`this_parking_pos` lat/lon is the routing endpoint and the nearest graph node
within `MAX_PARKING_TO_GRAPH_M` is the A* target.

**Docking — pad-centre stop (no `.py` stop offset, no door-side phrase):**
Deice areas centre the *aircraft* over the pad. All docking is datum-aligned (see
*Datum-aligned stop & door-side cue*), and for deice pads `DockingGuidanceManager`
additionally keeps the GSX `.py` per-aircraft stop offset at `GsxOffset.Zero` —
the stop position is the pad centre, and "Stop" fires when the aircraft datum
reaches it. On engagement
the system announces **`"Deicing guidance…"`** (followed by the distance to the
pad) instead of the SafeDock / Marshaller / neutral docking callout used for gate
stands. All other docking streams (proximity beeper, lateral steering tone,
spoken distance milestones) are identical to gate docking.

**Scope:** This feature covers **positioning and guidance to the deice pad**.
The deicing service itself — calling the trucks, the fluid application sequence,
the "deicing complete" confirmation — is GSX's own workflow, invoked through the
GSX menu as normal. The two concerns are fully decoupled: MSFSBA's guidance ends
at "Stop" on the pad; the pilot then interacts with GSX to request deicing.

**Airport coverage:** Only applicable at airports whose GSX profile contains
`is_deicearea=1` entries (EGLL, KDFW, KDEN, EHAM, EDDF, and similar large hubs
with remote deicing infrastructure). Airports without a GSX profile, or whose
profile has no deice sections, show the "No deicing areas at this airport"
placeholder and are unaffected.


### High confidence
- Graph data quality — verified across KJFK, EGLL, and GA fields; thousands of airports with named taxiways.
- SimConnect lat/lon precision on ground is sub-meter — more than enough for taxiway centerline tracking.
- Steering tone builds on the same battle-tested `AudioToneGenerator` + panning provider as hand-fly and visual guidance.
- ATC route validation — the router can check every taxiway name against the graph before starting.

### Known limitations
- No automatic ATC taxi-route capture (no SimConnect API exposes ATC clearances). User types what they hear.
- Pushback isn't guided. User must pushback manually (or teleport) before guidance can start.
- Custom scenery may diverge from navdatareader data; deviation detection + auto-reroute handles this gracefully but isn't perfect.
- No traffic awareness at intersections — pilot must listen to ATC for sequencing.

### Future work — DB fields not yet exploited

The navdatareader schema exposes a few fields this feature currently ignores. None are blocking; each is a marginal refinement if a real case comes up:

- **Directional hold-short filter.** `taxi_path.start_dir` / `end_dir` (`F`/`N`) indicates whether a hold-short node is directional (only one approach direction) or non-directional. The router currently treats all HS/IHS nodes as bidirectional. A directional filter would prevent routing "through" a one-way hold from the wrong side at airports that model this. Audit: 42,376 HSND undirected vs. 38,458 directional across the test DB — the non-directional majority is the common case, so the refinement is lower-priority.
- **ILS critical-area protection.** `runway_end.ils_ident` identifies ILS-equipped runways (~4,135 in the test DB). The IHS-over-HS preference stops behind the correct line when the IHS and HS sit on the same final approach (now gated by `SAME_APPROACH_IHS_MAX_M`, see "Runway hold-short selection" above); an explicit `ils_ident != NULL` hint could also be used to *force* IHS-preference even if the router's first match was HS — not currently needed, but worth noting if edge cases appear.

**Displaced-threshold handling (implemented).** `runway_end.offset_threshold` (feet) is now applied in `GetLandingExits`: the along-runway distance of each candidate exit is measured from the physical pavement start, then the offset is subtracted to produce `DistanceFromThresholdFeet` (from the landing threshold) and `DistanceFromTouchdownFeet` (from landing threshold + 1000 ft aim point). On runways with a non-zero offset (e.g. KJFK 13R at 2055 ft, KJFK 22R at 3438 ft, EGLL 27R at ~1004 ft), this keeps the "distance from touchdown" column honest — an exit listed at 2000 ft is 2000 ft past the landing threshold, not 2000 ft past the pavement start.

### What makes this reliable for blind users
- **Dual feedback**: continuous tone (fine) + speech (tactical). Neither alone is sufficient; together they give complete guidance.
- **Advance warnings**: 300 ft for turns, 300/150/50 ft for hold-shorts, distance countdowns for gates.
- **Safe defaults**: hold-short = silence (stop moving). Deviation = auto-reroute. Unknown state = bearing + distance fallback.
- **User always in control**: hotkeys for status, repeat, continue, stop available at any time.

## Unreachable-runway safety net, tone slew limiter & start cues

Three related behaviors added after an OMDB/PHNL 04L user session where a taxi
clearance ended on a taxiway that only *parallels* the destination runway (no
connector taxiway to the runway itself), so guidance silently held short ~450 m
off the centerline and the lineup tone panned forever.

- **Unreachable-runway warning (route load).** For a runway destination,
  `LoadRoute` measures the perpendicular distance from the route's final point to
  the runway centerline (`AbsLateralFromRunwayMeters`). Beyond
  `RUNWAY_REACH_MAX_CROSS_M` the route doesn't reach the runway. The full detail
  ("ends about N to the side of Runway X … missing the connecting taxiway …") is
  put in the route-summary **box** (`LastRouteSummary`); a **short** spoken form
  is exposed via `LastRouteReachWarning`. The route still loads — ATC routings and
  odd navdata exist; the pilot decides.
- **Warning is spoken AFTER `StartGuidance`, by the form.** Announcing it inside
  `LoadRoute` (even `AnnounceImmediate`) doesn't work — `OnCalculateClicked` calls
  `StartGuidance` immediately after, and its first-taxiway callout stomps it
  (confirmed in-sim: pilot saw it in the box, heard the taxiway, never the
  warning). `TaxiAssistForm` speaks `LastRouteReachWarning` via `AnnounceImmediate`
  *after* `StartGuidance`. The spoken summary is **skipped** when a reach warning
  is present (still in the box), and a 12.5 s `START_WARNING_CHATTER_GRACE_SEC`
  window — open at guidance start whenever a route-reach OR unmapped-start
  warning is pending — holds the advance turn notice, the "… ahead."
  callout and the curve cue, which wait and speak normally once the window
  closes. The taxiway-change callout is held the same way, but — unlike
  those three — has no early-clearing latch to protect, so once the window
  closes it either speaks the deferred name or, if the route has since moved
  past it, drops it silently instead (`TaxiwayChangeGate`). The
  taxiway-crossing callout is handled and skipped while the window's open,
  as before. Hold-shorts / runway crossings / the lineup bailout are
  never gated. The window is sized from the measured System.Speech Rate-0
  length of the longest start utterance it protects — a destination-not-connected
  warning followed by the route-start turn cue, 10.41 s — plus about a fifth;
  it does not cover a longer SayIntentions import summary spoken ahead of the
  warning.
- **Lineup bailout (during lineup).** If the runway-lineup phase sits beyond
  `LINEUP_UNREACHABLE_CROSS_FEET` for `LINEUP_UNREACHABLE_SEC` without converging,
  a one-shot bailout speaks ("This route does not reach Runway X. Reprogram …").
  This catches recalc-built routes too (which bypass `LoadRoute`). Both latches
  reset on `LoadRoute` / `StopGuidance`.

**The destination node is a runway ENTRANCE, not just the nearest node (LPPT 20).**
`TaxiAssistForm` resolves a runway destination through
`TaxiGraph.FindRunwayLineupEntryNode`, not a bare `FindNearestNode` of the lineup
point. The lineup point comes from the navdata `start` row, which is trusted for
where ALONG the runway the departure begins — correct at a displaced threshold, and
the reason `SnapStartToRunwayCenterline` exists — but nothing guarantees a taxiway
MEETS the runway there. LPPT 20 has a 1955 ft (596 m) displaced threshold with its
start row on it, ~619 m into the takeoff run, while the taxi network touches that
centerline only at S3 (~70 m, the full-length end) and U5/U6 (~1396 m). Nearest-node
returned an S3 node **204 m away and 201 m off to the side** — abeam the runway, not
on it — so the route dead-ended there, fired "does not reach Runway 20" (identically
with the taxiway list on "None", because the node is picked at dropdown-population
time), and the lineup intercept would have dragged the aircraft across ~200 m of
grass. The replacement is a real entrance: on the pavement, on the runway proper,
with an off-runway neighbour (`HasOffRunwayNeighbour`, the same test
`FindBacktrackEntryNode` uses), in the nearest node's connected component. Only
candidates **at or behind** the lineup point are eligible, nearest first — one
further downfield is an intersection departure, i.e. less runway than the pilot
selected, and must never be substituted silently (measured over the whole DB,
allowing downfield entrances moved 2,209 runway ends, EGLL 09L by 755 m and EHAM 36R
by 1,417 m, with nothing spoken). An entrance behind the `runway_end` pavement edge
still counts (starter extensions: EGLL 09L via AB13 300–355 m back), so there is no
`along >= 0` floor. It runs ONLY when the plain nearest node is already beyond
`RUNWAY_REACH_MAX_CROSS_M` — the same constant the warning uses, so the search and
the check can never disagree — and returns the plain node unchanged when no entrance
qualifies, so a genuinely unreachable lineup point still trips the honest warning
instead of being retargeted somewhere arbitrary. Measured: 80 of 54,032 runway ends
change, all 80 already inside the warning population, all clearing it.

**A route can END SHORT of the runway, and the destination-node probe cannot see it
(PHNL 04L).** `TaxiRouter.FindConstrainedPath`'s `lastTaxiwayTerminal` deliberately
ends a runway route on the LAST CLEARED TAXIWAY when that taxiway does not connect to
the destination — that is what honours a cleared taxiway instead of bypassing it
(EIDW N2, LFPG R1) — and `_destinationNodeId` is never reassigned, so the route stops
hundreds of metres away while the cross-track probe reads a destination node sitting
happily on the runway. That is the real PHNL 04L failure (clearance ended on a
taxiway paralleling 04L; guidance held ~456 m off behind a legitimate-looking "Hold
short of Runway 04L" and the lineup tone panned for four minutes). Moving the probe
onto the destination node in 2026-06-16 to kill the LPPT 02 false positive took this
protection with it. **Do not label the cross-track probe "the PHNL 04L check"** —
measured 2026-08-24, PHNL 04L's lineup point has a taxiway-F node 3.7 m away and
3.2 m off the centerline, so that probe cannot fire there at all.

`LoadRoute` / `TryRecalculateRoute` therefore capture whether the route ever
contained `destinationNodeId` **BEFORE `TruncateToHoldShort`** — truncation
legitimately moves a REACHING route's end back to a hold, so judging the
post-truncation end reads every normal departure as ended-short. An ended-short route
warns only when BOTH independent guards fail:

- `RouteEndIsRunwayHold` — the end is a hold-short node named (reciprocal-tolerant)
  for this runway. Covers set-back CAT II/III holds (EGKK A3 at 162 m) and the sparse
  GA fields where a legitimate hold is a long taxi from the pavement. An UNNAMED hold
  does not qualify; the walk below is what covers airports with no hold names.
- `TaxiGraph.GraphWalkToRunwayPavement` ≤ `RUNWAY_REACH_MAX_WALK_M` — how far the
  aircraft would still have to TAXI to be on the pavement. Never a perpendicular
  distance (it cannot separate a set-back hold from a parallel taxiway with no
  connector) and never the distance to the lineup node (the runway is not a
  continuous graph corridor: PHNL 04L's taxiway E reads 0 m to the pavement but
  1,399 m to the lineup node). Calibrated on 172,802 runway-owned hold nodes —
  p50 54 m, p90 130 m, 99.16 % within 400 m; PHNL 04L's real holds are 34–60 m while
  taxiway H is 655 m and P is 804 m.

**A crossing of the destination's own strip still gets a hold-short.**
The automatic pass once skipped any crossing whose name equalled the destination's,
which was wrong from both ends, because a crossing is named after whichever runway END
is nearer it: a route to 04L crossing the 04L/22R strip reported "22R" and announced a
hold-short of a runway the pilot never chose, while the same crossing nearer the 04L
end reported "04L" and was DROPPED — no hold before crossing the active runway, the
incursion direction FAA AIM 4-3-18 / ICAO Doc 4444 exist to prevent. The pass now
skips ONLY the route's own arrival: an entry of the destination strip that ends on it
(reciprocal-aware, `CenterlineHasDesignator`). Never skip on the strip alone — that
deletes the hold from every genuine entry or crossing of the destination runway. The label is
composed from the designator the PILOT selected, not the geometric one, and keeps its
hold point ("runway 04L at D5") so it stays distinct from the destination's own
"Stop. Hold short of Runway 04L".

That rename is passed to `RouteRunwayCrossings.ComposeCrossingLabel` as its
`preferredDesignator`, NOT as `crossedRwy`, and the distinction is load-bearing.
`TaxiGraph.Build` names every hold node after whichever runway END is nearer it, so on
the destination's own strip the DB label routinely already carries the reciprocal
("runway 22R at D5") — and `ComposeCrossingLabel`'s "already names this pavement" rule
KEEPS such a label. Passing the pilot's designator as `crossedRwy` therefore renamed
nothing on the normal path: the pilot still heard "hold short of runway 22R" while
taxiing to 04L, and the only case the swap reached was the minority one where the hold
node was unnamed. With the preference supplied, the rule swaps just the designator
TOKEN, so the hold point and the label's shape survive (both "runway 22R at D5" and
"D5, Runway 22R" are Build outputs). A user "end of taxiway" label is still never
touched, on this strip as everywhere else.

**The reach test is ONE pure core, used by both call sites
(`Navigation/RunwayReachGate.cs`).** `LoadRoute` and `TryRecalculateRoute` ask the same
question and used to answer it in two different shapes — an if/else-if chain and an
inverted `&&`/`||` expression — which had already drifted apart on the no-route-end
case. `RunwayReachGate.Evaluate` states the guard ORDER once (gate destination →
cross-track → route-reached-destination / no-end / named-hold → graph walk), and the
walk arrives as a `Func` so the bounded Dijkstra is never paid for when a cheaper guard
already decided. `DescribeFailure` owns the wording, including the rule that an
UNREACHABLE runway (`PositiveInfinity` from the walk) speaks no distance at all —
reporting `RUNWAY_REACH_WALK_SEARCH_M` instead told the pilot "about 1500 metres of
taxiing away" for a route with no path, a fabricated number they cannot check.

**`TryRecalculateRoute` writes `_routeReachesRunway` AFTER the no-op guard, never
before.** The verdict describes the RECALCULATED route, so computing it above the
`oldRemainingVia.SequenceEqual(viaNames)` early return let a recalc that was then
DISCARDED overwrite the flag for the route still loaded — arming the spoken
during-lineup bailout on a good departure, or disarming it on one that genuinely ends
short. This was harmless before the ended-short test was added, because the expression
then read only `_destinationNodeId` and was route-independent; it is not any more.

**The walk measures against the runway shape (`RunwayShape`), not the raw start rows.** `RunwayCenterlines` are paired from the navdata `start` table, and `SnapStartToRunwayCenterline` repairs those rows only LATERALLY — at a displaced threshold the row sits hundreds of metres inside the pavement (LPPT 20: 626 m), and `HalfWidthMeters` is a fixed 75 ft default. Measured over the shipped fs2024 DB, 7,123 of 95,989 runway ends (7.42 %) have a start row more than 50 m inboard and 344 exceed 400 m, while 5,127 of 48,040 runways are wider than 150 ft — so a start-row "is this node ON the runway?" test rejects real pavement and reports an aircraft standing on the runway as having no path to it. `RunwayShape` uses the runway table's pavement when it is a sound line for the centerline and the start rows otherwise, and every on-the-runway consumer reads it (see "Runway crossings and entries"). The entrance picker and the walk are the two halves of ONE reach test — they must agree about where the runway is.

**Tone slew limiter (`SlewLimitToneError`).** The Taxiing tone's heading error is
slew-rate-limited to `TAXI_TONE_MAX_SLEW_DEG_PER_SEC`. Two events move the tone's
target discontinuously and used to slam the pan hard left↔right: a multi-segment
index skip when a large aircraft (A380 ~50 m+ turn radius) swings wide through a
sharp corner (`AdvanceToNearestSegment` jumps the index), and an in-place route
recalc (resets the heading smoother). The limiter stretches such a jump into a
smooth ~1–1.5 s sweep; genuine turns (≈ yaw rate) pass untouched. Wrap-safe
(shortest-path step). Baseline persists across recalcs (so a recalc snap is
softened) and resets only on `StopGuidance` (so a fresh session snaps on frame
one). Applied **after** the rate-lead projection, so it also bounds lead-driven
swings. Taxiing only — lineup/docking keep their own precision profiles.

**Initial big-turn cue (route-start turn cue).** At guidance start the aircraft often
points well away from the route's first segment (post-pushback the tug leaves it
facing one way, the route leaves the gate another). A tone alone starts at full pan
and can't convey "turn around, which way." Above `RouteStartTurnCue.SharpTurnDeg`
(100°) a cue speaks *"Sharp turn left onto taxiway A."*; above `TurnaroundDeg` (135°)
*"Taxiway A is behind you. Turn left to come around."* Direction sign matches the
tone. Reset on `LoadRoute` / `StopGuidance` (not on recalc). Skipped when a reach
warning is present — that warning is the priority, the pilot will reprogram, and the
cue would only be extra words in front of it.

- **The wording has ONE owner, `Navigation/RouteStartTurnCue`, and the cue is composed
  ONCE by `LoadRoute` — never on the first taxiing frame.** Live KATL 2026-08-27: it
  fired there as an interrupting `AnnounceImmediate` ~50 ms after the SayIntentions
  import summary and cut it off mid-word, taking the destination, the applied taxiways
  and "hold short of runway 08L" with it. That is the fifth time two announcements at
  Calculate have stomped each other in this codebase, and the established remedy is one
  utterance.
- **Two delivery paths, never both.** `TaxiAssistForm` folds it into its single
  standstill utterance and calls `ConsumeInitialTurnCue()`; whatever is left unconsumed
  is spoken by the per-frame one-shot, so routes the form did not start keep the cue —
  or, on a route that STARTS HELD, where no taxiing frame runs before Continue, inside
  `ContinuePastHoldShort`'s resume sentence (*"Continuing. Sharp turn left onto taxiway
  K."*).
  The form consumes it UNCONDITIONALLY and speaks it only when there is no reach
  warning, so the suppression above survives the move.
  ⚠️ **The form does not fold it on EVERY path**, and the invariant must not be read as
  though it did: the **Progressive Taxi** branch of `OnCalculateClicked` calls
  `StartGuidance` and RETURNS well before the standstill block, so there the cue is still
  delivered by the per-frame one-shot — as are landing-exit handoffs and any
  `announceSummary:false` caller. That is unchanged from before the fold and is NOT a
  defect to fix: a progressive leg builds no route summary, no reach warning and no
  destination confirmation, so there is no second utterance for the cue to stomp.
- **The angle must be `ComputeSteeringHeadingError`'s value**, read against the route
  and segment cursor `LoadRoute` has just assigned, with BOTH sides TRUE north — the
  same look-ahead walk target the tone will read on its first frame. Never
  `route.Segments[0].BearingDegrees`: that is a different number (35° apart on the live
  KATL route), and a spoken left/right that contradicts the pan leaves a blind pilot
  with nothing to break the tie.
- ⚠️ **It names the ROUTE's first named leg, and that is a robustness fix, not the
  cause of the live defect.** Commit `fec4b05a`'s message and the first version of
  `RouteStartTurnCue`'s doc both blamed the bare *"Make a U-turn to the left"* on
  `_lastAnnouncedTaxiway` being blanked by `LoadRoute`. That does not survive reading
  the code: `StartGuidance` re-sets that field from an identical first-named-segment
  walk and runs synchronously before the first taxiing frame on both form paths, so on
  the Calculate path the old cue would have named the taxiway too. Naming from the
  route helps the paths that call `LoadRoute` WITHOUT `StartGuidance` — the three
  `Rollout` re-routes and `LandingExitPlanner` — where the field genuinely is still
  empty. The commit message cannot be rewritten; this is the correction.

## Tuning & Constants Reference

Constants live at the top of `TaxiGuidanceManager.cs` and `TaxiSteeringTone.cs`. Change carefully and re-test in sim.

### TaxiGuidanceManager

| Constant | Value | Purpose |
|---|---|---|
| `WAYPOINT_CAPTURE_RADIUS_M` | 25.0 | Advance to next segment when within this radius (skipped on last segment) |
| `APPROACH_ANNOUNCE_DISTANCE_M` | 100.0 | "In 300 feet, turn…" trigger |
| `TURN_IMMINENT_DISTANCE_M` | 30.0 | Base value for "turn now" — scaled by ground speed |
| `TURN_IMMINENT_MIN_M` / `MAX_M` | 20.0 / 75.0 | Floor / ceiling for speed-scaled turn trigger |
| `TURN_IMMINENT_SEC_LEAD` | 4.0 | Target lead time at current ground speed |
| `CROSSING_ANNOUNCE_DISTANCE_M` | 50.0 | "Crossing taxiway X" trigger |
| `ARRIVAL_RADIUS_M` | 12.0 | Runway arrival radius |
| `GATE_ARRIVAL_RADIUS_FEET` | 20.0 | Gate arrival radius |
| `RECALCULATION_COOLDOWN_SEC` | 15.0 | Minimum gap between auto-reroutes |
| `GUIDANCE_LOOK_AHEAD_SEC` | 6.0 | Speed-scaled look-ahead horizon for the heading target (continuous walk via `GuidanceGeometry.WalkTarget`) |
| `GUIDANCE_LOOK_AHEAD_MIN_M` | 50.0 | Look-ahead floor — keeps short segments from wobbling the tone at low speed |
| `GUIDANCE_LOOK_AHEAD_MAX_M` | 120.0 | Look-ahead ceiling — caps the projected heading target at high taxi speed |
| `HEADING_ERROR_FILTER_ALPHA` | 0.25 | Low-pass filter on heading error fed to the tone |
| `CROSSING_DEDUP_WINDOW_SEC` | 45.0 | Per-taxiway-name crossing announcement dedup |
| `MAX_TAXI_SPEED_STRAIGHT_KTS` | 30.0 | Speed-warning threshold on straight segments |
| `MAX_TAXI_SPEED_TURN_KTS` | 12.0 | Speed-warning threshold in / approaching turns |
| `LINEUP_HEADING_TOLERANCE_DEG` | 5.0 | Gate-lineup hysteresis center — docking-aware band: enter 4° / exit 7° normally, tightened to enter 1° / exit 2° only while docking is engaged |
| Runway-lineup heading hysteresis (literals in `UpdateLineup`) | enter 1° / exit 2° | "Lined up" announcement only fires when heading is within 1° of runway heading; tone re-resumes if drifted past 2°. Was 2°/5° but was leaving pilots 3° off with no cue |
| Runway-lineup centerline hysteresis (literals in `UpdateLineup`) | enter 10 ft / exit 20 ft | Same as above but for cross-track. Tightened from 15/30 |
| Runway-lineup tone thresholds (literals at the `UpdateHeadingErrorWithThresholds` call) | silent 0.5° / activation 1° / max-pan 15° | Bypasses width scaling. Tone keeps panning until heading is centered within ½°; resumes if drifted past 1° |
| `LINEUP_CENTERLINE_TOLERANCE_FEET` | 25.0 | Legacy constant kept for reference; runway lineup uses the literal hysteresis above |
| `LINEUP_NOISE_DEADBAND_FEET` | 8.0 | Below this cross-track, intercept = 0 — only purpose is to keep GPS sign-flips near the line from chattering, NOT a "small errors don't matter" deadband |
| `LINEUP_INTERCEPT_SAT_FEET` | 100.0 | Cross-track at which the intercept-angle saturates at MAX_INTERCEPT_DEG |
| `MAX_INTERCEPT_DEG` (literal in `UpdateLineup`) | 30.0 | Max intercept angle for sqrt-curve runway-lineup steering |
| `LINEUP_PULSE_MAX_GS_KTS` | 3.0 | Pulse the lineup tone only when ground speed ≤ this (essentially stopped) |
| `LINEUP_PULSE_MIN_HDG_ERR_DEG` | 5.0 | Pulse only when heading error ≥ this (don't pester pilots who are 4° off and still actively turning) |
| `INCURSION_WARN_DISTANCE_M` | 40.0 | Off-route hold-short proximity warning |
| `PavementTolerance.WidthCapFeet` | 300.0 | Upper cap on segment width used for off-route tolerance (rejects bad DB width rows) |
| `POST_TURN_OFFROUTE_GRACE_SEC` | 4.0 | Off-route detection suppressed for this many seconds after every segment advance |
| `RUNWAY_REACH_MAX_CROSS_M` | 120.0 | Unreachable-runway warning, half one: perpendicular distance from the route's DESTINATION NODE to the runway centerline beyond which the route clearly does not reach the runway. Sits above any legitimate runway-ENTRANCE offset (an entrance is on the pavement). Do NOT justify it by hold-short offsets — measured in this DB, LPPT 02's own full-length ILS hold is 151 m off, its IHSND holds reach 183 m and EGKK A3 is 162 m, so a hold-based measure at this threshold false-fires immediately. `TaxiGraph.FindRunwayLineupEntryNode` takes the same number, so the entrance search and this check agree on what "reaches the runway" means |
| `RUNWAY_REACH_MAX_WALK_M` | 400.0 | Unreachable-runway warning, half two ("the route ENDED SHORT"): how far the aircraft would still have to TAXI from the route's end to be on the pavement (`TaxiGraph.GraphWalkToRunwayPavement`). Calibrated on 172,802 runway-owned hold nodes — p50 54 m, p90 130 m, 99.16 % within 400 m. The remaining 1.85 % are almost all tiny GA strips and are caught by the independent hold-NAME signal (`RouteEndIsRunwayHold`); the warning needs BOTH guards to fail |
| `RUNWAY_REACH_WALK_SEARCH_M` | 1500.0 | Search bound for that walk — comfortably past the threshold so the answer is never a truncation artefact, small enough that an end disconnected from the runway cannot walk the whole airport. Route-load only, never per frame |
| `LINEUP_UNREACHABLE_CROSS_FEET` | 400.0 | During lineup, cross-track beyond this (~122 m) for `LINEUP_UNREACHABLE_SEC` with no convergence ⇒ the route never reached the runway → one-shot spoken bailout |
| `LINEUP_UNREACHABLE_SEC` | 12.0 | Sustain time for the lineup unreachable-runway bailout |
| `TAXI_TONE_MAX_SLEW_DEG_PER_SEC` | 60.0 | Slew-rate cap on the Taxiing tone's heading error. A genuine turn changes the error gradually (≈ yaw rate, well under the cap) and passes through; a one-frame target discontinuity (segment-index skip on a sharp corner, or a route recalc) is stretched into a smooth ~1–1.5 s sweep instead of a hard L↔R pan slam. Applied after the rate-lead projection, Taxiing only |
| `RouteStartTurnCue.SharpTurnDeg` / `.TurnaroundDeg` | 100.0 / 135.0 | At guidance start, if the heading error to the route's first target exceeds these, compose a one-shot turn-direction cue (matches the tone) so the pilot knows which way to come around — the normal post-pushback case. Skipped when a reach warning is present. **Not in this file:** the local `INITIAL_TURN_CUE_DEG`/`INITIAL_TURN_UTURN_DEG` consts were DELETED, not left in place, so nobody tunes a number here and wonders why nothing changes — the cue has one owner, `Navigation/RouteStartTurnCue` |
| `START_WARNING_CHATTER_GRACE_SEC` | 12.5 | After a route-reach OR unmapped-start warning, hold the taxiway-crossing (handled and skipped while open, as before), "… ahead.", and curve callouts this long so they don't stomp the (longer, safety-critical) warning at guidance start, but only for as long as doing so is safe (`StartWarningChatterGate`): a callout that would otherwise be lost for good speaks immediately instead, interrupting the warning; one that can still be delivered speaks normally once the window closes. The taxiway-change callout is also held this long, but through a separate, simpler gate (`TaxiwayChangeGate`) rather than `StartWarningChatterGate`: it has no early-clearing latch to lose, so it always defers while the window is open and, once it closes, either speaks the deferred name or drops it silently if the route has since moved on to a different taxiway. Sized from the measured System.Speech Rate-0 length of the longest start utterance protected (a destination-not-connected warning + the route-start turn cue, 10.41 s) plus about a fifth; does not cover a longer SayIntentions import summary spoken ahead of the warning. Hold-shorts, runway-crossing callouts, and the lineup bailout are NOT gated |

### TaxiSteeringTone

| Constant | Value | Purpose |
|---|---|---|
| `SILENT_THRESHOLD_DEG` | 3.0 | Below this while sounding → go silent |
| `ACTIVATION_THRESHOLD_DEG` | 6.0 | Must exceed this to start sounding |
| `MAX_PAN_THRESHOLD_DEG` | 30.0 | Full pan beyond this error |
| `MIN_SUSTAIN_MS` | 400.0 | Minimum audible sustain once started |
| `TONE_FREQUENCY` | 440.0 Hz | Fixed A4 |
| `BASELINE_WIDTH_FEET` | 60.0 | Width at which the hysteresis scale factor is 1.0 |
| `MIN_SCALE` / `MAX_SCALE` | 0.65 / 1.40 | Clamp for `sqrt(width / 60)` hysteresis scaling |
| `PULSE_HZ` | 3.0 | Cycles/second when `SetPulse(true)` is active. Phase computed from `DateTime.UtcNow.Ticks` so the cadence is exact regardless of caller jitter; volume alternates between configured and 0 |

### LandingExitPlanner

| Constant | Value | Purpose |
|---|---|---|
| `LANDING_MIN_GS_KNOTS` | 40.0 | Minimum ground speed at airborne→on-ground transition for it to count as a real touchdown (rejects teleports / taxi starts) |

### TaxiGraph.GetLandingExits

| Constant | Value | Purpose |
|---|---|---|
| `MIN_DIST_FT` | 500.0 | Earliest usable exit after the landing threshold |
| `TOUCHDOWN_AIM_FT` | 1000.0 | Reference point for "distance from touchdown" column |
| Lateral tolerance | half-width + 15 m | Node must be within this perpendicular distance of runway axis to count as touching the runway (fallback 75 ft half-width if runway width is missing) |
| `MIN_FALLBACK_EXIT_ANGLE_DEG` | 20.0 | Geometric implicit-exit fallback: a Normal node qualifies as an exit only if it has at least one named-taxiway edge whose bearing is ≥ 20° off the runway axis. Excludes parallel taxiways while picking up real intersections. Used only when the runway has zero HS/IHS nodes. |
| High-speed angle | ≤ 50° | RET-geometry exit angle |
| Normal angle | ≤ 110° | Perpendicular exit |
| End-of-runway ratio | ≥ 0.85 | Nodes in last 15% of runway length classify as `End` |

### TaxiGuidanceManager — Landing Rollout

Several of these now initialise from `public const double` fields on
`Navigation/RolloutExitGate.cs` (noted below) rather than being private literals here —
that file also holds the pure decision functions (`SelectToneMode`, `IsExitTurnBegun`,
`IsTurnTowardExit`, `MatchEarlyVacateExit`, `IsHandoffRouteReachable`) the rollout loop
calls. See the "Rollout exit-turn gate, drift-correction tone & early-vacate handoff"
subsection above for the behavioral story; this table is just the numbers.

| Constant | Value | Purpose |
|---|---|---|
| `ROLLOUT_TAXI_GS_KTS` | 30.0 | Below this GS the aircraft is at taxi speed for handoff purposes. Conjunctive with `nearExit` — speed alone does not trigger handoff |
| `ROLLOUT_TURN_BEGAN_HDG_DEG` (→ `RolloutExitGate.TurnBegunHeadingDeg`) | 15.0 | Heading deviation from runway centerline that signals the pilot has begun the turn off. Feeds `RolloutExitGate.IsExitTurnBegun`, which ALSO now requires the deviation to be on the exit's own side (`IsTurnTowardExit`) and to begin within `TurnWindowFeet` of the exit or past it — a bare 15° deviation anywhere on the runway no longer counts |
| `RolloutExitGate.TurnWindowFeet` | 1000.0 | How close to the exit (or past it) `IsExitTurnBegun`'s heading-deviation test is allowed to fire. Derived (558 ft worst-case exit-node displacement on a 200 ft runway at a 15° exit, plus the app's own 300 ft tone-arm + 150 ft "turn now" lead), not fitted — do not tighten to `ROLLOUT_NEAR_EXIT_FT` |
| `RolloutExitGate.ExitSideMinBearingDeg` | 3.0 | Below this relative bearing an exit has no meaningful side and `IsTurnTowardExit`'s direction test is skipped (matches the existing `ExitAngleDegrees >= 3.0` gate in `alignedWithExit`) |
| `ROLLOUT_TURN_MAX_GS_KTS` (→ `RolloutExitGate.TurnMaxGroundSpeedKts`) | 90.0 | Above this GS a heading deviation is touchdown yaw / crosswind crab, not a deliberate exit turn — used by both `IsExitTurnBegun` and the post-handoff overshoot monitor's `turnBegunPH` |
| `ROLLOUT_NEAR_EXIT_FT` | 500.0 | Proximity to the chosen exit at which the speed-based handoff is allowed to fire. Matches the existing "500 ft slow down" callout — by the time the pilot hears that, they're committed to the turn |
| `ROLLOUT_OVERSHOOT_FT` | 100.0 | Along-runway distance past the chosen exit at which an overshoot is declared, triggering retarget to the next downfield exit (or graceful end if none remain) |
| `ROLLOUT_NO_EXIT_STOPPED_GS_KTS` (→ `RolloutExitGate.NoExitStoppedGroundSpeedKts`) | 3.0 | Ground speed below which the runway-end countdown treats the aircraft as stopped (`RunwayEndCountdownGate`): within `RolloutExitGate.NearRunwayEndFeet` that starts backtracking, anywhere else it gives one "Stopped on runway" notice. It also decides when the landing rollout stops silencing ground-traffic callouts (`Services/GroundTrafficSuppression`). Lower than `ROLLOUT_TAXI_GS_KTS` (30) because the countdown has no more useful callouts to make once the pilot is at a crawl |
| `RolloutExitGate.NearRunwayEndFeet` | 500.0 | How close to the far end counts as "at the end" for `RunwayEndCountdownGate` — a STOP inside it means the pavement has run out. A guidance constant of its own, deliberately not the 500 ft / 150 m runway-end SPOKEN milestone it coincides with in feet mode: that table is built from the pilot's distance-unit setting, so reading it moved this decision by ~8 ft when they switched to metres |
| `RolloutExitGate.TaxiGroundSpeedKts` | 30.0 | The speed a rollout brakes TOWARD, not through. `RolloutCalloutSupersession.ReachFeet` uses it so the reach of a one-shot sentence allows for braking at high speed without assuming heavy braking at taxi speed. Mirrors `ROLLOUT_TAXI_GS_KTS` |
| `ROLLOUT_TONE_ACTIVE_BELOW_GS_KTS` (→ `RolloutExitGate.ToneActiveBelowGroundSpeedKts`) | 50.0 | Above this GS the rollout steering tone is `Silent` (`RolloutExitGate.SelectToneMode`) — crab/crosswind pan would be meaningless at runway speed |
| `ROLLOUT_EXIT_TONE_ARM_FT` (→ `RolloutExitGate.ExitToneArmFeet`) | 300.0 | Distance to the chosen exit at which `SelectToneMode` switches from `DriftCorrection` to `ExitBearing` |
| `ROLLOUT_EXIT_TONE_SILENT_DEG` / `_ACTIVATION_DEG` / `_MAX_PAN_DEG` | 1.5 / 2.5 / 15.0 | `ExitBearing`-mode tone thresholds — desired heading is bearing-to-exit-junction (or `ExitBearingTrue` once the "turn now" callout has fired for a Normal exit) |
| `ROLLOUT_DRIFT_TONE_SILENT_DEG` / `_ACTIVATION_DEG` / `_MAX_PAN_DEG` (→ `RolloutExitGate.DriftToneSilentDeg` / `DriftToneActivationDeg` / `DriftToneMaxPanDeg`) | 2.0 / 3.0 / 15.0 | `DriftCorrection`-mode tone thresholds — desired heading is the runway heading itself. Fills the previously-silent middle of the rollout (beyond the 300 ft exit-tone arm, below the 50 kt tone-active ceiling) with a steer-back-to-runway-heading cue — heading only, no cross-track term |
| `RolloutExitGate.EarlyVacateForwardSlackFeet` | 600.0 | How far AHEAD of the aircraft an exit node may read and still count as the one the pilot already reached, in `MatchEarlyVacateExit` |
| `RolloutExitGate.EarlyVacateMaxPassedFeet` | 1400.0 | How far BEHIND the aircraft an exit may be and still be matched as the one vacated at. Mirrors `EXIT_COVERAGE_GAP_FT` in `TaxiGraph.GetLandingExits` — keep the two in step |
| `RolloutExitGate.HandoffReachMarginM` | 15.0 | Buffer added to a taxiway's half-width before `IsHandoffRouteReachable` refuses a handoff route. Reuses the same 15 m `TaxiGraph.GetLandingExits` adds to a runway half-width for its lateral tolerance |
| `RolloutExitGate.HandoffReachDefaultHalfWidthM` | 25.0 | Half-width assumed when the handoff's first segment carries no `PathWidth`. Deliberately generous — this guard ENDS guidance, so missing navdata width must never cause a false refusal |

## Pavement lead-in onto the first cleared taxiway

When a pilot enters a clearance like "taxi to runway 23 via A, H" while parked at gate GB/GC at CYYZ, the nearest graph node *on* taxiway A can be 200–300 m away across open apron. Without a lead-in the old code pre-snapped the route start to that distant A node, and `FindConstrainedPath`'s Step-1 A* would draw a straight beeline from the aircraft across the apron — cutting through grass, crossing taxiway AJ at an angle, and demanding a 180° pivot before the pilot had even moved.

The fix is a **pavement-following lead-in** that only activates when the gap is large (> `TaxiLeadIn.TriggerMeters` = 75 m). In that case `LoadRoute` sets the constrained route's start node to the aircraft's nearest *in-component* graph node — whatever apron taxilane or connector it is sitting on — and lets `FindConstrainedPath`'s Step-1 A\* plan a proper ground-path from there onto the first cleared taxiway. The router walks the apron network (e.g. ramp taxilane 4 → AJ) and arrives at taxiway A in the correct direction, with hold-shorts and crossings intact.

The lead-in is **accepted** only when two conditions hold:

1. The router honoured the full clearance (`ConstrainedFallbackReason == null`) — a fallback to shortest path means the lead-in segment is suspect.
2. The lead-in distance is within `gap × 2.5 + 300 m` (`TaxiLeadIn.IsAcceptable`) — this dead-end guard rejects a route that had to backtrack through a graph dead-end, which would inflate the lead-in far beyond the straight-line gap.

If either condition fails, `LoadRoute` falls back to the pre-snap behaviour (start on the taxiway) and prepends *"Could not compute a path onto taxiway A along the apron; route starts on A."* to the spoken summary. The pilot knows the route may not begin on pavement.

On success the summary names the lead-in explicitly — *"Route to Runway 23 via A, H. First taxi via 4 and AJ to reach A. …"* — using `TaxiLeadIn.Clause`, so a screen reader announces it before the constrained sequence.

**Unchanged cases:**

- Gap ≤ 75 m: the pre-snap (`FindNearestNodeOnTaxiway`) runs exactly as before — the lead-in path is never computed.
- Unconstrained routes (no user taxiway sequence): `FindNearestNodeInDirection` is still used; lead-in logic is bypassed entirely.
- `TryRecalculateRoute`: always uses the heading-aware `FindNearestNodeInDirection`; the lead-in is a `LoadRoute`-only feature.

**Verification:** verified in-sim — the CYYZ GB/GC stand → runway 23 via A, H departure (2026-06-17). The route now starts at the apron node nearest the aircraft, and the lead-in follows the AJ taxiway onto A — the steering-tone target tracks the AJ centreline within ~3 m — instead of the earlier ~64 m straight beeline across the grass north of AJ; the route summary names the lead-in. The pure helper math (`TaxiLeadIn.Extract` / `IsAcceptable` / `Clause`) is small and self-contained in `Navigation/TaxiLeadIn.cs`.

## Taxi-Data Augmentation Pipeline (Phase 5)

**Goal:** silently fill in unnamed navdata taxi-path segments with real-world taxiway names sourced from OpenStreetMap (OSM) and the X-Plane apt.dat gateway, so ATC-clearance routing and spoken announcements work correctly at airports where the navdatareader database has unnamed segments.

### Architecture

```
IAirportDataProvider  (base — navdata SQLite)
        │
        ▼
AugmentingAirportDataProvider   (decorator — transparent to all consumers)
        │  GetTaxiPaths()
        │    ├─ cache hit  →  MergeOnto()  →  return enriched list
        │    └─ cache miss →  return navdata now
        │                      BackgroundFetch()  (fire-and-forget)
        │                           │
        │                    OsmTaxiSource  +  XplaneAptDatSource
        │                           │  FetchAsync()
        │                    TaxiDataCache  (in-memory, per session)
        │                           │  Save()
        │                    AirportDataUpdated event
        │
        ▼
TaxiDataMerger.MergeNamesOntoNavData()   (pure geometry — bearing + midpoint match)
        │
        ▼
List<TaxiPath>  (names written back BY INDEX — no object rebuild, no field loss)
```

### Key files

| File | Role |
|------|------|
| `Services/TaxiAugment/AugmentingAirportDataProvider.cs` | Decorator; the main deliverable |
| `Services/TaxiAugment/OsmTaxiSource.cs` | OSM Overpass fetch → `AirportTaxiData` |
| `Services/TaxiAugment/XplaneAptDatSource.cs` | X-Plane Gateway apt.dat fetch |
| `Services/TaxiAugment/TaxiDataMerger.cs` | Geometric name-overlay (pure) |
| `Services/TaxiAugment/TaxiDataCache.cs` | In-memory per-ICAO cache (`ConcurrentDictionary`, TTL, no disk) |
| `Services/TaxiAugment/AirportTaxiData.cs` | DTOs: `AirportTaxiData`, `MergeOptions`, `CoverageReport` |
| `MainForm.cs` | Wiring: wraps `DatabaseSelector.SelectProvider()` in the decorator |

### Wiring in MainForm

The decorator is constructed immediately after `DatabaseSelector.SelectProvider()` returns. Only constructed when a base provider is available (no DB = no decoration). The typed `_augmentingProvider` field is kept for Phase 6's `PrefetchAsync` calls.

Cache: **in-memory only** (`TaxiDataCache` = a `ConcurrentDictionary` with a TTL). There is no disk cache — every session fetches fresh, so data is never stale (the user explicitly did not want a disk cache). The active flight's departure + destination are fetched force-fresh; geofenced nearby airports ride the in-session cache.

Augmentation event log: `%APPDATA%\MSFSBlindAssist\logs\taxi-augment.log` (via `AppLogs.PathFor`).

### Merge strategy

- Segment matching uses midpoint proximity (`MatchMaxMidpointMeters = 30 m`) and bearing agreement (`MatchMaxBearingDeg = 25°`).
- Name writeback is **by index** — iterates `nav[i]` and `merged[i]` together; copies `merged[i].Name` onto `nav[i].Name` only when `nav[i].Name` is blank and `merged[i].Name` is not. All other `TaxiPath` fields (`Width`, `Type`, `Surface`, `StartType`, `EndType`, `StartDir`, `EndDir`) are preserved because the original objects are mutated in place, never rebuilt.
- `TaxiDataMerger.MergeNamesOntoNavData` never overwrites an existing non-whitespace name; the navdata name is always authoritative.

### Name normalization

`TaxiDataMerger.NormalizeTaxiwayName(string)` converts a taxiway name to a canonical comparison form: uppercase, trim, remove all spaces, strip a leading `TAXIWAY` or `TWY` token. Examples: `"twy k 2"` → `"K2"`, `"TAXIWAY K2"` → `"K2"`, `"K 2"` → `"K2"`. Used **only for comparing names**, never for storing or announcing them. The stored name is always the original human-readable form from the authoritative source.

### Alias resolution

Some airports have a mismatch between navdata taxiway names and OSM/apt.dat names (e.g. navdata calls a taxiway `"HAWKER"` while OSM labels it `"K"`). Without alias resolution, a pilot entering `"K"` as their cleared taxiway would get no match.

**How aliases are collected (in `TaxiDataMerger.MergeNamesOntoNavData`):** For every navdata segment that already has a name, the merger runs the geometry match against OSM and apt.dat to find what the online sources call that same pavement. If the normalized online name differs from the normalized navdata name, the raw online name is appended to `NavSegment.Aliases` (deduped, case-insensitive). The navdata name is never overwritten.

**How aliases propagate:**
1. `TaxiDataMerger` stores them in `NavSegment.Aliases`.
2. `AugmentingAirportDataProvider.MergeOnto` copies them to `TaxiPath.Aliases` (in-memory only; not persisted to the DB).
3. `TaxiGraph.Build` reads every `TaxiPath.Aliases` list and populates `TaxiwayAliasToCanonical` (normalized alias → canonical navdata name, case-insensitive dictionary).
4. `TaxiGraph.ResolveTaxiwayName(string entered)` normalizes the entered name, looks it up in `TaxiwayAliasToCanonical`, and returns the canonical name if found — otherwise the original input unchanged.
5. `TaxiGuidanceManager.LoadRoute` passes every pilot-entered taxiway name through `_graph.ResolveTaxiwayName(...)` **before** any routing or node-snapping. This is the single choke point; all callers benefit.

**Safety invariants:**
- Aliases only affect name lookup, never geometry or steering.
- Airports with no online data behave exactly as before (empty alias lists → `TaxiwayAliasToCanonical` is empty → `ResolveTaxiwayName` is a no-op).
- The navdata name is always the canonical form stored in the graph and spoken to the pilot.

### Gate / Parking Aliases

Some sceneries use internal spot codes (e.g. `"GN 3"`) while ATC, OSM, and real-world charts use the stand number (e.g. `"47"`). Without an alias, a pilot looking for gate 47 cannot find it in the Taxi Assist destination dropdown.

**How parking aliases are collected — REWORKED 2026-06-23 (identity-matched, alias-only).** The earlier nearest-within-50 m gate-NAME fill was REMOVED: it corrupted gate identity at dense terminals (CYUL gate 15 adopted "Gate 11B" from an offset apt.dat ramp). Now the PUBLIC `AugmentingAirportDataProvider.AugmentParking(icao, spots)` flattens the online stands once and, for each authoritative gate, sets `spot.Aliases = GateAliasResolver.ResolveAliases(spot, online)` — a **pure, idempotent** resolver that:

- matches by **IDENTITY, not distance**: an online stand aliases a gate only when their **numbers match AND any letters agree** (`StandId.Parse` extracts letter/number/suffix). Navdata gate 15 never adopts a neighbour's "Gate 11B" (number mismatch); an "N" de-ice pad never adopts "S3" (letter disagreement). A **150 m** Haversine value is a sanity *backstop* only (a same-number stand kilometres away is a data error, skipped) — it is NOT the matcher.
- adds an alias **only when it carries info the identity lacks** — a concourse letter (`"A51"` for bare gate 51), a MARS suffix (`"53A"`) — a pure restatement (`"51"`, `"N3"`) adds nothing and is dropped.
- **NEVER sets a Name or position and NEVER adds a selectable gate (anti-grass).** Online data cannot move where you taxi; it only contributes searchable alternative labels. `spot.Aliases` is recomputed from scratch each call → idempotent.

- **X-Plane apt.dat is the key gate source.** Many airports have *real* gate numbers in apt.dat that navdata lacks (CYYZ "Gate 131", KATL "A12"/"B7") — these surface as identity-matched aliases.
- `AugmentParking` is **public** and called on the GSX gate list too (GSX is the gate SOURCE and bypasses `GetParkingSpots`), so GSX stands get the same aliases. Called in `TaxiAssistForm` and `GateTeleportForm` after building the GSX list.
- Empty-name gate-type spots render **`Gate {n}`** (not `Spot {n}`); a stand with no taxi node within `MAX_PARKING_TO_GRAPH_M` is kept but marked **`(no taxi route)`** and refused by the Calculate guard (was: silently dropped).
- `ParkingSpot.Aliases` is in-memory only — never persisted to the database. `StandId` (`Services/StandId.cs`) + `GateAliasResolver` (`Services/TaxiAugment/`) are pure + probe-tested (`tools/TaxiAugmentProbe`).

**How parking aliases are surfaced:**

1. **TaxiAssistForm destination dropdown** — for each spot with `Aliases.Count > 0`, the normal label (e.g. `"GN 3 - Gate Large"`) is added first, then one additional combo item per alias formatted as `"{alias} ({normalLabel})"` (e.g. `"47 (GN 3 - Gate Large)"`). Both items map to the same spot in `_destinationSpotMap`, so routing is identical regardless of which label the pilot picks. This alias loop runs at both spots where parking spots are added to the dropdown: the deice section and the regular parking section.

2. **GateTeleportForm listbox** — `ParkingSpot.ToString()` appends `" (also 47)"` when `Aliases.Count > 0`, so a screen reader reading the gate list hears the alternative name without a separate selection.

**Safety invariants:**
- Spots with no aliases produce identical behavior (empty `Aliases` list → the alias loop is a no-op in TaxiAssistForm; `ToString()` is unchanged in GateTeleportForm).
- The navdata name is always authoritative; aliases are additive display helpers only.
- Both dropdown entries for the same spot resolve to the same navdata spot object and therefore the same routing endpoint.

### Background fetch / deduplication

- `BackgroundFetch` uses a `HashSet<string> _inFlight` + `lock` so at most one fetch per ICAO is in flight at a time.
- `FetchCoreAsync` wraps both sources in `Task.WhenAll` with a 60-second `CancellationTokenSource`.
- Any exception is swallowed — background fetches must never propagate into callers.
- `PrefetchAsync(icao, force)` is the awaitable variant for Phase 6.

### Settings toggle + manual refresh

`AugmentingAirportDataProvider.Enabled` (default `true`) is wired to `UserSettings.TaxiAugmentEnabled`, exposed as an in-dialog checkbox in the Taxi Guidance Options form (with visible "© OpenStreetMap contributors (ODbL) + X-Plane Scenery Gateway" attribution). The same dialog has a **"Refresh Taxiway Names"** button that force-fetches the airport the aircraft is AT (`CurrentAirport.Resolve`, within 5 NM, from a position asked of the simulator at the press — `GetFreshAircraftPositionAsync`, whose 1.5 s fallback is the cached `LastKnownPosition`) and announces how many names were added (`GetLastCoverage(icao)` → "Taxiway names refreshed for X: N added" / "No new names found"), "No airport nearby." when there is none, or "Aircraft position unavailable." when the simulator has given no position at all. It used to take the nearest four-character code within 50 NM of the cached position alone: heliport 10CL at KSNA's GA stands, and in quiet cruise, where that cache goes stale, usually the departure field.

### Dropdown presentation (taxiway + gate aliases)

Aliases are **separate, self-labeled dropdown items** — not merged into one. A taxiway navdata calls "HAWKER" but ATC calls "B" shows as TWO entries: `HAWKER` and `B (HAWKER)` (the latter at the "B" position). Gates likewise: `Gate 131 (GH 5 - …)` sits separately from `GH 5 - …`. Either resolves to the same canonical pavement/spot. This lets a pilot scroll to the name ATC actually used while still seeing what the scenery calls it.

### Licensing & data attribution (verified 2026-06)

Both online sources were checked for whether MSFSBA's use is permitted. **It is** — because MSFSBA only *consumes* names at runtime and **never redistributes the source data or any database derived from it.**

**OpenStreetMap — ODbL 1.0.** OSM data is under the [Open Database License](https://opendatacommons.org/licenses/odbl/1-0/). The license distinguishes a *Derivative Database* (redistributing the data / a database built from it → triggers **share-alike**, must be re-licensed ODbL) from a *Produced Work* (a finished output made *from* the data → **attribution only**). MSFSBA fetches OSM via Overpass, extracts only taxiway/parking **name tags**, overlays them onto the user's own navdata **in memory**, speaks/shows them, and discards them at session end — it publishes no database. Per the [OSMF Licence & Legal FAQ](https://osmfoundation.org/wiki/Licence/Licence_and_Legal_FAQ): *"If you do not make Public Use of the data, then you do not have to share anything with anybody."* So this is a **Produced Work, attribution-only, share-alike NOT triggered**. Required attribution — **"© OpenStreetMap contributors"** with a note that the data is under the ODbL — is shown in the Taxi Guidance Options dialog and the docs ([Attribution Guidelines](https://osmfoundation.org/wiki/Licence/Attribution_Guidelines)).

**X-Plane Scenery Gateway — public API, per-pack `COPYING`, no redistribution by us.** The [Gateway API](https://gateway.x-plane.com/api) is open (no authentication for downloads); its only stated condition is a courtesy: *"be considerate of our server load and avoid making unnecessary requests."* The airport data is community-contributed and each scenery pack carries its own artist-set `COPYING` license field (there is no single blanket data license). MSFSBA again **does not redistribute** the apt.dat data — it fetches per-airport on demand, extracts taxiway/gate **names** only, and holds them in memory — so per-pack redistribution terms don't bind it; attribution (**"X-Plane Scenery Gateway"**, shown in-app) is the obligation we honor. We respect the server-load courtesy by design: fetches are **on-demand (departure/destination only), cached per session, and never bulk-crawled** (the earlier full-database census tooling was removed precisely to honor this).

**Net:** name-only extraction + in-memory overlay + no redistribution + visible attribution for both sources = compliant. If MSFSBA ever changed to **bundle/redistribute** the source data (e.g. ship a prebuilt name database), the analysis changes — OSM share-alike would apply to any distributed OSM-derived database, and each Gateway pack's `COPYING` would need honoring. Don't do that without re-reviewing the licenses.

## Additional Implementation Notes (folded from CLAUDE.md, Task 1.3, 2026-07)

The bullets below were previously carried verbatim in CLAUDE.md as a running changelog of specific bug fixes, named constants, and airport-specific repro cases. They record implementation detail not restated in the sections above (which describe the current architecture); some of the same ground is covered above at a higher level, but the specific constants, commit/bug references, and reproduction cases below are only recorded here.

- **No airport-specific hardcoding.** Everything comes from the user's DB. Taxiway names (`A`, `K2`, `LINK 53`, `HAWKER`), parking abbreviations (`G`, `GA–GZ`, `P`, `NP`, `EP`), and runway IDs flow through unchanged.
- **Do not break the teleport → takeoff-assist flow.** The MainForm runway-reference seeding from taxi lineup remains guarded by `!takeoffAssistManager.IsActive && !takeoffAssistManager.HasRunwayReference` so the existing teleport dialog path wins for the current activation. **Takeoff Assist's `Toggle(off)` now unconditionally clears the runway reference** — within-session preservation was unsafe because turnaround flights silently reused flight 1's runway threshold and heading on flight 2's CTRL+T (the `HasRunwayReference` guard rejected the fresh taxi-lineup reference). Across-session preservation isn't needed: process restart resets everything. The teleport dialog path (`OnTakeoffRunwayReferenceSet`) still calls `SetRunwayReference` unconditionally so teleport always wins when used.
- **Where-Am-I runway-detection fallback.** When neither taxi-lineup nor teleport has provided a runway reference, the MainForm `POSITION_FOR_TAKEOFF_ASSIST` handler probes `TaxiGuidanceManager.TryDetectRunwayUnderAircraft` (which wraps `TaxiGraph.TryGetRunwayAtPosition`) using the aircraft's current lat/lon and heading, at the airport `CurrentAirport.Resolve` names — the one Where Am I speaks. It used to take the nearest four-character code, which on KSNA's runway 02L is heliport 10CL: no taxi paths, so no runway was ever found there and the assist fell back to a synthetic centerline on a runway the database knows (over the 56,401 runway start positions — `start` table rows — of the airports with taxi paths, the old rule named the right airport at 50,155; the resolver names it at 56,289). Gated on `_lastOnGround` — airborne CTRL+T still falls through to the synthetic-centerline path in `TakeoffAssistManager.Toggle()`. Uses a strict tolerance — `RunwayShape`'s own half-width with no margin (unlike `DescribeLocation`'s +5 m) so a high-speed exit adjacent to a runway doesn't false-positive. Falls through to synthetic centerline if the airport has no `RunwayCenterlines` (sparse navdata).
- **Auto-activate Takeoff Assist on lineup.** `TaxiGuidanceManager` fires `RequestTakeoffAssistAutoActivate` (one-shot per route, gated by `_autoActivateFired` which resets on `LoadRoute` / `StopGuidance`) when the aircraft enters the lineup-aligned hysteresis on a runway target (`_isRunwayLineup == true`). MainForm subscribes and, if `SettingsManager.Current.TakeoffAssistAutoActivateOnLineup` is true and Takeoff Assist isn't already active, fires the standard `RequestPositionForTakeoffAssist` flow after announcing *"Lined up. Activating takeoff assist."* The latch is intentionally NOT reset by lineup drift-out — if the pilot manually deactivates Takeoff Assist after auto-activation, drifts off, and re-aligns, Takeoff Assist does NOT re-engage. This prevents surprise after a deliberate manual decision.
- **Do not announce runway info** (length, surface, ILS) from taxi guidance. Out of scope.
- **WAYPOINT_CAPTURE_RADIUS_M (25 m) must skip the last segment.** Otherwise it preempts the gate arrival radius (6 m) and the 50/20/10 ft parking countdown. Runways are unaffected (30 m > 25 m), but gates break without this guard.
- Steering tone uses **stereo pan only** — no frequency or volume modulation. Hysteresis (3°/6°) + 400 ms min sustain + low-pass filter on heading error kill flapping. Thresholds are **width-aware**: scaled per call by `sqrt(pathWidthFeet / 60)` clamped to `[0.65, 1.40]`. For taxi / gate lineup, call the `UpdateHeadingError(error, pathWidthFeet)` overload with the real segment width (gate lineup uses the no-arg baseline). The takeoff *roll* (`TakeoffAssistManager`) handles the wide-runway case itself.
- **Taxiing tone target = continuous arc-length walk (`GuidanceGeometry.WalkTarget`), speed-scaled 6 s clamped 50–120 m.** Pure function of position along the route polyline — no turn/no-turn branch, so one-frame target jumps (the old hard pan-flip class) are impossible; probe-pinned by `tools/TaxiGuidanceProbe` (KATL curve + KIAH hairpin replicas). The projection clamps `t` — the WALK START — at the UPPER bound only; clamping it to 0 would teleport the walk start ~25 m at every capture. **`t` and `f` are different quantities and only `f` has a floor:** `f` is the TARGET's fraction along the segment, and it is floored at 0 (`WalkTarget`) so the target can never land behind the segment start, which steers the pilot backwards. That floor only binds when the aircraft is further behind the start than the whole look-ahead — never in the ~25 m-behind capture geometry the `t` rule protects — so the two coexist.
- **A sub-metre segment is a POINT and is skipped WHOLE, never projected onto or forced to `t = 1`** (`DEGENERATE_SEG_M = 1.0 m`, matching the manager's `len < 1.0 → bearing 0.0` rule; both `WalkTarget` and `CumulativeTurnDeg` advance past such segments BEFORE projecting). Both alternatives break the walk, in opposite directions: projecting onto a stub extrapolates along a phantom axis (`|t|` in the hundreds — a restarted route's snap segment produced a target sliding a few metres from the aircraft instead of leading up the route, and the pilot orbited it), while forcing `t = 1` discards the aircraft's behind-distance so the whole look-ahead is walked from the stub's far node and the target steps ~25 m in one frame — the very teleport the `t` rule above exists to prevent.
- **The segment-advance scan measures ENDPOINT distance, so it needs the projection-based pin breaker beside it** (`GuidanceGeometry.HasPassedOntoNextSegment`, `SEGMENT_PASS_ADVANCE_MAX_CROSS_M = 30 m`). The current segment shares its end node with the next, so once the aircraft rolls past that shared node outside the 25 m capture (a wide corner), the two endpoints TIE, strict-improvement keeps the stale index, and on a long segment (KLAS B is 345 m) the aircraft is squarely on the route yet beyond every endpoint's reach — the walk target freezes at (stale segment end + look-ahead) and the tone orbits the pilot around a fixed point (KLAS 26R, 2026-08-20: two full circles, then the pilot gave up). The breaker advances when the aircraft's projection is past the current segment's end AND interior on the next within the cross-track bound, and it must fire in BOTH cases the endpoint scan cannot advance — the tie (`bestIdx` unmoved) **or** a later segment still beyond `SEGMENT_ADVANCE_MAX_DIST_M` (the far half of that long segment, where the far endpoint wins the scan but is still out of range). It goes through `AdvanceSegment()` so announcements and latch resets behave like every other advance, and it **never fires while the current segment is a hold-short segment** — advancing past an un-announced hold-short is the runway-incursion direction and that invariant outranks un-pinning. Accepted consequence: a hold-short segment pinned this way keeps the orbit rather than risking a silent pass; fixing that needs a hold-short-aware announce path, not a wider breaker.
- **Taxiing tone is rate-lead projected** (`error − yawRate × TurnLeadSeconds`, clamp ±30°): centres ~1 lead-time BEFORE the nose reaches the bearing so pilots stop overshooting turns. `TaxiTurnLeadSeconds` is per-aircraft on `IAircraftDefinition` (A32NX 1.6 — closed-loop stepped up from the measured 1.3 because the pilot WAITS for the cue instead of self-anticipating / Fenix 1.3 proxy / 737 0.4 validated / 777 0.3 validated / base 1.2; **A380 → add 1.8 override at PR-85 merge** — measured flying it on 1.3: rollouts 10–42° long). Lineup/docking/rollout tone paths are NOT projected. "Straighten." fires per sustained-yaw EPISODE (≥4°/s, ≥35° turned, route ahead <15° over 60 m, projected error crosses centre) — never gate it on per-junction `TurnAngleDegrees`: navdata splits 90° turns into 5–15° micro-bends (measured: 1 junction ≥60° out of 107 at KSFO). "Curving left/right." = cumulative ≥30°/100 m with no ≥20° single step, deferred while yawing ≥3°/s against the announced direction. A per-aircraft user override setting is DESIGNED but deferred — see "Turn rollout anticipation" (this document).
- **Runway lineup uses explicit thresholds, NOT width scaling.** Call `_steeringTone.UpdateHeadingErrorWithThresholds(error, silent=0.5°, activation=1°, maxPan=15°)`. Width scaling — even at the 25 ft / `MIN_SCALE = 0.65` clamp — gave silent ≈1.95° / activation ≈3.9°, which left pilots sitting 3° off heading with no audio cue (between silent and activation thresholds). The new precision values keep the tone panning until heading is centered within ½° and re-resume immediately past 1°. Do NOT call the width-scaled overload from the runway-lineup branch.
- **Lineup-aligned hysteresis** (governs the "Lined up" announcement): enter at heading <1° AND cross-track <10 ft; exit at >2° OR >20 ft. These are literals in `UpdateLineup` runway branch — tightened from 2°/5° / 15ft/30ft after the same too-loose-deadband bug.
- **Lineup pulse mode.** When stopped (≤3 kt) AND (heading error ≥5° OR cross-track ≥10 ft), the runway-lineup branch calls `_steeringTone.SetPulse(true)` — same pan direction, but tone toggles on/off at `PULSE_HZ = 3.0` (volume modulation, phase from `DateTime.UtcNow.Ticks`). Pure audio cue ("you've stopped but you're not done yet") with no speech — pilot's hands are on rudder + throttle and can't field verbal callouts. Forced off in the gate-lineup branch. **Cross-track condition is critical**: intercept-angle saturates at ±30° when cross-track is large, so a pilot who matches the saturated desired heading then stops would get heading error ≈ 0 (tone silent) even though cross-track is still huge. Without the cross-track branch here, that pilot has zero audio cue that they need to move forward to let the intercept controller close on centerline. Pulse keeps firing until BOTH dimensions are within the lineup-aligned hysteresis.
- **Runway lineup math is intercept-angle, not bearing-to-threshold.** `desiredHeading = runwayHeading + intercept · sign(crossTrack)`, with `intercept` rising on a sqrt curve from 0° (at `LINEUP_NOISE_DEADBAND_FEET = 8`) to 30° (at `LINEUP_INTERCEPT_SAT_FEET = 100`). Don't reintroduce a bearing-to-threshold blend — once the aircraft crosses the threshold (which happens during every "line up and wait"), the threshold is behind the aircraft and bearing-to-threshold sits on the ±180° wrap, producing chaotic sign flips on GPS jitter.
- **Lineup state entry must reset the heading-error smoother.** `_smoothedHeadingError = 0; _headingErrorInitialized = false;` at every `LiningUp` entry path (both Continue-past-hold-short and gate `HandleArrival`). Without the reset, the taxi-phase low-pass residual (often 50–80°) leaks into the lineup tone for ~300 ms and steers the pilot off the runway at low speed.
- **No feet-quantity verbal cues for blind-pilot guidance.** "42 feet left of centerline" has no spatial reference for a blind pilot — the tone is the instrument for cross-track. Heading numbers are fine (every pilot has a heading instrument).
- **Threading.** `TaxiGuidanceManager._stateLock` serializes the SimConnect-thread `UpdatePosition` against UI-thread mutators (`LoadRoute`, `StartGuidance`, `StopGuidance`, `ContinuePastHoldShort`, `GetStatusAnnouncement`). Any new public method that touches `_route` / `_state` / `_currentSegmentIndex` MUST acquire `_stateLock`. `TaxiSteeringTone` has its own `_lock` covering `UpdateHeadingError` / `UpdateHeadingErrorWithThresholds` / `SetPulse` / `Pause` / `Resume` / `Start` / `Stop` so audio buffer ops can't race with disposal.
- Magnetic → true heading conversion uses `magVariation` (east positive) before comparing to graph bearings.
- Off-route detection uses **perpendicular cross-track distance** (equirectangular projection, clamped to segment endpoints). Do not switch to endpoint-distance comparisons — that breaks on long segments.
- **Off-route auto-recalc is gated on a route-joined latch (`_hasJoinedRoute`).** Off-route detection (and thus `TryRecalculateRoute`) is suppressed until the aircraft has reached the route line at least once (`perp <= perpTolerance` on any frame). The post-pushback taxi from the gate ONTO the first taxiway is legitimately off the route's first segment — the route starts on the taxiway, often 100 m+ from the gate — so without the latch the slow taxi-out (gs ≥ 2 kt but not yet on the route) reads as off-route and recalcs fire, **silently trimming the entered clearance before the pilot has joined it** (PHNL 2026-06-13: 4–5 recalcs at 3–6 kt while still on segment 0 cut `Z A L N Z D` → `Z D`; the `OFF_ROUTE_MIN_GS_KTS = 2` gate alone didn't help because pushback exceeds it). Latch resets on `LoadRoute` / `StopGuidance`; once joined, normal off-route detection runs for the rest of the taxi. **Relatedly, an accepted recalc now announces the new sequence** — *"Route changed. Now via X, Y. <dist> to <dest>."* (distinct named taxiways in order) instead of the generic "Recalculating. … Taxiway X.", because a recalc can trim/replace the cleared sequence and the old wording never told the pilot their taxiways had changed.
- Hold-short node naming picks **connector-style** names (letter+digit like `A5`) over plain parallel names (`A`) when both are available on the same hold-short node. Preserve this ranking in `TaxiGraph` hold-short resolution. **Runway association is by nearest runway CENTERLINE, not threshold distance.** `TaxiGraph.MatchHoldShortRunwayName(lat, lon, RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M = 150 m)` names a hold-short node after the runway it sits at via clamped perpendicular distance to the full-length centerline (nearer-end designator on `RunwayShape`, same convention as `DescribeLocation`), so a hold-short where a taxiway crosses a LONG runway far from either threshold is still named correctly. The previous distance-to-`runwayStarts`-threshold-`<500 m` test mislabeled such crossings with the taxiway name (KBOS 15R on N → "Hold short of N" instead of "runway 15R"); the threshold method survives only as a FALLBACK when no centerline is within tolerance (sparse navdata without reciprocal pairs). Matched format is `"runway X at <holdPoint>"` (e.g. `runway 15R at N`) → "Stop. Hold short of runway 15R at N." The automatic runway hold pass's label policy is `RouteRunwayCrossings.ComposeCrossingLabel` (2026-07, probe-tested): an empty label gets `"runway X"`; a bare non-runway DB name ("A5") is upgraded to `"runway X at A5"`; a label naming THIS pavement (designator or reciprocal — user picks, correct DB names) is preserved; a DB name for a DIFFERENT pavement is CORRECTED to the geometrically detected runway (TaxiGraph's 150 m nearest-centerline naming can mis-bind between close parallels). User "end of taxiway" labels are never touched. Correct source naming still matters — it supplies the "at <holdPoint>" locative the callout keeps — but crossings now self-heal. The route summary additionally names every runway the route crosses or enters (`RouteRunwayCrossings.DescribeRunwayEvents`, from `TaxiRoute.RunwayEvents`): "crossing runway 10L twice", "entering runway 04L"; reciprocal designators of ONE pavement merge and speak BOTH names ("10L/28R") so every designator the tactical callouts will say is pre-announced. All designator compares route through `RouteRunwayCrossings.NormalizeDesignator` (zero-padding-proof, W-suffix water runways reciprocate). Pure-geometry coverage: `tools/ProgressiveTaxiProbe`.
- **Progressive Taxi terminator UI (`TaxiAssistForm`).** In Progressive Taxi destination mode the *last* taxiway row carries a terminator block (`cmbTerminatorType`: Hold short of runway / Hold short of taxiway / After crossing runway / End of last taxiway). The block is **self-contained** — it has its OWN runway-target combo (`cmbTerminatorRunway`, Alt+U; label switches per type: "Runway to hold short of:" / "Runway to cross:") plus the taxiway-target combo (`cmbTerminatorTaxiway`, which doubles as the optional "Cross at taxiway" for the after-crossing type). Do NOT reuse the per-row "Hold short of runway" combo for the terminator target. Relatedly, the **per-row "Hold short of runway" label+combo are HIDDEN in Progressive Taxi mode** (`SetRowRunwayHoldShortVisible`, called from `OnDestTypeChanged` + `AddTaxiwayRow`; hidden combos reset to "(none)" so a stale pick can't leak into the route — `GetUserRunwayHoldShorts` / `OnAddTaxiwayClicked` unchanged) — the terminator block is the single runway-hold-short control, while mid-leg crossings still get automatic hold-shorts. The per-row "Hold short" checkbox stays visible.
- **Progressive Taxi "Hold at named holding point" terminator (2026-07 — EGLL VIKAS ask).** A fifth terminator type routes a progressive leg to a **published NAMED holding point** (VIKAS, HANLI, N2E, A11…) and holds there — the designators real ATC uses at complex airports ("taxi to VIKAS, hold"). Source: OSM `node[aeroway=holding_position]` fetched by `OsmTaxiSource` alongside taxiways/parking (`AirportTaxiData.HoldingPoints`; name from `ref` with `name` fallback, kind from `holding_position:type` — runway/ILS/intermediate; **unnamed painted hold lines are skipped** — only named points are pilot-selectable). `AugmentingAirportDataProvider.GetNamedHoldingPoints(icao)` exposes the cached raw points (no fetch of its own). **The augmentation safety rules apply unchanged:** the pure `Navigation/NamedHoldingPointResolver` (xUnit-pinned) attaches each name to a NAVDATA graph node — a scenery-designated hold-short node (HS/IHS) within 15 m wins over any nearer plain node (the painted line beats the centerline vertex beside it); otherwise nearest non-parking node within 30 m; **no node within 30 m → the point is DROPPED** (a mislabeled hold is worse than an omitted one), and the route target is always the navdata node's coordinates, never the online point's. Duplicate names (parallel painted lines: EGLL A4/SATUN) collapse to one entry (designated-snap beats plain, then smaller snap distance). UI: `cmbTerminatorHoldPoint` ("Named holding &point:", Alt+P) lists `DisplayLabel`s like "VIKAS (intermediate hold)"; empty airports show "(none available at this airport)". The combo is filled on airport load and re-resolves on dropdown open until the online source has been seen (`_namedHoldingPointsResolved`), so a late background fetch still surfaces without rescanning the graph on every dropdown open at an airport whose points all dropped. Arrival speaks *"Hold at VIKAS. Set a new route when cleared."* (`ProgressiveTerminatorType.HoldAtNamedPoint`). **This is ADDITIVE-ONLY: it is a new route DESTINATION type and does not touch the tuned runway-crossing hold-short derivation** (`HoldShortNodeResolver` / the automatic runway hold pass, `RouteRunwayCrossings.InsertRunwayHoldShorts`) — the separate "OSM holding_position → sharpen hold-shorts" idea (feeding these positions into the hold-short DERIVATION) remains deferred per the CLAUDE.md invariant — that pipeline is heavily tuned and needs its own in-sim-verified design session. Coverage varies by airport (EGLL: 96 named points, ~83 % resolvable; many airports have none — the feature silently degrades to the empty-list sentinel). **The snap radii are MEASURED, not guessed — do not tune them.** Probed 2026-07-27 against the owner's fs2024 navdata joined with live Overpass data at EGLL/EDDF/LOWW/LFPG/EHAM/KJFK: requiring a designated node for runway/ILS kinds loses 14 real points at EDDF and 3 at EHAM while gaining nothing (at EGLL every runway/ILS point already snaps designated); rejecting any snap that moves the target runway-ward rejects CORRECT designated nodes, because navdata's HS node routinely sits up to 14 m runway-ward of OSM's painted line (EDDF designated snaps 53 → 31, LOWW 22 → 6); and widening `DESIGNATED_SNAP_M` to the full `MAX_SNAP_M` leaves coverage identical but makes 4 of 7 changed points jump onto a DIFFERENT hold line — EDDF M15 (a runway hold 218 m from the centerline) lands on an HS node 23.7 m away that sits 126 m out, i.e. ~91 m runway-ward. The 15 m preference is tight so it can only pick the hold line the point actually sits on; the 30 m cap bounds worst-case runway-ward movement (26.5 m observed, all at intermediate/untagged holds far from any runway). **Reachability:** the resolved node is checked against the aircraft's `ComponentId` at Calculate time and refused with *"Cannot taxi to X from your position. Check your entry."* — this is the only terminator whose target is found by NAME across the whole graph, so unlike the others it can land on a disconnected island (LOWW/KJFK 6 components, EHAM 4, GCLP's 13-node S5 island), and `LoadRoute` snaps its start node into the DESTINATION's component with no distance bound. `SnappedToDesignatedNode` describes the chosen NODE; duplicate-name ranking keys on a separate internal `WonDesignatedPreference` flag so the two can never be conflated. Resolve outcomes (raw/distinct/resolved/dropped, plus per-point snap distance) go to `taxi_router.log`.
- **"Where Am I" (Output > `Alt+Y`)** — `TaxiGraph.DescribeLocation(lat, lon)` returns `Taxiway X` / `Gate X` / `Runway X` for the airport the aircraft is AT (`CurrentAirport.Resolve`). It does NOT depend on guidance being active; the manager caches a query-only graph in `_whereAmICachedGraph`. **Ground-only by design** — gated on `MainForm._lastOnGround` (cached from `SIM_ON_GROUND`); announces `"In flight."` when airborne. Airborne queries belong to the separate LocationInfo hotkey (city/terrain). **Runway detection** uses `TaxiGraph.RunwayCenterlines` — paired runway-start positions from the navdatareader `start` table — not `taxi_path.type='R'` edges (the DB has none). The pair is found by reciprocal designator first, then reciprocal heading within ±15°, with a threshold separation of 200–6000 m; on-runway membership uses `RunwayShape`. Without this, a pilot standing mid-runway only got a "Runway X" callout within 50 m of a threshold node. Note: hotkey must NOT collide with output `Shift+Y` (`HOTKEY_STATUS_DISPLAY`) — Win32 silently rejects duplicate-chord registrations.
- **Landing Exit Planner (Input > `Shift+X`)** — pre-touchdown exit picker. `LandingExitPlanner` edge-detects airborne→on-ground with GS ≥ 40 kt and auto-activates `TaxiGuidanceManager.LoadRoute(...)` using the pre-built graph. Reuses the existing ILS destination runway/airport (via `simConnectManager.GetDestinationRunway()`/`GetDestinationAirport()`) when set, otherwise the loaded flight plan's arrival airport and runway (`LandingExitPlannerPreset`) — do not duplicate runway-selection UI. **MainForm's SIM_ON_GROUND handler always uses `RequestAircraftPositionAsync` to feed `ProcessGroundState`** — do NOT trust `LastKnownPosition` here. The cached position is updated by the VISUAL_GUIDANCE / TAKEOFF_ASSIST / TAXI_GUIDANCE mirror paths, by ground traffic and TCAS while their own polls run, and — since PR #230 — by `AirportSurroundingsMonitor`'s own 2 s request whenever either surroundings callout switch is on, in flight as well as on the ground. With all of those idle (no route active, no surroundings switch on, ground traffic/TCAS not polling), the cache still goes stale during a hand-flown approach with visual guidance off — it stays at whatever the last active path left there (typically the departure-airport taxi-out at GS ~10 kt). Feeding that stale GS to `ProcessGroundState` fails the planner's `GS ≥ 40 kt` "real landing" gate and the activation is silently skipped. The async request adds one SimConnect roundtrip (~33 ms at 30 Hz) — negligible inside the rollout window — and guarantees fresh GS / lat / lon at the moment of the SIM_ON_GROUND change. `_activatedThisLanding` inside ActivateGuidance + a HasPendingExit recheck inside the async callback together prevent double-fire if SIM_ON_GROUND bounces (oleo flicker on hard landings). The `lastKnownPosition` mirror in cases 505/506/507 of SimConnectManager remains because other consumers (TCAS altitude diff, WeatherRadarForm altitude readout, Where-Am-I) still benefit from a fresher cache — but the landing-exit gate cannot rely on it. **`SetExit(..., bool currentlyAirborne)`** arms `_wasAirborne` from the actual air/ground state, NOT unconditionally true. Source: `simConnectManager.LastKnownOnGround` (mirrored from MainForm's SIM_ON_GROUND handler, and also refreshed by every `AIRCRAFT_POSITION` response in `ProcessAircraftPosition` since the GSX PR added `SIM ON GROUND` to that struct — the position-based write is typically fresher). Wrong-side fix: setting unconditionally to `true` while ON THE GROUND would meet the activation condition on the next ground-state event with GS≥40, false-triggering during a high-speed taxi or rejected takeoff. Honoring actual state means an on-ground plan correctly waits for the next takeoff+land cycle. Form's runway combo items each carry their own wind suffix in display text (`RunwayChoice` wrapper, refreshed via `RefreshRunwayItemsWithWind` when the async `RequestWindInfo` callback resolves — marshal back to UI thread via `BeginInvoke`); the screen reader reads "30R, 12 knot headwind" on focus during dropdown navigation, no separate post-selection announcement needed. Suffix suppressed when `|headwind| < 3 kt`. Don't auto-recommend a specific exit — that needs aircraft-perf data we don't have; let the pilot judge from the wind number.

  **Rollout-phase tone gate and overshoot retarget (`TaxiGuidanceManager.UpdateLandingRollout`).** The handoff from `LandingRollout` to `Taxiing` fires on `turnBegun` (among a few other signals — see Flow step 7 above for the full list), where `nearExit = distToExitFeet < ROLLOUT_NEAR_EXIT_FT = 500` feeds the `atTaxiSpeed && nearExit` member of that set. The pure `atTaxiSpeed` (GS < 30 kt) condition was wrong — on a long runway the aircraft routinely drops below 30 kt thousands of feet upfield of the planned exit, and resuming the tone there suggested "turn now" while the pilot was still on the runway centerline. **Do not relax the `nearExit` gate back to a speed-only condition.** `turnBegun` is `RolloutExitGate.IsExitTurnBegun`: `hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG (15°) && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS (90 kt)`, **plus (2026-08-21, see the KSEA 34L subsection above) the deviation must now also be toward the exit's own side and begin within `RolloutExitGate.TurnWindowFeet` (1,000 ft) of the exit or past it** — the bare heading/speed pair alone is necessary but no longer sufficient. The speed cap is critical: above 90 kt a heading deviation from runway centerline is touchdown yaw, crosswind crab alignment, or sim physics at wheel contact — not a real runway exit maneuver. Category E rapid-exit taxiways top out at ~90 kt, so legitimate high-speed exit turns are still detected. **Do not remove the `ROLLOUT_TURN_MAX_GS_KTS` guard** — a crabbed approach at KJFK 22L caused the heading to drift 16° from runway heading within 2 seconds of touchdown at 112 kt, falsely triggering the handoff 5,077 ft before the planned exit. Once a handoff signal fires, an early-vacate retarget and a reachability guard run before the pilot is committed to the re-route (`RolloutExitGate.MatchEarlyVacateExit` / `IsHandoffRouteReachable`, either of which can conclude guidance instead of routing) — but the two closure reasons are split: the reachability guard only sets `_landingExitVacatedEarly` (closure: "You have left the runway short of X") when `_landingExitVacatedEarlyPlannedName` is already set from a genuine preceding early vacate, and otherwise sets `_landingExitRouteUnreachable` (closure: "Exit guidance ended: no usable route from here…", no positional claim), because the guard can also fire when the pilot vacated at or past the planned exit, where "short of" would be false — again, see Flow step 7 and the KSEA 34L subsection above rather than a third copy of that mechanics here. Independently, the rollout steering tone itself is no longer a plain silent/active toggle — it now runs the three `RolloutExitGate.SelectToneMode` modes (`Silent` / `DriftCorrection` / `ExitBearing`) described in Flow step 7. On overshoot — aircraft along-runway projection past the chosen exit by ≥ `ROLLOUT_OVERSHOOT_FT = 100` ft AND heading still within `ROLLOUT_TURN_BEGAN_HDG_DEG = 15°` of runway heading — the manager scans `_rolloutAllExits` (cached at `BeginLandingRollout` time from `_graph.GetLandingExits(runway)`, sorted by `DistanceFromThresholdFeet` ascending) for the first exit further downfield and re-`LoadRoute`s in place via `RetargetLandingExit`. If no downfield exit remains, `EnterRunwayEndCountdown` clears `_route` / `_destinationNodeId` and switches into runway-end countdown mode — the route nulling is what prevents `TryRecalculateRoute` from routing back across the runway to the now-passed exit (the original bug). `BeginLandingRollout` now takes `Runway runway` and `List<LandingExit> allExits` parameters; the planner computes the exit list once at touchdown. **`TryEarlyExitHandoff` (at ≤50 kt within 300 ft) only fires for High-speed exits (`ExitType == "High-speed"`, angle < 50°).** For Normal and End exits (angle ≥ 50°) the extension node is too far off the runway heading to give useful tone steering 300 ft before the junction — a 90° exit immediately pans the tone to maximum and the rollout's own 150 ft "turn now" callout is silently lost because state has already moved to Taxiing. Normal exits (50–110°) rely on the 150 ft verbal callout from `UpdateLandingRollout`; at the same moment the verbal fires, the rollout tone switches its desired heading from "bearing to the junction" to `ExitBearingTrue`, giving an immediate hard-pan toward the exit direction. The bearing-to-junction heading fights the turn at this range (junction is still ahead, so heading error flips to the wrong sign as the pilot turns off the runway), whereas `ExitBearingTrue` correctly decreases as the pilot aligns, conveying both direction and "how much more to turn." The heading-error smoother is reset at this transition so the pan is sharp rather than ramping from the near-zero approach residual. The tone continues until `turnBegun` fires (15° heading change), at which point the Taxiing handoff re-routes from the live position to the extension node. End exits are excluded from the ExitBearingTrue switch — a backtaxi requires a ~180° turn whose direction is ambiguous in the heading-error sign; the verbal is sufficient. **Do not restore `TryEarlyExitHandoff` for Normal/End exits** — it caused the EGNX runway 27 / taxiway M (90°) miss: tone went max-left at 300 ft before M junction with no verbal cue, pilot couldn't respond in time. **At every handoff to Taxiing (turnBegun / exitedLaterally / alignedWithExit / atTaxiSpeed&&nearExit), always re-route from the live aircraft position** using the extension-node logic (ApronNodeId if set, else FindExitExtensionNode, else NodeId). This replaces the initial touchdown route — which goes through the taxiway network and gets a false "hold short of runway X" tag from the automatic runway hold pass because the route's destination sits on the runway — with a clean 1–2 segment route. **Do not revert to the ApronNodeId-only re-route** — Normal/End exits have ApronNodeId == NodeId and would keep the bogus initial route. **Post-high-speed-exit `ExitBearingTrue` floor (`_postHighSpeedExitMinBearing`) must release on a wrong-side route.** After `TryEarlyExitHandoff` fires for a high-speed exit, `ExitBearingTrue` is installed as a minimum-pan floor (the `_postHighSpeedExitMinBearing` block in `UpdatePosition`) so the tone stays panned toward the exit side through the shallow ramp. But `ExitBearingTrue` is the exit's first runway-edge bearing, which at some airports points to the OPPOSITE side from where the taxiway actually routes to the apron (CYVR M1 off 26R: first edge heads NW ~305°, but the M1 taxiway curves SOUTH; route bearings 256°→196°→134°→100°). The floor's `Math.Max/Min` clamp then forced the tone the WRONG way (right toward 305°) and snapped ~115° left the instant `turnComplete` released it at heading 305° — a violent L/R reversal on rollout (the reported "took us right, then abruptly left, back right" at CYVR 26R). Fix: the floor is now RELEASED (set to 0, permanently) the moment the live route steers clearly OPPOSITE it — `Math.Sign(headingError) == -Math.Sign(minError) && Math.Abs(headingError) >= FLOOR_OPPOSITE_RELEASE_DEG (10°)`. The opposite-SIGN test (NOT magnitude-vs-floor) is what distinguishes this from the shallow-RET case the floor exists for (EIDW S5, EDDB M3), where the live route runs ~parallel to the runway ON the exit side (same sign as the floor → `routeOpposesFloor` never fires, floor preserved). The 10° margin filters sensor noise so a single jittery frame can't permanently kill a legitimate floor. **Do not gate the release on magnitude-vs-the-floor or restore the unconditional `Math.Max/Min`** — both reintroduce the wrong-side hard pan. There is ONE further sanctioned release, added with the manual-landing work: once the aircraft is laterally CLEAR of the runway pavement (`IsWithinRolloutRunwayLaterally` false — half-width + `RUNWAY_CLEAR_MARGIN_M`), the floor is released unconditionally. This is a POSITION test, not a magnitude one: the distortion the floor exists to bridge (the exit node sitting off to the side of a runway the aircraft is still on) has expired by construction once the pavement is behind, and the CYVR wrong-side case is still caught earlier, on the pavement, by the untouched sign test. Without it the floor kept capping the live route's own steering after the exit — LOWS 15 → E went silent for 380 m at 33 kt with the aircraft on the wrong taxiway. NOTE: the over-eager undershoot retarget that *exposed* this at CYVR (M6→M1 the instant GS dipped below the 50 kt high-speed threshold, while M6 was still comfortably reachable) is a separate, unfixed contributing factor.

  **`TryEarlyExitHandoff` anchors the re-route START on the chosen exit taxiway (2026-07-18).** The early handoff fires while the aircraft is still on the runway *short* of the exit, so re-routing from the live snapped position can grab a NEIGHBOURING exit's node as the nearest. EIDW 28L vacating S6 (reproduced from the user's navdata): at handoff the aircraft was abeam the mouth of **S5** (nearest graph node 26 m away) while committed to **S6**, so `LoadRoute`'s `FindNearestNodeInDirection` snapped onto S5 and A\* routed **S5 → parallel taxiway S → S6** to reach the S6 apron node — a **628 m / 325° hairpin** up to the far apex and back. The apron TARGET node (`ApronNodeId`, ~44 m off the centreline) was CORRECT; only the START snap was wrong. Fix: `TryEarlyExitHandoff` passes `startTaxiwayName: _rolloutExit.TaxiwayName` to `LoadRoute`, which — **only when no `taxiwaySequence` is given** (the new branch is the `else` of the sequence path, so it never fights a clearance) — snaps the start to the nearest node ON that taxiway (`FindNearestNodeOnTaxiway`, `requiredComponentId`-filtered) instead of the nearest node overall. That gives the direct **213 m / 35° route straight up S6**. Still live-position-anchored (it *is* the nearest node on the exit, ~91 m ahead), so the look-ahead tone and the first-segment sanity gate are unaffected; an empty exit name (unnamed exit) → `null` → legacy nearest-node snap. **Only the EARLY handoff is anchored** — the FINAL handoff (`turnBegun`/`exitedLaterally`, line ~485) fires once the aircraft is already ON the exit, so its live snap already lands on the right taxiway; leaving it unchanged keeps the "re-route from live position" invariant intact. `RetargetLandingExit` is likewise unchanged. Needs in-sim re-verification: EIDW 28L → S6 (route now goes straight up S6, log shows `startTwy=S6` and a low `segs=` count, no S5/parallel-S loop); plus a Normal ≥50° exit (verbal-cue path, unaffected) and an End/backtaxi exit (regression).


  **The first cleared taxiway's ENTRY node is ranked by GRAPH distance, never Euclidean (`TaxiRouter.FindNearestNodesOnTaxiway`, 2026-08-07).** Same rule `FindNearestNodeOnTaxiwayToTarget` already follows for taxiway EXITS (the KDEN M4 dead-end case) — it just had never been applied to the ENTRY side. Straight-line distance treats a node the aircraft would have to taxi *past* the real junction to reach as equally close, and when it wins by a hair the route enters the cleared taxiway at the wrong end and immediately doubles back. Motivating defect, **EVRA, clearance "taxi to 218 via C then P", 2026-08-07**: taxiway C runs east-west with its junction onto F in the middle and a stub running west to the runway hold. From the aircraft's position the C/F junction was **454.1 m** away in a straight line and C's west end **452.8 m** — 1.3 m closer, so the west end was chosen; the route then ran the pilot 111 m west past the junction to that dead end and 111 m straight back east along C (the live log shows consecutive segment bearings of 275° then 95°). By graph distance the junction wins by the 111 m it actually is nearer, which is the point on C the aircraft genuinely reaches first. Unreachable candidates are kept but ranked behind every reachable one via a **finite** `1e9` offset plus their Euclidean distance — `double.MaxValue + x` is a no-op in floating point, which would tie every unreachable candidate and destroy their ordering. Pinned by `TaxiRouterEntryPointTests`, whose fixture uses the real EVRA coordinates so the 1.3 m near-tie is reproduced exactly rather than approximated.

  **Implicit-exit `ExitBearingTrue` shallow-angle override (TaxiGraph.cs).** For airports whose navdata contains no `HS`/`IHS`/`HSND`/`IHSND` markers (every vanilla MSFS 2024 implicit-exit airport — EDDB, LGAV, and most regional fields), `GetLandingExits` falls through to the implicit-exit path. The runway-edge node's only outgoing edge is often a 30-50 m connector stub that runs nearly parallel to the runway before the taxiway curves off, giving a misleading first-edge `ExitBearingTrue` (e.g. EDDB 24L → M3 stub bearing 255.7° / 6.9° off runway, vs. real M3 end-to-end direction ~280° / 31° off). The shallow-angle BFS override block widens the gate from `exitAngle < 5°` to `exitAngle < 20°` to mirror the parallel HS-style override at the next block. **Both override guards (apron forward-direction AND apronAngle > currentAngleFwd) are required** — without the `> currentAngleFwd` guard, an exit whose stub points further off-runway than its eventual apron node would have its bearing *narrowed* by the override, a regression vs. the first-edge value. `ExitAngleDegrees` is intentionally NOT updated here — matches the pre-existing HS-branch pattern; changing the angle would touch `ExitType` classification, the angle-proportional overshoot margin formula at `:1462`, and the `alignedWithExit` heading-delta requirement, each with separate regression risk. EIDW S5 / N4 are unaffected (HS-type, use the parallel branch). EGNX 27/M and other ≥20° normal exits are unaffected (above the gate). LGAV 03R D8/D9 (the airport the post-handoff pastExit guard at `:1448` was added for) is benign — a wider `ExitBearingTrue` makes `alignedWithExit` MORE restrictive, not less, so A/P jitter is even less likely to false-fire alignment than before.

  **`exitedLaterally` combined gate (TaxiGuidanceManager.cs).** The primary lateral-exit handoff trigger in `UpdateLandingRollout` is `lateral >= halfWidth + 30 ft AND (distToExitFeet <= 250 OR hdgDeltaAbs >= 8° OR pastExit)`. The bare-lateral version fired too eagerly when the pilot drifted laterally before the tone's `ExitBearing` phase engaged (gs > 50 kt, where the tone is `Silent`, or gs ≤ 50 kt beyond 300 ft, which was silent by the same design at the time of this fix but is now the audible `DriftCorrection` mode added 2026-08-21 — see the KSEA 34L subsection above; the gate itself is unchanged, only the tone underneath it). EDDB 24L → M3 reproduction: 129 ft lateral at distToExit=445 ft / hdgDelta=6.6° triggered handoff BEFORE the 150 ft "turn now" verbal cue, leaving the pilot off the runway with no directional cue. The dist gate catches "close enough that verbal cues have fired or are about to"; the hdgDelta gate (8° = half of turnBegun) catches "pilot has clearly committed to a turn"; the pastExit gate preserves the overshoot-detector path. True shallow RETs (< 8° real exit angle) still trigger via the dist gate as they close to ≤250 ft. **The passive-handoff `exitedLaterallyPH` check in the `_rolloutHandoffActive` block is intentionally NOT gated** — different semantics ("has the pilot committed?" post-check, not a handoff trigger); applying the same gate would delay clearing the overshoot monitor and could cause spurious retarget cascades. Commit b69a03d's pastExit guard handles the analogous A/P-jitter concern for the sibling `alignedWithExitPH` check.

  **Missed-exit rescue scan (`TaxiGraph.FindDownfieldExits`, 2026-08-23).** The overshoot handler used to
  answer "is there another way off this runway?" from `_rolloutAllExits` alone — the list
  `GetLandingExits` returns. That list is built for the **planner dialog** and is lossy on purpose in two
  ways that matter at 90 kt. It keeps one entry per taxiway name, and, decisively, the moment ONE node in
  the runway corridor carries a hold-short marker with a forward exit, `hasHoldShortOnRunway` switches the
  geometric fallback off for the **whole runway** — so every junction the scenery did not mark with a
  hold-short bar stops existing. Rapid-exit taxiways are routinely modelled that way, because they are
  one-way turnoffs with no hold-short line on the runway side, so a single marked crossing taxiway near the
  threshold can hide every RET behind it. Both choices are right for a menu a pilot reads before the
  flight; neither is an acceptable sole answer to a safety question whose fallback is a 180-degree
  backtrack on active pavement.

  **Reported case:** CYYZ runway 23 (11,122 ft), 2026-08-23. Planned exit H2, aircraft 104 ft past it,
  `allExits.Count=6` ending at H2 — *"Missed last exit on runway 23"* with 5,400 ft of runway and (in
  navdata) H4, J2 and H6 still ahead. The pilot rolled to the end and had to turn around on the runway.

  **Where "downfield" is measured from (`RolloutExitGate.DownfieldCutoffFeet`).** From whichever is
  further along the runway — the missed exit, or the AIRCRAFT — plus `ROLLOUT_OVERSHOOT_FT`. The
  aircraft is a FLOOR, not an extra margin: the exit-relative cutoff is unchanged and simply never
  allowed to fall behind the wing, so the only candidates this removes are ones already rolled past. It
  matters because a HIGH-SPEED exit is not declared missed until the aircraft is up to
  `ROLLOUT_HIGHSPEED_OVERSHOOT_FT` (500 ft) past it — a rapid-exit turn can still be started late — so an
  exit-relative cutoff would read a turnoff 200 ft beyond the missed exit as "downfield" while it sat 250
  ft BEHIND the wing, and the retarget would pan the tone back at it.

  **The scan.** When `FirstSuitableDownfieldExit` comes back null, `UpdateLandingRollout` calls
  `_graph.FindDownfieldExits(_rolloutRunway, downfieldCutoffFt)` — the same aircraft-floored
  cutoff `FirstSuitableDownfieldExit` was just asked with, never the bare
  `missedExitDistance + ROLLOUT_OVERSHOOT_FT`. That walks every
  corridor node beyond the cutoff whose named edges demonstrably leave the runway strip (same
  `MIN_FALLBACK_EXIT_ANGLE_DEG` test, same `ExitPathLeavesCorridor` BFS, same tolerances as
  `GetLandingExits`), **ignoring hold-short markers in both directions** — a marked node is as eligible as
  an unmarked one. Forward-peeling only: a stub past 90 degrees is the backtrack the scan exists to avoid.
  Findings go through `RolloutExitGate.MergeRescueExits` into `_rolloutAllExits` so the fall-forward,
  undershoot scan and early-vacate matcher keep reading one nearest-first list — and so a rediscovered node
  of the arc the pilot **just missed** is dropped (same name within `EarlyVacateMaxPassedFeet`, known entry
  wins). At CYYZ the H2 arc contributes five such nodes, the nearest 526 ft past the entry; steering back to
  one means crossing grass to a turnoff already behind the wing.

  **Measured, not assumed.** Swept across 558 runway directions at 120 large airports in fs2024 navdata,
  the rescue scan fires on **25** — and is silent on every one of the other 533, so a well-mapped runway is
  untouched. The affected set is not exotic: KCOS (planner list ends 821 ft down the runway, 10,172 ft and
  taxiway H still ahead), KLNK, WSSS, OERK, LTFM, HECA, EDDK, OLBA, KABQ, KMWH. Cost is a one-shot
  0.06–0.62 ms (EGLL, 5,366 nodes) at the overshoot decision — never per frame.

  **Diagnostics.** `BeginLandingRollout` and the no-exit verdict both now log the exit list itself
  (`allExits=[H2@5251/62deg H4@6727/6deg \u2026]`), because the CYYZ verdict was reached inside a loop that
  recorded nothing about what it looked at, and no amount of log reading could settle whether the list or
  the scan was at fault. **Do not remove those two `DescribeExits` calls** — they are the only channel that
  makes a repeat of this report answerable.

  **Runway-end countdown after a missed-last-exit (`UpdateRunwayEndCountdown`).** When `EnterRunwayEndCountdown` fires (overshoot with no downfield exit, a retarget LoadRoute failure, or `BeginRunwayEndCountdownRollout` at touchdown when a plan made for another runway finds no usable exit on the runway actually landed on), state stays in `LandingRollout` and a per-frame loop drives three voice callouts based on signed along-runway projection from `_rolloutRunway.StartLat/Lon` plus `Length`: *"Runway end in 1500 feet."* / *"Runway end in 500 feet. Slow down."* / *"Runway end in 100 feet. Stop."* — the 500 ft "Slow down" suffix is suppressed when GS ≤ 30 kt (still at taxi speed, the directive is noise); the 100 ft "Stop" suffix is **unconditional** (the pilot needs the action cue regardless of current speed). Hold-short and parking countdowns are also unconditional on their action suffixes. Tone stays silent — no steering target on rollout, pilot is on rudder/brakes. Ends by POSITION through `Navigation/RunwayEndCountdownGate` (xUnit-pinned), rules in order: laterally clear of the runway → *"Runway vacated. No route set — use the taxi planner for a route to your stand."* and `Taxiing` with `_route = null`, tone stopped first; heading ≥ 150° off the runway (turned around) → `BacktrackingOnRunway`; stopped (< `ROLLOUT_NO_EXIT_STOPPED_GS_KTS` = 3 kt) or turning (≥ 15° below 90 kt) within the 500 ft / 150 m runway-end milestone → `BacktrackingOnRunway`; stopped anywhere else → one *"Stopped on runway X. Runway end in N."*; otherwise the countdown continues, so a turn onto a taxiway mid-runway says nothing until the aircraft is clear. Backtracking says *"End of runway X. Turn around, heading H. Backtracking."* only near the end and *"Backtracking on runway X, heading H."* after a mid-runway turnaround; H is the MAGNETIC reciprocal (`RunwayHeadings.SpokenReciprocalMagnetic`, spoken 1-360) while the tone steers on the true one. The old rule handed ANY stop or 15° turn to backtracking and told a pilot turning off at a taxiway mid-runway to turn around (PR #236 review). **Keep `_rolloutRunway` cached through `EnterRunwayEndCountdown` — the countdown needs it.** Full silence on a missed-last-exit is unsafe for a blind pilot rolling toward the end of an active runway; the countdown gives them real braking information.
- **Connected-component-aware start-node selection.** `TaxiGraph.Build` runs a single BFS pass at the end to assign every `TaxiNode` a `ComponentId`. `LoadRoute` (and `TryRecalculateRoute`) look up the destination node's component and pass it as `requiredComponentId` to `FindNearestNodeInDirection` / `FindNearestNodeOnTaxiway`; candidates in a different connected component are filtered out. Same filter is applied to `TaxiRouter`'s private `FindNearestNodeOnTaxiway*` helpers via the from/target node's component. `_nextNodeId` in `TaxiGraph` starts at 1 so node ID 0 is a permanent "not set" sentinel — `TaxiGuidanceManager` uses `_destinationNodeId = 0` as the cleared-route marker, and a zero-based node ID would collide with that. Motivating defect: fs2024 navdata at GCLP models taxiway S5 as an isolated 13-node island (no graph connection to any other taxiway at either terminus). A pilot touching down on 03L near S5 had the start-node picker snap to an S5 node, and A* couldn't reach R9R (in the main 1075-node component) — the pilot heard "Could not calculate a route to the destination." and got silence during rollout. With the component filter the picker skips S5 and selects an R3 node ~187 m ahead instead. Applies to every `LoadRoute` caller, not just landing-exit, so the gate-to-runway taxi path at the same airports is protected too.
- **Stranded stand stubs are reattached; unreachable destinations are refused (OMDB B 18R, issue #228; narrowed in review 2026-09-14).** The component filter above misfired at OMDB. Gate B 18R's stand and connector are one `P` lead-in row whose open end stops 12 m short of taxiway U, so the stub stayed its own component and `LoadRoute` found no start node (*"Could not find a nearby taxiway node."*).

  **The bridge.** `TaxiGraph.BridgeOrphanParkingIslands` runs at the end of `Build`, after the first `AssignConnectedComponents`. It joins a component to the main one (the largest, `TaxiGraph.MainComponentId`) with a single `StandBridgePathType` edge, but only when **every** edge of the island is a navdata lead-in and the island holds a stand.
  - **Identity.** Stand and hold-short identity come from the navdata endpoint types `Build` records, never from `TaxiNode.Type`, which the parking pass stamps by proximity in any component.
  - **Island end.** Never a stand or a hold-short node.
  - **Network end.** Never a stand, a hold-short node, a node on runway pavement, or a node on another stand's lead-in chain. The chain is walked from each stand through two-neighbour nodes, with a 100 m cap; 3 of the bridges exist only because of that cap. Navdata draws a multi-segment lead-in as taxiway rows plus a final `P` row, so a "lies only on P edges" test never sees its bends.
  - **Runways.** A candidate pair whose straight line touches runway pavement (`RunwayPavement`) is skipped for the next closest pair within 50 m.

  **Why it was narrowed.** The first version bridged any component with one lead-in. Measured with the real `Build` over all fs2024 airports, that produced 1,144 bridges at 852 airports, 554 of them touching runway pavement. Route replays showed:
  - a gate route 94 m along VIJU runway 18/36 with no hold-short;
  - a crossing hold placed on ENAT runway 11/29;
  - a flipped landing-exit side at UKHH;
  - false "Crossing runway" callouts at SC42;
  - a 4.7 km loop at KPRC.

  Narrowed, and re-run over every airport that sweep found with a stand island, it gives 248 bridges at 112 airports (re-measured 2026-09-17 against the shipped fs2024 database via the committed harness `tools/StandBridgeSweep`, which links the production `TaxiGraph`/`RunwayPavement`/`RunwayShape` sources rather than reimplementing them — re-run it before trusting any further change to the bridging rule). An earlier measurement recorded 234; none of the PR #238 review's fixes on this branch is the cause — a per-airport diff of this sweep against a second sweep built from `TaxiGraph.cs`/`RunwayShape.cs`/`RunwayPavement.cs` exactly as they stood at commit `c1403ce1` (the only three files the runway-pavement/cap fix and its follow-ups touch, confirmed by `git diff --stat c1403ce1..HEAD` over the harness's full link set), run against the SAME database, is exit 0: zero airports differ — same 112 airports, same 248 total, in the same order. So every safety zero below is the narrowed rule's own property, not an artefact of the review pass. The 234→248 gap is most likely the fs2024 navdata itself having been rebuilt between the two measurements rather than any code change — navdata databases are user-rebuildable (see "Navdata database build" in CLAUDE.md) — but this environment has no snapshot of whichever database "234" was originally measured against, so it can't be confirmed either way. None is on or across a runway, or lands on a hold-short node, a stand or a lead-in chain, and no landing exit or downfield exit changes. The one backtrack-entrance difference is at WABI, where a bridged stand stub is the graph node nearest a runway end, so the entrance becomes reachable from that end. OMDB C 51L stays unbridged: the only node within 50 m of its stub is a bend on the neighbouring stand's taxiway Z lead-in. 13 network ends lie within 150 m of a runway centreline. The landing-exit corridor, the exit extension and the post-landing vacate walk never follow a bridge (`TaxiGraph.IsStandBridge`), so none of them can send a pilot from a runway to a stand.

  **Reachability.** `LoadRoute` and `TryRecalculateRoute` ask `RouteReachability` where the aircraft is before adopting a route.
  - **On runway pavement, or on no taxi edge** (not within `PavementTolerance` of any edge): the answer is `Unchanged`.
  - **On a different component from the destination:** the route is still built from the destination's piece, so it starts with a straight unmapped leg. When that leg touches runway pavement the route is refused, naming the runway. Otherwise the pilot hears a warning, delivered like the route-start turn cue: *"X isn't connected to the taxiway network you're on. The first N metres of the route aren't mapped."* when the destination is off the main network, or *"Your position isn't connected to the taxiway network. The first N metres to taxiway X aren't mapped."* when the aircraft is leaving an unconnected position.
  - **No start node in range, or no buildable route,** for a destination off the network: refused by name (*"No taxi route to X. It isn't connected to the taxiway network you're on."*) instead of the old *"Could not find a nearby taxiway node."*
  - **Delivery.** Turn, destination-ahead and curve callouts wait for the start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s, sized from a measured 10.4 s warning plus turn cue) to close, but only while `StartWarningChatterGate` judges the wait safe -- one that would otherwise be lost for good speaks immediately instead, interrupting the warning. The taxiway-change callout waits for the same window through its own, simpler gate (`TaxiwayChangeGate`): it has no early-clearing latch to lose, so it always defers while the window is open, then either speaks once it closes or drops the deferred name silently if the route has since moved on to a different taxiway. The window opens for the runway reach warning and for this warning, so a callout at a standstill can no longer cut either off. A refused load puts back the destination, lineup and graph state it had overwritten, so a refusal while taxiing leaves the route in progress untouched.

  At LFBP, Parking 40 from taxiway N3 used to start 265 m away across runway 13/31; it is now refused. Across fs2024, 13,498 stands at 2,502 airports sit on a piece of taxi network the main one does not reach (re-measured 2026-09-17 via `tools/StandBridgeSweep`, up from an earlier 13,234 at 2,430 — the same navdata-rebuild caveat above likely applies, though this figure wasn't independently re-diffed against the pre-fix code the way the bridge count above was; the bridges bring 254 more onto it, up from 239), so a route to them from a taxiway on the main network starts with the unmapped-leg warning, or is refused when that leg touches a runway; from open apron, off every taxi edge, nothing changes.

  **Not covered.** Stubs that stop beside a taxiway's middle, more than 50 m from its nodes (KGKY RGAS 34-36), are not bridged. Edge projection is a follow-up.

  **A bridge-only stand stub must never become a route start (PR #238 review, Task 6; narrowed further in the follow-up review, Important 1).** Bridging merges a stranded stub's island into the main component, so `requiredComponentId` — the ONLY filter `FindNearestNode`/`FindNearestNodeInDirection` had — can no longer tell "genuinely reachable real node" from "reachable only by crossing a guessed straight line into a stand." At an airport where real taxiway vertices sit only at junctions/bends (OMDB B 18R: the stub's connector 12 m off taxiway U, real U vertices 40 m away), the stub's connector can be the objectively nearest node to an aircraft holding on that taxiway, so a route opens inside the stand lead-in and crosses the fabricated line to get out — the first steering-tone target lands in a stand the pilot was never cleared into. `TaxiGraph` records every member of a successfully-bridged island (`IsBridgeOnlyStandStub`, set before the post-bridge component renumbering resets every `ComponentId`) and `FindNearestNode`, `FindNearestNodeInDirection` and `FindNearestNodeOnTaxiway` each take an `excludeBridgeOnlyStandStubs` opt-in (default `false`) that every route-start caller passes `true` for — `LoadRoute`'s lead-in and primary pickers, its constrained-route sanity advisory, `TryRecalculateRoute`'s two pickers (the heading-aware fallback AND `FindRemainingSequenceByPosition`'s taxiway-name walk), `SelectFirstTaxiwayEntry`'s Euclidean-nearest AND its Dijkstra anchor, the landing-exit early handoff's exit-taxiway anchor, and `TaxiAssistForm`'s Progressive Taxi terminator start. Destination lookups (gate/deice dropdowns) are left unfiltered on purpose — reaching the stand the bridge exists for is the whole point, so a stub must stay reachable as a destination.

  An earlier pass left `FindNearestNodeOnTaxiway` unfiltered on the reasoning that a "P" (stand lead-in) row is always unnamed — every stand-bridge test fixture at the time used `Name = ""` for one, an easy pattern to over-generalize from. Measured against the real fs2024 database this is false: 1,810 of 319,004 "P" rows carry a non-empty name (KNZY 503, KCLT 119, plus LEMG/LEMD/CYVR/EPWR) — `Build` adds a row's trimmed name to both endpoints unconditionally, regardless of `PathType`. So a bridge-only stub's node CAN register under a real taxiway name, including the same name a real network node carries (the EPWR/KCLT shape). `FindRemainingSequenceByPosition` (feeding `TryRecalculateRoute`'s A* start via an unbounded 50 m name-match) and three of `SelectFirstTaxiwayEntry`'s four exits (`anchor == null`, `bestId == -1`, the pre-snap distance gap — everything except the cost-ranked `best` node the Dijkstra branch returns) both fed straight off this method with no exclusion at all; a route cleared "via" a taxiway a bridged stub also carries could open inside the stand. `SplitEdgeAt` inherits the same membership for a node it mints on a "P" edge interior to an already-bridged island (e.g. a bend on a multi-segment lead-in, where neither endpoint is Parking-typed and the edge isn't the fabricated bridge itself) — otherwise a projected holding point there would mint an unmarked escape hatch around every filter above.

  `TaxiGraph.InsertHoldingPointNodeOnEdge`'s edge scan has its own, separate guard: a fabricated bridge's network end is guaranteed never a stand, so neither endpoint is ever Parking-typed and the pre-existing "a stand endpoint" guard cannot see it. It skips `IsStandBridge` edges explicitly, or a painted holding point within range could get projected onto the guessed bridge line and pin a Progressive Taxi terminator or a named holding-point departure to fabricated geometry.
- **Taxiway exit/intersection picking uses GRAPH distance, not Euclidean.** `TaxiRouter.FindNearestNodeOnTaxiwayToTarget` and `TaxiRouter.FindBestIntersection` both score candidate nodes via `ComputeGraphDistancesFrom(destinationId)` — a Dijkstra single-source shortest-paths run from the destination. Picking by straight-line distance silently fails when the closer-by-crow-flies node is a navdata graph dead-end: any path forward from a dead-end must round-trip back through its only neighbour, which is always worse than picking the other-end node. Original bug: KDEN gate A 60 recalc with `sequence=["M4"]` — M4's northern endpoint sits 1.56 km from the gate vs 2.13 km for the southern endpoint, so the Euclidean picker chose the northern endpoint, but the unconstrained final-leg A* then had to walk 540 m back south on M4 and loop 2.4 km around the airport to reach the gate. Graph distance correctly prices the round-trip into the dead-end's cost and picks the southern endpoint instead. The destination-side Dijkstra is hoisted to `FindConstrainedPath` (computed once per route build) and threaded through both helpers via an optional `precomputedDistFromTarget` / `distFromFinalDest` parameter — saves the Dijkstra-from-endNode runs on every taxiway-transition step. Euclidean is retained as a defensive last-resort fallback in `FindNearestNodeOnTaxiwayToTarget` only when Dijkstra finds no candidate (would only happen on a malformed graph where the ComponentId filter missed a split); `FindBestIntersection` does NOT have a Euclidean fallback — returning `-1` when every candidate is graph-unreachable is intentional (the old Euclidean code returned candidates A* couldn't traverse, producing silently wrong routes), and the callers' `-1` path (FindRunwayBridge / shortest-path) is the correct recovery.
- **Last cleared taxiway is honored as the route terminus when it branches off the destination (EIDW 28R via N2, 2026-06).** The graph-distance pick above DEGENERATES for the LAST taxiway when that taxiway holds short of / branches away from a runway destination the PRIOR taxiway already reaches: every node on it can only reach the destination by going BACK through its entry junction, so `FindNearestNodeOnTaxiwayToTarget` returns the entry node itself (`targetNode == currentNode`). The step then no-ops (`continue`) and the final unconstrained "to destination" leg routes onto the runway via the prior taxiway, **silently dropping the cleared last taxiway and giving NO hold short** — live at EIDW: clearance `F2 F3 F-OUTER N N2` to 28R, but taxiway N runs to the 28R threshold (4 m) while N2 is a ~450 m connector, so N2 was dropped and the aircraft was guided down N onto the runway (router log showed only 4 of 5 taxiways, no `Strict 'N2'`). Fix in `FindConstrainedPath`: when the last-taxiway target equals `currentNode` (the entry), force traversal ALONG that taxiway and SKIP the final bypass leg (`lastTaxiwayTerminal`) so the route ENDS on the cleared taxiway. Traversal target is picked in two steps: (a) `FindNodeOnTaxiwayNearestPosition` — the node geographically nearest the destination position (EIDW N2 → its hold-short end at 28R); (b) if THAT is still the entry, `FindNodeOnTaxiwayFarthestFromNode` — the node farthest ALONG the taxiway from the entry. **(b) is the LFPG `…R, R1` → 26R case (2026-06):** the 26R destination node sits closer to the R/R1 junction than to R1's far end, so nearest-to-position ALSO degenerated to the entry, R1 was dropped, the unconstrained final leg deviated off R1, and the aircraft (correctly on R1) read as off-route → a recalc fired. **Applies to BOTH** the multi-taxiway loop's last step AND the single-taxiway step-1 path (a lone cleared taxiway — e.g. a recalc trimmed to `R1` — is itself the last, same degeneration). Triggers ONLY when the step would otherwise no-op; a last taxiway that genuinely leads to the runway picks its far end and is unchanged. Needs in-sim re-verification at LFPG (`…R, R1` → 26R) + EIDW + a normal last-taxiway clearance (regression check).
- **Post-recalc sanity gate has TWO independent indicators.** `TaxiGuidanceManager.TryRecalculateRoute` rejects a recalculated route when EITHER (a) it's dramatically longer than what's left of the current route (`newRoute.TotalDistanceMeters > oldRemaining * RECALC_LENGTH_BLOWUP_RATIO + RECALC_LENGTH_BLOWUP_PAD_M`, i.e. >2× + 500 m) OR (b) its first segment heads >`RECALC_BACKWARDS_DELTA_DEG` (120°) away from the straight-line bearing from the aircraft to the destination. Either condition firing keeps the old route and announces *"Off route. Could not follow clearance. <reason>. Continuing on original route."* The previous gate only fired when the constrained search itself fell back to shortest path (`ConstrainedFallbackReason != null`); a constrained search that "succeeded" but produced a dead-end-backtrack route (KDEN: 48 segments, ~2960 m, when remaining was ~200 m) slipped through. Indicator (a) catches length-extreme cases; indicator (b) catches the dead-end backtrack signature regardless of total length. Both are needed: AND-ing them would let "long AND going forward" recoveries through, OR-ing them is the correct semantic. Thresholds are named constants near the top of the class so they're discoverable + tunable.
- **Initial constrained loads get a sanity ADVISORY; recalc never re-applies the full original clearance.** `LoadRoute` with a user taxiway sequence also computes the unconstrained shortest path and, when the constrained route exceeds `direct × CONSTRAINED_WARN_RATIO + CONSTRAINED_WARN_PAD_M` (2× + 500 m, mirroring the recalc gate), PREPENDS *"Warning: route via X is …; direct route is …. [Taxiway X is … from your position.] Check taxiway selection."* to the spoken summary (warning FIRST — the queued summary is interrupted by the first `AnnounceImmediate` tactical callout once rolling, so a tail-position warning never gets heard; KATL "via V" 7 km tour, 2026-06-11) — the route still loads (ATC can legitimately route long), the pilot decides. Motivating defect: KIAH "via FE" — FE is a cargo-area taxiway 1.5 km away across 26R; the gate was ~600 m away; the pilot got a silent 6,094 m tour with out-and-back loops. The advisory triggers on (and quotes) the **PRE-truncation `fullRouteMeters`**, captured before `TruncateToHoldShort` trims the tail — NOT the truncated total. A clearance that doubles back (cleared taxiways leading AWAY from the destination, forcing a loop) has its backtrack TRIMMED by truncation, so the old truncated-total comparison let an obvious detour slip under the 2×+500 m trigger (EHAM 18L via A12/B/N2/cross-27/E6, 2026-06-20: N2 and E6 sit north of runway 27 while the 18L lineup is south — the navdata had no clean E6→18L path — so the route went over 27 and reversed; the truncated 852 m sat below the threshold while the full backtrack was ~1.6 km, so the pilot got a silent 180° turn at the crossing instead of the advisory). Truncation only ever SHORTENS, and for a normal route `fullRouteMeters` is within ~60 m of the summary total (the hold-short trim) — far inside the 500 m pad — so using it never adds a false positive; it only catches genuine backtracks. Relatedly, when `TryRecalculateRoute`'s position-aware trim finds the aircraft near NO sequence taxiway, it now falls back to SHORTEST PATH — re-applying the FULL original sequence routed the pilot backwards through the entire clearance (the same KIAH session: a 126-node loop back to FE that only the post-recalc sanity gate stopped).
- **The landing-exit plan's runway is checked against the runway actually landed on (OMDB, issue #234, PR #236).** `LandingExitPlanner` still does not depend on which runway the SIM reports, but it no longer hands the PLANNED runway to the rollout as a measurement FRAME without checking that it describes the aircraft. The planner's runway box defaulted to the airport's first runway (OMDB 12L); a pilot who planned exit M13 against that default and landed on **30L** gave the rollout a frame rotated 179 degrees and offset onto the other runway, and on its FIRST update (`hdgDelta=179.2deg`, `signedAlongPast=+4570ft`, `lateral=1263ft`) `pastExit` and `exitedLaterally` both read true and it handed off to `Taxiing` — no touchdown callout, no approach calls, no turn cue. (The box now pre-fills the flight plan's arrival runway when no ILS destination is set, but a pilot can still land somewhere else.)

  **Identification — `Navigation/LandingRunwayMatch` (xUnit-pinned).** The runway list is the one the planner form built the graph from, captured by `SetExit`, so touchdown runs no database query. A candidate is a runway end whose pavement contains the aircraft — laterally by `RunwayVacateResolver.IsOffPavement` (half-width + 15 m), along-track from 50 m before its start (300 m for the manual landing assist's check in the flare) to 50 m past its end — AND whose heading is within 45 degrees of the aircraft's. The best-aligned candidate wins, except that among candidates within 3 degrees of the best alignment the smallest cross-track wins: near-parallel runway ends differ by hundredths of a degree, so an exact tie never happens, and without the window an overlapping parallel pair (LFCH 25/25L) was decided by touchdown yaw. Alignment is load-bearing: at an intersection the aircraft is on both pavements, and a pavement-only test read a KDCA 04 plan landed on 01 as a match and a KPHL 17 plan landed on 27R as the reciprocal end. `ReciprocalEnd` needs the planned runway's own twin (`IsTwin`: swapped endpoints within 30 m), so a crossing runway is always `DifferentRunway`.

  **Re-plan — `Navigation/LandingExitReplan` (xUnit-pinned).** `Matches` runs the original flow unchanged. `ReciprocalEnd` and `DifferentRunway` choose an exit from `GetLandingExits` on the runway actually landed on, in two passes. The first admits only exits the aircraft can slow down for comfortably — `RolloutExitGate.ComfortableExitLeadFeet`: 2 s at touchdown speed, then 2.0 m/s² down to the exit's turn-off speed (50 kt below 45°, 20 kt at 45° or more or for an unmeasured angle), never less than the floor — a stated assumption, not aircraft performance. Only when that pass finds nothing, even after `TaxiGraph.FindDownfieldExits` has been asked through `RolloutExitGate.MergeRescueExits` as the missed-exit handler does, does the second pass accept `RolloutExitGate.ExitLeadFeet` (`max(200 ft, 11 ft per kt)`, shared with the undershoot retarget scan), so no exit the floor offers is lost. The floor alone was tuned below 50 kt: at touchdown speed it asks 4-6 m/s², and offline across 408 fs2024 twin runway ends a pilot braking at 2 m/s² would have heard "Missed … Retargeting …" in about a third of re-planned landings. Within a pass the order is the pilot's own taxiway (reciprocal end only), else the first usable exit at or beyond the planned exit's distance from its threshold (the pilot's braking plan), else the closest one before it; usable also means a turn of at most 90 degrees — a rapid exit read from the far end is a backward hairpin. The pilot's own taxiway is the same turnoff seen from the other end: the same node, or the same name within 1,400 ft, and only on the same physical side of the runway. `GetLandingExits` keeps the node nearest ITS OWN threshold, so only 17% of planned exits come back with the same node, and 16% of same-name exits leave on the other side; a namesake on a different runway is never preferred. Only with no usable exit does `BeginRunwayEndCountdownRollout` run the runway-end countdown, and because no `LoadRoute` ran first it hands over this airport's taxi graph, data provider and ICAO and starts the steering tone (paused by `EnterRunwayEndCountdown`, resumed by the backtrack) — without them the backtrack that follows was silent and could not find a taxiway connection. `Unknown` — no aligned runway under the aircraft — starts no guidance for THAT landing (*"Touchdown. Runway not identified. No exit guidance this landing."*): the planned frame provably does not describe it. The plan is KEPT (`Navigation/LandingExitActivationPolicy`) and the message latched once per plan — the verdict rests on a single position sample taken at the ground bit, so consuming the plan made a bounce, a touch-and-go or a go-around fly the next approach with no exit guidance at all and no second word about why. Candidates are screened for what could be landed on (`LandingRunwayMatch.IsLandable`: never a closed record, never a water runway — the pilot's own runway is always judged) and the exits offered are screened through `Navigation/LandingExitVacateScreen`, so an exit mapped clear of the runway is preferred over a nearer junction that dead-ends on it, exactly as the planner dialog prefers one; a flagged exit is still offered when it is all the airport has. `landing_exit.log` records the verdict, the chosen exit, the rule and pass (`tier=`) that chose it, and for the pilot's own taxiway whether it matched by node or by name and how far apart.

  **One utterance.** The correction leads the touchdown sentence — *"Touchdown on runway 30L, not 12L. High-speed exit taxiway M12A in 4000 feet."*, or *"… No usable exit."* for the countdown — spoken through `AnnounceInstruction`, so Ctrl+Y replays it. The first approach milestone would otherwise `AnnounceImmediate` over it, so every milestone the aircraft is already inside, or reaches within `ROLLOUT_TOUCHDOWN_CORRECTION_LEAD_SEC` (9 s: the sentence measures 7.51 s through System.Speech at Rate 0, plus about a fifth), is retired and what it uniquely adds — "Slow down.", the turn direction, the runway-end distance — is folded in (`Navigation/TouchdownCallout`, on the time-based supersession rule shared with the crossing decline). Turn-now retires only when already inside. A landing on the planned runway speaks exactly the old sentence.

  **Position stream.** `BeginLandingRolloutNoGraph` (after a failed `LoadRoute`) and `BeginRunwayEndCountdownRollout` are the only rollout entries reached without a `Taxiing` transition, so they raise `PositionStreamRequired` and MainForm starts the stream. Do not start it on every `LandingRollout` state change instead: the crossing-decline loop re-enters that state about once a second.

  **Manual landing assist.** `LandingFlareAssistManager` runs the same check at flare engage and at touchdown and, when the aircraft is landing on a runway other than the armed one, steers its flare and rollout tones at the actual runway for that engagement and says so once (*"Flare guidance, runway 12R, not 12L."*, or on the rollout call after a silent flare); the armed runway is restored when the engagement ends. Its handoff-speed check now reads `IsLandingExitRolloutGuidanceActive`, which does not count the runway-end countdown, so its rollout tone no longer stops at 55 kt with nothing taking over. The moment taxi guidance TAKES OVER from a landing rollout — the exit handover, backtracking, the countdown's "Runway vacated", or any closure — the assist stops its tone SILENTLY on that frame (`LandingFlareAssistManager.StepTaxiHandover`, forwarded by MainForm from `StateChanged` and acted on after the taxi position update): taxi guidance's own sentence is the one utterance, and its tone, first audible on the next frame, the only one. Counting backtracking in `IsLandingExitTaxiSteering` instead would end the overlap but cut *"End of runway … Turn around"* off within a frame, because the assist's *"Rollout guidance complete"* interrupts. A Taxi Stop is not a takeover: the assist is independent of taxi guidance. When the assist ends by its own speed or turn rule after taxi guidance ran during the rollout, *"Rollout guidance complete"* is queued, never interrupting.
- **Landing-exit fallback when initial `LoadRoute` fails: full exit-geometry rollout, not runway-end countdown.** When `LandingExitPlanner.ActivateGuidance` cannot route from touchdown to the chosen exit (typically because the taxi graph is disconnected and the exit's connected component has no nodes near the touchdown zone), it calls `TaxiGuidanceManager.BeginLandingRolloutNoGraph(exit, runwayHeadingTrue, runway, allExits, lat, lon, settings, graph, dataProvider, icao, groundSpeedKts, correction)` to enter `LandingRollout` state with the exit set as the geometric target. The rollout's per-frame logic — distance callouts (1500 / 900 / 500 ft), steering tone, overshoot detection, undershoot retargeting — all use exit geometry directly, NOT the route, so they work without one. At handoff time (turnBegun / exitedLaterally / alignedWithExit), `UpdateLandingRollout` calls `LoadRoute` from the live aircraft position which by then IS in the exit's component, so the re-route succeeds and normal taxi guidance follows. **The caller hands `BeginLandingRolloutNoGraph` the graph, data provider and ICAO it routed with**, and the method stores them in `_graph`, `_dataProvider` and `_icao` for the handoff re-route. Never go back to relying on the failed `LoadRoute` having left them there: a reachability refusal restores those fields to their earlier values (null after `StopGuidance`, or another airport's objects), which silently skipped the handoff re-route and left the pilot with *"Exit reached. Route unavailable."* The precondition check at the top of the method still **logs a `RolloutDiag` warning if any is null but does NOT bail** (geometry-driven callouts and tone still work without them, but the eventual handoff re-route will fail). **`BeginLandingRolloutNoGraph` defensively nulls `_route` at entry.** The handoff-failure fallback in `UpdateLandingRollout` (`!handoffRerouted && _route == null` → *"Exit reached. Route unavailable. … use the taxi planner."* + `StopGuidance`) requires `_route` to be null on the NoGraph path. In the normal flow this holds because `OnTakeoffAssistActiveChanged` calls `taxiGuidanceManager.StopGuidance()` when TakeoffAssist activates at departure (`MainForm.cs:2785`). A pilot who hand-flies the departure without TakeoffAssist would otherwise carry the stale gate-to-runway `_route` across the flight — and on a NoGraph landing whose handoff re-route also fails, `FindNearestSegmentIndexFullRoute` would silently drive the steering tone against the stale departure-airport segments. `BeginLandingRolloutNoGraph` nulls `_route` defensively at entry to guarantee the invariant regardless of the takeoff path. **Runway-end countdown is the secondary fallback** — entered mid-rollout only when every downfield exit has failed to route (see `RetargetLandingExit` cascade below), and at touchdown only when a plan made for another runway finds no usable exit on the runway actually landed on (`BeginRunwayEndCountdownRollout`, see the runway-check bullet above). `_activatedThisLanding` is set to `true` so the fallback path is final for this landing; the planner doesn't retry on subsequent oleo bounces.

- **`RetargetLandingExit` cascades through downfield exits on `LoadRoute` failure.** When the initial retarget target's route cannot be built, the method walks `NextDownfieldExit` (first downfield exit beyond `ROLLOUT_OVERSHOOT_FT` with `ExitAngleDegrees ≤ 90°`) and retries `LoadRoute` against each successive candidate. The override-announcement is dropped when falling forward to a later exit (it described the originally requested target); the generic *"Missed X. Retargeting Y, Z feet ahead."* message is used instead. Only when EVERY downfield exit has failed does the method announce *"Missed X. No reachable exit remaining."* and call `EnterRunwayEndCountdown()`. Motivating defect: YSSY 16R retarget where one bad LoadRoute (degenerate target near the runway end) used to drop straight into runway-end countdown despite good earlier exits still being routable. **Cascade is state-safe** — `LoadRoute` mutates fields at the top (e.g., `_dataProvider`, `_destinationName`, `_icao`) but those are idempotent overwrites, and `_route` is set only AFTER the `route == null` check, so failed iterations leave `_route` untouched. Each iteration is independent. **Cosmetic edge case**: `prevName` is captured once at function entry from the old `_rolloutExit`. When an *undershoot* call (target earlier than `_rolloutExit`) cascades forward through `NextDownfieldExit` and happens to settle on the original `_rolloutExit` (e.g. no intermediate exit qualifies), both `prevName` and `newName` name the same exit and the announcement reads *"Missed taxiway X. Retargeting taxiway X, … feet ahead."* The steering tone and routing target are correct; only the wording is awkward. **Undershoot scan now requires a speed-proportional minimum lead**: `Math.Max(ROLLOUT_UNDERSHOOT_MIN_LEAD_FT = 200, gs · ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT = 11)`. Previously the scan picked whatever exit was nearest within `ROLLOUT_UNDERSHOOT_RANGE_FT = 1000`, which at YSSY 16R retargeted to taxiway L just 79 ft ahead at 52 kt — impossible to make — and then cascaded to a false "no exit remaining". **Implicit coupling**: at `gs ≥ 91 kt`, the min-lead floor (`91 · 11 = 1001 ft`) exceeds the scan range (1000 ft), so undershoot retargeting effectively no-ops. This is intentional (90 kt is the high-speed-exit ceiling, beyond which any retarget is unsafe anyway) but it's coupled across two unrelated-looking constants — the inline comment near `ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT` documents the interaction; keep both constants in sync if either changes.
- **900 ft RETIL-analog callout (high-speed exits only).** `UpdateLandingRollout` fires *"`<exit name>`, 900 feet."* between 500 and 900 ft from a high-speed exit, gated by `_rolloutApproach900Announced` and `ExitType == "High-speed"`. Analogous to the first Runway Exit Taxiway Indicator Light (RETIL) flash at ~984 ft — sighted pilots see the lights here, blind pilots get the equivalent verbal cue before the 500 ft "prepare to turn" window. **The 1500 ft callout's lower bound was tightened from 500 → 900 ft** so the two callouts don't overlap. Normal/End exits still get only the 1500 ft and 500 ft callouts; the 900 ft cue is reserved for high-speed exits because that's where RETIL semantics apply and Normal exits' 500 ft "prepare to turn, taxiway X" is already sufficient. **`_rolloutApproach900Announced` must be reset in all four sites that reset the other rollout-announce latches**: `BeginLandingRollout`, `BeginLandingRolloutNoGraph`, `EnterRunwayEndCountdown`, `StopGuidance`. RetargetLandingExit's success-on-cascade path also resets it so the new target's 900 ft callout can re-fire.
- **`GroundTrafficMonitor.SuppressCheck` — pluggable alert silencer.** Public `Func<bool>? SuppressCheck` predicate on `Services/GroundTrafficMonitor.cs`; when non-null and returning `true`, the 3-second poll tick early-returns without alerting. `MainForm.InitializeManagers` wires `groundTrafficMonitor.SuppressCheck = () => takeoffAssistManager.IsActive || taxiGuidanceManager.State == TaxiGuidanceState.Inactive || taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout;` — traffic callouts are silenced during the takeoff roll (where the pilot's hands are full on rudder + throttle and a traffic alert can't be acted on), whenever Taxi Guidance has no route engaged (no actionable context for the alert; the pilot is parked at the gate, mid-config, or post-stop), AND during the landing rollout (hands on brakes + rudder, and the exit/runway-end callouts must not be talked over). Predicate is read via `SuppressCheck?.Invoke()` which is safe under property-read-then-invoke even under interleaving (C# captures the property into a local first). When adding other suppression contexts (e.g. flare phase, hand-fly), prefer chaining additional predicates over adding more boolean state to the monitor. **Hotkey summary (`Alt+G` / `GetNearestTrafficSummary`) is intentionally NOT gated** — it's a manual lookup outside the `OnTick` polling loop and remains available at all times so the pilot can query nearby traffic on demand even when not under guidance. **Forward-arc filter (`FORWARD_ARC_DEG = 120°`) gates Caution and Warning, not Awareness.** "Slow down" and "Stop, traffic very close" fire only for traffic within ±120° of the nose; behind-arc threats are not actionable by braking. Awareness pings ("Traffic, behind, X feet") fire in all directions so the pilot retains passive awareness — the hotkey summary covers behind-arc details on demand.
- **`TaxiAssistForm` aircraft-position freshness.** OnCalculateClicked refreshes `_aircraftLat/Lon/Heading` from `_simConnectManager.LastKnownPosition` immediately before route construction. Without this, the route starts from wherever the aircraft was when the FORM was opened — typically a pre-pushback gate position — and the post-pushback aircraft is already off-route from frame one, triggering the 3-second off-route detector and an immediate recalc. The form's `_simConnectManager` is optional (defaults null for callers that don't have one) but MainForm always passes it. `LastKnownPosition` is updated by every position-bearing SimConnect path (visual guidance, hand-fly, etc.) so it's nearly always within a frame of truth, even when the taxi-specific position monitor isn't active yet.
- **Heading-independent start-node selection when a taxiway sequence is given.** `LoadRoute` first tries `_graph.FindNearestNodeOnTaxiway(lat, lon, taxiwaySequence[0])` and falls back to the heading-aware `FindNearestNodeInDirection` only if no node on the requested first taxiway exists nearby. Why: post-pushback the aircraft can be pointing 180° away from where the first taxiway is (e.g., ATC told them to face NE for pushback, but the cleared taxi route runs SW). The heading-aware fallback would pick an apron node "ahead" of the aircraft instead of the requested taxiway, the route's approach segments would diverge from the aircraft's heading, and the off-route detector would fire on the first frame of taxi. Snapping directly to the user's requested first taxiway makes the constrained route honor the clearance regardless of pushback orientation; the pilot will turn after pushback, and the lineup tone guides them onto the first segment naturally.
- **ILS spatial+heading fallback for orphaned fs2024 rows.** The fs2024 vanilla navdata extraction has roughly 200 ILS rows where `loc_airport_ident`, `loc_runway_name`, AND `loc_runway_end_id` are all NULL/empty (217 measured in a 2026-08-30 build; the count varies with installed scenery, and `OrphanIlsMatcher`'s class comment is the ONE place it is stated) — the ILS row itself is correct (right ident, frequency, location, heading) but the join columns weren't populated by navdatareader. KPHX, KORD, and several other major airports are affected (KPHX has 5 such orphans including 07R). fs2020 has zero orphans. `LittleNavMapProvider.GetILSForRunway` uses the direct `loc_airport_ident = ICAO AND loc_runway_name = name` query as the fast path; on miss it falls through to `GetILSForRunwayFallback` which: (a) looks up the runway end's threshold lat/lon and heading, (b) searches unlinked ILS rows within a 0.1° (~11 km) bounding box of the airport whose `loc_heading` is within ±5° of the runway heading (with ±180° wrap handling), (c) hands the candidate set to `OrphanIlsMatcher`, which picks the one nearest this runway's **centerline** — and only when no other runway end at the airport is nearer to it.

  **⚠️ The original rule was closest-by-straight-line-distance to the THRESHOLD**, justified as "localizer antennas sit on the runway centerline beyond the far end so closest-by-distance matching is unambiguous". That justification is FALSE at every parallel-runway airport and the rule is retired: the antenna serving a threshold is a full runway length away ALONG track (~3 km), while a parallel runway's antenna is only ~1-2 km away LATERALLY, so straight-line range is dominated by the along-track term and barely sees the offset that actually distinguishes one parallel from the next. Measured at KATL (Orbx scenery leaves `ils_ident` blank on all ten ends, so every runway takes this path; five parallels all on ~090/270): 08L's own localizer is 3,027 m from its threshold and 09R's is 3,011 m — a **sixteen-metre** margin decided it, so 08L, 08R and 09L all reported 09R's 108.90 MHz / 090 course, and 27L and 27R both reported 26L's 108.70. Swept across the whole fs2024 database the range rule mis-assigned **46 of 230** runway ends (LIMC, EDDT, KPHX, KDTW, LEZG, OEKF…). Cross-track separates them by ~two orders of magnitude instead: at KATL the correct localizer is 0.1-3.4 m off centerline and the nearest wrong one is 305 m off, and the sweep drops to 6 residuals — all six being stale ILS `name` labels rather than mis-assignments (ENSB's runway ends are name-swapped relative to their headings; FNLF/VAFA/ZKWS were renamed for magnetic drift). The **mutual-best** half is equally load-bearing and must not be dropped as redundant: cross-track alone still hands a closely-spaced parallel its neighbour's localizer when it has none of its own (KPHX 25L/25R are 246 m apart, and 25R took 25L's) — a runway with no localizer must report NONE, because a wrong ILS frequency read out to a blind pilot is worse than no frequency: they would tune and fly the localizer for the runway beside them. `MaxCrossTrackMetres` (300 m) is only a backstop for the UNCONTESTED absurd (VVLO's antenna is 9,971 m off the runway it would otherwise have been given, KDTW's 1,901 m, ZLLL's 2,229 m) — it is NOT the discriminator, and the whole-database result is identical anywhere from 250 m to 1,000 m, so do not tune it to "fix" a mismatch. The widest LEGITIMATE offset measured is 216 m (BIAR 19). Wired into both ILS code paths: `GetILSForRunway` (used by ILS-guidance lookup) and `CreateRunwayFromReader` (which sets `Runway.ILSFreq/ILSHeading` from `runway_end.ils_ident`, also empty for KPHX 07R in fs2024). For fs2020 users this is a no-op; for fs2024 users it re-links the orphans at query time without requiring a navdata rebuild. `ReadILSFromReader` is shared between fast and fallback paths so the projection stays consistent. **⚠️ The EFB Airport-Lookup runway-info box (`ElectronicFlightBagForm.GetRunwayDetailedInfo`) had its OWN inline ILS query that bypassed both protections** (fixed): (1) it was `SELECT * FROM ils WHERE ident=@IlsIdent LIMIT 1` — NOT airport-scoped, so for the 498 idents shared across airports it showed a DIFFERENT airport's ILS freq/heading/GS (e.g. ENSD 26 → DAUA 04); it now accepts ONLY rows scoped to this airport (exact airport+runway preferred, then same-airport) and otherwise falls through to the spatial+heading recovery (`GetILSForRunway`) — a bare ident-only row is NEVER trusted, because with ~200 orphans + 498 shared idents an unscoped tie can surface a foreign airport's row (live fs2024 cases: OMAM 31R, UIIR 32, DNMN 05, WSAT 36). (2) the ILS block was gated only on `runway_end.ils_ident`, so orphan runways (KPHX 07R) showed "No ILS available"; the else-branch now falls back to `LittleNavMapProvider.GetILSForRunway` (spatial recovery). **Rule: never query `ils` by `ident` alone — always scope by airport (+runway); anything unscoped goes through `GetILSForRunway` so it is spatially validated.**
- **DB operational-flag filtering — broad scenery compatibility.** `Database/Models/Runway.cs` has `IsClosed`, `IsLanding`, `IsTakeoff` flags (defaults: open / can-land / can-takeoff — PERMISSIVE). Read from `runway_end.has_closed_markings` / `is_landing` / `is_takeoff` via `LittleNavMapProvider.SafeReadBool(reader, columnName, defaultValue)` — handles missing column / NULL / int-as-bool gracefully. **TaxiAssistForm filters its destination dropdown to `!IsClosed`; LandingExitForm filters to `!IsClosed && IsLanding`.** Note the asymmetry: TaxiAssistForm intentionally does NOT filter by `IsTakeoff` — some third-party sceneries incorrectly mark runways with `is_takeoff=false` (a Navigraph/scenery data quality issue, not a real-world status), which caused those runways to vanish from the taxi-destination dropdown despite being perfectly usable for departure. Trusting `IsLanding` for the landing-exit picker is safer because the cost of routing to a landing-prohibited runway is much higher (live arrival inbound). Sparse DBs (most navdatareader builds) populate every row permissively → no behavior change. Rich DBs (third-party scenery, some Navigraph merges) → automatic filtering by `IsClosed` only on the taxi side. When adding new DB-backed fields that may not exist on every build, ALWAYS use `SafeReadBool` (or a similar safe-read helper) with a permissive default — do NOT use `Convert.ToInt32(reader["col"])` directly.
- **Per-row "Hold short of runway" picker** in `TaxiAssistForm` (mnemonic `Alt+O` — cycles across the first row + every dynamic row). `TaxiGuidanceManager.LoadRoute` accepts `Dictionary<int, string>? userRunwayHoldShorts` mapping taxiway-sequence index → runway designator; `ApplyUserRunwayHoldShorts` resolves the picked runway to a `TaxiGraph.RunwayCenterline` (matching against EITHER reciprocal designator — "10R" and "28L" name the same pavement), then honours the pick when the route enters or crosses that runway (`RunwayRouteClassifier`) at or after the START of the matching run of segments tagged with that taxiway, and places the hold with the same resolver as the automatic pass (`RouteRunwayCrossings.ApplyUserRunwayHold`, see "Runway crossings and entries"). Runs BEFORE the automatic pass, which shares that stop and keeps the pilot's label. If the route doesn't cross the requested runway anywhere from the named taxiway's first segment onward, or crosses it with no safe place to hold short, the method returns a warning string that's appended to the route summary announcement — the route still loads. **Scan must start at the FIRST run of segments tagged with the named taxiway, not the LAST.** At airports where the same-named taxiway continues across a runway crossing (e.g. KSFO D crosses 10R/28L mid-way and keeps the name "D" on both sides), the LAST D segment is already past the runway and the forward scan finds nothing — the user's correct "hold short 10R" pick gets silently rejected as "route does not cross 10R" even though auto-detect tags the within-D crossing correctly. **Geometry test must be reciprocal-aware.** Comparing a closer-threshold designator against the user's typed designator misses crossings closer to the opposite end of the same runway. Resolving to the `RunwayCenterline` once and classifying the route against it sidesteps both name-pair issues. **Duplicate-taxiway sequences** ("via N, hold short 15R, N, hold short 22R, N") use `priorOccurrences` (count of the same name earlier in `taxiwaySequence`) to pick the correct run — each sequence entry binds to a distinct maximal contiguous block of route segments with that `TaxiwayName`. The auto-detector remains the primary mechanism (covers most ATC clearances since FAA mandates hold-short of every crossed runway); the explicit picker is for confirmation and rare clearance/route mismatches.
- **Constrained-router runway bridge.** ATC clearances commonly contain consecutive taxiways separated by a runway crossing — e.g., *"K14 hold short 30L M17"* at OMDB. K14 ends at the 30L hold-short on its side; M17 starts at the 30L hold-short on the opposite side. They share NO graph node, so the previous `FindBestIntersection` failure path bailed to whole-route shortest path, ditching the user's clearance entirely. `TaxiRouter.FindRunwayBridge(currentTaxiway, nextTaxiway)` finds the closest pair of nodes between them within `MAX_BRIDGE_METERS = 200`; on success the constrained search routes along the current taxiway to its exit, free-A*-bridges across the runway, and resumes the constrained sequence at the entry on the next taxiway. The 200 m cap prevents silent half-airport jumps when an ATC clearance is genuinely wrong (in those cases, fall back to shortest path with a clear log line). Applied at both the step-1 (first→second taxiway) and the inner-loop (i→i+1) intersection lookups.
- **TaxiSteeringTone pulse-state reset.** `_pulseActive` is reset to `false` in both `Start()` and `Stop()`. Without this, a previous lineup session that ended with `SetPulse(true)` would leak its pulse state into the next route — first Taxiing-phase `UpdateHeadingError` call uses the width-scaled overload, which doesn't touch `_pulseActive`, so the inherited true would pulse the taxiing tone at 3 Hz. Always reset audio-modulation state on start/stop boundaries — don't trust caller-side cleanup.
- **TaxiSteeringTone volume refresh every sounding frame.** `SetTone` always calls `_toneGenerator.UpdateVolume(EffectiveVolume())` while sounding, regardless of `_pulseActive`. The previous "only refresh in pulse mode" optimization left the tone stuck at zero volume during a pulse→continuous transition: when the user is stopped-misaligned (pulse fires) and then starts moving, `SetPulse(false)` is called, but if the last pulse cycle had set the volume to 0 (silent half), the next frame in continuous mode skipped UpdateVolume and the tone stayed silent until something else triggered a state change (oversteer / going silent / Pause). Always refreshing the volume on every sounding frame is cheap (one float assign per ~30 Hz tick) and removes the entire class of bug.
- **Verbal turn direction is computed from aircraft heading, NOT route's static `TurnDirection`.** `ComputeTurnVerbalFromHeading(targetBearing, aircraftHeadingTrue)` derives the spoken "left / slight right / continue" from the angular difference between the aircraft's current true heading and the next segment's bearing — same input the steering tone uses for its pan, so the two always agree. The route's pre-computed `TaxiRouteSegment.TurnDirection` is `nextSeg.bearing - currentSeg.bearing` and assumes the aircraft is exactly on-axis with the current segment. When the aircraft is off-axis (post-pushback rotation, after a wide turn, brief deviation, or starting at the gate before moving) the actual turn it must make to align with the next segment can be the OPPOSITE direction from the route's intent — and the static verbal cue contradicted the (correct) tone. All three spoken sites — advance notice, "now" callout, status query (`GetStatusAnnouncement`) — go through the helper. The `TurnDirection != "straight"` predicates stay on the static field (those just check whether there's *any* turn at the junction; that doesn't depend on aircraft heading).
- **Parking listing parity with the gate-teleport dialog.** `TaxiAssistForm` (parking destination) builds its dropdown from `IAirportDataProvider.GetParkingSpots(icao)` — the same data source `GateTeleportForm` uses — labelled with `ParkingSpot.ToString()` (e.g. `"P 21 - Ramp GA Large (Jetway)"`). Routing endpoint is the nearest graph node within `MAX_PARKING_TO_GRAPH_M = 100 m`; the parking spot's actual lat/lon is the lineup convergence target, matching `SimConnectManager.TeleportToParkingSpot`. Don't drive the listing off graph parking-tagged nodes — that silently drops parking spots whose lat/lon lacks a nearby graph node (common in third-party scenery whose taxi paths lag the parking layout). **Empty-name gate-type spots render `Gate {n}`, NOT `Spot {n}`** (`ParkingSpot.Describe` → `IsGateType()` covers types 9/10/11/13/14; non-gate empty-name spots still read `Spot {n}`). **Graph-unreachable stands are KEPT, not silently dropped:** a spot with no graph node within `MAX_PARKING_TO_GRAPH_M` is resolved to `nodeId = -1`, listed with a `(no taxi route)` suffix, and `OnCalculateClicked` announces *"No taxi route to X. This stand can't be reached by the taxi network."* and bails instead of routing across non-pavement — so the pilot SEES the stand exists but is never sent onto the grass (anti-grass discoverability). Both added 2026-06-23 (commit `ffb4916f`); the rest of that gate-data-correctness branch is preserved on `gate-data-correctness-salvage`.
- **Destination runway hold-short — full-length by default, CAT III / ILS hold opt-in (`preferIlsHold`).** `TaxiGuidanceManager.TruncateToHoldShort(route, destinationName, preferIlsHold)` truncates a runway-destination route at the hold-short line and tags it. It scans the route backward for the latest `ILSHoldShort` (IHS/IHSND) and latest `HoldShort` (HS/HSND). **DEFAULT (`preferIlsHold == false`): hold at the line CLOSEST to the runway** — `Math.Max(truncateAtIHS, truncateAtHS)`, the full-length line (EGKK A1/M1), matching a normal ATC clearance. **CAT III / LVP (`preferIlsHold == true`): prefer the IHS over the HS ONLY when the two holds are within `SAME_APPROACH_IHS_MAX_M = 150 m` of each other** (the ILS hold genuinely just behind the CAT I hold on the same connector — EGKK A3/C3/M3, measured ~30–40 m behind the full-length line); otherwise it still takes the closest hold. The flag comes from the Taxi planner **"CAT III / low-visibility hold (LVP)"** checkbox (`TaxiAssistForm.chkCatIiiHold`, runway-destinations only), threaded through `LoadRoute(..., preferIlsHold)` and persisted in `_preferIlsHold` so a mid-taxi recalc keeps the same hold preference. **The 150 m same-approach gate is critical in the LVP branch** — an UNCONDITIONAL IHS preference placed the hold a whole taxiway early whenever the cleared route merely *crossed* an ILS-critical-area hold on a transit taxiway before turning onto the final connector (OMDB 30R via N12, fs2024: route runs down taxiway N — which carries IHS nodes ~620 m from N12's hold — then turns onto N12; the N IHS was wrongly picked over N12's real 30R hold). **Do NOT drop the gate, and do NOT make the IHS preference the default again** — the default must stay full-length (user decision 2026-07). Note the AIP hold names (A1/A3/M3) are NOT in the navdata — hold nodes are anonymous `HSND`/`IHSND` markers; MSFSBA names them after the nearest runway centerline ("Hold short of runway 26L"), so the checkbox is a full-length-vs-CAT-III toggle, not a per-named-point picker. The automatic runway hold pass (see "Runway crossings and entries") handles every runway the route crosses or enters and skips only the route's own arrival at the destination strip, so it won't double-tag.
- **Auto-inserted runway holds: route-level classification, not per-edge intersection or point-on-pavement.** The current rules are in "Runway crossings and entries" above. History worth keeping: the original test ("next segment's endpoint within half-width of the centerline") missed every crossing whose nodes sat more than ~half-width + 5 m out — KBOS to 33L via K/B/C, where C crosses 04L with its nearest node 35 m out, 04R at 26 m and 27 at 86 m, tagged only 04R and refused the pilot's explicit "hold short 04L" pick, while `CheckRunwayIncursion` (keyed on hold node TYPE and name) called out all three. Its replacement, a strict per-edge segment intersection, then lost or invented crossings at nodes on or centimetres from the line (P19, ESMX, KORD W5) and, framed on the start rows, could not see displaced-threshold bands (OMDB 12R). Do not restore either, and do not restore a blanket destination-name skip (2026-08-24). Don't disable this — VATSIM controllers expect it, and silently rolling across an active runway is a runway-incursion risk.
- **Crossings-based taxiway connectivity + full-airport fallback.** `TaxiGraph.GetConnectedTaxiwayNames(name)` BFS counts **named-taxiway crossings** (default `maxCrossings = 2`), not raw graph hops — walking along the seed taxiway and through unnamed connectors is free; only crossing into a different named taxiway consumes the budget. The previous hop-based 4-edge limit silently hid M1 from the M5 dropdown at KSFO (4–6 unnamed connectors physically lie between them) and similar patterns elsewhere. `TaxiAssistForm`'s "Add Taxiway" combo additionally lists every airport taxiway (via `GetAllTaxiwayNames()`) below the connected ones — the heuristic prioritizes the dropdown for the common case while the full list ensures the user can match any ATC-named taxiway even when the heuristic doesn't surface it. The constrained-path router and `FindRunwayBridge` resolve the actual route from any pair of selections. `GetReachableTaxiwayNames(name, maxCrossings)` is public for callers wanting a different budget.
- **Duplicate taxiways in the entered sequence are intentional.** ATC clearances commonly re-use a taxiway across a runway crossing (e.g., *"via C, hold short 04L, C"* at KBOS). `TaxiAssistForm.OnAddTaxiwayClicked` does not filter the dropdown by already-used taxiways — only the immediately-previous one is hidden, to catch accidental no-op double-picks. The router handles consecutive duplicates as a benign no-op step: `FindBestIntersection` resolves to the current node and the `currentNode == targetNode && !bridgedAcrossRunway` short-circuit at `TaxiRouter.cs` skips the redundant A* pass. The per-row user hold-short is sequence-index-keyed, so a hold-short on the first occurrence still tags the correct segment via `ApplyUserRunwayHoldShorts`. Do NOT add an "already used" filter back to the dropdown — it would break the KBOS-style clearance pattern and any other airport with the same topology. The immediately-previous taxiway is hidden from the dropdown ONLY when the previous slot has no hold-short configured — neither the row's "Hold short" checkbox nor a runway selected in its "Hold short of runway" combo. With a hold-short set, the same-taxiway duplicate is a legitimate "taxi to hold line, resume on far side of runway crossing" clearance (e.g., KBOS *"K, B, N, hold short 15R, N, hold short 22R, N"*) and must remain available. Do NOT restore the unconditional previous-taxiway exclusion — it blocks the very clearance pattern this code was written to handle.
- **Runway destination lineup uses the `start` table, not `runway_end`.** `TaxiAssistForm.PopulateDestinations` looks up each runway's lineup point via `IAirportDataProvider.GetRunwayStarts(icao)` and matches by `RunwayID`. `Runway.StartLat/StartLon` (sourced from `runway_end.lonx/laty`) is the **physical pavement edge** — it's hundreds of meters off the lineup point for runways with a displaced threshold (e.g., KLAS 26R has a 1407 ft displacement, putting the actual lineup point ~429 m west of the stored pavement-end coordinate). Anchoring the route destination AND the `_destinationThresholdMap` entry on the start-table position keeps taxi-lineup centerline math and `TakeoffAssistManager`'s centerline math (which reads `TaxiGraph.RunwayCenterlines`, also built from the start table) referencing the same physical coordinates. Fall back to `Runway.StartLat/StartLon` only when the start table has no entry for the runway name. **Do NOT revert to using `Runway.StartLat/StartLon` directly** — it silently routes the aircraft to a graph node hundreds of meters off the runway at displaced-threshold airports (the symptom: route ends on an adjacent taxiway and TakeoffAssist reports cross-track in the tens of thousands of feet).
- **Ground-speed announcer is mode-independent (every on-ground phase), not taxi-only.** Lives in `Services/GroundSpeedAnnouncer.cs`, owned by `MainForm`, fed by the always-on `GROUND_VELOCITY` continuous base variable (registered in `BaseAircraftDefinition.GetBaseVariables`). `MainForm.HandleSpecialAnnouncements` routes `GROUND_VELOCITY` events to `groundSpeedAnnouncer.ProcessGroundSpeed(value, _lastOnGround)` and returns true (suppressing the generic "value changed" announcement). It used to live inside `TaxiGuidanceManager.UpdatePosition` — which only ran while taxi guidance was active, so callouts stopped the instant takeoff assist took over or after touchdown before taxi guidance re-engaged. **Do not move it back into a per-mode manager.** **On-ground only**: `ProcessGroundSpeed` early-returns when `onGround` is false — GS callouts cover taxi, the takeoff roll, and the landing rollout, but are silent airborne. While airborne the bucket baseline is left FROZEN (not reset) so the first sample after touchdown announces the rollout speed immediately instead of spending a sample re-baselining. Controlled by `UserSettings.TaxiGuidanceGroundSpeedAnnounceInterval` (0=off, 5, 10) — the setting keeps that name to avoid a migration even though the behaviour is no longer taxi-scoped; it's surfaced in the Taxi Guidance Options dialog. Round-to-nearest-multiple via `Math.Round(gs / interval, AwayFromZero)` — 4/5/6 kt all read as "5 knots". Hysteresis: 0.5 kt margin past the rounding boundary before re-announcing — kills jitter at the midpoint. First on-ground sample establishes baseline silently. Goes through plain `Announce` (NOT `AnnounceImmediate`) so a fading "10 knots" callout doesn't displace the most recent actionable instruction in any feature's Repeat-Last buffer.
- **Tactical and safety-critical taxi announcements use `AnnounceImmediate`, not `Announce`.** `AnnounceInstruction` (the helper used for turns, hold-shorts, taxiway changes, lineup, arrival, and distance countdowns) calls `_announcer.AnnounceImmediate` internally — every tactical callout interrupts queued speech because the pilot needs the cue *now*, not after a fading "10 knots" GS callout finishes. The same applies to standalone safety callouts (speed warnings, runway-crossing alerts, off-route warnings, lineup-achieved, parking arrival). Two sites still use plain `_announcer.Announce`: (a) the `LoadRoute` route summary at start of guidance (informational, not time-critical), (b) the periodic GS announcer (must not displace the Repeat-Last buffer). When adding new taxi callouts, default to `AnnounceInstruction` / `AnnounceImmediate`; justify any plain `Announce` call explicitly.
- **Unreachable-runway safety net + tone slew limiter + initial big-turn cue (branch `fix/taxi-route-unreachable-runway-lineup`).** When a runway-destination route ends on a taxiway that only *parallels* the runway (no connector to the runway itself), the route used to silently hold short hundreds of metres off and the lineup tone panned forever (PHNL 04L: ~456 m off). Now: (1) `LoadRoute` measures the DESTINATION NODE's perpendicular distance to the runway centerline (`DestinationCrossTrackMeters`); beyond `RUNWAY_REACH_MAX_CROSS_M` (120 m — do NOT justify that by hold-short offsets, see the constant) it builds a warning — full detail in the box (`LastRouteSummary`), a **short** spoken form in `LastRouteReachWarning`. (2) The warning is spoken by `TaxiAssistForm` via `AnnounceImmediate` **after** `StartGuidance` (announcing it inside `LoadRoute` gets stomped by StartGuidance's first-taxiway callout); the spoken summary is skipped when a warning is present, and `START_WARNING_CHATTER_GRACE_SEC` (12.5 s) — open at guidance start whenever a route-reach OR unmapped-start warning is pending — holds the taxiway-crossing callout (handled and skipped while open, as before) plus the advance turn notice, the "… ahead." callout, and the curve cue so neither warning is cut off. The taxiway-change notice is held for the same window through its own gate (`TaxiwayChangeGate`, not `StartWarningChatterGate`): unlike the other three it has no early-clearing latch to protect, so it always defers while the window is open and, once it closes, either speaks or silently drops the deferred name if the route has since moved on. Hold-shorts / runway crossings / lineup bailout are never gated. (3) The runway-lineup phase fires a one-shot bailout when cross-track stays > `LINEUP_UNREACHABLE_CROSS_FEET` (400 ft) for `LINEUP_UNREACHABLE_SEC` (12 s) — catches recalc-built routes too. The **taxi tone is slew-rate-limited** (`SlewLimitToneError`, `TAXI_TONE_MAX_SLEW_DEG_PER_SEC` 60°/s, applied after the rate-lead projection, Taxiing only): a multi-segment index skip on a sharp corner (large aircraft cutting the turn) or an in-place recalc no longer slams the pan L↔R — it sweeps ~1–1.5 s; genuine turns pass untouched; baseline resets only on `StopGuidance` (so recalcs are smoothed, fresh starts snap). An **initial big-turn cue** speaks once at guidance start when the heading error to the first segment exceeds `INITIAL_TURN_CUE_DEG` (100°) — the post-pushback turnaround case — direction matching the tone; skipped when a reach warning is present.
- **Pavement lead-in onto the first cleared taxiway (large gap only).** When a user taxiway sequence is given and the nearest node ON the first cleared taxiway is more than `TaxiLeadIn.TriggerMeters` (75 m) from the aircraft, `LoadRoute` starts the constrained route from the aircraft's nearest *in-component* graph node (`FindNearestNode(..., requiredComponentId: destComponentId)`) instead of pre-snapping onto the taxiway. This lets `TaxiRouter.FindConstrainedPath` build its pavement-following lead-in (the Step-1 `AStarSearch`) onto the taxiway — apron taxilanes — rather than beelining across the apron/grass (CYYZ GB/GC → A: a 297 m beeline + 180° pivot, "in the grass crossing AJ", 2026-06-17). The lead-in is **accepted only** when the router honoured the clearance (`ConstrainedFallbackReason == null`) AND its distance is within `gap × 2.5 + 300 m` (`TaxiLeadIn.IsAcceptable`, a dead-end guard); otherwise the route is rebuilt from the on-taxiway node (today's behaviour) and the summary is prepended with *"Could not compute a path onto taxiway X along the apron; route starts on X."* On success the summary names the lead-in: *"Route to Runway 23 via A, H. First taxi via 4 and AJ to reach A. …"* (`TaxiLeadIn.Clause`). The ≤ 75 m common case (gate on/near its taxiway) is byte-for-byte unchanged, and `TryRecalculateRoute`/unconstrained routes are untouched. Verified in-sim at CYYZ (GB/GC → runway 23 via A, H, 2026-06-17): the route starts at the apron node and the lead-in tracks the AJ taxiway centreline within ~3 m onto A, replacing the earlier ~64 m straight beeline across the grass. Do NOT remove the pre-snap for the common case — it is the LEPA anchoring fix; the lead-in only replaces it when the first taxiway is far.
- **Taxi-data augmentation (online taxiway NAMES — branch `feat/taxi-data-augmentation`, separate from everything else).** `AugmentingAirportDataProvider` (`Services/TaxiAugment/`) decorates `IAirportDataProvider` BEHIND the interface, so `TaxiGraph.Build` and every consumer (route planning, Progressive Taxi, landing-exit, Where-Am-I, docking) get enriched data transparently — no caller changes. It fetches real-world taxiway names per-airport from **OpenStreetMap (Overpass)** + **X-Plane apt.dat (Gateway)**, geometrically overlays them onto the user's navdata, caches IN-MEMORY per-ICAO (`TaxiDataCache` = `ConcurrentDictionary` + TTL, NO disk → fresh every session), and never blocks `GetTaxiPaths` (returns navdata immediately, fetches in the background, raises `AirportDataUpdated`). **navdata is AUTHORITATIVE**: an existing navdata name is never overwritten (preserves add-on AIP names like OMDB KK/KG); online names only fill UNNAMED segments; online-only geometry is IGNORED (we only attach names to the scenery pavement the pilot actually taxis — never steer on an offset online line). **Works regardless of the user's database / add-ons** (purely additive; sparse navdata gets more, rich navdata stays). **Aliases**: when navdata and online name the SAME pavement differently (scenery "HAWKER" vs ATC "B"), the other name is stored as an alias, surfaced as a SEPARATE labeled dropdown entry ("B (HAWKER)" sits at the "B" position alongside "HAWKER" — not merged), and resolved to the canonical name at route time (`TaxiGraph.ResolveTaxiwayName`, single choke point in `LoadRoute`); names are normalized (`TaxiDataMerger.NormalizeTaxiwayName`: "K 2"="TWY K2"="K2"); a collision guard never remaps a name that is itself a real taxiway. **Freshness**: the active flight's departure + destination are force-fresh (force:true) via the MainForm triggers (nearest-on-ground / `GetDestinationAirport` / geofence ≤50 NM) AND `FlightPlanManager.LoadDeparture/LoadArrival`; geofenced nearby airports use the cache. **Parking/gates (REWORKED 2026-06-23, gate-data-correctness):** gate identity is AUTHORITATIVE from GSX-or-navdata and is NEVER overwritten by online data. The PUBLIC `AugmentParking(icao, spots)` (called for navdata via `GetParkingSpots` AND on the **GSX** list, which bypasses it) attaches online stand names ONLY as searchable `(online)`-tagged **aliases** via the pure, idempotent `GateAliasResolver.ResolveAliases` — an online stand aliases a gate only when their **numbers match AND any letters agree** (so navdata gate 15 never adopts a neighbour's "Gate 11B"; N/S de-ice pads never cross), with a 150 m sanity backstop; only info the identity lacks (concourse letter "A51", MARS suffix "53A") is added — a pure restatement adds nothing. Online **NEVER sets a Name/position and NEVER adds a selectable gate (anti-grass — online data can't move where you taxi)**; `spot.Aliases` is recomputed from scratch each call → idempotent. `StandId` (`Services/StandId.cs`) is the shared label→(letter,number,suffix) parser used by the resolver + `GateSearchFilter`. Empty-name gate-type spots render `Gate {n}` (not `Spot {n}`); a stand with no taxi node within `MAX_PARKING_TO_GRAPH_M` is kept but marked `(no taxi route)` and refused by the Calculate guard (was: silently dropped). **X-Plane apt.dat is the key gate source** (CYYZ "Gate 131", KATL "A12"/"B7"). The OLD nearest-distance gate-NAME fill (which corrupted identity — CYUL gate 15 → 'Gate 11B' from an offset apt.dat ramp) was REMOVED. **Telemetry** (replaces a mass census): each fetch logs a coverage line to `taxi-augment.log`. **Settings**: `UserSettings.TaxiAugmentEnabled` (default on) → `decorator.Enabled`; in-dialog checkbox + ODbL/X-Plane attribution + a "Refresh Taxiway Names" button that announces the names-added count (`GetLastCoverage` → "…: N added"). Internet assumed (MSFS 2024). Pure-logic is probe-tested (`tools/TaxiAugmentProbe`); the core taxi probes (`TaxiGuidanceProbe`/`ProgressiveTaxiProbe`) stay green. **Deep-reviewed (5-pass): no Critical issues; safety invariants (navdata authoritative, online-only geometry ignored, alias never remaps a real name) confirmed.** **Real-time (no manual refresh needed):** `TaxiAssistForm.LoadAirportGraph` AWAITS `PrefetchAsync(icao)` before building the graph (cache hit = instant, so dep/dest are immediate; only a never-fetched airport waits, with a status line), so the taxiway list + gate aliases include augmented names on FIRST open — a graph built on a cache-miss returned navdata-only and never rebuilt for the same airport. `MainForm`'s `AirportDataUpdated` handler calls `TaxiGuidanceManager.OnAirportDataUpdated(icao)`, which drops the cached Where-Am-I graph (`_whereAmICachedGraph`) when it was built pre-augmentation, so Alt+Y picks up fresh names on the next query (a manual Refresh propagates to Where-Am-I too). **Alias dropdown collision skip (OMDB 2026-06):** `TaxiGraph.GetAllTaxiwayNames` NOW implements its long-documented skip — an alias display label (`"Z (K)"`) whose normalized alias form is itself a real taxiway name is NOT surfaced. At rich, junction-dense airports a navdata segment's midpoint geometrically matches a DIFFERENT-named crossing online segment, producing ~hundreds of spurious cross-name aliases (OMDB: navdata `K` "aliased" to J/V1/W/Y/Z…). The `ResolveTaxiwayName` collision guard already routed the bare real name to the real taxiway; the missing dropdown skip meant those mislabeled duplicates still cluttered the list and, if selected, mis-routed. Genuine aliases (a scenery/ATC name NOT present as a navdata taxiway, e.g. "B (HAWKER)") still surface. **Coverage reality (telemetry):** at name-rich navdata airports augmentation adds FEW new NAMES (OMDB: +7 — K12/K21/P2A/P4A/V5/Z8/Z9; LFPG: apt.dat fills 851 unnamed connectors, +osm=0). OSM contributes more where navdata is sparse; apt.dat wins disagreements. Licensing detail for this augmentation feature is covered under "Licensing & data attribution" above.
- **No-op recalc suppression (taxi guidance, 2026-06).** `TaxiGuidanceManager.TryRecalculateRoute`'s accept block compares the recalculated route's distinct taxiway sequence to the CURRENT remaining sequence; if identical it returns early — current route, the "Route changed. Now via …" callout, the safety-critical countdown-latch resets, and the steering-tone re-slew are all skipped. A sharp turn ONTO a cleared taxiway (cutting the corner) laterally offsets the aircraft from the route's next segment long enough to trip the off-route detector, which then re-plans the IDENTICAL tail — reported live at LFPG as a spurious "Route changed … super sharp right" while turning onto N (route was unchanged: N B BD1 D1). The recalc cooldown is stamped by the caller before the accept block, so the no-op path can't re-fire each frame; a genuine reroute has a different sequence and proceeds as before.
- **Cold Temperature Altitude Correction (Output Ctrl+Shift+T) — offline calculator, aircraft-agnostic.** `Forms/ColdTemperatureCorrectionForm.cs` re-implements the FlyByWire EFB Performance-page `TemperatureCorrectionWidget` (which is a canvas/SimpleInput page a screen reader can't operate) as a plain accessible dialog. In cold air the pressure altimeter OVER-reads, so published minimum altitudes must be corrected UPWARD to keep obstacle clearance. Inputs: aerodrome field elevation (ft), reported temperature (°C), and a multiline list of published altitudes (one per line). Output: each "Published X ft → corrected Y ft (add Z)" in a read-only results box; a single altitude is also spoken via `AnnounceImmediate`. **Math is the EUROCONTROL Doc-2940 formula transcribed VERBATIM from the FBW source** (`ColdTemperatureCorrectionForm.CorrectedAltitude`, incl. the redundant `- fieldElevation + fieldElevation` term, and round-UP to nearest 10 ft) so the blind pilot gets the IDENTICAL number the sighted EFB gives — warm temps return the published altitude unchanged (never corrected downward). PURE offline arithmetic — no SimVars, no Coherent, no aircraft required, works in planning on the ground on EVERY aircraft. Wired globally as output-mode hotkey **Ctrl+Shift+T** (id 9250, in the offline-actions set; was Alt+T but rebound — Alt+letter global chords get swallowed by the sim/Windows menu handling, Ctrl+Shift is reliable) and listed in all 6 hotkey guides. Cross-checked: −30°C/3000 ft field-0 → +570 ft; +15°C → +0 ft; −40°C/5000 ft → +1210 ft.

## Related Documentation

- [Architecture](architecture.md) — overall system design
- [Hotkey System](hotkey-system.md) — dual-mode hotkey delegation
- [Visual Guidance](visual-guidance.md) — the other continuous-tone guidance feature, shares audio primitives
- [Development](development.md) — key files and dependencies
