# Fenix v3 Failures Window Implementation Plan (plan 10)

**Claimed by:** danielw97 · **Status:** done (code, reviews and the owner's in-sim steps 1-2 passed 2026-10-22; optional step 3 open)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A keyboard and screen-reader window, opened from "Fenix windows", that lists the Fenix's manual failures by ATA chapter, finds them by name, says which are armed or failed, and fails, arms or clears one only after the pilot confirms.

**Architecture:** Pure code in `MSFSBlindAssist/Services/FenixFailure*.cs` parses the gateway's `GET /fenix/failures/manual` into a catalog, filters it the way the Fenix EFB's own Failures app does, words every node and announcement, and builds the `POST /fenix/failures/saveManual` body the EFB sends (the GET's own failure object with `failed` and `failureCondition` changed). A thin `FenixFailuresForm` (a `NativeAccessibleTreeView` chapter → group → failure, a search box, a Show filter, three action buttons, a read-only status box) and `FenixFailuresArmDialog` use it through plan 01's `FenixDataService`; `FenixWindowRegistry.Failures.cs` adds the entry. While the window is open it re-reads the catalog every 10 s and speaks changes it did not make.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, Newtonsoft.Json (`JObject`), xUnit.

**Spec:** `docs/fenix-v3/spec.md` section 4.4. Index and file ownership: `docs/fenix-v3/plans/00-index.md`. Foundation (consumed by exact name): `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions (the plan implements the default; change D8 before Task 1, the rest before Task 4)

| # | Question | Default | Alternative |
|---|---|---|---|
| D1 | Confirm before a write? | Every write (Fail now, Arm, Clear) asks Yes/No with **No** as the default button (pattern `MSFSBlindAssist/Forms/GateTeleportForm.cs:476-481`) | Clear without asking |
| D2 | Checkboxes on failures? | **None.** A failure's state is in its node text; writes only from the buttons, so a stray Space never fails a system | Checkbox = failed, built per CORE-18 (`CheckboxStateImages = true`, `ShowCheckBox(node)` on failure nodes only, NodeMouseClick hit-test; never `TreeView.CheckBoxes`), still confirmed |
| D3 | Arm with no condition? | Refused: "Enter at least one condition." (the EFB saves an empty `{}`; what the Fenix does with it is unmeasured) | Allow `{}` |
| D4 | Speak failures that trigger in the background? | Yes, while the window is open: re-read every 10 s, speak changes this window did not make (CORE-7 background), at most three named | No re-read; F5 only |
| D5 | Failure names | The full `title` ("CPC 1"); the EFB strips the group name ("CPC 1" → "1", "TCAS TCAS" → empty). A group with one failure shows no group node (as the EFB) | The EFB's stripped names |
| D6 | Chapter order and wording | The Fenix's order (21, 22, 24 … 70, as the EFB); "Air Conditioning, ATA 21" (title first, for first-letter navigation) | Sorted by ATA number |
| D7 | Units in conditions | The EFB's own labels, no unit: "above IAS 250", "above altitude 10000 AMSL" (the EFB states none) | Add "knots"/"feet" after the owner confirms them in the sim |
| D8 | Test fixture | **Decided (coordinator, 2026-10-21):** commit only the first chapter (ATA 21: 14 groups, 57 failures), not the full catalog (Fenix's content, public repo); multi-chapter facts come from a small inline catalog in the tests; 20 chapters / 549 (A320 CFM) and 555 (A321 IAE) are checked only in the read-only sim check | (the full 65,828-byte capture: rejected) |
| D9 | Automatic failures (`auto_failure_mode`, `failure_rate`) | Shown read-only in the status box; plan 11 owns changing them | Leave out |

## What the Fenix EFB does (the evidence this plan follows)

Fetch the bundles from a running Fenix: `python -I docs/fenix-v3/tools/fetch_gateway_bundles.py <out>` (reads only); never commit them. Chunk names below are 3.0.0.7426 content hashes; on another build grep `<out>/efb` for `saveManualFailure` and the Fenix selector typo `app-edit-failure-condititon`.

- Base: `main.ccd882238ccd3f2d.js` `this.backendUrl=y.origin+"/fenix"`, so every path is under `http://127.0.0.1:8083/fenix`.
- `964.5deea9a5abefe01c.js` (backend service): `loadManualFailures(){return this.http.get(…+"/failures/manual")`, `saveManualFailure(n){return this.http.post(…+"/failures/saveManual",n)`. `loadFailureCases()` GETs `/failures`, has no caller in any chunk, and answered 404 in the investigation. `loadArrivalFailureCases()` GETs `/failures/arrival` with an `aircraftType` param and feeds chunk 437's arrival-procedures tree (`internalName`, `subItems`), not manual failures: out of scope.
- `463.562aecd7ef6dc413.js` (`ManualFailuresFeatureModule`, EFB app "Failures", route `efb/manual-failures`):
  - **Body.** "Apply Changes" copies the dialog's `failureCondition` and `failed` onto the catalog object `n`, sets an `afterEventSeconds`/`ias`/`alt`/`altb` that is not the number 0 but reads as 0 (an emptied field) to `undefined`, then calls `saveManualFailure(n)`: the body is the GET's own failure object (`id`, `title`, `failureCondition`, `failed`, plus the page-only `displayTitle`), sent as JSON (Angular's default `Content-Type: application/json`).
  - **Reply.** `subscribe(s=>{n.failed=s.failed,n.failureCondition=s.failureCondition,n.title=s.title…})`: the server answers with the saved failure object.
  - **Toggles.** "Failed" binds `failure.failed`. "Arm Failure Condition" off sets `failureCondition=void 0` (omitted from the JSON); on restores the previous condition, initially `{}`.
  - **Condition fields.** "Above IAS" `ias` (0-10000), "Above Alt AMSL" `alt` (0-50000), "Below Alt AMSL" `altb` (0-50000), "After Event" `afterEvent` ("(None)" has no value; "V1"; "Take-off" has value `"Takeoff"`; "Landing"), "After Time" `afterEventSeconds` (0-10000, caption "Seconds [from event] until failure"). `time` is never typed: shown as "Armed at" (`timeService.display` = `new Date(time)`) when `time && !afterEvent`; its "change" link clears it. Integer inputs emit JSON numbers (module 765: `valueChange.emit(this.toNumber(…))` for layout `"integer"`).
  - **State.** Armed = `failureCondition` truthy (`checkAtaArmed`); failed = `failed`; a failed failure shows failed even when armed (`manual-failure-armed` only when `!failed && failureCondition`).
  - **List.** Heading `ATA ${id} - ${title}`; a group header shows only when the group has more than one failure; search is a case-insensitive substring of the heading, group name or failure title, and a matching heading or group keeps all its failures; Failed/Armed filter buttons; search debounced 300 ms; the list reloads only on Reload (no polling, no push).
- **Live, read-only GETs, 2026-10-21.** A320 CFM (`docs/fenix-v3/evidence/failures-manual-shape.json` holds its first chapter): 65,828 bytes, 20 chapters, 178 groups (29 with one failure), 549 failures, unique ids, none armed or failed; chapter order 21, 22, 24, 26, 28, 29, 32, 34, 36, 38, 49, 23, 30, 27, 80, 31, 35, 46, 52, 70; first failure `F_PNEUMATIC_LAV_GAL_FAN` "Lavatory/Galley fan" in group "Lavatory/Galley Ventilation"; last `F_HYD_REVERSER_SHUTOFF_VALVE_2_MECH_SECURED_OPEN` "Reverser shutoff valve right secured open" in ATA 70 "Power plant". **A321 IAE: 66,393 bytes, 555 failures** (adds `F_CIDS_DEU_A_13` to `_16`, `F_CIDS_DEU_B_MID_CABIN_1/2`, `F_FLUSH_CONTROL_UNIT_LAV_M`; drops `F_CIDS_DEU_B_OVERWING`; same chapter order): the catalog follows the variant. Reply `Content-Type: application/json; charset=utf-8`, `Server: Kestrel`.
- **Unmeasured (nothing was posted):** the reply to an empty `{}`; whether several fields combine as AND or OR; what `time` holds and on which clock; whether a triggered failure reads `failed: true` in the next GET and keeps its condition; whether Clear repairs a failed system; the units of IAS and altitude. The owner's in-sim steps settle the first five.

## Global Constraints

- Paths `FenixFailureRequests.ManualPath = "/fenix/failures/manual"` and `SavePath = "/fenix/failures/saveManual"`, only through plan 01's `FenixDataService.GetRestAsync`/`PostRestAsync` (127.0.0.1, A320-40). Never `/fenix/failures` or `/failures/arrival`.
- A POST happens only after a pilot action and a Yes/No box answered Yes; nothing posts on open, on a timer, on search or on close. No other write anywhere in this plan's code (no L:var, no GraphQL mutation, no setting).
- The save body is the GET's own failure object, deep-cloned, with only `failed` and `failureCondition` changed (unknown fields kept). The EFB also sends its page-only `displayTitle` (absent from the GET); we do not, and in-sim step 1 proves the server takes the body without it (if it refuses, add `displayTitle` = `title`).
- Wire keys exactly: `atas`, `id`, `title`, `shortTitle`, `groups`, `groupName`, `failures`, `failureCondition`, `failed`, `ias`, `alt`, `altb`, `afterEvent`, `afterEventSeconds`, `time`; event values `V1`, `Takeoff`, `Landing`. We never write `time`.
- Never hard-code a failure id, a count or a chapter in shipped code: the catalog is per variant.
- Numbers go out as JSON integers; typed text goes through `NumberParsing.TryParseTyped` (CORE-17, `MSFSBlindAssist/Utils/NumberParsing.cs:52`); spoken numbers and times are invariant (tests under de-DE, plus fi-FI for the time separator).
- UI: `NativeAccessibleTreeView` (CORE-12; `MSFSBlindAssist/Controls/NativeAccessibleTreeView.cs:52`), chapters' children built on `BeforeExpand`; no `CheckBoxes` (D2); status text in a read-only `TextBox` (VAT-13); speech per CORE-7: a write is confirmed once, from the server's reply; validation errors, failures to load and background changes always speak. `Announce` (queued, `ScreenReaderAnnouncer.cs:222`) for confirmations and changes, `AnnounceImmediate` (`:283`) for errors.
- Every request is awaited from a UI-thread handler, so continuations resume on the UI thread; after each await `if (IsDisposed) return;`. No pool-thread callback reaches the form (no `Subscribe`); if an implementer moves work off the UI thread, marshal back with a try/catch `SafeBeginInvoke` (A380C-6; pattern `MSFSBlindAssist/Forms/FBWA380/FbwEfbForm.cs:287-292`).
- Logs through `Log.Debug("FenixFailures", …)` (CORE-15): each save's id, action, body and reply.
- Files: only those listed in the tasks (index ownership: `Forms/Fenix/FenixWindowRegistry.Failures.cs`, `Forms/Fenix/FenixFailures*.cs`, `Services/FenixFailure*.cs`, tests `*FenixFailure*`), plus the fixture `tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json` (the first chapter only, D8; never commit the full catalog).

## Review Focus

1. Gateway down, Fenix not loaded or a non-JSON reply: the window says "Fenix failures are not available. Load the Fenix, then press F5." once per outage, stays usable, never throws (Task 1 `Parse_rejects_what_is_not_the_catalog`; Task 4 Step 6).
2. Another variant loaded between two reads (A321 IAE, 555 ids): the tree rebuilds silently and only ids in both reads can speak (Task 2 `Changes_ignore_ids_in_only_one_read`).
3. A save the gateway refuses or answers with something else: success is never spoken; "The Fenix did not confirm the change to Lavatory/Galley fan." then a reload (Task 3 `ParseSaved_rejects_anything_but_a_failure`; Task 4 behaviour table).
4. A pilot on a de-DE or sv-SE PC types "10.000" or "250,5": refused aloud naming the field, never sent as 10 or 250 (Task 3 `TryBuildCondition_*`).
5. Five failures trigger between two reads, or one under the pilot's cursor: heard once, three named at most (Task 2 `Changes_cap_at_three`; NVDA check for the focused node in Task 4 Step 6).

---

### Task 1: Failure catalog: model, parser and search

**Files:**
- Create: `MSFSBlindAssist/Services/FenixFailureCatalog.cs`
- Create: `tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json`
- Test: `tests/MSFSBlindAssist.Tests/FenixFailureCatalogTests.cs`

**Interfaces** (namespace `MSFSBlindAssist.Services`):
- Produces:
  - `public enum FenixFailureState { Normal, Armed, Failed }`, `public enum FenixFailureFilter { All, Armed, Failed, ArmedOrFailed }`, `public enum FenixFailureAction { FailNow, Arm, Clear }`
  - `public sealed record FenixFailureCondition(int? Ias, int? AltAbove, int? AltBelow, string? AfterEvent, int? AfterEventSeconds, string? Time)` with `public bool IsEmpty` (all null). Wire keys `ias`, `alt`, `altb`, `afterEvent`, `afterEventSeconds`, `time`.
  - `public sealed record FenixFailure(string Id, string Title, bool Failed, FenixFailureCondition? Condition, JObject Raw)` with `public FenixFailureState State` (Failed wins, then Armed when `Condition != null`, else Normal).
  - `public sealed record FenixFailureGroup(string Name, IReadOnlyList<FenixFailure> Failures)` with `public bool ShowsHeader => Failures.Count > 1;`
  - `public sealed record FenixFailureChapter(int Ata, string Title, string ShortTitle, IReadOnlyList<FenixFailureGroup> Groups)` with `public IEnumerable<FenixFailure> AllFailures`.
  - `public sealed class FenixFailureCatalog` with `IReadOnlyList<FenixFailureChapter> Chapters`, `int FailureCount`, `static FenixFailureCatalog? Parse(string? json)`, `static FenixFailure? ParseFailure(JToken? token)`, `FenixFailure? Find(string id)`, `FenixFailureCatalog With(FenixFailure replacement)` (same id replaced, order kept), `(int Armed, int Failed) Counts()` (by `State`), `IReadOnlyList<FenixFailureChapter> Filter(string? search, FenixFailureFilter show)`.

- [x] **Step 1: Make the fixture: the first chapter only (D8).** Either copy the committed evidence, which is that chapter plus a top-level `_note` the parser ignores (11,996 bytes, pretty-printed): `Copy-Item docs/fenix-v3/evidence/failures-manual-shape.json tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json`; or trim a fresh read-only GET (any Fenix v3 preset, nothing armed or failed: ATA 21 was identical on the A320 CFM and the A321 IAE on 2026-10-21):
  `python -I -c "import json,sys,urllib.request as u; d=json.load(u.urlopen('http://127.0.0.1:8083/fenix/failures/manual',timeout=10)); open(sys.argv[1],'w',encoding='utf-8').write(json.dumps({'atas':[d['atas'][0]]},ensure_ascii=False,separators=(',',':')))" tests/MSFSBlindAssist.Tests/Fixtures/fenix-failures-manual-ch21.json`
  Expected for the trimmed read: 8,102 bytes, SHA-256 `fcfa5bcb9692c49be22f23dc539515bd5c7c05be97441a55e1ce92ec7b5bc3f6`. Never commit the full catalog. No csproj change: `Fixtures\*.json` is already copied to output (`tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj:40`).
- [x] **Step 2: Write the failing tests** (style of `FenixMcduSidesTests.cs`; two `internal static` helpers on `FenixFailureCatalogTests` that Tasks 2 and 3 reuse: `Load()` = `File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-failures-manual-ch21.json"))`; `Multi()` = the fixture's JSON with two inline chapters appended to `atas`, in this order: `{"id":32,"title":"Landing gear","shortTitle":"Gear","groups":[{"groupName":"Brake fan","failures":[{"id":"F_TEST_BRAKE_FAN","title":"Brake fan system","failureCondition":null,"failed":false}]}]}` and `{"id":23,"title":"Communications","shortTitle":"Communications","groups":[{"groupName":"Test","failures":[{"id":"F_TEST_A","title":"Alpha","failureCondition":null,"failed":false},{"id":"F_TEST_B","title":"Bravo","failureCondition":null,"failed":false}]}]}`, then parsed):
  - `Parse_reads_the_first_chapter`: `Chapters.Count == 1`, `FailureCount == 57`; the chapter is `(21, "Air Conditioning", "Air")` with 14 groups, 4 of them with one failure; its first failure is `F_PNEUMATIC_LAV_GAL_FAN` / "Lavatory/Galley fan", `State == Normal`, `Condition == null`; `Groups[0].ShowsHeader == false` ("Lavatory/Galley Ventilation"), `Groups[1]` is "CPC" with `ShowsHeader == true`; the last failure is `F_PNEUMATIC_AVNCS_COND_AIR_INLET_VALVE_MECH_SECURED_OPEN` / "Conditioning air inlet valve secured open" in group "Ventilation valves"; `Counts() == (0, 0)`.
  - `Parse_keeps_the_Fenix_chapter_order`: `Multi()` has `Chapters.Select(c => c.Ata)` equal to `{21, 32, 23}` (not sorted) and `FailureCount == 60`.
  - `Parse_reads_states`: an inline one-chapter catalog with four failures: `"failureCondition":{"ias":250,"afterEvent":"Takeoff","afterEventSeconds":30}` → Armed, `Ias 250`, `AfterEvent "Takeoff"`, `AfterEventSeconds 30`; `"failed":true` → Failed; `"failureCondition":{}` → Armed with `IsEmpty`; `"failed":true` plus a condition → Failed. `"alt":10000.0` reads `AltAbove 10000`; `"time":"2026-10-21T14:32:05Z"` reads that text; `"time":1792593125000` reads `"1792593125000"`.
  - `Parse_rejects_what_is_not_the_catalog`: `null`, `""`, `"not json"`, `"{}"`, `"[]"`, `{"atas":"x"}` → null. A failure without `id` is skipped (two failures, one without id → `FailureCount == 1`).
  - `Filter_matches_like_the_EFB`: on the fixture, `""` → 1 chapter, 57 failures; `"fan"` and `"FAN"` → 1 chapter, failure titles `{"Lavatory/Galley fan", "Recirculation fans"}`; `"cpc"` → `{"CPC 1", "CPC 2"}` (group-name match); `"ata 21"` → 57 failures (heading match); `"zzz"` → empty. On `Multi()`: `"fan"` → chapters `{21, 32}`, titles `{"Lavatory/Galley fan", "Recirculation fans", "Brake fan system"}`; `"ata 23"` → one chapter, `{"Alpha", "Bravo"}`.
  - `Filter_by_state`: fixture `.With(first failure with Failed = true)` → `Filter("", Failed)` gives exactly that failure, `Filter("", Armed)` is empty, `Filter("", ArmedOrFailed)` has one; `Counts() == (0, 1)`; after filtering, a group cut to one failure has `ShowsHeader == false`.
  - `With_replaces_by_id`: `Find("F_PNEUMATIC_LAV_GAL_FAN")` returns the replacement; `FailureCount` unchanged; the original catalog is unchanged.
- [x] **Step 3: Run them; they fail to compile.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FenixFailureCatalogTests`
- [x] **Step 4: Implement `FenixFailureCatalog.cs`.** `JToken.Parse` in try/catch (`JsonException`) → null; the root must be an object with an `atas` array; other top-level keys (the evidence copy's `_note`) are ignored. Numbers through one helper: JSON integer → int, an integral float → int, anything else → null. `time`: a string as is, a number as invariant text. A non-null `failureCondition` makes the failure Armed even when it is not an object (EFB truthiness). `Raw` is the failure's own `JObject`. Filter: a failure is kept when `$"ATA {Ata} - {Title}"`, the group name or its title contains the search (`StringComparison.OrdinalIgnoreCase`; null/blank matches all) AND its `State` passes the filter (Armed = `State == Armed`, Failed = `State == Failed`); empty groups and chapters are dropped; order kept.
- [x] **Step 5: Run them; all pass.**
- [x] **Step 6: Commit.** `git add` the three files; `feat(fenix): failures catalog parsing and search`.

### Task 2: What the window says

**Files:**
- Create: `MSFSBlindAssist/Services/FenixFailureText.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixFailureTextTests.cs`

**Interfaces:**
- Consumes: Task 1's types; plan 01's `FenixSetting(string Key, string Category, string? Value, IReadOnlyList<string> PossibleValues, string? Type, string? Source, bool Writable)`.
- Produces `public static class FenixFailureText`: `ChapterNode(FenixFailureChapter)`, `GroupNode(FenixFailureGroup)`, `FailureNode(FenixFailure, TimeZoneInfo)`, `Condition(FenixFailureCondition, TimeZoneInfo)`, `Summary(FenixFailureCatalog)`, `Refreshed(FenixFailureCatalog)`, `Matches(int count, string search)`, `Shown(int count)`, `AutomaticFailures(IReadOnlyList<FenixSetting>)`, `SavedLine(FenixFailure, TimeZoneInfo)`, `Confirm(FenixFailure, FenixFailureAction, FenixFailureCondition?, TimeZoneInfo)`, `NotConfirmed(string title)`, all returning `string`; `string? Changes(FenixFailureCatalog before, FenixFailureCatalog after, string? exceptId, TimeZoneInfo zone)`; consts `Unavailable = "Fenix failures are not available. Load the Fenix, then press F5."`, `SelectFailureFirst = "Select a failure first."`.

- [x] **Step 1: Write the failing tests.** The class sets `CultureInfo.CurrentCulture = new CultureInfo("de-DE")` and restores it in `Dispose` (pattern `A32nxPfdSpeedTargetTests.cs:15-21`); `zone = TimeZoneInfo.Utc`; `fan` = the fixture's first failure. Exact strings:
  - `Nodes`: `ChapterNode(ch21)` "Air Conditioning, ATA 21"; with fan failed "Air Conditioning, ATA 21, 1 failed"; with one armed and one failed "Air Conditioning, ATA 21, 1 armed, 1 failed"; `GroupNode(CPC)` "CPC", with CPC 1 failed "CPC, 1 failed". `FailureNode`: normal "Lavatory/Galley fan"; failed "Lavatory/Galley fan, failed"; armed `Ias 250` "Lavatory/Galley fan, armed: above IAS 250"; armed empty "Lavatory/Galley fan, armed"; armed with only `Time "2026-10-21T14:32:05Z"` "Lavatory/Galley fan, armed at 14:32:05".
  - `Condition_uses_the_EFB_labels`: `(250, 10000, 20000, null, null, null)` "above IAS 250, above altitude 10000 AMSL, below altitude 20000 AMSL"; `AfterEvent "Takeoff", AfterEventSeconds 30` "30 seconds after take-off"; `"V1"` "at V1"; `"Landing"` "at landing"; `"Rotation"` "at Rotation"; `AfterEventSeconds 90` "after 90 seconds"; `AfterEventSeconds 90, Time "2026-10-21T14:32:05Z"` "armed at 14:32:05"; `Time "1792593125000"` "armed at 14:32:05"; `Time "garbage"` "armed at garbage"; empty "".
  - `Times_and_numbers_are_invariant`: under `fi-FI` (its time separator is ".") the time still reads "14:32:05"; under de-DE `AltAbove 10000` reads "10000", never "10.000".
  - `Status_lines`: `Summary(fixture)` "57 failures in 1 chapter. 0 armed, 0 failed."; `Summary(Multi())` (Task 1's helper) "60 failures in 3 chapters. 0 armed, 0 failed."; a one-failure inline catalog "1 failure in 1 chapter. 0 armed, 0 failed."; `Refreshed(fixture)` "Failures refreshed. 0 armed, 0 failed."; `Matches(3,"fan")` "3 failures match fan."; `Matches(1,"cpc 1")` "1 failure matches cpc 1."; `Matches(0,"zzz")` "No failures match zzz."; `Shown(2)` "2 failures shown.", `Shown(1)` "1 failure shown.", `Shown(0)` "No failures shown.".
  - `Automatic_failures_from_settings`: rows `("auto_failure_mode","Realism","None",["None","Minor","All"],"Enum","UserFile",true)` and `("failure_rate","Realism","Realistic",["Realistic","High"],"Enum","UserFile",true)` (values of `docs/fenix-v3/evidence/settings.tsv`; labels "Automatic Failure Severity"/"Automatic Failure Rate" from `fenix-options-labels.tsv:17,52`) → "Automatic failure severity: None. Automatic failure rate: Realistic."; an empty list → "".
  - `Saved_and_confirm_lines`: `SavedLine` failed "Lavatory/Galley fan failed."; armed `Ias 250` "Lavatory/Galley fan armed: above IAS 250."; armed time-only "Lavatory/Galley fan armed at 14:32:05."; armed empty "Lavatory/Galley fan armed."; normal "Lavatory/Galley fan cleared.". `Confirm`: FailNow "Fail Lavatory/Galley fan now? This changes your flight."; Arm with `Ias 250` "Arm Lavatory/Galley fan: above IAS 250? This changes your flight."; Clear "Clear Lavatory/Galley fan? This changes your flight.". `NotConfirmed("Lavatory/Galley fan")` "The Fenix did not confirm the change to Lavatory/Galley fan."
  - `Changes_speak_what_changed`: fixture → fixture with fan failed: "Lavatory/Galley fan failed."; fan armed `Ias 250` → failed: "Failure triggered: Lavatory/Galley fan."; failed → normal: "Lavatory/Galley fan cleared."; no change → null; the same change with `exceptId: "F_PNEUMATIC_LAV_GAL_FAN"` → null.
  - `Changes_cap_at_three`: the first five failures of ATA 21 failed → "Lavatory/Galley fan failed. CPC 1 failed. CPC 2 failed. And 2 more changes."
  - `Changes_ignore_ids_in_only_one_read`: an inline catalog without fan but with a new id already failed, compared with the fixture → null.
- [x] **Step 2: Run them; they fail to compile.** `… --filter FenixFailureTextTests`
- [x] **Step 3: Implement `FenixFailureText.cs`.** Integers with `ToString(CultureInfo.InvariantCulture)`. Condition parts in order IAS, above, below, then the event part (`afterEvent` with seconds "N seconds after E", without "at E"; events V1 → "V1", Takeoff → "take-off", Landing → "landing", other text as is), else seconds without `time` "after N seconds", else `time` without event "armed at T". `T` = `HH:mm:ss` in `zone` with `CultureInfo.InvariantCulture`: an all-digit text is Unix milliseconds; otherwise `DateTimeOffset.TryParse(…, InvariantCulture, AssumeUniversal)`; unparseable text as is. When the only part is the time part, the node and saved line read "armed at T" (no colon). `Changes` compares, for ids in both reads, `State` and `Condition` text; armed → failed is "Failure triggered: X.", any other change is `SavedLine(after)`; lines joined with a space; more than three: the first three plus "And N more changes.".
- [x] **Step 4: Run them; all pass.**
- [x] **Step 5: Commit.** `feat(fenix): spoken text for Fenix failures`.

### Task 3: Save body, reply and condition entry

**Files:**
- Create: `MSFSBlindAssist/Services/FenixFailureRequests.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixFailureRequestsTests.cs`

**Interfaces:**
- Consumes: Task 1's types; `NumberParsing.TryParseTyped(string?, out double)`.
- Produces:
  - `public enum FenixFailureConditionField { None, Ias, AltAbove, AltBelow, Seconds }`
  - `public static class FenixFailureRequests` with `const string ManualPath = "/fenix/failures/manual"`, `const string SavePath = "/fenix/failures/saveManual"`, `static string BuildSaveBody(FenixFailure failure, FenixFailureAction action, FenixFailureCondition? condition = null)`, `static FenixFailure? ParseSaved(string? json)`, `static bool TryBuildCondition(string? ias, string? altAbove, string? altBelow, string? afterEvent, string? seconds, out FenixFailureCondition? condition, out FenixFailureConditionField errorField, out string? error)`.
- Semantics (the EFB's Apply, chunk 463): `FailNow` sets `failed: true`, condition untouched. `Arm` keeps `failed` and writes `failureCondition` with only the non-null fields as JSON integers (`afterEvent` as a string), never `time`; `Arm` without a condition throws `ArgumentException`. `Clear` sets `failed: false`; an object `failureCondition` is removed (the EFB's `undefined`), a null one stays null. Always on `failure.Raw.DeepClone()`, compact JSON.

- [x] **Step 1: Write the failing tests** (class under de-DE, as Task 2):
  - `Paths_are_the_EFBs`: the two consts.
  - `Fail_now_on_an_unarmed_failure_is_the_EFB_body`: `JToken.DeepEquals(JObject.Parse(BuildSaveBody(fan, FailNow)), JObject.Parse("{\"id\":\"F_PNEUMATIC_LAV_GAL_FAN\",\"title\":\"Lavatory/Galley fan\",\"failureCondition\":null,\"failed\":true}"))`.
  - `Arm_sends_integers`: `(250, 10000, null, "Takeoff", 30, null)` → `failureCondition` DeepEquals `{"ias":250,"alt":10000,"afterEvent":"Takeoff","afterEventSeconds":30}`, `failed` false; the body contains `"alt":10000` and neither `10000.0` nor `10.000`.
  - `Arm_never_writes_time`: a failure whose raw condition has `"time":"x"`, armed with `Ias 250` → the new `failureCondition` has no `time`.
  - `Fail_now_keeps_the_condition`: raw `{"ias":250,"time":"x"}` → that object unchanged, `failed` true.
  - `Clear_removes_an_armed_condition` (no `failureCondition` property, `failed` false) and `Clear_keeps_null_on_an_unarmed_failure`.
  - `Unknown_fields_survive_and_raw_is_not_mutated`: raw with `"foo":1` → body has `foo` 1; `fan.Raw` still has `failed` false afterwards.
  - `Arm_needs_a_condition`: `Assert.Throws<ArgumentException>`.
  - `ParseSaved_reads_the_reply`: `{"id":"F_X","title":"T","failureCondition":{"ias":250},"failed":false}` → Armed, `Ias 250`. `ParseSaved_rejects_anything_but_a_failure`: `null`, `""`, `"[]"`, `"{}"`, `{"title":"x"}`, `"garbage"` → null.
  - `TryBuildCondition_*` (Theory, fields in order ias, altAbove, altBelow, afterEvent, seconds): `("250","","",null,"")` true, `Ias 250`; `(" 250 ",…)` true; `("0",…)` true; `("","","","Takeoff","30")` true; `("","","","V1","")` true; all empty → false, `None`, "Enter at least one condition."; `("250,5",…)` and `("10001",…)` and `("-1",…)` → false, `Ias`, "Above IAS must be a whole number from 0 to 10000."; `("","1.500",…)` → false, `AltAbove`, "Above altitude AMSL must be a whole number from 0 to 50000."; `("","","50001",…)` → false, `AltBelow`, "Below altitude AMSL must be a whole number from 0 to 50000."; `("","","",null,"10001")` → false, `Seconds`, "Seconds until failure must be a whole number from 0 to 10000."; afterEvent `"Rotation"` → `ArgumentException` (the combo offers only None/V1/Takeoff/Landing).
- [x] **Step 2: Run them; they fail to compile.** `… --filter FenixFailureRequestsTests`
- [x] **Step 3: Implement `FenixFailureRequests.cs`** as specified (`JObject` building, `Formatting.None`; integers as `new JValue((long)n)`; `ParseSaved` = `FenixFailureCatalog.ParseFailure` on a parsed object, null on any `JsonException`).
- [x] **Step 4: Run them; all pass.**
- [x] **Step 5: Commit.** `feat(fenix): failure save body, reply and condition entry`.

### Task 4: The Failures window

**Files:**
- Create: `MSFSBlindAssist/Forms/Fenix/FenixWindowRegistry.Failures.cs`, `MSFSBlindAssist/Forms/Fenix/FenixFailuresForm.cs`, `MSFSBlindAssist/Forms/Fenix/FenixFailuresArmDialog.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixFailureWindowTests.cs`

**Interfaces:**
- Consumes (plan 01, exact names): `static partial void AddFailureWindows(List<FenixWindowEntry> entries);` in `public static partial class FenixWindowRegistry` (same namespace as plan 01's `FenixWindowRegistry.cs`); `FenixWindowEntry(string Name, string Description, Action<FenixWindowContext> Open)`; `FenixWindowContext(FenixA320Definition Definition, FenixDataService Data, SimConnectManager SimConnect, ScreenReaderAnnouncer Announcer, Form Owner)`; `FenixA320Definition.ShowFenixWindow<T>(Func<T> factory, Action<T> show)` (reuse-if-open, closed on aircraft switch: `BaseAircraftDefinition.cs:1144-1176`); `FenixA320Definition.Variant`, `VariantChanged` (UI thread), `FenixVariant.IsKnown`, `Describe()`; `FenixDataService.GetRestAsync(string, CancellationToken)`, `PostRestAsync(string, string, CancellationToken)` (both `Task<string?>`, null on failure), `GetSettingsAsync(CancellationToken)`.
- Produces: `AddFailureWindows` adds `new FenixWindowEntry("Failures", "Arm, fail or clear Fenix failures by ATA chapter", ctx => ctx.Definition.ShowFenixWindow(() => new FenixFailuresForm(ctx.Definition, ctx.Data, ctx.Announcer), f => f.ShowForm()))`; `public sealed class FenixFailuresForm : Form` with that constructor and `public void ShowForm()`; `public sealed class FenixFailuresArmDialog : Form` with `FenixFailuresArmDialog(FenixFailure failure, ScreenReaderAnnouncer announcer)` and `public FenixFailureCondition? Condition { get; }` (set when it closes with OK).

- [x] **Step 1: Write the failing tests.**
  - `Registry_lists_Failures_once`: `Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "Failures")`, its `Description` as above.
  - `Window_keeps_the_screen_reader_rules`: source scan (`ClaudeContextBudgetTests.RepoRoot()`, as `FenixDataServiceAddressTests.cs:16`) of `FenixFailuresForm.cs` and `FenixFailuresArmDialog.cs`: the form contains `NativeAccessibleTreeView`, `ReadOnly = true` and `MessageBoxDefaultButton.Button2`; neither matches `new\s+TreeView\b` nor contains `CheckBoxes`.
  - `Only_one_write_and_only_to_saveManual`: across `MSFSBlindAssist/Forms/Fenix/FenixFailures*.cs`, `FenixWindowRegistry.Failures.cs` and `MSFSBlindAssist/Services/FenixFailure*.cs`: `PostRestAsync(` occurs exactly once and its line contains `FenixFailureRequests.SavePath`; no `Write(Int|Bool|Float|String)Async`, no `mutation`, no `localhost`, no `/failures/arrival`; the literal `"/fenix/failures` occurs only in `FenixFailureRequests.cs`.
- [x] **Step 2: Run them; they fail** (`Build()` has no "Failures"; the files are missing). `… --filter FenixFailureWindowTests`
- [x] **Step 3: Implement the registry partial and the form.** Layout (one `TableLayoutPanel`, tab order as listed): Label "&Search" + `TextBox` (AccessibleName "Search failures"); Label "S&how" + `ComboBox` DropDownList "All failures", "Armed", "Failed", "Armed or failed" (→ `FenixFailureFilter`; AccessibleName "Show"); `NativeAccessibleTreeView` (AccessibleName "Failures", `AccessibleRole.Outline`, `HideSelection = false`, `ShowPlusMinus`/`ShowRootLines` as `FirstOfficerForm.cs:522-531`, no `CheckboxStateImages`); buttons "Fail &now", "&Arm…", "&Clear", "&Refresh"; a read-only, multiline, tab-stop `TextBox` (AccessibleName "Failures status": variant `Describe()` when `IsKnown`, `Summary`, `AutomaticFailures`, then the last search or action result). Title "Fenix failures"; `KeyPreview`; Escape closes; F5 refreshes. `ShowForm()` follows `FenixEFBForm.cs:194-207` (remember the foreground window, show, activate, focus the tree; restore the previous window on close). Chapters (Tag = ATA) carry one placeholder child; `BeforeExpand` builds groups (a group node only when `ShowsHeader`) and failures (Tag = id) from the current filtered view; keep a `Dictionary<string, TreeNode>` of built failure nodes. Never rebuild for a state change: update only the `Text` that changed; rebuild (keeping expanded chapters and the selected id) only when the filtered id list changes. Behaviour:

| When | Does | Speaks |
|---|---|---|
| Open | GET catalog and settings; first chapter selected; 10 s `System.Windows.Forms.Timer` starts | nothing on success (the reader reads the node); `Unavailable` on failure |
| Typing in Search | rebuild after 300 ms idle (the EFB's debounce), chapters expanded | nothing; status shows `Matches` |
| Enter in Search | rebuild now, focus the first failure | `Matches(count, text)` |
| Show changed | rebuild | `Shown(count)` |
| Fail now / Arm… (or Enter on a failure) / Clear | on a non-failure node: `SelectFailureFirst`. Arm: dialog first. Then `MessageBox.Show(this, Confirm(…), "Confirm failure change", YesNo, Warning, Button2)`; Yes → `PostRestAsync(FenixFailureRequests.SavePath, BuildSaveBody(…))`, log body and reply | reply parsed with matching id: `catalog.With(saved)`, update the node, `SavedLine(saved)`; else `NotConfirmed(title)` (immediate). Then reload with `exceptId` = that id |
| F5 / Refresh | GET | `Refreshed(catalog)` |
| Timer tick (no request in flight) | GET; update texts; rebuild if the id list changed (other variant) | `Changes(before, after, null, TimeZoneInfo.Local)` when not null; `Unavailable` once per outage, nothing on recovery |
| `VariantChanged` | GET | nothing |
| Close | stop the timer, cancel the `CancellationTokenSource`, unsubscribe `VariantChanged` | nothing |

  One request at a time (a `_busy` flag; a tick while busy is skipped, F5 while busy says "Still loading."). Arm dialog: Text "Arm " + title; Label + `TextBox` pairs "Above &IAS", "Above &altitude AMSL", "&Below altitude AMSL", "&Seconds until failure" (caption and AccessibleName become "Seconds from event until failure" while an event is chosen), and "After &event" `ComboBox` "None", "V1", "Take-off", "Landing" (→ null, `V1`, `Takeoff`, `Landing`); pre-filled from the current condition (invariant integers); "Arm" is the AcceptButton, "Cancel" the CancelButton; OK runs `TryBuildCondition`, and on false `AnnounceImmediate(error)` and focuses `errorField`'s box.
- [x] **Step 4: Run the three tests and the whole suite; all pass.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
- [x] **Step 5: Build.** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` (close MSFSBA first, CORE-3). Expected: 0 errors.
- [x] **Step 6: Read-only check with the sim up (no write: press no Fail now, Arm or Clear).** App on the Fenix profile, NVDA running: `[` (BA: "input"), Ctrl+Shift+F, Down to "Failures", Enter. Expect the tree on "Air Conditioning, ATA 21". This is the only place the full catalog is checked (D8): the status box gives the variant and "549 failures in 20 chapters. 0 armed, 0 failed." on an A320 CFM, "555 failures in 20 chapters. 0 armed, 0 failed." on an A321 IAE (live counts of 2026-10-21; a newer Fenix build may differ: record what you hear). Type "fan", Enter: "3 failures match fan." (Brake fan system in ATA 32 included), focus on "Lavatory/Galley fan". Show "Failed": "No failures shown.". F5: "Failures refreshed. 0 armed, 0 failed.". The next time the sim or the Fenix is closed anyway, have the window open: "Fenix failures are not available. Load the Fenix, then press F5." once within 10 s, not again. Switch aircraft profile with it open: the window closes. If the owner is present, arm one failure from the Fenix EFB (Shift+T, Failures app) with the window open on that failure's node and listen: the change is heard once (Review Focus 5).
- [x] **Step 7: Commit.** `feat(fenix): accessible Fenix failures window`.

### Task 5: Hand back

- [x] **Step 1:** Build and full suite green; `git diff --name-only <base-sha>` lists only this plan's files.
- [x] **Step 2:** Fill in "Handoff to plan 12" below with what you found (Task 4 Step 6 results; any deviation from the defaults) and hand the branch to the integrator.

## Owner-in-sim steps (writes: each needs the owner's go-ahead in chat)

Aircraft parked at a gate, app connected on the Fenix profile, nothing armed. All three use "Lavatory/Galley fan" (cabin ventilation, first in the list). Open the window: `[` ("input"), Ctrl+Shift+F, "Failures", Enter; Right arrow on "Air Conditioning, ATA 21", Down to "Lavatory/Galley fan".

1. **Fail and clear** (the body, the reply, the confirmation, and whether Clear repairs). Alt+N; the box reads "Fail Lavatory/Galley fan now? This changes your flight."; Y. Expect "Lavatory/Galley fan failed." and the node "Lavatory/Galley fan, failed"; note any ECAM caution or master caution BA reads. Alt+C, Y: "Lavatory/Galley fan cleared."; does the caution go away? The integrator copies the two `FenixFailures` lines (body and reply) from `%APPDATA%\MSFSBlindAssist\logs\debug.log`.
2. **Timed arm and trigger** (the condition shape, `time`, the trigger seen by a GET, background speech). Alt+A; in the dialog Alt+S, type 30, Enter; the box reads "Arm Lavatory/Galley fan: after 30 seconds? This changes your flight."; Y. Expect "Lavatory/Galley fan armed: after 30 seconds." or "… armed at <time>." (say which; for a time, is it the PC's clock or the sim's?). Leave the window open about 45 s: expect "Failure triggered: Lavatory/Galley fan." and the node "…, failed". Then Alt+C, Y.
3. **Optional, on a flight (AND or OR).** Before takeoff arm it with Above IAS 100 and Above altitude AMSL 30000. A failure on the takeoff roll means the fields combine as OR; none before 30000 ft means AND. Clear it after.

## Handoff to plan 12

### Results (2026-10-22, owner in the sim, Fenix A320 IAE)

- **Step 6 (read-only):** passed. Status box: "Fenix A320, IAE engines", "549 failures in 20 chapters. 0 armed, 0 failed.", "Automatic failure severity: None. Automatic failure rate: Realistic." So the A320 IAE has 549 failures, like the A320 CFM; the A321 IAE's 555 comes from the airframe. Search, Show, F5 and a steady tree behaved as written. One open on the pre-fix build said "3 failed" while the gateway later held at most one failed failure; it never came back on a clean open (not reproduced).
- **Owner step 1 (fail and clear):** passed. The body without `displayTitle` is accepted; the reply is the saved failure object. Logged pair: sent `{"id":"F_PNEUMATIC_LAV_GAL_FAN","title":"Lavatory/Galley fan","failureCondition":null,"failed":true}`, reply identical. Failing Lavatory/Galley fan gives an ECAM message; Clear (`failed:false`) repairs it and the message clears.
- **Owner step 2 (timed arm): the plan's reading of `time` was wrong, fixed in the sim round.** Sent `{"failureCondition":{"afterEventSeconds":30},…}`; the reply condition is `{"id":4,"ias":null,"alt":null,"altb":null,"time":30,"afterEvent":null,"afterEventSeconds":null}`. Seconds without an event become `time`, the **seconds remaining**: a float that counts down one per wall-clock second at sim rate 1 (polled 30.84 → 0.78 over 30.1 s). At zero the Fenix sets `failed:true` and **removes** `failureCondition`. The window first read 30 as Unix milliseconds ("armed at 19:00"), then the raw float. Now a numeric `time` reads "armed: fails in N seconds" (rounded up, at least 1), an ISO text still reads "armed at T", and `Changes` ignores a countdown that only ticks. Retested: counts down, "Failure triggered" once (the ECAM chime may talk over it, which is the right order), cleared after. Still unmeasured: whether the countdown pauses with the sim, and what `time` holds when an event is chosen.
- **Escape:** with the Show list dropped open it now closes only the list (fixed in the sim round; it closed the window before).
- **Not run:** optional step 3 (AND or OR between condition fields), the units of IAS and altitude (D7 keeps the EFB's unitless labels), and an EFB-side arm while our window sits on that failure's node (the countdown retest covered a node whose text changes under focus without repeated speech).
- **Deviations from the plan's text (in the code, with tests):** wording "And 1 more change." for exactly one extra; "Still loading. Nothing was changed." when a load starts while the Yes/No box is up; Enter in an empty search speaks `Shown`; a failure failed in both reads is not re-spoken when its condition changes; the timer stops while the Arm dialog or the Yes/No box is up and the failure is looked up again after Yes; a save or load that throws speaks `NotConfirmed` or `Unavailable`.
- **Deferred minors for plan 12 to triage:** the status box can show the stale summary after an outage once search or Show is used; an `afterEvent` value outside V1/Takeoff/Landing pre-fills as None; a selected group node falls back to its chapter on rebuild; Arm on an already failed failure keeps `failed:true` (as the EFB) and speaks "X failed."; `FenixFailuresForm.cs` (about 560 lines) has an unneeded `using System.Runtime.InteropServices`.

- **Pilot-facing:** "Fenix windows" (input mode, Ctrl+Shift+F) gains "Failures": a tree of the Fenix's manual failures by ATA chapter (names as the Fenix gives them; a failure says "armed: <condition>", "armed: fails in N seconds" for a timed arm, or "failed"), a search box (Enter jumps to the results and says how many), a Show filter (all, armed, failed, armed or failed), Fail now (Alt+N), Arm (Alt+A: above IAS, above or below altitude AMSL, after V1, take-off or landing, seconds), Clear (Alt+C), each asking first, F5 refresh; the status box shows the aircraft variant, the totals and the Fenix's automatic-failure options; while open, a failure that triggers is spoken ("Failure triggered: …"). Guide: the Fenix part of `guide/airbus-aircraft.md` (a "Failures" section beside the EFB window) and the hotkey appendix (no new key). Changelog: `feature`.
- **Proposed rules** (in `.claude/rules/fenix-data.md`; plan 01's final globs already cover `Services/Fenix*.cs` and `tests/**/*Fenix*.cs`; each line under 400 characters):

```
- [FNX-?] `FenixFailureRequests.BuildSaveBody` posts the GET's own failure object to /fenix/failures/saveManual with only `failed` and `failureCondition` changed, as the Fenix EFB's saveManualFailure does: never rebuild it from parsed fields; Clear removes an armed condition, numbers go out as JSON integers, `time` is never written. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] A Fenix failure write in `FenixFailuresForm` (fail now, arm, clear) posts only after a Yes/No box defaulting to No and speaks the state the Fenix's reply carries (`FenixFailureRequests.ParseSaved`), never the requested one; no timer, search, open or close ever posts. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] A numeric `failureCondition.time` from the Fenix is the seconds left before the failure triggers (a float counting down; at zero the Fenix sets `failed` and drops the condition), never a clock time: `FenixFailureText` speaks "fails in N seconds" and `Changes` never speaks a countdown tick. Full: docs/invariants/fenix-data.md#fnx-?
- [FNX-?] The Fenix failure catalog depends on the loaded variant (A320 CFM 549, A321 IAE 555 failures): never hard-code a failure id, count or chapter; `FenixFailureText.Changes` speaks only ids present in both reads. Full: docs/invariants/fenix-data.md#fnx-?
```
- **Handoff requests:**
  - ~~Plan 01 / `PostRestAsync` Content-Type~~ (done: plan 01 now sends `Content-Type: application/json` and returns the body).
  - ~~Index ownership of the fixture~~ (done).
  - Spec 4.4 and `docs/a32nx.md`: "549 failures" is the A320 CFM; the A321 IAE has 555. Record the endpoints and semantics above (the full rule text for the FNX lines comes from "What the Fenix EFB does") and the owner's in-sim results: AND/OR, `time`, Clear repairs or not, the trigger seen by GET, units (D7).
  - Follow-up, not planned: "clear all" (the EFB has none; it would be one POST per failure).
- **In-sim checks still needed:** optional owner step 3 (AND or OR, on a flight); whether the countdown pauses with the sim. Steps 1-2 and Step 6 passed (see Results).
