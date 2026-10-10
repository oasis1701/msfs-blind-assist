# Taxi position: off-route, start grace, Where Am I and the runway probe — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/taxi-position.md`, which Claude Code loads when it reads matching code. Background: [taxi-guidance.md](../taxi-guidance.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## RTE-4

- Take-off assist's under-aircraft runway probe — the take-off-assist toggle's fallback when no lineup reference is set (`TaxiGuidanceManager.TryDetectRunwayUnderAircraft`, then `TaxiGraph.TryGetRunwayAtPosition`) — must use a strict tolerance — `RunwayShape`'s own half-width, no +5m fudge — and stay gated on `_lastOnGround`: its centerline math needs the runway actually under the aircraft, and a 5 m fudge could pick the runway beside a high-speed exit the aircraft sits on. Where-Am-I's `DescribeLocation` keeps half-width + 5 m on purpose; the strict test is the take-off probe's, not Where-Am-I's. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: the strict tolerance belongs to take-off assist's under-aircraft probe; this rule had named Where-Am-I, whose runway answer keeps half-width + 5 m. Evidence: `TaxiGraph.TryGetRunwayAtPosition` (`Contains(lat, lon, 0.0)`), called only by `TaxiGuidanceManager.TryDetectRunwayUnderAircraft` from the take-off-assist toggle in `MainForm.Announcers.cs`; `TaxiGraph.DescribeLocation` (`ContainsAlongLateral(along, lateral, 5.0)`).

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).

## RTE-6

- Off-route detection must use perpendicular cross-track distance, never endpoint-distance comparisons (breaks on long segments). → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).

## RTE-7

- Off-route auto-recalc must stay gated on the route-joined latch (`_hasJoinedRoute`) — without it, the post-pushback taxi onto the first taxiway reads as off-route and silently trims the entered clearance before the pilot has joined it. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).

## RTE-11

- "Where Am I" is ground-only by design (gated on `_lastOnGround`); runway detection must use `TaxiGraph.RunwayCenterlines`, never rely on `taxi_path.type='R'` edges (the DB has none): `DescribeLocation` keeps a branch for them, dead on navdatareader databases, and no runway answer may depend on it. → [taxi-guidance.md](../taxi-guidance.md)

Corrected 2026-10-08: "not `taxi_path.type='R'` edges" became "never rely on" them, since `DescribeLocation` still has an R-edge branch. Evidence: `TaxiGraph.DescribeLocationLocked`, whose R-edge branch is commented as firing only for databases that store runway centerlines as `type='R'` rows, which navdatareader's do not.

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).

## RTE-12

- `TaxiGraph.DescribeLocation` is NOT a UI-thread-only query: Alt+L's surroundings lookup runs it on a thread-pool thread through `TaxiGuidanceManager.DescribeCurrentLocation`, which hands it the ACTIVE guidance graph when the airport matches — `TaxiAssistForm`'s own `_graph`, which the form SUBDIVIDES on the UI thread whenever it projects a painted holding point onto a taxi edge (`NamedHoldingPointResolver.SnapOrInsert` → `InsertHoldingPointNodeOnEdge` → `SplitEdgeAt`). Both hold the graph's `_structureLock` — the query for its whole run INCLUDING its lazily built index, the projection for its whole scan-then-split — so never read that index outside the lock, never add a post-Build mutation that skips it, and any `TaxiGraph` query reachable from a pool thread must take `_structureLock` inside TaxiGraph. UI-thread readers (routing, guidance, the taxi form) need no lock only because every post-Build mutation also runs on the UI thread. The index is DROPPED wherever `TaxiGraph`'s own mutators change the structure (`InvalidateCellIndex`, from `AddEdge`, `SplitEdgeAt` and `ResolveNode`'s new-node branch) and rebuilt by the next query — never detected by recounting adjacency lists on every query; writing the public `Nodes`/`Adjacency` directly (hand-built test graphs, the standalone probes) drops nothing, so never do it to a graph `DescribeLocation` will be asked about. Pass 1's three node answers have DIFFERENT reaches ON PURPOSE (owner ruling on the PR #230 review, the stand's narrowed further on 2026-09-23 — GC-5 fix round 1): the fallback (60 m) is TRUE metres in every direction at every latitude, unconditionally — `NodesNear` over the index's node half (every node, not only edge endpoints) on a ring sized separately in latitude and longitude — and takes the nearest node that HAS a taxiway name (taking the nearest node of any kind let a nearer unnamed one silence it); this decision never touches it. The stand (40 m) gets the same true-metre reach, but ONLY AWAY FROM A RUNWAY: near one — on runway pavement (the same `RunwayShape` test the runway answer uses), where a "near a runway start" answer is in reach (below), or where a hold-short node sits within the stand radius (from the navdata endpoint types `Build` records into `TaxiGraph._navdataHoldShortNodeIds`, NEVER `TaxiNode.Type`, which the parking pass can overwrite to Parking for a node within 100 m of a stand — exactly the nodes this predicate most needs to catch) — only a stand ALSO inside today's ring may answer, exactly as before this whole PR; letting the wider reach win unconditionally there put a stand ahead of "Runway X" at about 2,255 hold-short nodes across 2,036 fs2024 airports and on runway pavement itself at >= 1,660 more, breaking the original ruling's own promise that "nothing said at a hold line changes". The "near a runway start" answer (50 m) keeps EXACTLY the old ±30-cell ring of the 1.1 m node hash (±33 m north-south, ±33·cos(latitude) m east-west), because it outranks the taxiway you are on and `Build` names the entry junction or hold line "Runway X". `RunwayStartReach` replicates that ring's key arithmetic bit for bit (a distance test is not the same set) and `TaxiGraphLocationRadiusTests` pins it, and the near-runway stand carve-out, against a verbatim copy. Never widen the runway-start reach without asking the owner again; never gather FALLBACK candidates from a fixed ring of the 1.1 m node hash — it reached ±20 m east-west at 52°N, ±7 m at ENSB; and never let the STAND'S wider reach win near a runway without the same three-predicate gate (pavement, runway-start reach, hold-short radius). → [taxi-guidance.md](../taxi-guidance.md)

Split out on 2026-10-09: RTE-30 (post-Build changes on the UI thread, through TaxiGraph's own mutators). This text keeps it as written; it has its own section in docs/invariants/taxi-routing.md.

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).

## RTE-27

- The start grace window (`START_WARNING_CHATTER_GRACE_SEC`, 12.5 s: a destination warning followed by the route-start turn cue measures 10.4 s at System.Speech Rate 0) opens for either start warning, the unmapped-leg warning or the runway reach warning, and turn, destination-ahead and curve callouts WAIT it out when it is safe to (`StartWarningChatterGate` -- one that would otherwise be lost for good speaks immediately over the warning instead); the taxiway-change callout has no such loss hazard (it just names whichever taxiway the aircraft is currently on), so it always defers while the window is open and, once it closes, either speaks or — if the route has since moved on to a different taxiway — silently drops the stale name (`TaxiwayChangeGate`): never make any of them skip, and never gate hold-short or runway-crossing callouts on it. → [taxi-guidance.md](../taxi-guidance.md)

Moved 2026-10-10 from taxi-routing.md (split for the rule budget).
