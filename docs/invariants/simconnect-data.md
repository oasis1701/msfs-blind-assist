# SimConnect data definitions and requests — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/simconnect-data.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## SIM-2

- `SetupDataDefinitions` must register bulk/batch vars LAST, after the fixed/critical defs (AIRCRAFT_INFO/ATC/position) — so a def-count overflow degrades gracefully instead of stranding aircraft detection. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-3

- Watch `registration.log`'s `approxTotalDefs` — never let a new var addition push a connection's total definitions near/over 1000 without splitting to a second SimConnect connection. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-4

- `RequestVariable(key, forceUpdate:true)` must also work for batch-covered (no-individual-def) vars — `ProcessContinuousBatch` must consult `forceUpdateVariables` too, or a forced re-read of an unchanged batch value silently no-ops. → [architecture.md](../architecture.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-5

- `SimConnectManager.RequestVariable` may be called from ANY thread and must stay safe there: the MD-11's walks and read-backs reach `ReadFreshAsync` from thread-pool threads (`ConfigureAwait(false)` chains). The manager captures the UI thread's WinForms `SynchronizationContext` and thread id at construction (`UiThreadGate`; any other kind of context leaves the gate inert, because a context that runs its posts on a pool thread would re-post forever), and `RequestVariable` called off that thread POSTS itself there and re-checks the connection when it runs — a post refused at shutdown is dropped, never run inline. `FreshReadWaiters.WaitAsync` registers the waiter BEFORE invoking the issue callback, so a posted request loses nothing, and `continuousVariableIndexMap` is a `ConcurrentDictionary`. Never read that map or issue `RequestDataOnSimObject` from a pool thread directly. The CEVENT pump's calculator writes still run on the pump thread — `SendMFCommand` is a pure send with no shared state — a recorded residual, not a licence. → [md11.md](../md11.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-6

- Request ids 505–508 are the hand-numbered guidance frames (`(DATA_REQUESTS)505..508`: visual guidance, takeoff assist, taxi-guidance position, flare assist — `SimConnectManager.Monitoring.cs`/`Dispatch.cs`) and 600–607 are the ground-traffic sweeps' rotating range (`REQUEST_GROUND_TRAFFIC`, `GroundTrafficRequestIdCount`) — never assign an id in either range to anything else, and grep for raw `(DATA_REQUESTS)` casts before choosing a new one: SimConnect treats a request issued under an existing id as a REPLACEMENT, so a sweep planned at 501–508 would have cancelled taxi guidance's own position stream (the enum alone does not show those four; an earlier review missed them exactly that way). Pinned by `GroundTrafficRequestIdTests`, which also scans every `*.cs` under `MSFSBlindAssist/` for a raw `(DATA_REQUESTS)NNN` cast, so a future hand-numbered id is caught too. → [architecture.md](../architecture.md)

Added 2026-10-09: 341–348 (camera reads: `REQUEST_CAMERA_VIEW` + `CameraReadIdCount`, each display read on its own id, AI-8), below `INDIVIDUAL_VARIABLE_BASE` and pinned by `CameraReadWaitersTests.TheManagersCameraRange_IsBelowTheIndividualBase_AndClearOfEveryOtherRequestId`. The range was stated only in AI-3's full text, whose rule file loads on no `SimConnectManager` partial but the camera one.

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-7

- A delivered value is a CHANGE (fires `SimVarUpdated`) only when it moves by more than its definition's `SimVarDefinition.ChangeTolerance`, or the shared `SimConnectManager.ChangeTolerance` (0.001) when that is null — ONE rule, `SimConnectManager.IsValueChange`, for both delivery paths. Widen it only for a var whose readers need a coarse line and whose ripple costs work: the MD-11's `MD11_DC_BUS_VOLTAGE` is 0.5 V (its power gate needs only the 20 V line, and each ripple re-composed every stateful row of the open panel once a second). The cache still takes every delivery, so a drift slower than the tolerance per sample never fires — never widen a var whose reader acts on a small cumulative change, and never one `Md11SeedGate` counts (it compares at the shared constant). → [md11.md](../md11.md)

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).

## SIM-19

- A SIM_FRAME + CHANGED own subscription delivers only on a change, so it is SEEDED by a ONCE request on `FreshReadPolicy.SeedRequestId` (the definition id + 900000, with no request-map entry), and only once `OnRecvSimobjectData` is attached: `SeedSimFrameSubscriptions` runs after `SetupEvents`, and again after a re-registration, because `SetupDataDefinitions`' `DoEvents` drains any answer that arrives before the handler exists. `RequestVariable` also seeds on an empty or forced cache. A seed's answer fills the cache and completes NO fresh-read waiter. The ordering is untestable, and it is SimConnect-wide rather than MD-11-only, which is why this rule lives here. → [architecture.md](../architecture.md)

Split from MD11-8 on 2026-10-09: one mechanism per ID.

Moved 2026-10-10 from core-simconnect.md (split for the rule budget).
