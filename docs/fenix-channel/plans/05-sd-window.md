# Fenix SD window Implementation Plan (plan 05)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> Task 0, the claim, is the controller's own, done before Task 1 is dispatched.

**Goal:** Output Alt+S opens the live "Fenix SD" window (its list "SD display") while the channel can read the SD: the
page shown, that page's values (ENG, ELEC, HYD, FUEL and STATUS here; plan 06's pages through the same dispatch) and the
bottom line, with the AI read kept whenever the SD is not trusted.

**Architecture:** `FenixChannelSd.Compose` turns one snapshot into the window's rows: the page's name (plan 01's
`FenixChannelSdPages.NameOf` of the frame's `FwcPage`), what `FenixChannelSdPages.Compose` gives for that page, then the
bottom line (field 178). This plan's page composers read their named values from the snapshot's RemoteData through a
reader that checks each field's wire shape: a page with a wrong shape or a missing message shows one row and stops only
itself, while an unknown letter in the SD's own text (the bottom line, STATUS) takes the whole window to its status row,
since the gate decodes none of that text. The window is a `FenixChannelDisplayForm`; the key seam and the list entry are
the plan's switch commit, pushed to the shared branch only after the in-sim checks.

**Tech Stack:** C# 13 / .NET 10, Windows Forms, xUnit; `FenixChannelPush`, `FenixChannelSpeech`,
`FenixChannelProtoWriter` (tests).

**Spec:** sections 2.1, 2.2 (the SD: display live, no cross-check), 2.4 (the window, the SD's content), 2.5 ("not
available" for a value not shown), 2.6, 3.5, 4, 6 (SD pages 0 and 13, per-page content and enum values, with plan 06);
answers 1, 2, 12.

## Global Constraints

- Spec section 4 and the index's "Shared conventions" apply to every task.
- Owned files (index): `Aircraft/FenixA320Definition.ChannelSd.cs`, `Services/FenixChannelSd.cs`,
  `Services/FenixChannelSdFrame*.cs`, `Services/FenixChannelSdPage.Status.cs`, `.Eng.cs`, `.Elec.cs`, `.Hyd.cs`,
  `.Fuel.cs`, `Services/FenixChannelFields.Sd.cs`, `Forms/Fenix/FenixChannelSdForm.cs`,
  `Forms/Fenix/FenixWindowRegistry.ChannelSd.cs`, and plan 01's `Services/FenixChannelSdPages.cs` after it lands (its
  `NameOf` only, should Task 1 find page 0 or 13 drawn) with its test file `FenixChannelSdPagesTests.cs`; new tests
  `tests/MSFSBlindAssist.Tests/FenixChannelSdWindowTests.cs`,
  `FenixChannelSdEngTests.cs`, `FenixChannelSdElecTests.cs`, `FenixChannelSdHydTests.cs`, `FenixChannelSdFuelTests.cs`;
  `docs/fenix-channel/evidence/05-sd-window.md` (new); the "SD window" sub-heading of `docs/a32nx.md`'s "Fenix display
  channel" section.
- Implements plan 01's `ChannelSdKey`, `AddChannelSdWindows`, `ComposeStatus`, `ComposeEng`, `ComposeElec`, `ComposeHyd`
  and `ComposeFuel`, and no other seam. The page seams are inert until the window can open, so they land with their
  pages. Nothing speaks until the in-sim checks pass: `ChannelSdKey` and `AddChannelSdWindows` are Task 9, the switch
  commit (index, "Shared conventions"), pushed to `feat/fenix-display-channel` only once Task 10's ground checks have
  passed; until then Alt+S keeps the AI read. Task 9 may sit on `fc-p05-sd-window` on the remote, where plan 06's runner
  builds on it. A difference Task 10 finds is fixed in its own commit before the switch is pushed. Every page this plan
  shows can be checked on the ground; the in-flight step records what only a flight shows and gates nothing.
- Rows (plan 06 states the same, so its pages read alike in this window):
  - Top to bottom, left to right. A label or unit the SD draws is written as drawn, in its capitals ("F.USED", "OIL",
    "QT", "PSI", "°C", "GW", "KG", "LBS"); where the SD draws a symbol, a plain lower-case word ("LP valves"), Q1.
  - A pair is engine 1 (or left) then engine 2 (or right): a unit drawn once between them follows the label ("OIL QT:
    13.9, 14.1"), a unit drawn beside each value follows that value ("GEN 1: 25 %, 115 V, 400 HZ").
  - Numbers are whole, or to one decimal where the SD draws one: invariant culture, `MidpointRounding.AwayFromZero`,
    never "-0". NaN and invalid data read "XX", the SD's crosses, as plan 04's E/WD (Q4, owner question: "XX" as drawn,
    or "not available", as the FBW A32NX's SD rows say where its SD shows XX). An empty text reads "not available"
    (spec 2.5), and so does a value whose meaning is unknown, which is also reported.
  - State words are those of the FBW A32NX's SD rows (`FlyByWireA320Definition.SdSystemRows`): every valve state through
    plan 01's `FenixChannelSdPages.ValveWord` ("open", "closed", "in transit", "failed open", "failed closed", "XX" for
    DataInvalid, else "not available"); buses "powered", "not powered"; pumps "running", "off", and the SD's own "LO".
    Named values never speak a color; channel text (STATUS, the bottom line) only through `FenixChannelSpeech.Line`,
    and text before a color letter, or a line with none, follows plan 01's landed rule (its Task 12), as the expected
    strings here do.
  - Task 1's captures rule labels, order and rounding: where one shows another word, the table follows it and the
    execution record says so.
- Absent is not zero. A field the push leaves out is its protobuf default, and the Fenix's valve enum sends Open as 0,
  which protobuf leaves out (a cruise push carried the closed valves and none of the open ones). A varint or double
  left out reads as its default; a nested message a row reads that is left out makes its page "not available, no field
  <path>", never its defaults.
- Fields: Task 2's, in `FenixChannelFields.Sd.cs`. Plan 01 declares those several plans read: the engines (129), fuel
  used (134.13), `Remote.IsKg` (154; absent means LBS), `Remote.ApuData` with the complete `ApuData`, and
  `ValveStateEnum` (absent is Open). The airframe is no channel field: the channel's aircraftType is left out at its
  default, so the FUEL layout follows `FenixChannelSdPageContext.Variant.Type`, the definition's `Variant` read at each
  refresh. "ProsimRemoting" never appears here.
- Owner questions; the plan follows the recommendation until the owner rules (Task 10), and a ruling changes the words
  and their tests together. Q1: the words for what the SD draws as a symbol, as written in Tasks 5 to 8 (recommended:
  keep). Q2: the hydraulic reservoirs, drawn as a gauge, are spoken as numbers with no unit (recommended: yes). Q3: the
  bottom line's time is labeled "UTC", though the SD prints only "15 H 06" (recommended: keep "UTC"). Q4: NaN and
  invalid data, which the SD draws as crosses, read "XX" as drawn, or "not available" (recommended: "XX").

## Review Focus

- A message the Fenix leaves out read as its defaults (every valve open, every bus not powered, 0 PSI): Task 6
  `A_missing_box_is_not_available_never_zero`, Task 8 `A_missing_valve_message_is_not_available_never_open`; and the
  other side, an open valve that protobuf leaves out reads open: Task 8 `An_absent_valve_reads_open`.
- Kilograms and pounds: F.USED, FOB and GW follow field 154, or every weight is 2.2 times off: Task 3
  `The_gw_unit_follows_field_154`, Task 5 `Fuel_used_follows_the_weight_unit`, Task 8 `Weights_follow_the_weight_unit`.
- The A321's FUEL page has no outer tanks, transfer valves or center pumps, though the channel sends values for all of
  them (a known value never proves a part exists, as [FNX-15] found for the cabin): the layout follows the variant's
  airframe: Task 8 `The_a321_fuel_page_has_no_outer_tanks_or_center_pumps`.
- SD text the gate never decodes: an unknown letter in the bottom line, or in the STATUS text while the STATUS page is
  shown, takes the whole window to one status row, and a bottom line of another length is a wrong shape: Task 3
  `An_unknown_letter_in_the_bottom_line_untrusts_the_window`, `A_bottom_line_of_another_length_is_a_wrong_shape`; Task
  4 `An_unknown_letter_in_the_status_text_untrusts_the_window`.
- A wrong shape stops its page only: one row "<PAGE>: not available, field ... has the wrong shape", while the page's
  name and the bottom line stay and the problem is reported: Task 5 `A_wrong_shape_stops_the_page_only`.

---

### Task 0 [controller]: Claim this plan

Who: whoever runs the plan; with subagents, the controller itself, never an implementer. A claim counts only once it is
on the remote branch.

- [ ] **Step 1: Check the plan is free on the remote.** `git fetch <remote>`, then
  `git grep -e "^- \*\*Status:\*\*" -e "^- \*\*Claimed by:\*\*" <remote>/feat/fenix-display-channel -- docs/fenix-channel/plans/05-sd-window.md`.
  Anything but "not started" and "none" means the plan is taken: stop.
- [ ] **Step 2: Branch and claim.** Branch as in the index's workflow step 1 (`fc-p05-sd-window`). In this file's
  "Execution record", set **Status** to claimed, **Claimed by** to your name and **Started** to today's date and the
  commit you branched from. Commit only that change: `docs(fenix-channel): claim plan 05`, with the `Plan: 05` trailer.
- [ ] **Step 3: Push the claim.** `git fetch <remote>`, `git rebase <remote>/feat/fenix-display-channel`,
  `git push <remote> HEAD:feat/fenix-display-channel`. If the rebase stops on these lines, someone claimed the plan
  first: `git rebase --abort` and stop. If the push is rejected, repeat this step. Task 1 starts only once the push has
  gone through.

### Task 1 [sim]: Measure what the code reads, before the code

The controller with the sim's owner, never an implementer. Fenix 3.0.0.7613 at a gate on external power; the team's
local channel tools (the page watcher and the push decoder, never committed); the simconnect MCP once the owner hands
the aircraft over, writing only as the definition does: an ECP key is its L:var (`S_ECAM_ENGINE`, `S_ECAM_ELEC`,
`S_ECAM_HYD`, `S_ECAM_FUEL`, `S_ECAM_STATUS`, `S_ECAM_ALL`) written 0, 1, 0 as `ExecuteButtonTransition` does ([FNX-6]),
its light `I_ECAM_<key>` read back; any other control through the definition's own write for it; never a write on the
channel ([FNX-21]). Each result goes, with the date and the build, into `docs/fenix-channel/evidence/05-sd-window.md`.
Without a sim, Tasks 2 to 8 start from the values marked "inferred" (each says so in a code comment), and this task
waits on the owner before Task 10.

- [ ] **Pages 0 and 13.** The page watcher from cold and dark through power-up and the SD's self-test, each ECP key, ALL
  pressed 15 times one second apart, STS, an engine start. A 0 or 13 shown with a page drawn: Task 3 gives it its name
  in `NameOf`, and this plan composes nothing for it. Neither seen: no change.
- [ ] **Presence and shapes.** One decoded push each at the gate, with the APU running and with the engines running:
  fields 16 to 26, 129, 132 to 134, 154, 175, 176 and 178, and each nested part, present or absent, with its wire type.
  A part absent in a normal state becomes optional in its page's task, and the rows it feeds read "not available".
- [ ] **Values.** FUEL X FEED (`S_OH_FUEL_XFEED`) on and off: 134.11.4 left out (Open, 0) and 1 (Closed), and a 2 if the
  watcher catches the valve moving (Transit). Plan 01's [owner] check confirms `ValveStateEnum`'s values; if this check
  finds otherwise, tell the owner and hand the fix to plan 12 ("Handoff to plan 12"), since plan 01's
  `FenixChannelFields.cs` is frozen (plan 06 runs at the same time); the composers keep using `ValveWord`.
  `ptuState` (132) with everything normal, with the yellow electric pump on (`S_OH_HYD_YELLOW_ELEC_PUMP`) and the
  engines off, and with the PTU button off (`S_OH_HYD_PTU`): each value with what the SD's PTU symbol shows (a
  capture), which become Task 2's `PtuStates` and Task 7's words. The variant on the
  owner's A320 and A321 (`debug.log`'s "Variant:" line), and on the A321 the FUEL page's tank layout (a capture). Field
  154 with the Fenix's weight unit in kilograms and in pounds (the Fenix options window, `weight_unit`, then a reload).
- [ ] **The frame.** 178's nine elements before and after the FMS has a gross weight; 175 and 176 with nothing to
  report (NORMAL) and with GEN 1 off (`S_OH_ELEC_GEN1`) and the engines running; every letter seen in them.
- [ ] **Labels.** A capture of the sim's SD (the team's sim-window capture) on ENG (also during the engine start), ELEC,
  HYD, FUEL and STATUS, compared with the rows of Tasks 4 to 8; each difference recorded for its task.

### Task 2: The SD's field constants

**Files:** Create `Services/FenixChannelFields.Sd.cs`.

**Interfaces:**
- Consumes: plan 01's partial `FenixChannelFields`, `Remote` and `FuelState` (`Used = 13`), `NullWrapped.Value`.
- Produces, each with the contract's member name and wire shape in a comment:
  - In `Remote`: `ElecFlags = 16`, `BusStatus = 17`, `Gen1 = 18`, `Gen2 = 19`, `Tr1 = 20`, `Tr2 = 21`, `EssTr = 22`,
    `ExtPwr = 23`, `Bat1 = 25`, `Bat2 = 26` (nested); `PtuState = 132` (varint int32); `HydraulicState = 133` (nested);
    `StatusLeft = 175`, `StatusRight = 176`, `EcamBottom = 178` (repeated nested NullWrapped string).
  - In plan 01's `FuelState`, its other members: `JetPumps = 1`, `LeftWing1 = 2`, `LeftWing2 = 3`, `Center1 = 4`,
    `Center2 = 5`, `RightWing1 = 6`, `RightWing2 = 7`, `Advisory = 8` (nested); `ValveAmberEngine1 = 9`,
    `ValveAmberEngine2 = 10` (varint bool); `ValveState = 11`, `Quantity = 12`, `Temperature = 14`, `ActPump = 15`
    (nested).
  - Nested classes, the members read: `ElecFlags` (GalleyShed 1, Bat1Charging 2, Bat2Charging 3, Gen1Disconnected 4,
    Gen2Disconnected 5, Gen1LowOilPressure 6, Gen2LowOilPressure 7; varint bool); `BusStatus` (DcBus1 1, DcBus2 2,
    DcEssential 3, AcBus1 4, AcBus2 5, DcBat 6, AcEssential 7, ExternalPowerConnected 8; varint bool); `GeneratorData`
    (Load 1, Voltage 2, Frequency 3, Temperature 5, fixed64; Selector 4, varint bool); `VoltAmps` (Volts 1, Amps 2;
    fixed64); `ExtPwrData` (Volts 1, Frequency 2; fixed64); `HydraulicState` (Green 1, Blue 2, Yellow 3, nested;
    EnginePumpLeftSwitch 4, EnginePumpLeftOutput 5, EnginePumpRightSwitch 6, EnginePumpRightOutput 7, varint bool;
    ElecPumpBlue 8, ElecPumpYellow 9, nested; ShutoffLeft 10, ShutoffRight 11, RatDeployed 12, varint bool);
    `HydSystemState` (Pressure 1, Quantity 2, fixed64; LowAirPressure 6, ReservoirOverheat 7, varint bool);
    `HydElecPumpState` (Switch 1, Output 2, Overheat 3; varint bool); `JetPumpData` (LeftValveOpen 3, RightValveOpen
    4; varint bool); `FuelPumpState` (Switch 1, LowPressure 3; varint bool); `FuelValveStates` (Engine1 1, Engine2 2,
    Apu 3, Crossfeed 4, LeftTransfer1 5, LeftTransfer2 6, RightTransfer1 7, RightTransfer2 8; varint ValveStateEnum);
    `FuelQuantityData` (LeftOuter 1, LeftInner 2, Center 3, RightInner 4, RightOuter 5, Total 6, Act1 7, Act2 8;
    fixed64); `FuelTemperatureData` (LeftOuter 1, LeftInner 2, RightInner 3, RightOuter 4; fixed64).
  - `EcamBottom`, the bottom line's elements by position (spec 3.5): `Tat = 0`, `Sat = 1`, `UtcHour = 4`,
    `UtcMinute = 5`, `GrossWeight = 6`, `Count = 9`.
  - `PtuStates`: the values Task 1 measured, each named for what the SD showed; none is declared unmeasured.

- [ ] **Step 1.** Write the file; the build (`dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`) and
  `FenixChannelReadOnlyTests` green. Commit: `feat(fenix-channel): the SD's field numbers`.
- [ ] **Step 2 [owner].** Every number and wire shape above checked against 3.0.0.7613 with the team's local channel
  tools ([FNX-23]); one that moved is fixed here alone, and the evidence file says so.

### Task 3: The window's rows, the reader and the form

**Files:** Create `Services/FenixChannelSd.cs`, `Services/FenixChannelSdFrame.cs`, `Forms/Fenix/FenixChannelSdForm.cs`,
`Aircraft/FenixA320Definition.ChannelSd.cs` (the factory only); modify `Services/FenixChannelSdPages.cs` (`NameOf` only,
and only if Task 1 found page 0 or 13 drawn) and then `tests/MSFSBlindAssist.Tests/FenixChannelSdPagesTests.cs` (plan
01's test file, which follows the code); test `tests/MSFSBlindAssist.Tests/FenixChannelSdWindowTests.cs` (new; its
`SdPush` helper builds a RemoteData, a frame, a variant and a trusted snapshot with connection 1, field numbers written
out, for this plan's other test files).

**Interfaces:**
- Consumes: `FenixChannelSnapshot`, `FenixChannelPush`, `FenixChannelField`, `FenixChannelRows`,
  `FenixChannelSection<T>` (`UnknownCode` among them), `FenixChannelSpeech.Line`, `FenixChannelText.Parse`,
  `FenixChannelSdPages.NameOf`, `.Compose` and `.ValveWord`, `FenixChannelSdPageContext` (Push, Frame, Variant,
  ReportProblem), `FenixChannelDisplayForm`, `FenixChannelMonitor.ReportFeatureProblem`, plan 01's `Remote.IsKg` and
  `ValveStateEnum`, `FenixA320Definition.Variant`.
- Produces (inert: nothing calls the factory before Task 9):
  - `internal sealed class FenixChannelSdReader` (in `FenixChannelSd.cs`): `static FenixChannelSdReader Of(FenixChannelPush message, Action<string> report)`
    (RemoteData, path ""); `string? Problem`, shared by every reader it hands out, the first problem winning (reads
    after it give defaults); `FenixChannelSdReader Message(int field)` (left out: "no field <path>"; not a readable
    message: "field <path> has the wrong shape"); `double Double(int field)` (fixed64; left out 0; NaN kept);
    `bool Bool(int field)` and `int Int(int field)` (varint, its low 32 bits; left out false and 0);
    `string Valve(int field)` (`ValveWord` of the varint, left out 0, Open; a value outside `ValveStateEnum` reads "not
    available" and is reported, "field <path> has an unknown value <n>", without stopping the page);
    `IReadOnlyList<string> Strings(int field)` (repeated NullWrapped, an element without its field 1 is ""); any other
    wire type than the one asked is "field <path> has the wrong shape"; `void Fail(string problem)`.
  - `internal static class FenixChannelSd`: `public const string DisplayName = "SD"`; `static string Whole(double v)`
    and `static string OneDecimal(double v)` (the Rows rules); `static string WeightUnit(FenixChannelSdReader remote)`
    ("KG" while 154 is set, else "LBS");
    `static FenixChannelRows Compose(FenixChannelSnapshot s, Aircraft.FenixVariant variant, Action<string> report)`.
  - `internal static class FenixChannelSdFrame`: `static FenixChannelSection<IReadOnlyList<string>> BottomLine(FenixChannelPush remote)`
    and `static string? UnknownCode(FenixChannelPush remote, int page)`.
  - `internal sealed class FenixChannelSdForm : FenixChannelDisplayForm` with
    `FenixChannelSdForm(Services.FenixChannelMonitor monitor, Func<Aircraft.FenixVariant> variant)`, composing with
    `s => FenixChannelSd.Compose(s, variant(), p => monitor.ReportFeatureProblem("SD", p))`.
  - In `FenixA320Definition.ChannelSd.cs`: `internal Forms.Fenix.FenixChannelSdForm CreateChannelSdForm(Services.FenixChannelMonitor monitor)`,
    `new(monitor, () => Variant)`: the variant is read at each refresh, on the UI thread, where `Variant` changes (plan
    04's form reads the engine family the same way).

- [ ] **Steps 1-5.** Rules: `Compose` with no push or frame is `NotAvailable("no data")`. Next `UnknownCode`: the first
  letter `FenixChannelText` does not know in the bottom line, and on page 14 in the STATUS text, is the whole result,
  in the gate's words (`FenixChannelSection<T>.UnknownCode`: "unknown code 'k' in the bottom line", "... in the STATUS
  text"; spec 2.6). Then the bottom line: no element is `NotAvailable("no bottom line")`, a count other than nine is
  `NotAvailable("field 178 has the wrong shape")` (the FMA's precedent), each reported. Otherwise the rows: the page row
  `NameOf(FwcPage)`; the page's `Compose`, with the context (the push, the frame, the variant, and a `ReportProblem`
  that reports "<page name>: <problem>"): its rows, or one row "<page name>: not available, <problem>" (reported the
  same way), or nothing; then the bottom line, each element through `FenixChannelSpeech.Line` and an empty one "not
  available": `TAT °C: <0>`, `SAT °C: <1>`, `UTC: <4> H <5>` (spoken as one coded line; Q3), `GW <unit>: <6>`. Elements
  2, 3, 7 and 8 are not read (spec 3.5 lists four values; Task 10 records what they hold). Tests:
  `An_unknown_page_shows_its_number_then_the_bottom_line` (page 42, never 0 or 13, which Task 1 may name;
  elements "g+15", "g+15", "", "", "15", "06", "c--", "", "": "page 42", "TAT °C: +15, green", "SAT °C: +15, green",
  "UTC: 15 H 06", "GW KG: --, cyan"; the UTC line has no color letter, so it follows plan 01's landed rule for text
  before a color letter), `The_gw_unit_follows_field_154` (154 left out, "g63400": "GW LBS: 63400, green"),
  `An_empty_element_reads_not_available`, `A_missing_bottom_line_makes_the_window_not_available`,
  `A_bottom_line_of_another_length_is_a_wrong_shape`, `An_unknown_letter_in_the_bottom_line_untrusts_the_window`
  ("k+15"), `The_reader_names_the_first_problem_by_path` (a varint at 133.1.1: "field 133.1.1 has the wrong shape"; 133
  left out: "no field 133"; a later problem never replaces the first),
  `Numbers_are_invariant_and_never_negative_zero` (de-DE `OneDecimal(13.94)` "13.9"; sv-SE `Whole(-52.5)` "-53" with
  an ASCII minus, `Whole(-0.4)` "0", `OneDecimal(-0.04)` "0.0"; `Whole(double.NaN)` "XX", Q4), and, only if Task 1
  found page 0 or 13 drawn, `A_drawn_page_0_or_13_has_its_name` (in `FenixChannelSdPagesTests.cs`: its name in
  `NameOf`, the measured word). Commit: `feat(fenix-channel): the SD window's rows`.

### Task 4: The STATUS page

**Files:** Create `Services/FenixChannelSdPage.Status.cs`; test `FenixChannelSdWindowTests.cs`.

**Interfaces:**
- Consumes: Task 3's reader and `FenixChannelSd.Compose`.
- Produces: `internal static FenixChannelRows StatusRows(FenixChannelSdPageContext ctx)` in the partial
  `FenixChannelSdPages`, and `ComposeStatus` setting `rows` to it. Every member this plan adds to that partial class is
  named for its page, so none meets one of plan 06's.

- [ ] **Steps 1-5.** Page 14: each line of 175 that `FenixChannelSpeech.Line` speaks, in order, then each of 176; blank
  lines are skipped; no line in either is `NotAvailable("no STATUS text")`. Tests:
  `The_status_page_reads_the_left_column_then_the_right` (175 "", "            gNORMAL" and 176 "aGEN 1": "STATUS",
  "NORMAL, green", "GEN 1, amber", then the bottom line), `Blank_status_lines_are_skipped`,
  `An_unknown_letter_in_the_status_text_untrusts_the_window` (page 14), `The_status_text_is_not_checked_on_other_pages`
  (page 13 with "kX" in 175: the rows as usual), `A_status_page_with_no_text_is_one_row_and_is_reported` ("STATUS",
  "STATUS: not available, no STATUS text", then the bottom line; the report received "STATUS: no STATUS text"),
  `Status_speech_is_the_same_in_every_culture` (de-DE). Commit: `feat(fenix-channel): the SD's STATUS page`.

### Task 5: The ENG page

**Files:** Create `Services/FenixChannelSdPage.Eng.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdEngTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader, `Whole`, `OneDecimal`, `WeightUnit`; plan 01's `EngineControlState`, `SingleEngineState`,
  `FuelState.Used`, `FuelUsedData`.
- Produces: `internal static FenixChannelRows EngRows(FenixChannelSdPageContext ctx)` and `ComposeEng`.

- [ ] **Steps 1-5.** Page 8 reads 129 (.1 and .2) and 134 (.13). Rows, each a pair: `F.USED <unit>: <134.13.1>,
  <134.13.2>` (whole); `OIL QT: <129.1.23>, <129.2.23>` (one decimal); `OIL PSI` (.24), `OIL °C` (.25) (whole);
  `VIB N1` (.11), `VIB N2` (.12) (one decimal); while 129.7, `NAC °C` (.7, whole); while 129.6, `IGN` ("A" for .17,
  "B" for .18, "A B", or "off"), `start valve` (.8, valve words) and `PSI` (.9, whole). Tests:
  `The_eng_page_reads_both_engines_in_the_sds_order` ("ENG", "F.USED KG: 1250, 1260", "OIL QT: 13.9, 14.1",
  "OIL PSI: 266, 261", "OIL °C: 101, 102", "VIB N1: 0.2, 0.2", "VIB N2: 0.1, 0.1", then the bottom line),
  `Nacelle_and_start_rows_show_only_while_drawn` ("NAC °C: 44, 44"; "IGN: A B, off", "start valve: open, closed",
  "PSI: 32, 0"), `Fuel_used_follows_the_weight_unit`, `A_missing_engine_is_not_available` ("ENG: not available, no
  field 129.2"), `A_wrong_shape_stops_the_page_only` (129.1.23 as a varint: "ENG", "ENG: not available, field 129.1.23
  has the wrong shape", then the bottom line; the report received it), `Eng_rows_are_the_same_in_every_culture` (de-DE
  and sv-SE). Commit: `feat(fenix-channel): the SD's ENG page`.

### Task 6: The ELEC page

**Files:** Create `Services/FenixChannelSdPage.Elec.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdElecTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and `Whole`; Task 2's ELEC fields; plan 01's `Remote.ApuData` and `ApuData`.
- Produces: `internal static FenixChannelRows ElecRows(FenixChannelSdPageContext ctx)` and `ComposeElec`.

- [ ] **Steps 1-5.** Page 7 reads 16 to 26, all whole numbers. Rows: `BAT 1: <25.1> V, <25.2> A` (then ", charging"
  while 16.2); `DC BAT` (17.6: "powered", else "not powered"); `BAT 2` (26; 16.3); `DC 1` (17.1), `DC ESS` (17.3),
  `DC 2` (17.2); `AC 1` (17.4), `AC ESS` (17.7), `AC 2` (17.5); `TR 1: <20.1> V, <20.2> A`, `ESS TR` (22), `TR 2` (21);
  `GEN 1: <18.1> %, <18.2> V, <18.3> HZ` ("GEN 1: OFF" while 18.4 is clear); `APU GEN: <24.1> %, <24.2> V, <24.3> HZ`
  while 24.12, else "APU GEN" alone (the legend the SD draws with the APU master off; plan 06's APU page has the same
  row); `EXT PWR: <23.1> V, <23.2> HZ` only while 17.8; `GEN 2` (19); `IDG 1: <18.5> °C` (then ", LO PR" while 16.6,
  ", DISC" while 16.4); `GALLEY SHED` only while 16.1; `IDG 2` (19.5; 16.7, 16.5). EMER GEN and STAT INV have no field:
  no row. Tests: `The_elec_page_reads_every_box_in_the_sds_order` (17 rows after "ELEC" with no external power and no
  shed), `A_generator_switched_off_reads_off`, `Ext_pwr_and_galley_shed_show_only_while_drawn`,
  `Apu_gen_shows_values_only_with_the_apu_master_on`, `Idg_words_follow_the_temperature` ("IDG 1: 99 °C, LO PR,
  DISC"), `A_missing_box_is_not_available_never_zero` (17 left out: "ELEC: not available, no field 17"),
  `Elec_rows_are_the_same_in_every_culture`. Commit: `feat(fenix-channel): the SD's ELEC page`.

### Task 7: The HYD page

**Files:** Create `Services/FenixChannelSdPage.Hyd.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdHydTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader and `Whole`; Task 2's HYD fields and `PtuStates`.
- Produces: `internal static FenixChannelRows HydRows(FenixChannelSdPageContext ctx)` and `ComposeHyd`.

- [ ] **Steps 1-5.** Page 11 reads 132 and 133. Rows: `GREEN: <133.1.1> PSI`, `BLUE: <133.2.1> PSI`, `YELLOW:
  <133.3.1> PSI` (whole); `PTU: <word>` (132 through `PtuStates` and Task 1's words; a value with none reads "PTU: not
  available" and is reported as "PTU state <n> not known"); `GREEN ENG 1 pump` (133.4 switch, 133.5 output: "off"
  without the switch, "LO" with the switch and no output, else "running"), `BLUE ELEC pump` (133.8.1, .2; then ", OVHT"
  while 133.8.3), `YELLOW ENG 2 pump` (133.6, 133.7), `YELLOW ELEC pump` (133.9); `RAT: stowed` ("deployed" while
  133.12); `GREEN fire valve: open` (133.10; "closed" when clear; inferred from a cruise push, engines running),
  `YELLOW fire valve` (133.11); `GREEN reservoir: <133.1.2>` (whole, no unit, Q2; then ", LO AIR PRESS" while 133.1.6,
  ", OVHT" while 133.1.7), `BLUE reservoir`, `YELLOW reservoir`. Tests: `The_hyd_page_reads_the_three_systems`,
  `Pump_words_follow_switch_and_output`, `Each_measured_ptu_state_reads_its_word` (one case per value of Task 1),
  `An_unmeasured_ptu_state_reads_not_available_and_is_reported`, `A_missing_system_is_not_available` (133.2 left out:
  "HYD: not available, no field 133.2"), `Hyd_rows_are_the_same_in_every_culture`. Commit: `feat(fenix-channel): the
  SD's HYD page`.

### Task 8: The FUEL page

**Files:** Create `Services/FenixChannelSdPage.Fuel.cs`; test `tests/MSFSBlindAssist.Tests/FenixChannelSdFuelTests.cs` (new).

**Interfaces:**
- Consumes: Task 3's reader, `Whole`, `WeightUnit`; Task 2's FUEL fields; plan 01's `FuelState.Used`, `FuelUsedData`;
  the context's `Variant` (`Type`, `FenixAircraftType`; `ActCount`).
- Produces: `internal static FenixChannelRows FuelRows(FenixChannelSdPageContext ctx)` and `ComposeFuel`.

- [ ] **Steps 1-5.** Page 10 reads 154 and 134 (.2 to .7 and .11 to .14; on the A321 also .1). The airframe is
  `ctx.Variant.Type`, never a channel field. On an A319 or A320, the rows: `F.USED 1+2: <134.13.3> <unit>`; `APU valve`
  (134.11.3); `LP valves` (134.11.1, .2);
  `X FEED valve` (134.11.4); `left outer tank: <134.12.1>`, `left inner tank` (.2), `center tank` (.3), `right inner
  tank` (.4), `right outer tank` (.5) (whole, no unit: the SD draws none beside a tank); `left pumps` (134.2, 134.3),
  `center pumps` (134.4, 134.5), `right pumps` (134.6, 134.7) ("off" without the switch .1, "LO" with the switch and
  low pressure .3, else "running"); `left transfer valves` (134.11.5, .6), `right transfer valves` (134.11.7, .8);
  `FOB: <134.12.6> <unit>`; `outer tanks °C` (134.14.1, .4), `inner tanks °C` (134.14.2, .3) (whole). On an A321 the
  same without the outer tanks, the transfer valves, the center pumps and the outer temperatures, with the wing tanks
  as `left tank` and `right tank`, and after `center tank`: `center transfer valves` (134.1.3, .4: "open" while set,
  else "closed"), `ACT 1: <134.12.7>` while the variant's `ActCount` is 1 or more, `ACT 2: <134.12.8>` while it is 2 (an
  unread count shows no ACT row; the airframe's config decides, never the data); then `tanks °C` (134.14.2, .3). An
  airframe not known yet: `NotAvailable("airframe not known")`. Tests:
  `The_a320_fuel_page_reads_tanks_pumps_and_valves`, `The_a321_fuel_page_has_no_outer_tanks_or_center_pumps` (an A321
  variant and a push carrying every field: no outer tank, transfer valve, center pump or outer temperature row),
  `Act_rows_follow_the_variants_extra_tanks`, `An_absent_valve_reads_open` (134.11 without field 1: "LP valves: open,
  open" when field 2 is left out too), `A_missing_valve_message_is_not_available_never_open` (134.11 left out: "FUEL:
  not available, no field 134.11"), `An_unknown_valve_value_reads_not_available_and_is_reported` (134.11.4 at 9: "X FEED
  valve: not available", the rest of the page as usual; the report received "field 134.11.4 has an unknown value 9"),
  `An_unknown_airframe_is_not_available`, `The_window_passes_the_variant_to_the_page` (`FenixChannelSd.Compose` with an
  A321 variant gives the A321 rows), `Weights_follow_the_weight_unit` (154 left out: "F.USED 1+2: 7220 LBS", "FOB: 8150
  LBS"), `Fuel_rows_are_the_same_in_every_culture`. Commit: `feat(fenix-channel): the SD's FUEL page`.

### Task 9: The switch commit: Alt+S and the Fenix windows list

**Files:** Create `Forms/Fenix/FenixWindowRegistry.ChannelSd.cs` (the list entry only); modify
`Aircraft/FenixA320Definition.ChannelSd.cs`; test `FenixChannelSdWindowTests.cs`.

**Interfaces:**
- Consumes: `ChannelKeyContext`, `OpenChannelWindow`, `ShowChannelWindow`, `FenixWindowEntry`, Task 3's
  `CreateChannelSdForm`.
- Produces, the two seam implementations and nothing else: `ChannelSdKey`: for `ReadDisplayLowerECAM`,
  `handled = OpenChannelWindow(ctx, FenixChannelDisplay.Sd, FenixChannelSd.DisplayName, CreateChannelSdForm)`, so an
  untrusted SD leaves the key to the AI read with no extra words (spec 2.4); `AddChannelSdWindows`: one entry named by
  the display's short name, "SD" (index, "Shared conventions"), described "The System Display from the Fenix: the page
  shown, its values, STATUS and the bottom line", opening
  `ctx.Definition.ShowChannelWindow(ctx.Announcer, FenixChannelSd.DisplayName, ctx.Definition.CreateChannelSdForm)`.

- [ ] **Steps 1-5.** Tests: `The_sd_window_is_listed_once` (`FenixWindowRegistry.Build()` has one "SD"),
  `The_sd_key_opens_the_window_by_the_trusted_path` (a source test over `FenixA320Definition.ChannelSd.cs`: `ChannelSdKey`
  names `ReadDisplayLowerECAM`, `OpenChannelWindow` and `FenixChannelDisplay.Sd`, and never `ShowChannelWindow`). Commit,
  alone: `feat(fenix-channel): Alt+S and the windows list open the SD window`. Push it to the remote's
  `fc-p05-sd-window` only (`git push <remote> HEAD:fc-p05-sd-window`), never to `feat/fenix-display-channel` before
  Task 10's ground steps have passed.

### Task 10 [sim]: The window against the cockpit

MSFSBA built from this plan's branch with Task 9 (close it first, CORE-3), the Fenix on 3.0.0.7613 at a gate on
external power, Task 1's tools and method. The rows are read with the owner's NVDA or from a capture of the window, the
fields with the team's tools, the SD from a capture of the sim. Each result into the evidence file.

- [ ] **Each page.** Open the window (input Ctrl+Shift+F, "SD", Enter). For ENG, ELEC, HYD, FUEL and STATUS:
  after its ECP key (`I_ECAM_<key>` reads 1), within 2 s the first row is the page's name and each row matches the
  decoded fields and the capture's labels, order and rounding; and where the Fenix has a value of its own, BAT 1 and
  BAT 2 match `N_ELEC_VOLT_BAT_1` and `_2`, OIL QT, PSI and °C the Engines window (the Fenix windows list), FOB the
  E/WD's FOB line, TAT and SAT the Engines window's TAT and OAT, UTC the sim's ZULU TIME, GW the MCDU's FUEL PRED GW.
- [ ] **Changes.** GEN 1 off: "GEN 1: OFF" within 2 s. X FEED on: "X FEED valve: open" (and "in transit" if seen on
  the way). The yellow electric pump on, engines off: Task 1's PTU word. The APU master on: the APU GEN values. An
  engine start: IGN, start valve and PSI during the start, gone after it.
- [ ] **Alt+S.** Output Alt+S opens the same window on the page shown, and a second press brings it to the front. Cold
  and dark, Alt+S gives the AI read with nothing before it (`fenix_channel.log`: "SD not trusted: display off"), and the
  list's entry opens the window on "SD: not available, display off".
- [ ] **The log.** No "SD:" problem line in `fenix_channel.log` over the whole check.
- [ ] **[in flight]** (gates nothing; a difference becomes a fix with its test.) In cruise: ENG and FUEL against a
  capture; the bottom line with SAT below zero ("SAT °C: -53, green"); what elements 2, 3, 7 and 8 hold and what the SD
  draws beside them (for plan 12).
- [ ] **[owner]** By ear: the bottom line and the STATUS page read naturally with NVDA, colors after each stretch; the
  owner rules on Q1 to Q4.

### Task 11 [controller]: Push the switch and land

- [ ] Once Task 10's ground steps have passed, or the owner has ruled on what still waits: the fixes they found, each
  its own commit; rebase, build and suite green (index workflow steps 4 and 5); push Task 9 to
  `feat/fenix-display-channel`. Write the "SD window" sub-heading of `docs/a32nx.md` (what the window shows; a page's
  problem stops the page, an unknown letter or a bottom line of another length stops the window; Task 1's and Task 10's
  measurements; the fields) and commit it: `docs(fenix-channel): the SD window`. Set **Status** to landed with the date
  and commit, fill in "Tests at landing" and "Handoff to plan 12", push.

## Handoff to plan 12

(Filled in while working. Expected:)

- **What changes for a pilot.** `guide/airbus-aircraft.md`, "Fenix A320 CEO": the opening paragraph (the ECAM screens
  are no longer read with AI only) and a new section, "The SD window": Alt+S, the rows (the page's name, its values,
  the STATUS text and the bottom line, with colors after each stretch), F5, Escape, the status row, the AI read when the
  SD cannot be read, the list entry. `guide/hotkey-reference.md`, "Fenix A320 CEO": "**Output Alt+S** — Open the SD
  window: the System Display's page, its values, STATUS and the bottom line, read from the Fenix. When the Fenix cannot
  be read, the AI read as before."; its Ctrl+Shift+F line and "Fenix windows" list gain "SD".
  `HotkeyGuides/Fenix_A320_Hotkeys.txt`: "Alt+S Read SD (System Display)" becomes "Alt+S SD window (System Display);
  AI read when not available". Changelog `feature`: "Fenix: Alt+S opens a live System Display window read from the
  Fenix itself, with the page shown and its values, the STATUS page and the bottom line (TAT, SAT, UTC and gross
  weight). When the Fenix cannot be read, Alt+S reads the display with AI as before."
- **Proposed rule lines.**
  - `- [FNX-?] An SD page composer makes its page not available when a nested message its rows read is absent, never its defaults: protobuf leaves out the Fenix's valve value Open (0) and every false or 0, so a missing message would read every valve open and every bus not powered (`FenixChannelSdReader.Message`). Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: the cruise push that carried the closed valves and none of the open ones; Task 1's presence and values.
  - `- [FNX-?] The gate decodes no SD text, so `FenixChannelSdFrame.UnknownCode` checks it: an unknown letter in the bottom line, or in the STATUS text on the STATUS page, takes the whole SD window to its status row, as [FNX-24] does for the displays the gate reads. Full: docs/invariants/fenix-channel.md#fnx-?`
    Full text: spec 2.6; the window's rule against the gate's.
- **Requests.** None expected for frozen files: plan 01 declares the shared fields (154, 24, `ValveStateEnum`,
  `ValveWord`), and `NameOf` passes to this plan. A `ValveStateEnum` value a sim check here finds other than plan 01's
  (its [owner] check confirms them) is told to the owner and its fix written here for plan 12, since plan 01's
  `FenixChannelFields.cs` is frozen; the composers keep using `ValveWord`.
- **Also seen on.** Nothing: no code is shared with another aircraft.
- **Found while working (2026-11-03, the whole-plan review), for the owner and plan 12:**
  - **Pages with no composer yet.** Once this plan's switch is on, Alt+S opens the SD window on every page the SD shows.
    On the eight pages plan 06 composes (APU, BLEED, PRESS, COND, CRUISE, DOOR, WHEEL, F/CTL) the window shows only the
    page's name and the bottom line until plan 06's seams land, and pilots meet DOOR, WHEEL and CRUISE most. The guide
    text above says Alt+S reads with AI "when the Fenix cannot be read". Either plan 12 merges only once every SD page
    has its composer, or Alt+S keeps the AI read on a page with no composer (which needs a helper in plan 01's frozen
    `FenixChannelSdPages.cs`, a request for plan 12). The owner decides.
  - **A window-level problem after the gate trusts the SD.** An unknown letter in the bottom line or the STATUS text, a
    bottom line of another length, or field 154 in another shape is found when the window composes, after the gate said
    trusted, so Alt+S opens a window with one status row instead of the AI read. Rare on a checked build; the owner
    accepts it (and the guide says so) or the key checks these too.
  - **Plan 05's own follow-ups before it lands (Task 11) or in plan 12's pass:** the ENG tests give NAC and VIB N2 the
    same value on both engines, so an engine swap in those rows would pass; the pump words ("off", "LO", "running") and
    the open/closed words are built twice (HYD, FUEL); the FUEL page keeps 0 or "" for parts it does not read; the test
    files repeat small helpers that belong in the `SdPush` partial. None changes what a pilot hears today.

## Execution record

Kept in this file only, so no other plan's push ever touches it.

- **Status:** claimed
- **Claimed by:** kn4iee, 2026-11-03
- **Worked by:** kn4iee with Claude (superpowers:subagent-driven-development)
- **Started:** 2026-11-03, from `1d29a73ee`
- **Landed:**
- **Tests at landing:**
- **Tasks:** 0 done (claim `66a246511`); 2 to 8 done 2026-11-03 and pushed, inert (nothing calls them before Task 9),
  each with its review; 2's Step 2 field check done (evidence file). 9 done, with its review and a whole-plan review,
  on `fc-p05-sd-window` only (never on this branch before Task 10's ground steps pass). 1, 10 and 11 wait on the owner.
- **Waiting on the owner:** Task 1 [sim] and Task 10 [sim, in flight, owner]; then Task 11. The evidence file lists what
  the two runs must also look at.
- **In-sim results:**
- **Changed from the plan:** Tasks 2 to 8 ran before Task 1, from the plan's values marked "inferred" in the code, as
  Task 1's own text allows; the evidence file lists every inferred value.
- **Rulings made while working:** (asked by the plan's text; answered by Claude as controller, for the owner to revisit)
  - `PtuStates` with Task 1 unmeasured: one value, `Idle = 0`, inferred from the cruise push, spoken "PTU: idle"; every
    other value "PTU: not available" and reported "PTU state <n> not known".
  - Who reports a page's problem: a page returns a stopping problem and `FenixChannelSd.Compose` reports it once as
    "<page>: <problem>"; the reader's own report is only for what does not stop the page (an unknown valve value or PTU
    state).
  - NaN and the infinities read "XX" (spec row 15), not "<label> not available".
  - An empty UTC element makes the row "UTC: not available".
  - `Compose` reports an unknown letter, a missing bottom line and a bottom line of another length, but not "no data".
  - A field 154 in another shape stops the whole window (the gate never checks it, and a wrong unit is 2.2 times off).
  - `NameOf` unchanged: Task 1 has not shown page 0 or 13 drawn.
  - The A318 follows the A320's FUEL layout (inferred; no Fenix A318 today).
  - The A321's extra FUEL rows sit right after the center tank, as the plan's text reads, until a capture says
    otherwise.
