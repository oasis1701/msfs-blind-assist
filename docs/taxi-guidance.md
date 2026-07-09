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
| Runway-end countdown (missed last exit) | 1500 / 500 / 100 ft **or** 500 / 150 / 30 m (per Distance units setting) | `Runway end in 500 metres.` / `Runway end in 150 metres. Slow down.` / `Runway end in 30 metres. Stop.` Unit-native spacing via `DistanceMilestones.RunwayEnd`. |
| Ground traffic alert | live distance, unit-aware | `Slow down, traffic ahead, 150 metres.` (metres default) or `Slow down, traffic ahead, 500 feet.` (feet mode). Via `GroundTrafficMonitor`'s private `FormatDistance`, keyed on the independent `GroundTrafficUseMetres` toggle (see gsx.md: never fold it into `GroundDistanceUnit`). |
| On-demand status | Output > `Y` | `Taxiway Bravo. In 400 metres turn right onto Kilo. 0.8 miles to destination.` (distances in active unit; NM used for totals over ~1 NM regardless of unit setting). |
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

If the aircraft is on an unnamed apron taxilane connector at route start, `StartGuidance` walks forward through the route segments to the first one with a non-empty `TaxiwayName`, accumulating the distance. When the distance to join exceeds 10 m the announcement becomes `"Steering guidance active. Join taxiway Alpha in 200 metres."` (or feet, per the Distance units setting) instead of immediately announcing the taxiway name. This matches the real-world sequence after pushback — the aircraft rolls down the stand taxilane to the apron edge before joining the first named taxiway. The distance is formatted via `DistanceFormatter.FromMetres`.

### Convergence target: taxi guidance ends where teleport places you

For both runway and gate destinations, taxi guidance and the teleport hotkeys are designed to converge on the **same final aircraft state**:

| Destination | Teleport places you at | Taxi guidance ends with |
|---|---|---|
| Runway | 20 m back from the threshold, on the centerline, heading = runway heading, on ground (`SimConnectManager.TeleportToRunway`) | Aircraft on the runway centerline, aligned with runway heading within 1° (lineup-aligned hysteresis), `Lined up, runway X. Hold position.` announced, tone paused |
| Gate / parking | At the parking spot lat/lon, heading = gate heading, on ground (`SimConnectManager.TeleportToParkingSpot`) | Aircraft within 20 ft of the parking spot (`GATE_ARRIVAL_RADIUS_FEET`), unit-aware parking countdown announced (50/20/10 ft or 15/10/5 m per Distance units setting), gate-lineup heading tone silent at parking heading |

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

- For each route segment whose **edge** (FromNode→ToNode) crosses a runway centerline — `TaxiGraph.EdgeCrossesRunwayStatic` (a proper segment-vs-centerline-between-thresholds intersection) via `WhichRunwayCrossedByEdge` — tag the segment immediately **before** that crossing edge as `IsHoldShortPoint = true` with `HoldShortRunway = "runway X"`.
- Skip the destination runway (already handled; the prefixed `"Runway 33L"` is normalized to bare `"33L"` before comparing).
- Skip duplicate consecutive same-runway tags (`lastTaggedRunway` — a wide runway's entry and exit edges both cross the centerline).

**Why edge intersection, not point-on-pavement.** A taxiway crosses a runway via an edge that *spans* the pavement, with both flanking nodes sitting *off* the runway. The original test ("is the next segment's endpoint within `HalfWidth + 5 m` of the centerline?") therefore silently missed every crossing whose nodes were more than ~half-width+5 m out. Motivating defect (2026-06-20): KBOS taxi to 33L via K/B/C — taxiway C crosses 04L (nearest C node 35 m from the centerline), 04R (26 m → caught), and 27 (86 m); the point test tagged only 04R, and `ApplyUserRunwayHoldShorts` falsely reported *"route does not cross 04L after taxiway C"* for the user's explicit "hold short 04L" pick. Meanwhile the runtime incursion guard (`CheckRunwayIncursion`, which keys on graph `HoldShort` node TYPE + `HoldShortName` — a separate, working path) correctly called out all three crossings, which is why the symptom was "it announced the crossings but never told me to hold short." The edge test catches a crossing regardless of node spacing.

Pilot then gets the standard 300/150/50 ft hold-short countdown approaching each crossed runway, the tone pauses at the line, and pressing `Y` (Continue) crosses. Same flow as ATC-instructed hold-shorts — no separate UI. Driven by the runway-centerline pairs already used by Where-Am-I, so it works on any DB that has enough `start` table rows to pair runway thresholds (vanilla MSFS, Navigraph, third-party scenery — all supported). `TaxiGraph.EdgeCrossesRunwayStatic` is the public geometry primitive (probe-pinned in `tools/ProgressiveTaxiProbe`).

**Runway-NAME association uses the centerline, not the threshold.** A hold-short node is named after its runway by `TaxiGraph.MatchHoldShortRunwayName(lat, lon, RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M = 150 m)` — the nearest runway *centerline* by clamped perpendicular distance (closer-end designator), which is length-invariant. The previous heuristic (nearest runway *threshold* within 500 m) mislabeled a hold-short where a taxiway crosses a *long* runway far from either threshold with the taxiway name — e.g. KBOS 15R on taxiway N announced "Hold short of N" instead of "runway 15R". The 150 m tolerance sits above a CAT III / code-F holding-position setback (~107 m) yet below major-airport parallel-runway spacing, so a node binds to its own runway; a clamped perpendicular distance also means a node beyond a runway end is not falsely matched. The threshold method remains only as a fallback for navdata with no reciprocal centerline pairs. The matched name is `"runway X at <holdPoint>"` (e.g. `runway 15R at N`), so the crossing/arrival callout reads "Stop. Hold short of runway 15R at N." This must be correct at the source because `InsertRunwayCrossingHoldShorts` (above) does not overwrite a non-empty `HoldShortRunway`. Pure-geometry coverage: `tools/ProgressiveTaxiProbe` (mid-runway match, far-node reject, beyond-end reject).

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
6. Announcement: "Touchdown. High-speed exit taxiway K2 in 1800 metres." (metres mode) / "…in 5800 feet." (feet mode). The exit class ("high-speed exit", "exit", "runway-end exit") and unit are determined at runtime from the exit geometry and the user's distance-unit setting (`DistanceFormatter.FromFeet`).
7. **Rollout phase (`TaxiGuidanceState.LandingRollout`).** Steering tone is muted during the deceleration phase. Voice callouts at 1500 ft / 500 ft / turn-now mark approach to the chosen exit. Two transitions out of this phase:

   - **Normal handoff to `Taxiing`** when EITHER the pilot has begun the turn off the runway (heading deviation ≥ `ROLLOUT_TURN_BEGAN_HDG_DEG` (15°) off runway centerline), OR BOTH (a) the aircraft is at taxi speed (`< 30 kt`) AND (b) is within 500 ft (`ROLLOUT_NEAR_EXIT_FT`) of the exit. The conjunctive gate on `nearExit` prevents the tone from resuming early on long runways where GS drops below 30 kt thousands of feet upfield of the planned exit.

   - **Overshoot retarget.** If the aircraft has rolled past the chosen exit by ≥ 100 ft (`ROLLOUT_OVERSHOOT_FT`) along the runway centerline without starting the turn, `TaxiGuidanceManager` scans the precomputed exit list for the next downfield exit and `LoadRoute`s to it in place via `RetargetLandingExit`. Approach callouts re-arm for the new exit. Announcement: *"Missed taxiway A6. Retargeting taxiway A7, N feet ahead."* If no downfield exit remains, `EnterRunwayEndCountdown` clears the route (steering tone silent, no recalc) and announces *"Missed last exit on runway X."* before handing off to the runway-end countdown described below.

   - **Runway-end countdown.** When the overshoot path finds no downfield exit (or the retarget itself fails), state stays in `LandingRollout` and `UpdateRunwayEndCountdown` drives three voice callouts as the aircraft approaches the physical end of the runway: *"Runway end in 1500 feet."* / *"Runway end in 500 feet. Slow down."* (suffix suppressed when GS ≤ 30 kt) / *"Runway end in 100 feet. Stop."* (suffix unconditional — the action cue fires regardless of current GS). Tone stays silent — the pilot is on rudder/brakes alone. Transition to `Taxiing` (with `_route = null` — no recalc target) when GS drops below `ROLLOUT_NO_EXIT_STOPPED_GS_KTS = 3 kt` or heading deviates ≥ 15° (backtaxi turn). Replaces the previous "full silence" behavior so a blind pilot rolling toward the end of an active runway gets real braking information instead of being left to query Where-Am-I repeatedly.

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

## GSX Gate Integration

When **GSX Pro** is running and has a profile for the airport, it becomes the
**authoritative** source for gates/stands — GSX's metadata (heavy/jetway/VDGS,
exact positions) is far more accurate than navdata's, so navdata's own
heavy/jetway classification is never shown when GSX can answer. GSX availability
for gate sourcing = `GsxService.CouatlStarted` (running this session) AND a
matching profile exists. When GSX is absent, everything falls back to navdata
unchanged.

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
(spot, resolved graph node) pairs per ICAO; the search box and the fitting filter
then filter **in memory** on each keystroke (matching the `GateTeleportForm`
pattern). Do not reintroduce per-keystroke directory enumeration + navdata query
+ per-spot nearest-node resolution on the UI thread.

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
route to a gate and GSX is active, `GsxGateSelector` drives GSX's hierarchical
parking menu to select that exact stand — structure-agnostic
(terminal/concourse/flat), text-matching, and **never** chooses a WARP /
Follow-Me / reposition entry (positive-safe-action-only, abort on uncertainty).

The driver (`GsxMenuAutomation` over `GsxService`) is a **backtracking DFS**
(`GsxGateSelector.TraverseAsync`): at each menu level it matches a gate leaf,
else drills the best unvisited category (strongest concourse score first) and
recurses, pressing GSX's "↑ Back" to try the next sibling when a branch misses —
so it finds stands even when GSX files them under a different apron than their
letter (e.g. OMDB groups C47–C64 outside "Apron C"). Apron submenus default to a
filtered view, so the DFS clicks **"Show all positions"** first to reveal stands
hidden by the size filter. All choices are page-relative and sent only while the
live menu is on that page. Budget: 600 menu reads / 180 s for very large
airports. **Changing gates:** when a gate is already selected, the top menu is
"Change parking or service"; the selector drills its "Change Facility" entry to
re-open the position selector, then traverses to the new stand.

Success is confirmed by GSX's `FSDT_GSX_SetGate_Name/Number/Suffix` L-vars. These
update with a lag (and briefly hold the previous gate when changing), so after
choosing the leaf + the safe servicing action ("Show me this spot and activate",
which arms the VDGS/marshaller) the selector **polls** the vars up to 6 s until
they match before announcing success. Tuning lives in one place
(`GsxMenuClassifier`); the full walk is logged to
`%APPDATA%\MSFSBlindAssist\logs\gsx-gate-select.log`.

**Matching and reentrancy hardening:**

- **Bare-number leaf fallback.** A letterless GSX menu leaf ("Parking 209") now
  matches a navdata-lettered target ("P 209" — the letter was borrowed by the
  merger for display) on bare number alone. Exact identity match is always tried
  first; the fallback is logged as `MATCH-BARENUMBER`. Fixes a guaranteed
  "not found" at EGLL-style airports.
- **`SelectGateAsync` reentrancy latch** (`Interlocked`). Two Calculate clicks
  could interleave two DFS traversals on one live GSX menu — the `IsMenuActive`
  guard reads false during every menu transition, so it cannot prevent the
  overlap — pressing arbitrary wrong entries. The second call now fails fast.
- **Classifier ordering** (`GsxMenuClassifier`): the `"(N suitable parkings)"`
  count-suffix Category check runs **before** the Back check (a group like
  "Main Apron (12 suitable parkings)" classified as Back made every stand inside
  unreachable and desynced `BackOutAsync`), and the "main"/"top" back-patterns
  are full phrases ("main menu" / "back to top") so apron and terminal names
  cannot false-positive as Back.

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
  is present (still in the box), and an 8 s `REACH_WARNING_CHATTER_GRACE_SEC`
  window holds the informational taxiway-crossing callout so the warning isn't cut
  by "Crossing taxiway G". Hold-shorts / runway crossings / the lineup bailout are
  never gated.
- **Lineup bailout (during lineup).** If the runway-lineup phase sits beyond
  `LINEUP_UNREACHABLE_CROSS_FEET` for `LINEUP_UNREACHABLE_SEC` without converging,
  a one-shot bailout speaks ("This route does not reach Runway X. Reprogram …").
  This catches recalc-built routes too (which bypass `LoadRoute`). Both latches
  reset on `LoadRoute` / `StopGuidance`.

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

**Initial big-turn cue.** At guidance start the aircraft often points well away
from the route's first segment (post-pushback the tug leaves it facing one way,
the route leaves the gate another). A tone alone starts at full pan and can't
convey "turn around, which way." On the first taxiing frame, if the heading error
exceeds `INITIAL_TURN_CUE_DEG`, a one-shot cue speaks ("Taxiway A is behind you.
Turn left to come around." / "Sharp turn left onto taxiway A."), direction sign
matching the tone. One-shot, reset on `LoadRoute` / `StopGuidance` (not on
recalc). Skipped when a reach warning is present (that warning is the priority).

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
| `OFF_ROUTE_PERP_WIDTH_CAP_FT` | 300.0 | Upper cap on segment width used for off-route tolerance (rejects bad DB width rows) |
| `POST_TURN_OFFROUTE_GRACE_SEC` | 4.0 | Off-route detection suppressed for this many seconds after every segment advance |
| `RUNWAY_REACH_MAX_CROSS_M` | 120.0 | Unreachable-runway warning: if a runway-destination route's final point is more than this perpendicular distance off the runway centerline, the route doesn't reach the runway. Above the ~90 m ICAO Annex 14 max hold-short offset, below real misroutes (PHNL 04L: ~456 m) |
| `LINEUP_UNREACHABLE_CROSS_FEET` | 400.0 | During lineup, cross-track beyond this (~122 m) for `LINEUP_UNREACHABLE_SEC` with no convergence ⇒ the route never reached the runway → one-shot spoken bailout |
| `LINEUP_UNREACHABLE_SEC` | 12.0 | Sustain time for the lineup unreachable-runway bailout |
| `TAXI_TONE_MAX_SLEW_DEG_PER_SEC` | 60.0 | Slew-rate cap on the Taxiing tone's heading error. A genuine turn changes the error gradually (≈ yaw rate, well under the cap) and passes through; a one-frame target discontinuity (segment-index skip on a sharp corner, or a route recalc) is stretched into a smooth ~1–1.5 s sweep instead of a hard L↔R pan slam. Applied after the rate-lead projection, Taxiing only |
| `INITIAL_TURN_CUE_DEG` | 100.0 | At guidance start, if the heading error to the first segment exceeds this, speak a one-shot turn-direction cue (matches the tone) so the pilot knows which way to come around — the normal post-pushback case. Skipped when a reach warning is present |
| `REACH_WARNING_CHATTER_GRACE_SEC` | 8.0 | After a reach warning, hold the informational taxiway-crossing / taxiway-change callouts this long so they don't stomp the (longer, safety-critical) warning at guidance start. Hold-shorts, runway-crossing callouts, and the lineup bailout are NOT gated |

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

| Constant | Value | Purpose |
|---|---|---|
| `ROLLOUT_TAXI_GS_KTS` | 30.0 | Below this GS the aircraft is at taxi speed for handoff purposes. Conjunctive with `nearExit` — speed alone does not trigger handoff |
| `ROLLOUT_TURN_BEGAN_HDG_DEG` | 15.0 | Heading deviation from runway centerline that signals the pilot has begun the turn off — triggers immediate handoff regardless of speed or proximity |
| `ROLLOUT_NEAR_EXIT_FT` | 500.0 | Proximity to the chosen exit at which the speed-based handoff is allowed to fire. Matches the existing "500 ft slow down" callout — by the time the pilot hears that, they're committed to the turn |
| `ROLLOUT_OVERSHOOT_FT` | 100.0 | Along-runway distance past the chosen exit at which an overshoot is declared, triggering retarget to the next downfield exit (or graceful end if none remain) |
| `ROLLOUT_NO_EXIT_STOPPED_GS_KTS` | 3.0 | Ground-speed threshold at which the runway-end countdown mode considers the aircraft effectively stopped and hands off to plain `Taxiing`. Lower than `ROLLOUT_TAXI_GS_KTS` (30) because the countdown has no more useful callouts to make once the pilot is at a crawl |

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
        │                    TaxiDataCache  (per-ICAO JSON, 30-day TTL)
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

`AugmentingAirportDataProvider.Enabled` (default `true`) is wired to `UserSettings.TaxiAugmentEnabled`, exposed as an in-dialog checkbox in the Taxi Guidance Options form (with visible "© OpenStreetMap contributors (ODbL) + X-Plane Scenery Gateway" attribution). The same dialog has a **"Refresh Taxiway Names"** button that force-fetches the nearby airport and announces how many names were added (`GetLastCoverage(icao)` → "Taxiway names refreshed for X: N added" / "No new names found").

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
- **Where-Am-I runway-detection fallback.** When neither taxi-lineup nor teleport has provided a runway reference, the MainForm `POSITION_FOR_TAKEOFF_ASSIST` handler probes `TaxiGuidanceManager.TryDetectRunwayUnderAircraft` (which wraps `TaxiGraph.TryGetRunwayAtPosition`) using the aircraft's current lat/lon and heading. Gated on `_lastOnGround` — airborne CTRL+T still falls through to the synthetic-centerline path in `TakeoffAssistManager.Toggle()`. Uses a strict half-width tolerance (no +5 m fudge, unlike `DescribeLocation`) so a high-speed exit adjacent to a runway doesn't false-positive. Falls through to synthetic centerline if the airport has no `RunwayCenterlines` (sparse navdata).
- **Auto-activate Takeoff Assist on lineup.** `TaxiGuidanceManager` fires `RequestTakeoffAssistAutoActivate` (one-shot per route, gated by `_autoActivateFired` which resets on `LoadRoute` / `StopGuidance`) when the aircraft enters the lineup-aligned hysteresis on a runway target (`_isRunwayLineup == true`). MainForm subscribes and, if `SettingsManager.Current.TakeoffAssistAutoActivateOnLineup` is true and Takeoff Assist isn't already active, fires the standard `RequestPositionForTakeoffAssist` flow after announcing *"Lined up. Activating takeoff assist."* The latch is intentionally NOT reset by lineup drift-out — if the pilot manually deactivates Takeoff Assist after auto-activation, drifts off, and re-aligns, Takeoff Assist does NOT re-engage. This prevents surprise after a deliberate manual decision.
- **Do not announce runway info** (length, surface, ILS) from taxi guidance. Out of scope.
- **WAYPOINT_CAPTURE_RADIUS_M (25 m) must skip the last segment.** Otherwise it preempts the gate arrival radius (6 m) and the 50/20/10 ft parking countdown. Runways are unaffected (30 m > 25 m), but gates break without this guard.
- Steering tone uses **stereo pan only** — no frequency or volume modulation. Hysteresis (3°/6°) + 400 ms min sustain + low-pass filter on heading error kill flapping. Thresholds are **width-aware**: scaled per call by `sqrt(pathWidthFeet / 60)` clamped to `[0.65, 1.40]`. For taxi / gate lineup, call the `UpdateHeadingError(error, pathWidthFeet)` overload with the real segment width (gate lineup uses the no-arg baseline). The takeoff *roll* (`TakeoffAssistManager`) handles the wide-runway case itself.
- **Taxiing tone target = continuous arc-length walk (`GuidanceGeometry.WalkTarget`), speed-scaled 6 s clamped 50–120 m.** Pure function of position along the route polyline — no turn/no-turn branch, so one-frame target jumps (the old hard pan-flip class) are impossible; probe-pinned by `tools/TaxiGuidanceProbe` (KATL curve + KIAH hairpin replicas). The projection clamps `t` at the UPPER bound only — clamping to 0 would teleport the walk start ~25 m at every capture.
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
- Hold-short node naming picks **connector-style** names (letter+digit like `A5`) over plain parallel names (`A`) when both are available on the same hold-short node. Preserve this ranking in `TaxiGraph` hold-short resolution. **Runway association is by nearest runway CENTERLINE, not threshold distance.** `TaxiGraph.MatchHoldShortRunwayName(lat, lon, RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M = 150 m)` names a hold-short node after the runway it sits at via clamped perpendicular distance to the full-length centerline (closer-end designator, same convention as `DescribeLocation`), so a hold-short where a taxiway crosses a LONG runway far from either threshold is still named correctly. The previous distance-to-`runwayStarts`-threshold-`<500 m` test mislabeled such crossings with the taxiway name (KBOS 15R on N → "Hold short of N" instead of "runway 15R"); the threshold method survives only as a FALLBACK when no centerline is within tolerance (sparse navdata without reciprocal pairs). Matched format is `"runway X at <holdPoint>"` (e.g. `runway 15R at N`) → "Stop. Hold short of runway 15R at N." `InsertRunwayCrossingHoldShorts`' label policy is `RouteRunwayCrossings.ComposeCrossingLabel` (2026-07, probe-tested): an empty label gets `"runway X"`; a bare non-runway DB name ("A5") is upgraded to `"runway X at A5"`; a label naming THIS pavement (designator or reciprocal — user picks, correct DB names) is preserved; a DB name for a DIFFERENT pavement is CORRECTED to the geometrically detected runway (TaxiGraph's 150 m nearest-centerline naming can mis-bind between close parallels). User "end of taxiway" labels are never touched. Correct source naming still matters — it supplies the "at <holdPoint>" locative the callout keeps — but crossings now self-heal. The route summary additionally speaks a crossing clause (`RouteRunwayCrossings.Describe`): "crossing runway 10L twice"; reciprocal-labelled crossings of ONE pavement merge and speak BOTH signed names ("10L/28R") so every designator the tactical callouts will say is pre-announced. All designator compares route through `RouteRunwayCrossings.NormalizeDesignator` (zero-padding-proof, W-suffix water runways reciprocate). Pure-geometry coverage: `tools/ProgressiveTaxiProbe`.
- **Progressive Taxi terminator UI (`TaxiAssistForm`).** In Progressive Taxi destination mode the *last* taxiway row carries a terminator block (`cmbTerminatorType`: Hold short of runway / Hold short of taxiway / After crossing runway / End of last taxiway). The block is **self-contained** — it has its OWN runway-target combo (`cmbTerminatorRunway`, Alt+U; label switches per type: "Runway to hold short of:" / "Runway to cross:") plus the taxiway-target combo (`cmbTerminatorTaxiway`, which doubles as the optional "Cross at taxiway" for the after-crossing type). Do NOT reuse the per-row "Hold short of runway" combo for the terminator target. Relatedly, the **per-row "Hold short of runway" label+combo are HIDDEN in Progressive Taxi mode** (`SetRowRunwayHoldShortVisible`, called from `OnDestTypeChanged` + `AddTaxiwayRow`; hidden combos reset to "(none)" so a stale pick can't leak into the route — `GetUserRunwayHoldShorts` / `OnAddTaxiwayClicked` unchanged) — the terminator block is the single runway-hold-short control, while mid-leg crossings still get automatic hold-shorts. The per-row "Hold short" checkbox stays visible.
- **"Where Am I" (Output > `Alt+Y`)** — `TaxiGraph.DescribeLocation(lat, lon)` returns `Taxiway X` / `Gate X` / `Runway X` for the nearest airport. It does NOT depend on guidance being active; the manager caches a query-only graph in `_whereAmICachedGraph`. **Ground-only by design** — gated on `MainForm._lastOnGround` (cached from `SIM_ON_GROUND`); announces `"In flight."` when airborne. Airborne queries belong to the separate LocationInfo hotkey (city/terrain). **Runway detection** uses `TaxiGraph.RunwayCenterlines` — paired runway-start positions from the navdatareader `start` table — not `taxi_path.type='R'` edges (the DB has none). The pair is found by matching reciprocal headings within ±15° and a threshold separation of 200–6000 m. Without this, a pilot standing mid-runway only got a "Runway X" callout within 50 m of a threshold node. Note: hotkey must NOT collide with output `Shift+Y` (`HOTKEY_STATUS_DISPLAY`) — Win32 silently rejects duplicate-chord registrations.
- **Landing Exit Planner (Input > `Shift+X`)** — pre-touchdown exit picker. `LandingExitPlanner` edge-detects airborne→on-ground with GS ≥ 40 kt and auto-activates `TaxiGuidanceManager.LoadRoute(...)` using the pre-built graph. Reuses the existing ILS destination runway/airport (via `simConnectManager.GetDestinationRunway()`/`GetDestinationAirport()`) when set — do not duplicate runway-selection UI. **MainForm's SIM_ON_GROUND handler always uses `RequestAircraftPositionAsync` to feed `ProcessGroundState`** — do NOT trust `LastKnownPosition` here. The cached position is only updated by VISUAL_GUIDANCE / TAKEOFF_ASSIST / TAXI_GUIDANCE paths, and during a hand-flown approach with visual guidance off, none of those fire — the cache stays at whatever the last active path left there (typically the departure-airport taxi-out at GS ~10 kt). Feeding that stale GS to `ProcessGroundState` fails the planner's `GS ≥ 40 kt` "real landing" gate and the activation is silently skipped. The async request adds one SimConnect roundtrip (~33 ms at 30 Hz) — negligible inside the rollout window — and guarantees fresh GS / lat / lon at the moment of the SIM_ON_GROUND change. `_activatedThisLanding` inside ActivateGuidance + a HasPendingExit recheck inside the async callback together prevent double-fire if SIM_ON_GROUND bounces (oleo flicker on hard landings). The `lastKnownPosition` mirror in cases 505/506/507 of SimConnectManager remains because other consumers (TCAS altitude diff, WeatherRadarForm altitude readout, Where-Am-I) still benefit from a fresher cache — but the landing-exit gate cannot rely on it. **`SetExit(..., bool currentlyAirborne)`** arms `_wasAirborne` from the actual air/ground state, NOT unconditionally true. Source: `simConnectManager.LastKnownOnGround` (mirrored from MainForm's SIM_ON_GROUND handler, and also refreshed by every `AIRCRAFT_POSITION` response in `ProcessAircraftPosition` since the GSX PR added `SIM ON GROUND` to that struct — the position-based write is typically fresher). Wrong-side fix: setting unconditionally to `true` while ON THE GROUND would meet the activation condition on the next ground-state event with GS≥40, false-triggering during a high-speed taxi or rejected takeoff. Honoring actual state means an on-ground plan correctly waits for the next takeoff+land cycle. Form's runway combo items each carry their own wind suffix in display text (`RunwayChoice` wrapper, refreshed via `RefreshRunwayItemsWithWind` when the async `RequestWindInfo` callback resolves — marshal back to UI thread via `BeginInvoke`); the screen reader reads "30R, 12 knot headwind" on focus during dropdown navigation, no separate post-selection announcement needed. Suffix suppressed when `|headwind| < 3 kt`. Don't auto-recommend a specific exit — that needs aircraft-perf data we don't have; let the pilot judge from the wind number.

  **Rollout-phase tone gate and overshoot retarget (`TaxiGuidanceManager.UpdateLandingRollout`).** The handoff from `LandingRollout` to `Taxiing` requires `turnBegun || (atTaxiSpeed && nearExit)`, where `nearExit = distToExitFeet < ROLLOUT_NEAR_EXIT_FT = 500`. The pure `atTaxiSpeed` (GS < 30 kt) condition was wrong — on a long runway the aircraft routinely drops below 30 kt thousands of feet upfield of the planned exit, and resuming the tone there suggested "turn now" while the pilot was still on the runway centerline. **Do not relax the `nearExit` gate back to a speed-only condition.** `turnBegun` is `hdgDeltaAbs >= ROLLOUT_TURN_BEGAN_HDG_DEG (15°) && groundSpeedKts < ROLLOUT_TURN_MAX_GS_KTS (90 kt)`. The speed cap is critical: above 90 kt a heading deviation from runway centerline is touchdown yaw, crosswind crab alignment, or sim physics at wheel contact — not a real runway exit maneuver. Category E rapid-exit taxiways top out at ~90 kt, so legitimate high-speed exit turns are still detected. **Do not remove the `ROLLOUT_TURN_MAX_GS_KTS` guard** — a crabbed approach at KJFK 22L caused the heading to drift 16° from runway heading within 2 seconds of touchdown at 112 kt, falsely triggering the handoff 5,077 ft before the planned exit. On overshoot — aircraft along-runway projection past the chosen exit by ≥ `ROLLOUT_OVERSHOOT_FT = 100` ft AND heading still within `ROLLOUT_TURN_BEGAN_HDG_DEG = 15°` of runway heading — the manager scans `_rolloutAllExits` (cached at `BeginLandingRollout` time from `_graph.GetLandingExits(runway)`, sorted by `DistanceFromThresholdFeet` ascending) for the first exit further downfield and re-`LoadRoute`s in place via `RetargetLandingExit`. If no downfield exit remains, `EnterRunwayEndCountdown` clears `_route` / `_destinationNodeId` and switches into runway-end countdown mode — the route nulling is what prevents `TryRecalculateRoute` from routing back across the runway to the now-passed exit (the original bug). `BeginLandingRollout` now takes `Runway runway` and `List<LandingExit> allExits` parameters; the planner computes the exit list once at touchdown. **`TryEarlyExitHandoff` (at ≤50 kt within 300 ft) only fires for High-speed exits (`ExitType == "High-speed"`, angle < 50°).** For Normal and End exits (angle ≥ 50°) the extension node is too far off the runway heading to give useful tone steering 300 ft before the junction — a 90° exit immediately pans the tone to maximum and the rollout's own 150 ft "turn now" callout is silently lost because state has already moved to Taxiing. Normal exits (50–110°) rely on the 150 ft verbal callout from `UpdateLandingRollout`; at the same moment the verbal fires, the rollout tone switches its desired heading from "bearing to the junction" to `ExitBearingTrue`, giving an immediate hard-pan toward the exit direction. The bearing-to-junction heading fights the turn at this range (junction is still ahead, so heading error flips to the wrong sign as the pilot turns off the runway), whereas `ExitBearingTrue` correctly decreases as the pilot aligns, conveying both direction and "how much more to turn." The heading-error smoother is reset at this transition so the pan is sharp rather than ramping from the near-zero approach residual. The tone continues until `turnBegun` fires (15° heading change), at which point the Taxiing handoff re-routes from the live position to the extension node. End exits are excluded from the ExitBearingTrue switch — a backtaxi requires a ~180° turn whose direction is ambiguous in the heading-error sign; the verbal is sufficient. **Do not restore `TryEarlyExitHandoff` for Normal/End exits** — it caused the EGNX runway 27 / taxiway M (90°) miss: tone went max-left at 300 ft before M junction with no verbal cue, pilot couldn't respond in time. **At every handoff to Taxiing (turnBegun / exitedLaterally / alignedWithExit / atTaxiSpeed&&nearExit), always re-route from the live aircraft position** using the extension-node logic (ApronNodeId if set, else FindExitExtensionNode, else NodeId). This replaces the initial touchdown route — which goes through the taxiway network and gets a false "hold short of runway X" tag from `InsertRunwayCrossingHoldShorts` because the route's destination sits on the runway — with a clean 1–2 segment route. **Do not revert to the ApronNodeId-only re-route** — Normal/End exits have ApronNodeId == NodeId and would keep the bogus initial route. **Post-high-speed-exit `ExitBearingTrue` floor (`_postHighSpeedExitMinBearing`) must release on a wrong-side route.** After `TryEarlyExitHandoff` fires for a high-speed exit, `ExitBearingTrue` is installed as a minimum-pan floor (the `_postHighSpeedExitMinBearing` block in `UpdatePosition`) so the tone stays panned toward the exit side through the shallow ramp. But `ExitBearingTrue` is the exit's first runway-edge bearing, which at some airports points to the OPPOSITE side from where the taxiway actually routes to the apron (CYVR M1 off 26R: first edge heads NW ~305°, but the M1 taxiway curves SOUTH; route bearings 256°→196°→134°→100°). The floor's `Math.Max/Min` clamp then forced the tone the WRONG way (right toward 305°) and snapped ~115° left the instant `turnComplete` released it at heading 305° — a violent L/R reversal on rollout (the reported "took us right, then abruptly left, back right" at CYVR 26R). Fix: the floor is now RELEASED (set to 0, permanently) the moment the live route steers clearly OPPOSITE it — `Math.Sign(headingError) == -Math.Sign(minError) && Math.Abs(headingError) >= FLOOR_OPPOSITE_RELEASE_DEG (10°)`. The opposite-SIGN test (NOT magnitude-vs-floor) is what distinguishes this from the shallow-RET case the floor exists for (EIDW S5, EDDB M3), where the live route runs ~parallel to the runway ON the exit side (same sign as the floor → `routeOpposesFloor` never fires, floor preserved). The 10° margin filters sensor noise so a single jittery frame can't permanently kill a legitimate floor. **Do not gate the release on magnitude-vs-the-floor or restore the unconditional `Math.Max/Min`** — both reintroduce the wrong-side hard pan. NOTE: the over-eager undershoot retarget that *exposed* this at CYVR (M6→M1 the instant GS dipped below the 50 kt high-speed threshold, while M6 was still comfortably reachable) is a separate, unfixed contributing factor.

  **Implicit-exit `ExitBearingTrue` shallow-angle override (TaxiGraph.cs).** For airports whose navdata contains no `HS`/`IHS`/`HSND`/`IHSND` markers (every vanilla MSFS 2024 implicit-exit airport — EDDB, LGAV, and most regional fields), `GetLandingExits` falls through to the implicit-exit path. The runway-edge node's only outgoing edge is often a 30-50 m connector stub that runs nearly parallel to the runway before the taxiway curves off, giving a misleading first-edge `ExitBearingTrue` (e.g. EDDB 24L → M3 stub bearing 255.7° / 6.9° off runway, vs. real M3 end-to-end direction ~280° / 31° off). The shallow-angle BFS override block widens the gate from `exitAngle < 5°` to `exitAngle < 20°` to mirror the parallel HS-style override at the next block. **Both override guards (apron forward-direction AND apronAngle > currentAngleFwd) are required** — without the `> currentAngleFwd` guard, an exit whose stub points further off-runway than its eventual apron node would have its bearing *narrowed* by the override, a regression vs. the first-edge value. `ExitAngleDegrees` is intentionally NOT updated here — matches the pre-existing HS-branch pattern; changing the angle would touch `ExitType` classification, the angle-proportional overshoot margin formula at `:1462`, and the `alignedWithExit` heading-delta requirement, each with separate regression risk. EIDW S5 / N4 are unaffected (HS-type, use the parallel branch). EGNX 27/M and other ≥20° normal exits are unaffected (above the gate). LGAV 03R D8/D9 (the airport the post-handoff pastExit guard at `:1448` was added for) is benign — a wider `ExitBearingTrue` makes `alignedWithExit` MORE restrictive, not less, so A/P jitter is even less likely to false-fire alignment than before.

  **`exitedLaterally` combined gate (TaxiGuidanceManager.cs).** The primary lateral-exit handoff trigger in `UpdateLandingRollout` is `lateral >= halfWidth + 30 ft AND (distToExitFeet <= 250 OR hdgDeltaAbs >= 8° OR pastExit)`. The bare-lateral version fired too eagerly when the pilot drifted laterally during the rollout silent-tone phase (gs > 50 kt OR dist > 300 ft from exit — the tone is silent here by design to avoid pan-from-crosswind-crab confusion). EDDB 24L → M3 reproduction: 129 ft lateral at distToExit=445 ft / hdgDelta=6.6° triggered handoff BEFORE the 150 ft "turn now" verbal cue, leaving the pilot off the runway with no directional cue. The dist gate catches "close enough that verbal cues have fired or are about to"; the hdgDelta gate (8° = half of turnBegun) catches "pilot has clearly committed to a turn"; the pastExit gate preserves the overshoot-detector path. True shallow RETs (< 8° real exit angle) still trigger via the dist gate as they close to ≤250 ft. **The passive-handoff `exitedLaterallyPH` check in the `_rolloutHandoffActive` block is intentionally NOT gated** — different semantics ("has the pilot committed?" post-check, not a handoff trigger); applying the same gate would delay clearing the overshoot monitor and could cause spurious retarget cascades. Commit b69a03d's pastExit guard handles the analogous A/P-jitter concern for the sibling `alignedWithExitPH` check.

  **Runway-end countdown after a missed-last-exit (`UpdateRunwayEndCountdown`).** When `EnterRunwayEndCountdown` fires (overshoot with no downfield exit, or a retarget LoadRoute failure), state stays in `LandingRollout` and a per-frame loop drives three voice callouts based on signed along-runway projection from `_rolloutRunway.StartLat/Lon` plus `Length`: *"Runway end in 1500 feet."* / *"Runway end in 500 feet. Slow down."* / *"Runway end in 100 feet. Stop."* — the 500 ft "Slow down" suffix is suppressed when GS ≤ 30 kt (still at taxi speed, the directive is noise); the 100 ft "Stop" suffix is **unconditional** (the pilot needs the action cue regardless of current speed). Hold-short and parking countdowns are also unconditional on their action suffixes. Tone stays silent — no steering target on rollout, pilot is on rudder/brakes. Hands off to `Taxiing` (with `_route = null`) when GS drops below `ROLLOUT_NO_EXIT_STOPPED_GS_KTS = 3 kt` or heading deviates ≥ 15° AND GS < 90 kt (backtaxi turn; same `ROLLOUT_TURN_MAX_GS_KTS` speed gate applies here too). **Keep `_rolloutRunway` cached through `EnterRunwayEndCountdown` — the countdown needs it.** Full silence on a missed-last-exit is unsafe for a blind pilot rolling toward the end of an active runway; the countdown gives them real braking information.
- **Connected-component-aware start-node selection.** `TaxiGraph.Build` runs a single BFS pass at the end to assign every `TaxiNode` a `ComponentId`. `LoadRoute` (and `TryRecalculateRoute`) look up the destination node's component and pass it as `requiredComponentId` to `FindNearestNodeInDirection` / `FindNearestNodeOnTaxiway`; candidates in a different connected component are filtered out. Same filter is applied to `TaxiRouter`'s private `FindNearestNodeOnTaxiway*` helpers via the from/target node's component. `_nextNodeId` in `TaxiGraph` starts at 1 so node ID 0 is a permanent "not set" sentinel — `TaxiGuidanceManager` uses `_destinationNodeId = 0` as the cleared-route marker, and a zero-based node ID would collide with that. Motivating defect: fs2024 navdata at GCLP models taxiway S5 as an isolated 13-node island (no graph connection to any other taxiway at either terminus). A pilot touching down on 03L near S5 had the start-node picker snap to an S5 node, and A* couldn't reach R9R (in the main 1075-node component) — the pilot heard "Could not calculate a route to the destination." and got silence during rollout. With the component filter the picker skips S5 and selects an R3 node ~187 m ahead instead. Applies to every `LoadRoute` caller, not just landing-exit, so the gate-to-runway taxi path at the same airports is protected too.
- **Taxiway exit/intersection picking uses GRAPH distance, not Euclidean.** `TaxiRouter.FindNearestNodeOnTaxiwayToTarget` and `TaxiRouter.FindBestIntersection` both score candidate nodes via `ComputeGraphDistancesFrom(destinationId)` — a Dijkstra single-source shortest-paths run from the destination. Picking by straight-line distance silently fails when the closer-by-crow-flies node is a navdata graph dead-end: any path forward from a dead-end must round-trip back through its only neighbour, which is always worse than picking the other-end node. Original bug: KDEN gate A 60 recalc with `sequence=["M4"]` — M4's northern endpoint sits 1.56 km from the gate vs 2.13 km for the southern endpoint, so the Euclidean picker chose the northern endpoint, but the unconstrained final-leg A* then had to walk 540 m back south on M4 and loop 2.4 km around the airport to reach the gate. Graph distance correctly prices the round-trip into the dead-end's cost and picks the southern endpoint instead. The destination-side Dijkstra is hoisted to `FindConstrainedPath` (computed once per route build) and threaded through both helpers via an optional `precomputedDistFromTarget` / `distFromFinalDest` parameter — saves the Dijkstra-from-endNode runs on every taxiway-transition step. Euclidean is retained as a defensive last-resort fallback in `FindNearestNodeOnTaxiwayToTarget` only when Dijkstra finds no candidate (would only happen on a malformed graph where the ComponentId filter missed a split); `FindBestIntersection` does NOT have a Euclidean fallback — returning `-1` when every candidate is graph-unreachable is intentional (the old Euclidean code returned candidates A* couldn't traverse, producing silently wrong routes), and the callers' `-1` path (FindRunwayBridge / shortest-path) is the correct recovery.
- **Last cleared taxiway is honored as the route terminus when it branches off the destination (EIDW 28R via N2, 2026-06).** The graph-distance pick above DEGENERATES for the LAST taxiway when that taxiway holds short of / branches away from a runway destination the PRIOR taxiway already reaches: every node on it can only reach the destination by going BACK through its entry junction, so `FindNearestNodeOnTaxiwayToTarget` returns the entry node itself (`targetNode == currentNode`). The step then no-ops (`continue`) and the final unconstrained "to destination" leg routes onto the runway via the prior taxiway, **silently dropping the cleared last taxiway and giving NO hold short** — live at EIDW: clearance `F2 F3 F-OUTER N N2` to 28R, but taxiway N runs to the 28R threshold (4 m) while N2 is a ~450 m connector, so N2 was dropped and the aircraft was guided down N onto the runway (router log showed only 4 of 5 taxiways, no `Strict 'N2'`). Fix in `FindConstrainedPath`: when the last-taxiway target equals `currentNode` (the entry), force traversal ALONG that taxiway and SKIP the final bypass leg (`lastTaxiwayTerminal`) so the route ENDS on the cleared taxiway. Traversal target is picked in two steps: (a) `FindNodeOnTaxiwayNearestPosition` — the node geographically nearest the destination position (EIDW N2 → its hold-short end at 28R); (b) if THAT is still the entry, `FindNodeOnTaxiwayFarthestFromNode` — the node farthest ALONG the taxiway from the entry. **(b) is the LFPG `…R, R1` → 26R case (2026-06):** the 26R destination node sits closer to the R/R1 junction than to R1's far end, so nearest-to-position ALSO degenerated to the entry, R1 was dropped, the unconstrained final leg deviated off R1, and the aircraft (correctly on R1) read as off-route → a recalc fired. **Applies to BOTH** the multi-taxiway loop's last step AND the single-taxiway step-1 path (a lone cleared taxiway — e.g. a recalc trimmed to `R1` — is itself the last, same degeneration). Triggers ONLY when the step would otherwise no-op; a last taxiway that genuinely leads to the runway picks its far end and is unchanged. Needs in-sim re-verification at LFPG (`…R, R1` → 26R) + EIDW + a normal last-taxiway clearance (regression check).
- **Post-recalc sanity gate has TWO independent indicators.** `TaxiGuidanceManager.TryRecalculateRoute` rejects a recalculated route when EITHER (a) it's dramatically longer than what's left of the current route (`newRoute.TotalDistanceMeters > oldRemaining * RECALC_LENGTH_BLOWUP_RATIO + RECALC_LENGTH_BLOWUP_PAD_M`, i.e. >2× + 500 m) OR (b) its first segment heads >`RECALC_BACKWARDS_DELTA_DEG` (120°) away from the straight-line bearing from the aircraft to the destination. Either condition firing keeps the old route and announces *"Off route. Could not follow clearance. <reason>. Continuing on original route."* The previous gate only fired when the constrained search itself fell back to shortest path (`ConstrainedFallbackReason != null`); a constrained search that "succeeded" but produced a dead-end-backtrack route (KDEN: 48 segments, ~2960 m, when remaining was ~200 m) slipped through. Indicator (a) catches length-extreme cases; indicator (b) catches the dead-end backtrack signature regardless of total length. Both are needed: AND-ing them would let "long AND going forward" recoveries through, OR-ing them is the correct semantic. Thresholds are named constants near the top of the class so they're discoverable + tunable.
- **Initial constrained loads get a sanity ADVISORY; recalc never re-applies the full original clearance.** `LoadRoute` with a user taxiway sequence also computes the unconstrained shortest path and, when the constrained route exceeds `direct × CONSTRAINED_WARN_RATIO + CONSTRAINED_WARN_PAD_M` (2× + 500 m, mirroring the recalc gate), PREPENDS *"Warning: route via X is …; direct route is …. [Taxiway X is … from your position.] Check taxiway selection."* to the spoken summary (warning FIRST — the queued summary is interrupted by the first `AnnounceImmediate` tactical callout once rolling, so a tail-position warning never gets heard; KATL "via V" 7 km tour, 2026-06-11) — the route still loads (ATC can legitimately route long), the pilot decides. Motivating defect: KIAH "via FE" — FE is a cargo-area taxiway 1.5 km away across 26R; the gate was ~600 m away; the pilot got a silent 6,094 m tour with out-and-back loops. The advisory triggers on (and quotes) the **PRE-truncation `fullRouteMeters`**, captured before `TruncateToHoldShort` trims the tail — NOT the truncated total. A clearance that doubles back (cleared taxiways leading AWAY from the destination, forcing a loop) has its backtrack TRIMMED by truncation, so the old truncated-total comparison let an obvious detour slip under the 2×+500 m trigger (EHAM 18L via A12/B/N2/cross-27/E6, 2026-06-20: N2 and E6 sit north of runway 27 while the 18L lineup is south — the navdata had no clean E6→18L path — so the route went over 27 and reversed; the truncated 852 m sat below the threshold while the full backtrack was ~1.6 km, so the pilot got a silent 180° turn at the crossing instead of the advisory). Truncation only ever SHORTENS, and for a normal route `fullRouteMeters` is within ~60 m of the summary total (the hold-short trim) — far inside the 500 m pad — so using it never adds a false positive; it only catches genuine backtracks. Relatedly, when `TryRecalculateRoute`'s position-aware trim finds the aircraft near NO sequence taxiway, it now falls back to SHORTEST PATH — re-applying the FULL original sequence routed the pilot backwards through the entire clearance (the same KIAH session: a 126-node loop back to FE that only the post-recalc sanity gate stopped).
- **Landing-exit fallback when initial `LoadRoute` fails: full exit-geometry rollout, not runway-end countdown.** When `LandingExitPlanner.ActivateGuidance` cannot route from touchdown to the chosen exit (typically because the taxi graph is disconnected and the exit's connected component has no nodes near the touchdown zone), it calls `TaxiGuidanceManager.BeginLandingRolloutNoGraph(exit, runwayHeadingTrue, runway, allExits, lat, lon, settings)` to enter `LandingRollout` state with the exit set as the geometric target. The rollout's per-frame logic — distance callouts (1500 / 900 / 500 ft), steering tone, overshoot detection, undershoot retargeting — all use exit geometry directly, NOT the route, so they work without one. At handoff time (turnBegun / exitedLaterally / alignedWithExit), `UpdateLandingRollout` calls `LoadRoute` from the live aircraft position which by then IS in the exit's component, so the re-route succeeds and normal taxi guidance follows. **`BeginLandingRolloutNoGraph` is implicitly coupled to a prior `LoadRoute` call** — it reads `_graph`, `_dataProvider`, and `_icao` for the subsequent handoff re-route, and the precondition check at the top of the method **logs a `RolloutDiag` warning if any is null but does NOT bail** (geometry-driven callouts and tone still work without them, but the eventual handoff re-route will fail). Always call from a code path where a prior `LoadRoute` (even one that failed at route construction) has populated those fields. **`BeginLandingRolloutNoGraph` defensively nulls `_route` at entry.** The handoff-failure fallback in `UpdateLandingRollout` (`!handoffRerouted && _route == null` → *"Exit reached. Route unavailable. … use the taxi planner."* + `StopGuidance`) requires `_route` to be null on the NoGraph path. In the normal flow this holds because `OnTakeoffAssistActiveChanged` calls `taxiGuidanceManager.StopGuidance()` when TakeoffAssist activates at departure (`MainForm.cs:2785`). A pilot who hand-flies the departure without TakeoffAssist would otherwise carry the stale gate-to-runway `_route` across the flight — and on a NoGraph landing whose handoff re-route also fails, `FindNearestSegmentIndexFullRoute` would silently drive the steering tone against the stale departure-airport segments. `BeginLandingRolloutNoGraph` nulls `_route` defensively at entry to guarantee the invariant regardless of the takeoff path. **Runway-end countdown is the secondary fallback** — entered only when every downfield exit has failed to route (see `RetargetLandingExit` cascade below). `_activatedThisLanding` is set to `true` so the fallback path is final for this landing; the planner doesn't retry on subsequent oleo bounces.

- **`RetargetLandingExit` cascades through downfield exits on `LoadRoute` failure.** When the initial retarget target's route cannot be built, the method walks `NextDownfieldExit` (first downfield exit beyond `ROLLOUT_OVERSHOOT_FT` with `ExitAngleDegrees ≤ 90°`) and retries `LoadRoute` against each successive candidate. The override-announcement is dropped when falling forward to a later exit (it described the originally requested target); the generic *"Missed X. Retargeting Y, Z feet ahead."* message is used instead. Only when EVERY downfield exit has failed does the method announce *"Missed X. No reachable exit remaining."* and call `EnterRunwayEndCountdown()`. Motivating defect: YSSY 16R retarget where one bad LoadRoute (degenerate target near the runway end) used to drop straight into runway-end countdown despite good earlier exits still being routable. **Cascade is state-safe** — `LoadRoute` mutates fields at the top (e.g., `_dataProvider`, `_destinationName`, `_icao`) but those are idempotent overwrites, and `_route` is set only AFTER the `route == null` check, so failed iterations leave `_route` untouched. Each iteration is independent. **Cosmetic edge case**: `prevName` is captured once at function entry from the old `_rolloutExit`. When an *undershoot* call (target earlier than `_rolloutExit`) cascades forward through `NextDownfieldExit` and happens to settle on the original `_rolloutExit` (e.g. no intermediate exit qualifies), both `prevName` and `newName` name the same exit and the announcement reads *"Missed taxiway X. Retargeting taxiway X, … feet ahead."* The steering tone and routing target are correct; only the wording is awkward. **Undershoot scan now requires a speed-proportional minimum lead**: `Math.Max(ROLLOUT_UNDERSHOOT_MIN_LEAD_FT = 200, gs · ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT = 11)`. Previously the scan picked whatever exit was nearest within `ROLLOUT_UNDERSHOOT_RANGE_FT = 1000`, which at YSSY 16R retargeted to taxiway L just 79 ft ahead at 52 kt — impossible to make — and then cascaded to a false "no exit remaining". **Implicit coupling**: at `gs ≥ 91 kt`, the min-lead floor (`91 · 11 = 1001 ft`) exceeds the scan range (1000 ft), so undershoot retargeting effectively no-ops. This is intentional (90 kt is the high-speed-exit ceiling, beyond which any retarget is unsafe anyway) but it's coupled across two unrelated-looking constants — the inline comment near `ROLLOUT_UNDERSHOOT_LEAD_PER_KT_FT` documents the interaction; keep both constants in sync if either changes.
- **900 ft RETIL-analog callout (high-speed exits only).** `UpdateLandingRollout` fires *"`<exit name>`, 900 feet."* between 500 and 900 ft from a high-speed exit, gated by `_rolloutApproach900Announced` and `ExitType == "High-speed"`. Analogous to the first Runway Exit Taxiway Indicator Light (RETIL) flash at ~984 ft — sighted pilots see the lights here, blind pilots get the equivalent verbal cue before the 500 ft "prepare to turn" window. **The 1500 ft callout's lower bound was tightened from 500 → 900 ft** so the two callouts don't overlap. Normal/End exits still get only the 1500 ft and 500 ft callouts; the 900 ft cue is reserved for high-speed exits because that's where RETIL semantics apply and Normal exits' 500 ft "prepare to turn, taxiway X" is already sufficient. **`_rolloutApproach900Announced` must be reset in all four sites that reset the other rollout-announce latches**: `BeginLandingRollout`, `BeginLandingRolloutNoGraph`, `EnterRunwayEndCountdown`, `StopGuidance`. RetargetLandingExit's success-on-cascade path also resets it so the new target's 900 ft callout can re-fire.
- **`GroundTrafficMonitor.SuppressCheck` — pluggable alert silencer.** Public `Func<bool>? SuppressCheck` predicate on `Services/GroundTrafficMonitor.cs`; when non-null and returning `true`, the 3-second poll tick early-returns without alerting. `MainForm.InitializeManagers` wires `groundTrafficMonitor.SuppressCheck = () => takeoffAssistManager.IsActive || taxiGuidanceManager.State == TaxiGuidanceState.Inactive || taxiGuidanceManager.State == TaxiGuidanceState.LandingRollout;` — traffic callouts are silenced during the takeoff roll (where the pilot's hands are full on rudder + throttle and a traffic alert can't be acted on), whenever Taxi Guidance has no route engaged (no actionable context for the alert; the pilot is parked at the gate, mid-config, or post-stop), AND during the landing rollout (hands on brakes + rudder, and the exit/runway-end callouts must not be talked over). Predicate is read via `SuppressCheck?.Invoke()` which is safe under property-read-then-invoke even under interleaving (C# captures the property into a local first). When adding other suppression contexts (e.g. flare phase, hand-fly), prefer chaining additional predicates over adding more boolean state to the monitor. **Hotkey summary (`Alt+G` / `GetNearestTrafficSummary`) is intentionally NOT gated** — it's a manual lookup outside the `OnTick` polling loop and remains available at all times so the pilot can query nearby traffic on demand even when not under guidance. **Forward-arc filter (`FORWARD_ARC_DEG = 120°`) gates Caution and Warning, not Awareness.** "Slow down" and "Stop, traffic very close" fire only for traffic within ±120° of the nose; behind-arc threats are not actionable by braking. Awareness pings ("Traffic, behind, X feet") fire in all directions so the pilot retains passive awareness — the hotkey summary covers behind-arc details on demand.
- **`TaxiAssistForm` aircraft-position freshness.** OnCalculateClicked refreshes `_aircraftLat/Lon/Heading` from `_simConnectManager.LastKnownPosition` immediately before route construction. Without this, the route starts from wherever the aircraft was when the FORM was opened — typically a pre-pushback gate position — and the post-pushback aircraft is already off-route from frame one, triggering the 3-second off-route detector and an immediate recalc. The form's `_simConnectManager` is optional (defaults null for callers that don't have one) but MainForm always passes it. `LastKnownPosition` is updated by every position-bearing SimConnect path (visual guidance, hand-fly, etc.) so it's nearly always within a frame of truth, even when the taxi-specific position monitor isn't active yet.
- **Heading-independent start-node selection when a taxiway sequence is given.** `LoadRoute` first tries `_graph.FindNearestNodeOnTaxiway(lat, lon, taxiwaySequence[0])` and falls back to the heading-aware `FindNearestNodeInDirection` only if no node on the requested first taxiway exists nearby. Why: post-pushback the aircraft can be pointing 180° away from where the first taxiway is (e.g., ATC told them to face NE for pushback, but the cleared taxi route runs SW). The heading-aware fallback would pick an apron node "ahead" of the aircraft instead of the requested taxiway, the route's approach segments would diverge from the aircraft's heading, and the off-route detector would fire on the first frame of taxi. Snapping directly to the user's requested first taxiway makes the constrained route honor the clearance regardless of pushback orientation; the pilot will turn after pushback, and the lineup tone guides them onto the first segment naturally.
- **ILS spatial+heading fallback for orphaned fs2024 rows.** The fs2024 vanilla navdata extraction has 213 ILS rows where `loc_airport_ident`, `loc_runway_name`, AND `loc_runway_end_id` are all NULL/empty — the ILS row itself is correct (right ident, frequency, location, heading) but the join columns weren't populated by navdatareader. KPHX, KORD, and several other major airports are affected (KPHX has 5 such orphans including 07R). fs2020 has zero orphans. `LittleNavMapProvider.GetILSForRunway` uses the direct `loc_airport_ident = ICAO AND loc_runway_name = name` query as the fast path; on miss it falls through to `GetILSForRunwayFallback` which: (a) looks up the runway end's threshold lat/lon and heading, (b) searches unlinked ILS rows within a 0.1° (~11 km) bounding box of the airport whose `loc_heading` is within ±5° of the runway heading (with ±180° wrap handling), (c) picks the closest by squared-distance to the threshold. Localizer antennas sit on the runway centerline beyond the far end so closest-by-distance matching is unambiguous. Wired into both ILS code paths: `GetILSForRunway` (used by ILS-guidance lookup) and `CreateRunwayFromReader` (which sets `Runway.ILSFreq/ILSHeading` from `runway_end.ils_ident`, also empty for KPHX 07R in fs2024). For fs2020 users this is a no-op; for fs2024 users it re-links the orphans at query time without requiring a navdata rebuild. `ReadILSFromReader` is shared between fast and fallback paths so the projection stays consistent. **⚠️ The EFB Airport-Lookup runway-info box (`ElectronicFlightBagForm.GetRunwayDetailedInfo`) had its OWN inline ILS query that bypassed both protections** (fixed): (1) it was `SELECT * FROM ils WHERE ident=@IlsIdent LIMIT 1` — NOT airport-scoped, so for the 498 idents shared across airports it showed a DIFFERENT airport's ILS freq/heading/GS (e.g. ENSD 26 → DAUA 04); it now accepts ONLY rows scoped to this airport (exact airport+runway preferred, then same-airport) and otherwise falls through to the spatial+heading recovery (`GetILSForRunway`) — a bare ident-only row is NEVER trusted, because with 213 orphans + 498 shared idents an unscoped tie can surface a foreign airport's row (live fs2024 cases: OMAM 31R, UIIR 32, DNMN 05, WSAT 36). (2) the ILS block was gated only on `runway_end.ils_ident`, so orphan runways (KPHX 07R) showed "No ILS available"; the else-branch now falls back to `LittleNavMapProvider.GetILSForRunway` (spatial recovery). **Rule: never query `ils` by `ident` alone — always scope by airport (+runway); anything unscoped goes through `GetILSForRunway` so it is spatially validated.**
- **DB operational-flag filtering — broad scenery compatibility.** `Database/Models/Runway.cs` has `IsClosed`, `IsLanding`, `IsTakeoff` flags (defaults: open / can-land / can-takeoff — PERMISSIVE). Read from `runway_end.has_closed_markings` / `is_landing` / `is_takeoff` via `LittleNavMapProvider.SafeReadBool(reader, columnName, defaultValue)` — handles missing column / NULL / int-as-bool gracefully. **TaxiAssistForm filters its destination dropdown to `!IsClosed`; LandingExitForm filters to `!IsClosed && IsLanding`.** Note the asymmetry: TaxiAssistForm intentionally does NOT filter by `IsTakeoff` — some third-party sceneries incorrectly mark runways with `is_takeoff=false` (a Navigraph/scenery data quality issue, not a real-world status), which caused those runways to vanish from the taxi-destination dropdown despite being perfectly usable for departure. Trusting `IsLanding` for the landing-exit picker is safer because the cost of routing to a landing-prohibited runway is much higher (live arrival inbound). Sparse DBs (most navdatareader builds) populate every row permissively → no behavior change. Rich DBs (third-party scenery, some Navigraph merges) → automatic filtering by `IsClosed` only on the taxi side. When adding new DB-backed fields that may not exist on every build, ALWAYS use `SafeReadBool` (or a similar safe-read helper) with a permissive default — do NOT use `Convert.ToInt32(reader["col"])` directly.
- **Per-row "Hold short of runway" picker** in `TaxiAssistForm` (mnemonic `Alt+O` — cycles across the first row + every dynamic row). `TaxiGuidanceManager.LoadRoute` accepts `Dictionary<int, string>? userRunwayHoldShorts` mapping taxiway-sequence index → runway designator; `ApplyUserRunwayHoldShorts` resolves the picked runway to a `TaxiGraph.RunwayCenterline` (matching against EITHER reciprocal designator — "10R" and "28L" name the same pavement), then scans forward from the START of the matching run of segments tagged with that taxiway for the first segment whose **edge** crosses that runway (`TaxiGraph.EdgeCrossesRunwayStatic`, edge-vs-centerline intersection — NOT node-within-half-width, see the auto-detector bullet below for why), and tags the segment immediately before as a hold-short. Runs BEFORE auto-detection (`InsertRunwayCrossingHoldShorts`) so the user-set `HoldShortRunway` label wins where both fire on the same segment. If the route doesn't cross the requested runway anywhere from the named taxiway's first segment onward, the method returns a warning string that's appended to the route summary announcement — the route still loads. **Scan must start at the FIRST run of segments tagged with the named taxiway, not the LAST.** At airports where the same-named taxiway continues across a runway crossing (e.g. KSFO D crosses 10R/28L mid-way and keeps the name "D" on both sides), the LAST D segment is already past the runway and the forward scan finds nothing — the user's correct "hold short 10R" pick gets silently rejected as "route does not cross 10R" even though auto-detect tags the within-D crossing correctly. **Geometry test must be reciprocal-aware.** Comparing a closer-threshold designator against the user's typed designator misses crossings closer to the opposite end of the same runway. Resolving to the `RunwayCenterline` once and testing the edge-crossing directly against its endpoints sidesteps both name-pair issues. **Duplicate-taxiway sequences** ("via N, hold short 15R, N, hold short 22R, N") use `priorOccurrences` (count of the same name earlier in `taxiwaySequence`) to pick the correct run — each sequence entry binds to a distinct maximal contiguous block of route segments with that `TaxiwayName`. The auto-detector remains the primary mechanism (covers most ATC clearances since FAA mandates hold-short of every crossed runway); the explicit picker is for confirmation and rare clearance/route mismatches.
- **Constrained-router runway bridge.** ATC clearances commonly contain consecutive taxiways separated by a runway crossing — e.g., *"K14 hold short 30L M17"* at OMDB. K14 ends at the 30L hold-short on its side; M17 starts at the 30L hold-short on the opposite side. They share NO graph node, so the previous `FindBestIntersection` failure path bailed to whole-route shortest path, ditching the user's clearance entirely. `TaxiRouter.FindRunwayBridge(currentTaxiway, nextTaxiway)` finds the closest pair of nodes between them within `MAX_BRIDGE_METERS = 200`; on success the constrained search routes along the current taxiway to its exit, free-A*-bridges across the runway, and resumes the constrained sequence at the entry on the next taxiway. The 200 m cap prevents silent half-airport jumps when an ATC clearance is genuinely wrong (in those cases, fall back to shortest path with a clear log line). Applied at both the step-1 (first→second taxiway) and the inner-loop (i→i+1) intersection lookups.
- **TaxiSteeringTone pulse-state reset.** `_pulseActive` is reset to `false` in both `Start()` and `Stop()`. Without this, a previous lineup session that ended with `SetPulse(true)` would leak its pulse state into the next route — first Taxiing-phase `UpdateHeadingError` call uses the width-scaled overload, which doesn't touch `_pulseActive`, so the inherited true would pulse the taxiing tone at 3 Hz. Always reset audio-modulation state on start/stop boundaries — don't trust caller-side cleanup.
- **TaxiSteeringTone volume refresh every sounding frame.** `SetTone` always calls `_toneGenerator.UpdateVolume(EffectiveVolume())` while sounding, regardless of `_pulseActive`. The previous "only refresh in pulse mode" optimization left the tone stuck at zero volume during a pulse→continuous transition: when the user is stopped-misaligned (pulse fires) and then starts moving, `SetPulse(false)` is called, but if the last pulse cycle had set the volume to 0 (silent half), the next frame in continuous mode skipped UpdateVolume and the tone stayed silent until something else triggered a state change (oversteer / going silent / Pause). Always refreshing the volume on every sounding frame is cheap (one float assign per ~30 Hz tick) and removes the entire class of bug.
- **Verbal turn direction is computed from aircraft heading, NOT route's static `TurnDirection`.** `ComputeTurnVerbalFromHeading(targetBearing, aircraftHeadingTrue)` derives the spoken "left / slight right / continue" from the angular difference between the aircraft's current true heading and the next segment's bearing — same input the steering tone uses for its pan, so the two always agree. The route's pre-computed `TaxiRouteSegment.TurnDirection` is `nextSeg.bearing - currentSeg.bearing` and assumes the aircraft is exactly on-axis with the current segment. When the aircraft is off-axis (post-pushback rotation, after a wide turn, brief deviation, or starting at the gate before moving) the actual turn it must make to align with the next segment can be the OPPOSITE direction from the route's intent — and the static verbal cue contradicted the (correct) tone. All three spoken sites — advance notice, "now" callout, status query (`GetStatusAnnouncement`) — go through the helper. The `TurnDirection != "straight"` predicates stay on the static field (those just check whether there's *any* turn at the junction; that doesn't depend on aircraft heading).
- **Parking listing parity with the gate-teleport dialog.** `TaxiAssistForm` (parking destination) builds its dropdown from `IAirportDataProvider.GetParkingSpots(icao)` — the same data source `GateTeleportForm` uses — labelled with `ParkingSpot.ToString()` (e.g. `"P 21 - Ramp GA Large (Jetway)"`). Routing endpoint is the nearest graph node within `MAX_PARKING_TO_GRAPH_M = 100 m`; the parking spot's actual lat/lon is the lineup convergence target, matching `SimConnectManager.TeleportToParkingSpot`. Don't drive the listing off graph parking-tagged nodes — that silently drops parking spots whose lat/lon lacks a nearby graph node (common in third-party scenery whose taxi paths lag the parking layout). **Empty-name gate-type spots render `Gate {n}`, NOT `Spot {n}`** (`ParkingSpot.Describe` → `IsGateType()` covers types 9/10/11/13/14; non-gate empty-name spots still read `Spot {n}`). **Graph-unreachable stands are KEPT, not silently dropped:** a spot with no graph node within `MAX_PARKING_TO_GRAPH_M` is resolved to `nodeId = -1`, listed with a `(no taxi route)` suffix, and `OnCalculateClicked` announces *"No taxi route to X. This stand can't be reached by the taxi network."* and bails instead of routing across non-pavement — so the pilot SEES the stand exists but is never sent onto the grass (anti-grass discoverability). Both added 2026-06-23 (commit `ffb4916f`); the rest of that gate-data-correctness branch is preserved on `gate-data-correctness-salvage`.
- **Destination runway hold-short — IHS preference is same-approach-gated.** `TaxiGuidanceManager.TruncateToHoldShort` truncates a runway-destination route at the hold-short line and tags it. It scans the route backward for the latest `ILSHoldShort` (IHS/IHSND) and latest `HoldShort` (HS/HSND). **It prefers the IHS over the HS ONLY when the two holds are within `SAME_APPROACH_IHS_MAX_M = 150 m` of each other** (the ILS hold genuinely just behind the CAT I hold on the same connector); otherwise it takes the latest (closest-to-runway) hold of either type. **Do NOT restore the old unconditional IHS preference** — it placed the hold a whole taxiway early whenever the cleared route merely *crossed* an ILS-critical-area hold on a transit taxiway before turning onto the final connector (OMDB 30R via N12, fs2024: route runs down taxiway N — which carries IHS nodes ~620 m from N12's hold — then turns onto N12; the N IHS was wrongly picked over N12's real 30R hold). `InsertRunwayCrossingHoldShorts` (below) handles *crossed* runways and is independent (geometry-based), so it won't double-tag.
- **Auto-inserted runway-crossing hold-shorts use EDGE intersection, not point-on-pavement.** `TaxiGuidanceManager.InsertRunwayCrossingHoldShorts(route, destinationName)` runs after `LoadRoute` builds the route, AFTER `TruncateToHoldShort`. For each route segment whose **edge** (FromNode→ToNode) crosses a runway centerline — `TaxiGraph.EdgeCrossesRunwayStatic` (a proper segment-vs-centerline-between-thresholds intersection) via `WhichRunwayCrossedByEdge` — it tags the segment immediately **before** that crossing edge `IsHoldShortPoint = true` with `HoldShortRunway = "runway X"`. Skip the destination runway (already handled by `TruncateToHoldShort`; the prefixed `"Runway 33L"` destinationName is normalized to the bare `"33L"` before comparing) and skip consecutive same-runway tags (`lastTaggedRunway` — a wide runway's entry AND exit edges both cross the centerline). **Do NOT revert to the old "next segment's endpoint lies within half-width of the centerline" test** — a taxiway crosses a runway via an edge that SPANS the pavement, with both flanking nodes sitting OFF the runway, so the point-on-pavement test silently missed every crossing whose nodes were more than ~half-width+5 m out. Motivating defect: KBOS taxi to 33L via K/B/C — taxiway C crosses 04L (nearest C node 35 m from centerline), 04R (26 m → was caught), and 27 (86 m); the old test tagged only 04R and skipped 04L + 27, and `ApplyUserRunwayHoldShorts` (below) falsely reported *"route does not cross 04L after taxiway C"* for the user's explicit pick — even though the runtime incursion guard (`CheckRunwayIncursion`, which keys on graph `HoldShort` node TYPE + `HoldShortName`, a separate working path) correctly called out all three crossings. Pure-geometry coverage in `tools/ProgressiveTaxiProbe`. This implements FAA AIM 4-3-18 / ICAO Doc 4444: explicit hold-short and ATC continue at every runway crossing on the route. Don't disable this — VATSIM controllers expect it, and silently rolling across an active runway is a runway-incursion risk.
- **Crossings-based taxiway connectivity + full-airport fallback.** `TaxiGraph.GetConnectedTaxiwayNames(name)` BFS counts **named-taxiway crossings** (default `maxCrossings = 2`), not raw graph hops — walking along the seed taxiway and through unnamed connectors is free; only crossing into a different named taxiway consumes the budget. The previous hop-based 4-edge limit silently hid M1 from the M5 dropdown at KSFO (4–6 unnamed connectors physically lie between them) and similar patterns elsewhere. `TaxiAssistForm`'s "Add Taxiway" combo additionally lists every airport taxiway (via `GetAllTaxiwayNames()`) below the connected ones — the heuristic prioritizes the dropdown for the common case while the full list ensures the user can match any ATC-named taxiway even when the heuristic doesn't surface it. The constrained-path router and `FindRunwayBridge` resolve the actual route from any pair of selections. `GetReachableTaxiwayNames(name, maxCrossings)` is public for callers wanting a different budget.
- **Duplicate taxiways in the entered sequence are intentional.** ATC clearances commonly re-use a taxiway across a runway crossing (e.g., *"via C, hold short 04L, C"* at KBOS). `TaxiAssistForm.OnAddTaxiwayClicked` does not filter the dropdown by already-used taxiways — only the immediately-previous one is hidden, to catch accidental no-op double-picks. The router handles consecutive duplicates as a benign no-op step: `FindBestIntersection` resolves to the current node and the `currentNode == targetNode && !bridgedAcrossRunway` short-circuit at `TaxiRouter.cs` skips the redundant A* pass. The per-row user hold-short is sequence-index-keyed, so a hold-short on the first occurrence still tags the correct segment via `ApplyUserRunwayHoldShorts`. Do NOT add an "already used" filter back to the dropdown — it would break the KBOS-style clearance pattern and any other airport with the same topology. The immediately-previous taxiway is hidden from the dropdown ONLY when the previous slot has no hold-short configured — neither the row's "Hold short" checkbox nor a runway selected in its "Hold short of runway" combo. With a hold-short set, the same-taxiway duplicate is a legitimate "taxi to hold line, resume on far side of runway crossing" clearance (e.g., KBOS *"K, B, N, hold short 15R, N, hold short 22R, N"*) and must remain available. Do NOT restore the unconditional previous-taxiway exclusion — it blocks the very clearance pattern this code was written to handle.
- **Runway destination lineup uses the `start` table, not `runway_end`.** `TaxiAssistForm.PopulateDestinations` looks up each runway's lineup point via `IAirportDataProvider.GetRunwayStarts(icao)` and matches by `RunwayID`. `Runway.StartLat/StartLon` (sourced from `runway_end.lonx/laty`) is the **physical pavement edge** — it's hundreds of meters off the lineup point for runways with a displaced threshold (e.g., KLAS 26R has a 1407 ft displacement, putting the actual lineup point ~429 m west of the stored pavement-end coordinate). Anchoring the route destination AND the `_destinationThresholdMap` entry on the start-table position keeps taxi-lineup centerline math and `TakeoffAssistManager`'s centerline math (which reads `TaxiGraph.RunwayCenterlines`, also built from the start table) referencing the same physical coordinates. Fall back to `Runway.StartLat/StartLon` only when the start table has no entry for the runway name. **Do NOT revert to using `Runway.StartLat/StartLon` directly** — it silently routes the aircraft to a graph node hundreds of meters off the runway at displaced-threshold airports (the symptom: route ends on an adjacent taxiway and TakeoffAssist reports cross-track in the tens of thousands of feet).
- **Ground-speed announcer is mode-independent (every on-ground phase), not taxi-only.** Lives in `Services/GroundSpeedAnnouncer.cs`, owned by `MainForm`, fed by the always-on `GROUND_VELOCITY` continuous base variable (registered in `BaseAircraftDefinition.GetBaseVariables`). `MainForm.HandleSpecialAnnouncements` routes `GROUND_VELOCITY` events to `groundSpeedAnnouncer.ProcessGroundSpeed(value, _lastOnGround)` and returns true (suppressing the generic "value changed" announcement). It used to live inside `TaxiGuidanceManager.UpdatePosition` — which only ran while taxi guidance was active, so callouts stopped the instant takeoff assist took over or after touchdown before taxi guidance re-engaged. **Do not move it back into a per-mode manager.** **On-ground only**: `ProcessGroundSpeed` early-returns when `onGround` is false — GS callouts cover taxi, the takeoff roll, and the landing rollout, but are silent airborne. While airborne the bucket baseline is left FROZEN (not reset) so the first sample after touchdown announces the rollout speed immediately instead of spending a sample re-baselining. Controlled by `UserSettings.TaxiGuidanceGroundSpeedAnnounceInterval` (0=off, 5, 10) — the setting keeps that name to avoid a migration even though the behaviour is no longer taxi-scoped; it's surfaced in the Taxi Guidance Options dialog. Round-to-nearest-multiple via `Math.Round(gs / interval, AwayFromZero)` — 4/5/6 kt all read as "5 knots". Hysteresis: 0.5 kt margin past the rounding boundary before re-announcing — kills jitter at the midpoint. First on-ground sample establishes baseline silently. Goes through plain `Announce` (NOT `AnnounceImmediate`) so a fading "10 knots" callout doesn't displace the most recent actionable instruction in any feature's Repeat-Last buffer.
- **Tactical and safety-critical taxi announcements use `AnnounceImmediate`, not `Announce`.** `AnnounceInstruction` (the helper used for turns, hold-shorts, taxiway changes, lineup, arrival, and distance countdowns) calls `_announcer.AnnounceImmediate` internally — every tactical callout interrupts queued speech because the pilot needs the cue *now*, not after a fading "10 knots" GS callout finishes. The same applies to standalone safety callouts (speed warnings, runway-crossing alerts, off-route warnings, lineup-achieved, parking arrival). Two sites still use plain `_announcer.Announce`: (a) the `LoadRoute` route summary at start of guidance (informational, not time-critical), (b) the periodic GS announcer (must not displace the Repeat-Last buffer). When adding new taxi callouts, default to `AnnounceInstruction` / `AnnounceImmediate`; justify any plain `Announce` call explicitly.
- **Unreachable-runway safety net + tone slew limiter + initial big-turn cue (branch `fix/taxi-route-unreachable-runway-lineup`).** When a runway-destination route ends on a taxiway that only *parallels* the runway (no connector to the runway itself), the route used to silently hold short hundreds of metres off and the lineup tone panned forever (PHNL 04L: ~456 m off). Now: (1) `LoadRoute` measures the route's final perpendicular distance to the runway centerline (`AbsLateralFromRunwayMeters`); beyond `RUNWAY_REACH_MAX_CROSS_M` (120 m, above the ~90 m ICAO max hold-short offset) it builds a warning — full detail in the box (`LastRouteSummary`), a **short** spoken form in `LastRouteReachWarning`. (2) The warning is spoken by `TaxiAssistForm` via `AnnounceImmediate` **after** `StartGuidance` (announcing it inside `LoadRoute` gets stomped by StartGuidance's first-taxiway callout); the spoken summary is skipped when a warning is present, and `REACH_WARNING_CHATTER_GRACE_SEC` (8 s) holds the informational taxiway-crossing callout so it doesn't cut the warning — hold-shorts / runway crossings / lineup bailout are never gated. (3) The runway-lineup phase fires a one-shot bailout when cross-track stays > `LINEUP_UNREACHABLE_CROSS_FEET` (400 ft) for `LINEUP_UNREACHABLE_SEC` (12 s) — catches recalc-built routes too. The **taxi tone is slew-rate-limited** (`SlewLimitToneError`, `TAXI_TONE_MAX_SLEW_DEG_PER_SEC` 60°/s, applied after the rate-lead projection, Taxiing only): a multi-segment index skip on a sharp corner (large aircraft cutting the turn) or an in-place recalc no longer slams the pan L↔R — it sweeps ~1–1.5 s; genuine turns pass untouched; baseline resets only on `StopGuidance` (so recalcs are smoothed, fresh starts snap). An **initial big-turn cue** speaks once at guidance start when the heading error to the first segment exceeds `INITIAL_TURN_CUE_DEG` (100°) — the post-pushback turnaround case — direction matching the tone; skipped when a reach warning is present.
- **Pavement lead-in onto the first cleared taxiway (large gap only).** When a user taxiway sequence is given and the nearest node ON the first cleared taxiway is more than `TaxiLeadIn.TriggerMeters` (75 m) from the aircraft, `LoadRoute` starts the constrained route from the aircraft's nearest *in-component* graph node (`FindNearestNode(..., requiredComponentId: destComponentId)`) instead of pre-snapping onto the taxiway. This lets `TaxiRouter.FindConstrainedPath` build its pavement-following lead-in (the Step-1 `AStarSearch`) onto the taxiway — apron taxilanes — rather than beelining across the apron/grass (CYYZ GB/GC → A: a 297 m beeline + 180° pivot, "in the grass crossing AJ", 2026-06-17). The lead-in is **accepted only** when the router honoured the clearance (`ConstrainedFallbackReason == null`) AND its distance is within `gap × 2.5 + 300 m` (`TaxiLeadIn.IsAcceptable`, a dead-end guard); otherwise the route is rebuilt from the on-taxiway node (today's behaviour) and the summary is prepended with *"Could not compute a path onto taxiway X along the apron; route starts on X."* On success the summary names the lead-in: *"Route to Runway 23 via A, H. First taxi via 4 and AJ to reach A. …"* (`TaxiLeadIn.Clause`). The ≤ 75 m common case (gate on/near its taxiway) is byte-for-byte unchanged, and `TryRecalculateRoute`/unconstrained routes are untouched. Verified in-sim at CYYZ (GB/GC → runway 23 via A, H, 2026-06-17): the route starts at the apron node and the lead-in tracks the AJ taxiway centreline within ~3 m onto A, replacing the earlier ~64 m straight beeline across the grass. Do NOT remove the pre-snap for the common case — it is the LEPA anchoring fix; the lead-in only replaces it when the first taxiway is far.
- **Taxi-data augmentation (online taxiway NAMES — branch `feat/taxi-data-augmentation`, separate from everything else).** `AugmentingAirportDataProvider` (`Services/TaxiAugment/`) decorates `IAirportDataProvider` BEHIND the interface, so `TaxiGraph.Build` and every consumer (route planning, Progressive Taxi, landing-exit, Where-Am-I, docking) get enriched data transparently — no caller changes. It fetches real-world taxiway names per-airport from **OpenStreetMap (Overpass)** + **X-Plane apt.dat (Gateway)**, geometrically overlays them onto the user's navdata, caches IN-MEMORY per-ICAO (`TaxiDataCache` = `ConcurrentDictionary` + TTL, NO disk → fresh every session), and never blocks `GetTaxiPaths` (returns navdata immediately, fetches in the background, raises `AirportDataUpdated`). **navdata is AUTHORITATIVE**: an existing navdata name is never overwritten (preserves add-on AIP names like OMDB KK/KG); online names only fill UNNAMED segments; online-only geometry is IGNORED (we only attach names to the scenery pavement the pilot actually taxis — never steer on an offset online line). **Works regardless of the user's database / add-ons** (purely additive; sparse navdata gets more, rich navdata stays). **Aliases**: when navdata and online name the SAME pavement differently (scenery "HAWKER" vs ATC "B"), the other name is stored as an alias, surfaced as a SEPARATE labeled dropdown entry ("B (HAWKER)" sits at the "B" position alongside "HAWKER" — not merged), and resolved to the canonical name at route time (`TaxiGraph.ResolveTaxiwayName`, single choke point in `LoadRoute`); names are normalized (`TaxiDataMerger.NormalizeTaxiwayName`: "K 2"="TWY K2"="K2"); a collision guard never remaps a name that is itself a real taxiway. **Freshness**: the active flight's departure + destination are force-fresh (force:true) via the MainForm triggers (nearest-on-ground / `GetDestinationAirport` / geofence ≤50 NM) AND `FlightPlanManager.LoadDeparture/LoadArrival`; geofenced nearby airports use the cache. **Parking/gates (REWORKED 2026-06-23, gate-data-correctness):** gate identity is AUTHORITATIVE from GSX-or-navdata and is NEVER overwritten by online data. The PUBLIC `AugmentParking(icao, spots)` (called for navdata via `GetParkingSpots` AND on the **GSX** list, which bypasses it) attaches online stand names ONLY as searchable `(online)`-tagged **aliases** via the pure, idempotent `GateAliasResolver.ResolveAliases` — an online stand aliases a gate only when their **numbers match AND any letters agree** (so navdata gate 15 never adopts a neighbour's "Gate 11B"; N/S de-ice pads never cross), with a 150 m sanity backstop; only info the identity lacks (concourse letter "A51", MARS suffix "53A") is added — a pure restatement adds nothing. Online **NEVER sets a Name/position and NEVER adds a selectable gate (anti-grass — online data can't move where you taxi)**; `spot.Aliases` is recomputed from scratch each call → idempotent. `StandId` (`Services/StandId.cs`) is the shared label→(letter,number,suffix) parser used by the resolver + `GateSearchFilter`. Empty-name gate-type spots render `Gate {n}` (not `Spot {n}`); a stand with no taxi node within `MAX_PARKING_TO_GRAPH_M` is kept but marked `(no taxi route)` and refused by the Calculate guard (was: silently dropped). **X-Plane apt.dat is the key gate source** (CYYZ "Gate 131", KATL "A12"/"B7"). The OLD nearest-distance gate-NAME fill (which corrupted identity — CYUL gate 15 → 'Gate 11B' from an offset apt.dat ramp) was REMOVED. **Telemetry** (replaces a mass census): each fetch logs a coverage line to `taxi-augment.log`. **Settings**: `UserSettings.TaxiAugmentEnabled` (default on) → `decorator.Enabled`; in-dialog checkbox + ODbL/X-Plane attribution + a "Refresh Taxiway Names" button that announces the names-added count (`GetLastCoverage` → "…: N added"). Internet assumed (MSFS 2024). Pure-logic is probe-tested (`tools/TaxiAugmentProbe`); the core taxi probes (`TaxiGuidanceProbe`/`ProgressiveTaxiProbe`) stay green. **Deep-reviewed (5-pass): no Critical issues; safety invariants (navdata authoritative, online-only geometry ignored, alias never remaps a real name) confirmed.** **Real-time (no manual refresh needed):** `TaxiAssistForm.LoadAirportGraph` AWAITS `PrefetchAsync(icao)` before building the graph (cache hit = instant, so dep/dest are immediate; only a never-fetched airport waits, with a status line), so the taxiway list + gate aliases include augmented names on FIRST open — a graph built on a cache-miss returned navdata-only and never rebuilt for the same airport. `MainForm`'s `AirportDataUpdated` handler calls `TaxiGuidanceManager.OnAirportDataUpdated(icao)`, which drops the cached Where-Am-I graph (`_whereAmICachedGraph`) when it was built pre-augmentation, so Alt+Y picks up fresh names on the next query (a manual Refresh propagates to Where-Am-I too). **Alias dropdown collision skip (OMDB 2026-06):** `TaxiGraph.GetAllTaxiwayNames` NOW implements its long-documented skip — an alias display label (`"Z (K)"`) whose normalized alias form is itself a real taxiway name is NOT surfaced. At rich, junction-dense airports a navdata segment's midpoint geometrically matches a DIFFERENT-named crossing online segment, producing ~hundreds of spurious cross-name aliases (OMDB: navdata `K` "aliased" to J/V1/W/Y/Z…). The `ResolveTaxiwayName` collision guard already routed the bare real name to the real taxiway; the missing dropdown skip meant those mislabeled duplicates still cluttered the list and, if selected, mis-routed. Genuine aliases (a scenery/ATC name NOT present as a navdata taxiway, e.g. "B (HAWKER)") still surface. **Coverage reality (telemetry):** at name-rich navdata airports augmentation adds FEW new NAMES (OMDB: +7 — K12/K21/P2A/P4A/V5/Z8/Z9; LFPG: apt.dat fills 851 unnamed connectors, +osm=0). OSM contributes more where navdata is sparse; apt.dat wins disagreements. Licensing detail for this augmentation feature is covered under "Licensing & data attribution" above.
- **No-op recalc suppression (taxi guidance, 2026-06).** `TaxiGuidanceManager.TryRecalculateRoute`'s accept block compares the recalculated route's distinct taxiway sequence to the CURRENT remaining sequence; if identical it returns early — current route, the "Route changed. Now via …" callout, the safety-critical countdown-latch resets, and the steering-tone re-slew are all skipped. A sharp turn ONTO a cleared taxiway (cutting the corner) laterally offsets the aircraft from the route's next segment long enough to trip the off-route detector, which then re-plans the IDENTICAL tail — reported live at LFPG as a spurious "Route changed … super sharp right" while turning onto N (route was unchanged: N B BD1 D1). The recalc cooldown is stamped by the caller before the accept block, so the no-op path can't re-fire each frame; a genuine reroute has a different sequence and proceeds as before.

## Related Documentation

- [Architecture](architecture.md) — overall system design
- [Hotkey System](hotkey-system.md) — dual-mode hotkey delegation
- [Visual Guidance](visual-guidance.md) — the other continuous-tone guidance feature, shares audio primitives
- [Development](development.md) — key files and dependencies
