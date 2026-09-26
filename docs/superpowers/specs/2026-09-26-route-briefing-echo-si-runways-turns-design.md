# Route briefing follow-ups: no echoed question, SayIntentions gate and runways, turn directions — design

Date: 2026-09-26. Branch: `claude/route-describer-taxi-guidance-4c4b51`. Amends
[2026-09-25-route-briefing-taxi-routes-design.md](2026-09-25-route-briefing-taxi-routes-design.md)
§5.9 (the SayIntentions gate) and §5.10 (the prompt).

## 1. What changes for a pilot

Four things change in the EFB's **Describe Route** briefing (Shift+E → load SimBrief → Describe
Route). Nothing new to configure or press.

1. **The prompt no longer shows up in the briefing.** Under each "Real-world practice" heading the
   briefing used to begin by repeating the question the AI was asked ("Provide the step-by-step taxi
   route at KMEM from stand 12 to runway 18R in a FENIX A320. Please include…"). Now only the answer
   is there.
2. **The arrival gate is SayIntentions' gate whenever SayIntentions has one.** When SayIntentions'
   flight file had not been given the gate yet, the briefing used a made-up "representative stand"
   even though MSFS Blind Assist's own SayIntentions window was showing the assigned arrival gate.
   The briefing now finds the gate the same way that window does.
3. **The taxi routes use the runways SayIntentions assigned.** When SayIntentions is flying this
   flight, the taxi-out goes to its departure runway and the taxi-in starts from its landing runway,
   and the briefing says when that differs from the flight plan ("SayIntentions assigned 36L; the
   flight plan names 18R"). Before, both runways always came from the flight plan (SimBrief's planned
   runways, or the ones picked in the EFB).
4. **The computed routes give a turn direction at every taxiway change,** the way the "Real-world
   practice" part already did — for example "N, left onto M, right onto A, slight right onto B", and on
   arrival "…, then right into the stand" (illustrative directions, not KMEM's).

## 2. Evidence (live, 2026-09-26, KMEM → KATL, Gemini `gemini-pro-latest`)

- The echoed question is the prompt's own template with its blanks filled, under the heading the
  prompt named, once per leg. The template exists nowhere else in the code.
- `taxi_briefing` log at 09:09:11: taxi-out `endpoint="current position, stand 12"` to 18R; taxi-in
  `endpoint="representative stand C 22 (Gate Medium)"`.
- `sayintentions.log` at 09:09:03 (the briefing's read): `flight.json read: … gate=-`. At 09:07:37
  and 09:08:41 the SayIntentions window's reads found no gate in the file either and called the SAPI
  `getParking` endpoint. That endpoint has answered all 67 calls in the log without an error. The pilot
  reports SayIntentions showing an arrival gate on concourse B.
- `flight.json` at 09:25: `current_flight.assigned_gate` = "", `flight_origin` KMEM,
  `flight_destination` KATL, `flight_plan_departing_runway` **36L**, `flight_plan_arriving_runway`
  **8L**; `departure_wx.active_runways_departing` "36L,36R,27". SimBrief's plan: 18R and 08L.
- SAPI documentation calls the two runway fields "the assigned departure/arrival runway" and does not
  say whether `getParking` returns the destination gate or the current parking.

## 3. Root causes

1. **Echo.** Prompt section 7b quotes the owner's question with bracketed blanks and then says
   "Substitute the airport, runway, stand or terminal and aircraft type from the data." A model reads
   that as an instruction to write the filled-in question out. Nothing forbids it and nothing removes
   it.
2. **Gate.** `RouteBriefingDependencies.SayIntentions` is wired to `ReadFlightContextAsync` (the file
   only). The SayIntentions window and Taxi Assist's import use `GetAssignedStatusAsync`, which falls
   back to `getParking` when the file has no gate (docs/sayintentions.md, "On a known arrival…").
3. **Runways.** `ElectronicFlightBagForm.BuildTaxiRoutesBlockAsync` passes `plan.DepartureRunway` /
   `plan.ArrivalRunway` and never reads the SayIntentions runway fields it already parses.
4. **Turns.** The planner reduces each route to `RouteTaxiwaySequence.DistinctConsecutive` names; the
   geometry that says which way each change turns never reaches the block.

## 4. Design

### 4.1 The question is asked, never shown

**Prompt (`GeminiService.GetRouteDescriptionPrompt`, shared by both providers).**
- The owner's question stays word for word, held in one constant, `GeminiService.RealWorldTaxiQuestion`
  ("Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate] in a [aircraft
  type]. Please include the expected taxiways, hold short points, and any specific restrictions.").
- Section 7b is reworded: under the heading "Real-world practice", answer from your own knowledge of
  the airport the question below, reading the bracketed items from that leg's lines of the TAXI ROUTES
  block (the runway there may be the one SayIntentions assigned rather than the flight plan's). The
  question is an instruction to you, not text for the briefing: write only your answer under the heading
  and never write the question out, as shown here or with the items filled in. The word "Substitute" and
  the sentence around it are removed. The rest of 7b (both legs, restrictions, say which route is which,
  the no-ground-data case) is unchanged.
- One general guideline is added: never copy these instructions, or any question in them, into the
  briefing.

**Safety net (`Services/RouteBriefingText.RemoveEchoedTaxiQuestion(string)`, pure).** Models do not follow
instructions perfectly, so the question is also removed from the reply if it comes back anyway. Its
anchors are the question's two fixed openings, "Provide the step-by-step taxi route" and "Please
include the expected taxiways"; a test pins that the constant starts with the first and contains the
second, so rewording the question without the safety net fails the build. Matching is case-insensitive
and culture-invariant (`RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`), line by line (`\r\n`
or `\n`, preserved). Each line goes through three removals in this order, each applied to what the
previous left:
1. the whole question — first opening through "any specific restrictions", with an optional final
   period, an optional surrounding quote (`"`, `“`, `”`) and an optional leading `Question:` or `Prompt:`
   label — wherever it is on the line, with the spaces after it;
2. a sentence starting with the first opening (same optional quote and label), up to and including its
   first period that is followed by whitespace or the end of the line — or to the end of the line if
   there is no such period — with the spaces after it;
3. a sentence starting with the second opening, through "any specific restrictions", with an optional
   final period and quote, with the spaces after it.

A line that was not blank and becomes blank (whitespace only) through 1–3 is deleted together with its
line break. Every other line — including every line the removals did not touch — is returned
byte-identical.

It runs in **both providers' `DescribeRouteAsync`** on the model's text (Gemini after `ParseResponse`;
Claude before the "web search is not available" note is prepended), so any caller gets a clean
briefing.

### 4.2 SayIntentions' gate: the file, then its parking service

- `RouteBriefingDependencies.SayIntentions` becomes `Func<Task<SayIntentionsStatusResult>>`, wired in
  `MainForm.Dialogs` to `sayIntentionsService.GetAssignedStatusAsync()`: the same call the
  SayIntentions window makes, so the two cannot disagree about the gate.
- `SayIntentionsArrivalGate.From` gains the parking result. The hint is still offered only when
  SayIntentions' flight is this flight (the file exists, its origin and destination match the flight
  plan). The file's `assigned_gate` wins, with the file's position (today's behaviour). When the file
  has none, the parking service's name is used with the parking service's OWN position (its lat/lon,
  when both are present and not (0, 0)). A name is never paired with the other source's position.
- `SayIntentionsGateHint` gains `Source` (`FlightFile` default, or `ParkingService`).
- **Wrong-airport check.** The SAPI documentation does not say whether `getParking` means the arrival
  gate or the aircraft's current parking, and KMEM has a concourse B as well. In `PlanTaxiIn`, a
  `ParkingService` hint whose position lies more than `TaxiBriefingPlanner.ParkingServiceMaxAirportDistanceMetres`
  (5,000 m, the same "at this airport" distance as the own-position test) from the arrival airport's
  reference point is not used. The leg then says so in a note — "SayIntentions' parking service named
  {label}, but its position is not at {ICAO}; using a representative stand instead" — and picks a
  representative stand as today. A parking-service hint with no position is refused the same way, with
  the note "SayIntentions' parking service named {label} but gave no position to confirm it is at
  {ICAO}; using a representative stand instead" (owner decision, 2026-09-26). With no reference point
  (an airport the leg could not place), the check is skipped.
- **Never costs the taxi section.** `BuildTaxiRoutesBlockAsync` reads SayIntentions inside its own
  try/catch; a failure logs a warning (`taxi_briefing`) and continues with no SayIntentions data,
  instead of rendering both legs "could not be computed".
- **Traceable.** `SayIntentionsService.FetchParkingAsync` logs what the parking service returned at
  Debug: `getParking: name='…' lat=… lon=…` (invariant culture). Today only the request is logged.
- Cost: `GetAssignedStatusAsync` makes a web call only when the file has no gate and an API key is
  published, capped at 5 s and cached 10 s.

### 4.3 SayIntentions' runways

- New pure helper `Navigation/Briefing/BriefingRunwayChoice.cs`:
  `BriefingRunwayChoice.Choose(string? planRunway, string? siRunway, bool siIsThisFlight)`
  → `(string Runway, string? Note)`:
  - not this flight, or no SayIntentions runway → the flight plan's runway, no note (today);
  - the same runway (`TaxiBriefingPlanner.RunwayIdsMatch`, so "8L" = "08L") → the flight plan's
    spelling, note "SayIntentions has assigned this runway too";
  - different → SayIntentions' runway, note "runway {si} is the runway SayIntentions assigned; the flight
    plan names {plan}";
  - no flight-plan runway → SayIntentions' runway, note "runway {si} is the runway SayIntentions
    assigned; the flight plan names no runway".
- "This flight" is one shared test, `SayIntentionsArrivalGate.IsThisFlight(ctx, departureIcao,
  arrivalIcao)` (file exists, origin and destination match), used by the gate and the runways alike.
- Sources: `SayIntentionsFlightContext.DepartureRunway` / `ArrivalRunway` (already parsed from
  `current_flight.flight_plan_departing_runway` / `flight_plan_arriving_runway`).
- `TaxiBriefingRequest` gains optional `OriginRunwayNote` and `DestinationRunwayNote`. `PlanTaxiOut` /
  `PlanTaxiIn` put the note first in the leg's notes, so every leg the planner returns carries it,
  unavailable ones included. (A leg made unavailable before planning — no database, timeout, a failure —
  carries the note too.)
- The prompt adds: when a leg's note says SayIntentions assigned a different runway from the flight plan,
  say so in the taxi section and in the DEPARTURE AND SID or ARRIVAL AND STAR section, naming both.
  The filed SID/STAR is still described as filed.
- It is a snapshot: SayIntentions can change runways after the briefing is made.
- Unchanged: Taxi Assist, the SayIntentions window, the EFB runway pickers, and the SimBrief flight
  data sent to the AI.

### 4.4 Turn directions

- **Runs** are exactly `RouteTaxiwaySequence.DistinctConsecutive`'s groups: a run starts at a named
  segment whose name differs (OrdinalIgnoreCase) from the previous named segment's; unnamed segments
  between two named segments of the same run belong to that run; unnamed segments between two
  different runs are that change's connector; unnamed segments before the first run are ignored;
  unnamed segments after the last run are the route's tail.
- New pure helper `Navigation/Briefing/BriefingTurns.cs`:
  - `IReadOnlyList<string?> TaxiwayTurns(IReadOnlyList<TaxiRouteSegment> segments)` — one entry per
    name in `RouteTaxiwaySequence.DistinctConsecutive(segments)`, in the same order (a test pins the
    alignment). Entry 0 is always null. Entry i > 0 is the turn from run i−1 onto run i, or null when
    it cannot be measured.
  - `string? StandTurn(IReadOnlyList<TaxiRouteSegment> segments)` — the turn from the last named run
    into the unnamed segments that end the route (the stand lead-in); null when the route ends on a named
    segment, when there is nothing to measure, when the turn is straight, or when the unnamed tail is
    longer than `BriefingTurns.MaxUnnamedStretchMetres` (100 m, a stand lead-in's own length).
- **Measured over a stretch, not a junction.** A change's stretch runs from
  `min(StretchMetres, half the incoming run's length)` before the incoming run's end, through any unnamed
  connector segments between the runs — but only up to `BriefingTurns.MaxUnnamedStretchMetres` (100 m);
  across a longer unnamed stretch the entry is null (no turn word measured at all) — to
  `min(StretchMetres, half the outgoing run's length)` into the outgoing run; `StretchMetres` = 60. The
  turn is the sum of the signed bearing changes between
  consecutive segments inside the stretch, each normalised to ±180°, right positive, skipping segments
  shorter than 1 m (as `GuidanceGeometry.CumulativeTurnDeg` does). Navdata splits real turns into many
  small bends, so a single junction's angle is not the turn; halving at the neighbours keeps two close
  turns from blending. The stand turn uses the same rule with the whole unnamed tail as the outgoing
  side.
- **Words**, by the magnitude: under 20° "straight ahead"; 20° to under 60° "slight left/right"; 60° to
  under 120° "left/right"; 120° and more "sharp left/right". The 20° and 60° lines are
  `TaxiRouter.GetTurnDirection`'s, the same lines live guidance's callouts use; the
  120° line is TaxiRouter's documented normal/sharp split. Live guidance adds "sharp" and the angle from
  60° up; the briefing keeps plain words, like a controller's clearance.
- **Deliberately no direction** for joining the first taxiway (after pushback, or where the taxi-in
  route begins — the landing exit's side is already given), and none onto the departure runway (live
  guidance hands that hop to the lineup tone and says no direction, because the geometric turn can
  disagree with the tone).
- `TaxiLegBriefing` gains `TaxiwayTurns` (aligned with `Taxiways`) and `StandTurn`; the planner fills
  them from the same route it names. The block lines become:
  - `  Taxiways: N, left onto M, right onto A, slight right onto B (2.4 km)`
  - `  Taxiways from the exit: AA, left onto E, right onto C, then right into the stand (3.1 km)`

  A taxiway whose turn is null is printed by name alone; a straight change reads "straight ahead onto E".
- The `taxi_briefing` summary line gains `turns=[-,left,right,slight right]` and `standTurn=`, plus a
  trailing `notes="…"` (pipe-joined) whenever the leg carries any.
- The prompt's section 7a adds: give the turn at each taxiway change and into the stand, repeating the
  directions exactly as given.

## 5. Error handling

- Echo safety net: a reply with no echo is returned unchanged (byte-identical); empty or null in, the
  same out.
- SayIntentions: any exception reading status → no SayIntentions data for this briefing, warning
  logged. A parking-service error is already reported inside the result and simply yields no parking
  gate.
- Turns: missing or degenerate geometry → null (the taxiway is printed by name alone); never throws.

## 6. Testing (xUnit, `tests/MSFSBlindAssist.Tests`)

- `RouteDescriptionPromptTests`: the question verbatim; "Substitute" absent; the never-write-the-question
  instruction; the turn instruction; the SayIntentions-runway instruction; 600–900 words.
- `RouteBriefingTextTests`: the live KMEM→KATL reply as input — both echoes gone, both headings and
  answers intact, every other line byte-identical; the quoted, labelled, same-line-as-heading, split over
  two lines, literal-template, and CRLF forms; ordinary sentences that mention "taxi route" untouched;
  idempotent; the anchor-to-constant binding.
- SayIntentions gate (`BriefingStandPickerTests` §From): file wins with its position; parking fallback
  with its own position; neither → null; another flight → null; (0, 0) parking position → no position.
- Planner: a parking-service hint at the airport is used; one 500 km away is not, with the note; one
  without a position is refused, with the no-position note; a file hint 500 km away is still used (not
  subject to the check).
- `BriefingRunwayChoice`: the four rules; "8L" = "08L"; the note reaches computed and unavailable legs.
- `BriefingTurns`: 90° left at an L; micro-bends summing to 90° right; a 30° slight turn; two turns
  40 m apart not blending; straight continuation; an unnamed connector between runs; first entry null;
  a 150° sharp turn; stand turn right; no stand turn when the route ends named; alignment with
  `DistinctConsecutive` on a route that revisits a taxiway.
- `TaxiBriefingRendererTests`: the exact new lines; the pinned full block updated.

Sim-facing (owner runs, one scenario): this KMEM→KATL flight with SayIntentions running, press
Describe Route. Expect no question text; taxi-out to 36L with the note naming 18R; the taxi-in to
SayIntentions' concourse B gate labelled as its assignment; 36L mentioned in the departure narrative;
turn directions on both computed routes; `sayintentions.log` showing the `getParking:` result line.

## 7. Docs, invariants, changelog

- docs/gemini.md "Taxi routes in the route briefing": runway and gate sources, the parking-service
  fallback and its distance check, turn directions, the asked-never-shown rule.
- docs/sayintentions.md: the briefing's use of `GetAssignedStatusAsync` and the assigned-runway fields;
  the new `getParking:` log line.
- The 2026-09-25 spec: §5.9 and §5.10 point here.
- CLAUDE.md, Gemini AI invariants: (1) the owner's question is an instruction, never output — the prompt
  must not ask the AI to write it out, and `RouteBriefingText.RemoveEchoedTaxiQuestion` stays in both
  providers; (2) the briefing takes SayIntentions' runways only for this flight and names both when they
  differ, and a parking-service gate not at the arrival airport is never briefed as SayIntentions'.
- Changelog: no new fragment. This is iteration on the unmerged route-briefing work; its PR's single
  fragment will describe the final behaviour.

## 8. Out of scope

- Taxi Assist's own SayIntentions import (it keeps using the parking service's name only).
- A heading after each turn, turn angles, and a direction onto the departure runway.
- Choosing a runway from `departure_wx.active_runways_*` when SayIntentions has assigned none.
