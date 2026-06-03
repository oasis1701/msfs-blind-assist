# SimConnect → MobiFlight Migration Study (Universal Transport Architecture)

**Status:** Research + design. No code migrated yet. **Date:** 2026-06-03.
**Scope:** Every aircraft MSFSBA supports today (FlyByWire A380X, FlyByWire A32NX, Fenix
A320, HorizonSim 787, PMDG 777, PMDG 737 NG3) **and any future aircraft**. The design is
deliberately aircraft-agnostic.

This document answers the question: *"Can we ditch SimConnect and do everything through
MobiFlight, given the 1000-data-definition cap?"* — and, since the answer is "no, not
entirely," it specifies exactly **what moves, what stays, and how to build the universal
transport layer** that demotes SimConnect to a thin mandatory tail.

---

## 0. TL;DR (the executive answer)

- **The 1000 cap is real and cannot be raised.** It's an MSFS SDK hard limit
  (`SimConnect_AddToDataDefinition`: *"The maximum number of entries in a data definition
  is 1000,"* plus ~1000 requests/objects per connection). The A380 already sits at ~1207
  distinct variables and only fits because batch-covered continuous vars skip their
  individual data-def (605 individual + 602 batch).
- **You cannot fully ditch SimConnect.** A specific, small set of capabilities is reachable
  *only* through the SimConnect protocol and has no gauge/RPN (MobiFlight) equivalent:
  **PMDG's client-data areas (CDU screens + data struct), airport/taxiway facility data
  (Taxi Assist), AI traffic enumeration, aircraft teleport, string SimVars (TITLE / ATC /
  NAV IDENT / ECAM lines), system events, and B: InputEvents.**
- **Everything else can move to a WASM gateway** (MobiFlight today, or WASimCommander):
  all L-vars, all numeric A-vars, K-events, H-events, and arbitrary RPN. The FBW fleet's
  **writes already go through MobiFlight**; the reads are the remaining work.
- **The "64-variable cap" is a myth for numbers.** 64 is the *string* ceiling per channel.
  MobiFlight gives ~1024 **float** vars per channel (and you can add more channels/clients);
  WASimCommander has no fixed ceiling (client-side allocation). Our own
  `MobiFlightWasmModule.cs` currently self-limits to `MAX_LVARS_COUNT = 64` — that's our
  code, not the module's limit.
- **The real MobiFlight cost is CPU, not slots.** It runs `execute_calculator_code` **per
  variable, per frame** (round-robin, `MAX_VARS_PER_FRAME` default 30). For large mostly-
  static SimVar sets, SimConnect's batched, change-flagged struct delivery is far cheaper
  and won't stutter. MobiFlight wins where you *need* L-vars/expressions SimConnect can't read.
- **Recommended end state:** a `SimDataRouter` facade over two backends — a **WASM gateway**
  (L/A/calc/events) and a **thin SimConnect client** (facilities, AI, teleport, strings,
  PMDG CDA, system events) — routed by a declarative capability policy. Migrate via a
  five-phase strangler-fig, L-vars/H-vars/calc first.

---

## 1. The 1000 cap — what it is and why it can't be raised

`SimConnect_AddToDataDefinition` documents a hard limit of **1000 entries per data
definition**, and a SimConnect *client* hits `SIMCONNECT_EXCEPTION_TOO_MANY_OBJECTS`
(case 11 in `SimConnectManager.OnRecvException`) and a parallel ~1000 cap on simultaneous
requests. It is **per-connection** and **resets on aircraft switch**; identical on FS2020
and FS2024. It is **not** MobiFlight's 64-string cap and **not** an array size in our code.

MSFSBA already engineers around it (`SimConnectManager.cs:772-805`):
- `const int IndividualDefCap = 900;` headroom guard.
- Batch-covered continuous vars (`Continuous && IsAnnounced && !ExcludeFromBatch`) are read
  from 5 × `CONTINUOUS_BATCH_n` structs (≤300 datums each) and **skip their individual
  def** — roughly halving the A380 footprint (~1083 → ~530, now ~605 individual + 602 batch
  per `registration.log`).

**Conclusion:** the cap is a fixed SDK constraint. The only ways past it are (a) shrink the
footprint, or (b) move bulk reads off SimConnect data-defs onto a WASM gateway (which doesn't
use data-defs at all). This document is mostly about (b), done safely and universally.

---

## 2. Can we ditch SimConnect entirely? — No. The irreducible set.

A capability is **SimConnect-only** if it needs a dedicated SimConnect *message type* that the
in-sim gauge/RPN API (what MobiFlight exposes) has no token for. The litmus test: *if it's a
variable read/write or a Key/H event, MobiFlight can do it; if it's facilities, object
enumeration, client-data, a system-event subscription, weather, init-position, or string
SimVars, it's SimConnect-only.*

| # | Capability | Verdict | Why / mechanism |
|---|---|---|---|
| 1 | Numeric A-vars (position, wind, NAV freq, speeds, AGL) | **MobiFlight-capable** | `(A:NAME,unit)` via RPN |
| 2 | L-vars / H-vars / K-events / calc | **MobiFlight-capable** | the whole point of the gauge API |
| 3 | **String SimVars** (TITLE, ATC TYPE/ID, NAV IDENT, ECAM lines) | **SimConnect-only** | RPN cannot reliably read/set string SimVars |
| 4 | **Aircraft teleport / position-set** | **SimConnect-only** | `SetDataOnSimObject` + `INITPOSITION`; RPN writes to `PLANE LATITUDE` don't move the plane |
| 5 | **Facilities: airports/runways/taxiways/parking** | **SimConnect-only** | `RequestFacilityData` family. *fs9gps RPN gives airports/navaids but **never taxiways**, and is dead in MSFS 2024.* **Load-bearing for Taxi Assist.** |
| 6 | **AI traffic / SimObject enumeration** | **SimConnect-only** | `RequestDataOnSimObjectType` |
| 7 | **Another addon's client-data area** (PMDG struct + CDU screens) | **SimConnect-only** | `MapClientDataNameToID` + `RequestClientData`; no RPN path |
| 8 | **System events** (AircraftLoaded, FlightLoaded, paused, crashed, frame) | **SimConnect-only** | `SubscribeToSystemEvent` |
| 9 | **B: InputEvents** (HS787 AT-arm / bleed / fuel-xfeed) | **SimConnect-only** | `SetInputEvent` / `EnumerateInputEvents`; no RPN setter |
| 10 | On-screen text, METAR-station read | **SimConnect-only** | no RPN equivalent |
| 11 | ATC menu | MobiFlight-capable | `(>K:ATC_MENU_n)` |

**The minimal irreducible SimConnect core for MSFSBA specifically:**
1. **PMDG client-data** (470 of 494 PMDG vars: 443 on the 777, 27 on the 737, + control CDAs).
2. **Facility/taxiway data** for Taxi Assist.
3. **Teleport** (gate/runway).
4. **String structs** — aircraft detection (`TITLE`/`ATC TYPE`), `ATC_ID_INFO`, `NAV IDENT/NAME`, 14× ECAM lines.
5. **AI traffic** enumeration.
6. **System events** (aircraft/flight loaded, pause).
7. **HS787 B: InputEvents** (3).

Everything else is movable.

---

## 3. Per-aircraft transport reality (universal coverage)

Total SimConnect-read variable counts (from the live `registration.log` and a static census):

| Aircraft | Read transport today | Continuous (per-frame) | On-request | **Total** | MobiFlight-movable reads? |
|---|---|---|---|---|---|
| Shared base (all) | SimConnect | 6 | 9 | **15** | all A-vars → yes |
| **FBW A380** | SimConnect | ~602 | ~605 | **~1207** | ~1150 L-vars + ~40 A-vars → yes |
| **Fenix A320** | SimConnect | 446 | 431 | **877** | 872 L-vars + 4 A-vars → yes |
| **FBW A32NX** | SimConnect | ~bulk | ~rest | **≥509** | 356 L-vars + 55 A-vars → yes; 18 H-vars already MobiFlight |
| **HorizonSim 787** | SimConnect | — | — | **211** | 122 L + 87 A → yes; **3 B: InputEvents stay SimConnect** |
| **PMDG 777** | **PMDG CDA** | — | — | **454** | **443 PMDGVar stay SimConnect**; 9 misc movable |
| **PMDG 737** | **PMDG CDA** | — | — | **40** | **27 PMDGVar stay SimConnect**; 11 misc movable |

Data-exposure mechanism per aircraft (decides the per-aircraft strategy):

| Aircraft | Primary mechanism | Displays (CDU/MFD/EFB) | Forces SimConnect? |
|---|---|---|---|
| PMDG 737/777 | **Private SimConnect CDA** + 3rd-party event range `0x11000+` | CDU = 24×14 cell grid **in the CDA** | **YES** (only family that does) |
| Fenix A320 | L-vars + H/B-events | Web MCDU/EFB at `localhost:8083` | No (telemetry); display is web-scraped |
| FBW A32NX/A380X | L-vars + H-events + RPN + A-vars | React/Coherent web views (already scraped) | No |
| HorizonSim 787 | L-vars + H-events (WT-derived) | Coherent/JS instruments | No (except 3 B: events) |
| Default / Working Title | A-vars + L-vars (WT framework) | Coherent/JS | No |

**Universal rule:** *PMDG is the one family whose useful state and all CDU text live in a
private SimConnect client-data area MobiFlight can't read. Every other aircraft — and the
expected shape of future add-ons (L-var + H-event + web-view displays) — is fully WASM-capable.*

**Write side:** the core router `SimConnectManager.SendEvent` already routes `H:` events and
dotted custom events (`A32NX.FCU_*`) through `ExecuteCalculatorCode` → MobiFlight. The A380 is
essentially **already migrated on writes** (0 `SetLVar`, ~45 calc writes). **Fenix is the
biggest write-side lift** (243 native `SetLVar`). PMDG writes stay on the CDA control area.

---

## 4. The WASM gateway: MobiFlight vs WASimCommander vs alternatives

### 4.1 The "64" correction and real ceilings

| Gateway | Float vars | String vars | Streaming | Var ceiling | .NET client | License | Notes |
|---|---|---|---|---|---|---|---|
| **MobiFlight WASM** | ~1024/channel (4096 B ÷ 4) | 64/channel (8192 B ÷ 128) | per-frame `execute_calculator_code`, round-robin `MAX_VARS_PER_FRAME` (default 30), change-only push (`ON_SET`) | ~1024 floats/client; add clients (~500 max) | community libs (we already have one) | **MIT** | already integrated; ships in MobiFlight Connector → Community folder |
| **WASimCommander** | no fixed cap (client-side alloc) | yes | per-sub push ≥25 ms, `deltaEpsilon` change filter, **bytecode-compiled** calc | **no documented max** | ✅ `WASimClient.dll` | **GPLv3/LGPLv3** (LGPL arm → link from closed app) | strictly lower overhead than MobiFlight at scale |
| FSUIPC7 + WAPI | 3066 L-vars + offsets (AI/weather!) | — | offset poll + callbacks | 3066 | offset DLLs (WAPI is C++) | **paid app** | only one with AI/facility via offsets, but per-user paid license → disqualified as base for a free a11y tool |
| SPAD.neXt / Air Manager | in-app only | — | — | — | not embeddable | paid/closed | dead ends |
| Bespoke WASM | your design | your design | your design | your design | you write it | yours | WASimCommander's design re-implemented + perpetual SDK maintenance — only if WASim ever falls short |

### 4.2 Recommendation

- **Keep MobiFlight as the default WASM backend.** It's already integrated, MIT-licensed,
  many users already have it, and ~1024 floats/channel is enough for one aircraft if we lift
  our self-imposed `MAX_LVARS_COUNT = 64` and add a second channel for headroom.
- **Add WASimCommander as the scalable upgrade path behind the same interface** (§5). Its
  client-side allocation, no-ceiling subscriptions, bytecode-compiled calc, and first-class
  LGPL `.dll` make it the better engine for data-hungry aircraft (A380 ~602 continuous,
  Fenix 446). Switching engines must be a one-line policy change, not a rewrite.
- **Keep a thin raw-SimConnect client permanently** for the §2 irreducible set.
- **Never** make FSUIPC/SPAD/Air Manager the base (paid/closed); optionally *detect* FSUIPC
  as a bonus backend for users who own it.

---

## 5. Universal transport architecture

### 5.1 `ISimDataTransport` + `SimDataRouter`

A narrow five-verb interface with a **capability bitset** so the router asks "can you?"
rather than hard-coding backend knowledge.

```csharp
public enum VarKind { SimVarA, LVar, HVar, KEvent, BEvent, Calc, Facility, Position, AiObject, CduScreen, StringVar }

[Flags]
public enum TransportCapability
{
    None = 0,
    ReadSimVar = 1<<0, WriteSimVar = 1<<1, ReadLVar = 1<<2, WriteLVar = 1<<3,
    FireHEvent = 1<<4, FireKEvent = 1<<5, ExecCalc = 1<<6,
    Facilities = 1<<7, PositionSet = 1<<8, Subscribe = 1<<9,
    ReadString = 1<<10, Enumerate = 1<<11, SystemEvents = 1<<12, ClientDataArea = 1<<13,
}

public readonly record struct VarRef(VarKind Kind, string Name, string? Unit = null, uint Index = 0)
{
    public static VarRef A(string n, string u = "number") => new(VarKind.SimVarA, n, u);
    public static VarRef L(string n) => new(VarKind.LVar, n);
    public static VarRef H(string n) => new(VarKind.HVar, n);
    public static VarRef Calc(string rpn) => new(VarKind.Calc, rpn);
}

public interface ISimDataTransport : IAsyncDisposable
{
    string Name { get; }                       // "SimConnect" | "MobiFlight" | "WASimCommander"
    TransportCapability Capabilities { get; }  // probed at runtime
    bool IsConnected { get; }

    Task<double>  ReadVar(VarRef v, CancellationToken ct);
    Task          WriteVar(VarRef v, double value, CancellationToken ct);
    Task<string?> ReadString(VarRef v, CancellationToken ct);
    IDisposable   SubscribeVar(VarRef v, SubscriptionOptions opt, Action<VarSample> onChanged);
    Task          FireEvent(VarRef ev, params uint[] data);
    Task<double>  ExecuteCalc(string rpn, CancellationToken ct);
}
```

`SimDataRouter` is the only type the rest of the app sees. It owns both backends and resolves
each `VarRef` to a backend via an ordered, config-overridable policy, with graceful
degradation when MobiFlight is absent (`ISimDataTransport? _wasm`).

### 5.2 Routing policy (the default; overridable via shipped config)

| Request | Kind | Primary | Fallback | Rationale |
|---|---|---|---|---|
| Read L-var | LVar | **WASM** | SimConnect (unit-risky) | SimConnect L-var read mis-converts units |
| Write L-var | LVar | **WASM** (`(>L:..)`) | SimConnect calc | supported path |
| H-event / B-event / calc | HVar/Calc | **WASM** | — | gauge-API only |
| Read A-var | SimVarA | **policy (default SimConnect)** | WASM | SimConnect cheaper for static SimVars |
| Write A-var | SimVarA | SimConnect | WASM calc | direct datadef/event |
| K-event | KEvent | SimConnect (`TransmitClientEvent`) | WASM calc | native mapping |
| String SimVar | StringVar | **SimConnect** | — | RPN can't read strings |
| Facilities / AI / Position-set | — | **SimConnect** | — | protocol-only |
| PMDG CDU / data struct | CduScreen | **SimConnect (PMDG CDA)** | — | vendor CDA |
| System events | — | **SimConnect** | — | `SubscribeToSystemEvent` |

Encoded rule: *gauge-API stuff (L/H/B/calc) → WASM; first-class SimConnect facilities
(facilities, AI, position, strings, vendor CDAs, system events) → SimConnect; A-vars
configurable, default SimConnect.* This mirrors how SPAD.neXt and FSUIPC split.

### 5.3 Capability detection + graceful degradation (soft dependency)

The app must **not hard-depend** on the MobiFlight community module.

1. Open the shared SimConnect connection (always works if MSFS is up).
2. `MapClientDataNameToID("MobiFlight.Command"/...)` then send `MF.Clients.Add.MsfsBlindAssist`
   and **await `…Add.MsfsBlindAssist.Finished`** (2–3 s timeout). Receipt proves the module
   is present and our private channels exist.
3. `MF.Version.Get` → gate features by version; `MF.Ping`→`MF.Pong` for liveness.
4. If absent/old → backend stays `null`; per-feature degradation: **Fallback** (route to
   SimConnect), **Disable** (announce *"L-var monitoring needs the MobiFlight module"* — a
   blind user must hear *why* a callout is missing), never silent.
5. **Hot-attach:** re-probe on a timer / on SimConnect `Open` so a user who installs MobiFlight
   mid-session gets promoted live; re-arm queued subscriptions.

### 5.4 Threading model — generalize the reentrancy fix

Facts: managed SimConnect is **UI-thread / message-pump driven**, **not thread-safe for
concurrent calls**, and **`ReceiveMessage()` is not reentrant** (the crash we just fixed:
`0xC0000005` in coreclr from a `DoEvents()` pump re-entering `ReceiveMessage`). Critically,
**MobiFlight responses arrive as client-data events on the *same* SimConnect connection** —
there is exactly **one** pump to make safe.

Target pattern:
- **One dedicated SimConnect thread** owns the handle for its lifetime; a hidden message-only
  window pumps `WM_USER_SIMCONNECT` → `ReceiveMessage()`.
- **`_inReceiveMessage` reentrancy guard** (already added in `SimConnectManager`) is the
  permanent rule; **ban `Application.DoEvents()`** in SimConnect paths (add a CI lint).
- Move work in/out with **`System.Threading.Channels`**: `ReadVar` posts a command + a
  `TaskCompletionSource`; the matching `On_Recv*` completes it. No blocking on the pump.
- `On_Recv*` handlers do **no blocking, no DoEvents** — only parse + `channel.Writer.TryWrite`.
- Marshal to the UI once at the edge via a captured `SynchronizationContext`.
- **Bounded** sample channel with `DropOldest` + per-`VarRef` dead-band so a slow speech
  consumer can't make the pump fall behind a per-frame push flood.

### 5.5 Unified subscription model

Present one `VarSample onChanged` event from a per-backend `ISubscriptionSource`:
- **SimConnect:** `RequestDataOnSimObject` with `SIMCONNECT_PERIOD` + `CHANGED` flag.
- **MobiFlight:** maintain a slot table, `MF.SimVars.Add.(...)`, subscribe `ON_SET`, diff each
  pushed block against the last, emit only changed slots (> `Epsilon`).
The router applies the dead-band + coalescing centrally — essential for an a11y app where
every change may become an utterance.

---

## 6. Phased migration (strangler-fig) + performance

### Phase 0 — Insert the facade (no behavior change)
Wrap **all** existing SimConnect access behind `SimDataRouter`/`ISimDataAccess`; router has
only the SimConnect backend; everything routes to it. Pure refactor, identical behavior. Ship.

### Phase 1 — Stand up the WASM backend "dark"
Implement `MobiFlightTransport` (+ the `WASimCommander` variant) and the capability probe, but
keep the policy pointing at SimConnect. Add a **dual-read differential harness**: when the WASM
backend is present, read the same L-vars both ways and **log value/unit discrepancies**. No
user-visible change.

### Phase 2 — Strangle L/H/calc first (highest value, lowest risk)
Flip the policy so `LVar`/`HVar`/`Calc`/`BEvent` route to WASM when present. These are what
SimConnect does *worst* (L-var unit-conversion surprises), so biggest correctness win, smallest
blast radius. Feature-flagged. **This is also where the 1000-cap pressure is relieved** — moving
the A380/Fenix L-var reads off data-defs frees hundreds of slots.

### Phase 3 — Migrate A-var subscriptions selectively
Move high-frequency A-var monitoring to WASM where consolidating into one push stream reduces
data-def churn; keep one-shot / low-frequency A-var reads on SimConnect. Driven entirely by the
configurable A-var policy (no code changes per aircraft).

### Phase 4 — Freeze the SimConnect tail
SimConnect permanently owns the §2 irreducible set (facilities/taxi, AI, teleport, strings,
PMDG CDA, system events, HS787 B: events). Steady-state routing, not migration.

### Performance budget (the central scaling concern)
MobiFlight evaluates each registered var with `execute_calculator_code` **every frame**,
round-robin at `MAX_VARS_PER_FRAME` (default 30). Full-set refresh = `ceil(N / budget)` frames.
At 50 fps:
- N ≤ 30 → ~50 Hz per var.
- N = 300 → ~5 Hz per var.
- A380 ~602 continuous at budget 30 → ~2.5 Hz full refresh (raise budget → more calc/frame →
  stutter risk on expensive FBW L-vars).

Implications: **don't blindly move all ~602 A380 continuous vars to MobiFlight** — that's where
SimConnect's near-free batched delivery wins. Prefer **WASimCommander** (bytecode-compiled,
deltaEpsilon, client-side alloc) for large continuous sets, tune `MAX_VARS_PER_FRAME` per
aircraft, and keep the cheapest static SimVars on SimConnect. The migration's *goal* is relieving
the 1000-cap and getting reliable L-var I/O — **not** maximizing how much rides MobiFlight.

### Testing / rollback
- **Dual-read differential harness** (Phase 1) gates every policy flip: equal within tolerance,
  unit mismatches logged loudly.
- **Capability-matrix tests:** run with MobiFlight present / absent / hot-attached.
- **Reentrancy soak test:** fuzz callbacks during `ReceiveMessage`, assert the `_inReceive`
  guard holds (regression test for the crash just fixed).
- **Golden-utterance tests:** recorded sample streams → assert the spoken-callout sequence is
  unchanged across the migration.
- **Rollback = config flip** (policy is data, not code); per-phase feature flags; the SimConnect
  path is never deleted during migration, so it stays warm as the rollback target.

### Risk table
| Risk | Mitigation |
|---|---|
| Community WASM module absent/old | soft dependency (`?`), per-feature spoken degradation, version-gated capabilities, hot-attach |
| L-var unit/scaling differs between paths | Phase-1 differential harness must pass before flip; WASM canonical |
| Per-frame push floods overwhelm speech | bounded channel + DropOldest + Epsilon dead-band |
| Reentrancy regression | single-pump thread + `_inReceive` guard + soak test + ban `DoEvents` in lint |
| MobiFlight slot drift after re-add | `MF.SimVars.Clear` then rebuild atomically; never mutate slots mid-flight |
| Scaling A380/Fenix continuous sets | prefer WASimCommander; tune `MAX_VARS_PER_FRAME`; keep cheap static SimVars on SimConnect |

---

## 7. Bottom line

SimConnect stays in the family — as the quiet child that does the few things only it can:
**PMDG client-data, taxiway/facility data, AI traffic, teleport, string SimVars, and system
events.** Everything variable- and event-shaped (all L-vars, numeric A-vars, K/H/B events,
RPN) moves to a WASM gateway behind a capability-routed `SimDataRouter`, with **MobiFlight as
the already-integrated default and WASimCommander as the scalable upgrade**, a soft-dependency
detection path, a single reentrancy-safe pump, and a five-phase strangler-fig migration that
relieves the 1000-cap while never risking a big-bang rewrite. The design is aircraft-agnostic:
new add-ons that expose L-vars + H-events + web-view displays (the modern norm) are fully
covered on day one; only a new PMDG-style private-CDA aircraft would need a dedicated SimConnect
module, exactly as PMDG does today.

---

### Sources (accessed 2026-06-03)
- MobiFlight WASM Module (README + `Module.cpp`): https://github.com/MobiFlight/MobiFlight-WASM-Module
- MobiFlight Connector (`WasmModuleUpdater.cs`, `SimConnectCache.cs`): https://github.com/MobiFlight/MobiFlight-Connector
- WASimCommander (+ API docs): https://github.com/mpaperno/WASimCommander · https://wasimcommander.max.paperno.us/
- SimConnect SDK — AddToDataDefinition (1000-entry limit): https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Events_And_Data/SimConnect_AddToDataDefinition.htm
- SimConnect SDK — RequestFacilityData / Managed-code message pump: docs.flightsimulator.com
- PMDG SDK header (CDA names + `THIRD_PARTY_EVENT_ID_MIN`): https://www.fsdeveloper.com/forum/threads/issues-retrieving-cdu-text-from-pmdg-737-ng3-using-simconnect.455478/
- FSUIPC7 / WAPI: https://github.com/jldowson/FSUIPC_WAPI · SPAD.neXt PMDG: https://docs.spadnext.com/simulations/msfs-2024/msfs2024-pmdg-data-access
- Fenix L-var binding + Web MCDU: https://support.fenixsim.com · FBW flight-deck API: https://docs.flybywiresim.com
- .NET `System.Threading.Channels`: https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/
- Strangler-fig: https://martinfowler.com/bliki/StranglerFigApplication.html
- MSFSBA internal: `SimConnectManager.cs`, `MobiFlightWasmModule.cs`, `PMDG777DataManager.cs`, `IPMDGDataManager.cs`, aircraft definitions, `registration.log`.
