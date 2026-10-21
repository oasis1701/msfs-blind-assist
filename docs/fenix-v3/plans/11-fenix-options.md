# Fenix Options Implementation Plan (plan 11)

**Claimed by:** nobody yet · **Status:** not started

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An accessible "Fenix options" window that lists every Fenix EFB option with its value in Fenix's own words, puts the eleven options that can change how MSFS Blind Assist behaves first (each with a plain-language note), and measures in the sim which of those eleven really disturb the app.

**Architecture:** Pure catalog code in `MSFSBlindAssist/Services/FenixOption*.cs`: Fenix's own labels (a snapshot of the EFB bundle's settings map), exact-key masking of credentials, value formatting by the EFB's own rules, grouping and ordering. One read of plan 01's `FenixDataQueries.SettingsQuery` through `FenixDataService.QueryAsync` feeds a `NativeAccessibleTreeView` window registered in the "Fenix windows" list via `AddOptionsWindows`. Read only unless the owner chooses otherwise (decision D1; Task 5 is then the only write).

**Tech Stack:** C# 13 / .NET 10 Windows Forms, Newtonsoft.Json (`JObject`, already referenced), xUnit.

**Spec:** `docs/fenix-v3/spec.md` section 4.5; index `docs/fenix-v3/plans/00-index.md`; foundation `docs/fenix-v3/plans/01-foundation.md` (consumed by exact name below). Depends on plan 01: branch from the base SHA 00-index.md records (`git switch -c FenixV3-p11-options <base-sha>`, worktree `.claude/worktrees/fenix-v3-p11`).

## Owner decisions

| # | Decision | Options | Default if unanswered |
|---|---|---|---|
| D1 | **May the window change options?** | (a) Read only. (b) **Recommended:** F2 changes an on/off or choice option for the current sim session only, the way the EFB's own toggle does. | **Decided (owner, 2026-10-21): (b)**, with a Yes/No confirm before cockpit-reshaping options (Task 5) |
| D2 | `selcal_code` | Shown (decided): the EFB shows it in plain text, it is painted on the livery and broadcast on HF, and it is not a login. Masked only if the owner says so (add it to `FenixOptionKeys.Secrets`, one line, plus a test row). | Shown |
| D3 | Live updates | Re-read on open, on reopen and on F5 (decided). No `settingsState` subscription: plan 01's `Subscribe` carries only dataRefs, a second socket would copy its client, and options change only when the pilot changes them or a SimBrief import switches `weight_unit`, which no app path reads. | Re-read |
| D4 | Options the EFB hides (`writable` false, category `None`) | Shown (decided): they carry the variant facts (`type_code`, `engine_family`, `is_sharklet`). Category `None` becomes the last group, "Other, not in the Fenix EFB". | Shown |
| D5 | In-sim measurements M1-M4 | Owner runs them (section "In-sim measurements"); results decide handoffs R1-R4. | Notes stay cautious |

D1 facts, all from the EFB bundle (`main.ccd882238ccd3f2d.js`, chunk `869`; operations also in `docs/fenix-v3/evidence/gql-operations.txt` lines 13, 14, 25, 34), none tried live:
- Mutation, verbatim: `mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }`. `SetSettingInput` is `{ key, value, persist }`: the store's `B()` builds `{key:h.key, value:String(h.value), persist:h.persist}`, so `value` is always a string ("true"/"false" for a toggle, the enum name for a choice). Also present, not used: `setSettings([SetSettingInput!]!)`, `setDefault(key)`, `setDefaults(keys)`.
- The EFB's own toggle sends `persist: false`; Fenix's own tooltip for that state: "This setting is overridden for the current simulator session and will reset when you close MSFS or change aircraft/livery." Its "Set as user override" sends `persist: true` ("will apply regardless of livery or Fenix defaults"). (b) sends `persist: false` only, never `true`, never a reset.
- Risks of (b): the write path has never run against the gateway (first write is owner step E1); `standby_type`, `adirs_type`, `acp_type`, `dcdu_enabled` reshape the cockpit mid-session (plans 01/06 follow the config L:vars); Fenix itself warns that FCU Readouts "New" can blank the FCU until the flight restarts; a wrong choice lasts until MSFS closes. Why recommended: whether the EFB settings page works with a screen reader is unmeasured, and (b) lets a pilot switch off an option that M1-M4 show disturbs the app.

## Global Constraints

- Data service only at `127.0.0.1:8083` through plan 01's `FenixDataService` (A320-40); never `localhost`, never schema introspection, never `navigraph { … }`, never a guessed name (FNX-3).
- Read with plan 01's `FenixDataQueries.SettingsQuery` = `query { settings { softwareVersion state { settings { key category value possibleValues source type writable } } } }` via `QueryAsync`; parse with `FenixDataQueries.ParseSettings` and `ParseSoftwareVersion`. `QueryAsync` null = the Fenix did not answer.
- No write in Tasks 1-4. Task 5 (only if D1 = b) sends `setSetting` with `persist: false`, never for a secret, never `setSettings`, `setDefault(s)` or `persist: true`.
- Secrets by EXACT key (ordinal): `hoppie_key`, `navigraph_refresh_token`, `pushover_application_key`, `pushover_token`, `simbrief_id`. Never substring: the live-dump script's substring mask hid `encoder_acceleration_enabled`, `type_code`, `cdls_keypad_beep_type`, `on_screen_keyboard_enabled` and `navigraph_navdata_mode`. A secret's value never reaches a row, the details, speech or a log; no file of this plan logs any option value (keys and counts only).
- Labels, descriptions and value labels are Fenix's own (EFB bundle 3.0.0.7426). A key without one uses `FenixOptionCatalog.Humanize`, a port of the EFB's `humanizeKey` pipe, which is what the EFB itself shows; an enum value without one shows the raw value, `""` shows "(none)", as the EFB does.
- Numbers formatted invariant (`"0.###"`); every formatting test also runs under de-DE and sv-SE (`TestCulture.Run`); `Humanize` casing is culture-invariant (tr-TR test).
- Window: `NativeAccessibleTreeView` with children filled on `BeforeExpand` (CORE-12), no `TreeView.CheckBoxes` (CORE-18), status and details are read-only `TextBox`es, never a `Label` (VAT-13), speech per CORE-7: nothing on open; F5's result, Enter's details and (Task 5) the read-back after a change.
- No pool-thread callbacks in this design (no subscription): every `await` runs from a UI-thread handler without `ConfigureAwait(false)`, and the form checks `IsDisposed` after each `await`. A later subscription must marshal with a try/catch `SafeBeginInvoke` (A380C-6, FNX-2).
- Files: only `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Options.cs`, `MSFSBlindAssist/Forms/Fenix/FenixOptions*.cs`, `MSFSBlindAssist/Services/FenixOption*.cs`, `tests/MSFSBlindAssist.Tests/FenixOption*.cs`. Everything else is a handoff.

## Review Focus

- A credential shown or logged, or a harmless option hidden because its key contains "key" or "code" (exact-key masking both ways: Task 2 tests `Secrets_are_masked_by_exact_key` and `Lookalike_keys_are_not_masked`; log scan in Task 4).
- Plan 01's `FenixSetting.Value` turns a JSON bool into "True" or "true" and may turn `10.5` into "10,5" on a de-DE PC; neither is pinned by plan 01. Rows must still read "On"/"Off" and "10.5" (Task 2 `Booleans_read_on_or_off_in_either_case`, `Numbers_are_invariant_under_comma_cultures`).
- The Fenix is not loaded, or the gateway goes away between open and F5: the status box says so, the tree is empty or keeps the last values, F5 retries, nothing throws (Task 3 `Status_texts`; Task 4 manual check).
- The window is closed, or the aircraft switched (plan 01 closes tracked windows and disposes the data service), while a read or write is in flight: no `ObjectDisposedException`, no write to a disposed control (Task 4 step 3, `IsDisposed` after every `await`; manual check).
- A Fenix update adds keys, enum values or categories the snapshot lacks, or the variant changes the list (146 options on the A320 CFM, 149 on the A321 IAE): rows never blank, groups re-sort (Task 2 `Unlabelled_key_uses_Humanize`, Task 3 `Unknown_category_is_humanized_and_sorted`).

---

## File structure

| File | Responsibility |
|---|---|
| `MSFSBlindAssist/Services/FenixOptionLabels.cs` | Fenix's own labels, descriptions, value labels and control hints (snapshot of 125 options, 34 value labels) and their parser |
| `MSFSBlindAssist/Services/FenixOptionKeys.cs` | Key constants: the 11 options that affect the app, the 5 secrets |
| `MSFSBlindAssist/Services/FenixOptionNotes.cs` | The plain-language note per affecting option and the value the app is tested with |
| `MSFSBlindAssist/Services/FenixOptionCatalog.cs` | Masking, `Humanize`, labels, value formatting, row text, detail lines, source text |
| `MSFSBlindAssist/Services/FenixOptionGrouping.cs` | Groups and order, change count, untested list, status and refresh texts |
| `MSFSBlindAssist/Forms/Fenix/FenixOptionsForm.cs` | The window |
| `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Options.cs` | `AddOptionsWindows`: the "Fenix options" entry |
| `MSFSBlindAssist/Services/FenixOptionEdits.cs`, `MSFSBlindAssist/Forms/Fenix/FenixOptionsChangeDialog.cs` | Task 5 only (D1 = b) |
| `tests/MSFSBlindAssist.Tests/FenixOptionLabelsTests.cs`, `FenixOptionCatalogTests.cs`, `FenixOptionGroupingTests.cs`, `FenixOptionsFormSourceTests.cs`, `FenixOptionEditsTests.cs` (Task 5) | Tests |

All new code is in namespace `MSFSBlindAssist.Services` (Services) or `MSFSBlindAssist.Forms.Fenix` (Forms). Test helper used throughout (put it in each test class that needs it):

```csharp
static FenixSetting S(string key, string? value, string type = "System.Boolean", string category = "Aircraft",
    string[]? possible = null, string source = "Internal", bool writable = true)
    => new(key, category, value, possible ?? Array.Empty<string>(), type, source, writable);
```

---

### Task 1: Fenix's own option labels

**Files:**
- Create: `MSFSBlindAssist/Services/FenixOptionLabels.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionLabelsTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record FenixOptionText(string Label, string Description, string? ControlType, IReadOnlyDictionary<string, string> ValueLabels);`
  - `public static class FenixOptionLabels` with `public const string SourceVersion = "3.0.0.7426";`, `public static IReadOnlyDictionary<string, FenixOptionText> All { get; }` (ordinal keys), `public static bool TryGet(string key, [NotNullWhen(true)] out FenixOptionText? text)`, `internal static IReadOnlyDictionary<string, FenixOptionText> Parse(string table)`.

- [ ] **Step 1: Generate the table.** With the Fenix loaded: `python -I docs/fenix-v3/tools/fetch_gateway_bundles.py <new-empty-folder>`; in `<folder>/efb/` find the chunk holding `name:"humanizeKey"` (`869.06e499eaa8d7474c.js` in 3.0.0.7426); save Appendix A outside that folder and run it on the chunk: `python -I option_labels.py <chunk.js> fenix-options-labels.tsv` must print `125 options, 159 rows`. Do not use the committed `docs/fenix-v3/evidence/fenix-options-labels.tsv` (regex extraction: it drops 18 descriptions, truncates one, leaves `→` escapes and keeps one label per value name across options, so refuel rate's "Realistic (16 Kg/s)" is lost); handoff R6 replaces it.
- [ ] **Step 2: Write the failing tests.**

```csharp
public class FenixOptionLabelsTests
{
    [Fact] public void Table_holds_every_option_the_EFB_labels()
    {
        Assert.Equal(125, FenixOptionLabels.All.Count);
        Assert.Equal(34, FenixOptionLabels.All.Values.Sum(t => t.ValueLabels.Count));
    }

    [Theory]
    [InlineData("encoder_acceleration_enabled", "Encoder Acceleration")]
    [InlineData("efis_baro_linking_type", "EFIS Linking: Baro")]
    [InlineData("fd_ls_sync_enabled", "EFIS Linking: FD/LS")]
    [InlineData("channel_spacing_type", "Channel Spacing")]
    [InlineData("tcas_ap_enabled", "AP/FD TCAS")]
    public void Labels_are_the_EFB_own(string key, string label) => Assert.Equal(label, FenixOptionLabels.All[key].Label);

    [Fact] public void Value_labels_are_scoped_to_their_option()
    {
        Assert.Equal("Realistic (16 Kg/s)", FenixOptionLabels.All["refuel_rate_type"].ValueLabels["Realistic"]);
        Assert.Equal("Realistic (1x)", FenixOptionLabels.All["failure_rate"].ValueLabels["Realistic"]);
        Assert.Equal("8.33 kHz", FenixOptionLabels.All["channel_spacing_type"].ValueLabels["Khz8d33"]);
        Assert.Equal("Linked Left+Right+Standby", FenixOptionLabels.All["efis_baro_linking_type"].ValueLabels["LinkedWithStandby"]);
    }

    [Fact] public void Descriptions_are_whole_and_unescaped()
    {
        Assert.EndsWith("For GSX, doors will always be handled automatically.", FenixOptionLabels.All["door_automation_enabled"].Description);
        Assert.StartsWith("New style FCU readouts", FenixOptionLabels.All["fcu_readouts_type"].Description);
        Assert.Equal("FWC \"Ten\" callout.", FenixOptionLabels.All["gpws10"].Description);
        Assert.Equal("Boarding → Fuel", FenixOptionLabels.All["gsx_boarding_service_sequence_type"].ValueLabels["BoardingThenFuel"]);
    }

    [Fact] public void Control_hints_are_kept()
    {
        Assert.Equal("password", FenixOptionLabels.All["hoppie_key"].ControlType);
        Assert.Equal("action-button", FenixOptionLabels.All["navigraph_refresh_token"].ControlType);
        Assert.Null(FenixOptionLabels.All["weight_unit"].ControlType);
    }

    [Fact] public void Parse_reads_option_and_value_rows()
    {
        var t = FenixOptionLabels.Parse("k||Key label|Key text|\nk|V1|Value one||\n");
        Assert.Equal("Key label", t["k"].Label);
        Assert.Equal("Value one", t["k"].ValueLabels["V1"]);
    }

    [Theory]
    [InlineData("k|Key")]                 // not five fields
    [InlineData("k|V1|Value one||\n")]    // a value row before its option row
    public void Parse_refuses_a_malformed_table(string table) => Assert.Throws<FormatException>(() => FenixOptionLabels.Parse(table));
}
```

- [ ] **Step 3: Run them; they fail to compile.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FenixOptionLabelsTests`
- [ ] **Step 4: Implement.** Paste the 159 data rows (no header) of Step 1's TSV, tabs replaced by `|` (`tail -n +2 fenix-options-labels.tsv | tr '\t' '|'`), into a C# raw string literal `private const string Table = """ … """;`; `All` = `Parse(Table)`, built once (`Lazy` or static readonly). Row = `key|value|label|description|control`: empty `value` = option row, else a value label of the option above it; empty control = null. Split lines on `\n` and trim a trailing `\r` (a CRLF checkout puts one on every row), skip blank lines. The file header says where the rows come from and how to regenerate them (Step 1 and Appendix A; after plan 12 deletes `docs/fenix-v3/`, `docs/a32nx.md`, handoff R6).
- [ ] **Step 5: Run them; all pass.**
- [ ] **Step 6: Commit.** `git add MSFSBlindAssist/Services/FenixOptionLabels.cs tests/MSFSBlindAssist.Tests/FenixOptionLabelsTests.cs` and `git commit -m "feat(fenix): the Fenix EFB's own option labels (plan 11)"`

### Task 2: Catalog: keys, notes, masking and formatting

**Files:**
- Create: `MSFSBlindAssist/Services/FenixOptionKeys.cs`, `MSFSBlindAssist/Services/FenixOptionNotes.cs`, `MSFSBlindAssist/Services/FenixOptionCatalog.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1 `FenixOptionLabels.TryGet`; plan 01 `FenixSetting(string Key, string Category, string? Value, IReadOnlyList<string> PossibleValues, string? Type, string? Source, bool Writable)`; `MSFSBlindAssist.Utils.NumberParsing.TryParseExternal` (CORE-17).
- Produces:
  - `public static class FenixOptionKeys`: consts `EncoderAcceleration = "encoder_acceleration_enabled"`, `EfisBaroLinking = "efis_baro_linking_type"`, `FdLsSync = "fd_ls_sync_enabled"`, `ChannelSpacing = "channel_spacing_type"`, `FcuReadouts = "fcu_readouts_type"`, `WeightUnit = "weight_unit"`, `BaroUnit = "baro_unit"`, `ControlPosition = "control_position_type"`, `IncrementalThrust = "incremental_thrust_handling_enabled"`, `AutoToga = "auto_toga_enabled"`, `TcasAp = "tcas_ap_enabled"`; `IReadOnlyList<string> AffectsApp` (those 11 in this order); `IReadOnlyList<string> Secrets` (the 5 of Global Constraints).
  - `public static class FenixOptionNotes`: `string? AppNote(string key)`, `string? TestedValue(string key)`.
  - `public static class FenixOptionCatalog`: `bool IsSecret(string key)`, `string Humanize(string key)`, `string Label(string key)`, `string FormatValue(FenixSetting s)`, `string RowText(FenixSetting s)` (= `$"{Label(s.Key)}: {FormatValue(s)}"`), `IReadOnlyList<string> DetailLines(FenixSetting s)`, `string SourceText(string? source)`.

Exact copy for `FenixOptionNotes` (pilot-facing; plan 12 updates the wording once M1-M4 are in):

| Key | `AppNote` | `TestedValue` |
|---|---|---|
| encoder_acceleration_enabled | When on, the app's Set heading, speed, altitude, vertical speed and altimeter entries may stop on the wrong value: they turn the FCU knobs many clicks at once. The app is tested with this off. | `false` |
| efis_baro_linking_type | The app sets the left and then the right altimeter. When linked, either knob moves both, so a set may land a step off. The app is tested with Independent. | `Independent` |
| fd_ls_sync_enabled | When on, pressing FD or LS on one side also switches the other side. The First Officer presses each side in turn, which may switch them back off. The app is tested with this off. | `false` |
| channel_spacing_type | With 25 kHz, an 8.33 kHz frequency typed into the app's COM standby box may be changed to a 25 kHz channel. Listen to the frequency the RMP reads back. | null |
| fcu_readouts_type | Changes how the FCU windows are drawn. The app reads the FCU values, not the picture, so its FCU readouts should not change. | null |
| weight_unit | The unit the MCDU, EFB and ECAM use for weights. The app's weight and fuel keys read the simulator and say their own unit, so they do not follow this option. | null |
| baro_unit | The altimeter unit the aircraft starts with. The app's altimeter readout says both units, and Set altimeter uses the unit you choose in it. | null |
| control_position_type | Fenix says this changes the sidesticks and the audio control panels. The app is tested from the Captain seat; its audio panel controls have not been checked from the First Officer seat. | `Captain` |
| incremental_thrust_handling_enabled | Changes only the simulator's throttle increase and decrease keys. The app moves the thrust levers to their detents directly, so it is not affected. | null |
| auto_toga_enabled | The Fenix may select TOGA thrust by itself during the takeoff roll. The app's thrust lever readout says where the levers are, not the thrust the engines give. | null |
| tcas_ap_enabled | When on, the autopilot and flight directors follow TCAS resolution advisories. The app gives no TCAS resolution guidance on the Fenix, so nothing in it changes. | null |

Code evidence behind the "not affected" notes: weights and fuel read stock `TOTAL WEIGHT` / `FUEL TOTAL QUANTITY WEIGHT` in pounds (`MSFSBlindAssist/Aircraft/FenixA320Definition.cs:12511-12583`); Set altimeter picks the EFIS mode from the window's unit (`FenixA320Definition.cs:12306-12324`, `Forms/FenixA320/FenixBaroWindow.cs:222-282`); thrust levers are absolute detent writes (`FenixA320Definition.cs:12035-12052`); TCAS RA guidance exists only in the FBW definitions (`Services/TcasRaGuidance.cs` users).

`FormatValue` rules, first match wins:

| Condition | Text |
|---|---|
| `IsSecret(key)` | `Value` null or "" → "not set"; else "set, hidden" |
| `Value` null | "unknown" |
| `Type == "System.Boolean"` | `bool.TryParse` true → "On", false → "Off"; unparsable → `Value` |
| `PossibleValues.Count > 0` (enums, `printer_name`) | "" → "(none)"; else `ValueLabels[value]`, then `ValueLabels[value.ToLowerInvariant()]` (the EFB's `F()`), else `Value` |
| `Type` is `System.Int32` or `System.Single` | `NumberParsing.TryParseExternal` → `ToString("0.###", CultureInfo.InvariantCulture)`; unparsable → `Value` |
| otherwise | "" → "not set"; else `Value` |

`DetailLines` order (each present only when it applies): `$"Effect on MSFS Blind Assist: {AppNote}"`; the description, else "Fenix gives no description for this option."; `$"Choices: {labels joined with ", "}."` when `PossibleValues.Count > 0` (labels by the enum rule above, in `PossibleValues` order); `SourceText(s.Source)`; "The Fenix EFB does not let you change this option." when `!Writable`; "MSFS Blind Assist never shows or logs this value." when secret. `SourceText` follows the EFB's `C()`: lowercase, `userfile` → `user`, anything outside default/livery/session/user → `default`, then Fenix's own tooltip words: default "This setting is the default Fenix value."; livery "This setting has been overridden by the current livery."; session "This setting is overridden for the current simulator session and will reset when you close MSFS or change aircraft/livery."; user "This setting has been overridden by you and will apply regardless of livery or Fenix defaults."

`Humanize` ports the EFB pipe: `Regex.Replace(k, "[_-]+", " ")`, then `"([A-Z]+)([A-Z][a-z])"` → `"$1 $2"`, then `"([a-z0-9])([A-Z])"` → `"$1 $2"`, split on whitespace dropping empties, first char `char.ToUpperInvariant`, join with " ". `Label` = `FenixOptionLabels` label, else `Humanize(key)`.

- [ ] **Step 1: Write the failing tests** (`FenixOptionCatalogTests`, helper `S` from the top):

```csharp
[Theory]
[InlineData("hoppie_key")] [InlineData("navigraph_refresh_token")] [InlineData("pushover_application_key")]
[InlineData("pushover_token")] [InlineData("simbrief_id")]
public void Secrets_are_masked_by_exact_key(string key)
{
    var set = S(key, "abc123", "System.String", "Connections");
    Assert.EndsWith(": set, hidden", FenixOptionCatalog.RowText(set));
    Assert.DoesNotContain("abc123", FenixOptionCatalog.RowText(set) + string.Join(" ", FenixOptionCatalog.DetailLines(set)));
    Assert.Contains("MSFS Blind Assist never shows or logs this value.", FenixOptionCatalog.DetailLines(set));
    Assert.EndsWith(": not set", FenixOptionCatalog.RowText(S(key, "", "System.String", "Connections")));
}

[Fact] public void Lookalike_keys_are_not_masked()
{
    Assert.Equal("Encoder Acceleration: Off", FenixOptionCatalog.RowText(S("encoder_acceleration_enabled", "False")));
    Assert.Equal("Cockpit Door Locking System (CDLS) Keypad Beep: Single Beep",
        FenixOptionCatalog.RowText(S("cdls_keypad_beep_type", "Beep", "Enum", possible: new[] { "None", "Beep", "BeepBeep" })));
    Assert.Equal("On-Screen Keyboard: On", FenixOptionCatalog.RowText(S("on_screen_keyboard_enabled", "true")));
    Assert.Equal("Type Code: A321", FenixOptionCatalog.RowText(S("type_code", "A321", "Enum", "None",
        new[] { "Unknown", "A318", "A319", "A320", "A321" }, writable: false)));
    Assert.Equal("Navigraph Navdata Mode: Hot Standby", FenixOptionCatalog.RowText(S("navigraph_navdata_mode", "HotStandby", "Enum",
        possible: new[] { "Disabled", "HotStandby", "Automatic", "ManualImport" })));
    Assert.Equal("SELCAL Code: AB-CD", FenixOptionCatalog.RowText(S("selcal_code", "AB-CD", "System.String")));   // D2
}

[Theory] [InlineData("True", "On")] [InlineData("true", "On")] [InlineData("False", "Off")] [InlineData("false", "Off")]
public void Booleans_read_on_or_off_in_either_case(string raw, string spoken)
    => Assert.Equal($"Auto TOGA: {spoken}", FenixOptionCatalog.RowText(S("auto_toga_enabled", raw)));

[Fact] public void Enum_values_use_Fenix_labels_else_the_raw_value()
{
    var spacing = new[] { "Khz25", "Khz8d33" };
    Assert.Equal("Channel Spacing: 8.33 kHz", FenixOptionCatalog.RowText(S("channel_spacing_type", "Khz8d33", "Enum", possible: spacing)));
    Assert.Equal("Channel Spacing: 25 kHz", FenixOptionCatalog.RowText(S("channel_spacing_type", "Khz25", "Enum", possible: spacing)));
    var seats = new[] { "Captain", "FirstOfficer" };
    Assert.Equal("Control Position: Captain", FenixOptionCatalog.RowText(S("control_position_type", "Captain", "Enum", "Misc", seats)));
    Assert.Equal("Control Position: First Officer", FenixOptionCatalog.RowText(S("control_position_type", "FirstOfficer", "Enum", "Misc", seats)));
    Assert.Equal("Printer: (none)", FenixOptionCatalog.RowText(S("printer_name", "", "System.String", "Printer",
        new[] { "", "Microsoft Print to PDF", "Pushover" })));
}

[Theory] [InlineData("de-DE")] [InlineData("sv-SE")] [InlineData("en-US")]
public void Numbers_are_invariant_under_comma_cultures(string culture)
{
    Assert.Equal("Printer: Font Size: 10.5", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("printer_font_size", "10.5", "System.Single", "Printer"))));
    Assert.Equal("Printer: Font Size: 10.5", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("printer_font_size", "10,5", "System.Single", "Printer"))));
    Assert.Equal("Default Acceleration Altitude: 1500", TestCulture.Run(culture, () => FenixOptionCatalog.RowText(S("default_acceleration_altitude_ft", "1500", "System.Int32"))));
}

[Fact] public void Empty_and_missing_values_read_plainly()
{
    Assert.Equal("Registration: not set", FenixOptionCatalog.RowText(S("registration", "", "System.String")));
    Assert.Equal("Weight Unit: unknown", FenixOptionCatalog.RowText(S("weight_unit", null, "Enum", possible: new[] { "Kilograms", "Pounds" })));
}

[Theory]
[InlineData("type_code", "Type Code")] [InlineData("GroundServices", "Ground Services")] [InlineData("deu_a_count", "Deu A Count")]
[InlineData("IaeV2500", "Iae V2500")] [InlineData("cids_mid_top_line", "Cids Mid Top Line")] [InlineData("", "")]
public void Humanize_matches_the_EFB_pipe(string key, string expected) => Assert.Equal(expected, FenixOptionCatalog.Humanize(key));

[Fact] public void Humanize_is_culture_proof() => Assert.Equal("Is Sharklet", TestCulture.Run("tr-TR", () => FenixOptionCatalog.Humanize("is_sharklet")));

[Fact] public void Unlabelled_key_uses_Humanize()
{
    Assert.Equal("Engine Family", FenixOptionCatalog.Label("engine_family"));
    Assert.Equal("Brand New Option: On", FenixOptionCatalog.RowText(S("brand_new_option", "true")));
}

[Theory]
[InlineData("Internal", "This setting is the default Fenix value.")]
[InlineData("Livery", "This setting has been overridden by the current livery.")]
[InlineData("UserFile", "This setting has been overridden by you and will apply regardless of livery or Fenix defaults.")]
[InlineData("Session", "This setting is overridden for the current simulator session and will reset when you close MSFS or change aircraft/livery.")]
[InlineData("Xyz", "This setting is the default Fenix value.")]
public void Source_is_named_in_Fenix_words(string source, string text) => Assert.Equal(text, FenixOptionCatalog.SourceText(source));

[Fact] public void Details_lead_with_the_app_note_and_list_choices()
{
    var lines = FenixOptionCatalog.DetailLines(S("efis_baro_linking_type", "Independent", "Enum", "Realism",
        new[] { "Independent", "LinkedWithStandby", "Linked" }, "UserFile"));
    Assert.StartsWith("Effect on MSFS Blind Assist: The app sets the left and then the right altimeter.", lines[0]);
    Assert.Equal("Links the EFIS barometer values between left EFIS, right EFIS and the standby instrument.", lines[1]);
    Assert.Equal("Choices: Independent, Linked Left+Right+Standby, Linked Left+Right.", lines[2]);
    Assert.Equal(4, lines.Count);
    Assert.Contains("The Fenix EFB does not let you change this option.",
        FenixOptionCatalog.DetailLines(S("is_sharklet", "true", category: "None", writable: false)));
}

[Fact] public void Notes_cover_the_eleven_options_in_order()
{
    Assert.Equal(new[] { "encoder_acceleration_enabled", "efis_baro_linking_type", "fd_ls_sync_enabled", "channel_spacing_type",
        "fcu_readouts_type", "weight_unit", "baro_unit", "control_position_type", "incremental_thrust_handling_enabled",
        "auto_toga_enabled", "tcas_ap_enabled" }, FenixOptionKeys.AffectsApp);
    Assert.All(FenixOptionKeys.AffectsApp, k => Assert.False(string.IsNullOrWhiteSpace(FenixOptionNotes.AppNote(k))));
    Assert.Null(FenixOptionNotes.AppNote("registration"));
}
```

- [ ] **Step 2: Run them; they fail to compile.** `… --filter FenixOptionCatalogTests`
- [ ] **Step 3: Implement the three files** per Interfaces and the two tables above. `IsSecret` uses `FenixOptionKeys.Secrets.Contains(key, StringComparer.Ordinal)`.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git add` the four files; `git commit -m "feat(fenix): option catalog: masking, Fenix's value labels, app notes (plan 11)"`

### Task 3: Grouping, order and status texts

**Files:**
- Create: `MSFSBlindAssist/Services/FenixOptionGrouping.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionGroupingTests.cs`

**Interfaces:**
- Consumes: Task 2 `FenixOptionCatalog.Label/RowText/Humanize`, `FenixOptionKeys.AffectsApp`, `FenixOptionNotes.TestedValue`.
- Produces:
  - `public sealed record FenixOptionGroup(string Name, IReadOnlyList<FenixSetting> Options) { public string NodeText => $"{Name} ({Options.Count})"; }`
  - `public static class FenixOptionGrouping` with `const string AffectsAppGroup = "Options that affect MSFS Blind Assist"`, `const string OtherGroup = "Other, not in the Fenix EFB"`, `const string LoadingText = "Loading Fenix options..."`, `const string NoAnswerText = "The Fenix did not answer. Load the Fenix in the simulator, then press F5."`, `IReadOnlyList<FenixOptionGroup> Group(IReadOnlyList<FenixSetting> settings)`, `int CountChanged(IReadOnlyList<FenixSetting> before, IReadOnlyList<FenixSetting> after)`, `IReadOnlyList<string> Untested(IReadOnlyList<FenixSetting> settings)`, `string StatusText(string? version, IReadOnlyList<FenixSetting> settings, bool changesAllowed)`, `string RefreshText(int changed)`.

Rules: first the affecting group (keys in `FenixOptionKeys.AffectsApp` order, only those present; omitted when none), then one group per category named `Humanize(category)` sorted with `StringComparer.InvariantCultureIgnoreCase`, then `OtherGroup` for category `None` or "". An affecting option appears in both its own group and the first. Inside a category: the EFB's `compareSettings`: two `^gpws(\d+)([a-z|_]*)$` keys (ignore case) by altitude descending, then suffix ordinal; otherwise by `Label` with `InvariantCultureIgnoreCase`; use a stable sort (`OrderBy` with an `IComparer`). `CountChanged` counts keys whose `Value` differs (ordinal) plus keys present on one side only. `Untested` returns, in `FenixOptionKeys.AffectsApp` order, `RowText` of each present option whose `TestedValue` is non-null and differs (booleans compared by `bool.TryParse`, others ordinal). `StatusText` = `$"Fenix {version ?? "version unknown"}: {n} options. "` + (`changesAllowed` ? "F2 changes an on/off or choice option for this simulator session." : "Read only: change options in the Fenix EFB.") + (untested any ? `$" Not what MSFS Blind Assist is tested with: {string.Join("; ", untested)}."` : ""). `RefreshText`: 0 → "Refreshed, no changes."; 1 → "Refreshed, 1 option changed."; n → `$"Refreshed, {n} options changed."`.

- [ ] **Step 1: Write the failing tests.** Fixture `LiveEleven()`: the 11 affecting options as read live on the IAE A321 (2026-10-21; `docs/fenix-v3/evidence/settings.tsv` has the CFM A320 equivalents): encoder `false` Realism; efis_baro_linking_type `Independent` Realism `[Independent, LinkedWithStandby, Linked]`; fd_ls `false` Realism; channel_spacing `Khz8d33` Aircraft `[Khz25, Khz8d33]`; fcu_readouts `Old` Misc `[Old, New]`; weight_unit `Pounds` Aircraft `[Kilograms, Pounds]`; baro_unit `Inhg` Aircraft `[Hpa, Inhg]`; control_position `Captain` Misc `[Captain, FirstOfficer]`; incremental_thrust `false` Misc; auto_toga `true` Aircraft; tcas_ap `false` Aircraft.

```csharp
[Fact] public void Live_snapshot_reads_as_the_pilot_hears_it()
{
    var first = FenixOptionGrouping.Group(LiveEleven())[0];
    Assert.Equal("Options that affect MSFS Blind Assist (11)", first.NodeText);
    Assert.Equal(new[] { "Encoder Acceleration: Off", "EFIS Linking: Baro: Independent", "EFIS Linking: FD/LS: Off",
        "Channel Spacing: 8.33 kHz", "FCU Readouts: Old", "Weight Unit: Pounds", "Barometric Unit: inHg",
        "Control Position: Captain", "Incremental Thrust Handling: Off", "Auto TOGA: On", "AP/FD TCAS: Off" },
        first.Options.Select(FenixOptionCatalog.RowText));
}

[Fact] public void Groups_come_in_order()
{
    var cats = new[] { "Realism", "None", "Printer", "Misc", "GroundServices", "Connections", "Cabin", "Audio", "Aircraft" };
    var list = cats.Select((c, i) => S($"k{i}", "true", category: c)).Append(S("encoder_acceleration_enabled", "false", category: "Realism")).ToList();
    Assert.Equal(new[] { "Options that affect MSFS Blind Assist", "Aircraft", "Audio", "Cabin", "Connections", "Ground Services",
        "Misc", "Printer", "Realism", "Other, not in the Fenix EFB" }, FenixOptionGrouping.Group(list).Select(g => g.Name));
}

[Fact] public void Affecting_group_keeps_its_order_skips_missing_and_is_omitted_when_empty()
{
    var two = new[] { S("fd_ls_sync_enabled", "false", category: "Realism"), S("encoder_acceleration_enabled", "false", category: "Realism") };
    var groups = FenixOptionGrouping.Group(two);
    Assert.Equal(new[] { "encoder_acceleration_enabled", "fd_ls_sync_enabled" }, groups[0].Options.Select(o => o.Key));
    Assert.Equal(2, groups.Single(g => g.Name == "Realism").Options.Count);
    Assert.Equal("Aircraft", FenixOptionGrouping.Group(new[] { S("registration", "N1", "System.String") })[0].Name);
}

[Fact] public void Callouts_sort_by_altitude_then_others_by_label()
{
    var audio = new[] { "mute_cabin_audio_in_background", "gpws10", "gpws500_glide_deviation", "gpws2500b", "gpws500", "gpws2500" }
        .Select(k => S(k, "true", category: "Audio")).ToList();
    Assert.Equal(new[] { "gpws2500", "gpws2500b", "gpws500", "gpws500_glide_deviation", "gpws10", "mute_cabin_audio_in_background" },
        FenixOptionGrouping.Group(audio)[0].Options.Select(o => o.Key));
    var aircraft = new[] { S("weight_unit", "Pounds", "Enum"), S("ac_ess_auto_switching_enabled", "false"), S("baro_unit", "Inhg", "Enum") };
    Assert.Equal(new[] { "ac_ess_auto_switching_enabled", "baro_unit", "weight_unit" },
        FenixOptionGrouping.Group(aircraft).Single(g => g.Name == "Aircraft").Options.Select(o => o.Key));
}

[Fact] public void Unknown_category_is_humanized_and_sorted()
    => Assert.Equal(new[] { "Aircraft", "Datalink Things" },
        FenixOptionGrouping.Group(new[] { S("x1", "true", category: "DatalinkThings"), S("x2", "true") }).Select(g => g.Name));

[Fact] public void CountChanged_counts_changes_additions_and_removals()
    => Assert.Equal(2, FenixOptionGrouping.CountChanged(new[] { S("a", "1"), S("b", "2") }, new[] { S("a", "1"), S("b", "3"), S("c", "4") }));

[Fact] public void Status_texts()
{
    Assert.Equal("Fenix 3.0.0.7426: 11 options. Read only: change options in the Fenix EFB.",
        FenixOptionGrouping.StatusText("3.0.0.7426", LiveEleven(), changesAllowed: false));
    var risky = LiveEleven().Select(s => s.Key switch
    {
        "encoder_acceleration_enabled" => s with { Value = "True" },
        "efis_baro_linking_type" => s with { Value = "Linked" },
        _ => s,
    }).ToList();
    Assert.Equal("Fenix version unknown: 11 options. Read only: change options in the Fenix EFB. Not what MSFS Blind Assist is tested with: "
        + "Encoder Acceleration: On; EFIS Linking: Baro: Linked Left+Right.", FenixOptionGrouping.StatusText(null, risky, false));
    Assert.EndsWith("F2 changes an on/off or choice option for this simulator session.", FenixOptionGrouping.StatusText("3.0.0.7426", LiveEleven(), true));
    Assert.Equal("Refreshed, no changes.", FenixOptionGrouping.RefreshText(0));
    Assert.Equal("Refreshed, 1 option changed.", FenixOptionGrouping.RefreshText(1));
    Assert.Equal("Refreshed, 3 options changed.", FenixOptionGrouping.RefreshText(3));
}
```

- [ ] **Step 2: Run them; they fail to compile.** `… --filter FenixOptionGroupingTests`
- [ ] **Step 3: Implement `FenixOptionGrouping.cs`** per Interfaces and Rules.
- [ ] **Step 4: Run them; all pass.**
- [ ] **Step 5: Commit.** `git commit -m "feat(fenix): option groups, order and status texts (plan 11)"` (the two files).

### Task 4: The "Fenix options" window and its registry entry

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixOptionsForm.cs`, `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Options.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionsFormSourceTests.cs`

**Interfaces:**
- Consumes: plan 01 `static partial void AddOptionsWindows(List<FenixWindowEntry> entries)`, `FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open)`, `FenixWindowContext(FenixA320Definition Definition, FenixDataService Data, SimConnectManager SimConnect, ScreenReaderAnnouncer Announcer, Form Owner)`, `FenixA320Definition.ShowFenixWindow<T>(Func<T> factory, Action<T> show)`, `FenixDataService.QueryAsync(string query, object? variables = null, CancellationToken ct = default)`, `FenixDataQueries.SettingsQuery/ParseSettings/ParseSoftwareVersion`; Tasks 1-3.
- Produces: `public sealed class FenixOptionsForm : Form` with `public FenixOptionsForm(FenixDataService data, ScreenReaderAnnouncer announcer)` and `public void ShowForm()`; registry entry `new FenixWindowEntry("Fenix options", "Every Fenix EFB option with its value; the options that change how MSFS Blind Assist works come first", ctx => ctx.Definition.ShowFenixWindow(() => new FenixOptionsForm(ctx.Data, ctx.Announcer), f => f.ShowForm()))`.

Window (title "Fenix options"), tab order: tree, details, status, Close.
- `NativeAccessibleTreeView` `AccessibleName = "Fenix options"`, `HideSelection = false`. One node per group (`NodeText`, `Tag` = the group), each with a "Loading..." child tagged `"placeholder"` filled on `BeforeExpand` with one node per option (`Text` = `RowText`, `Tag` = the key); pattern: `Forms/FirstOfficer/FirstOfficerForm.cs:663` (`PopulateChecklistTree`) and `:698` (`ChecklistTree_BeforeExpand`). After the first load: expand group 0, select its first option, focus the tree (the reader then says "Encoder Acceleration: Off").
- Details: read-only multiline `TextBox` (`AccessibleName = "Details"`, vertical scroll bar) = `DetailLines` joined with `Environment.NewLine` for the selected option; empty for a group node.
- Status: read-only `TextBox` (`AccessibleName = "Status"`): `LoadingText`, then `StatusText(version, settings, changesAllowed: false)` or `NoAnswerText`. Model: `MakeStatusBox`, `FirstOfficerForm.cs:1409`.
- Keys (`KeyPreview`): F5 refresh; Escape closes; Enter on an option node speaks `string.Join(" ", DetailLines)` with `AnnounceImmediate` (`SuppressKeyPress`; the reader cannot say the description). Close restores the previous foreground window (as `Forms/FenixA320/FenixBaroWindow.cs:42-51, 165-171`).
- Load (`async Task LoadAsync(bool speak)`): ignore a second load while one runs (F5 then speaks "Still loading."); `data = await _data.QueryAsync(FenixDataQueries.SettingsQuery)`; `if (IsDisposed) return;`. Null → status `NoAnswerText`, keep any previous tree, speak `NoAnswerText` if `speak`. Otherwise parse version and settings; when every group name and each group's key sequence equal the previous load, update populated nodes' texts in place (selection and expansion kept); else rebuild and reselect the same key (first occurrence) or group. If `speak`, `AnnounceImmediate(RefreshText(CountChanged(old, new)))`. Opening loads with `speak: false`; `ShowForm` on an already-open window (plan 01 reuses tracked windows) activates it and reloads with `speak: false`; F5 loads with `speak: true`.
- Logging: `Log.Debug("FenixOptions", …)` with counts or keys only (Global Constraints).

- [ ] **Step 1: Write the failing tests.**

```csharp
public class FenixOptionsFormSourceTests
{
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(p).ToArray()));

    [Fact] public void Tree_is_the_native_accessible_one_and_status_is_never_a_label()
    {
        string form = Src("Forms", "Fenix", "FenixOptionsForm.cs");
        Assert.Contains("new NativeAccessibleTreeView", form);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?TreeView\b", form);
        Assert.DoesNotMatch(@"new\s+(System\.Windows\.Forms\.)?Label\b", form);
        Assert.DoesNotContain("CheckBoxes = true", form);
        Assert.DoesNotContain("ConfigureAwait(false)", form);
    }

    [Fact] public void Never_logs_an_option_value()
    {
        var root = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist");
        var files = Directory.GetFiles(Path.Combine(root, "Services"), "FenixOption*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "Forms", "Fenix"), "FenixOptions*.cs"));
        foreach (var line in files.SelectMany(File.ReadAllLines).Where(l => l.Contains("Log.")))
            Assert.DoesNotMatch(@"\.Value\b|RowText|FormatValue|DetailLines", line);
    }

    [Fact] public void Registry_lists_Fenix_options_once()
        => Assert.Single(MSFSBlindAssist.Forms.Fenix.FenixWindowRegistry.Build(), e => e.Name == "Fenix options");
}
```

- [ ] **Step 2: Run them; they fail.** `… --filter FenixOptionsFormSourceTests`
- [ ] **Step 3: Implement the form and the registry partial** as above.
- [ ] **Step 4: Run the whole suite and build.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` then `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`; all green.
- [ ] **Step 5: Live check (app built, sim up with the Fenix loaded, owner at the keyboard; reads only).** `[` then Ctrl+Shift+F, select "Fenix options", Enter: the reader says "Encoder Acceleration: Off" (or On); Tab twice: status "Fenix 3.0.0.7426: 149 options. Read only: change options in the Fenix EFB." on the IAE A321 (146 on the CFM A320); Enter on a row speaks its note and description; F5 says "Refreshed, no changes."; Tab back, collapse and expand "Connections (…)": the Hoppie, Navigraph, Pushover and SimBrief rows say "set, hidden" or "not set" and no value appears in `%APPDATA%\MSFSBlindAssist\logs\debug.log`; switch to another aircraft profile with the window open: it closes, no error in debug.log. If the sim is not up, stop and ask the owner to load the Fenix.
- [ ] **Step 6: Commit.** `git commit -m "feat(fenix): Fenix options window in the Fenix windows list (plan 11)"` (the three files).

### Task 5 (D1 = b, decided by the owner 2026-10-21; index answer 5): Change an option for this session

**Files:**
- Create: `MSFSBlindAssist/Services/FenixOptionEdits.cs`, `MSFSBlindAssist/Forms/Fenix/FenixOptionsChangeDialog.cs`
- Modify: `MSFSBlindAssist/Forms/Fenix/FenixOptionsForm.cs` (F2 handler; `changesAllowed: true`)
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionEditsTests.cs`

**Interfaces:**
- Produces: `public sealed record FenixOptionChoice(string Value, string Label);` and `public static class FenixOptionEdits` with `public const string SetSettingDocument = "mutation SetSetting($setSetting: SetSettingInput!) { settings { setSetting(setSetting: $setSetting) { success } } }";`, `bool CanChange(FenixSetting s)` (`Writable`, not secret, key not `charts_provider_type` (the EFB hides it), and `Type` is `System.Boolean` or `Enum` with at least 2 `PossibleValues`), `IReadOnlyList<FenixOptionChoice> Choices(FenixSetting s)`, `object Variables(string key, string value)`, `bool Succeeded(JObject? data)` (`data.settings.setSetting.success == true`), `bool NeedsConfirm(FenixSetting s)` (true for the cockpit-reshaping keys `standby_type`, `isis_type`, `adirs_type`, `acp_type`, `dcdu_enabled`; owner answer 5), and `string ConfirmText(string optionLabel, string choiceLabel)` = `$"Change {optionLabel} to {choiceLabel}? This changes your cockpit panels until MSFS closes."`. `FenixOptionsChangeDialog(string optionLabel, IReadOnlyList<FenixOptionChoice> choices, string? currentValue)` with `string? SelectedValue`: title "Change Fenix option", a DropDownList `ComboBox` named `$"{optionLabel}, for this simulator session"` on the current value, OK (`AcceptButton`) and Cancel (`CancelButton`).

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void Only_writable_on_off_and_choice_options_can_change()
{
    Assert.True(FenixOptionEdits.CanChange(S("encoder_acceleration_enabled", "false", category: "Realism")));
    Assert.True(FenixOptionEdits.CanChange(S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
    Assert.False(FenixOptionEdits.CanChange(S("hoppie_key", "", "System.String", "Connections")));
    Assert.False(FenixOptionEdits.CanChange(S("simbrief_id", "x", "System.String", "Connections")));
    Assert.False(FenixOptionEdits.CanChange(S("type_code", "A321", "Enum", "None", new[] { "A320", "A321" }, writable: false)));
    Assert.False(FenixOptionEdits.CanChange(S("registration", "N1", "System.String")));
    Assert.False(FenixOptionEdits.CanChange(S("printer_font_size", "10", "System.Single", "Printer")));
    Assert.False(FenixOptionEdits.CanChange(S("charts_provider_type", "Navigraph", "Enum", "Connections", new[] { "Navigraph", "Simulator" })));
}

[Fact] public void Choices_are_sent_as_the_EFB_sends_them()
{
    Assert.Equal(new[] { new FenixOptionChoice("true", "On"), new FenixOptionChoice("false", "Off") },
        FenixOptionEdits.Choices(S("fd_ls_sync_enabled", "false", category: "Realism")));
    Assert.Equal(new[] { new FenixOptionChoice("Khz25", "25 kHz"), new FenixOptionChoice("Khz8d33", "8.33 kHz") },
        FenixOptionEdits.Choices(S("channel_spacing_type", "Khz8d33", "Enum", possible: new[] { "Khz25", "Khz8d33" })));
}

[Fact] public void Variables_are_a_session_change()
    => Assert.Equal("{\"setSetting\":{\"key\":\"encoder_acceleration_enabled\",\"value\":\"true\",\"persist\":false}}",
        JObject.FromObject(FenixOptionEdits.Variables("encoder_acceleration_enabled", "true")).ToString(Formatting.None));

[Fact] public void Cockpit_reshaping_options_ask_first()
{
    Assert.True(FenixOptionEdits.NeedsConfirm(S("adirs_type", "Adirs", "Enum", possible: new[] { "Gnadirs", "Adirs" })));
    Assert.True(FenixOptionEdits.NeedsConfirm(S("dcdu_enabled", "false")));
    Assert.False(FenixOptionEdits.NeedsConfirm(S("encoder_acceleration_enabled", "false", category: "Realism")));
    Assert.Equal("Change Air Data Inertial Reference System (ADIRS) to GNADIRS? This changes your cockpit panels until MSFS closes.",
        FenixOptionEdits.ConfirmText("Air Data Inertial Reference System (ADIRS)", "GNADIRS"));
}

[Fact] public void Succeeded_reads_success()
{
    Assert.True(FenixOptionEdits.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":true}}}")));
    Assert.False(FenixOptionEdits.Succeeded(JObject.Parse("{\"settings\":{\"setSetting\":{\"success\":false}}}")));
    Assert.False(FenixOptionEdits.Succeeded(null));
}
```

- [ ] **Step 2: Run them; they fail to compile.** `… --filter FenixOptionEditsTests`
- [ ] **Step 3: Implement.** F2 on an option node: `!CanChange` → speak "MSFS Blind Assist cannot change this option."; else show the dialog modally; on OK with a different value, if `NeedsConfirm`, a Yes/No box with `ConfirmText` and **No** as the default (pattern `MSFSBlindAssist/Forms/GateTeleportForm.cs:476-481`), No → nothing sent, nothing spoken; then `await _data.QueryAsync(SetSettingDocument, Variables(key, value))`, `if (IsDisposed) return;`; not `Succeeded` → speak "The Fenix did not accept the change."; else reload silently and speak the new `RowText` (a read-back, CORE-7), or `$"Fenix kept {RowText}."` when the re-read value is not the one sent. Status uses `changesAllowed: true`. Never log the value.
- [ ] **Step 4: Run the whole suite and build; all green.**
- [ ] **Step 5: Owner step E1 (first write ever; owner present, sim up, on the ground).** In Fenix options on "Encoder Acceleration: Off": F2, choose On, Enter → "Encoder Acceleration: On"; Enter on the row → details end with the session sentence ("…will reset when you close MSFS or change aircraft/livery."); F2, Off, Enter → "Encoder Acceleration: Off". Any other answer: stop, record it, do not commit Task 5.
- [ ] **Step 6: Commit.** `git commit -m "feat(fenix): change a Fenix option for this session from the options window (plan 11)"`

### Task 6 (owner request, 2026-10-21; index answer 19): permanent change and reset to the Fenix default

Why: the EFB's per-option menu (the unnamed "overflow" button after each option) lets a sighted pilot make a change permanent ("Set as user override", `persist: true`, which "will apply regardless of livery or Fenix defaults") and go back to the default (`setDefault`); by the owner principle the window offers both.

**Files:**
- Modify: `MSFSBlindAssist/Services/FenixOptionEdits.cs`, `MSFSBlindAssist/Forms/Fenix/FenixOptionsForm.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixOptionEditsTests.cs`

**Interfaces:**
- Consumes: Task 5's `FenixOptionEdits`, `FenixOptionsChangeDialog`.
- Produces: `object Variables(string key, string value, bool persist)` (Task 5's two-argument form keeps `persist: false`); `public const string SetDefaultDocument` = the bundle's `SetDefault` mutation verbatim (`docs/fenix-v3/evidence/gql-operations.txt`); `bool DefaultSucceeded(JObject? data)` (`data.settings.setDefault.success == true`); `string PermanentConfirmText(string optionLabel, string choiceLabel)` = `$"Make {optionLabel} {choiceLabel} your permanent setting? It applies to every flight and livery until you change it."`; `string ResetConfirmText(string optionLabel)` = `$"Reset {optionLabel} to the Fenix default?"`.

- [ ] **Step 1: Read what the EFB's overflow menu sends.** In the EFB bundle (fetch with `docs/fenix-v3/tools/fetch_gateway_bundles.py`), find the overflow menu's items and the call each makes (`setSetting` with `persist: true`, `setDefault(key)`, or anything else), and the label the EFB shows for each. Quote them in this task; never guess. If the menu offers something else (e.g. "remove override" with a different call), mirror that instead and keep the same confirm-first pattern.
- [ ] **Step 2: Write the failing tests:** `Variables_can_be_permanent` (`Variables("weight_unit", "Kilograms", persist: true)` serialises `"persist":true`); `Default_succeeded_reads_success`; `Permanent_and_reset_ask_first` (the two exact confirm texts above, de-DE).
- [ ] **Step 3: Run them; they fail.**
- [ ] **Step 4: Implement.** Shift+F2 on an option that `CanChange`: the same dialog titled "Make a Fenix option permanent", its ComboBox named `$"{optionLabel}, permanent"`; then always a Yes/No box with `PermanentConfirmText`, **No** default; then `setSetting` with `persist: true`; read back as Task 5. Ctrl+F2: a Yes/No box with `ResetConfirmText`, **No** default; then `SetDefaultDocument` with the key; read back and speak the new `RowText`. A failure speaks "The Fenix did not accept the change." Never on a secret; never `setSettings` or `setDefaults` (all-option resets have no single-option EFB equivalent).
- [ ] **Step 5: Run the whole suite and build; all green.**
- [ ] **Step 6: Owner step E2 (after E1).** On "Encoder Acceleration: Off": Shift+F2, On, Enter, Yes → "Encoder Acceleration: On"; close and reopen MSFS (or switch livery): still On; Ctrl+F2, Yes → back to the Fenix default ("Encoder Acceleration: Off"). Any other answer: stop and record it.
- [ ] **Step 7: Commit.** `git commit -m "feat(fenix): make a Fenix option permanent or reset it to the default (plan 11)"`

### Task 6: Hand back

- [ ] **Step 1:** Full suite and solution build green; `git diff --name-only <base-sha>` lists only this plan's files.
- [ ] **Step 2:** Ask the owner for M1-M4 (below; E1 first if D1 = b). Write each heard phrase into "Measurement results" at the end of this file, and mark which handoff (R1-R4) applies.
- [ ] **Step 3:** Hand back the branch and this file's handoff section to plan 12.

## In-sim measurements (owner; prioritised; reads and the owner's own switch presses only)

Setup for all: Fenix loaded (any variant), parked, aircraft powered (batteries plus external power or APU), FCU and EFIS lit, MSFS Blind Assist connected on the Fenix profile, Fenix options window open (`[`, Ctrl+Shift+F, "Fenix options"). To change an option: D1 = b, F2 on its row; otherwise Fenix EFB (`[` then Shift+T) → Settings → the category → the option; then F5 in Fenix options and listen to the row. Put every option back as it was at the end.

**M1, Encoder Acceleration (a wrong FCU target is silent: the set dialogs say nothing after the set, `FenixA320Definition.cs:13000`).**
1. Row reads "Encoder Acceleration: Off". Baseline: `[` Ctrl+A, type 15000, Enter; wait 3 s; `]` Shift+A → "FCU altitude 15000, selected" (or "…, managed").
2. Turn Encoder Acceleration on; row reads "Encoder Acceleration: On".
3. `[` Ctrl+A 7000 Enter; wait 3 s; `]` Shift+A. Works: "FCU altitude 07000, …". Misbehaves: any other altitude.
4. `]` Shift+S. If "FCU speed …, selected": `[` Ctrl+S 250 Enter, wait 3 s, `]` Shift+S. Works: "FCU speed 250, selected". If "Speed is managed", skip.
5. `[` Ctrl+B, Mode QNH, Unit "QNH (hPa)", Value 1020, Enter. Works: "Altimeter set: 1020, 30.12". Then 1005 → "Altimeter set: 1005, 29.68".
6. FCU Readouts to New, `]` Shift+A → the step 3 phrase again (any other answer is a finding for the `fcu_readouts_type` note); FCU Readouts back to Old; Encoder Acceleration back off.

**M2, EFIS Linking: FD/LS (the First Officer could switch both flight directors off).** Settings > First Officer: "Speak flow progress" ticked.
1. EFIS Linking: FD/LS on. Glareshield > EFIS Left > "EFIS Left FD", press once. Expected: "EFIS 1 FD Fault: Off" and "EFIS 2 FD Fault: Off" (the link follows our L:var press). If only EFIS 1 changes, record it and press "EFIS Right FD" too.
2. `]` Shift+C, Flows tab, "Preflight", Enter. Works: "Flight director 1: ON", both lights "…: On", then "Already set: Flight director 2: ON". Misbehaves: "Flight director 2: ON" followed by "EFIS 1 FD Fault: Off" / "EFIS 2 FD Fault: Off".
3. Both LS lights off, then the "Approach" flow (on the ground it also turns the seat belts on and chimes the cabin). Works: "LS captain: ON", then "Already set: LS first officer: ON". Misbehaves: the LS lights go off after "LS first officer: ON".
4. EFIS Linking: FD/LS back off; FDs on.

**M3, EFIS Linking: Baro (an altimeter set may land a step off).**
1. EFIS Linking: Baro → "Linked Left+Right". `[` Ctrl+B, QNH, "QNH (hPa)", 1020, Enter → expect "Altimeter set: 1020, 30.12". Read both sides with Appendix B: `python -I read_names.py system.numerical.N_FCU_EFIS1_BARO_HPA system.numerical.N_FCU_EFIS2_BARO_HPA` → works: both `1020`. Listen also for any later "EFIS2 BARO HPA Value: …" announcement (a sign the right side moved after the set).
2. Unit "Inches (inHg)", 29.92, Enter → expect "Altimeter set: 1013, 29.92"; read `…_EFIS1_BARO_INCH` and `…_EFIS2_BARO_INCH` → both about `2992` (the data service gives inches x100).
3. Back to Independent.

**M4, Channel Spacing (a typed 8.33 kHz frequency may be rounded silently).** RMP 1 on, VHF 1 selected.
1. Row reads "Channel Spacing: 8.33 kHz". Pedestal > "Radio Management Panel (RMP)" > "COM1 Standby": 121.800, Enter → "RMP1 standby 121.800". Then 118.010 → works: "RMP1 standby 118.010" (record what is said).
2. Channel Spacing → 25 kHz. COM1 Standby 118.010, Enter → record what is said ("RMP1 standby 118.000", "…118.025", or nothing). Works for a pilot: the RMP's answer is spoken; misbehaves: silence or a frequency the pilot did not type with no warning.
3. Back to 8.33 kHz.

Not measured (note only): `control_position_type` First Officer (ACP behaviour), `auto_toga_enabled`, `tcas_ap_enabled`, `incremental_thrust_handling_enabled` (code shows no app path, see Task 2).

## Handoff to plan 12

**Pilot-facing changes.**
- "Fenix windows" (`[` then Ctrl+Shift+F) lists "Fenix options": every option of the Fenix EFB with its value in Fenix's own words, grouped as in the EFB; the eleven that can change how the app works come first, each with a note (Enter speaks it, Tab shows it); F5 refreshes; Hoppie, Navigraph, Pushover and SimBrief credentials read "set, hidden". With D1 = b: F2 changes an on/off or choice option until MSFS closes.
- Guide (CORE-19): Fenix chapter, new section "Fenix options"; hotkey list unchanged (plan 01's key).
- Changelog (`feature`): "Fenix: the new Fenix options window lists every Fenix EFB option with its value and tells you which ones change how MSFS Blind Assist works."
- After M1-M4: update `FenixOptionNotes` copy (measured effect or "no effect, measured <date>").

**Proposed rule lines** (fenix-data.md; R5 adds the globs they need; 361, 356 and 276 characters; the third only if D1 = b):

```text
- [FNX-?] Fenix option secrets (`FenixOptionKeys.Secrets`: hoppie_key, navigraph_refresh_token, pushover_application_key, pushover_token, simbrief_id) match by EXACT key, never substring (a substring mask hid encoder_acceleration_enabled); `FenixOptionCatalog` never puts their value in a row, details, speech or a log. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] Fenix option labels, descriptions and value labels are the EFB's own (`FenixOptionLabels`, scoped per option: "Realistic" differs between refuel rate and failure rate); a key or value without one falls back to `FenixOptionCatalog.Humanize` (the EFB's humanizeKey) or the raw value, never our own wording. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] `FenixOptionEdits` changes one on/off or choice option at a time, as the EFB does: F2 session only (`persist: false`), Shift+F2 permanent (`persist: true`) and Ctrl+F2 `setDefault`, the last two always behind a Yes/No defaulting to No; never a secret, never `setSettings` or `setDefaults`; the read-back is spoken, never the request. Full: docs/invariants/fenix-data.md#fnx-?
```

**Handoff requests.**
- R1 → plan 02, if M1 misbehaves: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` `JumpCounter` (12847-12870) writes a whole delta at once, `SetFCUValueWithVerification` (12876-12949) retries in batches of 10, `AdjustBaroCounter` (12428-12437) one RPN delta. Make the sets land on target when acceleration is on (smaller spaced steps, or read `FenixOptionKeys.EncoderAcceleration` through `DataService.GetSettingsAsync()` before a set), and speak a final miss ("Altitude set to 7100, not 7000") instead of the silent log at 12947.
- R2 → plan 05, if M2 misbehaves: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` PF_FD1/PF_FD2 (175-178), AP_LS1/AP_LS2 (482-485), SD_LS1/SD_LS2 (620-623), and `FenixChecklistDefinitions.cs` 378-385 and 473-479: with FD/LS sync on, press one side and verify both.
- R3 → plan 02, if M3 misbehaves: `SetFCUBaro` (12301-12406) computes EFIS 2's delta from a cached read 500 ms after a write (12332-12335, 12363-12366) and does not wait after EFIS 1's correction (12354, 12382): when linked, set EFIS 1 only and verify EFIS 2 with `ReadFreshAsync`.
- R4 → plan 02, if M4 misbehaves: COM standby set (9392-9406) accepts any 118.000-136.975: check the value against the Fenix's spacing and say so, or speak the RMP's answer.
- R5 → ~~rule globs~~ (Covered by plan 01: its fenix-data.md globs are final and include these files.)
- R6 → plan 12: `docs/a32nx.md` gets a "Fenix options" section: where labels come from, Appendix A as the regeneration recipe, D1-D4 and the M1-M4 results; replace `docs/fenix-v3/evidence/fenix-options-labels.tsv` with Appendix A's output (the committed one drops 18 descriptions, truncates door automation's, keeps `→` escapes and loses scoped value labels).
- Also seen, not this plan: RMP frequencies spoken with `{freqMHz:F3}` in the PC's culture (`FenixA320Definition.cs:12154`, "132,475" on de-DE) → plan 02; "Setting altimeter to {inHgValue:0.00}" in the PC's culture (`Forms/FenixA320/FenixBaroWindow.cs:280`) → plan 12 (no owner); the FD and LS pushbutton lights are announced as "EFIS 1 FD Fault", "EFIS 2 LS Fault" and so on (`FenixA320Definition.cs:1258-1275, 1321-1338`) though the FO reads them as "FD on" → plan 02 to check against the cockpit.

**In-sim checks still needed:** Task 4 Step 5; E1 (D1 = b); M1-M4.

## Measurement results

(Filled in by the implementer from the owner's run: date, aircraft, each step's phrase, handoff triggered.)

## Appendix A: option label extractor (run with `python -I`, on a bundle in its own folder)

```python
"""Fenix EFB option labels from the chunk that defines the humanizeKey pipe. Parses the object
literal `const $={...}` before that pipe, so value labels stay scoped and text is never cut.
Usage: python -I option_labels.py <chunk.js> <out.tsv>   Columns: key value label description control"""
import sys
src = open(sys.argv[1], encoding="utf-8").read()
pipe = src.find('name:"humanizeKey"')
if pipe < 0: sys.exit("no humanizeKey pipe in this chunk")
i = src.rfind("={ac_ess_auto_switching_enabled:", 0, pipe)
if i < 0: i = src.rfind("={", 0, pipe)
i += 1
def ws():
    global i
    while src[i] in " \t\r\n": i += 1
def string():
    global i
    q = src[i]; i += 1; out = []
    while src[i] != q:
        if src[i] == "\\":
            n = src[i + 1]
            if n == "u": out.append(chr(int(src[i + 2:i + 6], 16))); i += 6; continue
            out.append({"n": "\n", "t": "\t", "r": "\r"}.get(n, n)); i += 2; continue
        out.append(src[i]); i += 1
    i += 1; return "".join(out)
def key():
    global i
    ws()
    if src[i] in "\"'": return string()
    j = i
    while src[i].isalnum() or src[i] in "_$": i += 1
    return src[j:i]
def value():
    global i
    ws()
    if src[i] == "{": return obj()
    if src[i] in "\"'": return string()
    j = i
    while src[i] not in ",}": i += 1
    return src[j:i].strip()
def obj():
    global i
    i += 1; o = {}
    while True:
        ws()
        if src[i] == "}": i += 1; return o
        k = key(); ws(); assert src[i] == ":"; i += 1; o[k] = value(); ws()
        if src[i] == ",": i += 1
def clean(s):
    t = " ".join(str(s).split()) if s else ""
    if "|" in t: sys.exit(f"'|' in text: {t!r}")
    return t
table = obj(); rows = 0
with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
    f.write("key\tvalue\tlabel\tdescription\tcontrol\n")
    for k, e in table.items():
        f.write(f"{k}\t\t{clean(e.get('label'))}\t{clean(e.get('description'))}\t{clean(e.get('controlType'))}\n"); rows += 1
        for v, ve in (e.get("options") or {}).items():
            f.write(f"{k}\t{v}\t{clean(ve.get('label'))}\t{clean(ve.get('description'))}\t\n"); rows += 1
print(f"{len(table)} options, {rows} rows", file=sys.stderr)
```

3.0.0.7426: `125 options, 159 rows` (34 value labels).

## Appendix B: read-only name reader (M3)

```python
"""Read-only. Usage: python -I read_names.py name1 name2 ..."""
import json, sys, urllib.request
names = sys.argv[1:]
parts = " ".join(f"r{i}: dataRef(name: {json.dumps(n)}) {{ value }}" for i, n in enumerate(names))
req = urllib.request.Request("http://127.0.0.1:8083/graphql", data=json.dumps({"query": f"query {{ dataRef {{ {parts} }} }}"}).encode(),
                             headers={"Content-Type": "application/json"})
data = (json.loads(urllib.request.urlopen(req, timeout=10).read()).get("data") or {}).get("dataRef") or {}
for i, n in enumerate(names): print(n, json.dumps((data.get(f"r{i}") or {}).get("value")), sep="\t")
```

Read live 2026-10-21 (IAE A321, parked): both EFIS `1013` hPa and `2991.389` (inches x100), ISIS `1013`, RMP 1 standby `124850`, active `122800` (kHz), FD lights `1`/`1`, EFIS 1 LS `0`.
