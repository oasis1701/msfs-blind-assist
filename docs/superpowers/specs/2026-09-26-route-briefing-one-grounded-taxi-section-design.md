# Route briefing: one short taxi section, grounded in the scenery — design

Date: 2026-09-26. Branch: `claude/route-briefing-real-world-only` (continued on the owner's instruction; no new branch).

## 1. What changes for a pilot

The Describe Route briefing's **TAXI OUT AND TAXI IN** section becomes ONE short part: a paragraph for the taxi
out and a paragraph for the taxi in, written the way the old "Real-world practice" part read, but built on the
route MSFS Blind Assist works out from the pilot's own scenery. There is no second "computed route" version any
more, no list of every exit, and no talk about data sources.

- **Taxi out:** where the taxi starts (the stand the aircraft is parked at, or a typical stand when it is not at
  the origin yet — said to be typical, not assigned), the taxiways in order with the turn at each change, every
  hold-short point with the runway it protects, the departure runway, and the total distance.
- **Taxi in:** which side to leave the landing runway, the exit taxiway and how far down it is, the next exit if
  that one is missed, the taxiways to the gate with their turns, every runway crossed on the way, the gate, and the
  total distance.
- **From the AI's own knowledge, but only about taxiways, runways and stands the route data names:** aircraft
  restrictions (for example an A380's wingspan limits, alongside the scenery's own stand sizes and taxiway-width
  notes), current operational information (for example a NOTAM closing a taxiway on the route), and usual practice
  at that airport.
- **Kept:** the owner's question, word for word; the note when SayIntentions assigned a different runway from the
  flight plan; one unit for every distance.
- **Exceptions to "no talk about sources":** a leg whose route comes from OpenStreetMap says so in a few words
  (taxi guidance cannot use it), and a leg with no ground data at all gets the AI's own route, said to be from
  general knowledge.

## 2. Why

Live KMEM→KATL (2026-09-26) with the real-world-only briefing (`06148c89`): the AI invented a departure route
(S, C, V, V1 — KMEM's scenery has no V1) and arrival exits (V3, V4 — KATL has neither). Run against the same fs2024
database, the planner's route was J, right onto T, P, left onto N, M, right onto M2 to 36L, and exit B11 on the
right at 6,025 ft, B, D (crossing 08R), E, F5, F to C50. The owner found the grounded version reliable but too
long with two sections.

Timing, measured on the owner's machine against the fs2024 database for this flight: the planner's own work took
150 ms for both airports; the rest was the online taxiway-name wait, a full 8 s on every run because the fetch did
not complete in time. The owner chose to **keep that wait as before** (completeness over speed).

## 3. Scope

- **Restored exactly as before the strip (`dd1c72ae`):** the route planner and everything it uses — tiers per
  airport (scenery navdata; OpenStreetMap names filling unnamed taxiways, waiting up to 8 s; an OpenStreetMap
  planning graph when the scenery has no taxiways), the stand and exit pickers, turn directions, the TAXI ROUTES
  block, SayIntentions' runways and gate, SimBrief aircraft classification, the planner's time budget. The strip
  commit `06148c89` is reverted as a whole.
- **Carried over from the strip:** the owner's question in the owner's own wording ("… at [ICAO Code] from
  [Runway] to [Terminal/Gate] in a [Aircraft Type] …") and the compass-point runway test for
  `BriefingRunwayChoice`.
- **Changed:** the prompt's section 7 only, plus the docs and CLAUDE.md bullets that describe two sections.
- **Not changed:** the TAXI ROUTES block's content. Length is controlled by the prompt, and the block keeps what
  the AI needs to judge restrictions (wingspan, stand, taxiway-width notes, the exits list).

## 4. The prompt's section 7

One instruction set, replacing the old a) and b) parts:

1. For each leg, answer the owner's question (quoted verbatim from `GeminiService.RealWorldTaxiQuestion`, never
   written into the briefing) from the TAXI ROUTES block, as one short paragraph.
2. The route — stand, taxiways and their order, turns, hold-shorts, exit, next exit, crossings, gate, distances —
   comes from the block only: names exactly as given, turns and sides exactly as given, no turn where the block
   gives none, never a taxiway, exit or stand name the block does not contain.
3. From its own knowledge the AI may add only restrictions, current operational information and usual practice
   that concern a taxiway, runway or stand the block names; it never offers a different route.
4. Brevity: a short paragraph per leg; do not list every exit; no data-source commentary except the two
   exceptions in §1; mention a block note only when it changes what the pilot does or hears (a SayIntentions
   runway difference, a typical rather than assigned stand, a SayIntentions gate the scenery lists under another
   name or found at a different position, a width or size note).
5. Unchanged rules: the distance unit; a SayIntentions runway difference named here and in the departure/arrival
   narrative; a representative stand called typical, not assigned; an unavailable leg with no ground data answered
   from general knowledge and said to be.

`RouteBriefingText.RemoveEchoedTaxiQuestion` stays in both providers.

## 5. Testing

- The restored planner, renderer, picker and turn tests come back with the revert.
- `RouteDescriptionPromptTests` pins: one section (no "Real-world practice" heading, no a)/b) parts); the question
  verbatim from the constant and never written out; names only from the block; own-knowledge additions only about
  named taxiways, runways and stands, never a different route; the brevity rules; the unit, SayIntentions runway,
  typical-stand and no-ground-data rules.
- In-sim (owner): KMEM→KATL with SayIntentions — the taxi out names J, T, P, N, M, M2 to 36L; the taxi in names
  B11 on the right, D across 08R, E, F5, F to C50; one short section; no invented names; the question not read
  back.
