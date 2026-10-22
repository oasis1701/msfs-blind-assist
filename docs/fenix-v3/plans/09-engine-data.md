# Fenix Engine Data Implementation Plan (plan 09)

**Claimed by:** danielw97 · **Status:** done (code, reviews and every in-sim check passed 2026-10-22)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An accessible "Engines" window, listed in the "Fenix windows" list (input mode, Ctrl+Shift+F), that shows the Fenix's OWN engine values for both engines (running, N1, EGT, N2, fuel flow, oil quantity, pressure and temperature) plus TAT and OAT, refreshed calmly once a second.

**Architecture:** Pure logic in `MSFSBlindAssist/Services/FenixEngine*.cs`: the 18 data names, a thread-safe snapshot the data-service callback writes into, a formatter that turns a snapshot into rows and a status line, a display hold that stops rounding jitter, and a watcher that turns the Fenix's `running` flag into "Engine 1 running" / "Engine 1 stopped". The window (`MSFSBlindAssist/Forms/Fenix/FenixEnginesForm.cs`) subscribes through plan 01's `FenixDataService`, never marshals per update (values stream at 30-45 Hz), and reads the snapshot on a 1 s WinForms timer into a `DisplayListBox`, the app's standard live display list. `FenixWindowRegistry.Engines.cs` implements plan 01's `AddEngineWindows` seam.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, plan 01's `FenixDataService` (Newtonsoft.Json), xUnit.

**Spec:** `docs/fenix-v3/spec.md`, sections 3 (IAE: "Stock engine SimVars are worse on the IAE") and 4.1. Index, ownership and workflow: `docs/fenix-v3/plans/00-index.md`. Foundation interfaces: `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

**My calls** (the implementer follows these unless the owner changes them before Task 2):

1. **Rows, one value each, both engines interleaved by parameter**, in the order the Airbus screens show them: the E/WD (N1, EGT, N2, fuel flow), then the SD ENGINE page (oil quantity, pressure, temperature), then TAT and OAT. Engine 1 then engine 2 on adjacent rows, so the two engines compare row by row. Wording follows the PMDG 777 System Display ENG page (`MSFSBlindAssist/Aircraft/PMDG777Definition.SystemDisplay.cs:85-97, 116-139`: "Engine 1 N1", "percent", "degrees C"), the closest existing presentation; HS787's EICAS text (`MainForm.AircraftSwitch.cs:731-754`) uses "%" and "C" and is the shorter alternative.
2. **Precision:** N1 and N2 0.1 %, EGT 1 °C, fuel flow 1 unit per hour, oil quantity 0.1, oil pressure 1, oil temperature 1, TAT/OAT 1 °C. Invariant culture, ASCII minus, never "-0". Unknown is `--` (as P777-9 renders an unread PMDG field), never 0.
3. **Calm refresh:** a 1 s WinForms timer; `DisplayListBox.SetLines` rewrites only changed rows; a display hold keeps a shown value until the raw value moves 0.6 of a display step away, so a value sitting on a rounding edge (116.49/116.51) does not rewrite its row every second. The refresh never calls `Focus` or `Activate`. As in every `DisplayListBox` window (E/WD, HS787 EICAS), a FOCUSED row whose value changes is re-read by the screen reader; during a start the focused N2 row reads about once a second. In cruise the hold keeps rows still.
4. **Fuel flow unit** = the Fenix's own `weight_unit` setting (Kilograms or Pounds; `docs/fenix-v3/evidence/settings.tsv:141` reads Pounds on the owner's PC; the Fenix labels it "Select the unit for weight values throughout the aircraft", `fenix-options-labels.tsv:152`), read when the window opens and on F5. The published value is `ff.kg` (kilograms per hour: 441 against stock 482 kg/h at IAE idle, spec section 3); pounds = kg x 2.2046226218487757 (1 lb = 0.45359237 kg). If the setting cannot be read, kilograms, and the status says so.
5. **Oil values without units** until in-sim check 2 reads the Fenix's own ENGINE page. The live values (quantity 14.4, pressure 150, temperature 118 on the IAE at idle) look like QT, PSI and °C, but nothing open says so; never invent a unit. **Superseded 2026-10-22:** check 2 read QT, PSI and °C on the Fenix's SD ENGINE page; the rows now say "quarts", "psi" and "degrees C" (Q2, Task 5 Step 3).
6. **Speech (CORE-7):** opening, arrowing and Escape speak nothing of ours (the screen reader reads the window and rows). F5 says "Engines refreshed" (precedent: "E W D refreshed", `MSFSBlindAssist/Forms/FbwEwdWindow.cs:85`; "Status display refreshed", `MainForm.Hotkeys.cs:831`). When the first read or an F5 finds no known value: "No engine data from the Fenix. Is the Fenix loaded?" (once per read). **Proposal:** while the window is open, the Fenix's own `running` flag edges are spoken, queued, as "Engine 2 running" / "Engine 2 stopped" (a background change the pilot did not directly trigger; baseline-first, so opening the window says nothing). Built as one removable piece (Task 3) so the owner can drop it.
7. **No vibration row:** `aircraft.engines.{1,2}.vibration.n1` reads `""` on v3 (not published, measured below). No new hotkey (index: windows open from the list).

**Questions only the owner can answer** (none blocks Tasks 1-2):

- Q1. Keep the `running` announcements (window-only, as built), drop them, or ask plan 12 to make them always-on (a definition-owned watcher, with a Ctrl+M entry)? Decide after in-sim check 1, which shows WHEN in a start the Fenix sets `running`. **Answered (owner, 2026-10-22): keep, window-only as built.** Check 1: the Fenix sets `running` when the start is complete (N2 59.2 %, at idle), the same moment its generator came on line; spoken once, not chatty. Always-on stays open for plan 12 (handoff 4).
- Q2. After in-sim check 2: which oil units to show ("quarts", "psi", "degrees C" are the A320 ENGINE page's QT/PSI/°C, if the Fenix's page shows those)? **Answered (owner, 2026-10-22): "quarts", "psi", "degrees C"**, the Fenix SD ENGINE page's QT, PSI and °C.
- Q3. Braille readers: one value per row (as built) or one row per parameter carrying both engines ("N2: 62.2, 62.4 percent")?
- Q4. Output O on the Fenix speaks the sim's stock `AMBIENT TEMPERATURE`. If check 2 shows it differs from the Fenix's OAT row, should O speak the Fenix's value (handoff, not this plan)? **Answered (owner check, 2026-10-22): no change;** Output O matched the window's Outside Air Temperature.

## Global Constraints

- Data names, exactly these 18 and no others: `aircraft.engines.{1,2}.running`, `.n1`, `.egt`, `.n2`, `.ff.kg`, `.oil.quantity`, `.oil.pressure`, `.oil.temperature`, then `aircraft.temperature.tat`, `aircraft.temperature.oat`. All read non-null live on v3 (Evidence below). Never guess or probe another name; never introspect the schema (FNX-3).
- All data-service access goes through plan 01's `FenixDataService` (which owns `127.0.0.1`); this plan opens no socket or `HttpClient` and never reads a stock engine SimVar.
- An unknown value (`FenixDataValue.IsKnown == false`, a wrong kind, NaN, infinity) shows as `--`, never 0, false or "not running" (FNX-1).
- Formatting uses `CultureInfo.InvariantCulture`, `MidpointRounding.AwayFromZero`, an ASCII `-`, and never prints `-0`.
- At most one refresh per second; the refresh never calls `Focus()`, `Activate()` or `BringToFront()`.
- The subscription callback (pool thread) touches only `FenixEngineSnapshot`. No `BeginInvoke`/`Invoke` in the form; if one is ever added it is a try/catch `SafeBeginInvoke` (A380C-6, FNX-2).
- Status text is a read-only `TextBox` (VAT-13). Speech uses `ScreenReaderAnnouncer.Announce` (queued, honours `Suppressed`).
- Files: only `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Engines.cs`, `MSFSBlindAssist/Forms/Fenix/FenixEngines*.cs`, `MSFSBlindAssist/Services/FenixEngine*.cs`, new `tests/MSFSBlindAssist.Tests/*FenixEngine*.cs`. Every public type is prefixed `FenixEngine` so nothing collides with the other window plans at merge.
- Never write to the sim. Reads are fine; a step that needs the live sim says what state the aircraft must be in.

## Review Focus

- **Fenix not loaded, gateway down, or a v2 Fenix** (several names read `""`): every affected row reads `--`, the status says "Waiting for engine values from the Fenix. Is the Fenix loaded?", BA says "No engine data from the Fenix. Is the Fenix loaded?" once, and nothing throws. Tests: Task 1 `Unknown_values_read_as_null_never_zero`, Task 2 `Empty_reading_is_all_dashes`, `Status_waits_before_the_first_value`.
- **Comma-decimal and Swedish minus:** under de-DE 22.448 must read `22.4`, under sv-SE an OAT of -12.4 must read `-12` with an ASCII hyphen (sv-SE's NegativeSign is U+2212), and -0.4 must read `0`. Task 2 `Number_is_invariant_and_never_negative_zero`.
- **A value parked on a rounding edge** must not rewrite (and so re-speak) its focused row every second. Task 2 `Rows_hold_a_jittering_value` and the `FenixEngineDisplayHoldTests`.
- **A data dropout mid-flight** (values turn unknown, then return) must not speak "Engine 1 running" again, and opening the window with engines running says nothing. Task 3 `A_dropout_does_not_respeak`, `First_values_are_silent`.
- **Closing the window or switching aircraft mid-start:** the subscription is disposed and nothing more is spoken. Task 4 source-scan `Engines_window_never_marshals_and_cleans_up` plus Task 4 Step 6's manual check.

## Evidence

- Measured for this plan, 2026-10-21 13:06 local, read-only batched query, Fenix 3.0.0.7426, IAE A321 ("FenixA321 IAE", `engine_family` IaeV2500, `type_code` A321, `weight_unit` Pounds), both engines at idle: engine 1 / engine 2 `running` true / true, `n1` 22.448190078473196 / 22.445335864998796, `n2` 62.212353401252194 / 62.391407703690426, `egt` 475.38836459330855 / 474.14250676655524, `ff.kg` 437.8835188757868 / 440.70427092395045, `oil.pressure` 149.60090176687544 / 149.69811391994867, `oil.temperature` 117.83659509082182 / 116.73367427951749, `oil.quantity` 14.382028854544323 / 14.267072188538169, `vibration.n1` `""` / `""`; `aircraft.temperature.tat` 17.0724731445315, `.oat` 17.0685977935791. Three samples 0.4 s apart: every engine value moved in its third to fifth significant digit (oil temperature 2: 116.73, 116.62, 116.55), hence the display hold.
- Newly non-null on v3: `docs/fenix-v3/evidence/live-findings.md:14`. Stock versus Fenix: spec section 3 (IAE idle: N1 30.9/22.4 %, N2 72.4/62.3 %, EGT 506/476 C, FF 482/441 kg/h; stock identical across engines) and `live-findings.md:34` (CFM: EGT 490/411, FF 404/287 kg/h).
- The stock SimVars could not be re-read for a paired sample today (the simconnect MCP timed out); in-sim check 2 compares OAT against Output O instead.
- **Owner check 1, 2026-10-22 12:25 local**, CFM A320 (C-FFWM), engine 2 started with the window open, data service logged every ~0.8 s: N2 leaves 0 at 12:25:00, fuel flow from about 24 % N2 (12:25:19), `running` false → true at 12:25:49.2 with N2 59.2 %, N1 18.1 %, EGT 410 °C; settled idle N2 59.0 %, N1 17.9 %, EGT 409 °C, fuel flow 290 kg/h. "Engine 2 running" was spoken once, when the start was complete (heard with the generator coming on line).
- **Owner check 2, 2026-10-22**, same aircraft, engine 2 at idle. The AI reads (Alt+S, Alt+E) framed the captain's PFD and ND on v3, as plan 04 predicts, so the ENGINE page was read from a screenshot of instrument view index 8 (camera restored afterwards; `CAMERA VIEW TYPE AND INDEX MAX:2` = 19). Fenix screen against the data service: oil quantity 17.5 / 17.73 and 14.5 / 14.51 QT, oil pressure 0 / 0 and 34 / 33.85 PSI, oil temperature 15 / 14.97 and 30 / 31.76 °C, engine 2 fuel flow 300 / 289 KG/H (the E/WD shows steps of 20), TAT and SAT +15 / 14.97 °C. Output O matched the window's OAT.

## Stock engine SimVars on the Fenix outside the First Officer (survey, read-only)

Searched every `.cs` under `MSFSBlindAssist/` for the stock engine and air-temperature SimVars (`TURB ENG *`, `GENERAL ENG *`, `ENG *`, `AMBIENT TEMPERATURE`, `TOTAL AIR TEMPERATURE`) and their struct fields. Takeoff assist, callouts, landing rollout, taxi guidance, visual guidance, universal automation, GSX, SayIntentions, the panel builder and `BaseAircraftDefinition.GetBaseVariables` read none. `FenixA320Definition.cs` defines no stock engine or temperature SimVar and no hotkey that speaks an engine value (its stock SimVars are COM standby, magnetic heading and fuel weight). What remains:

| Where | Stock SimVar | What the pilot gets on the Fenix | Note / handoff |
|---|---|---|---|
| `MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs:41`, `SimConnectManager.DataRequests.cs:662`, `MainForm.Hotkeys.cs:183-184`, spoken at `SimConnectManager.Dispatch.cs:413` | `AMBIENT TEMPERATURE` | Output O: "17 degrees Celsius" (the Fenix definition does not handle O, so the generic path speaks) | In-sim check 2 compares it with the Fenix's OAT; only if they differ, plan 12 routes the Fenix's O to `aircraft.temperature.oat` (Q4) |
| `SimConnectManager.Setup.cs:343`, `Services/WeatherService.cs:774-788`, `Forms/WeatherRadarForm.cs:483` | `AMBIENT TEMPERATURE` | Weather Radar window "Temperature: 17°C" | The sim's weather, the same for every aircraft: leave it |
| `SimConnectManager.Setup.cs:61-63`, `SimConnectManager.DataRequests.cs:537-542`, `Forms/FirstOfficer/FirstOfficerForm.cs:220`, `FirstOfficer/Fenix/FenixStateEvaluator.cs:12-19` | `TURB ENG N2:1/2` | First Officer "engine running" (never spoken) | Plan 05's, as briefed; in-sim check 1 gives it the Fenix N2 at the `running` edge |
| `Aircraft/FenixA320DisplayReads.cs:67`, `Services/GeminiService.cs:343` | none (AI reads E/WD pixels) | Output Alt+E | Not a SimVar; the guide can point to the Engines window for exact values (plan 12) |

Not engine values, noted only: the fuel and weight readouts (F, Shift+F, W, Shift+W) use stock `FUEL TOTAL QUANTITY WEIGHT` and `TOTAL WEIGHT` (`FenixA320Definition.cs:12511-12571`, `BaseAircraftDefinition.cs:1358-1366`); no mismatch has been measured.

## Before you start

- [x] Branch from the base commit in `docs/fenix-v3/plans/00-index.md` ("Base commit for parallel work"): `git switch -c FenixV3-p09-engine-data <base-sha>`; an agent works in `.claude/worktrees/fenix-v3-p09`.
- [x] Read (Read tool, so rules load, CORE-16): `MSFSBlindAssist/Services/FenixDataService.cs`, `FenixDataValue.cs`, `FenixDataQueries.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.cs`, `MSFSBlindAssist/Aircraft/FenixA320Definition.Hooks.cs`, `MSFSBlindAssist/Aircraft/FenixVariant.cs` (plan 01's), `MSFSBlindAssist/Forms/HS787/HS787EicasForm.cs` (the window pattern), `MSFSBlindAssist/Forms/DisplayListBox.cs`, `tests/MSFSBlindAssist.Tests/TestCulture.cs`.
- [x] `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` then `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`: both green; note the test count.

---

### Task 1: Engine data names and snapshot

**Files:**
- Create: `MSFSBlindAssist/Services/FenixEngineNames.cs`, `MSFSBlindAssist/Services/FenixEngineSnapshot.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixEngineSnapshotTests.cs`

**Interfaces:**
- Consumes (plan 01): `FenixDataValue` (`Kind`, `Number`, `Bool`, `IsKnown`, `static FromJson(JToken?)`), `FenixValueKind`.
- Produces (namespace `MSFSBlindAssist.Services`):
  - `public enum FenixEngineField { Running, N1, Egt, N2, FuelFlowKg, OilQuantity, OilPressure, OilTemperature }` (this order is the row order)
  - `public static class FenixEngineNames` with `public const string Tat = "aircraft.temperature.tat";`, `public const string Oat = "aircraft.temperature.oat";`, `public static string For(int engine, FenixEngineField field)` (engine 1 or 2, else `ArgumentOutOfRangeException`; suffixes `running`, `n1`, `egt`, `n2`, `ff.kg`, `oil.quantity`, `oil.pressure`, `oil.temperature`), `public static IReadOnlyList<string> All` (engine 1's eight in enum order, engine 2's eight, `Tat`, `Oat`)
  - `public sealed record FenixEngineValues(bool? Running, double? N1, double? Egt, double? N2, double? FuelFlowKgPerHour, double? OilQuantity, double? OilPressure, double? OilTemperature)` with `public static readonly FenixEngineValues None` (all null)
  - `public sealed record FenixEngineReading(FenixEngineValues Engine1, FenixEngineValues Engine2, double? TatC, double? OatC, long? LastUpdateTick)` with `public static readonly FenixEngineReading None`
  - `public sealed class FenixEngineSnapshot` with `public FenixEngineSnapshot(Func<long>? clock = null)` (default `() => Environment.TickCount64`), `public void Apply(string name, FenixDataValue value)` (any thread), `public FenixEngineReading Read()` (any thread)

- [x] **Step 1: Write the failing tests.**

```csharp
// Characterization tests for the Fenix engine data names and the snapshot the data-service
// callback writes into (pool thread) and the Engines window reads (UI timer).
using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixEngineSnapshotTests
{
    private static FenixDataValue V(string json) => FenixDataValue.FromJson(JToken.Parse(json));

    [Fact]
    public void Names_are_the_eighteen_published_values()
    {
        string[] engine(int n) => new[] { "running", "n1", "egt", "n2", "ff.kg", "oil.quantity", "oil.pressure", "oil.temperature" }
            .Select(f => $"aircraft.engines.{n}.{f}").ToArray();
        Assert.Equal(engine(1).Concat(engine(2)).Append("aircraft.temperature.tat").Append("aircraft.temperature.oat"), FenixEngineNames.All);
        Assert.DoesNotContain(FenixEngineNames.All, n => n.Contains("vibration"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void For_rejects_an_engine_the_A320_does_not_have(int engine)
        => Assert.Throws<ArgumentOutOfRangeException>(() => FenixEngineNames.For(engine, FenixEngineField.N1));

    [Fact]
    public void Apply_reads_numbers_and_the_running_flag()
    {
        var s = new FenixEngineSnapshot(() => 1000);
        s.Apply("aircraft.engines.2.n2", V("62.391407703690426"));
        s.Apply("aircraft.engines.1.running", V("true"));
        s.Apply("aircraft.engines.2.ff.kg", V("440.70427092395045"));
        s.Apply("aircraft.temperature.oat", V("17.0685977935791"));
        var r = s.Read();
        Assert.Equal(62.391407703690426, r.Engine2.N2);
        Assert.True(r.Engine1.Running);
        Assert.Equal(440.70427092395045, r.Engine2.FuelFlowKgPerHour);
        Assert.Equal(17.0685977935791, r.OatC);
        Assert.Null(r.Engine1.N1);
        Assert.Equal(1000, r.LastUpdateTick);
    }

    [Fact]
    public void Unknown_values_read_as_null_never_zero()
    {
        var s = new FenixEngineSnapshot(() => 1);
        s.Apply("aircraft.engines.1.n1", V("22.4"));
        s.Apply("aircraft.engines.1.n1", V("\"\""));              // v3: unknown name or no value
        s.Apply("aircraft.engines.1.egt", V("null"));
        s.Apply("aircraft.engines.1.n2", V("true"));              // wrong kind
        s.Apply("aircraft.engines.1.running", V("1"));            // wrong kind: only a JSON bool counts
        var e1 = s.Read().Engine1;
        Assert.Null(e1.N1); Assert.Null(e1.Egt); Assert.Null(e1.N2); Assert.Null(e1.Running);
    }

    [Fact]
    public void Other_names_are_ignored()
    {
        var s = new FenixEngineSnapshot(() => 5);
        s.Apply("aircraft.engines.1.vibration.n1", V("3"));
        s.Apply("aircraft.mcdu1.display", V("\"<root/>\""));
        Assert.Equal(FenixEngineReading.None, s.Read());
    }

    [Fact]
    public void Last_update_follows_the_clock()
    {
        long now = 1000;
        var s = new FenixEngineSnapshot(() => now);
        Assert.Null(s.Read().LastUpdateTick);
        s.Apply("aircraft.engines.1.n1", V("22.4"));
        now = 5000;
        s.Apply("aircraft.engines.1.n1", V("\"\""));
        Assert.Equal(5000, s.Read().LastUpdateTick);
    }
}
```

- [x] **Step 2: Run them; they fail to compile.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FenixEngineSnapshotTests"`. Expected: build error CS0246/CS0103 (`FenixEngineSnapshot`, `FenixEngineNames` not found).
- [x] **Step 3: Implement both files.** `Apply`: map the name through a static `Dictionary<string, (int engine, FenixEngineField field)>` built from `All` (TAT/OAT as engine 0); ignore unmapped names; numeric fields take `Kind == Number` and a finite `Number`, `Running` takes `Kind == Bool`, anything else stores null; every mapped `Apply` sets `LastUpdateTick = clock()`. One `lock` guards the fields; `Read()` builds the records under it. Records give value equality (`None` must equal a fresh snapshot's `Read()`).
- [x] **Step 4: Run them; all pass.** Same command. Expected: 7 passed.
- [x] **Step 5: Commit.** `git add MSFSBlindAssist/Services/FenixEngineNames.cs MSFSBlindAssist/Services/FenixEngineSnapshot.cs tests/MSFSBlindAssist.Tests/FenixEngineSnapshotTests.cs` then `git commit -m "feat(fenix): engine data names and snapshot from the data service"`.

### Task 2: Formatting, display hold and status

**Files:**
- Create: `MSFSBlindAssist/Services/FenixEngineFormat.cs`, `MSFSBlindAssist/Services/FenixEngineDisplayHold.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixEngineFormatTests.cs`, `tests/MSFSBlindAssist.Tests/FenixEngineDisplayHoldTests.cs`

**Interfaces:**
- Consumes: Task 1's `FenixEngineReading`, `FenixEngineValues`; plan 01's `FenixSetting(string Key, string Category, string? Value, IReadOnlyList<string> PossibleValues, string? Type, string? Source, bool Writable)`.
- Produces (namespace `MSFSBlindAssist.Services`):
  - `public enum FenixEngineWeightUnit { Kilograms, Pounds }` (in `FenixEngineFormat.cs`)
  - `public sealed class FenixEngineDisplayHold` with `public const double Band = 0.6;` and `public double? Hold(string key, double? raw, int decimals)`
  - `public static class FenixEngineFormat` with `public const string Unknown = "--";`, `public const double PoundsPerKilogram = 2.2046226218487757;`, `public const int StaleMs = 10_000;`, and:
    - `public static string Number(double? value, int decimals)` (decimals 0 or 1, else `ArgumentOutOfRangeException`)
    - `public static FenixEngineWeightUnit? WeightUnitFrom(IReadOnlyList<FenixSetting> settings)` (key `weight_unit`, value exactly `Kilograms` or `Pounds`, else null)
    - `public static IReadOnlyList<string> Rows(FenixEngineReading reading, FenixEngineWeightUnit unit, FenixEngineDisplayHold? hold = null)`
    - `public static string Status(string? variant, FenixEngineWeightUnit unit, bool unitFromFenix, long? lastUpdateTick, long nowTick)`

- [x] **Step 1: Write the failing tests.** `FenixEngineFormatTests` (header comment: the rows are the pilot's only view of the Fenix's engine gauges; spoken and brailled, so culture-proof):

```csharp
private static readonly FenixEngineReading Idle = new(   // IAE A321 at idle, 2026-10-21 13:06
    new FenixEngineValues(true, 22.448190078473196, 475.38836459330855, 62.212353401252194, 437.8835188757868, 14.382028854544323, 149.60090176687544, 117.83659509082182),
    new FenixEngineValues(true, 22.445335864998796, 474.14250676655524, 62.391407703690426, 440.70427092395045, 14.267072188538169, 149.69811391994867, 116.73367427951749),
    17.0724731445315, 17.0685977935791, 1000);

private static readonly string[] IdleKg =
{
    "Engine 1: running", "Engine 2: running",
    "Engine 1 N1: 22.4 percent", "Engine 2 N1: 22.4 percent",
    "Engine 1 EGT: 475 degrees C", "Engine 2 EGT: 474 degrees C",
    "Engine 1 N2: 62.2 percent", "Engine 2 N2: 62.4 percent",
    "Engine 1 Fuel Flow: 438 kilograms per hour", "Engine 2 Fuel Flow: 441 kilograms per hour",
    "Engine 1 Oil Quantity: 14.4", "Engine 2 Oil Quantity: 14.3",
    "Engine 1 Oil Pressure: 150", "Engine 2 Oil Pressure: 150",
    "Engine 1 Oil Temperature: 118", "Engine 2 Oil Temperature: 117",
    "Total Air Temperature: 17 degrees C", "Outside Air Temperature: 17 degrees C",
};
```

  Facts:
  - `Rows_read_the_idle_engines_in_kilograms`: `Assert.Equal(IdleKg, FenixEngineFormat.Rows(Idle, FenixEngineWeightUnit.Kilograms))`.
  - `Rows_convert_fuel_flow_to_pounds`: with `Pounds`, rows 8 and 9 (zero-based) are `"Engine 1 Fuel Flow: 965 pounds per hour"` and `"Engine 2 Fuel Flow: 972 pounds per hour"`; every other row equals `IdleKg`.
  - `Rows_are_the_same_under_de_DE_and_sv_SE`: `TestCulture.Run("de-DE", () => FenixEngineFormat.Rows(Idle, Kilograms))` and the same under `"sv-SE"` both equal `IdleKg`.
  - `Empty_reading_is_all_dashes`: `Rows(FenixEngineReading.None, Pounds)` is `"Engine 1: --"`, `"Engine 2: --"`, `"Engine 1 N1: --"` … `"Outside Air Temperature: --"` (18 rows, each label as in `IdleKg` followed by `": --"`; no unit after `--`).
  - `Running_false_reads_not_running`: `Idle with { Engine1 = Idle.Engine1 with { Running = false } }` gives row 0 `"Engine 1: not running"`.
  - `Number_is_invariant_and_never_negative_zero`: under `de-DE` `Number(22.448, 1) == "22.4"`; under `sv-SE` `Number(-12.4, 0) == "-12"` (ASCII U+002D) and the OAT row of a reading with `OatC = -12.4` is `"Outside Air Temperature: -12 degrees C"`; `Number(-0.4, 0) == "0"`; `Number(-0.04, 1) == "0.0"`; `Number(null, 0)`, `Number(double.NaN, 0)` and `Number(double.PositiveInfinity, 1)` are `"--"`; `Number(1, 2)` throws `ArgumentOutOfRangeException`.
  - `Rows_hold_a_jittering_value`: one `FenixEngineDisplayHold`; rows of `Idle` with engine 2 oil temperature 116.49, then 116.51 (`with` expressions): both give `"Engine 2 Oil Temperature: 116"`; without a hold the second gives `"... 117"`.
  - `Weight_unit_comes_from_the_Fenix_setting`: `new FenixSetting("weight_unit", "Aircraft", "Pounds", new[] { "Kilograms", "Pounds" }, "Enum", "Livery", true)` → `Pounds`; value `"Kilograms"` → `Kilograms`; value `"Stones"`, a list without the key, and an empty list → null.
  - `Status_reads_the_variant_and_unit`: `Status("Fenix A321, IAE engines, 2 extra tanks", Pounds, true, 1000, 2000) == "Fenix A321, IAE engines, 2 extra tanks. Fuel flow in pounds per hour, the Fenix's weight unit. Values update every second; F5 reads them again."`
  - `Status_says_when_the_unit_is_a_fallback`: `Status(null, Kilograms, false, 1000, 2000) == "Fuel flow in kilograms per hour; the Fenix's weight unit could not be read. Values update every second; F5 reads them again."`
  - `Status_waits_before_the_first_value`: `Status("x", Pounds, true, null, 5000) == "Waiting for engine values from the Fenix. Is the Fenix loaded?"`
  - `Status_says_when_values_stop`: `Status(null, Pounds, true, 1000, 11_001) == "Fuel flow in pounds per hour, the Fenix's weight unit. No new values from the Fenix for more than 10 seconds; F5 reads them again."`; at `nowTick` 11_000 it is the "update every second" text (stale is `now - last > StaleMs`).

  `FenixEngineDisplayHoldTests` (use `Assert.Equal(expected, actual!.Value, 6)`):
  - `First_value_rounds_normally`: `Hold("k", 116.4, 0)` → 116; `Hold("t", 22.448, 1)` → 22.4.
  - `Jitter_inside_the_band_keeps_the_shown_value`: on one key, 116.4 → 116, 116.55 → 116, 116.45 → 116, 116.59 → 116.
  - `A_real_move_updates_and_then_holds`: 116.4 → 116, 116.7 → 117, 116.45 → 117, 116.3 → 116.
  - `Tenths_use_a_tenth_of_the_band`: 22.448 → 22.4, 22.45 → 22.4, 22.47 → 22.5.
  - `Unknown_forgets_the_key`: 116.4 → 116, null → null, then 116.55 → 117 (fresh rounding).
  - `Keys_are_independent`: `Hold("a", 116.4, 0)` then `Hold("b", 116.7, 0)` → 116 and 117.
- [x] **Step 2: Run them; they fail to compile.** `dotnet test … --filter "FullyQualifiedName~FenixEngineFormatTests|FullyQualifiedName~FenixEngineDisplayHoldTests"`. Expected: build errors for the missing types.
- [x] **Step 3: Implement `FenixEngineDisplayHold`.** Per key keep the last shown value: null raw → remove the key, return null; no previous → shown = `Math.Round(raw, decimals, AwayFromZero)`; else if `|raw - shown| >= Band * 10^-decimals` → shown = that rounding; return shown.
- [x] **Step 4: Implement `FenixEngineFormat`.** `Number`: non-finite or null → `Unknown`; round with `AwayFromZero`; a result equal to 0 becomes `+0.0`; `ToString(decimals == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)`. `Rows`: labels and units exactly as `IdleKg`; the decimals per field are Owner decision 2; when `hold` is given, each numeric passes through `hold.Hold(<row label>, value, decimals)` (fuel flow AFTER the pounds conversion); an unknown value is `"<label>: --"`. `Status`: with no `lastUpdateTick`, the waiting text alone; otherwise `"<variant>. "` (when non-null) + the unit sentence (`"Fuel flow in pounds per hour, the Fenix's weight unit."` or `"Fuel flow in kilograms per hour; the Fenix's weight unit could not be read."`, likewise for kilograms from the Fenix and pounds) + `" Values update every second; F5 reads them again."`, or, when `nowTick - lastUpdateTick > StaleMs`, `" No new values from the Fenix for more than 10 seconds; F5 reads them again."`.
- [x] **Step 5: Run them; all pass.** Expected: 18 passed (12 format + 6 hold).
- [x] **Step 6: Commit.** `git add` the four files; `git commit -m "feat(fenix): engine readout formatting, display hold and status"`.

### Task 3: Running announcements (removable, Owner decision 6 / Q1)

**Files:**
- Create: `MSFSBlindAssist/Services/FenixEngineRunningWatch.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixEngineRunningWatchTests.cs`

**Interfaces:**
- Consumes: Task 1's `FenixEngineReading`.
- Produces: `public sealed class FenixEngineRunningWatch` with `public IReadOnlyList<string> Observe(FenixEngineReading reading)`: the lines to speak, engine 1 first.

- [x] **Step 1: Write the failing tests.** Helper `static FenixEngineReading R(bool? e1, bool? e2) => FenixEngineReading.None with { Engine1 = FenixEngineValues.None with { Running = e1 }, Engine2 = FenixEngineValues.None with { Running = e2 } };`
  - `First_values_are_silent`: `Observe(R(true, false))` is empty.
  - `A_start_and_a_shutdown_are_spoken`: `R(true,false)` → `[]`; `R(true,true)` → `["Engine 2 running"]`; `R(false,true)` → `["Engine 1 stopped"]`.
  - `Both_in_one_tick_engine_1_first`: `R(false,false)` → `[]`; `R(true,true)` → `["Engine 1 running", "Engine 2 running"]`.
  - `A_dropout_does_not_respeak`: `R(true,true)` → `[]`; `R(null,null)` → `[]`; `R(true,true)` → `[]`.
  - `Unknown_first_then_known_is_a_baseline`: `R(null,null)` → `[]`; `R(true,false)` → `[]`.
- [x] **Step 2: Run; compile failure.** `--filter "FullyQualifiedName~FenixEngineRunningWatchTests"`.
- [x] **Step 3: Implement.** Keep the last KNOWN value per engine; a null never changes it; a known value that differs from a known previous gives `"Engine {n} running"` (now true) or `"Engine {n} stopped"` (now false).
- [x] **Step 4: Run; 5 passed.**
- [x] **Step 5: Commit.** `git commit -m "feat(fenix): engine running edges as spoken lines"` (the two files).

### Task 4: The Engines window and its list entry

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixEnginesForm.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Engines.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixEngineWindowTests.cs`

**Interfaces:**
- Consumes (plan 01): `FenixWindowContext(FenixA320Definition Definition, FenixDataService Data, SimConnectManager SimConnect, ScreenReaderAnnouncer Announcer, Form Owner)`; `FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open)`; the seam `static partial void AddEngineWindows(List<FenixWindowEntry> entries);` on `public static partial class FenixWindowRegistry`; `FenixA320Definition.ShowFenixWindow<T>(Func<T> factory, Action<T> show)` (reuse-if-open, closed and disposed on aircraft switch by `DisposeTrackedWindows`, `BaseAircraftDefinition.cs:1144-1176`); `FenixA320Definition.Variant` (`IsKnown`, `Describe()`); `FenixDataService.Subscribe(IReadOnlyList<string>, Action<string, FenixDataValue>) : IDisposable` (callbacks on a pool thread), `ReadManyAsync(IReadOnlyList<string>, CancellationToken)`, `GetSettingsAsync(CancellationToken)`. Tasks 1-3's types.
- Produces: the list entry named `"Engines"` (plan 12's guide text); `public sealed class FenixEnginesForm : Form` with `public FenixEnginesForm(FenixWindowContext context)` and `public void ShowForm()`.

- [x] **Step 1: Write the failing tests** (`FenixEngineWindowTests`, header comment naming FNX-2 and the 30-45 Hz stream):
  - `Registry_lists_the_Engines_window_once`: `var e = Assert.Single(FenixWindowRegistry.Build(), x => x.Name == "Engines");` and `Assert.Equal("N1, EGT, N2, fuel flow, oil and air temperatures, from the Fenix's own values", e.Description);`
  - `Engines_window_never_marshals_and_cleans_up` (source scan of `MSFSBlindAssist/Forms/Fenix/FenixEnginesForm.cs` via `ClaudeContextBudgetTests.RepoRoot()`, as `FenixDataServiceAddressTests.cs:16`): `DoesNotContain("BeginInvoke")`, `DoesNotContain("Invoke(")`, `Contains("FenixEngineNames.All")`, `Contains("Interval = 1000")`, `Contains("protected override void Dispose(bool disposing)")`.
  - `Engine_readouts_never_use_stock_engine_simvars`: for the form and every `MSFSBlindAssist/Services/FenixEngine*.cs`: no `"TURB ENG"`, `"GENERAL ENG"`, `"ENG EXHAUST"`, `"FUEL FLOW PPH"`, `"AMBIENT TEMPERATURE"`, `"localhost"`.
- [x] **Step 2: Run them; all three fail.** `dotnet test … --filter "FullyQualifiedName~FenixEngineWindowTests"`. Expected: `Registry_lists_the_Engines_window_once` fails (plan 01's `Build()` has no "Engines" entry yet); the two source scans fail with `FileNotFoundException` for `FenixEnginesForm.cs`.
- [x] **Step 3: Implement `FenixWindowRegistry.Engines.cs`** in the namespace `FenixWindowRegistry.cs` declares (expected `MSFSBlindAssist.Forms.Fenix`, as `FenixEFBForm.cs:6`): `static partial void AddEngineWindows(List<FenixWindowEntry> entries) => entries.Add(new FenixWindowEntry("Engines", "<the description above>", ctx => ctx.Definition.ShowFenixWindow(() => new FenixEnginesForm(ctx), f => f.ShowForm())));`
- [x] **Step 4: Implement `FenixEnginesForm`** following `HS787EicasForm.cs` (previous-window restore 16-17, 53-57, 62; `ShowForm` 60-71):
  - Title `"Fenix engines"`; `KeyPreview = true`; about 520 x 440, centred, `FormBorderStyle.SizableToolWindow`, in the taskbar.
  - `DisplayListBox _list` (`Dock = Fill`, `AccessibleName = "Engine values"`, `TabIndex = 0`); `TextBox _status` (`ReadOnly`, `Multiline`, 2 lines high, `Dock = Bottom`, `AccessibleName = "Status"`, `TabIndex = 1`, VAT-13).
  - Fields: `FenixEngineSnapshot`, `FenixEngineDisplayHold`, `FenixEngineRunningWatch`, `IDisposable? _subscription`, `System.Windows.Forms.Timer _timer` (`Interval = 1000`), `FenixEngineWeightUnit _unit = Kilograms`, `bool _unitFromFenix`.
  - `RefreshNow()` (the timer's Tick; UI thread): `var r = _snapshot.Read();` → `_list.SetLines(FenixEngineFormat.Rows(r, _unit, _hold))`; speak each `_watch.Observe(r)` line with `Announce`; status = `FenixEngineFormat.Status(def.Variant.IsKnown ? def.Variant.Describe() : null, _unit, _unitFromFenix, r.LastUpdateTick, Environment.TickCount64)`, assigned only when it differs. Never `Focus`/`Activate` here.
  - `async Task ReloadAsync(bool announce)` (UI thread, no `ConfigureAwait(false)`): `WeightUnitFrom(await GetSettingsAsync())` sets `_unit`/`_unitFromFenix`; `await ReadManyAsync(FenixEngineNames.All)` and `Apply` each pair to the snapshot; if `IsDisposed`, return; `RefreshNow()`; then if no value was `IsKnown` → `Announce("No engine data from the Fenix. Is the Fenix loaded?")`, else if `announce` → `Announce("Engines refreshed")`. The whole body in try/catch that logs `Log.Debug("FenixEngines", …)` (CORE-15).
  - `ShowForm()`: remember the foreground window; on the first call only, `RefreshNow()` and `_list.SelectedIndex = 0` (so the screen reader lands on a row, not an unselected list), then `_subscription = Data.Subscribe(FenixEngineNames.All, _snapshot.Apply)`, `_timer.Start()`, `_ = ReloadAsync(false)`; every call: `Show()`, `BringToFront()`, `Activate()`, `_list.Focus()` (the pilot's own open; the only focus calls in the form).
  - `ProcessCmdKey`: Escape → `Close()`; F5 when not auto-repeat (`(msg.LParam.ToInt64() & 0x40000000) == 0`, as `MainForm.Hotkeys.cs:829`) → `_ = ReloadAsync(true)`; both return true.
  - `FormClosing`: stop the timer, dispose the subscription, restore the previous window. `protected override void Dispose(bool disposing)`: the same teardown, idempotent, `_subscription.Dispose()` inside try/catch (the definition may already have disposed the service on aircraft switch).
- [x] **Step 5: Run the three tests, then the whole suite and the build.** `dotnet test … --filter "FullyQualifiedName~FenixEngineWindowTests"` (3 passed); `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (all green, baseline + 33: 7 + 18 + 5 + 3); `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (0 errors). `ClaudeContextBudgetTests` must stay green (the new files load `a32nx-fenix.md` and `fenix-data.md` through their `Fenix*` globs).
- [x] **Step 6: Manual check (app + sim with the Fenix loaded; reads only; if you have no sim, list it for the owner).** Close any running MSFSBA first (CORE-3). Press `[` then Ctrl+Shift+F, arrow to "Engines", Enter: the window opens on "Engine 1: running" (or "not running"); BA itself says nothing; values match a one-shot read (`query { dataRef { r0: dataRef(name: "aircraft.engines.1.n2") { value } r1: dataRef(name: "aircraft.engines.1.ff.kg") { value } } }` posted to `http://127.0.0.1:8083/graphql` from a scratch script outside the repo): N2 to 0.1, fuel flow equal to kg x 2.2046 when `weight_unit` is Pounds. Arrow down and wait 10 s on "Engine 2 Oil Temperature": the row stays still. F5 says "Engines refreshed". `[`, Ctrl+Shift+F, Engines again while open: the same window comes forward, no second window. Escape closes and focus returns. Switch the aircraft profile with the window open: it closes and nothing more is spoken.
- [x] **Step 7: Commit.** `git add` the three files; `git commit -m "feat(fenix): Engines window in the Fenix windows list"`.

### Task 5: Hand back

- [x] **Step 1:** `git diff --name-only <base-sha>` lists only the 12 files of Tasks 1-4 (7 code, 5 tests), plus `docs/fenix-v3/plans/09-engine-data.md` if you ticked its boxes; build and full suite green.
- [x] **Step 2:** Fill in "Handoff to plan 12" below with what changed (Task 4 Step 6 results, the test count) then rebase onto `FenixV3` and push (index workflow, step 4).
- [x] **Step 3 (after the owner's in-sim check 2, only if the owner confirms units, Q2):** change the three oil suffixes in `FenixEngineFormat` and the six oil rows of `IdleKg` together (e.g. `"Engine 1 Oil Quantity: 14.4 quarts"`, `"Engine 1 Oil Pressure: 150 psi"`, `"Engine 1 Oil Temperature: 118 degrees C"`), run the suite, commit `feat(fenix): oil units measured on the Fenix ENGINE page`.

## Owner in-sim checks (few, prioritised)

1. **An engine start with the window open** (aircraft on the ground, APU bleed on, engine 2 off; any Fenix, an IAE one preferred). Press `[` then Ctrl+Shift+F, arrow to "Engines", Enter; it reads "Engine 2: not running", and BA says nothing. Alt+Tab to the sim and start engine 2 as usual. Expected: BA says "Engine 2 running" once. As soon as you hear it, Alt+Tab back, arrow to "Engine 2 N2" and note the number; note it again when the engine has settled at idle. Tell us: did "Engine 2 running" come at light-up or when the start was complete, and the two N2 numbers (this also answers plan 05's "engine running" question). Was the window too chatty during the start? Press F5: "Engines refreshed". Then switch the aircraft profile with the window still open: it closes and says nothing more.
2. **Units, engines at idle, AI provider set up.** In BA's main window choose section "Pedestal", panel "ECAM", press "ECAM ENG". Press `]` then Alt+S (the SD by AI): note the oil quantity, pressure and temperature and the units it reads. Press `]` then Alt+E (the E/WD): note the fuel flow and its unit (KG/H or LBS/H). Press `]` then O: note "… degrees Celsius". Compare with the window: the oil numbers should match the Engines rows, the fuel flow should be within about 20 of the row and in the same unit, and O should equal "Outside Air Temperature". Tell us the units (Q2) and any difference (Q4).

## Handoff to plan 12

**Pilot-facing changes** (guide: the Fenix section of `guide/airbus-aircraft.md` near its displays text at line 218, and the hotkey appendix; changelog fragment, category `feature`):
- Fenix: input mode, Ctrl+Shift+F, "Engines" opens a window with the Fenix's own engine readings, the same numbers its screens show: running or not, N1, EGT, N2, fuel flow (in the weight unit set in the Fenix EFB), oil quantity (quarts), pressure (psi) and temperature, total and outside air temperature. Values update every second; F5 reads them again and says "Engines refreshed"; Escape closes. While it is open BA says "Engine 1 running" or "Engine 1 stopped" when the Fenix's engine state changes (kept, Q1): "running" comes when the start is complete, not at light-up. Why: the sim's own engine values do not match the Fenix (an IAE A321 at idle: sim N1 30.9 % against the Fenix's 22.4 %).
- Draft changelog line: "Fenix: a new Engines window (input mode, Ctrl+Shift+F, then Engines) reads N1, EGT, N2, fuel flow, oil and air temperatures straight from the Fenix, so the numbers match its own displays."

**Proposed rule lines** (for `.claude/rules/fenix-data.md`; full text under `## FNX-?` in `docs/invariants/fenix-data.md` from this plan's Evidence and Owner decisions 3 and 6):
```text
- [FNX-?] Fenix engine and air-temperature readouts come from the Fenix's own data-service values (`FenixEngineNames`), never the stock engine SimVars, which run a generic engine model on the Fenix (IAE A321 at idle: stock N1 30.9 vs Fenix 22.4 %, EGT 506 vs 476 C, stock identical on both engines). Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] `FenixEnginesForm` never marshals per update: the `FenixDataService` callback writes only to the lock-protected `FenixEngineSnapshot`, and a 1 s WinForms timer reads it (engine values stream at 30-45 Hz); the refresh never focuses or activates the window. Full: docs/invariants/fenix-data.md#fnx-?
```

(341 and 307 characters.)
- No line for the formatting (invariant culture, no "-0", `--` for unknown, pounds only on `weight_unit` Pounds) or the baseline-first running edges: `FenixEngineFormatTests` and `FenixEngineRunningWatchTests` pin them and their names explain a failure.

**Handoff requests:**
1. ~~Rule globs~~ (Covered by plan 01: its fenix-data.md globs are final and include these files.)
2. ~~Output O on the Fenix (Q4)~~ (Not needed: check 2 found Output O equal to the Fenix's OAT.)
3. Plan 05 (First Officer engine running): check 1 measured the Fenix's `running` edge at N2 59.2 % and a CFM idle of 59.0 % (plan 05 measured 59.3 % CFM, 62.3 % IAE). Plan 05's 55 % N2 threshold would have called this start running about 4 s before the Fenix's own flag, while N2 was still climbing (56.2 % at 12:25:45.9, flag at 12:25:49.2). Not a defect; plan 05 may prefer `aircraft.engines.{n}.running` for "running" and keep N2 for "stopped". After merge, plan 05's own engine names can fold onto `FenixEngineNames.For(n, FenixEngineField.N2)` / `.Running` if they duplicate them.
4. If the owner chooses always-on running announcements (Q1): implement plan 01's `StartEngineBackground` / `StopEngineBackground` seams in a new `MSFSBlindAssist/Aircraft/FenixA320Definition.Engines.cs` (owned by plan 09) holding a `FenixEngineSnapshot` + `FenixEngineRunningWatch`, and remove the window's own speaking. Only the Ctrl+M row (`UserSettings.FenixDisabledMonitorVariables`, monitor manager) stays a plan 12 handoff.
5. Guide (plan 04 / 12): the AI E/WD read (Output Alt+E) stays for alerts; the guide can point pilots to the Engines window for exact engine numbers.
6. Plan 04 (not claimed when measured): on v3, 2026-10-22, the owner's Alt+S and Alt+E framed the captain's PFD and ND, as plan 04 predicts. `CAMERA VIEW TYPE AND INDEX MAX:2` read 19. Instrument view index 8 framed the E/WD, the SD and the standby instruments whole, but **both NDs were clipped at its edges** (1920 x 1080), which is plan 04 Task 1 Step 5's "stop and ask the owner" case for Alt+N. Indices 6, 7, 9, 10 and 11 were not tried.
7. **Follow-up done by plan 05 (2026-10-22, approved by the owner):** a live check found the `dataRefs` subscription sends `aircraft.engines.{n}.n2` once when it opens and never again, so this window kept its first N2 until F5. Plan 05's follow-up Task 13 adds `FenixEngineNames.Polled` (the 16 values that are not the running flags) and a once-a-second `FenixDataPoller` in `FenixEnginesForm`, started after the open read; the running flags still come from the subscription and the open/F5 read. Two comments here still say 30-45 Hz (`FenixEngineSnapshot.cs:33`, `FenixEngineWindowTests.cs:1`). Details: `05-first-officer.md`, "Follow-up from in-sim check 1".

**Rulings made during the build** (both from the final review; plan text above is the pre-ruling design):
- `ReloadAsync` applies a read to the snapshot only when at least one value is known, so a gateway that is down leaves the status on "Waiting for engine values…". Cost if wrong: after the Fenix quits mid-session, F5 keeps the last values on screen (the status says they stopped after 10 s) instead of blanking them.
- `FenixEngineSnapshot` gained `Version` and `ApplyAllIfUnchangedSince`, and Task 1's `Last_update_follows_the_clock` became `Last_update_follows_known_values_only`: the plan stamped every apply, against its own Review Focus bullet 1. A read that a newer subscription push overtook never undoes it (97f49b49e).

**Deferred review minors** (final review, 0 Critical, 2 Important fixed, 5 Minor left for plan 12 to take or drop):
1. Rows keep their last values, with no per-row stale marker, after the Fenix quits; only the status box says the values stopped.
2. ~~If the gateway pushes only on change, the status may say "No new values … for more than 10 seconds" in cold-and-dark, where nothing moves.~~ (Not seen: cold and dark, batteries off, the status kept "Values update every second", 2026-10-22.)
3. F5 pressed while the open-time reload is still running starts a second reload ("No engine data" spoken twice with the gateway down).
4. "No engine data" is spoken when the HTTP read fails even if the subscription is already delivering values.
5. `_unit` is assigned before the `IsDisposed` check in `ReloadAsync` (harmless).

**Done in the sim (2026-10-22, CFM A320):** owner checks 1 and 2 (Evidence); Output O checked; all of Task 4 Step 6: the window opens from the list and its rows read well, a focused row stays still for 10 s, F5 says "Engines refreshed", opening Engines again brings the same window forward, Escape closes and focus returns, switching the aircraft profile with the window open closes it with nothing spoken. Cold and dark (engines, APU and batteries off), the status read "Fenix A320, CFM engines. Fuel flow in kilograms per hour, the Fenix's weight unit. Values update every second; F5 reads them again."

**In-sim checks still needed:** none.

---

Summary:
1. Plan 09 adds a Fenix "Engines" window (input mode, Ctrl+Shift+F, Engines) reading the Fenix's own 18 engine and temperature values through plan 01's data service.
2. Pure logic (names, snapshot, invariant formatting with a display hold, status, running-edge watcher) is test-first in `Services/FenixEngine*.cs`, with de-DE/sv-SE and "-0" tests.
3. The window never marshals per update: the 30-45 Hz callback fills a locked snapshot; a 1 s timer reconciles a `DisplayListBox` in place, without moving focus.
4. Owner decides: running announcements (window-only proposal), oil units after an ENGINE-page check, row layout, and whether Output O should use the Fenix's OAT.
5. Survey: outside the First Officer only Output O and the weather window read a stock temperature on the Fenix; no stock engine SimVar is read; two rule lines and five handoffs go to plan 12.
