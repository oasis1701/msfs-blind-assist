# Taxi routing — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-routing.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## RTE-1

- No airport-specific hardcoding — every taxiway/parking/runway name must flow through from the user's DB unchanged. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-3

- A bridge-only stand stub — every node in an island `BridgeOrphanParkingIslands` successfully bridged, `TaxiGraph.IsBridgeOnlyStandStub` — must never be picked as a route START, only reachable as a destination: bridging merges it into the main component, so the `requiredComponentId` filter alone can no longer tell it apart from a real network node, and a "P" (stand lead-in) row is NOT always unnamed (1,810 of 319,004 real fs2024 rows carry a name), so a stub's node can register on a real taxiway name too, including one a real network node also carries. `FindNearestNode`, `FindNearestNodeInDirection` and `FindNearestNodeOnTaxiway` all take an `excludeBridgeOnlyStandStubs` opt-in (default false, so destination lookups are unaffected) that every route-start caller must pass true; a node `SplitEdgeAt` mints on an edge interior to an already-bridged island inherits the same membership. Any future picker that can feed an A*/Dijkstra start inherits the same obligation — stated once on `TaxiGraph.IsBridgeOnlyStandStub`'s own doc, not re-derived per call site. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-5

- Never announce runway info (length/surface/ILS) from taxi guidance — out of scope. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-8

- An accepted recalc must announce the new taxiway sequence by name ("Route changed. Now via X, Y…") — never the old generic "Recalculating… Taxiway X." wording. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-9

- The Progressive Taxi terminator block is self-contained with its OWN runway/taxiway combos — never reuse the per-row "Hold short of runway" combo for the terminator target; that per-row combo must be hidden (and reset) in Progressive Taxi mode so a stale pick can't leak into the route. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-10

- Never restore an "already used taxiway" filter on the Add-Taxiway dropdown — ATC clearances legitimately reuse a taxiway across a runway crossing (KBOS pattern); only the immediately-previous taxiway is conditionally hidden. → [taxi-guidance.md](../taxi-guidance.md)

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

- `FindRunwayBridge`'s cap (`MAX_BRIDGE_METERS`, 400 m) must not be dropped, and a bridge must never again be scored by its gap alone: candidates are scored by total route cost (the cost to the exit on the current taxiway, plus the gap, plus the cost from the entry on the next taxiway to the destination), and the cap still prevents a silent half-airport jump when an ATC clearance is genuinely wrong (those must fall back to shortest path with a log line). The cap was 200 m until #76 raised it with that scoring: at KDEN BN→G the right handoff is about 330 m from G, while the nearest BN node (73 m) needed a 600 m loop to reach. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: the rule said the 200 m cap must not be raised; #76 raised it to 400 m and added total-cost scoring, and the doc comment above `FindRunwayBridge` still said 200 m until this date. Evidence: `TaxiRouter.FindRunwayBridge` (`MAX_BRIDGE_METERS = 400.0`), commit `5aff86be`.

## RTE-25

- One taxiway has ONE spelling: `TaxiGraph.BuildCanonicalTaxiwayNames` folds every case variant onto a single spelling as names enter `Build` — PROVENANCE first (a spelling with a navdata row behind it beats one with none, however many online rows carry the latter: `TaxiPath.NameFromOnlineSource`, set at the one place `AugmentingAirportDataProvider.ApplyMergedNames` adopts an online name), then THE SPELLING THE DATA MOSTLY USES, then ordinally smallest. Without the provenance step a taxiway navdata names on two segments and OSM/apt.dat fills on three loses 3-2 and the whole taxiway takes the online spelling — undoing "navdata is AUTHORITATIVE" one layer above the merger that enforces it. Ordinal-smallest ALONE was tried and is wrong — uppercase sorts first at the first differing letter, so ONE stray `LINK 5` row renamed every `Link 5` segment at the airport, and `TaxiwayName` is SPOKEN VERBATIM, so a screen reader then reads it letter by letter; `docs/taxi-guidance.md` states the rule that breaks ("The stored name is always the original human-readable form from the authoritative source"). A row `Build` DISCARDS gets no vote (zero-length rows are skipped only AFTER the fold runs, so one could otherwise decide the spelling while contributing no node and no edge) but still MAPS, so whatever `Build` does with it lands on the same spelling. This does NOT only arise where an airport's own data is inconsistently cased: the list reaching `Build` is the AUGMENTED one, so an un-normalised OSM/apt.dat name competes here with a navdata one — the provenance step is what keeps navdata winning, whatever the count. Every consumer that COMPARES `TaxiwayName` does so `OrdinalIgnoreCase`, which is what makes folding safe — that is not a class invariant, this file's own edge dedups and `SayIntentionsTaxiPathSnapper` compare ordinally, and the fold is what makes THOSE safe. → [sayintentions.md](../sayintentions.md)

Corrected 2026-10-08: removed the clause that credited the vote with keeping navdata winning and said this code could not tell a row's provenance, written before the provenance step existed. Evidence: `TaxiGraph.BuildCanonicalTaxiwayNames`, which weighs `TaxiPath.NameFromOnlineSource` before the vote.

## RTE-26

- `RouteReachability` in `LoadRoute`/`TryRecalculateRoute` classifies where the aircraft is before a route is adopted. When the aircraft is not on the destination's piece of network, the route still starts on the destination's piece and its straight unmapped first leg is checked: refused, naming the runway, when that leg touches a runway (LFBP Parking 40 from N3), otherwise spoken as a warning ("X isn't connected to the taxiway network you're on. The first N metres of the route aren't mapped." for a destination off the network, "Your position isn't connected…" when leaving an unconnected position). A destination off the network with no start node in range, or with no buildable route, is refused by name, never with the old "Could not find a nearby taxiway node." An aircraft on runway pavement, or on no taxi edge, classifies `Unchanged`; that is what keeps landing roll-outs and the GCLP S5 case exactly as before, so never classify from the nearest node alone. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-28

- Reachability sentences name a stand by its identifier only (the form's label up to its first spaced dash, `RouteReachabilityMessages.SpokenDestinationName`), never the whole label. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-29

- A refused `LoadRoute` puts back the destination, lineup and graph state it had already overwritten, so a refusal mid-taxi leaves the route being flown untouched; the older failure returns do not. → [taxi-guidance.md](../taxi-guidance.md)

## RTE-30

- `TaxiGraph` structure changes after `Build` run on the UI thread only, and only through TaxiGraph's own mutators. UI-thread readers (routing, guidance, the taxi form) take no `_structureLock` precisely because every post-Build mutation also runs on the UI thread; today that is the painted holding-point projection (`TaxiAssistForm` → `NamedHoldingPointResolver.SnapOrInsert` → `InsertHoldingPointNodeOnEdge` → `SplitEdgeAt`), so moving a mutation off the UI thread races routing even when it takes the lock. The mutators drop the lazy cell index (`InvalidateCellIndex`, from `AddEdge`, `SplitEdgeAt` and `ResolveNode`'s new-node branch) and the next query rebuilds it. Never detect a stale index by recounting adjacency lists on every query, and never write the public `Nodes`/`Adjacency` directly (as hand-built test graphs and the standalone probes do) on a graph `DescribeLocation` will be asked about: that drops nothing. `TaxiGraphCellIndexTests.A_subdivision_drops_the_index_and_the_next_query_rebuilds_it_once` covers today's split only, and a per-query recount would still pass `The_index_is_built_on_the_first_query_and_not_again_without_a_change`. → [taxi-guidance.md](../taxi-guidance.md)

Split from RTE-12 on 2026-10-09: one mechanism per ID.
