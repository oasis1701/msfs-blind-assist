# Route briefing: expected taxi routes — design

Date: 2026-09-25. Branch: `claude/route-describer-taxi-guidance-4c4b51`.

## 1. What changes for a pilot

The EFB's **Describe Route** briefing (Shift+E → load SimBrief → Describe Route) gains one
section, **TAXI OUT AND TAXI IN**. For the departure it says where the taxi starts (where the
aircraft is parked, or a typical stand for the aircraft when it is not at the origin yet), the
taxiways to the departure runway in order, and every hold-short point with the runway it
protects. For the arrival it says whether to plan to leave the runway to the **left or right**,
which exit taxiway and how far down the runway it is, the next exit if that one is missed, the
taxiways from there to the gate, and every runway crossed on the way with its hold-short point.

The gate is the SayIntentions-assigned gate when SayIntentions is running *for this flight*;
otherwise a sensible stand for the aircraft — a cargo stand for a freighter, a stand that fits an
A380's wingspan, a gate the pilot's airline uses when the scenery records airline codes — and the
briefing says plainly that it is an example stand, not an assignment.

Every part names its source: the scenery's own taxiway data, OpenStreetMap ("planning only")
when the scenery has no taxiway data for that airport, or the AI's own knowledge when neither
exists. The AI always adds a short **real-world practice** note (typical exits, standard routes,
restrictions such as an A380 being kept off certain taxiways), and says where that differs from
the computed route and which is which. Nothing new to configure or press; the briefing takes a
couple of seconds longer; if the taxi part cannot be worked out, the rest of the briefing still
comes.

## 2. Scope and non-goals

- Taxi Assist, taxi guidance (Ctrl+T), the Landing Exit planner and GSX docking are **not**
  changed in behaviour. The briefing reads the same airport data they use, through the same
  pure code (`TaxiGraph`, `TaxiRouter`, `RouteRunwayCrossings`, `GetLandingExits`), so the
  taxiways it names are the ones Taxi Assist offers later.
- The one shared-code change outside the new files is lifting the runway lineup-entry-node
  maths out of `TaxiAssistForm.PopulateDestinations` into `Navigation/RunwayLineupTarget`, which
  the form then calls. Byte-identical behaviour; one owner so the two cannot disagree.
- The OpenStreetMap graph is **briefing-only**. It is built inside the planner, never stored where
  guidance could reach it, and the briefing labels it "planning only; taxi guidance cannot use
  it". `TaxiDataMerger`'s rule ("never steer on online geometry") is untouched.
- No new announcements. Only the EFB status line changes ("Computing taxi routes…" before the
  existing "Generating route description…"). The existing "Generating route description, please
  wait" / "Route description ready" announcements stay as they are.
- Not in scope: hold-short truncation of the departure route (guidance's private
  `TruncateToHoldShort`); the briefing only *describes* the hold-short points.

## 3. Data flow

```
Describe Route
  ├─ plan = _flightPlanManager.CurrentFlightPlan (DepartureICAO/Runway, ArrivalICAO/Runway,
  │        AircraftTypeIcao, AircraftName, AircraftMaxPassengers, AirlineIcao)
  ├─ own position  ← SimConnectManager.RequestAircraftPositionAsync (1.5 s cap, null if absent)
  ├─ SI context    ← SayIntentionsService.ReadFlightContextAsync (local file, no network)
  ├─ TaxiBriefingPlanner.PlanAsync(request, provider, gateSource, 20 s budget)   [background]
  ├─ block = TaxiBriefingRenderer.Render(briefing)
  ├─ flightData = plan.ExtractedFlightData + "\n\n" + block     (stored data NOT modified)
  └─ aiProvider.DescribeRouteAsync(flightData)  → prompt now has section 7
```

The stored `ExtractedFlightData` stays pure SimBrief: the taxi facts change when the pilot edits a
runway in the EFB or moves the aircraft, so they are recomputed per press and appended for that
call only.

## 4. Tiers per airport

| Tier | When | Route source |
| --- | --- | --- |
| 1 Navdata | `provider.GetTaxiPaths(icao)` has rows | Graph built exactly as `LandingExitForm` builds it (paths, `ParkingSpotSource.GetNamedSpots`, runway starts, runways). |
| 2 OpenStreetMap | Tier 1 has zero rows, the provider is the `AugmentingAirportDataProvider`, `Enabled` is true, and cached/fetched online data exists within the planning budget | Throw-away graph from the online `AirportTaxiData` (see §5.7). Runways and runway starts still come from the database. |
| 3 None | Neither | The block says no ground data exists; the prompt makes the AI's own step-by-step route the primary content. |

The block always states the tier for each leg. Taxi-out and taxi-in are computed independently
(different airports, independent failures).

## 5. Components

All new code lives under `MSFSBlindAssist/Navigation/Briefing/` (namespace
`MSFSBlindAssist.Navigation.Briefing`) except where noted. Everything there is pure and
unit-tested; the sim-facing parts are the EFB wiring and the position request only.

### 5.1 `FlightPlan` + `SimBriefService`

`FlightPlan` gains `AircraftTypeIcao` (`aircraft/icaocode`), `AircraftName` (`aircraft/name`),
`AircraftMaxPassengers` (`int?`, `aircraft/max_passengers`, null when absent or unparseable) and
`AirlineIcao` (`general/icao_airline`). `SimBriefService.ParseSimBriefXML` fills them; it becomes
`internal` so a test can parse a minimal OFP string. `ExtractFlightData` is unchanged.

### 5.2 `AircraftSizeClass`

```csharp
public enum IcaoCodeLetter { Unknown, A, B, C, D, E, F }
public sealed record AircraftProfile(string TypeCode, string DisplayName, double? WingspanMetres,
    IcaoCodeLetter CodeLetter, bool IsFreighter, double TouchdownSpeedKts);
public static class AircraftSizeClass
{
    public static AircraftProfile Resolve(string? typeCode, string? name, int? maxPassengers);
    public static IcaoCodeLetter LetterForWingspan(double metres); // <15 A, <24 B, <36 C, <52 D, <65 E, else F
    public static double TouchdownSpeedKts(IcaoCodeLetter l);      // F 140, E 140, D 135, C 130, B 115, A 70, Unknown 130
    public static double MinTaxiwayWidthMetres(IcaoCodeLetter l);  // ICAO Annex 14: A 7.5, B 10.5, C 15, D 18, E 23, F 25, Unknown 0
    public static bool LooksLikeFreighter(string? typeCode, string? name, int? maxPassengers);
}
```

- Wingspan comes from a built-in table keyed by SimBrief/ICAO type code (about 70 common types:
  A388 79.75, A35K/A359 64.75, A346/A345 63.45, A343/A342 60.3, A339/A338 64.0, A333/A332 60.3,
  A306/A3ST 44.84, A310 43.9, A21N/A20N/A19N 35.8, A321/A320/A319/A318 34.1, BCS1/BCS3 35.1,
  B748 68.4, B744 64.44, B741/B742/B743 59.64, B77W/B77L 64.8, B773/B772 60.93, B78X/B789/B788
  60.12, B764 51.92, B763/B762 47.57, B753/B752 38.05, B3XM/B39M/B38M/B37M 35.92,
  B739/B738/B737/B736 35.79, B735/B734/B733 28.88, B732 28.35, B722 32.92, B712 28.45, MD11/MD1F
  51.7, DC10 50.4, MD90/MD88/MD83/MD82 32.87, L101 47.34, E295 35.12, E290 33.72, E195/E190 28.72,
  E75L 28.65, E175/E170/E75S 26.0, CRJX 26.18, CRJ9 24.85, CRJ7 23.25, CRJ2 21.21, DH8D 28.42,
  AT76/AT75/AT72 27.05, AT46/AT45/AT43 24.57, B463/B462/B461 26.34, F100/F70 28.08, SU95 27.8,
  C919 35.8, A124 73.3, A225 88.4, IL96 60.1, C130/C30J 40.4, A400 42.4, C17 51.75, GL7T 31.7,
  GLF6 30.36, F900 19.33, B350 17.65, C56X 17.17, C25C 16.26, PC12 16.28, C208 15.88, TBM9 12.68,
  C172 11.0). The table is a plain static dictionary in code so a missing type is a one-line
  addition. Unknown type → `WingspanMetres = null`, `CodeLetter = Unknown`; the block says the
  type was not recognised and no wingspan filtering is applied.
- `IsFreighter` is true when: `maxPassengers == 0`, **or** the type code is in the freighter set
  {MD1F, B74F, B76F, B77F, A33F, DC1F}, **or** the name matches
  `(?i)(?:\d|-|\s)(?:F|BCF|BDSF|SF|PF|PCF|ERF|LRF)\b|freighter|cargo` ("Boeing 777F",
  "747-8F", "MD-11F", "737-800BCF", "757-200PF", "A330-200F"). "Boeing 777-200LR" and
  "Airbus A320" are not freighters. `DisplayName` is the SimBrief name, falling back to the code.
- Per the owner's choice, classification is **SimBrief-only**: nothing is read from the loaded
  aircraft definition or from the sim's `WING SPAN` SimVar.

### 5.3 `BriefingStandPicker`

```csharp
public enum StandChoiceSource { SayIntentions, AirlineMatch, Category, Any }
public sealed record StandChoice(ParkingSpot Spot, StandChoiceSource Source, IReadOnlyList<string> Notes);
public sealed record SayIntentionsGateHint(string Label, GeoPoint? Position);
public static class BriefingStandPicker
{
    public const double SiPositionBackstopMetres = 150.0;
    public const double SiNoseStopRadiusFactor = 2.0;   // same factor TaxiAssistForm's SI coordinate step uses
    public static StandChoice? Pick(IReadOnlyList<ParkingSpot> spots, AircraftProfile aircraft,
        string? airlineIcao, SayIntentionsGateHint? siGate, Func<ParkingSpot, bool> hasGraphNode);
}
```

Order of decisions:

0. **SayIntentions gate** (hint non-null; the caller has already checked the SI flight matches
   this OFP, §5.9): exact normalized name match — `SayIntentionsClearanceParser.NormalizeParkingName(hint.Label)`
   against `NormalizeParkingName(spot.Describe())` and against each of `spot.Aliases` — then, if
   nothing matched and a position is published, the nearest spot within
   `min(SiPositionBackstopMetres, radiusMetres × SiNoseStopRadiusFactor)` where radius is
   converted by `Source` (navdata radius is FEET, GSX radius is METRES). A found stand is
   `StandChoiceSource.SayIntentions` (note "matched by position" for the second path). Nothing
   found → fall through to the inference below, with a note that the assigned gate was not found
   at this airport.
1. **Category.** Exclude `Type` 1 (None), 8 (Military Combat), 16 (Fuel), 17 (Vehicles) and
   `IsDeiceArea`. Freighter → keep `Type` 6 or 7 (Ramp Cargo / Ramp Military Cargo); if none
   exist, keep everything else with note "no cargo stands at this airport". Non-freighter → keep
   gate types 9, 10, 11, 13, 14; if none exist, keep ramps 2, 3, 4, 5, 12, 15 with note "no gate
   stands at this airport; using a ramp". Stands of unknown type (`Type` 0, OSM tier) are always
   kept.
2. **Wingspan fit.** When `WingspanMetres` is known: keep spots for which
   `FitsAircraft(wingspanFeet)` is true **or** whose size is unknown (`Radius <= 0` and no
   `MaxWingspanMeters`). If that empties the list, keep the unfiltered list with note "no stand at
   this airport is marked as fitting a N m wingspan".
3. **Airline.** When `airlineIcao` is set and any candidate's `AirlineCodes` (split on `,`, `;`,
   whitespace; ordinal-ignore-case) contains it, restrict to those (`AirlineMatch`).
4. **Reachable.** Keep only `hasGraphNode(spot)` (the planner supplies "nearest graph node within
   100 m", the same rule Taxi Assist uses for a gate destination). Empty → return null with the
   caller noting "no stand at this airport connects to the taxiway network".
5. **Central.** The candidate nearest the arithmetic centroid of the candidates' coordinates;
   ties → smallest `Describe()` ordinal. Source is `AirlineMatch`, `Category` (a category filter
   applied) or `Any`.

### 5.4 `BriefingExitPicker`

```csharp
public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable);
public static class BriefingExitPicker
{
    public const double HighSpeedPreferenceFeet = 1500.0;
    public static ExitChoice? Pick(IReadOnlyList<LandingExit> exitsSortedByThreshold, double touchdownSpeedKts);
}
```

Input is `TaxiGraph.GetLandingExits(rwy)` after `LandingExitVacateScreen.Mark`. Candidates:
`VacatesRunway` and `ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg` (90°); if none,
any `VacatesRunway` exit; if none, null. Reachable = candidates with
`DistanceFromTouchdownFeet >= RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, ExitAngleDegrees)`.
Chosen = the first reachable exit, except that a `High-speed` exit no more than
`HighSpeedPreferenceFeet` further along than that first one wins over a `Normal` one. If nothing
is reachable, the furthest candidate is chosen with `ComfortablyReachable = false` (the block says
the runway is short for this aircraft). `NextExit` = the next candidate beyond the chosen one, or
null.

### 5.5 `RunwayLineupTarget` (in `Navigation/`)

```csharp
public static class RunwayLineupTarget
{
    public sealed record Result(double LineupLat, double LineupLon, TaxiNode? EntryNode);
    public static Result Resolve(TaxiGraph graph, Runway rwy, IReadOnlyList<StartPosition> startsForRunway,
        double? anchorLat, double? anchorLon);
}
```

The body is the code currently inline in `TaxiAssistForm.PopulateDestinations`:
`PickFullLengthStart` (when start rows exist) → `SnapStartToRunwayCenterline`, else the runway
`StartLat/Lon`; half-width `(rwy.Width > 0 ? rwy.Width : 150) × 0.3048 / 2`;
`FindRunwayLineupEntryNode(..., TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M, anchorLat, anchorLon)`.
`TaxiAssistForm` calls it with the same arguments it computes today (anchor = own position when
known, else null). No behaviour change; a characterization test pins the result on a synthetic
graph.

### 5.6 `TaxiBriefingPlanner`

```csharp
public enum BriefingTier { Navdata, OpenStreetMap, None }
public sealed record OwnPosition(double Lat, double Lon, bool OnGround);
public sealed record TaxiBriefingRequest(string OriginIcao, string OriginRunway, string DestinationIcao,
    string DestinationRunway, AircraftProfile Aircraft, string? AirlineIcao, OwnPosition? Own,
    SayIntentionsGateHint? ArrivalGate);
public sealed record HoldShortNote(string Runway, string Taxiway, bool BeforeEntering);
public sealed record NarrowTaxiwayNote(string Taxiway, double WidthMetres, double MinimumMetres);
public sealed record TaxiLegBriefing(string Icao, string Runway, BriefingTier Tier,
    string? Unavailable,                      // non-null: no route; the reason, pilot-readable
    string EndpointDescription,               // taxi-out START / taxi-in STAND: "current position, stand N12 (Ramp Cargo)" / "representative stand E12 (Ramp Cargo, UPS)" / "SayIntentions assigned gate 52A"
    StandChoice? Stand, IReadOnlyList<string> Taxiways, double DistanceMetres,
    IReadOnlyList<HoldShortNote> HoldShorts, ExitChoice? Exit, IReadOnlyList<LandingExit> VacatingExits,
    IReadOnlyList<NarrowTaxiwayNote> NarrowTaxiways, IReadOnlyList<string> Notes);
public sealed record TaxiBriefing(AircraftProfile Aircraft, TaxiLegBriefing TaxiOut, TaxiLegBriefing TaxiIn);
public sealed class TaxiBriefingPlanner
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);
    public Task<TaxiBriefing> PlanAsync(TaxiBriefingRequest request, IAirportDataProvider provider,
        GateDataSource? gateSource, TimeSpan budget, CancellationToken ct);
    // Pure, graph-in helpers (what the tests drive):
    public static TaxiLegBriefing PlanTaxiOut(TaxiBriefingRequest r, GraphBundle g);
    public static TaxiLegBriefing PlanTaxiIn(TaxiBriefingRequest r, GraphBundle g);
}
public sealed record GraphBundle(TaxiGraph Graph, BriefingTier Tier, IReadOnlyList<Runway> Runways,
    IReadOnlyList<StartPosition> Starts, IReadOnlyList<ParkingSpot> Spots, string? Note);
```

`PlanAsync` runs everything (DB reads, graph builds, routing) inside `Task.Run`, one leg after the
other, each leg in its own try/catch: an exception becomes `Unavailable = "taxi route could not be
computed (<exception message>)"` and a `Log.Warn("taxi_briefing", …)` line; a leg that overruns
the budget becomes `Unavailable = "taxi route computation timed out"`. Every leg logs one
`Log.Info("taxi_briefing", …)` summary (tier, start, stand, taxiways, holds, exit).

**Graph per airport** (`TaxiBriefingGraphSource.BuildAsync`): tier 1 when `GetTaxiPaths` has rows
— `TaxiGraph.Build(paths, ParkingSpotSource.GetNamedSpots(provider, gateSource, icao),
GetRunwayStarts(icao), GetRunways(icao))`. Otherwise tier 2 via §5.7 when possible, else tier 3
with `Note` = "the navigation database has no taxiways for this airport" (plus "and OpenStreetMap
data is not available / not yet available / disabled in settings" as applicable).

**Taxi-out** (`PlanTaxiOut`):
1. Runway: `Runways` entry whose `RunwayID` equals the plan's runway (leading-zero and case
   tolerant, as `SayIntentionsClearanceParser.CleanRunway` compares). Missing → `Unavailable =
   "runway NN is not in the navigation database for ICAO"`.
2. Start: when `Own` is non-null, `OnGround`, and within 5 km of the airport reference point
   (`provider.GetAirport(icao)`), the start node is `graph.FindNearestNode(lat, lon,
   excludeBridgeOnlyStandStubs: true)` provided it is within 150 m; `EndpointDescription` names the
   parking spot whose node it is when the node carries a `ParkingName`, else "current position".
   Otherwise the start is the stand from `BriefingStandPicker.Pick` with `siGate = null`
   (SayIntentions never assigns a departure gate) and `EndpointDescription` = "representative stand
   …". No stand → `Unavailable`.
3. Destination node: `RunwayLineupTarget.Resolve(graph, rwy, startsForRunway, startLat, startLon).EntryNode`;
   null → `Unavailable = "no taxiway reaches runway NN in this scenery"`.
4. Route: `new TaxiRouter(graph).FindShortestPath(start, entry)`; null → `Unavailable = "no taxi
   route connects the stand to runway NN in this scenery"`. Then
   `RouteRunwayCrossings.InsertRunwayHoldShorts(route, graph.RunwayCenterlines, $"Runway {id}",
   aircraft: null)`.
5. `Taxiways` = `RouteTaxiwaySequence.DistinctConsecutive(route.Segments)` (unnamed segments
   dropped); `DistanceMetres` = `route.TotalDistanceMeters`; `HoldShorts` = one note per segment
   with `IsHoldShortPoint`: runway = `HoldShortRunway`, taxiway = the segment's `TaxiwayName`
   (or the previous named segment's), `BeforeEntering` = the runway is the departure runway
   (reciprocal-aware via `RouteRunwayCrossings.Reciprocal`). A `RunwayEvent` with `Held == false`
   adds the note "no hold short point could be placed for runway X (cross with care)". If the
   route's start hold (`TaxiRoute.StartHoldRunway`) names a runway, a hold note is emitted for it
   at the first taxiway.
6. `NarrowTaxiways`: for each distinct taxiway on the route, the minimum `WidthFeet` of its edges
   on the route; when `> 0` and below `MinTaxiwayWidthMetres(aircraft.CodeLetter)` (converted),
   one note. Width 0 (unknown, and every OSM edge) never produces a note.

**Taxi-in** (`PlanTaxiIn`):
1. Runway as above.
2. Exits: `graph.GetLandingExits(rwy)`, `LandingExitVacateScreen.Mark(graph, exits, rwy)`,
   `BriefingExitPicker.Pick(exits, aircraft.TouchdownSpeedKts)`. `VacatingExits` = the exits with
   `VacatesRunway`, nearest-first, capped at 12 for the block. No exit → `Unavailable = "no exit
   taxiway is mapped clear of runway NN in this scenery"` (the stand is still picked and named).
3. Start node: `LandingExitDestination.Resolve(graph, exit, exits, rwy, rwy.Heading, out _, out _, out _)`.
4. Stand: `BriefingStandPicker.Pick(spots, aircraft, airline, request.ArrivalGate, hasGraphNode)`
   where `hasGraphNode` = `graph.FindNearestNode(spot.Lat, spot.Lon)` within 100 m; the
   destination node is that node. `EndpointDescription` here describes the stand ("SayIntentions
   assigned gate 52A" / "representative stand E12 (Ramp Cargo, UPS)" / "representative stand
   E12 — assigned gate 'Terminal 1 Gate 6' not found at this airport").
5. Route: shortest path from the vacate node to the stand node, then
   `InsertRunwayHoldShorts(route, centerlines, destinationName: "", aircraft: null)`.
   A start hold naming the runway just landed on (either designator of its pair) is **discarded**
   — the aircraft has just vacated it. Crossings of any other runway are hold notes as in taxi-out.
6. Taxiways, distance, narrow-taxiway notes as in taxi-out.

### 5.7 OpenStreetMap planning graph (tier 2)

- `AugmentingAirportDataProvider` gains
  `Task<IReadOnlyList<AirportTaxiData>?> GetOnlineTaxiDataAsync(string icao, CancellationToken ct)`:
  returns the cached sources when `TaxiDataCache.TryLoad` has them; otherwise, when `Enabled`,
  awaits `PrefetchAsync(icao)` (bounded by `ct`) and re-reads the cache; null when disabled, not
  augmenting, or nothing arrived in time. It is the only new surface on the decorator and it
  hands out the data read-only.
- `OsmPlanningGraph.Build(IReadOnlyList<AirportTaxiData> sources, IReadOnlyList<Runway> runways,
  IReadOnlyList<StartPosition> starts)` → `GraphBundle?` (tier `OpenStreetMap`). It picks the source
  with the most `Taxiways` (ties → the one whose `Source == "osm"`, since only OSM carries holding
  points) and converts:
  - each `NamedTaxiSegment` → `TaxiPath { Type = "T", Name, Width = 0, StartType/EndType = "N" }`;
    an endpoint within `HoldSnapMetres` (3 m) of a `HoldingPoints` entry becomes `"HS"`, or
    `"IHS"` when that point's `Kind` is "ILS" (case-insensitive). `TaxiGraph.Build` merges
    endpoints within 1.5 m, so consecutive OSM vertices connect without ids;
  - each `Parking` entry → `ParkingSpot { Type = 0, Radius = 0, Source = Navdata }` with
    `Name/Number/Suffix` from `StandId.Parse(ref)` (whole ref as `Name` when it does not parse).
    Unknown type/size means §5.3 keeps them all and applies no fit filter; the note says "stand
    types unknown (OpenStreetMap)";
  - runways and starts pass through from the database. With no start rows the graph has no
    centerlines, so hold-short placement and lineup targets cannot run; the leg then reports
    `Unavailable = "the database has no runway start positions for ICAO"`.
  - zero taxiways → null (tier 3).
- Every OSM-tier block line carries "(OpenStreetMap, planning only — taxi guidance cannot use
  this)". The graph object is a local of the planner; nothing stores it.

### 5.8 `TaxiBriefingRenderer`

`public static string Render(TaxiBriefing b)` — plain text, `\n` line ends, no markdown. Format
(exact strings are pinned by tests):

```
TAXI ROUTES (computed by MSFS Blind Assist; each leg names its data source, and taxiway names are that source's own)
Aircraft: <DisplayName> (SimBrief type <code>), size class <letter>, wingspan <n.n> m, <freighter: cargo stands preferred | passenger>
TAXI OUT at <ICAO> (<tier label>): from <EndpointDescription> to runway <NN>
  Taxiways: A, B, C (2.4 km)
  Hold short: runway 27 on taxiway M (crossing); runway 36L on taxiway B (before entering)
  Taxiway width note: taxiway K is 15 m in the navdata, below the 25 m code F minimum
TAXI IN at <ICAO> (<tier label>), landing runway <NN>
  Expected exit: taxiway AA, high-speed, RIGHT side, 6,200 ft from the threshold. Next exit if missed: AB, right side, 7,100 ft
  Stand: <EndpointDescription>
  Taxiways from the exit: AA, E, C (3.1 km)
  Hold short: runway 25R on taxiway AA (crossing)
  Exits on 25L that get clear of the runway: AA (6,200 ft, right, high-speed), AB (7,100 ft, right, normal), … and 3 more
```

- Tier labels: "scenery navdata", "OpenStreetMap, planning only — taxi guidance cannot use this",
  and for tier 3 the leg renders as `TAXI OUT at LOWI: taxi route unavailable — <reason>` (same
  for TAXI IN, keeping the `Stand:` line when a stand was still chosen).
- "Hold short:" is omitted when the route has none ("No runway crossings on this route." instead).
- Unknown wingspan renders "wingspan unknown (aircraft type not recognised)". Unknown size class
  renders "size class unknown".
- Distances: route length in km with one decimal (m under 1 km); exit distances in feet with
  thousands separators; sides upper-case on the expected exit line. `InvariantCulture` everywhere.
- Notes (from the picker and the planner) render one per line as `  Note: …`.
- Hard cap: the exits list shows the 12 nearest and "… and N more".

### 5.9 EFB wiring (`ElectronicFlightBagForm`)

- New constructor parameter `RouteBriefingDependencies? briefing` — a record of three delegates:
  `Func<IAirportDataProvider?> Provider` (a getter, because `RefreshDatabaseProvider` swaps the
  instance), `Func<GateDataSource?> GateSource`, `Func<Task<SayIntentionsFlightContext>> SayIntentions`.
  `MainForm.Dialogs` passes `() => airportDataProvider`, `BuildGateDataSource`,
  `sayIntentionsService.ReadFlightContextAsync`. Null (tests, or no provider) → the planner is
  skipped and the block says "taxi route unavailable — no navigation database loaded".
- `DescribeRouteAsync` after its existing plan check and announcement: status "Computing taxi
  routes…"; own position via `RequestAircraftPositionAsync` wrapped in a `TaskCompletionSource`
  with a 1.5 s timeout (null when not connected or timed out; `OnGround = SimOnGround > 0.5`);
  SI context; then `PlanAsync` with `DefaultBudget`; then the existing AI call with the block
  appended. Any exception in this pre-step is caught, logged and rendered as an unavailable
  block — it never prevents the AI briefing.
- **SayIntentions gate hint** is passed only when all hold: `FlightJsonExists`, `Origin` equals
  the plan's departure ICAO, `Destination` equals the plan's arrival ICAO, and `AssignedGate` is
  non-empty. So: SI not open → no file → inferred stand; SI on another flight, or a stale file
  from another city pair → ignored; a stale file for the same city pair is accepted and labelled
  as SayIntentions' assignment. No freshness timestamp is consulted.

### 5.10 Prompt (`GeminiService.GetRouteDescriptionPrompt`) and Gemini truncation

Section 7 is appended after NOTAMS (both AI providers share this prompt):

```
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block computed by the pilot's own simulator
   scenery. For each of the two legs, do two things:
   a) Describe the computed route in prose: the stand it starts from (say plainly when it is a
      representative stand rather than an assignment), the taxiways in order, every hold-short
      point and which runway it protects, and for the arrival which side to leave the runway
      (left or right), the exit taxiway and its distance from the threshold, and the fallback
      exit. Use ONLY the taxiway, exit and stand names given in the block for this part; if the
      block says a leg is unavailable, say so in one sentence.
   b) Then, under the heading "Real-world practice", answer this from your own knowledge of the
      airport: "Provide the step-by-step taxi route at [ICAO] from [runway] to [terminal/gate]
      in a [aircraft type]. Please include the expected taxiways, hold short points, and any
      specific restrictions." — substituting the airport, runway, stand or terminal and aircraft
      type from the data — for the departure from the stand to the runway, and for the
      arrival from the runway (via the expected exit) to the terminal or gate. Mention wingspan
      or aircraft-type restrictions on taxiways and stands where you know of them. Where your
      route differs from the computed one, say so and say which is which; never present your
      own route as the computed one. When the block says no ground data exists for an airport,
      this real-world route is the answer and should be given in full.
```

Guidelines change: "Aim for 600 to 900 words" (was 300–500); add "Distances and sides in the taxi
section must be repeated exactly as given in the data". Tests pin the presence of the section, the
verbatim template sentence and the word target.

Gemini: `Candidate` gains `finishReason`; when it is `MAX_TOKENS` the returned text gets the
suffix `\n\n(Response may be incomplete — Gemini stopped before finishing.)`, mirroring
`ClaudeService`'s existing note. Composition lives in an `internal static` helper so a test can
pin it.

## 6. Degradation matrix

| Situation | What the pilot reads |
| --- | --- |
| No navigation database loaded | both legs "taxi route unavailable — no navigation database loaded"; AI real-world route is primary |
| Airport not in DB / runway not in DB | that leg unavailable, reason names the ICAO or runway |
| Scenery has no taxiways, OSM available | leg computed, labelled OpenStreetMap planning-only; stand types unknown |
| Scenery has no taxiways, OSM disabled / offline / not yet fetched | leg unavailable with that reason; AI route primary |
| Not parked at origin (airborne, elsewhere, or no SimConnect) | taxi-out from a representative stand, said so |
| SI absent / other flight / stale other city pair | representative stand, "No SayIntentions gate" |
| SI gate not found at the airport | representative stand, note names the missing gate |
| No exit vacates the runway | taxi-in exit unavailable; stand still named |
| No stand fits / no cargo stands / no gates | falls back with a note, never silent |
| Aircraft type unknown | no fit filter, no width notes, said so |
| Planner exception / timeout | leg unavailable, reason given, `debug.log` has the detail; AI briefing still runs |

## 7. Limits (checked)

- Input: the block adds ~1–2 KB (exits capped at 12) to a prompt already a few KB; Gemini 1M /
  Claude 200k token contexts. Output: Claude `max_tokens = 16000` with its existing incomplete
  note; Gemini gets the equivalent note (§5.10). Display: the description `TextBox` is filled
  programmatically, which WinForms never truncates. Time: graph builds 0.2–2 s per airport, OSM
  fetch up to 60 s but only the 20 s budget is waited; the AI HTTP timeout (120 s) is unaffected.

## 8. Testing

xUnit in `tests/MSFSBlindAssist.Tests` (all pure; graph fixtures copied from
`OrphanParkingIslandBridgeTests` into a shared `TaxiBriefingFixture` — `Taxiway`, `LeadIn`,
`BuildGraph`, `Starts09And27`, `Runway09`, `NodeAt`):

- `AircraftSizeClassTests`: A388 → F, 79.75 m, 140 kt, 25 m minimum width; B738 → C; MD1F →
  freighter; "Boeing 777F"/"747-8F"/"MD-11F"/"737-800BCF" → freighter; "Boeing 777-200LR" → not;
  `maxPassengers == 0` → freighter; unknown code → Unknown letter, null wingspan; letter
  boundaries (35.99 → C, 36 → D).
- `BriefingStandPickerTests`: freighter prefers Ramp Cargo; falls back with note when none; airliner
  prefers gates; A380 wingspan drops small stands, keeps unknown-size stands, falls back with
  note when nothing fits; airline restriction; central pick and tie-break; excluded types never
  chosen; SI gate by name (label "Terminal 3 Gate J1" ↔ spot "J 1"), by alias, by position
  (radius unit by `Source`), and not-found note; `hasGraphNode` filter.
- `BriefingExitPickerTests`: heavy at 140 kt skips an exit 3,000 ft from touchdown and takes the
  first comfortable one; high-speed exit within 1,500 ft beyond wins over a normal one; beyond
  1,500 ft does not; non-vacating and >90° exits excluded; nothing reachable → furthest, flagged;
  `NextExit` correct and null at the end.
- `RunwayLineupTargetTests`: characterization on a synthetic runway with start rows (result equals
  `FindRunwayLineupEntryNode` on the snapped start) and without (falls back to `StartLat/Lon`).
- `TaxiBriefingPlannerTests` (`PlanTaxiOut`/`PlanTaxiIn` on a synthetic airport: stand lead-in →
  taxiway U → crossing runway 09/27 with an HSND hold → taxiway to runway 18 entry; landing
  runway 09 with two exits at 2,000 ft and 6,000 ft): taxi-out sequence, crossing hold note,
  before-entering hold note, start from own position when on ground within 5 km, representative
  stand otherwise; taxi-in exit choice by speed, taxi-in route from the vacate node, landing-runway
  start hold discarded, other-runway crossing kept; unavailable reasons for missing runway, no
  entry node, no exit, no stand; narrow-taxiway note when width < minimum and none at width 0.
- `OsmPlanningGraphTests`: segments become connected edges; a holding point within 3 m makes an
  HS node (ILS → IHS); stands parse; no taxiways → null; source with most taxiways chosen.
- `TaxiBriefingRendererTests`: exact block for a two-leg briefing; unavailable lines; OSM label;
  unknown-type aircraft line; exits cap "… and N more"; no-crossings sentence; invariant-culture
  numbers.
- `RouteDescriptionPromptTests`: section 7 present, the template sentence verbatim, "600 to 900
  words"; `GeminiFinishReasonTests`: `MAX_TOKENS` appends the note, `STOP` does not.
- `SimBriefFlightPlanFieldsTests`: minimal OFP XML → the four new `FlightPlan` fields; missing
  `max_passengers` → null.

In-sim test plan for the PR (owner runs): (1) parked at the origin with SayIntentions running and
a gate assigned — both legs computed, taxi-out "from current position", taxi-in to the SI gate;
(2) from the main menu or airborne with SI closed — representative stands at both ends, labelled
as such; (3) an airport whose scenery has no taxiways in the DB — OSM planning-only leg or the
AI-only wording. Confirm Taxi Assist and the Landing Exit dialog behave exactly as before.

## 9. Docs, changelog, invariants

- `docs/gemini.md`: new section "Taxi routes in the route briefing" (tiers, what is computed vs
  AI knowledge, the SI gate rule, the OSM quarantine). `docs/taxi-guidance.md`: one paragraph
  pointing there and stating the OSM planning graph never reaches guidance.
- CLAUDE.md invariants (Gemini AI group), two bullets: the OSM briefing graph is planning-only and
  must never be handed to `TaxiGuidanceManager`/`TaxiAssistForm`; the AI's real-world route and
  the computed route are always both shown and never merged into one.
- Changelog fragment: `changelog.d/<pr>-route-briefing-taxi-routes.feature.md`, written after the
  PR number exists.
- Invariants respected: route starts exclude bridge-only stand stubs; `GetNamedSpots` for every
  graph build (never raw `GetParkingSpots`); navdata radius is feet, GSX metres; SI gate is
  arrival-only; no announcements for UI actions; `Log` for every diagnostic line; no airport
  hardcoding.
