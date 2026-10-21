# Fenix v3 Foundation Implementation Plan (plan 01)

**Claimed by:** nobody yet · **Status:** not started

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the seams that let plans 02-11 work in parallel without touching each other's files: partial-class hooks on the Fenix definition, one shared client for the Fenix data service, variant detection (which Fenix is loaded), and a "Fenix windows" list opened by one new hotkey.

**Architecture:** `FenixA320Definition` becomes `partial`; its extension points (variables, panel controls, panel structure, panel display list and text, UI writes, updates, visibility, background start and stop) call optional `partial void` methods declared in a new `FenixA320Definition.Hooks.cs`, which later plans implement in their own partial files. A new `FenixDataService` (HTTP + one `graphql-transport-ws` socket to 127.0.0.1:8083) is owned by the definition and disposed on aircraft switch. A pure `FenixVariant` record, fed by eight config L:vars the foundation registers, is exposed as `FenixA320Definition.Variant` with a `VariantChanged` event, so panels (06), the First Officer (05) and display reads (04) share one answer. A static partial `FenixWindowRegistry` lists the windows later plans add; `FenixWindowsForm` shows the list on Ctrl+Shift+F (input mode). No pilot-visible behaviour changes except the new key, which says "No Fenix windows yet" until another plan registers one.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, Newtonsoft.Json (already used by `FenixMCDUService`), `System.Net.WebSockets.ClientWebSocket`, xUnit.

**Spec:** `docs/fenix-v3/spec.md`; index and file ownership: `docs/fenix-v3/plans/00-index.md`. The `FenixV3` branch already holds `docs/fenix-v3/`; start from its head.

## Global Constraints

- Data service base: `http://127.0.0.1:8083/graphql` and `ws://127.0.0.1:8083/graphql`, REST base `http://127.0.0.1:8083`; never `localhost` (A320-40).
- WebSocket subprotocol `graphql-transport-ws`: `connection_init` → expect `connection_ack`; `subscribe` with `id`; answer every server `ping` with `pong` (the service pings about every 15 s).
- Read query shape: `query { dataRef { r0: dataRef(name: "<name>") { value } … } }`. Subscription: `subscription ($names: [String!]!) { dataRefs(names: $names) { name value } }`. Writes: `mutation ($name: String!, $value: <Int!|Boolean!|Float!|String!>) { dataRef { write<Int|Bool|Float|String>(name: $name, value: $value) } }`. Settings: `query { settings { softwareVersion state { settings { key category value possibleValues source type writable } } } }`.
- An unknown name reads `""`. It is never 0, false or "off".
- Never query the schema (introspection, `?sdl`, the IDE page); never call `navigraph { … }`.
- New hotkey: input mode, Ctrl+Shift+F (`MOD_CONTROL | MOD_SHIFT`, `0x46`), action `HotkeyAction.ShowAircraftWindows`, hotkey id = the next unused `HOTKEY_*` id above 9253 (grep before choosing).
- No change to any existing behaviour: the seams must leave the Fenix variables, panels, UI writes, announcements and hotkeys exactly as they were.

## Review Focus

- The Fenix is not loaded or the gateway is down: every read returns `EmptyOrUnknown`, every write returns false, nothing throws, the subscription retries quietly (Task 3 test with a throwing `HttpMessageHandler`).
- A name containing `"` or `\` in a batched read must not break the query (Task 2 test).
- A float written under a comma-decimal culture (de-DE) must go out as `1.5`, never `1,5` (Task 2 test).
- Ctrl+Shift+F on a non-Fenix aircraft must say so, not stay silent (Task 4 step).
- Switching aircraft with a Fenix window open must close the window and dispose the data service (Task 4 manual check; tracked windows plus `OnSwitchedAway`).

---

### Task 1: Partial-class seams on the Fenix definition

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (class declaration line 11; end of `BuildVariables` before `return variables;` about line 8502; `GetPanelStructure` line 8505 to its closing `};` about line 8575; `BuildPanelControls` from line 8577 to its closing `};` about line 9327; `GetPanelDisplayVariables` line 9329; top of `HandleUIVariableSet` line 9350; top of `ProcessSimVarUpdate` line 12100; top of `HandleHotkeyAction` line 13195)
- Create: `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDefinitionSeamTests.cs`

**Interfaces:**
- Produces (declared in `FenixA320Definition.Hooks.cs`, implemented by plan 06 in `FenixA320Definition.Variants.cs`):
  - `partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition> variables);`
  - `partial void AddVariantPanelControls(Dictionary<string, List<string>> panels);`
  - `partial void AddVariantPanelStructure(Dictionary<string, List<string>> structure);` (sections to their panel lists, after the main file builds them; plan 06 adds the "Clock" panel)
  - `partial void AddVariantPanelDisplayVariables(Dictionary<string, List<string>> displayVariables);`
  - `partial void TryVariantDisplayOverride(string varKey, double value, ref string? displayText);` (sets `displayText` to show that text in a panel display; leaves it null otherwise)
  - `partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled);`
  - `partial void OnVariantSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled);`
  - `partial void IsVariantControlVisible(string varKey, ref bool visible);`
  - `partial void OnFenixSwitchedAway();` (only one implementation is allowed by C#, so plan 06 owns it)
  - Background seams, one pair per plan so each implements its own in its own partial file: `partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer);` and `partial void StopCabinBackground();` (plan 08, `FenixA320Definition.Cabin.cs`); `partial void StartEngineBackground(Accessibility.ScreenReaderAnnouncer announcer);` and `partial void StopEngineBackground();` (plan 09, `FenixA320Definition.Engines.cs`, only if the owner wants engine announcements outside the Engines window). The foundation calls each Start once per definition instance, on the UI thread, from the first `ProcessSimVarUpdate` after a connection (a private `_backgroundStarted` flag in the hooks file, reset in `OnSwitchedAway`), and each Stop in `OnSwitchedAway` before it disposes the data service.
- Produces (in `FenixA320Definition.Hooks.cs`):
  - `public override bool IsPanelControlVisible(string varKey, SimConnect.SimConnectManager? simConnect)`: starts `true`, calls `IsVariantControlVisible`, returns the result.
  - `public override void OnSwitchedAway()`: calls `OnFenixSwitchedAway()`, then disposes the data service (Task 3) and sets it to null.
  - `public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)`: `string? text = null; TryVariantDisplayOverride(varKey, value, ref text); displayText = text ?? ""; return text != null;`. The Fenix has no override today, so this is the only one: no other plan adds one.

- [ ] **Step 1: Prove "nothing moved" for this task only, then write the failing test.** On the unmodified code note `new FenixA320Definition().GetVariables().Count`, `GetPanelControls().Count` and the sum of all panel list lengths; re-check the same three numbers by hand after Step 3 and state them in the commit message. Do NOT commit a test that pins totals: plans 02 and 06 add and remove definitions in parallel, so a pinned count would turn every other plan's suite red. Write `FenixDefinitionSeamTests` with facts that survive those plans: `IsPanelControlVisible("S_OH_ELEC_BAT1", null)` is true; `TryGetDisplayOverride("S_OH_ELEC_BAT1", 1, out _)` is false; `GetVariables()` still contains a sample of long-standing keys (`S_OH_ELEC_BAT1`, `N_FCU_SPEED`, `S_FC_FLAPS`, `I_MIP_MASTER_WARNING_CAPT`); every `GetPanelControls()` entry names a key in `GetVariables()`; and a source scan that `FenixA320Definition.cs` declares `public partial class FenixA320Definition` and calls each of `AddVariantVariables(`, `AddVariantPanelControls(`, `AddVariantPanelStructure(`, `AddVariantPanelDisplayVariables(`, `HandleVariantUIVariableSet(`, `OnVariantSimVarUpdate(`.
- [ ] **Step 2: Run it; the source-scan fact fails.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FenixDefinitionSeamTests`. Expected: 1 failure (partial and the calls), the others pass.
- [ ] **Step 3: Add the seams.** Make the class `partial`. In `BuildVariables` call `AddVariantVariables(variables);` immediately before `return variables;` (after the readout twins). Turn `BuildPanelControls`'s `return new Dictionary<…> { … };` into `var panels = new Dictionary<…> { … }; AddVariantPanelControls(panels); return panels;`, and do the same in `GetPanelStructure` (`var structure = …; AddVariantPanelStructure(structure); return structure;`) and `GetPanelDisplayVariables` (`var displayVariables = …; AddVariantPanelDisplayVariables(displayVariables); return displayVariables;`). At the top of `HandleUIVariableSet` (before the `try`): `bool handled = false; HandleVariantUIVariableSet(varKey, value, varDef, simConnect, announcer, ref handled); if (handled) return true;`. In `ProcessSimVarUpdate` right after the `lastAnnouncer` line: `EnsureFenixBackground(announcer);` (in the hooks file: starts the background seams once, see Interfaces), then the same handled pattern with `OnVariantSimVarUpdate`. Create `FenixA320Definition.Hooks.cs` with the declarations and the three overrides above, a class-level comment naming which plan implements each seam.
- [ ] **Step 4: Run the test and the whole suite; everything passes, and the three hand-checked numbers are unchanged.** Same command without `--filter`.
- [ ] **Step 5: Commit.** `git add` the three files; message `feat(fenix): partial-class seams for the v3 plans`.

### Task 2: Data-service values, queries and settings (pure)

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDataValue.cs`, `MSFSBlindAssist/Services/FenixDataQueries.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixDataQueriesTests.cs`

**Interfaces:**
- Produces:
  - `public enum FenixValueKind { EmptyOrUnknown, Null, Number, Bool, Text, Array, Object }`
  - `public readonly record struct FenixDataValue(FenixValueKind Kind, double Number, bool Bool, string? Text, string RawJson)` with `public static FenixDataValue FromJson(JToken? token)`, `public static readonly FenixDataValue Unknown`, and `public bool IsKnown => Kind is not (FenixValueKind.EmptyOrUnknown or FenixValueKind.Null);`
  - `public sealed record FenixSetting(string Key, string Category, string? Value, IReadOnlyList<string> PossibleValues, string? Type, string? Source, bool Writable)`
  - `public static class FenixDataQueries` with:
    - `string BuildReadMany(IReadOnlyList<string> names)`: aliases `r0`, `r1`, … in order
    - `IReadOnlyDictionary<string, FenixDataValue> ParseReadMany(JObject? data, IReadOnlyList<string> names)`: missing or failed entries map to `Unknown`
    - `string BuildWriteBody(string kind, string name, object value)` where kind is `"Int"`, `"Bool"`, `"Float"` or `"String"`: the full JSON request body (query + variables)
    - `IReadOnlyList<FenixSetting> ParseSettings(JObject? data)` and `string? ParseSoftwareVersion(JObject? data)`
    - `const string SettingsQuery`, `const string SubscriptionQuery`

- [ ] **Step 1: Write the failing tests.**
  - `FromJson_reads_each_kind`: `""` → `EmptyOrUnknown`; `62.3` → `Number` 62.3; `true` → `Bool`; `"A321"` → `Text`; `[false,true]` → `Array` with `RawJson` `[false,true]`; JSON `null` → `Null`; a C# `null` token → `EmptyOrUnknown`.
  - `BuildReadMany_escapes_quotes_and_backslashes`: names `a"b` and `c\d` produce a query containing `r0: dataRef(name: "a\"b")` and `r1: dataRef(name: "c\\d")`.
  - `ParseReadMany_maps_aliases_back_to_names`: data `{"dataRef":{"r0":{"value":276},"r1":{"value":""}}}` for names `system.numerical.N_ELEC_VOLT_BAT_1`, `bogus` → 276 (Number) and `EmptyOrUnknown`; a names list longer than the data → extra names `Unknown`.
  - `BuildWriteBody_float_is_invariant_under_de_DE`: with `CultureInfo.CurrentCulture = new CultureInfo("de-DE")`, `BuildWriteBody("Float", "x", 1.5)` contains `"value":1.5` and no `1,5`.
  - `ParseSettings_reads_a_row`: a `settings.state.settings` array with one row `{"key":"weight_unit","category":"Aircraft","value":"Pounds","possibleValues":["Kilograms","Pounds"],"source":"Livery","type":"Enum","writable":true}` gives that `FenixSetting`; `ParseSoftwareVersion` of `{"settings":{"softwareVersion":"3.0.0.7426"}}` gives `"3.0.0.7426"`.
- [ ] **Step 2: Run them; they fail to compile.** `dotnet test … --filter FenixDataQueriesTests`.
- [ ] **Step 3: Implement both files.** Build query strings with a single escaping helper (JSON string rules); build write bodies with `JsonConvert.SerializeObject` so numbers are invariant. Use the exact shapes in Global Constraints.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `feat(fenix): data-service values, queries and settings parsing`.

### Task 3: FenixDataService client

**Files:**
- Create: `MSFSBlindAssist/Services/FenixDataService.cs`
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs` (add the owned instance)
- Test: `tests/MSFSBlindAssist.Tests/FenixDataServiceClientTests.cs`

**Interfaces:**
- Consumes: Task 2's `FenixDataValue`, `FenixDataQueries`, `FenixSetting`.
- Produces:
  - `public sealed class FenixDataService : IDisposable`
    - `public FenixDataService(HttpMessageHandler? handler = null)` (the handler is for tests)
    - `Task<FenixDataValue> ReadAsync(string name, CancellationToken ct = default)`
    - `Task<IReadOnlyDictionary<string, FenixDataValue>> ReadManyAsync(IReadOnlyList<string> names, CancellationToken ct = default)`
    - `Task<bool> WriteIntAsync(string name, int value, CancellationToken ct = default)`, likewise `WriteBoolAsync(bool)`, `WriteFloatAsync(double)`, `WriteStringAsync(string)`: true only on HTTP 200 with no `errors`
    - `Task<JObject?> QueryAsync(string query, object? variables = null, CancellationToken ct = default)`: returns `data`, or null on any failure or `errors`
    - `Task<string?> GetRestAsync(string path, CancellationToken ct = default)` and `Task<string?> PostRestAsync(string path, string jsonBody, CancellationToken ct = default)`: `path` starts with `/` (e.g. `/fenix/failures/manual`); the POST sends `Content-Type: application/json` (the EFB's own `saveManual` call relies on it); both return the response body, or null on failure
    - `Task<IReadOnlyList<FenixSetting>> GetSettingsAsync(CancellationToken ct = default)` (empty on failure) and `Task<string?> GetSoftwareVersionAsync(CancellationToken ct = default)`
    - `IDisposable Subscribe(IReadOnlyList<string> names, Action<string, FenixDataValue> onChange)`: one shared socket for all subscriptions (each its own `id`); connects on first use; reconnects with backoff 3/6/12/30 s and re-sends every live subscription; disposing the returned handle sends `complete` and drops it; **callbacks run on a pool thread**
    - `void Dispose()`: cancels the socket loop, completes all subscriptions, never throws
  - In `FenixA320Definition.Hooks.cs`: `public FenixDataService DataService => _dataService ??= new FenixDataService();` (lazy); `OnSwitchedAway` disposes it (Task 1).

- [ ] **Step 1: Write the failing tests** (pure, with a fake handler; no network):
  - `ReadAsync_returns_unknown_when_the_service_is_down`: a handler that throws `HttpRequestException` → `ReadAsync("x")` returns `Kind == EmptyOrUnknown`, `WriteIntAsync` returns false, `GetSettingsAsync` returns empty, `QueryAsync` returns null. No exception escapes.
  - `ReadAsync_reads_the_value`: a handler answering `{"data":{"dataRef":{"r0":{"value":59.3}}}}` → Number 59.3.
  - `Write_returns_false_when_the_service_reports_errors`: handler answers 200 with `{"errors":[{"message":"x"}]}` → false.
  - `Never_addresses_the_service_by_name`: source scan of `FenixDataService.cs` (pattern of `FenixDataServiceAddressTests`): no `(ws|http)s?://localhost`; contains `"http://127.0.0.1:8083"` and `"ws://127.0.0.1:8083/graphql"`.
- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement `FenixDataService`.** Pattern after `FenixMCDUService` (receive loop, `connection_init`/`connection_ack`, ping→pong, backoff), but multiplexed: a dictionary of live subscriptions keyed by id; each `next` message carries `id` and `payload.data.dataRefs {name, value}`. HTTP via one `HttpClient` (10 s timeout). Log through `Log.Debug("FenixData", …)` (CORE-15); no spoken output.
- [ ] **Step 4: Run them; all pass. Then the whole suite.**
- [ ] **Step 5: Live check (sim up, Fenix loaded; reads only).** With a scratch harness or the debugger: `ReadManyAsync(["system.numerical.N_FCU_SPEED","aircraft.engines.1.n2","bogus.name"])` gives a number, a number and `EmptyOrUnknown`; `Subscribe(["aircraft.engines.1.n2"], …)` delivers repeated values within a few seconds; `GetSoftwareVersionAsync()` gives `3.0.0.7426` or later. Write nothing. If the sim is not up, stop and ask the owner to load the Fenix.
- [ ] **Step 6: Commit.** `feat(fenix): shared data-service client owned by the Fenix definition`.

### Task 4: Variant detection

**Files:**
- Create: `MSFSBlindAssist/Aircraft/FenixVariant.cs`
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs` (registration, state, event)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (two one-line calls: in `BuildVariables` immediately before Task 1's `AddVariantVariables(variables);` add `AddConfigVariables(variables);`; in `ProcessSimVarUpdate` immediately before Task 1's `OnVariantSimVarUpdate` block add `if (TryApplyConfigValue(varName, value)) return true;`)
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantTests.cs`

**Interfaces:**
- Produces (in `FenixVariant.cs`, namespace `MSFSBlindAssist.Aircraft`):
  - `public enum FenixAircraftType { Unknown = 0, A318 = 1, A319 = 2, A320 = 3, A321 = 4 }`
  - `public enum FenixEngineFamily { Unknown = -1, Cfm56 = 0, IaeV2500 = 1, CfmLeap = 2, PwGtf = 3 }`
  - `public sealed record FenixVariant(FenixAircraftType Type, FenixEngineFamily Engine, int? ActCount, bool? HasIsis, bool? HasDcdu, bool? IsNeo, bool? HasGnadirs, bool? HasCargoHeat = null)` (the last parameter is optional so every seven-argument construction in the other plans still compiles) with `public static FenixVariant Unknown`, `public bool IsKnown` (Type and Engine both known), `public string Describe()` (e.g. "Fenix A321, IAE engines, 2 extra tanks"; parts that are unknown are left out; cargo heat is never mentioned), and `public FenixVariant With(string configVar, double value)` returning the record with that one field applied (unknown names return `this`).
  - `public static class FenixConfigVars` with the eight names as consts: `AircraftType = "N_CONFIG_AIRCRAFT_TYPE"`, `EngineType = "N_CONFIG_ENGINETYPE"`, `ActCount = "N_CONFIG_ACT_COUNT"`, `Standby = "B_CONFIG_STDBY"`, `Dcdu = "B_CONFIG_DCDU"`, `IsNeo = "B_CONFIG_IS_NEO"`, `Adirs = "B_CONFIG_ADIRS"`, `CargoHeat = "B_CONFIG_CARGOMEAT"` (the Fenix's own spelling), and `IReadOnlyList<string> All`.
- Produces (on `FenixA320Definition`): `public FenixVariant Variant { get; private set; } = FenixVariant.Unknown;`, `public event EventHandler<FenixVariant>? VariantChanged;` (raised on the UI thread, only when the record actually changes), `private void AddConfigVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)`, `private bool TryApplyConfigValue(string varName, double value)`.
- Value meanings (spec section 3): aircraft type 3 = A320 and 4 = A321 measured, 0-4 follow the Fenix settings order Unknown/A318/A319/A320/A321 (Fenix's own LOD.js 1/2/3 mapping is wrong; do not use it); engine 0 = CFM and 1 = IAE measured, 2/3 from the settings order (unmeasured); ACT count 2 measured; `B_CONFIG_STDBY` 0 = ISIS (measured); `B_CONFIG_DCDU` 1 = fitted (measured); `B_CONFIG_IS_NEO` 0 = CEO (measured); `B_CONFIG_ADIRS` 0 = GNADIRS keypad, 1 = plain ADIRS (from the model's visibility template and the settings order; read it live in Step 5); `B_CONFIG_CARGOMEAT` 0 = no aft cargo heat (read 0 on 2026-10-21 on the A321 IAE whose settings say `cargo_heat_enabled` False; the model shows the cargo heat controls, node `CargoHeat_CargoHeat`, for any non-zero value and a blank vent plate for 0). Any other value maps to Unknown / null, except that any non-zero `B_CONFIG_CARGOMEAT` means cargo heat is fitted.

- [ ] **Step 1: Write the failing tests.** `Unknown_until_type_and_engine_known`; `With_maps_the_measured_values`: `Unknown.With("N_CONFIG_AIRCRAFT_TYPE", 3)` → A320, `…4` → A321, `With("N_CONFIG_ENGINETYPE", 0)` → Cfm56, `1` → IaeV2500, `With("N_CONFIG_ACT_COUNT", 2).ActCount == 2`, `With("B_CONFIG_STDBY", 0).HasIsis == true`, `With("B_CONFIG_ADIRS", 0).HasGnadirs == true`, `With("B_CONFIG_ADIRS", 1).HasGnadirs == false`, `With("B_CONFIG_CARGOMEAT", 0).HasCargoHeat == false`, `With("B_CONFIG_CARGOMEAT", 1).HasCargoHeat == true`; `Out_of_range_is_unknown`: type 7 → Unknown, engine 9 → Unknown, NaN → unchanged; `Describe_reads_naturally`: A321 + IAE + ACT 2 → "Fenix A321, IAE engines, 2 extra tanks"; A320 + CFM + ACT 0 → "Fenix A320, CFM engines"; `Definition_registers_the_config_vars`: every name in `FenixConfigVars.All` is in `new FenixA320Definition().GetVariables()` with `ExcludeFromMonitorManager == true` and not announced.
- [ ] **Step 2: Run them; they fail to compile.**
- [ ] **Step 3: Implement.** Register the eight as `Continuous`, `IsAnnounced = false`, `ExcludeFromMonitorManager = true` (VAR-9), units "number", unique `DisplayName`s ("Fenix config: aircraft type" …, VAR-1). `TryApplyConfigValue` updates `Variant` via `With`, raises `VariantChanged` when the record changed, returns true for the eight names (consumed silently) and false otherwise.
- [ ] **Step 4: Run them and the whole suite; all pass.**
- [ ] **Step 5: Live check (sim up; reads only).** With the app connected on the Fenix profile, log `Variant.Describe()` on change (`Log.Debug("Fenix", …)`): the loaded aircraft is described correctly within about 2 s of connecting, and `B_CONFIG_ADIRS` reads the value the Fenix settings' `adirs_type` implies. Record the ADIRS reading in the spec's section 3.
- [ ] **Step 6: Commit.** `feat(fenix): variant detection from the config L:vars`.

### Task 5: "Fenix windows" list and its hotkey

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowsForm.cs`
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs` (enum `HotkeyAction` at about line 1295: add `ShowAircraftWindows` at the end; a `HOTKEY_*` id const; input-mode registration beside `HOTKEY_SHOW_DCDU` at line 979; the input-mode dispatch `switch` at about line 563; the matching `UnregisterHotKey` in the input-mode teardown)
- Modify: `MSFSBlindAssist/MainForm.Hotkeys.cs` (`offlineActions` set at line 25: add `HotkeyAction.ShowAircraftWindows`; the post-aircraft `switch`: a `case HotkeyAction.ShowAircraftWindows:` that announces "This aircraft has no extra windows." for any aircraft that did not handle it)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (top of `HandleHotkeyAction`: `if (action == HotkeyAction.ShowAircraftWindows) { ShowFenixWindows(simConnect, announcer, parentForm); return true; }`)
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs` (add `ShowFenixWindows` and `ShowFenixWindow<T>`)
- Modify: `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt` (one line in the input-mode section: "Ctrl+Shift+F: Fenix windows (cabin, engines, failures, options)")
- Test: `tests/MSFSBlindAssist.Tests/FenixWindowRegistryTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record FenixWindowContext(FenixA320Definition Definition, FenixDataService Data, SimConnect.SimConnectManager SimConnect, Accessibility.ScreenReaderAnnouncer Announcer, System.Windows.Forms.Form Owner);`
  - `public sealed record FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open);`
  - `public static partial class FenixWindowRegistry` with `public static IReadOnlyList<FenixWindowEntry> Build()`, which calls, in this order, the optional seams `static partial void AddEngineWindows(List<FenixWindowEntry> entries);` (plan 09, `FenixWindowRegistry.Engines.cs`), `AddCabinWindows` (plan 08, `.Cabin.cs`), `AddFailureWindows` (plan 10, `.Failures.cs`), `AddDocumentWindows` (plan 07, `.Documents.cs`), `AddOptionsWindows` (plan 11, `.Options.cs`).
  - On `FenixA320Definition`: `public void ShowFenixWindow<T>(Func<T> factory, Action<T> show) where T : System.Windows.Forms.Form => ShowTrackedWindow(factory, show);` (so registry entries open tracked windows that close on aircraft switch) and `private void ShowFenixWindows(SimConnectManager, ScreenReaderAnnouncer, Form)`.
  - `FenixWindowsForm(IReadOnlyList<FenixWindowEntry> entries, FenixWindowContext context)` with `public void ShowForm()`: title "Fenix windows"; a `ListBox` of `Name` items (each item's accessible text "Name. Description"); Enter or double-click opens the selected entry and closes the list; Escape closes; with no entries the list is replaced by a read-only `TextBox` reading "No Fenix windows yet." (VAT-13).

- [ ] **Step 1: Write the failing test.** `Build_returns_unique_names`: `FenixWindowRegistry.Build()` has no duplicate `Name` (with no plan partials present it is empty, which passes; the test keeps later plans honest). `Build_is_stable`: two calls give the same names in the same order.
- [ ] **Step 2: Run it; it fails to compile.**
- [ ] **Step 3: Implement the registry, the form and the hotkey path** as in Files and Interfaces. Exit input hotkey mode before showing the form (`hotkeyManager.ExitInputHotkeyMode()` is done by MainForm only for its own windows; the definition receives `hotkeyManager` in `HandleHotkeyAction` and must call it).
- [ ] **Step 4: Run the test and the whole suite; all pass. Build the solution.**
- [ ] **Step 5: Manual check (app + sim, owner present).** On the Fenix: input mode then Ctrl+Shift+F opens "Fenix windows" reading "No Fenix windows yet."; Escape closes it and focus returns. On another aircraft profile: Ctrl+Shift+F says "This aircraft has no extra windows." Switch aircraft with the list open: it closes.
- [ ] **Step 6: Commit.** `feat(fenix): Fenix windows list on Ctrl+Shift+F`.

### Task 6: Rule file, full text and CLAUDE.md row

**Files:**
- Create: `.claude/rules/fenix-data.md`, `docs/invariants/fenix-data.md`
- Modify: `CLAUDE.md` (the `a32nx.md` row of the "Where things live" table: add `fenix-data` to its rule files)
- Test: existing `ClaudeContextBudgetTests` (no new test file)

**Interfaces:**
- Produces: rule prefix `FNX`, rules FNX-1 to FNX-3 (later plans propose FNX-4 onward through plan 05).

- [ ] **Step 1: Write the rule file.** Frontmatter `paths:`, final for the whole branch so plans 02-11 never need to widen it: `MSFSBlindAssist/Services/Fenix*.cs`, `MSFSBlindAssist/Aircraft/FenixA320*.cs`, `MSFSBlindAssist/Aircraft/FenixVariant*.cs`, `MSFSBlindAssist/Forms/Fenix/**`, `MSFSBlindAssist/FirstOfficer/Fenix/**`, `MSFSBlindAssist/Resources/fenix-*.js` (plan 03's EFB layer), `tests/MSFSBlindAssist.Tests/**/*Fenix*.cs`. (These overlap a32nx-fenix.md's and first-officer*.md's globs on purpose; no rule appears in two files, so nothing loads twice. Run `ClaudeContextBudgetTests`, including its per-file load budget on `FenixA320Definition.cs`.) Heading and one background line naming docs/a32nx.md. Three rules, each one line under 400 chars in the repo format, naming code in backticks that these globs' files declare:
  - `[FNX-1]` A Fenix data-service read of an unknown name returns `""`: `FenixDataValue.FromJson` maps it to `EmptyOrUnknown`, and no caller may treat that as 0, false or off.
  - `[FNX-2]` `FenixDataService` callbacks run on a pool thread: a form marshals with a try/catch `SafeBeginInvoke` (A380C-6); the service never speaks.
  - `[FNX-3]` Fenix data-service names come only from open sources (the gateway's own EFB/FAP bundles, names already in the app, HubHop): never introspect the schema or guess a name; `FenixDataService` never calls `navigraph`.
- [ ] **Step 2: Write `docs/invariants/fenix-data.md`** with `## FNX-1` … `## FNX-3`: the measurement (2026-10-21, Fenix 3.0.0.7426) and why, from the spec.
- [ ] **Step 3: Add `fenix-data` to CLAUDE.md's a32nx.md row.**
- [ ] **Step 4: Run `ClaudeContextBudgetTests` and the whole suite.** Expected: green. If a budget rule fails, follow its message (it says what to do).
- [ ] **Step 5: Commit.** `docs(rules): fenix-data rule file (FNX-1..3)`.

### Task 7: Land the base

**Files:** `.github/workflows/tests.yml` (the `push:` branch list and the comment above it), `docs/fenix-v3/plans/00-index.md` (the base-commit line).

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and the full test suite: both green.
- [ ] **Step 2: Tests on every FenixV3 push.** In `.github/workflows/tests.yml`, change `push:` `branches: [main]` to `branches: [main, FenixV3]`, and the comment above it to "Post-merge safety net: also run on direct pushes to main, and on every push to the FenixV3 work branch (plan 12 removes FenixV3 before its pull request)." Nothing else in the file changes. GitHub runs a push with the workflow file inside the pushed commit, so this takes effect on the branch alone; the test job has no pull-request-only step. Commit `ci: run the tests on every push to FenixV3`. (Pushing a workflow edit works with `git push`; only `gh`'s token lacks the workflow scope.)
- [ ] **Step 3:** Record the `FenixV3` HEAD SHA in `docs/fenix-v3/plans/00-index.md` under "Base commit for parallel work" and commit that one line. The owner says when the branch is pushed; the first push to FenixV3 runs the tests on GitHub (check the run is green).
- [ ] **Step 4:** Hand back: SHA, files, test count, the live-check results (Task 3 Step 5, Task 4 Step 5), and anything a later plan must know.

## Handoff to plan 12

- Pilot-facing: input mode, Ctrl+Shift+F opens "Fenix windows" (the guide's Fenix chapter and hotkey appendix).
- Rules added: FNX-1 to FNX-3 (`.claude/rules/fenix-data.md`).
- In-sim checks: Task 5 Step 5 (the list opens and closes; another aircraft says it has no extra windows); Task 4 Step 5 (variant described correctly on an A320 and an A321).
