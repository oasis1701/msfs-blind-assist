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
- `MSFSBlindAssist/Forms/TaxiGuidanceOptionsForm.cs` — user settings dialog
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

1. **Prefer `ILSHoldShort` (IHS/IHSND) over `HoldShort` (HS/HSND).** If the route passes both on the way to the same runway, the ILS hold-short wins. ILS hold lines sit farther back from the runway (ICAO Annex 14: ~90–107.5 m) to protect the localizer critical area, so stopping at the IHS is always safe (it's also the correct hold line when low-vis / LVP is in force). Plain HS distances scale with runway code (30 m Code A → 90 m Code E/F). When only one type exists in the DB, whichever is present is used.
2. **Synthetic 60 m back-off when no HS/IHS node exists.** Some airports in some DB builds have runways with no hold-short graph nodes at all (older navdata, partial Navigraph merges, small GA fields). Rather than routing the aircraft onto the runway, we walk backwards along the route from the runway lineup target and stop at the first node that is at least 60 m from it — an ICAO-Annex-14-inspired minimum distance. The final segment is marked `IsHoldShortPoint = true` so the guidance manager announces "Hold short" normally.

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
3. **Width outlier cap (`OFF_ROUTE_PERP_WIDTH_CAP_FT = 300.0 ft`).** When the current segment's `PathWidth` looks corrupt (e.g. a stray 999 ft value from bad DB data on an apron taxilane), it is capped at 300 ft before being used to derive the off-route tolerance. Prevents a single bad row from effectively disabling the off-route check for that leg.

### Recalculation hardening (remaining sequence + diversion guard)

When a recalc does fire, two additional safeguards keep the new route honest to the original ATC clearance:

1. **Position-aware sequence trimming (`FindRemainingSequenceByPosition`).** The ATC taxiway sequence is stored in `_originalTaxiwaySequence`. On recalc the manager walks the sequence from **last to first**, asking the graph "is there a node on this taxiway within 50 m of the aircraft?" The first hit is the latest taxiway the aircraft is physically on; everything before it is dropped before passing to `FindConstrainedPath`. Driving the trim from aircraft position (rather than from the recorded `_currentSegmentIndex`) handles the case where a previous recalc reset the segment index to 0 even though the aircraft is far along the route — observed at LEPA where a recalc near the H2 hold-short produced a route that physically started back at LE, sending the aircraft on a big loop. Aircraft drifted entirely off the cleared route → no sequence-taxiway hit → caller falls back to `FindNearestNodeInDirection` + shortest path.
2. **Diversion guard.** If the router fell back to unconstrained shortest path (`ConstrainedFallbackReason` is non-empty) and the new total distance exceeds `2 × oldRemaining + 500 m`, the manager **rejects the recalc** and announces: `"Off route. Could not follow clearance. <reason>. Continuing on original route."` This prevents a bad recalc from sending the aircraft across the field just because a single intersection node has no bridging edge on the expected taxiway.

## Concurrency

`UpdatePosition` runs on the **SimConnect thread** (~30 Hz). Route mutation and teardown calls — `LoadRoute`, `StartGuidance`, `StopGuidance`, `ContinuePastHoldShort`, `GetStatusAnnouncement` — arrive from the **UI thread** (hotkeys, form buttons, landing-exit activation).

A single `_stateLock` in `TaxiGuidanceManager` serializes all of these. Without it, a UI-thread `StopGuidance` can null out `_route` while the SimConnect thread is mid-traversal of `_route.Segments[_currentSegmentIndex]`, producing `NullReferenceException` / `IndexOutOfRangeException`. The critical sections do no I/O — audio is already async through NAudio's mixer — so lock contention is negligible.

`TaxiSteeringTone` has its own `_lock`. `UpdateHeadingError`, `Pause`, `Resume`, `Start`, `Stop` all acquire it so that `SetPan` / `UpdateVolume` can't race with `Dispose` freeing the underlying NAudio buffer. `ClearWhereAmICache` is unlocked by design: readers of `_whereAmICachedGraph` / `_whereAmICachedIcao` capture the reference into a local before use, so a concurrent clear is safe.

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

## Announcements

| Trigger | Distance / Condition | Announcement |
|---|---|---|
| Route calculated | — | `Taxi to runway 22L via Alpha, Bravo, hold short 13L, Kilo. Total distance 1.2 miles.` |
| Start of taxi (apron first-leg look-ahead) | first movement | `Steering guidance active. Join taxiway Alpha in 200 feet.` (or `Taxiway Alpha. Steering guidance active.` if already on a named taxiway) |
| Taxiway change | on segment advance | `Taxiway Bravo.` |
| Approaching turn | ~300 ft (100 m) | `In 300 feet, turn left onto taxiway Bravo.` Direction is computed from the aircraft's CURRENT heading toward the next segment's bearing — see "Verbal turn direction" below — so the spoken cue always agrees with the steering tone's pan. |
| Turn imminent | speed-scaled ~65–245 ft | `Turn left, taxiway Bravo.` Same heading-based direction as the approaching-turn cue. |
| Crossing taxiway | ~150 ft (50 m) | `Crossing taxiway Kilo.` (toggle in settings) |
| Hold short countdown | 300 / 150 / 50 ft | `Hold short runway 13 Left in 300 feet.` / `Hold short runway 13 Left in 150 feet. Slow down.` (the *Slow down* suffix only fires if GS > 10 kt) / `Hold short runway 13 Left in 50 feet. Stop.` (the *Stop* suffix only fires if GS > 1 kt — no point telling a stopped pilot to stop). |
| At hold short | within radius | `Hold short runway 13 Left. Press continue when cleared.` (tone pauses) |
| Continue pressed | — | `Crossing runway 13 Left. Taxiway Kilo.` (tone resumes) |
| Approaching runway destination | ~300 ft | `Runway 22 Left ahead.` |
| Runway lineup achieved | heading <1° AND cross <10 ft | `Lined up, runway 22 Left. Hold position.` Tone pauses. The *Hold position* directive is the LUAW stop cue (FAA AIM 5-2-5 / ICAO Doc 4444 / EASA SERA: align with centerline and remain stationary awaiting further clearance). Convergence target matches what runway-teleport places you at (20 m back from the threshold, aligned with runway heading). |
| Gate countdown | 50 / 20 / 10 ft | `50 feet to gate.` / `20 feet.` / `10 feet. Stop.` (the *Stop* suffix at 10 ft only fires if GS > 1 kt). |
| Arrived at gate | within 20 ft | `Gate Alpha 25 reached.` |
| Off route | >50 m for >3 s | `Off route. Recalculating.` |
| Speed warning | >30 kt straight / >12 kt turn | `Slow down.` (8 s cooldown) |
| Runway incursion | non-route hold-short within 40 m | `Runway crossing ahead. Hold short.` (10 s cooldown) |
| On-demand status | Output > `Y` | `Taxiway Bravo. In 400 feet turn right onto Kilo. 0.8 miles to destination.` |
| Repeat last | Output > `Ctrl+Y` | Replays the most recent **actionable instruction** verbatim (turn callout, hold-short, taxiway change, lineup, arrival, distance countdown). Distinct from `Y` (status), which recomputes a snapshot from current position. Useful when the announcement was clipped by another sound. Returns `"No taxi instruction yet."` if guidance is active but nothing has fired; `"No taxi guidance active."` otherwise. Implemented via `TaxiGuidanceManager._lastInstruction`, populated only by `AnnounceInstruction()` — two peripheral sites still call plain `_announcer.Announce` without populating `_lastInstruction`: (a) the LoadRoute route summary, (b) the periodic ground-speed bucket announcer — so the Repeat-Last buffer keeps the most recent actionable callout. |
| Where am I | Output > `Alt+Y` | `Taxiway Bravo at KJFK.` / `Gate A25 at KJFK.` / `Runway 22L at KJFK.` Works with or without active guidance. |

### Verbal turn direction (heading-based, not route-static)

`ComputeTurnVerbalFromHeading(targetBearing, aircraftHeadingTrue)` derives the spoken "left / slight right / continue" from the angular difference between the aircraft's current true heading and the next segment's bearing — the same input the steering tone uses for its pan, so the tone and verbal cue always agree.

The route's pre-computed `TaxiRouteSegment.TurnDirection` (`nextSeg.bearing − currentSeg.bearing`) assumes the aircraft is exactly on-axis with the current segment. When the aircraft is off-axis — post-pushback rotation, after a wide turn, brief deviation, or sitting at the gate before moving — the actual turn it must make to align with the next segment can be the OPPOSITE direction from the route's intent. Before this fix the verbal cue ("turn left") sometimes contradicted the (correct) tone (panning right). Following the tone was always the right call; the verbal is now correct too.

All three spoken sites use the helper: advance notice (`In 300 feet, turn left onto Kilo`), "now" callout (`Turn left, taxiway Kilo`), and the on-demand status query. The `TurnDirection != "straight"` predicates stay on the static field because they only ask "is there a turn at this junction" (true regardless of aircraft heading); the SIGN of the turn comes from the helper.

### Deduplication

Crossing announcements use a 45-second per-taxiway-name dedup window (`_recentCrossingAnnouncements`). Many airports split what looks like a single intersection into two closely-spaced graph nodes; without the dedup the same name would fire twice in a row ("Crossing taxiway Link 53... Crossing taxiway Link 53").

### Route summary count (implicit destination hold-short excluded)

`BuildRouteSummary` takes an `isRunwayDestination` flag. When the destination is a runway, the final segment is **always** marked `IsHoldShortPoint = true` by `TruncateToHoldShort` — that's the runway hold line. That implicit hold-short is subtracted from the user-facing "N hold short points" count so the summary only advertises intermediate hold-shorts the pilot must act on. Without this, a plain `via A B K to runway 22L` clearance with no intermediate stops would announce "1 hold short point" confusingly.

### Start of taxi — apron look-ahead

If the aircraft is on an unnamed apron taxilane connector at route start, `StartGuidance` walks forward through the route segments to the first one with a non-empty `TaxiwayName`, accumulating the distance. When the distance to join exceeds 10 m the announcement becomes `"Steering guidance active. Join taxiway Alpha in 200 feet."` instead of immediately announcing the taxiway name. This matches the real-world sequence after pushback — the aircraft rolls down the stand taxilane to the apron edge before joining the first named taxiway.

### Convergence target: taxi guidance ends where teleport places you

For both runway and gate destinations, taxi guidance and the teleport hotkeys are designed to converge on the **same final aircraft state**:

| Destination | Teleport places you at | Taxi guidance ends with |
|---|---|---|
| Runway | 20 m back from the threshold, on the centerline, heading = runway heading, on ground (`SimConnectManager.TeleportToRunway`) | Aircraft on the runway centerline, aligned with runway heading within 1° (lineup-aligned hysteresis), `Lined up, runway X. Hold position.` announced, tone paused |
| Gate / parking | At the parking spot lat/lon, heading = gate heading, on ground (`SimConnectManager.TeleportToParkingSpot`) | Aircraft within 20 ft of the parking spot (`GATE_ARRIVAL_RADIUS_FEET`), 50/20/10 ft countdown announced, gate-lineup heading tone silent at parking heading |

The "Hold position" wording on runway-aligned matches the FAA AIM 5-2-5 / ICAO Doc 4444 / EASA SERA "line up and wait" procedure — align with the centerline and remain stationary awaiting further clearance from ATC. This is the universal stop point for LUAW *and* the spot where you'd briefly stop before applying takeoff thrust under "cleared for takeoff." Either way, that's the convergence target.

**Parking-listing parity with the gate-teleport dialog.** The taxi-assist form's parking dropdown is built directly from `IAirportDataProvider.GetParkingSpots(icao)` — the same data source the gate-teleport dialog uses — and labels each entry with `ParkingSpot.ToString()` (which expands to e.g. `"P 21 - Ramp GA Large (Jetway)"`). Earlier the listing was driven off graph nodes that happened to be tagged with a `ParkingName` during graph build, which silently dropped any parking spot whose lat/lon didn't have a nearby graph node — common in third-party scenery (Colombo, KORD payware, etc.) whose taxi-path data lags the parking layout. A pilot given "Parking 21" by ATC would see "Parking 21" in the gate-teleport dialog but NOT in the taxi-assist form. Now the same set of entries appears in both. Each parking spot's actual lat/lon is the lineup convergence target; routing endpoint is the nearest graph node within 100 m (the `MAX_PARKING_TO_GRAPH_M` floor — beyond that, the spot is dropped because there's no realistic taxi path to reach it).

### Taxiway connectivity in the route form

`TaxiAssistForm`'s "Add Taxiway" dropdown lists **every named taxiway at the airport**, with heuristically-connected taxiways at the top (sorted by aircraft distance) and the rest alphabetically below. The connectivity heuristic is `TaxiGraph.GetConnectedTaxiwayNames(taxiwayName)`, which counts **named-taxiway crossings** (transitions between distinct named taxiways) rather than raw graph hops — walking along the seed taxiway and through unnamed connectors is free; only crossing into a different named taxiway counts toward the budget (`maxCrossings = 2`).

Why crossings, not hops: at KSFO, M5 connects to M1 via 4–6 unnamed connector segments. The previous 4-hop BFS would visit those connector nodes and exhaust its budget before encountering any M1 edge, hiding M1 from the dropdown entirely. The crossings-based metric mirrors how ATC clearances read — "M5 M1 A L" is 3 crossings end-to-end regardless of how many short connectors physically lie between them.

Why also list non-connected taxiways: occasional ATC clearances skip a taxiway the heuristic doesn't surface, and rare scenery has graph quirks our metric misses. Showing the full airport list as a fallback ensures the user can always match what ATC said. The router's constrained-path logic and `FindRunwayBridge` resolve the actual route from any pair of selections — picking a non-connected taxiway just means the router does more work, never that the route silently fails. `GetReachableTaxiwayNames(taxiwayName, maxCrossings)` is public for callers wanting a different budget; `GetAllTaxiwayNames()` provides the full list.

### Navdata coverage caveat (sparse extractions)

`navdatareader` builds taxi_path data from the user's installed scenery. **MSFS 2024's vanilla scenery has measurably sparser taxi_path coverage at some airports than MSFS 2020.** Example at KPHX: fs2020.sqlite has 3939 taxi_path rows and 121 named taxiways; the same DB schema built from MSFS 2024 has only 845 rows and 112 names — taxiway "O" exists in the 2020 build but is absent from the 2024 build. A user whose ATC clearance names a taxiway that isn't in their DB cannot route through it; the form lists every named taxiway the DB knows about, but it cannot invent one. Workaround: rebuild navdata after a sim or scenery update, or use the FS2020 navdata if the airport is known to be richer there. Schemas are 100% identical between fs2020 and fs2024 builds (same atools / Navdatareader version), so swapping DBs is a drop-in operation through `DatabasePathResolver.ResolveExistingDatabasePath(simVer)`.

### Auto-inserted hold-shorts at intermediate runway crossings

FAA AIM 4-3-18 and ICAO Doc 4444 require that an aircraft hold short of *every* runway it crosses on the way to its destination, with explicit ATC clearance for each crossing — controllers issue them one at a time. `TaxiGuidanceManager.InsertRunwayCrossingHoldShorts(route, destinationName)` runs after `LoadRoute` builds the route and after `TruncateToHoldShort` (which owns the destination runway's hold-short separately):

- For every consecutive segment pair, if the *next* segment's end is on a runway centerline (perpendicular distance ≤ `HalfWidth + 5 m` via `RunwayCenterline`-pair geometry from `TaxiGraph`), tag the *current* segment as `IsHoldShortPoint = true` with `HoldShortRunway = "runway X"`.
- Skip the destination runway (already handled).
- Skip duplicate consecutive same-runway tags (a multi-segment crossing pavement only fires one hold-short, not one per segment).

Pilot then gets the standard 300/150/50 ft hold-short countdown approaching each crossed runway, the tone pauses at the line, and pressing `Y` (Continue) crosses. Same flow as ATC-instructed hold-shorts — no separate UI. Driven by the runway-centerline pairs already used by Where-Am-I, so it works on any DB that has enough `start` table rows to pair runway thresholds (vanilla MSFS, Navigraph, third-party scenery — all supported). `WhichRunwayContains(lat, lon)` is exposed as a private helper using the public `TaxiGraph.PerpendicularDistanceMetersStatic`.

### Ground-speed announcer (configurable interval)

A configurable periodic ground-speed callout in `TaxiGuidanceManager.UpdatePosition`. Off by default; user picks 5 or 10 kt in the Taxi Guidance Options form (`TaxiGuidanceGroundSpeedAnnounceInterval` setting). When enabled, the screen reader announces the current speed rounded to the **nearest** multiple of the interval — 4 / 5 / 6 kt all read as `"5 knots"`, 9 / 10 / 11 read as `"10 knots"`. Implementation: `(int)Math.Round(gs / interval, AwayFromZero)`. The previous floor-bucket implementation (`(int)(gs / interval)`) flipped between "0" and "5" every time the raw value crossed 5.000, producing announcements that bore no resemblance to the actual speed at any given moment.

**Hysteresis**: once a bucket has been announced, the new bucket must be reached with a 0.5 kt margin past its rounding boundary before re-announcing. This kills jitter at the new midpoint (e.g., 7.5 kt with interval=5) — without it, a steady throttle near a boundary alternated `"5 knots"` / `"10 knots"` from frame to frame as raw GS jittered. With the margin, transitioning from 5 → 10 (going up) requires gs ≥ 8; 10 → 5 (going down) requires gs ≤ 7. The 1-kt-wide deadband (gs ∈ (7, 8)) preserves whichever announcement was last spoken.

**Source field**: the GS feed is `taxiData.GroundVelocityKnots` (real GROUND VELOCITY from SimConnect), NOT IndicatedAirspeedKnots. At low taxi speeds (under ~30 kt) IAS reads near zero — pitot pressure differential is below the indicator's working range — and substituting IAS for GS made the announcer say `"0 knots"` at 5-kt actual GS and `"10 knots"` at 15–20 kt. The `TakeoffAssistData` struct now carries both fields; takeoff assist still reads IAS for V-speed callouts (correct — V-speeds are airspeed-relative), the GS announcer reads GS.

First sample after route load establishes baseline silently (`_lastAnnouncedGsBucket = -1` initial, updated with no announcement). Goes through plain `_announcer.Announce` (NOT `AnnounceInstruction`) so a fading "10 knots" callout doesn't displace the most recent actionable instruction in the Repeat-Last buffer.

Active during normal taxiing, lineup, and the takeoff roll — complements the takeoff-assist 80/100/V1/rotate callouts (which use absolute-speed thresholds while this fires at every multiple). Useful for monitoring SOP taxi-speed caps (10 kt turns, 30 kt straight) without pressing the GS hotkey, and for blind pilots tracking acceleration on the takeoff roll.

### Speed-aware directives

The user principle: only fire an action directive when actually needed. Telling a pilot already at 5 kt to "slow down" is noise; telling a stopped pilot to "stop" is patronising. The pattern is applied to every directive suffix:

- **Hold-short at 150 ft → "Slow down."** suffix only added if `_lastGroundSpeedKts > 10` kt (`SLOW_DOWN_GS_THRESHOLD_KTS`).
- **Hold-short at 50 ft → "Stop."** suffix only added if `_lastGroundSpeedKts > 1` kt (`STOP_GS_THRESHOLD_KTS`).
- **Gate countdown at 10 ft → "Stop."** suffix only added if `_lastGroundSpeedKts > 1` kt.
- The base distance callout always fires (e.g. `"Hold short runway 13 Left in 150 feet."`) — only the action verb is conditional.

**The lineup tone itself is intentionally NOT speed-dependent.** The user's principle: alignment cues must work regardless of how slowly you're maneuvering. The runway-lineup tone (silent 0.5° / activation 1° / max-pan 15°) and the "Lined up" announcement fire purely on heading + cross-track error, never gated on ground speed. The pulse-mode cue (`SetPulse(true)` at ≤3 kt + ≥5° error) is an *extra* "you're stopped and stuck" signal, not a substitute for the always-on tone.

## Hotkeys

Hotkeys are identical across all supported aircraft.

### Output mode (press `]`)

| Key | Action |
|---|---|
| `Y` | Announce taxi status (current taxiway, next turn, distance to destination) |
| `Ctrl+Y` | Repeat current instruction |
| `Alt+Y` | Where Am I — announces current taxiway, gate, or runway at nearest airport (works any time). On `Alt+Y` rather than `Shift+Y` because `Shift+Y` in output mode is `HOTKEY_STATUS_DISPLAY`. |

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
- `HOTKEY_TAXI_FORM` (input `Shift+Y`), `HOTKEY_TAXI_CONTINUE` (input `Y`), `HOTKEY_TAXI_STOP` (input `Ctrl+Y`)
- `HOTKEY_LANDING_EXIT` (input `Shift+X`)

### Where Am I implementation

`HotkeyAction.TaxiWhereAmI` routes to `MainForm.AnnounceWhereAmI()`, which:

1. **Air/ground gate.** Reads the cached `MainForm._lastOnGround` (kept fresh by the `SIM_ON_GROUND` event handler). If airborne, announces `"In flight."` and returns — Where Am I is ground-only by design (the LocationInfo hotkey covers airborne city/terrain queries).
2. Fetches the aircraft position asynchronously.
3. Looks up the nearest airport (5 NM radius) via `IAirportDataProvider.GetNearbyAirportICAOs`. The result is filtered at the call site to canonical 4-char ICAOs only (`.Where(c => c.Length == 4)`); `GetNearbyAirportICAOs` itself returns `COALESCE(NULLIF(icao, ''), ident)` to also serve `GateResolver`'s TCAS lookup, which needs 3-char idents — that filter must NOT be pushed into the SQL.
4. Calls `TaxiGuidanceManager.DescribeCurrentLocation(provider, icao, lat, lon)`. The manager reuses the active guidance graph when the ICAO matches, otherwise builds and caches a dedicated query graph in `_whereAmICachedGraph` (invalidated via `ClearWhereAmICache()`).

The actual classification happens in `TaxiGraph.DescribeLocation(lat, lon)`:
1. **Parking node** within 40 m → `Gate X`.
2. **Runway edge** (PathType starts with `R`) within half-width + 5 m perpendicular → `Runway X`. *(Effectively dead code in current navdatareader DBs — no `taxi_path` row has type R; the centerline scan below covers this case.)*
3. **Runway centerline scan** — for each `TaxiGraph.RunwayCenterline` (paired runway-start positions from the DB's `start` table), perpendicular distance to the segment between the two thresholds; if within `HalfWidthMeters + 5` (~28 m for default 75 ft half-width), returns `Runway X` for whichever threshold the aircraft is closer to (matches the takeoff direction). Pairs are built in `TaxiGraph.Build` by matching opposing-end runway-start rows whose headings differ by ~180° (±15° tolerance) AND whose threshold separation is 200–6000 m. **This is what makes "Runway 27L" work mid-runway**, not just within 50 m of the threshold node.
4. **Runway threshold node** (ParkingName `Runway …`) within 50 m → that name. Catches edge cases where a runway has unpaired start positions.
5. **Taxiway edge** within half-width + 3 m perpendicular → `Taxiway X`.
6. **Nearest node** (≤ 60 m) with at least one taxiway name → `Near taxiway X`.

Perpendicular distance uses equirectangular projection (sub-cm accuracy at taxi scale) and clamps to segment endpoints so the foot of the perpendicular falling outside still counts.

## Taxi Assist Form (route entry)

Opened via Input > `Shift+Y`. Tab order mirrors the way ATC says a clearance.

1. **Airport ICAO** — text input, auto-filled from nearest airport on Show().
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

**Per-row "Hold short of runway" picker** lets the user explicitly annotate an ATC-instructed runway hold-short between this taxiway and the next. Defaults to `(none)`; the dropdown lists every non-closed runway at the airport. When set, `ApplyUserRunwayHoldShorts` (called from `TaxiGuidanceManager.LoadRoute`) finds the first segment after this taxiway whose endpoint sits on the chosen runway's centerline and tags it as a hold-short. If the route doesn't cross the requested runway between this taxiway and the next, a warning is appended to the route summary announcement so the pilot knows the explicit pick was a clearance/route mismatch — the route still loads. Auto-detection (`InsertRunwayCrossingHoldShorts`) runs after this and respects user-set `HoldShortRunway` labels, so user wins on naming where both fire on the same segment.

**Tab order** is set explicitly: `txtAirport → cmbDestType → cmbDestination → cmbFirstTaxiway → chkFirstHoldShort → btnAddTaxiway → pnlTaxiways → btnCalculate → btnStop`. The `pnlTaxiways` panel needs `TabStop = true` and an explicit `TabIndex` *between* Add and Calculate; without that, its inner controls land at the very end of the form's tab order (after Stop), so Tab from Add jumped past every newly-added taxiway straight to Calculate. Each new dynamic group's Combo / Hold-short / Remove gets sequential tab indices inside the panel as it is added.

## Taxi Guidance Options Form

Opened from the File menu. User-tunable settings:

- **Steering tone waveform** — Sine (default), Square, Triangle, Sawtooth. Picks up from `HandFlyWaveType` to share the hand-fly tone palette.
- **Steering tone volume** — slider, default 0.05 (quiet — intended to sit under ATC chatter).
- **Announce taxiway crossings** — checkbox, default on. Turns off the ~150 ft "Crossing taxiway X" callouts for pilots who find them chatty.

All settings persist through `UserSettings`.

## Landing Exit Planner

Before touchdown (during cruise or descent), the pilot picks a runway-exit taxiway. When the aircraft lands, taxi guidance **auto-activates** from the touchdown point to that exit — no scrambling to open a form while decelerating on the rollout.

**Opened via:** Input > `Shift+X`. If an ILS destination runway is already set (from the existing ILS destination selection UI), both the ICAO and runway are pre-filled — the pilot only has to pick the exit. No duplicate runway selection UI.

### Flow

1. Pilot picks destination runway for ILS guidance (existing feature). Alternatively, types ICAO + selects runway directly in the Landing Exit Planner.
2. Planner calls `TaxiGraph.GetLandingExits(runway)` → a distance-sorted list of exit taxiways with classification:
   - **High-speed** — RET-geometry angle ≤ 50°, supports higher rollout speeds.
   - **Normal** — standard perpendicular exit (≤ 110°).
   - **End** — end-of-runway turnoff (near last 15% of runway length).
   - Each exit has its taxiway name, distance from threshold, distance from the 1000-ft touchdown aim point, and the graph node id to route to.
3. Pilot picks an exit, presses `Plan Exit`. The planner stores the selection plus the pre-built `TaxiGraph` (so activation doesn't need to rebuild it).
4. `LandingExitPlanner.ProcessGroundState(onGround, gs, lat, lon, headingTrue)` is fed from every `SIM_ON_GROUND` update in `MainForm.OnSimVarUpdated`. It edge-detects the airborne→on-ground transition and requires ground speed ≥ 40 kt (`LANDING_MIN_GS_KNOTS`) to count as a real touchdown — a teleport or reload at low speed won't trigger it.
5. On touchdown it calls `TaxiGuidanceManager.LoadRoute(...)` with the exit node id as the destination, `isRunwayDestination: false`, and the pre-built graph as `prebuiltGraph`. Then `StartGuidance(SettingsManager.Current)`. The route snaps from the aircraft's current (post-touchdown) position through the exit's graph node — shortest path, so it naturally follows the runway centerline until the chosen exit.
6. Announcement: "Touchdown. Guiding to taxiway K2, 5800 feet remaining."

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

- The planner does **not** depend on runway detection from SimConnect at touchdown. Many airports don't expose the landing runway ID on the ground, so we just trust the pilot's pre-selection and route to the chosen exit node by its lat/lon. Pilots can land on any runway and the exit still works as long as they actually landed on the runway they planned for.
- The 40-kt threshold rejects taxi starts and teleport reloads.
- `_activatedThisLanding` is set once per touchdown — no double-firing if the ground bit bounces at decel.
- If no route is found (`LoadRoute` returns an error), the planner announces `"Landing exit guidance failed: <reason>"` and stays inactive. Normal taxi guidance can be manually requested via the Taxi Assist form.
- `Clear()` / the `Clear Plan` button fully drops the pending selection.

## Integration Points

### MainForm

- `MainForm.OnSimConnectData` feeds position updates into `taxiGuidanceManager.UpdatePosition(...)`.
- `HotkeyAction.TaxiAssist` → opens `TaxiAssistForm`.
- `HotkeyAction.TaxiStatus`, `TaxiRepeat`, `TaxiContinue`, `TaxiStop` → delegate to manager methods.
- `HotkeyAction.LandingExitPlanner` → opens `LandingExitForm`, pre-filling ICAO and runway from `simConnectManager.GetDestinationAirport()` / `GetDestinationRunway()` when an ILS destination is set.
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

We do NOT auto-recommend a specific exit because that would require aircraft-performance data we don't have (approach speed, weight, brake config, etc.). Giving the pilot the headwind/tailwind number lets them choose appropriately for whatever airframe they're flying. Works with any weather source the user has active — MSFS live weather, ActiveSky, REX, or static — because SimConnect's `AMBIENT WIND DIRECTION` / `AMBIENT WIND VELOCITY` reflect the active weather model.

## Reliability Notes

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
- **ILS critical-area protection.** `runway_end.ils_ident` identifies ILS-equipped runways (~4,135 in the test DB). The IHS-over-HS preference already stops behind the correct line when IHS nodes exist; an explicit `ils_ident != NULL` hint could also be used to *force* IHS-preference even if the router's first match was HS — not currently needed, but worth noting if edge cases appear.

**Displaced-threshold handling (implemented).** `runway_end.offset_threshold` (feet) is now applied in `GetLandingExits`: the along-runway distance of each candidate exit is measured from the physical pavement start, then the offset is subtracted to produce `DistanceFromThresholdFeet` (from the landing threshold) and `DistanceFromTouchdownFeet` (from landing threshold + 1000 ft aim point). On runways with a non-zero offset (e.g. KJFK 13R at 2055 ft, KJFK 22R at 3438 ft, EGLL 27R at ~1004 ft), this keeps the "distance from touchdown" column honest — an exit listed at 2000 ft is 2000 ft past the landing threshold, not 2000 ft past the pavement start.

### What makes this reliable for blind users
- **Dual feedback**: continuous tone (fine) + speech (tactical). Neither alone is sufficient; together they give complete guidance.
- **Advance warnings**: 300 ft for turns, 300/150/50 ft for hold-shorts, distance countdowns for gates.
- **Safe defaults**: hold-short = silence (stop moving). Deviation = auto-reroute. Unknown state = bearing + distance fallback.
- **User always in control**: hotkeys for status, repeat, continue, stop available at any time.

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
| `ARRIVAL_RADIUS_M` | 30.0 | Runway arrival radius |
| `GATE_ARRIVAL_RADIUS_FEET` | 20.0 | Gate arrival radius |
| `RECALCULATION_COOLDOWN_SEC` | 15.0 | Minimum gap between auto-reroutes |
| `GUIDANCE_LOOK_AHEAD_M` | 50.0 | Minimum distance to the heading target — keeps short segments from wobbling the tone |
| `HEADING_ERROR_FILTER_ALPHA` | 0.25 | Low-pass filter on heading error fed to the tone |
| `CROSSING_DEDUP_WINDOW_SEC` | 45.0 | Per-taxiway-name crossing announcement dedup |
| `MAX_TAXI_SPEED_STRAIGHT_KTS` | 30.0 | Speed-warning threshold on straight segments |
| `MAX_TAXI_SPEED_TURN_KTS` | 12.0 | Speed-warning threshold in / approaching turns |
| `LINEUP_HEADING_TOLERANCE_DEG` | 5.0 | Gate-lineup hysteresis center (enter at 4°, exit at 7°) |
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
| `OFF_ROUTE_PERP_WIDTH_CAP_FT` | 300.0 | Upper cap on segment width used for off-route tolerance (rejects bad DB width rows) |
| `POST_TURN_OFFROUTE_GRACE_SEC` | 4.0 | Off-route detection suppressed for this many seconds after every segment advance |

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

## Related Documentation

- [Architecture](architecture.md) — overall system design
- [Hotkey System](hotkey-system.md) — dual-mode hotkey delegation
- [Visual Guidance](visual-guidance.md) — the other continuous-tone guidance feature, shares audio primitives
- [Development](development.md) — key files and dependencies
