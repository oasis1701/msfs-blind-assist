# First Officer for Fenix v3 Implementation Plan (plan 05)

**Claimed by:** kn4iee · **Status:** in progress (code and reviews done 2026-10-22; in-sim checks remain, see "Owner-in-sim checks")

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Fenix First Officer right on Fenix v3: takeoff speeds from the Fenix's own perf L:vars, "engine running/stopped" from the Fenix's own N2 (correct on IAE engines), the A321's missing PACK FLOW selector, v3's purser-driven CABIN READY, and the fuel line counting extra centre tanks.

**Architecture:** One new pure class, `FenixFoLiveReadings`, holds the few Fenix data-service values the FO needs (engine N2 and running flags, cabin ready, total fuel), fed by plan 01's `FenixA320Definition.DataService.Subscribe`. `FenixStateEvaluator` attaches to the definition through `SimConnectManager.CurrentAircraft`, which `FenixFoProfile.BindDataManager` already receives, so nothing under `Forms/FirstOfficer/` changes. The evaluator turns the readings into its existing synthetic fields (`FO_ENG1_N2`, `FO_ENGINES_OFF`, …) with the stock SimVars as the per-engine fallback, and exposes plan 01's `Variant` to the flows. Flows and checklists keep their ids, labels, kinds and links, so the A32NX parity lists need no new entries.

**Tech Stack:** C# 13 / .NET 10, xUnit. Consumes plan 01: `FenixA320Definition.DataService` (`FenixDataService.Subscribe`), `FenixDataValue` / `FenixValueKind`, `FenixVariant` / `FenixAircraftType` / `FenixEngineFamily`, `FenixA320Definition.Variant`.

**Spec:** `docs/fenix-v3/spec.md` (sections 2.1, 3, 4.1, 4.2); index and file ownership `docs/fenix-v3/plans/00-index.md`; interfaces `docs/fenix-v3/plans/01-foundation.md`.

**Branch:** `git switch -c FenixV3-p05-first-officer <base-sha>` (the SHA plan 01 records in `00-index.md`), in a worktree `.claude/worktrees/fenix-v3-p05`.

## Owner decisions

What was checked for this plan (2026-10-21, IAE A321 with two ACTs at idle; reads only, data service and code):
Fenix N2 `aircraft.engines.1.n2` / `.2.n2` read 62.35 / 62.33 (five reads: 62.34 to 62.44 and 62.31 to 62.32, so the value
moves all the time at idle); `aircraft.engines.{1,2}.running` true; `aircraft.communication.cids.cabinReady` false;
`system.numerical.N_MISC_PERF_TO_V1/VR/V2/FLEX` 0 with no perf entered; `S_OH_PNEUMATIC_PACK_FLOW` 1 (no knob on the
A321, spec 3); `S_OH_PNEUMATIC_ECON_FLOW` 0 and its `_L` light 0; `S_OH_FUEL_ACT` 0 with `I_OH_FUEL_ACT_U/_L` 0/0 (both
ACTs empty); `S_OH_CALLS_ALL`, `_MID`, `_EXIT` all exist; `aircraft.fuel.total.amount.kg` 2,557 and `.total.capacity` 23,342
(ACT1/ACT2 capacity 2,349 each). Every cockpit name the FO uses (110) exists in v3: 109 answer on the data service, and
`S_COCKPIT_DOOR` is in the package model (`Part_Exterior_Fuselage_A319/model/Fuselage_Behavior.xml`). The FO uses
none of the names v3 renamed or removed (spec 1.1 to 1.5, 2.3).

| # | Decision | Recommendation | Verified or assumed |
|---|---|---|---|
| O1 | Engine running/stopped source | The Fenix's own N2, per engine; stock `TURB ENG N2` only while that engine's Fenix value is unknown or more than 5 s old. Threshold stays 55 % for CFM and IAE (no per-family threshold) | Idle N2 **measured**: Fenix 59.3 % CFM A320 (spec 3), 62.3 % IAE A321 (above). Starter cut-out "about 50 % N2" **sourced** (Airbus instructor support, [manualslib p.22](https://www.manualslib.com/manual/1964928/Airbus-A320-Series.html?page=22)). Stock N2 on the IAE: 72.4 % at idle, identical on both engines (spec 3, **measured**); how stock N2 moves through an IAE start is **unmeasured**, in-sim check 1 logs it |
| O2 | Engine-start wait | Raise `ES_ENG1_N2` / `ES_ENG2_N2` from 120 s to 180 s on every Fenix variant | The IAE FADEC dry-cranks 30 s before ignition and fuel on every auto start (**sourced**, same manualslib page); Fenix's own option `iae_crank_type` offers 30 or 50 s (`docs/fenix-v3/evidence/fenix-options-labels.tsv`). 180 s is **assumed** margin; in-sim check 1 times it |
| O3 | A321 PACK FLOW | On an A321 the Preflight write `PF_PACKFLOW` becomes a silent skip (no write, no "Already set" for a knob that is not there); its checklist line keeps reading the Fenix's system value. ECON FLOW is left to the Captain; no new reminder (a Captain step cannot be skipped silently per variant without a shared `FlowManager` change) | No PACK FLOW selector on the A321: **verified** (spec 3, package per-type panels). What the A321 SOP says for ECON FLOW: **assumed** "as required"; no public Airbus text found |
| O4 | CABIN READY (v3: the purser sets it, a cockpit call only hurries the purser) | The read-back lines "Cabin: READY" (Line-up and Landing) get a live value: "reported ready" or "not reported". The CALL ALL steps and the flow shapes stay as they are | Changelog: **verified** (`docs/fenix-v3/evidence/web-research.md`, "CABIN READY changes"). Purser timing after the call: **unmeasured**, in-sim check 2. Alternative (a read-only cabin-ready check that delivers the line) needs edits to three shared A320-family tests: proposed in Handoff, not done here |
| O5 | Fuel quantity line | Prefer the Fenix's own `aircraft.fuel.total.amount.kg`, stock weight as fallback | The Fenix total counts the ACTs: **inferred** from `total.capacity` 23,342 kg = wing and centre tanks plus 2 x 2,349 kg. Whether stock counts them: unmeasured; the Fenix's own value is the right one either way |
| O6 | ACT PUMP, IAE N1 MODE, CALLS MID/EXIT | No FO change. ACT PUMP stays in its normal AUTO (transfer is automatic; FWD is a manual backup); N1 MODE is for an abnormal EPR-mode loss; the FO's cabin calls use CALL ALL, which the A321 keeps | **Assumed** from secondary sources: ACT AUTO/FWD ([Brainscape fuel cards](https://www.brainscape.com/flashcards/fuel-system-8538069/packs/14519617), [airlinetechs A321 ACT](http://mobile.airlinetechs.com/airbus/fuel%20transfer/prime.html)); N1 MODE ([FlyByWire ENG MAN panel](https://docs.flybywiresim.com/pilots-corner/a32nx/a32nx-briefing/flight-deck/ovhd/eng-man/)). CALL ALL on the A321: **verified** live |
| O7 | Other timings (APU master, 3 s, START; 180 s AVAIL; Securing 120 s APU flap; Shutdown 120 s power and spool-down waits) | Unchanged. The v3 changelog rebuilt the FADEC and the FWC, not the APU; the AVAIL legend is still re-read once (in-sim check 1) because everything APU rests on it (FO-11) | Legends: **measured on v2** (2026-09-04), to re-read on v3 |

## Global Constraints

- Work only in owned files: `MSFSBlindAssist/FirstOfficer/Fenix/*.cs` except `FenixTakeoffConfigVerdict.cs` and `FenixActionExecutor.cs`, which belong to plan 02 for the whole parallel phase (this plan does not edit the executor, so two people never touch it at once); tests `tests/MSFSBlindAssist.Tests/FirstOfficer/Fenix*.cs` and `A320FamilyParityTests.cs`. New test files must not contain `FenixVariant`, `FenixData`, `FenixEngine`, `FenixCabin`, `FenixTakeoffConfig` (other plans' patterns): this plan's are `FenixFo*.cs`.
- An unknown data-service value (`""`, JSON null, wrong kind) is `FenixValueKind.EmptyOrUnknown`/`Null`: unknown, never 0 or false (FNX-1). In the FO it reads NaN.
- Data-service callbacks arrive on a pool thread (FNX-2); the evaluator stores them under a lock and never speaks from them.
- Data names only from open sources (FNX-3); this plan uses exactly six: `aircraft.engines.1.n2`, `aircraft.engines.2.n2`, `aircraft.engines.1.running`, `aircraft.engines.2.running`, `aircraft.communication.cids.cabinReady`, `aircraft.fuel.total.amount.kg`.
- Variant only through `FenixA320Definition.Variant`; never read the `N_CONFIG_*` L:vars. Unknown variant behaves as today's A320.
- Never infer a Fenix legend (FO-11); never write to the sim while measuring (reads only).
- Keep FO-5 pacing, FO-20 silent skips, FO-25 cut guards (NaN still fails `FO_ENGINES_OFF`), and A32NX parity (`A320FamilyParityTests` stays green with no new allow-list entry).
- Spoken or formatted text gets a test under de-DE and sv-SE. A log line with numbers is built with `string.Create(CultureInfo.InvariantCulture, …)` (`Log.Info` takes a plain string).
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (append `--filter "FullyQualifiedName~<Class>"` for one class). Build first with `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`.

## Review Focus

1. **The gateway stops or restarts mid-flight** (values freeze): a frozen "0 %" must never pass the power-up guard over running engines. Expect the stock fallback after 5 s. Test: Task 2 `N2_goes_stale_after_five_seconds`.
2. **A Fenix that publishes only engine 1's N2** (v2.4 did; JSON null for engine 2): engine 2 must use stock, engine 1 the Fenix. Test: Task 3 `Stock_is_the_fallback_per_engine_while_the_Fenix_value_is_unknown`.
3. **A comma-decimal PC** (de-DE, sv-SE): "V1 142, VR 145, V2 149, flex 55" and "12,200 kilograms" unchanged. Tests: Task 1 and Task 6 theories.
4. **The FO window opened before the variant is known**, or detection failing: the A320 behaviour, never a skipped pack-flow step on an A320. Test: Task 4 `A320_and_unknown_keep_pack_flow_normal`.
5. **An aircraft switch or reconnect while attached** (`CurrentAircraft` already another type, or a second bind): detach cleanly, subscribe once per data service, never throw. Test: Task 2 `Attach_subscribes_once_per_source_and_detaches_on_null`.

## File structure

| File | Change | Responsibility |
|---|---|---|
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixFoLiveReadings.cs` | Create | The six data-service values, freshness, thread safety (pure) |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs` | Modify | Attach to the definition; synthetic fields from the readings; `Variant`; cabin and fuel text; takeoff-speed poll names |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixFoProfile.cs` | Modify | `BindDataManager` attaches the evaluator to `sc.CurrentAircraft` |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs` | Modify | Takeoff-speed names; cabin live values |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` | Modify | A321 pack-flow provider; 180 s start waits; comments |
| `MSFSBlindAssist/FirstOfficer/Fenix/FenixFOAutoManager.cs` | Modify | Stale comment only |
| `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFo{TakeoffSpeeds,LiveReadings,EngineRunning,A321,CabinReady,NameSweep}Tests.cs` | Create | One per task |
| `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoBaroStdTests.cs` | Create | Task 7: baro STD from the QNH lights |
| `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixReadbackTests.cs`, `FenixFlowParityTests.cs` | Modify | Updated names, timeout, live-value list, fuel tests |

---

### Task 1: Takeoff speeds from the native perf L:vars

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs:598-601` (`BSC_TOSPEEDS` live value)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs:67` (poll list)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFOAutoManager.cs:5-9` (comment: the Fenix does publish V-speeds now, `N_MISC_PERF_TO_*`; there is no Fenix flap schedule because FO-15 limits auto-flaps to the FBW jets and the A330)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixReadbackTests.cs:220`
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoTakeoffSpeedsTests.cs`

**Interfaces:**
- Consumes: `AirbusReadbackFormat.TakeoffSpeeds(v1, vr, v2, flex)` (shared, unchanged: NaN gives null, a known value below 1 gives "not set", the same threshold as `SimVarDefinition.NotSetBelow = 1` that A320-10 names).
- Produces: `BSC_TOSPEEDS` reads `N_MISC_PERF_TO_V1`, `N_MISC_PERF_TO_VR`, `N_MISC_PERF_TO_V2`, `N_MISC_PERF_TO_FLEX` (all registered Continuous at `FenixA320Definition.cs:6548-6579`; the takeoff-speed hotkey reads V1/VR at `MainForm.Hotkeys.cs:525-526`). No FO file names `FNX2PLD_` any more, so plan 02 can delete those definitions.

- [x] **Step 1: Write the failing tests** in `FenixFoTakeoffSpeedsTests`:

```csharp
// Cultures: "en-US", "de-DE", "sv-SE" (MemberData, the FenixReadbackValueTests pattern).
[Theory, MemberData(nameof(Cultures))]
public void Takeoff_speeds_line_reads_the_native_perf_vars(string culture)
{
    var e = new FenixStateEvaluator();
    e.SetSimConnect(SeededSimConnectCache.With(("N_MISC_PERF_TO_V1", 142), ("N_MISC_PERF_TO_VR", 145),
        ("N_MISC_PERF_TO_V2", 149), ("N_MISC_PERF_TO_FLEX", 55)));
    Assert.Equal("V1 142, VR 145, V2 149, flex 55", InCulture(culture, () => Item("BSC_TOSPEEDS").ReadLiveValue(e)));
}
// Measured on v3 2026-10-21: all four read 0 until PERF TAKEOFF is filled in.
[Fact] public void Takeoff_speeds_read_not_set_before_perf_is_entered()  // all four seeded 0 -> "not set"
[Fact] public void Takeoff_speeds_are_unknown_until_read()               // empty cache -> null
[Fact] public void No_Fenix_first_officer_file_names_a_FenixQuartz_var() // no "FNX2PLD_" in any MSFSBlindAssist/FirstOfficer/Fenix/*.cs (ClaudeContextBudgetTests.RepoRoot())
```

- [x] **Step 2: Run them; the live-value theory, the "not set" fact and the source scan fail.**
- [x] **Step 3: Replace the three `FNX2PLD_speed*` names** in `BSC_TOSPEEDS` and in `PollFields` with `N_MISC_PERF_TO_V1`, `N_MISC_PERF_TO_VR`, `N_MISC_PERF_TO_V2` (same order); update the list at `FenixReadbackTests.cs:220` the same way; fix the `FenixFOAutoManager` comment.
- [x] **Step 4: Run the Fenix FO tests and the whole suite; all pass** (`FoPollSilenceTests` included: the perf vars are not spoken on every delivery).
- [x] **Step 5: Commit.** `fix(fo-fenix): takeoff speeds read-back from N_MISC_PERF_TO_* (FenixQuartz is gone)`

### Task 2: Fenix live readings and attaching the evaluator

**Files:**
- Create: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFoLiveReadings.cs`
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs` (fields, `Variant`, `AttachFenix`)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFoProfile.cs:27-28` (`BindDataManager`)
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoLiveReadingsTests.cs`

**Interfaces:**
- Consumes (plan 01): `FenixA320Definition.DataService.Subscribe(IReadOnlyList<string>, Action<string, FenixDataValue>) : IDisposable` (callbacks on a pool thread), `FenixDataValue(Kind, Number, Bool, Text, RawJson)`, `FenixDataValue.Unknown`, `FenixA320Definition.Variant`, `FenixVariant.Unknown`.
- Produces, in `FenixFoLiveReadings` (namespace `MSFSBlindAssist.FirstOfficer.Fenix`):
  - consts `Engine1N2 = "aircraft.engines.1.n2"`, `Engine2N2 = "aircraft.engines.2.n2"`, `Engine1Running = "aircraft.engines.1.running"`, `Engine2Running = "aircraft.engines.2.running"`, `CabinReadyName = "aircraft.communication.cids.cabinReady"`, `FuelTotalKgName = "aircraft.fuel.total.amount.kg"`; `public static IReadOnlyList<string> Names` (those six, in that order); `public const long N2StaleAfterMs = 5_000;`
  - `public FenixFoLiveReadings(Func<long>? clockMs = null)` (default `Environment.TickCount64`)
  - `public void Apply(string name, FenixDataValue value)`, `public void Clear()`
  - `public double N2(int engine)`: the Number if received within `N2StaleAfterMs`, else NaN (engine 1 or 2, anything else NaN)
  - `public double Running(int engine)`, `public double CabinReady { get; }`: a Bool as 1 or 0, else NaN (no staleness: they change rarely and are pushed on change)
  - `public double FuelTotalKg { get; }`: last Number, else NaN
- Produces, on `FenixStateEvaluator`:
  - `public FenixVariant Variant { get; }` (from the attached definition; `FenixVariant.Unknown` while detached)
  - `public void AttachFenix(FenixA320Definition? definition)`: null detaches; otherwise calls the overload below with `definition.DataService` as source, `() => definition.Variant`, and `(names, cb) => definition.DataService.Subscribe(names, cb)`
  - `internal void AttachFenix(object? source, Func<FenixVariant>? variant, Func<IReadOnlyList<string>, Action<string, FenixDataValue>, IDisposable>? subscribe)`: same source as last time does nothing; a new source disposes the old handle, clears the readings, subscribes `FenixFoLiveReadings.Names` with `Apply`, logs `Log.Info("FO", "Fenix First Officer: reading the Fenix's own engine, cabin and fuel values")`; a null source disposes, clears, and returns `Variant` to Unknown. A null `subscribe` attaches the variant only (tests).
  - `private readonly FenixFoLiveReadings _live = new();` (read by the synthetic fields in Tasks 3, 5, 6; the class is sealed, so not `protected`)
- Produces, in `FenixFoProfile.BindDataManager`: `state.SetSimConnect(sc); state.AttachFenix(sc.CurrentAircraft as FenixA320Definition);` Why this path: `SimConnectManager.CurrentAircraft` (`SimConnectManager.cs:155-163`) is set by MainForm on load and on switch (`MainForm.cs:698`, `MainForm.AircraftSwitch.cs:875`), and `FirstOfficerForm.UpdateServicesFromConnection` (`FirstOfficerForm.cs:464-468`) calls `BindDataManager` at construction and on every reconnect, so the Fenix row in `FirstOfficerWindows.cs:38` stays as it is. The subscription ends when the definition disposes its data service on a switch (plan 01's `OnSwitchedAway`), which is also when MainForm disposes the FO window.

- [x] **Step 1: Write the failing tests** in `FenixFoLiveReadingsTests` (helpers `Num(double)` and `Flag(bool)` build `FenixDataValue`s; a fake clock is a `long` field the test moves):
  - `Unknown_values_read_NaN`: `Apply(Engine1N2, FenixDataValue.Unknown)` gives `double.IsNaN(N2(1))`; a JSON-null value (`Kind == Null`) the same; `Apply(CabinReadyName, Num(1))` (wrong kind) gives `double.IsNaN(CabinReady)`.
  - `N2_goes_stale_after_five_seconds`: clock 0, `Apply(Engine1N2, Num(62.35))`; at 4,999 `N2(1) == 62.35`; at 5,001 `double.IsNaN(N2(1))`; a new `Apply` at 5,001 makes it 62.35 again.
  - `Flags_and_fuel_read_their_last_value`: running true gives `Running(1) == 1`, cabin false gives `CabinReady == 0`, fuel `Num(2557.18)` gives `FuelTotalKg == 2557.18`, all still the same after the clock moves 60 s.
  - `Clear_forgets_everything`: after `Clear()`, every getter is NaN.
  - `Attach_subscribes_once_per_source_and_detaches_on_null`: with a fake `subscribe` that records each call and returns a handle with a `Disposed` flag: `AttachFenix(srcA, () => A321, sub)` twice gives one call, whose names equal `FenixFoLiveReadings.Names`, and `Variant.Type == FenixAircraftType.A321`; `AttachFenix(srcB, …)` disposes handle A and calls again; `AttachFenix(null, null, null)` disposes handle B and `Variant == FenixVariant.Unknown`. `AttachFenix((FenixA320Definition?)null)` on a fresh evaluator does not throw. (That a switch also clears the values is pinned in Task 3, where the values first reach a field.)
- [x] **Step 2: Run them; they fail to compile.**
- [x] **Step 3: Implement** `FenixFoLiveReadings` (one private lock, a dictionary name to (value, received-at ms)) and the evaluator and profile members above. The evaluator keeps its parameterless constructor (`FoPollSilenceTests` creates it by reflection).
- [x] **Step 4: Run the class, then the whole suite; all pass.**
- [x] **Step 5: Commit.** `feat(fo-fenix): read the Fenix's own engine, cabin and fuel values from the data service`

### Task 3: Engine running and stopped from the Fenix's N2

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs:6-19` (class and const comments), `:103-109` (`FO_ENG1_N2`, `FO_ENG2_N2`, `FO_ENGINES_OFF`)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs:34-35` (comment), `:280-292` (comment and the two timeouts)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFlowParityTests.cs:43-58` (180, comment)
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoEngineRunningTests.cs`

**Interfaces:**
- Consumes: Task 2's `_live.N2(n)`, `_live.Running(n)`; the base class's stock `Eng1N2` / `Eng2N2` (`LVarStateEvaluator.cs:83-84`, pushed by the window each second).
- Produces: `FO_ENG{n}_N2` = `_live.N2(n)` when known, else the stock value; `FO_ENGINES_OFF` = `Both(FO_ENG1_N2, FO_ENG2_N2, both < 20)` on those resolved values (NaN while either is unknown, as today). `EngineRunningN2` stays 55.0. `internal const int EngineStartTimeoutSeconds = 180;` in `FenixFlowDefinitions`, used by both start waits. Every consumer follows without change: `EPU_ENGINES_STOPPED`, `SC_ENGINES_STOPPED`, `SD_ENGINES_STOPPED`, `ES_ENG{n}_RUN`, `PKC_ENGINES`, `NotParked`.
- Diagnostic, no test: `private void TraceEngine(int engine, double fenixN2, double stockN2)`, called from the two `FO_ENG{n}_N2` reads, logs one `Log.Info("FO", …)` line when the source changes (Fenix or stock), or when Fenix N2 ≥ 55, stock N2 ≥ 55 or the running flag changes, for example `Fenix engine 1: Fenix N2 55.2 %, stock N2 68.0 %, running flag 1, source Fenix` (invariant culture; a lock guards the edge state, since the window timer and a flow read at the same time). In-sim check 1 reads these lines.

- [x] **Step 1: Write the failing tests** in `FenixFoEngineRunningTests` (attach a fake subscription as in Task 2 and push values through its callback):

```csharp
[Fact] public void Fresh_Fenix_N2_wins_over_stock()
{   // IAE: stock reads idle while the Fenix engine is still starting (spec 3: stock 72.4 vs Fenix 62.3 at idle).
    push(Engine1N2, Num(50.0)); push(Engine2N2, Num(62.3)); e.SetEngineN2(72.4, 72.4);
    Assert.Equal(50.0, e.GetValue("FO_ENG1_N2"));
    Assert.True(e.GetValue("FO_ENG1_N2") < FenixStateEvaluator.EngineRunningN2);
    Assert.Equal(62.3, e.GetValue("FO_ENG2_N2"));
}
[Fact] public void Stock_is_the_fallback_per_engine_while_the_Fenix_value_is_unknown()
    // push Engine1N2 Unknown and Engine2N2 Kind.Null; SetEngineN2(61.4, 59.3) -> 61.4 and 59.3;
    // push Engine1N2 Num(60.1) -> engine 1 reads 60.1, engine 2 still 59.3. Not attached at all -> stock.
[Fact] public void Engines_off_follows_the_Fenix_while_it_is_fresh()
    // stock (0, 0) pushed; Fenix 30.0 / 0.0 -> FO_ENGINES_OFF == 0; Fenix 3.0 / 0.0 -> 1;
    // nothing pushed and nothing attached -> NaN (FO-25: unread never passes the guard).
[Fact] public void Detaching_or_switching_source_returns_to_stock()
    // Fenix 50 pushed, stock 61.4: AttachFenix(otherSource, …) -> FO_ENG1_N2 == 61.4 (values cleared);
    // push 50 again through the new callback, then AttachFenix(null, null, null) -> 61.4.
[Fact] public void Running_threshold_sits_between_starter_cut_out_and_both_measured_idles()
{
    const double StarterCutOut = 50.0, CfmIdleFenix = 59.3, IaeIdleFenix = 62.3; // manualslib p.22; spec 3; plan 05 read
    Assert.InRange(FenixStateEvaluator.EngineRunningN2, StarterCutOut + 1, Math.Min(CfmIdleFenix, IaeIdleFenix) - 1);
}
```

  And in `FenixFlowParityTests.Engine_start_waits_for_N2_and_completes_its_running_line`: `Assert.Equal(180, s.TimeoutSeconds)`.
- [x] **Step 2: Run them; all but the threshold test fail** (the evaluator still reads stock only, and the timeout is 120).
- [x] **Step 3: Implement** the three synthetic fields over the resolved per-engine N2, `TraceEngine`, the 180 s constant; rewrite the stale comments (stock is now the fallback; give the measured idles and the cut-out source).
- [x] **Step 4: Run the class, `FoEngineAndGroundGuardTests`, `FoCutGuardSweepTests` (both push stock N2 only and must pass unchanged) and the whole suite.**
- [x] **Step 5: Commit.** `fix(fo-fenix): engine running and stopped from the Fenix's own N2; 3 min start wait for IAE`

### Task 4: The A321 has no PACK FLOW selector

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs` (one property)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs:152-153` (`PF_PACKFLOW`)
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoA321Tests.cs`

**Interfaces:**
- Consumes: Task 2's `Variant`; the file's `Provider(...)` builder (`FenixFlowDefinitions.cs:729-735`).
- Produces: `public bool HasPackFlowSelector => Variant.Type != FenixAircraftType.A321;` (Unknown, A318, A319, A320 have one). `PF_PACKFLOW` becomes `Done(Skip(Provider("PF_PACKFLOW", "Pack flow: NORMAL", "S_OH_PNEUMATIC_PACK_FLOW", s => s.HasPackFlowSelector ? 1 : null), s => s.HasPackFlowSelector && s.IsPosition("S_OH_PNEUMATIC_PACK_FLOW", 1)), "PF_PACKFLOW")`: same id, label, kind (SetSwitch) and link, so parity is untouched; on an A321 FlowManager's FO-20 path skips it silently. The checklist line `PF_PACKFLOW` is unchanged.

- [x] **Step 1: Write the failing tests** in `FenixFoA321Tests` (`A321 = FenixVariant.Unknown with { Type = FenixAircraftType.A321, Engine = FenixEngineFamily.IaeV2500 }`, attached with `e.AttachFenix(new object(), () => A321, null)` over `SeededSimConnectCache.ConnectedWith(("S_OH_PNEUMATIC_PACK_FLOW", 1))`):
  - `A321_preflight_neither_writes_nor_claims_pack_flow`: `Assert.False(step.SkipCondition!(e))`, `Assert.Null(step.TargetValueProvider!(e))`, `Assert.Equal(new[] { "PF_PACKFLOW" }, step.LinkedChecklistItemIds)`.
  - `A320_and_unknown_keep_pack_flow_normal` (Theory over `FenixAircraftType.Unknown`, `A319`, `A320`): `Assert.True(step.SkipCondition!(e))` and `Assert.Equal(1, step.TargetValueProvider!(e))`.
- [x] **Step 2: Run them; they fail** (no provider yet).
- [x] **Step 3: Implement** the property and the step change.
- [x] **Step 4: Run the class, `A320FamilyParityTests` and the whole suite; all pass with no allow-list change.**
- [x] **Step 5: Commit.** `fix(fo-fenix): no pack flow step on the A321, which has no selector`

### Task 5: CABIN READY from the purser (v3)

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs` (synthetic field, text)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs:663` (`LUC_CABIN`), `:703` (`LDC_MEMO_CABIN`); `:315-318` and `:389-391` comments only (in v3 the call hurries the purser; it does not set CABIN READY)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs:399-404`, `:490-491` (the same comment)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixReadbackTests.cs:122-125` (add both ids to the live-value list)
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoCabinReadyTests.cs`

**Interfaces:**
- Consumes: Task 2's `_live.CabinReady`.
- Produces: `public const string CabinReadyField = "FO_CABIN_READY";` (1, 0 or NaN); `public string? CabinReadyText()` and `internal static string? CabinReadyText(double v)`: 1 gives `"reported ready"`, 0 gives `"not reported"`, NaN gives null. Both lines become `Reminder(…, "Cabin: READY", s => s.CabinReadyText())`: still Reminders (the pilot confirms; FOA-8), the tick speaks only the value (CORE-7), and the status line shows it. Nothing in the FO announces cabin ready on its own: plan 08 owns that announcement.

- [x] **Step 1: Write the failing tests** in `FenixFoCabinReadyTests`:
  - `Cabin_lines_read_the_pursers_report` (Theory over `LUC_CABIN`, `LDC_MEMO_CABIN`): push `CabinReadyName` `Flag(true)` gives `ReadLiveValue(e) == "reported ready"`; `Flag(false)` gives `"not reported"`; `FenixDataValue.Unknown` gives null.
  - `Cabin_lines_stay_pilot_confirmed_reminders`: `Type == ChecklistItemType.CaptainReminder`, `StateFieldName` null, `CheckAction` null.
  - `No_Fenix_first_officer_string_says_cabin_ready`: no string literal matching `"[^"\n]*cabin ready[^"\n]*"` (case-insensitive) in `MSFSBlindAssist/FirstOfficer/Fenix/*.cs` (the labels say "Cabin: READY").
- [x] **Step 2: Run them; the live-value theory fails.**
- [x] **Step 3: Implement**, and add both ids to the `FenixReadbackTests` live-value list.
- [x] **Step 4: Run the class, `FenixReadbackTests`, `A320FamilyParityTests` and the whole suite.**
- [x] **Step 5: Commit.** `feat(fo-fenix): Cabin READY lines read the purser's report (Fenix v3)`

### Task 6: Fuel quantity from the Fenix's own total

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs:189-196` (`FuelText`)
- Modify: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixReadbackTests.cs` (class `FenixReadbackValueTests`, beside `:302-312`)

**Interfaces:**
- Consumes: Task 2's `_live.FuelTotalKg`.
- Produces: `public const string FuelTotalKgField = "FO_FUEL_TOTAL_KG";`; `internal static string? FuelText(Func<string, double> read)` reads `FuelTotalKgField` first (kilograms) and falls back to `FUEL TOTAL QUANTITY WEIGHT` (pounds, as today). The stock weight stays polled.

- [x] **Step 1: Write the failing tests:**

```csharp
[Theory, MemberData(nameof(Cultures))]
public void Fuel_prefers_the_Fenixs_own_total(string culture)
{   // The Fenix total counts the extra centre tanks (capacity 23,342 kg on the ACT A321).
    var read = Reader(new() { [FenixStateEvaluator.FuelTotalKgField] = 12_247, ["FUEL TOTAL QUANTITY WEIGHT"] = 99_999 });
    Assert.Equal("12,200 kilograms", InCulture(culture, () => FenixStateEvaluator.FuelText(read)));
}
// The existing Fuel_is_the_stock_pounds_spoken_in_kilograms_to_the_nearest_hundred and Fuel_is_null_when_unknown stay as they are.
```

- [x] **Step 2: Run; the new theory fails** ("45,400 kilograms", from the stock 99,999 lb).
- [x] **Step 3: Implement** the field and the fallback.
- [x] **Step 4: Run `FenixReadbackTests` and the whole suite.**
- [x] **Step 5: Commit.** `fix(fo-fenix): fuel quantity line from the Fenix's own total (counts ACT fuel)`

### Task 7: Baro STD from the QNH lights (measured 2026-10-21)

Why: `S_FCU_EFISn_BARO_STD` is the baro knob's push/pull counter, not a state: a pull adds 1, a push takes 1 away, so after cockpit pulls and pushes it holds any number (measured: 3 while in STD). The state is `I_FCU_EFISn_QNH`, 1 = QNH, 0 = STD (HubHop's "EFIS1_BARO_STD Status" output; read live: 0 in STD, 1 in QNH). The First Officer's baro readback (`BaroText`, "Baro reference: SET" reminders, `FenixChecklistDefinitions.cs:587, 678`) reads the counter today. Plan 02 Task 11 fixes the write side (`SetBaroStdBoth`, plan 02's file); this task fixes the read side.

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixStateEvaluator.cs` (polled list :56; `BaroText` :175-190)
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoBaroStdTests.cs` (new)

**Interfaces:** `internal static string? BaroText(Func<string, double> read)` keeps its signature; it reads `I_FCU_EFIS1_QNH` (registered `Continuous` in the definition at :1375) instead of `S_FCU_EFIS1_BARO_STD`.

- [x] **Step 1: Write the failing tests:** `Std_comes_from_the_qnh_light`: with `I_FCU_EFIS1_QNH` 0, `BaroText` reads as standard whatever `S_FCU_EFIS1_BARO_STD` holds (seed it 3, then 0, then -2); with the light 1 and `S_FCU_EFIS1_BARO_MODE` 1, `N_FCU_EFIS1_BARO_HPA` 1010, it reads "QNH 1010" even with the counter at 3; light unread (NaN) gives the same result as today's unread STD. A source scan: `FenixStateEvaluator.cs` polls `I_FCU_EFIS1_QNH` and `I_FCU_EFIS2_QNH`, and no longer names `S_FCU_EFIS1_BARO_STD`.
- [x] **Step 2: Run; they fail.**
- [x] **Step 3: Implement:** poll the two lights in place of the two counters; `BaroText` treats light 0 as STD, 1 as QNH (the existing unit and value logic unchanged).
- [x] **Step 4: Run `FenixFoBaroStdTests`, `FenixReadbackTests` and the whole suite; all pass.**
- [x] **Step 5: Commit.** `fix(fo-fenix): baro STD read from the QNH light, not the knob's push/pull counter`

### Task 8: Name sweep: everything the FO reads exists, nothing renamed by v3

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FirstOfficer/FenixFoNameSweepTests.cs`

**Interfaces:**
- Consumes: `RecordingSimConnectCache` (`tests/.../FirstOfficer/RecordingSimConnectCache.cs`), `ClaudeContextBudgetTests.RepoRoot()`.
- Produces: the guard plan 12 relies on when it merges plan 02's renames and deletions.

- [x] **Step 1: Write the tests:**
  - `Every_cache_key_the_first_officer_reads_is_registered`: `keys = new FenixA320Definition().GetVariables().Keys`; for each seed value 0, 1 and 2 (conditions short-circuit, so one seed cannot reach every read), build `RecordingSimConnectCache.With(keys.Select(k => (k, (double)seed)))`, give it to a new `FenixStateEvaluator`, then call every flow step's `SkipCondition`, `LeaveAloneWhen`, `TargetValueProvider` and `GetValue(ConditionFieldName)`, and every checklist item's `GetValue(StateFieldName)`, `GetValue` of each `AdditionalStateFields`, `ReadLiveValue` and `LeaveAloneWhen`. Assert every recorded key, and every `OnRequestPollFields` entry, is in `keys` (message lists the strays).
  - `No_first_officer_file_names_a_name_v3_renamed_removed_or_left_dead`: none of `FNX2PLD_`, `B_ELEC_BUS_POWER_DC_SERIVICE`, `I_OH_DOOR_VIDEO`, `I_OH_ELEC_COMMERCIAL_U`, `S_HYD_GRAVITY_GEAR_EXTEND_ROTATION`, `I_MIP_AUTOLAND_`, `S_OH_ELEC_GEN1` appears in `MSFSBlindAssist/FirstOfficer/Fenix/*.cs` (spec 1.1 to 1.5, 2.1, 2.3).
- [x] **Step 2: Run them; both pass** after Tasks 1-7 (they pin today's state; if the first fails, the message names a read that nothing registers: fix the FO, not the test).
- [x] **Step 3: Commit.** `test(fo-fenix): sweep the names the Fenix First Officer reads against the definition and v3's renames`

### Task 9: Hand back

- [x] **Step 1:** Build the solution and run the whole suite: green. `git diff --name-only <base-sha>` lists only the files in "File structure", plus `FenixFlightPhaseMonitor.cs` (one comment; handoff request 9).
- [ ] **Step 2:** Owner-in-sim checks below (stop and ask the owner; build only with MSFSBA closed, CORE-3).
- [ ] **Step 3:** Fill in the results under "Handoff to plan 12" (measured values, decision outcomes) and hand back the branch. Done except the measured values, which wait for Step 2.

## Owner-in-sim checks (few, prioritised)

Anyone with MSFS 2024 and the Fenix v3 can run these (the owner or a collaborator). Build `FenixV3` with MSFSBA closed
(CORE-3), start MSFSBA, and load the **Fenix A321 IAE with ACTs** at a stand. The First Officer window is
**Output Shift+C**. **Ctrl+Tab** moves between its **Flows** and **Checklists** tabs. On Flows, arrow to a flow and press
**Enter** to run it. On Checklists, arrow to a checklist, **Right Arrow** opens it, **Down Arrow** walks its lines, and
**Tab** reaches **Checklist status**, which reads the selected line with its live value. Leave **Speak flow progress**
on.

Who does what: the pilot presses the keys and listens. The readings below can be taken by a second person, or by Claude
Code with the simconnect MCP (cockpit L:vars and stock SimVars) and the PowerShell query below (the Fenix's own values
on its data service). Reads only: nothing writes to the sim except the pilot's own flows and cockpit actions. The MCP
can return 0 or null for an L:var until it reconnects: read one name per call and check a stock value before trusting
a null. The First Officer's own record is `%APPDATA%\MSFSBlindAssist\logs\debug.log` (lines starting `Fenix engine 1:`
and `Fenix engine 2:`, written only when something changes).

One read of the APU legends and cabin ready (PowerShell):

```powershell
$q = '{"query":"query { dataRef { u: dataRef(name: \"system.indicators.I_OH_ELEC_APU_START_U\") { value } l: dataRef(name: \"system.indicators.I_OH_ELEC_APU_START_L\") { value } c: dataRef(name: \"aircraft.communication.cids.cabinReady\") { value } } }"}'
Invoke-RestMethod -Uri http://127.0.0.1:8083/graphql -Method Post -ContentType 'application/json' -Body $q | ConvertTo-Json -Depth 5 -Compress
```

The Fenix's own engine N2 and fuel total (PowerShell; the MCP cannot read these, they live only on the data service):

```powershell
$q = '{"query":"query { dataRef { e1: dataRef(name: \"aircraft.engines.1.n2\") { value } e2: dataRef(name: \"aircraft.engines.2.n2\") { value } f: dataRef(name: \"aircraft.fuel.total.amount.kg\") { value } } }"}'
Invoke-RestMethod -Uri http://127.0.0.1:8083/graphql -Method Post -ContentType 'application/json' -Body $q | ConvertTo-Json -Depth 5 -Compress
```

1. **Ground turnaround: APU, engines off, IAE engine start (highest; O1, O2, O7).** Note the Fenix EFB setting *IAE Crank Duration* first.
   - Run **Before Start**. Expect *"APU master: ON"*, *"Waiting before APU start"*, *"APU start"*, *"Waiting for: Waiting for APU available"*, then *"APU bleed: ON"* with no *"Timed out waiting for"*. Every few seconds the reader runs the APU read above (or reads `I_OH_ELEC_APU_START_U` and `_L` with the MCP): once AVAIL, `u` should read 1 and `l` 0, as on v2. If `l` is the lit one, stop: `FenixActionExecutor.ApuAvailField` must change (FO-11), report it.
   - Run **Shutdown**. Expect *"Waiting for: Engines stopped"* to pass, then *"Fuel pumps: ALL OFF"*, and *"Shutdown flow complete"*, not *"Shutdown flow stopped"*. A minute later, note in debug.log whether a `Fenix engine` line ending `source stock` appeared (expected if the Fenix sends N2 only when it changes; harmless, stock N2 is 0 too).
   - Run **Before Start** again (pumps and beacon back on; the APU says *"Already set"*), then **Engine Start**. Expect *"Engine 1 starting — waiting for the engine to stabilize"*, then *"Engine 2 master: ON"*, then *"Engine Start flow complete"*, with no *"Timed out"*.
   - Read debug.log's `Fenix engine 1:` and `Fenix engine 2:` lines: the Fenix-versus-stock N2 at each crossing, the running-flag edge, and the time from the *"Engine 1 master: ON"* step to engine 1's first line with Fenix N2 ≥ 55 (must be under 180 s). Between those two moments there should be no line ending `source stock`: if there is one, the Fenix stopped sending N2 during the crank and the FO judged on stock N2, so report it with the lines (the 5 s staleness rule rests on how often the Fenix sends N2, which is unmeasured for a crank). Record these numbers in the handoff.
2. **Checklist live values: takeoff speeds, fuel, baro, cabin ready (O4, O5, Tasks 1, 5, 6, 7).** All on the **Checklists** tab, each read with **Checklist status**.
   - *"Before Start Checklist"*, *"Takeoff speeds and thrust: SET"*: before PERF TAKEOFF is filled in it says *"not set"*. On the MCDU's PERF TAKEOFF page enter V1 142, VR 145, V2 149, FLX 55: it reads *"Takeoff speeds and thrust: SET, V1 142, VR 145, V2 149, flex 55 — Incomplete"*.
   - *"Cockpit Preparation Checklist"*, *"Fuel quantity: CHECKED"*: kilograms to the nearest hundred, matching the fuel on board the ECAM or EFB shows, the ACT fuel included (or the `f` value of the second read above).
   - Same checklist, *"Baro reference: SET"*: pull and push the captain's baro knob a few times in the cockpit, ending pulled (STD): it says *"standard"*. Push it (QNH): it says *"QNH"* and the value. (The old code read the knob's counter and got this wrong after several pulls; the reader can confirm `I_FCU_EFIS1_QNH` with the MCP: 0 in STD, 1 in QNH.)
   - Run **Line-up** (the cabin call is its first step) and watch *"Line-up Checklist"*, *"Cabin: READY"*: *"not reported"* until the purser reports, then *"reported ready"*. Record the seconds from the call to `c` reading true, and the EFB's *Cabin Ready Call* setting. Also record what Checklist status says right after the call: *"Cabin: READY, not reported — Complete"* is the known open question (handoff request 8).
   - In flight, after takeoff and before the approach: *"Landing Checklist"*, *"Cabin: READY"* should say *"not reported"* (and `c` false). If it says *"reported ready"*, the Fenix keeps the departure report and the Landing line would claim a ready cabin before the purser reports for landing: record it (handoff request 8). If you fly on to the approach, note when it changes to *"reported ready"* after the **Approach** flow's cabin call.
3. **A321 pack flow (O3; optional).** Run **Preflight** on the A321: no *"Pack flow"* line is spoken. On any A320 preset it still says *"Already set: Pack flow: NORMAL"*.

## Handoff to plan 12

**Pilot-facing changes (guide: the First Officer part, `guide/first-officer.md`; plan 12 writes it):**
- Fenix: the Before Start Checklist's *"Takeoff speeds and thrust: SET"* line now reads V1, VR, V2 and flex from the MCDU's PERF TAKEOFF page (it was silent unless the discontinued FenixQuartz tool ran), and *"not set"* until they are entered.
- Fenix: Engine Start waits for the Fenix's own N2, which is right on IAE engines too, and allows up to 3 minutes per engine (the IAE cranks 30 or 50 seconds before it lights).
- Fenix A321: the Preflight flow no longer mentions the pack flow selector, which the A321 does not have; set its ECON FLOW button as you need it.
- Fenix v3: the Line-up and Landing checklists' *"Cabin: READY"* lines say whether the cabin crew has reported (*"reported ready"* or *"not reported"*). In v3 the cabin call no longer makes the cabin ready; the purser reports when the cabin is ready.
- Fenix: the Cockpit Preparation Checklist's fuel line uses the Fenix's own total, which counts the extra centre tanks.
- Fenix: the Cockpit Preparation and Approach checklists' *"Baro reference: SET"* lines read standard or QNH from the EFIS's own QNH light, so they stay right after you pull and push the baro knob several times.
- Changelog fragment (category `fix`), for example: "Fenix A320 family First Officer: the takeoff-speeds check reads your MCDU speeds again, engine start is judged on the Fenix's own engine readings (IAE engines no longer move on too early), the A321 skips the pack flow selector it does not have, the cabin-ready lines say whether the cabin crew has reported, and the baro reference line is right after you pull and push the baro knob."

**Proposed rule lines** (`.claude/rules/first-officer-airbus.md`, body 4,736 of 8,000 characters; these four add about 1,450; IDs provisional, renumber if plan 02 also proposes FOA lines; full text under `## FOA-13..16` in `docs/invariants/first-officer-airbus.md` from this plan's Owner decisions and sources, FOA-16's from Task 7):
```text
- [FOA-13] The Fenix FO judges an engine running or stopped on the Fenix's own N2 (`FenixFoLiveReadings.N2`, data service), falling back per engine to stock N2 only while that value is unknown or 5 s old: stock N2 is a generic model (IAE idle 72 % vs the Fenix's 62 %). Running is 55 %: above the ~50 % starter cut-out, below both measured idles. Full: docs/invariants/first-officer-airbus.md#foa-13
- [FOA-14] Fenix v3: a cockpit cabin call no longer sets CABIN READY, the virtual purser does. The FO reads `FenixStateEvaluator.CabinReadyField` only as the "Cabin: READY" lines' live value and never speaks cabin ready itself (the Fenix cabin window owns that announcement). Full: docs/invariants/first-officer-airbus.md#foa-14
- [FOA-15] A Fenix FO step only some variants have gates on `FenixStateEvaluator.Variant` (the definition's detected variant), never on the config L:vars; on a variant without it the step's target provider returns null (FO-20's silent skip), and an unknown variant behaves as the A320 (the A321 has no PACK FLOW selector). Full: docs/invariants/first-officer-airbus.md#foa-15
- [FOA-16] The Fenix FO reads baro STD from the EFIS QNH light (`I_FCU_EFIS1_QNH`: 0 STD, 1 QNH; `FenixStateEvaluator.BaroText`), never `S_FCU_EFIS1_BARO_STD`, the knob's push/pull counter (a pull adds 1, a push takes 1 away; it read 3 in STD, 2026-10-21). Full: docs/invariants/first-officer-airbus.md#foa-16
```
(Lengths 399, 328, 375 and 309 characters, each under the 400 cap.)

**Handoff requests:**
1. **Order with plan 02: resolved.** Plan 02 keeps `FNX2PLD_speedV1/VR/V2` and deletes only the other 13 FenixQuartz definitions; plan 12 deletes the three after this plan's Task 1 is on `FenixV3`, so the two plans can land in either order.
2. ~~Rule globs~~ (Covered by plan 01: its fenix-data.md globs are final and include these files.) Plan 12 still runs `ClaudeContextBudgetTests` (per-file load: FO code about 13,800, these tests about 22,600, under 30,000).
3. **Optional, owner's choice (O4 alternative):** make the Line-up and Landing *"Cabin: READY"* lines delivered by a read-only `FO_CABIN_READY` check (Skip on timeout; 30 s at line-up, before `LU_CLEARANCE`; 20 s in Landing, before the gear check that FO-13 keeps last) instead of by the CALL ALL step. It needs, together: the steps in `FenixFlowDefinitions.cs`; `LU_CABIN` linking only itself; `A320FamilyParityTests` entries (`KnownStepDivergences` for both new steps, `KnownStepLinkDivergences` for `LU_CABIN`, reason: the Fenix v3 publishes the purser's cabin ready and a call no longer sets it; the A32NX publishes none); and edits to the shared tests `A320FamilyReadbackLinkTests.cs:100`, `A320FamilyReadbackLinkBehaviourTests.cs:41` and `A320FamilyTaxiAndRunwayEntryFlowTests.cs:44`, which pin the Fenix's Line-up steps and the `LU_CABIN` to `LUC_CABIN` link. Set the timeouts from in-sim check 2.
4. ~~Also seen, plan 02's file~~ Resolved: plan 02 Task 9 set `NotSetBelow = 1` on `N_MISC_PERF_TO_V1/VR/V2` (owner answer 16; checked on `FenixV3` 2026-10-22).
5. **Docs (`docs/first-officer.md`):** line 802 names the APU AVAIL lamp `_L` (the code and the 2026-09-04 measurement say `_U`, spec 2.4); add the v3 result of in-sim check 1. Replace "stock N2" wording for the Fenix (around line 76) with the Fenix's own N2 and the stock fallback. Line 8 makes two claims that are now wrong: that the Fenix exposes no V1/VR/V2 L:var outside the MCDU display (the FO reads `N_MISC_PERF_TO_V1/VR/V2`), and that baro STD is a directly readable per-side state written as 1/0 (it is a push/pull counter; plan 02 steps it, this plan reads the QNH light).
6. **For plan 01, information only:** `system.numerical.N_CONFIG_AIRCRAFT_TYPE`, `_ENGINETYPE` and `_ACT_COUNT` read `""` on the data service today, so variant detection must stay on the L:vars, as plan 01 plans.
7. **Not changed, for the record:** `Forms/FirstOfficer/FirstOfficerWindows.cs:38` could pass the definition to `FenixFoProfile` explicitly (as the FBW rows do); not needed, since `SimConnectManager.CurrentAircraft` serves.
8. **Owner decides, after in-sim check 2: a "Cabin: READY" line can read Complete before the purser reports.** Both lines are Reminders (not auto-detected), and both get ticked whatever the purser reported: the Line-up cabin call step links `LUC_CABIN`, so a sent call ticks it (FO-6, `FenixFlowDefinitions.cs:419-420` → `FlowManager.cs:388-389`), and a finished Line-up or Landing flow ticks and latches every line of its group (FO-7, `ChecklistManager.cs:229-241`), which is the only way `LDC_MEMO_CABIN` gets ticked. So Checklist status can say *"Cabin: READY, not reported — Complete"*. Unlinking the call alone would not change it. Owner answer 15 (live value only; request 3 dropped) was given before this was known, so it is worth asking again: keep it, or adopt request 3's read-only check, which also needs the flag to clear after departure (check 2) or the Landing check would pass on the departure report.
9. **One file outside "File structure":** `FenixFlightPhaseMonitor.cs` (plan 05's, comment only): its class comment described the baro STD write as a direct state write; it now describes plan 02's counter step (`FenixActionExecutor.SetBaroStdBoth`).
10. **Left as the plan says (O3), for the owner:** on an A321 the Preflight checklist line `PF_PACKFLOW` keeps its `CheckAction`; a hand-tick while the state is still unread would write the knob the A321 does not have (harmless: the name reads 1 and nothing moves).

**Done (2026-10-22):** Tasks 1-8 plus a final-review fix wave, each reviewed, on `FenixV3`; full suite green (14,620 at the fix wave, baseline 14,558). Nothing was checked in the sim.

**In-sim checks still needed after merge:** the three above, with their recorded numbers: the APU AVAIL legend on v3, the IAE start time, the Fenix-versus-stock N2 crossings and any `source stock` line during the crank, the purser's cabin-ready delay, and whether the cabin-ready flag clears after departure.
