# Taxi steering tone, lineup and turn cues — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-steering.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## STR-1

- `WAYPOINT_CAPTURE_RADIUS_M` (25m) must skip the last route segment or it preempts the gate arrival radius / parking countdown. → [taxi-guidance.md](../taxi-guidance.md)

## STR-2

- Steering tone must stay stereo-pan only — never add frequency/volume modulation to the taxi/lineup tone (pulse mode's on/off volume toggle is the one deliberate exception). → [taxi-guidance.md](../taxi-guidance.md)

## STR-3

- Taxiing tone target must be the continuous arc-length walk (`GuidanceGeometry.WalkTarget`) — a turn/no-turn branch reintroduces one-frame target jumps (hard pan-flips); clamp `t` (the walk START) at the upper bound only, never the lower (clamping low teleports the walk start ~25m at every capture). `f` (the TARGET's fraction) is a different quantity and IS floored at 0, so the target can never land behind the segment start and steer the pilot backwards — the two coexist because the floor only binds beyond a whole look-ahead behind the start. → [taxi-guidance.md](../taxi-guidance.md)

## STR-4

- A sub-metre segment (`DEGENERATE_SEG_M` 1.0m, matching the manager's `len < 1.0 → bearing 0.0`) is a POINT: `WalkTarget`/`CumulativeTurnDeg` must SKIP it before projecting, never project onto it (phantom-axis extrapolation — a restarted route's snap stub gave a target sliding metres from the aircraft) and never force `t = 1` for it (discards the behind-distance, stepping the target ~25m in one frame). → [taxi-guidance.md](../taxi-guidance.md)

## STR-5

- The segment-advance scan is ENDPOINT-distance based, so `AdvanceToNearestSegment` needs the projection pin-breaker (`GuidanceGeometry.HasPassedOntoNextSegment`, 30m cross-track) beside it: the current segment shares its end node with the next, so a wide corner outside the 25m capture TIES them forever and on a long segment (KLAS B, 345m) the aircraft is on the route yet beyond every endpoint — target frozen, tone orbiting a fixed point (KLAS 26R 2026-08-20). It must fire in BOTH un-advanceable cases (the tie AND a later segment still beyond `SEGMENT_ADVANCE_MAX_DIST_M`), must go through `AdvanceSegment()`, and must NEVER fire while the current segment is a hold-short — a silent pass is the runway-incursion direction and outranks un-pinning. → [taxi-guidance.md](../taxi-guidance.md)

## STR-6

- "Straighten." must fire per sustained-yaw episode — never gate it on per-junction `TurnAngleDegrees`; navdata splits real 90° turns into many small micro-bends. → [taxi-guidance.md](../taxi-guidance.md)

## STR-7

- Runway lineup must use explicit thresholds (`UpdateHeadingErrorWithThresholds`, 0.5°/1°/15°) — do NOT call the width-scaled tone overload here; its `MIN_SCALE` clamp leaves pilots 3° off heading with no audio cue. → [taxi-guidance.md](../taxi-guidance.md)

## STR-8

- Lineup-aligned hysteresis (enter <1°/<10ft, exit >2°/>20ft) are fixed literals in `UpdateLineup` — do not loosen back toward the old 2°/5°–15ft/30ft deadband. → [taxi-guidance.md](../taxi-guidance.md)

## STR-9

- Lineup pulse mode must key on BOTH heading error AND cross-track — cross-track can be huge while intercept-angle saturation reads heading error as ~zero; dropping the cross-track branch leaves the pilot with no cue to move forward. → [taxi-guidance.md](../taxi-guidance.md)

## STR-10

- Runway lineup steering must stay intercept-angle-based — never reintroduce a bearing-to-threshold blend; once past the threshold, bearing-to-threshold sits on the ±180° wrap and produces chaotic sign flips. → [taxi-guidance.md](../taxi-guidance.md)

## STR-11

- Every `LiningUp` state entry must reset the heading-error smoother, or the taxi-phase low-pass residual leaks into the lineup tone and can steer the pilot off the runway. → [taxi-guidance.md](../taxi-guidance.md)

## STR-12

- No feet-quantity verbal cues for lateral (cross-track) guidance — a blind pilot has no reference for "42 feet left"; the tone is the cross-track instrument (heading numbers are fine, every pilot has a heading instrument). Along-track distances are not banned: two rules require them, ROL-23's "Continue to taxiway Y, N feet." and ROL-27's `RolloutRunwayReCrossing.ComposeContinueToExit`, which names the exit and the distance. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: "spatial/cross-track guidance" became "lateral (cross-track)", since ROL-23 and ROL-27 require along-track distances in feet. Evidence: ROL-23 and ROL-27 in `docs/invariants/landing-rollout.md`, and `RolloutRunwayReCrossing.ComposeContinueToExit`.

## STR-13

- `TaxiGuidanceManager._stateLock` must be acquired by any new public method touching `_route`/`_state`/`_currentSegmentIndex`. → [taxi-guidance.md](../taxi-guidance.md)

## STR-14

- TaxiSteeringTone must reset audio-modulation state (`_pulseActive`) in both `Start()` and `Stop()` — never trust caller-side cleanup; a leaked pulse state pulses the next route's taxiing tone at 3Hz. → [taxi-guidance.md](../taxi-guidance.md)

## STR-15

- TaxiSteeringTone must refresh volume on every sounding frame, not only in pulse mode — a pulse→continuous transition can otherwise leave the tone stuck at zero volume until an unrelated state change. → [taxi-guidance.md](../taxi-guidance.md)

## STR-16

- Verbal turn direction must be computed from the aircraft's current heading (`ComputeTurnVerbalFromHeading`), never the route's static `TurnDirection` — off-axis (post-pushback, after a wide turn) the actual turn can be the opposite direction and the static cue contradicts the (correct) tone. → [taxi-guidance.md](../taxi-guidance.md)

## STR-17

- Runway-destination lineup must anchor on the `start` table (`GetRunwayStarts`), never `Runway.StartLat/StartLon` directly — the latter is the pavement edge, hundreds of metres off the lineup point at displaced-threshold runways, and routes the aircraft to a node off the runway. `RunwayLineupTarget.Resolve` falls back to the pavement start only when no start row is usable (`TaxiGraph.PickFullLengthStart` returns none), the one legitimate use. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: the one-line form restores "directly", which it had dropped, and both now name the fallback. Evidence: `RunwayLineupTarget.Resolve`.

## STR-18

- The route-start turn cue has ONE owner (`Navigation/RouteStartTurnCue`), composed through the single `TaxiGuidanceManager.ComposeInitialTurnCue` — by `LoadRoute`, and again by the landing-exit handoff's segment-cursor RE-ANCHOR — never on the first taxiing frame — it fired there as an interrupting `AnnounceImmediate` 50 ms after the SayIntentions import summary and cut it off mid-word (live KATL 2026-08-27; the fifth time two announcements at Calculate have stomped each other, and the established remedy is one utterance). It is delivered EITHER folded into `TaxiAssistForm`'s single standstill utterance OR by the per-frame one-shot — or, on a route that STARTS HELD, in the Continue sentence (`ContinuePastHoldShort`), because the one-shot runs only on a taxiing frame — never twice, via `ConsumeInitialTurnCue()`. ⚠️ The form folds it only on the path that reaches its standstill block: **Progressive Taxi** calls `StartGuidance` and returns before it, so there (as on landing-exit handoffs and `announceSummary:false` callers) the one-shot still delivers the cue — unchanged by this branch and deliberately left alone, since a progressive leg composes no other utterance for it to stomp. The RE-ANCHOR call is load-bearing, not tidiness: when the handoff re-route FAILS, guidance continues on the TOUCHDOWN route, whose cue was composed rolling straight down the runway (heading error ≈ 0, so `Compose` returned null) while the re-anchor moves the cursor to a segment that can sit BEHIND the aircraft — leaving the pilot the hard-panned tone of a turnaround with no words on the degraded path, where the pre-composition design had computed it fresh on the first frame. It names the first NAMED leg at or after the cursor, which for a re-anchored cursor is not `Segments[0]`. The angle MUST be `ComputeSteeringHeadingError`'s value read against the route and cursor just assigned, with BOTH sides TRUE north, or the spoken left/right can contradict the tone's pan and a blind pilot has nothing to break the tie — never `route.Segments[0].BearingDegrees`, a different number (35° apart on the live KATL route). It names the ROUTE's first named leg; ⚠️ that is a robustness improvement for the paths that call `LoadRoute` WITHOUT `StartGuidance` (the three `Rollout` re-routes and `LandingExitPlanner`), NOT the cause of the live bare "Make a U-turn to the left" — `StartGuidance` re-sets `_lastAnnouncedTaxiway` from an identical first-named-segment walk before the first taxiing frame, so on the form's Calculate path the old cue would have named the taxiway too. Commit fec4b05a's message and the first version of that type's doc both blamed the blanked field; they are wrong and this is the correction. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: the one-line form said the cue's owner was "never the first taxiing frame", which read as a ban on the per-frame one-shot; it now says the cue is never COMPOSED there, and the one-shot stays a delivery path, as this text says. Evidence: `ComposeInitialTurnCue`'s callers in `TaxiGuidanceManager.Routing.cs` (`LoadRoute`) and `TaxiGuidanceManager.Rollout.cs` (the re-anchor), and `ConsumeInitialTurnCue`'s three consumers (`TaxiAssistForm`'s standstill utterance, the per-frame one-shot and the start-held Continue sentence).
