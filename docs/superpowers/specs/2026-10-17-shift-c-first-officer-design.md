# Shift+C opens the First Officer; the text checklist window is retired

Date: 2026-10-17. PR #160 (First Officer), committed directly on `feature/first-officer`.

## Problem

Once #160 merges, MSFSBA has two checklist windows:

- **Shift+C** (output mode) opens `ChecklistForm`, a passive tick-list read from
  `Checklists\<ChecklistFileName>`, or says "No checklist for this aircraft."
  (`MainForm.ShowChecklistDialog`). It works without a sim connection.
- **The First Officer window** (`FirstOfficerForm<TExec,TState>`) has interactive checklists and
  flows, but no hotkey. It opens only from eight per-aircraft File menu items, each visible on
  its own aircraft. MainForm holds eight typed form fields and eight `Show…FirstOfficerDialog`
  methods, and the "does this aircraft have a First Officer" answer is spread over
  `UpdateAircraftSpecificMenuItems` (aircraft-code checks) and the Show methods (type checks).

The First Officer supersedes the text checklist. On today's fleet the two windows do not
overlap the way they seem to:

| Aircraft | Text checklist | FO profile | Shift+C before | Shift+C after |
|---|---|---|---|---|
| FBW A32NX, Headwind A330, Fenix A320, FBW A380, iFly 737 MAX8 | yes | yes | text checklist | First Officer |
| PMDG 737, PMDG 777, TFDi MD-11 | none | yes | "No checklist for this aircraft." | First Officer |
| HorizonSim 787 | none | none | "No checklist for this aircraft." | unchanged |

No aircraft has a text checklist without a First Officer, so the text checklist window would
never open again.

## Decisions (owner, 2026-10-17)

1. Shift+C opens the current aircraft's First Officer window. An aircraft without one says
   "No checklist for this aircraft."
2. The text checklists are retired: the five `Checklists\*.txt` files, `ChecklistForm`,
   `ChecklistFileName` and their tests go. The walkthrough content they held (MCDU setup, climb
   and cruise, app hotkey hints) is dropped, not moved.
3. Opening the window by Shift+C starts the First Officer exactly as the menu does today:
   its automation (10,000 ft lights and transition altimeters by default, plus whatever the
   pilot enabled) runs for the rest of the aircraft session, and the in-flight catch-up
   ([FO-22]) applies. No extra announcement; the changelog says so plainly.
4. The eight File menu items become one "First Officer" item, backed by one factory.
5. The work is committed directly on `feature/first-officer`, as part of #160.

## Design

### `FirstOfficerWindows`: the one answer to "which aircraft has a First Officer"

A new static class, `Forms/FirstOfficer/FirstOfficerWindows.cs`, holds one private table, one
row per First Officer aircraft, keyed on the EXACT `AircraftCode`:

| Code | Spoken name | Profile |
|---|---|---|
| `PMDG_777` | PMDG 777 First Officer | `Pmdg777FoProfile()` |
| `PMDG_737` | PMDG 737 First Officer | `PMDG737.Pmdg737FoProfile()` |
| `FENIX_A320CEO` | Fenix A320 First Officer | `Fenix.FenixFoProfile(announcer)` |
| `FBW_A380` | FlyByWire A380 First Officer | `FBWA380.FbwA380FoProfile(def, announcer)` |
| `A320` | FlyByWire A32NX First Officer | `FBWA320.FbwA320FoProfile(def, announcer)` |
| `HW_A330` | Headwind A330 First Officer | `HWA330.HwA330FoProfile(def, announcer)` |
| `IFLY_737MAX8` | iFly 737 MAX8 First Officer | `IFly737.IFly737FoProfile(def, announcer)` |
| `TFDI_MD11` | TFDi MD-11 First Officer | `MD11.Md11FoProfile(def)` |

The spoken names are today's menu-item accessible names. Matching on the exact code keeps the
Headwind A330 (whose definition derives from `FlyByWireA320Definition`) on its own profile
without the type-test ordering the current Show methods rely on. `FENIX_A320CEO` is the only
code `FenixA320Definition` reports (today's menu gate is the looser `StartsWith("FENIX_")`).

Two members, both reading that table:

- `string? NameFor(IAircraftDefinition aircraft)`: the spoken name, or null when the aircraft
  has no First Officer. Pure. It decides whether the menu item shows, gives it its accessible
  name, and decides what Shift+C does.
- `IFirstOfficerWindow? Create(IAircraftDefinition aircraft, SimConnectManager sim,
  ScreenReaderAnnouncer announcer, UserSettings settings)`: builds the profile and the closed
  generic `FirstOfficerForm<TExec,TState>` exactly as the Show method does today (including
  `new SimBriefService()`), or null when there is no row. A row whose profile needs the
  definition casts it (`aircraft is FlyByWireA380Definition def`) and returns null if the cast
  fails, as the Show methods do now.

### `IFirstOfficerWindow` grows to what MainForm needs

`IFirstOfficerWindow` gains `void ShowForm()` and `bool IsDisposed`, and extends `IDisposable`.
`FirstOfficerForm` already has all three. Its doc comment stops naming
`MainForm.OpenFirstOfficerForms()`.

### MainForm

- The eight typed fields become one `Forms.FirstOfficer.IFirstOfficerWindow? firstOfficerForm`.
- The eight `Show…FirstOfficerDialog` methods and eight `…FirstOfficerMenuItem_Click` handlers
  become one `ShowFirstOfficerDialog()`: create the window if it is null or disposed
  (`FirstOfficerWindows.Create`), then `ShowForm()`. It returns silently when `Create` returns
  null (only reachable from a stale menu click).
- `OpenFirstOfficerForms()` is removed. Its two callers (the Settings save in
  `MainForm.MenuHandlers.cs`, the reconnect re-wire in `MainForm.AircraftSwitch.cs`) use
  `firstOfficerForm is { IsDisposed: false } fo`.
- The aircraft swap disposes and nulls the one field instead of eight.
- The eight menu items in `MainForm.Designer.cs` become one `firstOfficerMenuItem`, at the same
  place in the File menu: Text "&First Officer", AccessibleDescription "Open the First Officer
  checklists and flows window". `UpdateAircraftSpecificMenuItems` sets its `Visible` to
  `NameFor(currentAircraft) != null` and its `AccessibleName` to that name.

### Shift+C

- `HotkeyAction.ShowChecklist` is renamed `ShowFirstOfficer` (nothing persists the enum name).
  `HOTKEY_CHECKLIST` keeps its id and registration; its comment and name follow
  (`HOTKEY_FIRST_OFFICER`). The action stays in the offline set: the window opens without a sim
  connection, as the menu item does today, and re-wires on connect (`OnSimConnectChanged`).
- `ShowChecklistDialog` is renamed `ShowFirstOfficerFromHotkey` and becomes the hotkey path:
  exit output mode, then, if
  `NameFor(currentAircraft)` is null, say "No checklist for this aircraft." (AnnounceImmediate,
  as today); otherwise `ShowFirstOfficerDialog()`.
- Pressing Shift+C while the window is open brings it to the front with its tab and selection
  as they were; no tab is forced.

### Focus goes back where it came from

`ChecklistForm` captured the foreground window on show and gave it focus back on close, so
Shift+C, read, Escape returned the pilot to the sim. `FirstOfficerForm` hides on Escape and on
a user close with no focus restore. It gains the same behaviour: `ShowForm()` captures
`GetForegroundWindow()` (unless the window is already the foreground one, so a re-show does not
capture itself), and the Escape handler and the user-close path call `SetForegroundWindow` on it
after hiding. From the menu, that window is MainForm.

### The A380 ECL key on other aircraft

`ShowChecklistECLDialog`'s refusal on an aircraft that is not the A380 becomes "The live
Electronic Checklist is only on the A380." followed by " Shift+C opens the First Officer." only
where `NameFor` is not null. Today's second sentence names the text checklist.

### Removal

- `Forms/ChecklistForm.cs`.
- `Checklists/FBW_A320_Checklist.txt`, `FBW_A330_Checklist.txt`, `FBW_A380_Checklist.txt`,
  `Fenix_A320_Checklist.txt`, `iFly_737MAX8_Checklist.txt`, and their five `<None Update>` items
  (and the "Checklist Files" comment) in `MSFSBlindAssist.csproj`.
- `IAircraftDefinition.ChecklistFileName`, the `BaseAircraftDefinition` default, and the
  overrides in the FBW A320, Headwind A330, Fenix A320, FBW A380 and iFly 737 MAX8 definitions.
- MainForm's `checklistForm` field and its dispose in the aircraft swap.
- `tests/MSFSBlindAssist.Tests/ChecklistFileNameTests.cs`.

Released changelog fragments that mention the text checklist (`266-checklist-fallback-warning`)
stay, as the changelog convention requires.

## Rule

A new First Officer rule, next number FO-24, in `.claude/rules/first-officer.md` with its full
text under `## FO-24` in `docs/invariants/first-officer.md`:

> [FO-24] Which aircraft has a First Officer window is answered only by `FirstOfficerWindows`
> (`NameFor` for Shift+C and the File menu item, `Create` for the window), keyed on the exact
> aircraft code; never a per-aircraft form field, Show method, menu item or type test in
> MainForm. A new First Officer aircraft adds one row.

The rule file's globs cover `Forms/FirstOfficer/**`, where `FirstOfficerWindows` is declared.
The aircraft code is deliberately not backticked: the rule's code does not declare it. The
rule's "never" guards MainForm (`MainForm.Dialogs.cs`, `MainForm.cs`, `MainForm.AircraftSwitch.cs`),
so its line is mirrored word for word into `mainform-dialogs-call-sites.md` with a MIRRORS
note, unless `ClaudeContextBudgetTests` reports that file already loads it or names another
call-sites file.

## Tests

`tests/MSFSBlindAssist.Tests/FirstOfficerWindowsTests.cs` replaces `ChecklistFileNameTests`,
with the same shape: a theory over `ComboLabelCollapseTests.AllAircraft` and an expected-name
table with one row per aircraft code (the eight names above, `HS_787` null). A new aircraft in
`AllAircraft` fails until it has a row, as before. A separate fact asserts that the Headwind A330
and the FBW A32NX get different names, pinning the derived-definition trap.

`Create` builds a WinForms window over a live `SimConnectManager`, so it stays out of the unit
tests ([CORE-5]); the in-sim plan covers it.

## Docs and release note

- Hotkey guides: in `FBW_A320`, `FBW_A330`, `FBW_A380`, `Fenix_A320` and `iFly_737MAX8`, the
  Shift+C line becomes the First Officer window (checklists and flows). `PMDG_737`, `PMDG_777`
  and `TFDi_MD11` gain that line. `HS787` is unchanged.
- `docs/first-officer.md`: the overview's "Opened from a per-aircraft File menu item" becomes
  Shift+C in output mode or the one File menu item, gated by `FirstOfficerWindows`, plus a short
  section on Shift+C (the automation starting on first open, the focus return).
- `docs/adding-features.md`: the text-checklist clause goes; a First Officer aircraft adds a
  `FirstOfficerWindows` row.
- `docs/md11.md`: the `TFDi_MD11_Checklist.txt` follow-up is struck; Shift+C opens the MD-11
  First Officer.
- `docs/troubleshooting-playbook.md`: the A380 Shift+C note gets a dated line saying the text
  checklist was retired and Shift+C opens the First Officer.
- `README.md`: the "checklist viewer" bullet becomes the First Officer on Shift+C.
- `changelog.d/160-shift-c-first-officer.improvement.md`, written for a pilot: Shift+C now
  opens the First Officer (checklists and flows) on every aircraft that has one, including the
  PMDG 737 and 777 and the MD-11, which had no checklist; the old text checklists are gone; the
  File menu has one First Officer item; opening it starts the First Officer's automation as
  the menu always did, each part switchable in Settings, First Officer.

## In-sim test plan (owner runs)

1. **Each family** (one Airbus with a text checklist before, e.g. the Fenix; one Boeing that
   had none, e.g. the PMDG 737; the MD-11): from the sim, `]` then Shift+C opens that aircraft's
   First Officer window, titled for it. Escape returns focus to the sim. Shift+C again shows the
   same window on the tab last used, ticks intact.
2. **File menu:** one "First Officer" item, read with the aircraft's name, opening the same
   window; no item on the HorizonSim 787.
3. **HorizonSim 787:** Shift+C says "No checklist for this aircraft." and opens nothing.
4. **A380:** Ctrl+Shift+C still opens the live ECL; Shift+C opens the A380 First Officer. On
   another aircraft, Ctrl+Shift+C says the ECL is A380-only and that Shift+C opens the First
   Officer (the 787: the first sentence only).
5. **Aircraft swap:** load the FBW A32NX, open with Shift+C, switch to the Headwind A330, press
   Shift+C: the A330 window, not the A32NX one.
6. **Offline:** with the sim not connected, Shift+C opens the window; connecting afterwards
   brings its checklists to life (auto-detection ticks).
7. **Output mode:** after Shift+C, output mode is off (the next key is not an output command).

## Out of scope

- Changing when or how First Officer automation starts.
- A replacement for the text checklists' walkthrough content.
- The A380 live ECL window and its Ctrl+Shift+C key.
