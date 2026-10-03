# Taxi routing — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-routing.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## RTE-1

- No airport-specific hardcoding — every taxiway/parking/runway name must flow through from the user's DB unchanged. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-2

- A stand stranded in its own connected component is bridged into the main taxi network at Build time (`BridgeOrphanParkingIslands`) ONLY when the island is made entirely of navdata stand lead-ins (`P` rows), a stand STUB, within 50 m. The bridge is never on or across runway pavement, never onto a hold-short node or a stand, and never onto another stand's lead-in chain (`MarkStandLeadInChains`, 100 m cap). Bridges carry `TaxiGraph.StandBridgePathType`, never `P`. Nothing that decides how an aircraft leaves a runway follows a bridge: the landing-exit corridor, the exit extension and the post-landing vacate walk all skip `TaxiGraph.IsStandBridge` edges. Stand and hold-short identity come from the navdata endpoint types recorded in `Build`, never from `TaxiNode.Type`, which the parking pass stamps by proximity in any component. Never widen this to islands that carry a taxiway: bridging whole networks at their closest pair drew routes along active runways with no hold-short (VIJU), put a crossing hold on the runway (ENAT), flipped a landing exit's side (UKHH) and turned a 272 m hop into a 4.7 km loop (KPRC). → [taxi-guidance.md](../taxi-guidance.md) (Split 2026-10 from one bullet, words unchanged: its route-reachability, start grace window, stand naming and refused-`LoadRoute` sentences are now RTE-26 to RTE-29.)

## RTE-3

- A bridge-only stand stub — every node in an island `BridgeOrphanParkingIslands` successfully bridged, `TaxiGraph.IsBridgeOnlyStandStub` — must never be picked as a route START, only reachable as a destination: bridging merges it into the main component, so the `requiredComponentId` filter alone can no longer tell it apart from a real network node, and a "P" (stand lead-in) row is NOT always unnamed (1,810 of 319,004 real fs2024 rows carry a name), so a stub's node can register on a real taxiway name too, including one a real network node also carries. `FindNearestNode`, `FindNearestNodeInDirection` and `FindNearestNodeOnTaxiway` all take an `excludeBridgeOnlyStandStubs` opt-in (default false, so destination lookups are unaffected) that every route-start caller must pass true; a node `SplitEdgeAt` mints on an edge interior to an already-bridged island inherits the same membership. Any future picker that can feed an A*/Dijkstra start inherits the same obligation — stated once on `TaxiGraph.IsBridgeOnlyStandStub`'s own doc, not re-derived per call site. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-4

- Where-Am-I's runway-detection fallback must use a strict tolerance — `RunwayShape`'s own half-width, no +5m fudge — and stay gated on `_lastOnGround`. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-5

- Never announce runway info (length/surface/ILS) from taxi guidance — out of scope. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-6

- Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons (breaks on long segments). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-7

- Off-route auto-recalc must stay gated on the route-joined latch (`_hasJoinedRoute`) — without it, the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance before the pilot has joined it. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-8

- An accepted recalc must announce the new taxiway sequence by name ("Route changed. Now via X, Y…") — never the old generic "Recalculating… Taxiway X." wording. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-9

- The Progressive Taxi terminator block is self-contained with its OWN runway/taxiway combos — never reuse the per-row "Hold short of runway" combo for the terminator target; that per-row combo must be hidden (and reset) in Progressive Taxi mode so a stale pick can't leak into the route. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-10

- Never restore an "already used taxiway" filter on the Add-Taxiway dropdown — ATC clearances legitimately reuse a taxiway across a runway crossing (KBOS pattern); only the immediately-previous taxiway is conditionally hidden. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-11

- "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, not `taxi_path.type='R'` edges (the DB has none). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-12

- `TaxiGraph.DescribeLocation` is NOT a UI-thread-only query: Alt+L's surroundings lookup runs it on a thread-pool thread through `TaxiGuidanceManager.DescribeCurrentLocation`, which hands it the ACTIVE guidance graph when the airport matches — `TaxiAssistForm`'s own `_graph`, which the form SUBDIVIDES on the UI thread whenever it projects a painted holding point onto a taxi edge (`NamedHoldingPointResolver.SnapOrInsert` → `InsertHoldingPointNodeOnEdge` → `SplitEdgeAt`). Both hold the graph's `_structureLock` — the query for its whole run INCLUDING its lazily built index, the projection for its whole scan-then-split — so never read that index outside the lock, never add a post-Build mutation that skips it, and any `TaxiGraph` query reachable from a pool thread must take `_structureLock` inside TaxiGraph. UI-thread readers (routing, guidance, the taxi form) need no lock only because every post-Build mutation also runs on the UI thread. The index is DROPPED wherever `TaxiGraph`'s own mutators change the structure (`InvalidateCellIndex`, from `AddEdge`, `SplitEdgeAt` and `ResolveNode`'s new-node branch) and rebuilt by the next query — never detected by recounting adjacency lists on every query; writing the public `Nodes`/`Adjacency` directly (hand-built test graphs, the standalone probes) drops nothing, so never do it to a graph `DescribeLocation` will be asked about. Pass 1's three node answers have DIFFERENT reaches ON PURPOSE (owner ruling on the PR #230 review, the stand's narrowed further on 2026-09-23 — GC-5 fix round 1): the fallback (60 m) is TRUE metres in every direction at every latitude, unconditionally — `NodesNear` over the index's node half (every node, not only edge endpoints) on a ring sized separately in latitude and longitude — and takes the nearest node that HAS a taxiway name (taking the nearest node of any kind let a nearer unnamed one silence it); this decision never touches it. The stand (40 m) gets the same true-metre reach, but ONLY AWAY FROM A RUNWAY: near one — on runway pavement (the same `RunwayShape` test the runway answer uses), where a "near a runway start" answer is in reach (below), or where a hold-short node sits within the stand radius (from the navdata endpoint types `Build` records into `TaxiGraph._navdataHoldShortNodeIds`, NEVER `TaxiNode.Type`, which the parking pass can overwrite to Parking for a node within 100 m of a stand — exactly the nodes this predicate most needs to catch) — only a stand ALSO inside today's ring may answer, exactly as before this whole PR; letting the wider reach win unconditionally there put a stand ahead of "Runway X" at about 2,255 hold-short nodes across 2,036 fs2024 airports and on runway pavement itself at >= 1,660 more, breaking the original ruling's own promise that "nothing said at a hold line changes". The "near a runway start" answer (50 m) keeps EXACTLY the old ±30-cell ring of the 1.1 m node hash (±33 m north-south, ±33·cos(latitude) m east-west), because it outranks the taxiway you are on and `Build` names the entry junction or hold line "Runway X". `RunwayStartReach` replicates that ring's key arithmetic bit for bit (a distance test is not the same set) and `TaxiGraphLocationRadiusTests` pins it, and the near-runway stand carve-out, against a verbatim copy. Never widen the runway-start reach without asking the owner again; never gather FALLBACK candidates from a fixed ring of the 1.1 m node hash — it reached ±20 m east-west at 52°N, ±7 m at ENSB; and never let the STAND'S wider reach win near a runway without the same three-predicate gate (pavement, runway-start reach, hold-short radius). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-13

- `LoadRoute`/`TryRecalculateRoute` must filter start-node candidates to the destination's `ComponentId` — without the connected-component filter, A* can't reach an isolated taxiway island (GCLP S5) and route calc silently fails. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-14

- Node ID 0 is a permanent "not set" sentinel in `TaxiGraph` — never reuse it as a real node id. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-15

- Taxiway exit/intersection picking must use GRAPH distance (Dijkstra from the destination), never Euclidean — Euclidean silently fails on a graph dead-end (KDEN case). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-16

- `FindBestIntersection` has no Euclidean fallback by design — returning -1 (unreachable) on a malformed graph is intentional; don't add one back (the old Euclidean path produced silently wrong routes). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-17

- The last cleared taxiway must be honored as the route terminus when it branches off the destination — never let the final unconstrained leg silently drop the cleared last taxiway (EIDW N2, LFPG R1 cases). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-18

- The post-recalc sanity gate needs BOTH the length-blowup indicator AND the backwards-bearing indicator, OR'd (not AND'd) — either alone misses real dead-end-backtrack recalcs. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-19

- The initial-load sanity advisory must compare against the PRE-truncation `fullRouteMeters`, not the truncated total — truncation can hide a genuine backtrack detour (EHAM 18L case). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-20

- `TryRecalculateRoute` must fall back to shortest path (never the full original clearance sequence) when the aircraft isn't near any sequence taxiway — reapplying the full sequence routes the pilot backwards through the whole clearance. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-21

- `TaxiAssistForm.OnCalculateClicked` must refresh the aircraft position from `LastKnownPosition` immediately before building the route, or the route starts from a stale pre-pushback position and off-routes on frame one. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-22

- No-op recalc suppression: a recalculated route identical to the current remaining sequence must skip the "Route changed" callout, latch resets, and tone re-slew entirely — otherwise a sharp corner-cut trips a spurious "Route changed" on an unchanged route (LFPG case). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-23

- Do NOT remove the first-taxiway pre-snap — it's the LEPA anchoring fix; the pavement lead-in only replaces it when the first cleared taxiway is far (>75 m from the aircraft). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-24

- The `FindRunwayBridge` 200m cap must not be raised — it prevents a silent half-airport jump when an ATC clearance is genuinely wrong (those must fall back to shortest path with a log line). → [taxi-guidance.md](../taxi-guidance.md)

## RTE-25

- One taxiway has ONE spelling: `TaxiGraph.BuildCanonicalTaxiwayNames` folds every case variant onto a single spelling as names enter `Build` — PROVENANCE first (a spelling with a navdata row behind it beats one with none, however many online rows carry the latter: `TaxiPath.NameFromOnlineSource`, set at the one place `AugmentingAirportDataProvider.ApplyMergedNames` adopts an online name), then THE SPELLING THE DATA MOSTLY USES, then ordinally smallest. Without the provenance step a taxiway navdata names on two segments and OSM/apt.dat fills on three loses 3-2 and the whole taxiway takes the online spelling — undoing "navdata is AUTHORITATIVE" one layer above the merger that enforces it. Ordinal-smallest ALONE was tried and is wrong — uppercase sorts first at the first differing letter, so ONE stray `LINK 5` row renamed every `Link 5` segment at the airport, and `TaxiwayName` is SPOKEN VERBATIM, so a screen reader then reads it letter by letter; `docs/taxi-guidance.md` states the rule that breaks ("The stored name is always the original human-readable form from the authoritative source"). A row `Build` DISCARDS gets no vote (zero-length rows are skipped only AFTER the fold runs, so one could otherwise decide the spelling while contributing no node and no edge) but still MAPS, so whatever `Build` does with it lands on the same spelling. This does NOT only arise where an airport's own data is inconsistently cased: the list reaching `Build` is the AUGMENTED one, so an un-normalised OSM/apt.dat name competes here with a navdata one — the vote is what keeps navdata winning (online rows are the minority), NOT a guarantee, because nothing here knows a row's provenance. Every consumer that COMPARES `TaxiwayName` does so `OrdinalIgnoreCase`, which is what makes folding safe — that is not a class invariant, this file's own edge dedups and `SayIntentionsTaxiPathSnapper` compare ordinally, and the fold is what makes THOSE safe. → [sayintentions.md](../sayintentions.md)

## RTE-26

- `RouteReachability` in `LoadRoute`/`TryRecalculateRoute` classifies where the aircraft is before a route is adopted. When the aircraft is not on the destination's piece of network, the route still starts on the destination's piece and its straight unmapped first leg is checked: refused, naming the runway, when that leg touches a runway (LFBP Parking 40 from N3), otherwise spoken as a warning ("X isn't connected to the taxiway network you're on. The first N metres of the route aren't mapped." for a destination off the network, "Your position isn't connected…" when leaving an unconnected position). A destination off the network with no start node in range, or with no buildable route, is refused by name, never with the old "Could not find a nearby taxiway node." An aircraft on runway pavement, or on no taxi edge, classifies `Unchanged`; that is what keeps landing roll-outs and the GCLP S5 case exactly as before, so never classify from the nearest node alone. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-27

- The start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s: a destination warning followed by the route-start turn cue measures 10.4 s at System.Speech Rate 0) opens for either start warning, the unmapped-leg warning or the runway reach warning, and turn, destination-ahead and curve callouts WAIT it out when it is safe to (`StartWarningChatterGate` -- one that would otherwise be lost for good speaks immediately over the warning instead); the taxiway-change callout has no such loss hazard (it just names whichever taxiway the aircraft is currently on), so it always defers while the window is open and, once it closes, either speaks or — if the route has since moved on to a different taxiway — silently drops the stale name (`TaxiwayChangeGate`): never make any of them skip, and never gate hold-short or runway-crossing callouts on it. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-28

- Reachability sentences name a stand by its identifier only (the form's label up to its first spaced dash, `RouteReachabilityMessages.SpokenDestinationName`), never the whole label. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-29

- A refused `LoadRoute` puts back the destination, lineup and graph state it had already overwritten, so a refusal mid-taxi leaves the route being flown untouched; the older failure returns do not. → [taxi-guidance.md](../taxi-guidance.md)
