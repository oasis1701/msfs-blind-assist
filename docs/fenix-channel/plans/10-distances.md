# D and Shift+D Implementation Plan (plan 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Tasks 0, 1 and 4 are the controller's own; Task 3 is the owner's, in the sim; Task 2 is the one implementer task.

**Goal:** Output D and Shift+D on the Fenix speak the distance and time to the destination and to the top of descent,
computed from the active route in the Fenix's display channel, and "not available" wherever the computation was not
shown to match the MCDU.

**Changed on 2026-11-03 (spec row 48, the team's decision, while this plan was unclaimed):** plan 03 decodes the route,
because its ND map reads every field of a leg. Its route decoder (`Services/FenixChannelRoute.cs`,
`Services/FenixChannelFields.Route.cs`) gives the leg rules `IsPath`, `IsDestination` and `BreaksPath`, and plan 03
measured the route's structure, the position and a discontinuity (`docs/fenix-channel/evidence/03-nd.md`).

**Changed on 2026-11-04 (Hadi, the claimant, before any code):** no flight. Plan 03 recorded every push of its two
flights, and each push holds the route, the position, the markers and the captain's MCDU text, so the sums and the
MCDU's figures come from the same push. The measurements the old Task 1 and Task 7 were to fly (the sums at four
points, the top of descent passed, the checks against the MCDU) were replayed over those recordings instead:
`docs/fenix-channel/evidence/10-distances.md`. The replay changed the rules (the start, the turns, the discontinuities,
the top of descent once the descent begins) and the plan went from nine tasks with two flights to four with one gate
check. The old text is in this file's history (the commit `docs(fenix-channel): claim plan 10`).

**Architecture:** Plan 03's decoder reads the captain's route (field 2: the active route's elements, the TO waypoint, the
markers, the runways) and the present position (192) out of the snapshot's message. One line model serves both keys:
the drawn line to the destination, its turns as arcs, a discontinuity crossed straight. D is the great circle to the TO
waypoint plus the line from there. Shift+D finds the top-of-descent marker on the line and subtracts its distance to the
line's end from the aircraft's, and is past once an end-of-descent marker shows. The switch commit implements plan
01's `ChannelDistancesKey`: the ND verdict and the route from one snapshot, `BaseAircraftDefinition`'s sentences, the
time from `FormatEtaFromDistance` at SimConnect's ground speed.

**Tech Stack:** C# 13 / .NET 10, xUnit; plan 01's `FenixChannelPush`; plan 03's `FenixChannelRoute`;
`NavigationCalculator` (the app's great-circle formulas); `FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` 2.2 (the ND verdict), 2.5 (the keys: computed, checked against the MCDU, "not
available" rather than a guess), 3.3 (the route fields), 4, 6; answers 9, 39-41 and 48.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelDistances.cs`, `Services/FenixChannelRouteGeometry.cs`,
  `Services/FenixChannelRouteDistances.cs`, new tests `tests/MSFSBlindAssist.Tests/FenixChannelRouteGeometryTests.cs`,
  `FenixChannelRouteDistancesTests.cs` and `FenixChannelDistances*Tests.cs`,
  `docs/fenix-channel/evidence/10-distances.md`, and the "D and Shift+D" sub-heading of the "Fenix display channel"
  section of `docs/a32nx.md`. Not this plan's: the route decoder and its tests (plan 03's).
- Plan 03's names consumed as they are: `FenixChannelRoute.Decode`, `IsPath`, `IsDestination`, `BreaksPath`,
  `ArrivalIdent`; `FenixRoute` (`Position`, `Legs`, `ActiveLeg`, `Markers`, `Runways`), `FenixRouteLeg` (`Point`,
  `Kind`, `Ident`, `MissedApproach`, `TurnRadius`, `CourseIn`, `CourseOut`, `EndReason`, `Destination`),
  `FenixRouteMarker` (`Point`, `Type`, `IsGoAround`), `FenixRouteRunway` (`Threshold`), `FenixLatLon`;
  `FenixChannelFields.RouteLegKind` (`Line`, `Arc`, `Start`, `End`, `Symbol`), `RouteEndReason`, `MapMarkerKind`
  (`Tod`, `Eod`). A member this plan needs that the decoder lacks is a question for plan 03's claimant (Hadi).
- Plan 01 seam implemented here, in the switch commit: `ChannelDistancesKey`. Plan 01 names consumed as they are:
  `ChannelKeyContext`, `SpeakNotAvailable`, `FenixChannelSnapshot`, `FenixChannelPush`, `FenixChannelSection<T>`,
  `FenixChannelMonitor.ReportFeatureProblem`, `FenixChannelDisplay.Nd`. App names: `BaseAircraftDefinition.FormatEtaFromDistance`
  (protected static, so the sentences live in the definition), `SimConnectManager.IsConnected` and
  `RequestAircraftPositionAsync` (`AircraftPosition.GroundSpeedKnots`), `NavigationCalculator.CalculateDistance` and
  `CalculateBearing`.
- Both keys take the ND verdict and the route from one snapshot (spec 2.2). The time uses SimConnect's ground speed,
  never the ND's ground speed text.
- **Tolerance**, checked by Task 2's recorded tests: 1 NM plus 0.5 % of the MCDU's figure at the same push, the F-PLN
  DEST line's DIST for D and PERF CRZ's "TO (T/D)" DIST for Shift+D. Why: the MCDU prints whole miles (0.5 NM a
  figure); the app's spherical earth (`NavigationCalculator`, 3440.065 NM) and an ellipsoid differ by up to about 0.5 %
  of a distance. A larger difference means the sum follows another path than the FMS: the tolerance must catch that,
  never absorb it. The one measured exception is the MCDU's own lag after a sharp turn is sequenced (evidence: 52 s
  after BAIRE's 153° turn); no recorded case sits in it.
- Nothing speaks until Task 3 passes: Task 2's first two commits are inert and pushed as usual; its third is the switch
  commit, tested on your own build in Task 3 and pushed to `feat/fenix-display-channel` only in Task 4 (until then it may
  sit on `fc-p10-distances` on the remote). Until then D and Shift+D stay silent on the Fenix, as today.
- The recordings and the replay scripts stay local and are never committed ([FNX-23]'s local tools). The recorded
  cases are route tables of public waypoints and positions with the MCDU's figures, written into the tests.

## Review Focus

- **The start.** D runs straight from the aircraft to the TO waypoint's own point, then along the line from the last
  kept point carrying the TO's ident; never from the first element carrying it, which can lie far behind
  (`The_sum_runs_straight_to_the_to_waypoint_then_along_the_line`, the recorded `ClimbInHdg` and `RunwayTo`).
- **Turns and breaks.** A turn piece counts as its arc; a discontinuity is crossed straight, a heading leg's drawn far
  point dropped (`A_turn_piece_counts_as_its_arc`, `A_discontinuity_is_crossed_straight`,
  `A_heading_leg_s_drawn_point_is_dropped`, the recorded `CurvedLeg` and `ClimbThreeDiscontinuities`).
- **The destination ends the sum**, never the missed approach (`The_sum_stops_at_the_destination`, the recorded
  `LandingRoll`).
- **A route the sum cannot follow** says "Distance to destination not available", never a partial sum
  (`An_unfollowable_route_is_not_available`).
- **The top of descent.** An end-of-descent marker means past, whatever type 0 markers show (the descent restart
  points); no marker, two, a go-around one only, or one off the line is not available
  (`An_end_of_descent_marker_is_past`, `Top_of_descent_markers_that_cannot_be_used_are_not_available`, the recorded
  `DescentRestartMarker`, `EarlyDescent`, `NoCruiseLevel`, `AfterGlideslope`).
- **The marker on the line**, not on the straight line from the aircraft (`A_marker_is_placed_on_the_line_in_a_turn`,
  the recorded `TurnAfterArtur`).
- **The time.** No time under 30 knots (`FormatEtaFromDistance`); with SimConnect disconnected the distance is still
  spoken, without a time, never silence (`The_time_needs_thirty_knots`, `A_disconnected_sim_still_speaks_the_distance`).
- **One snapshot.** An untrusted ND, no message or a wrong shape gives "not available" for both keys, and the field is
  logged once (`The_answer_takes_one_snapshot`).

---

### Task 0 [controller]: Claim this plan

Done 2026-11-04 (`dba68cd84`, from `58c25234e`).

- [x] **Step 1: Check the plan is free on the remote.**
- [x] **Step 2: Branch and claim** (`fc-p10-distances`, worktree `.claude/worktrees/fc-p10`).
- [x] **Step 3: Push the claim.**

### Task 1 [controller]: Settle the rules on the recordings

Who: the controller, with the local replay scripts over plan 03's recordings; no sim.

- [x] **Step 1.** Replay the plan's rules and the candidates over every push with a DEST line or a PERF CRZ T/D figure;
  keep the rule that meets the tolerance; explain every miss (evidence: "Distance to destination", "Top of descent").
- [x] **Step 2.** Pick the recorded cases and dump each push as a route table with the MCDU's figure (the evidence's
  last table; the tables themselves are handed to Task 2's implementer).
- [x] **Step 3.** Commit the evidence file and this plan's change together (`docs(fenix-channel): plan 10 settled on the
  recorded flights`) and push.

### Task 2: The code: geometry, the two distances, the sentences and the switch

**Files:** Create `Services/FenixChannelRouteGeometry.cs`, `Services/FenixChannelRouteDistances.cs`,
`Aircraft/FenixA320Definition.ChannelDistances.cs`; tests `tests/MSFSBlindAssist.Tests/FenixChannelRouteGeometryTests.cs`,
`FenixChannelRouteDistancesTests.cs`, `FenixChannelDistancesRecordedTests.cs` and `FenixChannelDistancesKeyTests.cs`
(all new).

**Interfaces:**
- Produces:
  - `internal static class FenixChannelRouteGeometry`: `double DistanceNm(FenixLatLon a, FenixLatLon b)`
    (`NavigationCalculator.CalculateDistance`); `(double AlongNm, double CrossNm) Project(FenixLatLon from, FenixLatLon to, FenixLatLon p)`
    (the aviation formulary's along-track and cross-track distances on the great circle from `from` to `to`; along
    negative behind `from`, cross negative left); `double ArcNm(double radiusNm, double courseIn, double courseOut)`
    (the radius times the smaller angle between the two courses, in radians).
  - `internal enum FenixDistanceKind { Distance, Past, NotAvailable }`;
    `internal readonly record struct FenixDistanceAnswer(FenixDistanceKind Kind, double Nm)`.
  - `internal static class FenixChannelRouteDistances` with `double? ToDestination(FenixRoute r)`,
    `FenixDistanceAnswer ToTopOfDescent(FenixRoute r)`,
    `FenixDistanceAnswer Answer(FenixChannelSnapshot s, bool topOfDescent, Action<string> reportProblem)`, and
    `internal const double OnLineNm = 0.5, PastWithinNm = 0.1, SamePointNm = 0.05`.
  - In `FenixA320Definition`, `internal static` for the tests: `string DistanceSentence(bool topOfDescent, double nm, double groundSpeedKnots)`
    (`nm` as F0 in the invariant culture, "miles to destination" or "miles to top of descent", then
    `FormatEtaFromDistance(nm, groundSpeedKnots)`, as `AnnounceDestFromSDK` and `AnnounceTODFromSDK` speak);
    `const string PastTopOfDescent = "Past top of descent"`, `DestinationName = "Distance to destination"`,
    `TopOfDescentName = "Top of descent"` (for `SpeakNotAvailable`, which adds " not available");
    `void SpeakDistance(bool topOfDescent, double nm, bool simConnected, Action<Action<double>> requestGroundSpeed, Action<string> speak)`
    (connected: ask for the ground speed and speak the sentence with it when it comes; disconnected: speak it at once
    without a time, since `RequestAircraftPositionAsync` does nothing while disconnected); and the seam.

**Rules** (measured: `evidence/10-distances.md`, "The rules"):
- **The line.** The elements of `r.Legs` that `IsPath` keeps and that are not `MissedApproach`, in order, to the first
  one `IsDestination` names or, with none, to the last of them. Walk them keeping points: a `BreaksPath` element first
  drops the kept points before it that carry its own ident and lie more than `SamePointNm` from its point (a heading
  leg's drawn far point), then is kept at its own point unless the last kept point is within `SamePointNm` of it; every
  other element is kept, its step from the last kept point being `ArcNm` for a `RouteLegKind.Arc` element with
  `TurnRadius`, `CourseIn` and `CourseOut`, otherwise `DistanceNm`. Each kept point's distance to the line's end is the
  sum of the steps after it.
- **The TO.** `r.ActiveLeg`. Not available (both keys): null or an empty ident; `MissedApproach`; no `Symbol`-kind
  element of its ident in `r.Legs`, unless its ident is `ArrivalIdent(r.Runways)`; its ident on no kept point. Added
  by the final review (2026-11-05): its ident on kept points in two runs with another ident between; a hold on any
  element of the line.
- **The aircraft's distance to the line's end:** `DistanceNm(r.Position, TO.Point)` plus the end distance of the last
  kept point carrying the TO's ident.
- **`ToDestination`:** the TO's ident is `ArrivalIdent(r.Runways)`: `DistanceNm(r.Position, r.Runways[1].Threshold)`.
  Two or more runways and no `IsDestination` element in the line: null. Fewer than two runways (changed after Task 3,
  2026-11-05): the line's last element must be an `End` within `SamePointNm` of the last `Symbol`-kind element off the
  missed approach, and the line takes that `End` under the symbol's ident (for both keys); otherwise null. Any TO rule
  above: null. Otherwise the aircraft's distance to the line's end.
- **`ToTopOfDescent`:** any marker of type `MapMarkerKind.Eod`: Past. Otherwise the `Tod` markers that are not
  `IsGoAround`: none or two or more, NotAvailable. One: a TO rule above, NotAvailable; place it on the line: of the
  steps between consecutive kept points where `Project` gives a cross of at most `OnLineNm` and an along between -0.1
  and the step's great-circle length plus 0.1, the one nearest across; its distance to the line's end is the rest of the
  step (the great-circle length less the along, at least 0, scaled by the step's length over its great-circle length)
  plus the end distance of the step's far point; on no step, NotAvailable. The answer is the aircraft's distance to the
  line's end less the marker's: at most `PastWithinNm`, Past; otherwise Distance.
- **`Answer`:** not trusted or no message: NotAvailable; `FenixChannelRoute.Decode` not ok: `reportProblem` with its
  problem, then NotAvailable; otherwise `ToDestination` (null: NotAvailable) or `ToTopOfDescent`.

- [x] **Step 1: Geometry and the two distances, with their tests.** Geometry tests, each within 0.01 NM:
  `Distances_follow_the_app_s_formula` ((0, 0) to (0, 1): 60.04; (50, 8) to (50, 9): 38.59; a point to itself: 0),
  `A_point_projects_onto_a_leg` (leg (0, 0) to (0, 2), point (0.5, 1): along 60.04, cross -30.02),
  `A_point_behind_a_leg_projects_negative` (leg (0, 0) to (0, 1), point (0, -0.5): along -30.02, cross 0),
  `An_arc_is_its_radius_times_its_turn` (radius 10, 0° to 90°: 15.71; 350° to 10°: 3.49; 90° to 90°: 0).
  Distance tests (a helper builds pushes with `FenixChannelProtoWriter` and decodes them with
  `FenixChannelRoute.Decode`, runways "KAAA"/"01" and "KBBB"/"19", the destination "KBBB19"):
  `The_sum_runs_straight_to_the_to_waypoint_then_along_the_line` (position (0, 0); line: start (0, -1) "A", "A" (0, 1),
  "B" (1, 1), "KBBB19" (1, 2); symbols "A", "B"; TO "A" at (0, 1): 180.11 within 0.01; the same with the position at
  (-0.5, 1), off the line: 150.09, 30.02 to "A" and 120.07 after it), `The_sum_stops_at_the_destination` (two missed approach elements after it:
  still 180.11), `A_turn_piece_counts_as_its_arc` (an `Arc` element of radius 10, 0° to 90°, between "A" and "B": its
  step is 15.71, not its chord), `A_discontinuity_is_crossed_straight` (an `End` element, reason 1, at "B", then a
  `Start` at (2, 1): the step from "B" to (2, 1) is its great circle), `A_heading_leg_s_drawn_point_is_dropped` ("M" at
  (9, 1) after "B", then the `End` element "M" at "B"'s point: the sum is as if "M" were not there),
  `The_runway_as_to_is_the_distance_to_its_threshold` (TO "KBBB19": the great circle to the arrival threshold),
  `An_unfollowable_route_is_not_available` (theory, each null: no legs; no TO; a TO with an empty ident; the TO on the
  missed approach; a TO with no symbol; the TO's ident on no kept point; no destination element; one runway, no
  destination end (since Task 3, one runway alone is no longer a refusal)),
  `An_end_of_descent_marker_is_past` (an `Eod` marker and a `Tod` marker ahead: Past),
  `The_top_of_descent_is_measured_along_the_line` (a `Tod` marker at (1, 1.5): Distance 150.10 within 0.01; the same
  with no destination element: the line's end is its last point, still 150.10),
  `A_marker_behind_the_aircraft_is_past` (at (0, -0.5): Past; 0.05 NM ahead: Past),
  `A_marker_is_placed_on_the_line_in_a_turn` (TO "B", the position (0.2, 0.9) beside the leg from "A" to "B", a `Tod`
  at (0.5, 1) on that leg, 3.72 NM from the straight line from the aircraft to "B": found on the line, Distance 18.39
  within 0.01), `Top_of_descent_markers_that_cannot_be_used_are_not_available` (theory: none; two;
  only a go-around one; one 2 NM off every step: NotAvailable), `The_answer_takes_one_snapshot` (a snapshot with an
  untrusted verdict and a good message: NotAvailable; trusted with no message: NotAvailable; a wrong shape at 2.4.1:
  NotAvailable, and `reportProblem` called once with "field 2.4.1 has the wrong shape").
  Recorded tests (`FenixChannelDistancesRecordedTests.cs`): the fourteen route tables Task 1 dumped, kept verbatim as
  raw string literals in the file, one line per item (`pos`, `rwy`, `to`, `leg`, `mark`, documented in the file's
  header comment), built into a push with `FenixChannelProtoWriter` (2.1, 2.4, 2.11 and 2.12 under field 2, the
  position under 192) and decoded with `FenixChannelRoute.Decode`; `Recorded_routes_match_the_mcdu` (theory over the
  cases, each as the evidence's last table says: within the tolerance of the MCDU's figure, Past or NotAvailable).
  Commit: `feat(fenix-channel): distance to the destination and to the top of descent`.
- [x] **Step 2: The sentences.** Tests (`FenixChannelDistancesKeyTests.cs`): `Sentences_follow_the_base` (412 NM at 400
  knots: "412 miles to destination: 01:01:48"; 96 at 480: "96 miles to top of descent: 00:12:00"; 412 at 25: "412 miles
  to destination"), `The_time_needs_thirty_knots` (29.9 knots: no time; 30: ": HH:MM:SS"),
  `Past_and_not_available_are_the_base_s_words` ("Past top of descent"; `DestinationName + " not available"` is
  "Distance to destination not available"; "Top of descent not available"),
  `A_disconnected_sim_still_speaks_the_distance` (one sentence without a time, the request never made),
  `A_connected_sim_speaks_when_the_ground_speed_comes` (nothing before the callback; at 400 knots, "412 miles to
  destination: 01:01:48", once), `Sentences_are_the_same_in_every_culture` (de-DE and sv-SE: 412.4 NM at 25 knots gives
  "412 miles to destination"; 1234.6 at 400 gives "1235 miles to destination: 03:05:11"). Commit:
  `feat(fenix-channel): the D and Shift+D sentences`. Inert (no seam yet): pushed with Step 1.
- [x] **Step 3: The switch commit.** `ChannelDistancesKey` always sets `handled` (nothing else answers these keys on the
  Fenix). No monitor: `SpeakNotAvailable(ctx, name)`. Otherwise `Answer(ctx.Monitor.Snapshot(FenixChannelDisplay.Nd),
  topOfDescent, p => ctx.Monitor.ReportFeatureProblem("D and Shift+D", p))`: Past,
  `AnnounceImmediate(PastTopOfDescent)`; NotAvailable, `SpeakNotAvailable(ctx, name)`; Distance, one
  `Log.Debug("Fenix", ...)` line for Task 3 (the key, the distance to a tenth, the TO's ident, the leg count; invariant
  culture), then `SpeakDistance(..., ctx.SimConnect.IsConnected, cb => ctx.SimConnect.RequestAircraftPositionAsync(p =>
  cb(p.GroundSpeedKnots)), ctx.Announcer.AnnounceImmediate)`. Test: `The_seam_reads_one_nd_snapshot` (a source test
  over `FenixA320Definition.ChannelDistances.cs`: one `Snapshot(` call, with `FenixChannelDisplay.Nd`, and no other
  display named). Commit: `feat(fenix-channel): D and Shift+D speak from the route`. Build and run it on your own
  machine for Task 3; push it to `feat/fenix-display-channel` only in Task 4.

### Task 3 [owner] [sim]: The gate check

With MSFSBA built from the plan's branch, Task 2 included, the Fenix on 3.0.0.7613, powered at a gate. The computed
value is the press's line in `debug.log`, read straight away (it rotates in about ten minutes). Record each result in
`docs/fenix-channel/evidence/10-distances.md`; a check that fails is fixed and run again before Task 4.

- [x] A flight plan loaded, no cruise level: D says "<n> miles to destination" with no time, within the tolerance of
  the F-PLN DEST line's DIST; Shift+D says "Top of descent not available". (2026-11-05: with both runways, 398.2
  against 398. With fewer than two runways D was not available where the FMS gave 389 and 394: fixed, see the
  execution record.)
- [x] The cruise level entered on INIT: Shift+D says "<n> miles to top of descent" with no time (recorded beside PERF
  CRZ's "TO (T/D)" DIST if the page prints one at the gate). (2026-11-05: "not available". The ND draws no T/D marker
  at the gate, while PERF CRZ printed 292. Follows the data; the expectation was not measured before.)
- [x] Captain ND Brightness at Off (the app's panel control): `fenix_channel.log` shows the ND "display off"; D says
  "Distance to destination not available" and Shift+D "Top of descent not available"; the brightness back.
- [x] No flight plan (a fresh power-up, or the flight plan cleared): both keys say "not available".
- [x] By ear with NVDA: the sentences read as written.

### Task 4 [controller]: Docs and landing

- [x] **Step 1.** `docs/a32nx.md`, in place of "(Plan 10 writes this section.)" under "D and Shift+D": the fields read,
  the rules, the tolerance and why, the refusals, the measured exception (the MCDU's lag after a sharp turn), and where
  the evidence is. Commit: `docs(fenix-channel): D and Shift+D from the route`.
- [x] **Step 2.** The whole-plan review (index, "Running a plan with subagents": the ownership diff, a test for every
  Review Focus line, the handoff and the execution record). Set **Status** to landed with the date and the switch
  commit, fill in "Tests at landing" and "Handoff to plan 12", and push everything, the switch commit included (index
  workflow steps 4-6).

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** guide `guide/airbus-aircraft.md`, "Fenix A320 CEO", "Aircraft-specific hotkeys and callouts": the
  sentence saying Output D and Shift+D are not offered on the Fenix changes (they work, computed from the Fenix's flight
  plan; Shift+D says "Past top of descent" once the descent has begun, and "not available" at the gate (the ND draws no
  top of descent there, measured 2026-11-05), before a cruise level is entered, and from glideslope capture on; plan 02
  changes the speeds' half of it). Plan 12's flight is the first check of when the top of descent appears after
  takeoff with the cruise level entered at the gate. Hotkey guide
  `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`: the output readouts gain "D Distance to destination" and
  "Shift+D Distance to top of descent". Changelog `feature`: "On the Fenix, D and Shift+D now tell you how far it is,
  and how long at your ground speed, to your destination and to the top of descent, worked out from the Fenix's own
  flight plan."
- **Rule** (one line, under 400 characters):
  ```
  - [FNX-?] Fenix D and Shift+D (`FenixChannelRouteDistances`) sum as the FMS does (9,806 recorded pushes): straight to the TO waypoint, then the drawn line, turns as arcs, a discontinuity crossed straight. Shift+D is past once an end-of-descent marker shows, never from a T/D-type marker alone; no TO, destination or T/D marker is "not available". Full: docs/invariants/fenix-channel.md#fnx-?
  ```
  Full text: the rules and refusals, the tolerance and its reasons, the replay's numbers and the BAIRE lag.
- **Frozen files:** none.
- **Also seen on:** the FBW A32NX and Headwind A330 D (`MainForm.Announcers.cs`) says "Destination distance not
  available", where the base, the PMDG 737 and this plan say "Distance to destination not available": a presentation
  difference across aircraft, for its own issue.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** landed 2026-11-05 (the switch commit `2855d6911`)
- **Claimed by:** Hadi (oasis1701), 2026-11-04
- **Worked by:** Hadi with Claude, with subagents (superpowers:subagent-driven-development)
- **Started:** 2026-11-04, from 58c25234e (the branch's head; plan 03's route decoder is on it)
- **Landed:** 2026-11-05, pushed to `feat/fenix-display-channel` with the switch commit and the two fixes (`888d45cd9`
  after the gate check, `6fabff991` after the final review)
- **Tests at landing:** the full suite 19,271 passed, 0 failed (ClaudeRulesHookTests filtered out); plan 10 adds 66
  tests, 16 of them recorded cases
- **Tasks:** 0 to 4 done
- **Waiting on the owner:** nothing
- **In-sim results:** Task 3 at a KCVG gate on 2026-11-05 (the evidence's "Task 3 at the gate"): no flight plan, both
  keys not available; both runways in, D "398 miles to destination" (398.2 against the DEST line's 398), Shift+D not
  available; fewer than two runways, D not available where the FMS gave 389 and 394 (fixed after the sitting; the two
  pushes are recorded cases, 389.16 and 394.07); the cruise level in, Shift+D not available (no T/D marker on the ND at
  the gate, PERF CRZ 292); the captain ND off, both not available, and back; by ear, no complaint.
- **Changed from the plan:** 2026-11-04, the plan rewritten before any code (the note at the top): the old Task 1
  (measure in flight) and Task 7 (check in flight) replaced by the replay over plan 03's recordings and a gate check;
  the old Tasks 3 to 6 merged into Task 2, and 8 and 9 into Task 4. Rules changed by the replay: the start (straight to
  the TO waypoint, then the line from its last kept point), turn pieces as arcs, a discontinuity crossed straight with
  a heading leg's far point dropped, the top of descent placed on the line, past once an end-of-descent marker shows
  (the old `ShowsPastTopOfDescent` is not needed), the TO-without-a-symbol refusal. Shift+D's tolerance now takes PERF
  CRZ's printed figure as its reference. 2026-11-05, after Task 3: D with fewer than two runways gives the distance to
  the line's end at the destination airport (the destination end), where it refused before. 2026-11-05, the final
  review: a hold on the line and a TO ident on two separate runs are not available (neither measured; over the 17,807
  recorded pushes the first is only on the landing rolls and the second never), and the line runs to the last
  destination element.
- **Rulings made while working:**
  - Can plan 10 skip its flights? Yes, from the recordings, with one gate check; four tasks (Hadi, 2026-11-04).
  - Shift+D after the descent begins: its distance while the T/D marker shows; once past it, "Past top of descent" or
    the distance, whichever the data allows (Hadi, 2026-11-04). The marker goes when the descent phase begins and the
    data then holds no point for it, so "Past top of descent".
  - D with fewer than two runways: the FMS still gives a DEST distance, so D gives it too; fixed before landing, with
    no second sitting (Hadi, 2026-11-05).
  - Shift+D at the gate is not available: the ND draws no T/D marker there; the plan's expectation is corrected and
    the first check moves to plan 12's flight (controller, 2026-11-05).
  - A hold on the line and a TO ident on two separate runs: not available, as spec 2.5 asks rather than a guess
    (controller, on the final review, 2026-11-05).
