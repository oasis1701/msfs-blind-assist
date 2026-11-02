# D and Shift+D Implementation Plan (plan 10)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output D and Shift+D on the Fenix speak the distance and time to the destination and to the top of descent,
computed along the active route in the Fenix's display channel, and "not available" wherever the computation was not
shown to match the MCDU.

**Architecture:** A decoder reads the captain's route data (field 2: the active route's legs, the active leg and the map
markers) and the aircraft's position (field 192) out of the snapshot's message. Pure geometry sums great-circle
distances from the position through the active leg to the destination, and projects the top-of-descent marker onto the
route ahead. Which legs draw the path, which is the destination, what breaks the path and what shows a passed top of
descent are measured in the sim first (Task 1); a route Task 1 did not show matching the MCDU is "not available". The
switch commit last implements plan 01's `ChannelDistancesKey`: the ND verdict and the route from one snapshot,
`BaseAircraftDefinition`'s sentences, the time from `FormatEtaFromDistance` at SimConnect's ground speed.

**Tech Stack:** C# 13 / .NET 10, xUnit; plan 01's `FenixChannelPush`; `NavigationCalculator` (the app's great-circle
formulas); `FenixChannelProtoWriter` (tests).

**Spec:** `docs/fenix-channel/spec.md` 2.2 (the ND verdict), 2.5 (the keys: computed, checked against the MCDU, "not
available" rather than a guess), 3.3 (the route fields), 4, 6 (leg fields 3 and 9-11, the top-of-descent marker type,
the distance against the MCDU); answer 9.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelDistances.cs`, `Services/FenixChannelRoute*.cs`,
  `Services/FenixChannelFields.Route.cs`, new tests `tests/MSFSBlindAssist.Tests/FenixChannelRoute*Tests.cs` and
  `FenixChannelDistances*Tests.cs`, `docs/fenix-channel/evidence/10-distances.md` (new), and the "D and Shift+D"
  sub-heading of the "Fenix display channel" section of `docs/a32nx.md`.
- Plan 01 seam implemented here, in the switch commit (Task 6): `ChannelDistancesKey`. Plan 01 names consumed as they
  are: `ChannelKeyContext`, `SpeakNotAvailable`, `FenixChannelSnapshot`, `FenixChannelPush`, `FenixChannelField`,
  `FenixChannelSection<T>`, `FenixChannelMonitor.ReportFeatureProblem`, `FenixChannelDisplay.Nd`. App names:
  `BaseAircraftDefinition.FormatEtaFromDistance` (protected static, so the sentences live in the definition),
  `SimConnectManager.IsConnected` and `RequestAircraftPositionAsync` (`AircraftPosition.GroundSpeedKnots`),
  `NavigationCalculator.CalculateDistance` and `CalculateBearing`.
- Both keys take the ND verdict and the route from one snapshot (spec 2.2). The time uses SimConnect's ground speed,
  never the ND's ground speed text (its `d` letter is plan 03's to measure).
- The present position is field 192 (truePos, a LatLon: the aircraft's own position by name and type). Field 56
  (ndPosCapt, an NdPositionData) holds its Lat and Lon beside the ND's map reference point (MrpValid, MrpTrack,
  MrpDist), so it may follow what the ND shows. Task 1 Step 2 checks both against the sim.
- **Tolerance**, fixed here; Task 1 measures against it and Task 7 checks it. To the destination: within 1 NM plus 0.5 %
  of the MCDU F-PLN DEST line's DIST taken at the same instant (Task 1: the same push; Task 7: read straight after the
  press). To the top of descent: within 1 NM plus 0.5 NM for each F-PLN leg distance summed in the reference (the ND's
  distance to the TO waypoint plus the F-PLN's leg distances down to "(T/D)"), plus 0.5 %. Why: the MCDU prints whole
  miles (0.5 NM a figure); a fly-by turn summed as its drawn pieces' chords loses under 0.1 NM; the app's spherical earth
  (`NavigationCalculator`, 3440.065 NM) and an ellipsoid differ by up to about 0.5 % of a distance; at 450 knots the
  aircraft flies about 0.25 NM between a press and the reading. A larger difference means the sum follows another path
  than the FMS (a missing STAR, the missed approach, a discontinuity, a route cut short): the tolerance must catch that,
  never absorb it.
- Nothing speaks until its in-sim checks pass: Tasks 1-5 are inert and pushed as usual; Task 6 is the switch commit,
  tested on your own build in Task 7 and pushed to `feat/fenix-display-channel` only once Task 7 passes (until then it
  may sit on `fc-p10-distances` on the remote). Until then D and Shift+D stay silent on the Fenix, as today.

## Review Focus

- **The destination ends the sum.** The missed approach follows the destination in the active route: the sum must stop
  at the destination (Task 4, `The_sum_stops_at_the_destination`).
- **A route the sum cannot follow** (no flight plan, the active leg not in the route, no destination element, a
  discontinuity before it, each refusal Task 1 found) says "Distance to destination not available", never a partial sum
  (Task 4, `An_unfollowable_route_is_not_available`).
- **The top of descent.** A marker behind the aircraft or within 0.1 NM ahead is "Past top of descent"; a marker off the
  route, two of them, or only go-around ones are "Top of descent not available"; with none, Task 1's indication decides
  (Task 4, `A_marker_behind_the_aircraft_is_past`, `A_marker_off_the_route_or_twice_is_not_available`,
  `No_marker_follows_task_one`).
- **The time.** No time under 30 knots (`FormatEtaFromDistance`); with SimConnect disconnected the distance is still
  spoken, without a time, never silence (Task 5, `The_time_needs_thirty_knots`,
  `A_disconnected_sim_still_speaks_the_distance`).
- **One snapshot.** An untrusted ND, no message or a wrong shape gives "not available" for both keys, and the field is
  logged once (Task 4, `The_answer_takes_one_snapshot`; Task 2, `A_wrong_shape_names_its_field`).

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/10-distances.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p10-distances`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 10`, with the `Plan: 10` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim] [in flight]: Measure the route before the code

Who: the controller with the owner, never an implementer. The team's local channel tools capture one push at a time, so
the route data, the markers, the position and the MCDU's text come from one instant; a distance computed from a capture
uses a scratch script outside the repo, never committed, with `NavigationCalculator`'s formula and radius. Setup: MSFS
2024, the Fenix on 3.0.0.7613, powered at a gate; a flight plan with a SID, at least one airway, a STAR, an ILS approach
with its missed approach and an alternate, loaded the owner's usual way; the captain's EFIS in NAV and the captain's
MCDU on F-PLN. Record each result with the date and build in `docs/fenix-channel/evidence/10-distances.md`. Tasks 2 and
3 need only Steps 1 and 2 (on the ground) and may start while Steps 4 and 5 wait for a flight; Task 4 waits for Step 6.

- [ ] **Step 1 [sim]: The route's structure.** Capture a push, then page through the F-PLN in the app's MCDU window
  (input Shift+M), capturing each page. Record every field present in each element of 2.4 (1, 2, 3, 6 and any of 4, 5,
  7-17), in 2.11 and in each 2.12 marker (1-7). Answer: which kinds (2.4.3) draw the path and which are symbols drawn on
  it (inferred from one cruise capture: 0 straight, 1 a turn piece, 2 the route's start; 3 an airport, 4 a symbol); which element
  is the destination (its kind and ident: the runway or the airport), whether the missed approach follows it in 2.4 and
  what marks its first element; whether 2.4 holds the whole flight plan; whether 2.11 is the TO waypoint and its ident
  names the first element of its leg in 2.4, also after a DIR TO ("T-P") and on the SID's altitude leg; each marker's
  type, label, color and go-around flag (T/C, T/D, DECEL; the T/D expected as type 0, MapMarkerEnum's first member), and
  whether any belongs to the alternate or secondary route.
- [ ] **Step 2 [sim]: The position.** 192 and 56 beside the sim's PLANE LATITUDE and PLANE LONGITUDE (simconnect MCP
  reads), with the captain's EFIS in ROSE ILS, ROSE VOR, ROSE NAV, ARC and PLAN (PLAN centered on a far waypoint), and
  whether 2.4 is still sent in each mode. 192 within 0.05 NM of the sim in every mode: the code reads 192; otherwise the
  field that is, recorded under "Changed from the plan", and Task 2 declares that one instead.
- [ ] **Step 3 [sim]: A discontinuity.** The owner makes a F-PLN DISCONTINUITY (for example, deleting the STAR's first
  waypoint in the MCDU window); capture; record how 2.4 shows it; then reload the flight plan.
- [ ] **Step 4 [in flight]: The sums.** At four points (climbing with the T/C ahead; cruising more than 50 NM before the
  T/D; within 10 NM after the T/D; on the approach before the final fix), with the captain's MCDU on F-PLN so that the
  DEST line is in the push, capture one push. Sum from 192 through the path (chords) to each candidate destination
  element and to the T/D marker's projection; compare with the DEST line's DIST of the same push, and the T/D with the
  ND's distance to the TO waypoint (68, line 2) plus the F-PLN's leg distances down to "(T/D)". Record each difference
  beside its tolerance. At the gate too, informative (the DEST line before take-off).
- [ ] **Step 5 [in flight]: Passing the top of descent.** From 5 NM before the T/D to 5 NM after it, descending at the
  T/D in managed descent, a push every 30 s: when the T/D marker leaves 2.12, where it sits until then (behind the
  aircraft or moving with it), and which markers or route fields change at the T/D.
- [ ] **Step 6.** Record the outcomes below in the evidence file, commit it (`docs(fenix-channel): plan 10's route
  measurements`) and push.

The outcomes, which Tasks 2 and 4 follow:
- `IsPath`: the kinds that draw the path; the symbol kinds are skipped.
- `IsDestination`: what names the destination element (its kind, or its ident where the kind does not set it apart).
- `BreaksPath`: how a discontinuity shows (expected: a start point after the first element).
- Refusals: each configuration of Steps 1-4 whose sum missed the tolerance and which the snapshot shows becomes a "not
  available" rule with its own test in Task 4.
- `ShowsPastTopOfDescent`: what Step 5 found showing a passed T/D once the marker is gone, or nothing (then "Top of
  descent not available" from there on). An indication outside the route data joins Task 2's decoder and `FenixRoute`
  only if it is an ND field no other plan declares (a field another open plan declares is a question for its claimant).
- Turns: if the chord sums miss the tolerance because of the turn pieces alone, Task 2 also reads 2.4.8 (turn side,
  string) and 2.4.9-11 (fixed32 in the notes; radius and the courses in and out once Step 1 confirms them), and Task 4
  sums a turn piece as its radius times its turn angle.
- Stop: a sum that misses the tolerance where the snapshot cannot show it, or a 2.4 that does not reach the destination
  for most of a flight (a cruise before the arrival is in the flight plan included), stops the plan: Status "waiting on
  owner", with the numbers. The owner rules whether D ships saying "not available" there, or this plan leaves D silent
  and ships Shift+D alone.

### Task 2: The route fields and their decoder

**Files:** Create `Services/FenixChannelFields.Route.cs`, `Services/FenixChannelRoute.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelRouteDecoderTests.cs` (new).

**Interfaces:**
- Consumes: `FenixChannelPush.TryGet` and `All`, `FenixChannelField` (`Type`, `Varint`, `AsDouble`, `AsBool`,
  `AsString`, `AsMessage`), `FenixChannelSection<T>`.
- Produces:
  - In `FenixChannelFields`, each with its contract member name and wire shape in a comment: `Remote.RouteDataCapt = 2`
    (routeDataCapt: nested RouteData), `Remote.TruePos = 192` (truePos: nested LatLon); `RouteData`: `ActRoute = 4`
    (actRoute: repeated nested leg, a type whose contract name is scrambled), `ActiveLeg = 11` (activeLeg: nested leg),
    `MapMarkers = 12` (mapMarkers: repeated nested MapMarker); `RouteLeg`: `Latitude = 1`, `Longitude = 2` (fixed64,
    double), `Kind = 3` (varint, an enum of a scrambled type), `Ident = 6` (string); `RouteLegKind`: one constant for
    each kind Task 1 names, with its measured meaning; `MapMarker`: `Lat = 1`, `Lon = 2` (fixed64, double),
    `MapMarkerType = 4` (varint, MapMarkerEnum), `IsGoAround = 6` (varint, bool); `MapMarkerKind`: `Tod = 0`
    (MapMarkerEnum.TOD, its first member), and Task 1's past indication if it is a marker type; `LatLon`: `Latitude = 1`,
    `Longitude = 2` (fixed64, double).
  - `internal readonly record struct FenixLatLon(double Latitude, double Longitude)`;
    `internal readonly record struct FenixRouteLeg(FenixLatLon Point, int Kind, string Ident)`;
    `internal readonly record struct FenixRouteMarker(FenixLatLon Point, int Type, bool IsGoAround)`;
    `internal sealed record FenixRoute(FenixLatLon Position, IReadOnlyList<FenixRouteLeg> Legs, FenixRouteLeg? ActiveLeg, IReadOnlyList<FenixRouteMarker> Markers)`.
  - `internal static class FenixChannelRoute` with `FenixChannelSection<FenixRoute> Decode(FenixChannelPush push)`.

- [ ] **Steps 1-5.** Rules: 2 absent: `Missing("no route data")`; 192 absent: `Missing("no present position")`; a field
  in another wire type than above: `WrongShape` with its path ("2", "2.4", "2.4.1", "2.4.2", "2.4.3", "2.4.6", "2.11"
  and "2.11.<n>", "2.12", "2.12.1", "2.12.2", "2.12.4", "2.12.6", "192", "192.1", "192.2"); an ident that is not valid
  UTF-8: `WrongShape("2.4.6")` (or "2.11.6"); an absent member reads its default (kind 0, ident "", type 0, go-around
  false, a coordinate 0); a route with no legs decodes (no flight plan); members not listed are skipped unread. Tests:
  `A_route_decodes_its_legs_marker_and_position` (2 {4: three legs with 1, 2, 3 and 6; 11: a leg; 12: one marker with
  no 4}, 192 {1, 2}: the legs in order, the active leg's ident, one marker of type 0, the position),
  `Absent_members_read_their_defaults`, `No_route_or_no_position_is_missing` ("no route data", "no present position"),
  `An_empty_route_decodes_with_no_legs`, `A_wrong_shape_names_its_field` (theory over the paths above, each "field
  <path> has the wrong shape"), `Unread_members_are_skipped` (a leg carrying 8, 9-11 and 16 decodes as one without).
  Commit: `feat(fenix-channel): the route fields and their decoder`.
- [ ] **Step 6 [owner].** The numbers and wire shapes this task adds checked against 3.0.0.7613 with the team's local
  channel tools ([FNX-23]), the scrambled leg type's included; a number that moved is fixed in
  `FenixChannelFields.Route.cs` alone and recorded in the evidence file.

### Task 3: Great-circle geometry

**Files:** Create `Services/FenixChannelRouteGeometry.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelRouteGeometryTests.cs` (new).

**Interfaces:**
- Consumes: `NavigationCalculator.CalculateDistance` and `CalculateBearing` (a sphere of 3440.065 NM).
- Produces: `internal static class FenixChannelRouteGeometry` with `double DistanceNm(FenixLatLon a, FenixLatLon b)`;
  `(double AlongNm, double CrossNm) Project(FenixLatLon from, FenixLatLon to, FenixLatLon p)` (the aviation
  formulary's along-track and cross-track distances on the leg's great circle; along negative behind `from`, cross
  negative left of the leg); `double PathNm(IReadOnlyList<FenixLatLon> points)` (consecutive distances summed; 0 for
  fewer than two points).

- [ ] **Steps 1-5.** Tests, each within 0.01 NM: `Distances_follow_the_app_s_formula` ((0, 0) to (0, 1): 60.04; (50, 8)
  to (50, 9): 38.59; a point to itself: 0), `A_point_projects_onto_a_leg` (leg (0, 0) to (0, 2), point (0.5, 1): along
  60.04, cross -30.02), `A_point_behind_a_leg_projects_negative` (leg (0, 0) to (0, 1), point (0, -0.5): along -30.02,
  cross 0), `A_path_sums_its_legs` ((0, 0), (0, 1), (1, 1), (1, 2): 180.11), `A_path_of_one_point_is_zero`. Commit:
  `feat(fenix-channel): great-circle distances along a route`.

### Task 4: Distance to the destination and to the top of descent

**Files:** Create `Services/FenixChannelRouteDistances.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelRouteDistancesTests.cs` (new).

**Interfaces:**
- Consumes: Tasks 2 and 3; `FenixChannelSnapshot`.
- Produces: `internal enum FenixDistanceKind { Distance, Past, NotAvailable }`;
  `internal readonly record struct FenixDistanceAnswer(FenixDistanceKind Kind, double Nm)`;
  `internal static class FenixChannelRouteDistances` with `double? ToDestination(FenixRoute r)`,
  `FenixDistanceAnswer ToTopOfDescent(FenixRoute r)`,
  `FenixDistanceAnswer Answer(FenixChannelSnapshot s, bool topOfDescent, Action<string> reportProblem)`, Task 1's rules
  as `internal static` `IsPath(FenixRouteLeg)`, `IsDestination(FenixRouteLeg)`, `BreaksPath(FenixRouteLeg)` and
  `ShowsPastTopOfDescent(FenixRoute)`, and `OnRouteNm = 0.5`, `PastWithinNm = 0.1` (the base's "past" threshold).

- [ ] **Steps 1-5.** Rules:
  - The path is the legs `IsPath` keeps, in order. The active start is the first path leg whose ident equals the active
    leg's (ordinal, not empty). No legs, no active leg or no such leg: no distance, and no top of descent.
  - Destination: the first leg of 2.4 at or after the active start that `IsDestination` names, path or symbol. None, a
    `BreaksPath` leg between the active start and it, or one of Task 1's refusals: null. Otherwise the position to the
    active start, then along the path legs between them, then to the destination's point (`PathNm`).
  - Top of descent: the markers of type `MapMarkerKind.Tod` that are not go-around. None: `Past` when
    `ShowsPastTopOfDescent`, else NotAvailable. More than one: NotAvailable. One: the route ahead is the position, the
    active start and every path leg after it; on the first of its legs where the marker lies within `OnRouteNm` across
    and between -0.1 and the leg's length plus 0.1 along, the answer is Distance (the legs before it plus the along
    distance), or Past when that is at most `PastWithinNm`. Not ahead but within `OnRouteNm` of the route behind (the
    path legs before the active start, then the position): Past. Neither: NotAvailable. No destination is needed: the
    T/D may lie in a route that does not reach the destination.
  - `Answer`: not trusted or no message: NotAvailable; a section that is not ok: `reportProblem` with its problem, then
    NotAvailable; otherwise `ToDestination` (null: NotAvailable) or `ToTopOfDescent`.
  Tests (a helper builds legs with Task 1's kinds, the destination and the break as Task 1 found them):
  `The_sum_runs_from_the_position_through_the_active_leg` (position (0, 0); path: start (0, -1), "A" (0, 1), "B" (1, 1),
  the destination (1, 2); active leg "A": 180.11 within 0.01), `The_sum_stops_at_the_destination` (two missed approach
  legs after the destination: still 180.11), `An_unfollowable_route_is_not_available` (theory: no legs; no active leg;
  an active ident not in the path; no destination; a break between "A" and the destination; each of Task 1's refusals:
  null), `Symbols_are_not_path` (a symbol between "A" and "B": 180.11),
  `The_top_of_descent_is_measured_along_the_route` (a T/D marker at (1, 1.5): Distance 150.10 within 0.01; the same with
  the destination left out of the route), `A_marker_behind_the_aircraft_is_past` (at (0, -0.5): Past; 0.05 NM ahead of
  the position: Past), `A_marker_off_the_route_or_twice_is_not_available` (2 NM off every leg: NotAvailable; two T/D
  markers: NotAvailable; only a go-around T/D: as none), `No_marker_follows_task_one` (no T/D marker, with and without
  `ShowsPastTopOfDescent`'s indication: Past, NotAvailable), `The_answer_takes_one_snapshot` (a snapshot with an untrusted
  verdict and a good message: NotAvailable; trusted with no message: NotAvailable; a wrong shape: NotAvailable, and
  `reportProblem` called once with "field 2.4.1 has the wrong shape"). Commit: `feat(fenix-channel): distance to the
  destination and to the top of descent`.

### Task 5: The sentences

**Files:** Create `Aircraft/FenixA320Definition.ChannelDistances.cs` (the sentences only); test
`tests/MSFSBlindAssist.Tests/FenixChannelDistancesKeyTests.cs` (new).

**Interfaces:**
- Consumes: `BaseAircraftDefinition.FormatEtaFromDistance`.
- Produces, in `FenixA320Definition`, `internal static` for the tests:
  `string DistanceSentence(bool topOfDescent, double nm, double groundSpeedKnots)` (`nm` as F0 in the invariant culture,
  "miles to destination" or "miles to top of descent", then `FormatEtaFromDistance(nm, groundSpeedKnots)`, as
  `AnnounceDestFromSDK` and `AnnounceTODFromSDK` speak); `const string PastTopOfDescent = "Past top of descent"`,
  `DestinationName = "Distance to destination"`, `TopOfDescentName = "Top of descent"` (for `SpeakNotAvailable`, which
  adds " not available", the base's words); `void SpeakDistance(bool topOfDescent, double nm, bool simConnected, Action<Action<double>> requestGroundSpeed, Action<string> speak)`
  (connected: ask for the ground speed and speak the sentence with it when it comes; disconnected: speak it at once
  without a time, since `RequestAircraftPositionAsync` does nothing while disconnected).

- [ ] **Steps 1-5.** Tests: `Sentences_follow_the_base` (412 NM at 400 knots: "412 miles to destination: 01:01:48"; 96
  at 480: "96 miles to top of descent: 00:12:00"; 412 at 25: "412 miles to destination"), `The_time_needs_thirty_knots`
  (29.9 knots: no time; 30: ": HH:MM:SS"), `Past_and_not_available_are_the_base_s_words` ("Past top of descent";
  `DestinationName + " not available"` is "Distance to destination not available"; "Top of descent not available"),
  `A_disconnected_sim_still_speaks_the_distance` (one sentence without a time, the request never made),
  `A_connected_sim_speaks_when_the_ground_speed_comes` (nothing before the callback; at 400 knots, "412 miles to
  destination: 01:01:48", once), `Sentences_are_the_same_in_every_culture` (de-DE and sv-SE: 412.4 NM at 25 knots gives
  "412 miles to destination"; 1234.6 at 400 gives "1235 miles to destination: 03:05:11"). Commit: `feat(fenix-channel):
  the D and Shift+D sentences`. Inert (no seam yet): pushed as usual.

### Task 6: The switch commit: D and Shift+D

**Files:** Modify `Aircraft/FenixA320Definition.ChannelDistances.cs`; test
`tests/MSFSBlindAssist.Tests/FenixChannelDistancesKeyTests.cs`.

**Interfaces:**
- Consumes: plan 01's `ChannelDistancesKey`, `ChannelKeyContext`, `SpeakNotAvailable`; Task 4's `Answer`; Task 5's
  sentences.
- Produces: the seam implementation.

- [ ] **Steps 1-5.** `ChannelDistancesKey` always sets `handled` (nothing else answers these keys on the Fenix). No
  monitor: `SpeakNotAvailable(ctx, name)`. Otherwise `Answer(ctx.Monitor.Snapshot(FenixChannelDisplay.Nd), topOfDescent,
  p => ctx.Monitor.ReportFeatureProblem("D and Shift+D", p))`: Past, `AnnounceImmediate(PastTopOfDescent)`;
  NotAvailable, `SpeakNotAvailable(ctx, name)`; Distance, one `Log.Debug("Fenix", ...)` line for Task 7 (the key, the
  distance to a tenth, the active and destination idents, the leg count; invariant culture), then `SpeakDistance(...,
  ctx.SimConnect.IsConnected, cb => ctx.SimConnect.RequestAircraftPositionAsync(p => cb(p.GroundSpeedKnots)),
  ctx.Announcer.AnnounceImmediate)`. Test: `The_seam_reads_one_nd_snapshot` (a source test over
  `FenixA320Definition.ChannelDistances.cs`: one `Snapshot(` call, with `FenixChannelDisplay.Nd`, and no other display
  named). Commit: `feat(fenix-channel): D and Shift+D speak from the route`. This is the switch commit: build and run it
  on your own machine for Task 7; push it to `feat/fenix-display-channel` only in Task 9.

### Task 7 [in flight]: Check D and Shift+D against the MCDU

With MSFSBA built from the plan's branch, Task 6 included, the Fenix on 3.0.0.7613 and a flight plan as in Task 1 (a
different one if possible). The reference is the captain's MCDU on F-PLN, read straight after each press (the owner in
the MCDU window, or the team's tools reading the push); the computed value is the press's line in `debug.log`, read
straight away (it rotates in about ten minutes). Record each result in `docs/fenix-channel/evidence/10-distances.md`;
a check that fails is fixed and run again before Task 9.

- [ ] At the gate: D says "<n> miles to destination", with no time; recorded beside the DEST line (within the tolerance,
  or one of Task 1's refusals saying "Distance to destination not available").
- [ ] At Task 1's four points: D and Shift+D within the tolerance, each with a time.
- [ ] After the T/D: Shift+D says "Past top of descent" (or what Task 1's outcome gives).
- [ ] Captain ND Brightness at Off (the app's panel control): `fenix_channel.log` shows the ND "display off"; D says
  "Distance to destination not available" and Shift+D "Top of descent not available"; the brightness back.
- [ ] A fresh power-up with no flight plan: both keys say "not available".
- [ ] [owner] By ear with NVDA: the sentences read as written.

### Task 8: D and Shift+D in docs/a32nx.md

**Files:** Modify `docs/a32nx.md` (only under the "D and Shift+D" sub-heading of "Fenix display channel").

- [ ] **Step 1.** In place of "(Plan 10 writes this section.)": the fields read, Task 1's rules (path, destination, break,
  past T/D), the tolerance and why, the refusals, and where the evidence is. Commit: `docs(fenix-channel): D and Shift+D
  from the route`.

### Task 9 [controller]: Land

- [ ] The whole-plan review (index, "Running a plan with subagents": the ownership diff, a test for every Review Focus
  line, the handoff and the execution record). Set **Status** to landed with the date and the switch commit (or waiting
  on owner, under Task 1's stop rule), fill in "Tests at landing" and "Handoff to plan 12", and push everything, the
  switch commit included (index workflow steps 4-6).

## Handoff to plan 12

(Filled in while working. Expected:)

- **For a pilot:** guide `guide/airbus-aircraft.md`, "Fenix A320 CEO", "Aircraft-specific hotkeys and callouts": the
  sentence saying Output D and Shift+D are not offered on the Fenix changes (they work, computed from the Fenix's flight
  plan, with what "not available" means; plan 02 changes the speeds' half of it). Hotkey guide
  `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`: the output readouts gain "D Distance to destination" and
  "Shift+D Distance to top of descent". Changelog `feature`: "On the Fenix, D and Shift+D now tell you how far it is,
  and how long at your ground speed, to your destination and to the top of descent, worked out from the Fenix's own
  flight plan."
- **Rule** (one line, 397 characters):
  ```
  - [FNX-?] Fenix D and Shift+D (`FenixChannelRouteDistances`) speak a distance only where the in-sim check matched the route sum to the MCDU's DEST line within 1 NM plus 0.5 %: the sum stops at the destination, never in the missed approach, a route it cannot follow says "not available", never a partial sum, and the time uses SimConnect's ground speed. Full: docs/invariants/fenix-channel.md#fnx-?
  ```
  Full text: Task 1's rules and refusals, the tolerance and its reasons, the measurements behind them.
- **Frozen files:** none.
- **Also seen on:** the FBW A32NX and Headwind A330 D (`MainForm.Announcers.cs`) says "Destination distance not
  available", where the base, the PMDG 737 and this plan say "Distance to destination not available": a presentation
  difference across aircraft, for its own issue.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** not started
- **Claimed by:** none
- **Worked by:**
- **Started:**
- **Landed:**
- **Tests at landing:**
- **Tasks:**
- **Waiting on the owner:**
- **In-sim results:**
- **Changed from the plan:**
- **Rulings made while working:**
