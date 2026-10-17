# Shift+C opens the First Officer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Shift+C (output mode) opens the current aircraft's First Officer window, one File menu item replaces eight, and the text checklist window and its files are removed.

**Architecture:** A new static `FirstOfficerWindows` table in `Forms/FirstOfficer/` is the only place that knows which aircraft has a First Officer: `NameFor` (pure, unit-tested) gates Shift+C and the menu item, `Create` builds the window. MainForm keeps one `IFirstOfficerWindow` field. `ChecklistForm`, `ChecklistFileName` and `Checklists\*.txt` are deleted.

**Tech Stack:** .NET 10, C# 13, Windows Forms, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-17-shift-c-first-officer-design.md`

## Global Constraints

- Work on `feature/first-officer` directly (owner decision); commit after each task; do not push until the owner says so.
- Build the solution, never the bare csproj: `dotnet build MSFSBlindAssist.sln -c Debug` ([CORE-1]). Close MSFSBA first if it is running ([CORE-3]).
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- Read every file with the Read tool before editing it, so its rules load ([CORE-16]).
- Exact spoken strings: "No checklist for this aircraft." and "The live Electronic Checklist is only on the A380." plus " Shift+C opens the First Officer." where `NameFor` is not null.
- Match on the EXACT aircraft code; never `is FlyByWireA320Definition` alone (the Headwind A330 derives from it).
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Aircraft swap with the window open or hidden** (A32NX → A330, then Shift+C): must build the A330 window, never re-show the A32NX one. The swap disposes and nulls the one field (Task 2); in-sim step 5.
2. **Shift+C with no sim connection:** the window opens without throwing and comes alive on connect (Task 3 checks each profile's binding; in-sim step 6).
3. **Shift+C while the window is already in front:** `ShowForm` must not record the window itself as the one to return to, or Escape leaves focus nowhere (Task 4).
4. **App exit or update with the window hidden:** only a user close is cancelled, and focus return runs only on Escape or user close (Task 4).
5. **No aircraft loaded yet** (menu gating at startup): `NameFor(null)` is null, so the item stays hidden (Task 1 test `No_aircraft_has_no_first_officer`).

---

### Task 1: `FirstOfficerWindows` and the wider `IFirstOfficerWindow`

**Files:**
- Create: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerWindows.cs`
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/IFirstOfficerWindow.cs`
- Test: `tests/MSFSBlindAssist.Tests/FirstOfficer/FirstOfficerWindowsTests.cs`

**Interfaces:**
- Produces: `public static class FirstOfficerWindows` (namespace `MSFSBlindAssist.Forms.FirstOfficer`) with
  `public static string? NameFor(IAircraftDefinition? aircraft)` and
  `public static IFirstOfficerWindow? Create(IAircraftDefinition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer, UserSettings settings)`.
- Produces: `public interface IFirstOfficerWindow : IDisposable` with `ApplySettings()`, `OnSimConnectChanged()`, `void ShowForm()`, `bool IsDisposed { get; }`.

- [ ] **Step 1: Write the failing test** (namespace `MSFSBlindAssist.Tests.FirstOfficer`; usings `MSFSBlindAssist.Aircraft`, `MSFSBlindAssist.Forms.FirstOfficer` and `Xunit`, but no `using MSFSBlindAssist.FirstOfficer;`, which the folder's namespace shadows)

```csharp
public class FirstOfficerWindowsTests
{
    // The First Officer window Shift+C and the File menu open, by its spoken name, or null where
    // Shift+C says "No checklist for this aircraft." A new aircraft in
    // ComboLabelCollapseTests.AllAircraft fails here until it has a row (FO-24).
    private static readonly Dictionary<string, string?> ExpectedName = new()
    {
        ["PMDG_777"] = "PMDG 777 First Officer",
        ["PMDG_737"] = "PMDG 737 First Officer",
        ["FENIX_A320CEO"] = "Fenix A320 First Officer",
        ["FBW_A380"] = "FlyByWire A380 First Officer",
        ["A320"] = "FlyByWire A32NX First Officer",
        ["HW_A330"] = "Headwind A330 First Officer",
        ["IFLY_737MAX8"] = "iFly 737 MAX8 First Officer",
        ["TFDI_MD11"] = "TFDi MD-11 First Officer",
        ["HS_787"] = null,
    };

    [Theory]
    [MemberData(nameof(ComboLabelCollapseTests.AllAircraft), MemberType = typeof(ComboLabelCollapseTests))]
    public void Every_aircraft_names_its_own_first_officer_or_none(IAircraftDefinition aircraft)
    {
        Assert.True(ExpectedName.TryGetValue(aircraft.AircraftCode, out string? expected), $"no row for {aircraft.AircraftCode}");
        Assert.Equal(expected, FirstOfficerWindows.NameFor(aircraft));
    }

    [Fact]
    public void The_A330_gets_its_own_first_officer_not_the_A32NX_one()
    {
        // HeadwindA330Definition derives from FlyByWireA320Definition.
        Assert.Equal("Headwind A330 First Officer", FirstOfficerWindows.NameFor(new HeadwindA330Definition()));
        Assert.Equal("FlyByWire A32NX First Officer", FirstOfficerWindows.NameFor(new FlyByWireA320Definition()));
    }

    [Fact]
    public void No_aircraft_has_no_first_officer() => Assert.Null(FirstOfficerWindows.NameFor(null));
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~FirstOfficerWindowsTests"`
Expected: build error, `FirstOfficerWindows` does not exist.

- [ ] **Step 3: Widen `IFirstOfficerWindow`**

Add `: IDisposable`, `void ShowForm();` and `bool IsDisposed { get; }` (`FirstOfficerForm` already has all three). Rewrite the summary: MainForm holds the ONE window `FirstOfficerWindows.Create` built; it no longer names `MainForm.OpenFirstOfficerForms()`.

- [ ] **Step 4: Implement `FirstOfficerWindows`**

One private `Dictionary<string, Row>` keyed by exact code, where `Row` is a private record of `string Name` and a `Func<IAircraftDefinition, SimConnectManager, ScreenReaderAnnouncer, UserSettings, IFirstOfficerWindow?>`. Rows, names and profiles exactly as the spec's table (the profile constructors are the ones `MainForm.Dialogs.cs:546-692` calls today: `Pmdg777FoProfile()`, `PMDG737.Pmdg737FoProfile()`, `Fenix.FenixFoProfile(announcer)`, `FBWA380.FbwA380FoProfile(def, announcer)`, `FBWA320.FbwA320FoProfile(def, announcer)`, `HWA330.HwA330FoProfile(def, announcer)`, `IFly737.IFly737FoProfile(def, announcer)`, `MD11.Md11FoProfile(def)`). A row that needs the definition casts (`aircraft is FlyByWireA380Definition def`) and returns null if the cast fails. Every row builds through one private generic helper `Window<TExec, TState>(IFoProfile<TExec, TState> profile, …) => new FirstOfficerForm<TExec, TState>(profile, sim, announcer, settings, new SimBriefService())` with the form's own `where` constraints. Use string literals for the codes (the PMDG and iFly `Code` constants are private). Summary comment cites FO-24.

- [ ] **Step 5: Run the tests and see them pass**

Run: the Step 2 command. Expected: 11 passed (9 theory rows, 2 facts).

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`. Expected: `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerWindows.cs MSFSBlindAssist/Forms/FirstOfficer/IFirstOfficerWindow.cs tests/MSFSBlindAssist.Tests/FirstOfficer/FirstOfficerWindowsTests.cs
git commit -m "feat(fo): FirstOfficerWindows, the one table of which aircraft has a First Officer"
```

---

### Task 2: MainForm holds one First Officer window and one menu item

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs:111-135` (8 fields + `OpenFirstOfficerForms`)
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs:541-692` (8 click handlers + 8 Show methods)
- Modify: `MSFSBlindAssist/MainForm.Designer.cs` (8 items: declarations 20-27, `new` 66-73, File `AddRange` 109-116, property blocks 205-275)
- Modify: `MSFSBlindAssist/MainForm.AircraftSwitch.cs:323-325` (reconnect), `:1068-1079` (swap dispose), `:1280-1293` (menu gating)
- Modify: `MSFSBlindAssist/MainForm.MenuHandlers.cs:282-286` (settings push)

**Interfaces:**
- Consumes: `FirstOfficerWindows.NameFor`, `FirstOfficerWindows.Create`, `IFirstOfficerWindow` (Task 1).
- Produces: `private Forms.FirstOfficer.IFirstOfficerWindow? firstOfficerForm;` and `private void ShowFirstOfficerDialog()` in MainForm; menu item `firstOfficerMenuItem` with handler `FirstOfficerMenuItem_Click`.

- [ ] **Step 1: One field.** Replace the eight typed fields and `OpenFirstOfficerForms()` in `MainForm.cs` with `firstOfficerForm`, commented: built only by `FirstOfficerWindows.Create` (FO-24), disposed on aircraft swap.

- [ ] **Step 2: One Show method.** Replace `MainForm.Dialogs.cs:541-692` with `FirstOfficerMenuItem_Click(object? sender, EventArgs e) => ShowFirstOfficerDialog();` and `ShowFirstOfficerDialog()`: if `firstOfficerForm` is null or disposed, set it from `FirstOfficerWindows.Create(currentAircraft, simConnectManager, announcer, MSFSBlindAssist.Settings.SettingsManager.Current)`; then `firstOfficerForm?.ShowForm()`. A null `Create` result returns silently.

- [ ] **Step 3: One menu item.** In `MainForm.Designer.cs` remove the eight items and add `firstOfficerMenuItem` at the place of the first one in the File `AddRange` (after `settingsMenuItem`): AccessibleName "First Officer", AccessibleDescription "Open the First Officer checklists and flows window", Name "firstOfficerMenuItem", Size (280, 26), Text "&First Officer", Click → `FirstOfficerMenuItem_Click`.

- [ ] **Step 4: Gate, re-wire, dispose, push settings.**
  - `UpdateAircraftSpecificMenuItems` (`AircraftSwitch.cs:1280-1293`): `string? foName = FirstOfficerWindows.NameFor(currentAircraft);` → `Visible = foName != null`, and set `AccessibleName = foName` when not null. Keep `isFenix`/`isPmdg` for the FMC Settings item.
  - Reconnect (`:323-325`) and settings push (`MenuHandlers.cs:285-286`): `if (firstOfficerForm is { IsDisposed: false } fo) fo.OnSimConnectChanged();` / `fo.ApplySettings();`.
  - Swap (`:1068-1079`): `firstOfficerForm?.Dispose(); firstOfficerForm = null;`, keeping the comment's point that nulling unconditionally releases an already-disposed form.

- [ ] **Step 5: Build and check nothing is left over**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `0 Error(s)`.
Run: `grep -rnE "(pmdg777|pmdg737|fenix|fbwA380|fbwA320|ifly737|hwA330|tfdiMd11)FirstOfficer|OpenFirstOfficerForms" MSFSBlindAssist --include=*.cs` → no output.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.Dialogs.cs MSFSBlindAssist/MainForm.Designer.cs MSFSBlindAssist/MainForm.AircraftSwitch.cs MSFSBlindAssist/MainForm.MenuHandlers.cs
git commit -m "refactor(fo): one First Officer window field and File menu item, built by FirstOfficerWindows"
```

---

### Task 3: Shift+C opens the First Officer

**Files:**
- Modify: `MSFSBlindAssist/Hotkeys/HotkeyManager.cs:85` (const), `:409-411` (dispatch), `:769` (register), `:884` (unregister), `:1349` (enum)
- Modify: `MSFSBlindAssist/MainForm.Hotkeys.cs:27` (offline set), `:211-212` (dispatch)
- Modify: `MSFSBlindAssist/MainForm.Dialogs.cs:281-319` (`ShowChecklistDialog`, the ECL refusal)

**Interfaces:**
- Consumes: `ShowFirstOfficerDialog()` (Task 2), `FirstOfficerWindows.NameFor` (Task 1).
- Produces: `HotkeyAction.ShowFirstOfficer`; `private void ShowFirstOfficerFromHotkey()` in MainForm.

- [ ] **Step 1: Rename.** `HotkeyAction.ShowChecklist` → `ShowFirstOfficer`; `HOTKEY_CHECKLIST` → `HOTKEY_FIRST_OFFICER` (id 9052, `MOD_SHIFT`, `0x43` unchanged); register comment "Shift+C (First Officer window)". The action stays in `MainForm.Hotkeys.cs`'s offline set.

- [ ] **Step 2: Hotkey path.** Rename `ShowChecklistDialog` → `ShowFirstOfficerFromHotkey`: `hotkeyManager.ExitOutputHotkeyMode()`; if `FirstOfficerWindows.NameFor(currentAircraft)` is null, `announcer.AnnounceImmediate("No checklist for this aircraft.")` and return; else `ShowFirstOfficerDialog()`. Rewrite its comment: Shift+C opens the aircraft's First Officer (FO-24); the A380's live ECL stays on Ctrl+Shift+C. Wire the dispatch case to it.

- [ ] **Step 3: ECL refusal.** In `ShowChecklistECLDialog`, the non-A380 sentence is "The live Electronic Checklist is only on the A380." plus " Shift+C opens the First Officer." when `NameFor(currentAircraft)` is not null; its comment stops naming the text checklist.

- [ ] **Step 4: Check the offline path.** Read each profile's `BindDataManager` and `SetExecutorSimConnect` (the eight profiles in `FirstOfficerWindows`) and confirm none dereferences a data manager or the SimConnect handle that is null before the sim connects. The menu already opens the window offline, so expect no change; if one would throw, stop and report rather than patch around it.

- [ ] **Step 5: Build and grep**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `0 Error(s)`.
Run: `grep -rnE "ShowChecklist\b|HOTKEY_CHECKLIST\b|ShowChecklistDialog" MSFSBlindAssist --include=*.cs` → no output.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Hotkeys/HotkeyManager.cs MSFSBlindAssist/MainForm.Hotkeys.cs MSFSBlindAssist/MainForm.Dialogs.cs
git commit -m "feat(fo): Shift+C opens the First Officer window"
```

---

### Task 4: Closing the First Officer window returns focus

**Files:**
- Create: `MSFSBlindAssist/Forms/FirstOfficer/NativeForeground.cs`
- Modify: `MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs:218-238` (`ShowForm`), `:385-390` (Escape), `:1136-1149` (`OnFormClosing`)

**Interfaces:**
- Produces: `internal static class NativeForeground` with `public static IntPtr Current()` (`GetForegroundWindow`) and `public static void Restore(IntPtr hWnd)` (`SetForegroundWindow` when not `IntPtr.Zero`). It exists because the CLR refuses a P/Invoke declared inside a generic type, and `FirstOfficerForm<TExec,TState>` is generic.

- [ ] **Step 1: Create `NativeForeground`** with the two `user32.dll` imports private inside it.

- [ ] **Step 2: Remember where the pilot came from.** `FirstOfficerForm` gets `private IntPtr _previousWindow;`. At the top of `ShowForm()`: take `NativeForeground.Current()` and store it only when it is not this form's own `Handle` (a re-press while in front must not overwrite it).

- [ ] **Step 3: Give it back.** A private `HideAndReturnFocus()` does `Hide()` then `NativeForeground.Restore(_previousWindow)`. The Escape `KeyDown` handler and the `CloseReason.UserClosing` branch of `OnFormClosing` call it; every other close reason falls through to the existing teardown untouched.

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `0 Error(s)`. (Behaviour is sim-facing: in-sim steps 1 and 2.)

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/FirstOfficer/NativeForeground.cs MSFSBlindAssist/Forms/FirstOfficer/FirstOfficerForm.cs
git commit -m "feat(fo): closing the First Officer window returns focus to where it was opened from"
```

---

### Task 5: Remove the text checklist window

**Files:**
- Delete: `MSFSBlindAssist/Forms/ChecklistForm.cs`; `MSFSBlindAssist/Checklists/FBW_A320_Checklist.txt`, `FBW_A330_Checklist.txt`, `FBW_A380_Checklist.txt`, `Fenix_A320_Checklist.txt`, `iFly_737MAX8_Checklist.txt`; `tests/MSFSBlindAssist.Tests/ChecklistFileNameTests.cs`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj:163-178` (the "Checklist Files" comment and five `<None Update>` items)
- Modify: `MSFSBlindAssist/Aircraft/IAircraftDefinition.cs:556-561`, `BaseAircraftDefinition.cs:950`, and the overrides in `FenixA320Definition.cs:15`, `FlyByWireA320Definition.cs:116`, `FlyByWireA380Definition.cs:43`, `HeadwindA330Definition.cs:66`, `IFly737MAXDefinition.cs:33`
- Modify: `MSFSBlindAssist/MainForm.cs:86` (`checklistForm` field), `MainForm.AircraftSwitch.cs:939-944` (its dispose)
- Modify: `tests/MSFSBlindAssist.Tests/ClaudeContextBudgetTests.cs:729` (drop `"MSFSBlindAssist/Forms/ChecklistForm.cs"` from the generic-dialogs exemptions)
- Modify (comment only): the "(like ChecklistForm pattern)" summary in `MSFSBlindAssist/Forms/DisplayReadingResultForm.cs:35` and `Forms/FenixA320/FenixAltitudeWindow.cs:42`, `FenixAutopilotWindow.cs:47`, `FenixHeadingWindow.cs:43`, `FenixSpeedWindow.cs:43`, `FenixVSWindow.cs:42` becomes "(the shared non-modal ShowForm pattern)"

**Interfaces:**
- Consumes: Task 3 removed the last readers of `ChecklistFileName` outside this list.

- [ ] **Step 1: Delete the files** with `git rm` (the seven paths above).

- [ ] **Step 2: Remove the members, field, dispose block, csproj items and the exemption entry, and reword the six comments** listed above.

- [ ] **Step 3: Build and grep**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `0 Error(s)`.
Run: `grep -rnE "ChecklistFileName|\bChecklistForm\b|Checklists\\\\" MSFSBlindAssist tests --include=*.cs --include=*.csproj` → no output (`FBWA380ChecklistForm` does not match `\bChecklistForm\b`).

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` → `Failed: 0`. A `ClaudeContextBudgetTests` failure here is about the exemption list: fix what its message names.

- [ ] **Step 5: Commit**

```bash
git add -A MSFSBlindAssist tests
git commit -m "feat(fo): retire the text checklist window and its five checklists"
```

---

### Task 6: Rule FO-24, docs, hotkey guides and the release note

**Files:**
- Modify: `.claude/rules/first-officer.md` (append FO-24), `docs/invariants/first-officer.md` (`## FO-24`), `.claude/rules/mainform-switch-call-sites.md` (mirror)
- Modify: `MSFSBlindAssist/HotkeyGuides/FBW_A320_Hotkeys.txt:156`, `FBW_A330_Hotkeys.txt:159`, `FBW_A380_Hotkeys.txt:161`, `Fenix_A320_Hotkeys.txt:151`, `iFly_737MAX8_Hotkeys.txt:119`, `PMDG_737_Hotkeys.txt` (after `:158`), `PMDG_777_Hotkeys.txt` (after `:162`), `TFDi_MD11_Hotkeys.txt` (new block after the Traffic block, `:182-184`)
- Modify: `docs/first-officer.md:7` and a new section, `docs/adding-features.md:341`, `docs/md11.md:2053`, `docs/troubleshooting-playbook.md:131`, `README.md:74`
- Modify: `changelog.d/160-first-officer.feature.md` (first paragraph, and the "While the window is open" sentences)

- [ ] **Step 1: The rule.** Append to `.claude/rules/first-officer.md`, word for word, and mirror the same line into `mainform-switch-call-sites.md`:

```
- [FO-24] Which aircraft has a First Officer window is answered only by `FirstOfficerWindows` (`NameFor` for Shift+C and the File menu item, `Create` for the window), keyed on the exact aircraft code; never a per-aircraft form field, Show method, menu item or type test in MainForm. A new First Officer aircraft adds one row. Full: docs/invariants/first-officer.md#fo-24
```

Under `## FO-24` in `docs/invariants/first-officer.md`: why (eight copies of the gating before 2026-10-17; the A330-derives-from-A320 trap), where (`Forms/FirstOfficer/FirstOfficerWindows.cs`, pinned by `FirstOfficerWindowsTests`), and that Shift+C replaced the retired text checklist.

- [ ] **Step 2: Run the rule tests**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ClaudeContextBudgetTests|FullyQualifiedName~ClaudeRulesHookTests"` → `Failed: 0`. On a failure, follow its message (it names the file and the remedy). Never raise a budget to make it pass ([CCT-4]): shorten FO-24 or its mirror, or stop and report.

- [ ] **Step 3: Hotkey guides.** Shift+C line text: `First Officer window (checklists and flows)`, in each guide's own column format (the Fenix guide uses `Shift+C,`). Replace the existing line in the five guides; add it after the SimBrief line in the PMDG 737 and 777 guides; in the MD-11 guide add a `First Officer:` block with that one line after the Traffic block. HS787 unchanged.

- [ ] **Step 4: Docs.**
  - `first-officer.md:7`: "Opened from a per-aircraft File menu item…" becomes Shift+C in output mode or the one File → First Officer item, both gated by `FirstOfficerWindows` (FO-24). Add `## Opening the window: Shift+C (2026-10-17)` stating: Shift+C or the menu; the 787 hears "No checklist for this aircraft."; the first open starts the automation for the rest of the aircraft session, hidden or not; Escape returns focus to where it was opened from; the text checklists were retired.
  - `adding-features.md:341`: drop the text-checklist clause; a First Officer aircraft adds a `FirstOfficerWindows` row and a `FirstOfficerWindowsTests` row.
  - `md11.md:2053`: drop the `TFDi_MD11_Checklist.txt` follow-up; Shift+C opens the MD-11 First Officer.
  - `troubleshooting-playbook.md:131`: append "(2026-10-17: the text checklist is retired; Shift+C opens the First Officer, FO-24.)". Do not rewrite the history.
  - `README.md:74`: the checklist-viewer bullet becomes the First Officer, interactive checklists and flows, on Shift+C.

- [ ] **Step 5: Release note.** In `changelog.d/160-first-officer.feature.md` (unreleased, #160's own), replace "Open it from the File menu: … only shows up when you are flying that aircraft." with, for a pilot: press Shift+C in output mode, or choose First Officer in the File menu (it shows only on an aircraft that has one); on the A32NX, A330, Fenix A320, A380 and iFly MAX8, Shift+C used to open a plain text checklist, and that window is gone, replaced by these checklists; opening the window either way starts the First Officer's automation for the rest of the flight. Change the two "While the window is open" sentences to "Once the window has been opened".

- [ ] **Step 6: Commit**

```bash
git add .claude/rules/first-officer.md .claude/rules/mainform-switch-call-sites.md docs/invariants/first-officer.md MSFSBlindAssist/HotkeyGuides docs/first-officer.md docs/adding-features.md docs/md11.md docs/troubleshooting-playbook.md README.md changelog.d/160-first-officer.feature.md
git commit -m "docs(fo): FO-24, the hotkey guides and the release note for Shift+C"
```

---

### Task 7: Final verification and the owner's in-sim test

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug` → `0 Error(s)`; full `dotnet test …` → `Failed: 0`. Quote both summaries.
- [ ] **Step 2:** `git log --oneline 5d8f64bd..HEAD` shows the spec/plan commit plus Tasks 1-6; `git status --short` is clean.
- [ ] **Step 3:** Hand the owner the spec's "In-sim test plan" (steps 1-7) and the "Effect on open pull requests" list. Ask before pushing `feature/first-officer` or editing PR #160's description; do neither unasked.
