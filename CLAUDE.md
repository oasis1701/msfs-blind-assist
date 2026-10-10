# CLAUDE.md

Guidance for Claude Code in this repository, kept small on purpose: each area's rules load on their own when you read that area's code, and their full text lives in `docs/invariants/`. See "Where things live" and "Adding or changing a rule" below.

## Project Overview

MSFS Blind Assist - C# Windows Forms accessibility application for Microsoft Flight Simulator. Multi-aircraft support (FlyByWire A320, Fenix A320, extensible). SimConnect integration, screen reader optimized (NVDA/JAWS). .NET 10, Windows Forms, SQLite.

## Build

```bash
dotnet build MSFSBlindAssist.sln -c Debug
dotnet build MSFSBlindAssist.sln -c Release
```

The app runs from `MSFSBlindAssist\bin\x64\{Debug|Release}\net10.0-windows\`. Prerequisites: the MSFS_SDK environment variable and the .NET 10 SDK. The solution builds six projects, `tools/PMDGDispatchTester`, `tools/ChangelogBuilder` and the vPilot plugin among them; the other 12 `tools/*` projects (`tools/CDUTest`, the probes and the sweeps) build on their own. Output paths, the projects and the probes in full: [docs/development.md](docs/development.md#build-output-and-traps).

- [CORE-1] Always build the `.sln` or pass `-p:Platform=x64`; never the bare `.csproj`, which defaults to AnyCPU and writes to `bin\Debug\…`, so the x64 exe the app runs from never updates. Full: docs/invariants/core.md#core-1
- [CORE-2] Keep `RuntimeIdentifier=win-x64` (else the build ships every platform's SQLite binaries) and `AppendRuntimeIdentifierToOutputPath=false` (else it writes to a `win-x64\` folder the app, release.yml and PostBuild never use) in the csproj. `dotnet publish` writes `win-x64\publish\`; other `win-x64\` build output predates 2026-08-25: stale. Full: docs/invariants/core.md#core-2
- [CORE-3] The exe is file-locked while MSFSBA runs (MSB3021): close the app before building an exe the user will run. Full: docs/invariants/core.md#core-3
- [CORE-4] `dotnet build MSFSBlindAssist.sln` builds only `tools/PMDGDispatchTester` and `tools/ChangelogBuilder`; the other 12 `tools/*` projects (`CDUTest`, the probes and the sweeps, listed in docs/development.md) build on their own, never as part of the solution. Full: docs/invariants/core.md#core-4

## Testing

Pure-logic code is covered by an xUnit characterization suite at `tests/MSFSBlindAssist.Tests`
(run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`).
CI (`.github/workflows/tests.yml`) runs it on every PR and on pushes to main.
Sim-facing behavior cannot be unit-tested: when changing SimConnect/UI paths, build, then
describe an in-sim test plan in the PR — the human owner of the repo runs it. New pure logic
(formatters, parsers, geometry, classifiers) should get characterization tests; don't add
speculative tests for sim-driven paths.

- [CORE-5] Sim-facing paths are verified only in a live sim: a PR touching them describes an in-sim test plan. Pure logic gets characterization tests in `tests/MSFSBlindAssist.Tests` (CI-enforced). Full: docs/invariants/core.md#core-5

## Before changing behaviour

**Check before calling it an oversight.** Before changing how something BEHAVES (wording,
rounding, what a control shows, a checklist step, a default), rather than fixing a clear defect,
read the aircraft's rule files (`.claude/rules/`) and its own doc: many differences between aircraft
are deliberate and pinned there ("never harmonize"). Then compare:

- For how an aircraft SYSTEM behaves, compare only against another add-on of the same type: the
  FBW A32NX against the Fenix, the PMDG 737 against the iFly 737. Different types differ by
  design, even from the same maker (a 787 is not a 737, an A380 is not an A320), so a difference
  there is not an oversight.
- For how the APP presents things (whether a window shows a control's state, wording, number
  formatting), compare across all aircraft.

If the comparison shows this aircraft is the odd one out, fix it and say in the PR which aircraft
you compared against. If not, it is a design choice: write the PR as a proposal the reviewer can
adjust or drop. Worked example: #264. The FBW A320 and A380 autopilot windows showed a fixed
"Autothrust engage" button, while every other button in those windows, and the autothrottle
buttons of the HS787, iFly 737 and MD-11 windows, showed state. That is a presentation question,
so comparing across types was valid. The Fenix window shows state on none of its buttons, so it
was left alone.

**Fix the aircraft the request is about.** If the same problem shows up on another aircraft, fix
it in the same PR only when the code is shared (#264: the A320 and A380 read the same
`A32NX_AUTOTHRUST_STATUS` through one helper). When the other aircraft keeps its own code, name it
in the PR as "also seen on …" and leave it for its own issue or PR, so it gets its own in-sim test
and review. The other aircraft shows what is missing; it is not a template for the fix. Build each
aircraft's fix from its own variables and behaviour.

**Check the whole of what you touched.** For a display, check the whole page, not only the broken
line (#259 fixed one MCDU line and pushed the others off the 24-column grid, fixed in #262). For
shared code, check every aircraft that uses it, including those that override it (#260's shared
trim debounce also had to hold on the PMDG 777's override). For anything spoken or formatted, add a
test that sets a comma-decimal culture (de-DE or sv-SE): CI runs under en-US, so a test that does
not set one cannot catch it.

## Git workflow and release notes

The `main` branch is protected. Always create a new branch for changes and open a pull request — never commit directly to main. Every PR adds `changelog.d/<pr>-<slug>.<category>.md`, written for a pilot, not a reviewer; a required CI check fails without it. `<pr>` is READ, never guessed (issues and PRs share one number sequence): commit and push, open the PR (`gh pr create` prints its number), then add the fragment, commit and push. Categories: `aircraft`, `feature`, `improvement`, `fix`, `internal` (never published). A released fragment is never deleted; before merging, a long-running PR's own fragments may be condensed to what a pilot sees changed. Full convention: [changelog.d/README.md](changelog.d/README.md).

- [CORE-6] `main` is protected: never commit to it directly; always branch and open a PR. Full: docs/invariants/core.md#core-6

## Rules for any file

### Screen reader announcements

Screen readers already announce every UI control interaction, so the app never repeats one: it confirms a direct interaction once, and only with what the reader cannot say, and it always speaks numeric input confirmations, validation errors and background state changes the user did not trigger. Today's cases: the TFDi MD-11's once-after-settle press confirmation ([MD11-11]); the EFB shell's `announceChange` opt-in ([MD11-26]); the FBW A320, Headwind A330 and FBW A380 button read-back (`GetButtonStateMapping`, `HandleButtonStateAnnouncement`); the value-dialog toggle read-back (`ValueInputForm`, about 1.2 s after a press); "<name> pressed" for a momentary button or an Idle/Activate action combo with no readable state (`PulseMomentaryLVar`, the A380's `PulseEcpKey`, MainForm's unhandled-button fallback); the A380 FCU push/pull value read-out (`OnPanelButtonFired`); the FBW E/WD pop-out's refresh and the menu's hotkey suspend and resume confirmations.

- [CORE-7] Never repeat what the screen reader just said (a panel control's press, label or new combo value). Confirm a direct interaction once, only with what the reader cannot say: a state read back after the press, or "<name> pressed" for an action with no readable state. Numeric confirmations, validation errors and background changes always speak. Full: docs/invariants/core.md#core-7
- [CORE-8] Combo double-announce suppression is GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window. Full: docs/invariants/core.md#core-8
- [CORE-9] The echo wrap around `ProcessSimVarUpdate` matches on TIME only, never on value: a combo set can write a different encoding than the SDK reads back, so a value compare silently misses. The generic `_uiSetEcho` gate stays value-matched (opt-out: `UiEchoMatchesAnyValue`), so another source's change inside the window still speaks. Full: docs/invariants/core.md#core-9
- [CORE-10] Never blanket-suppress value-0 resting-state button labels in MainForm; use the opt-in `SimVarDefinition.SuppressRestingButtonState` only. "LNAV: Off" and "Baro STD: QNH" are real states. Full: docs/invariants/core.md#core-10

### Everywhere else

- [CORE-16] Read a file with the Read tool before changing it: area rules load with Read, Edit or Write, and with a lone `cat`, `head`, `tail` or `sed -n` of one file, never with grep, a pipe, a redirect, PowerShell, Grep, Glob or a shell write. `rules-hook.ps1` covers subagent worktrees and `git diff`, and refuses shell writes to covered files. Full: docs/invariants/core.md#core-16
- [CORE-11] In `SimConnectManager`, set `IsConnected = true` BEFORE calling `SetupDataDefinitions()`: `StartContinuousMonitoring()` guards on it. Full: docs/invariants/core.md#core-11
- [CORE-12] Never use `TreeView` directly in a form: use `NativeAccessibleTreeView` (the .NET UIA tree gives NVDA a wrong order); a tree with detail data populates its children lazily on `BeforeExpand`. Full: docs/invariants/core.md#core-12
- [CORE-13] Never hardcode the FBWBA/MSFSBlindAssist database path: reads go through `DatabasePathResolver.ResolveExistingDatabasePath`, writes through `GetCanonicalDatabasePath`. Full: docs/invariants/core.md#core-13
- [CORE-14] Every diagnostic log path resolves through `Utils/AppLogs.PathFor(...)` into `%APPDATA%\MSFSBlindAssist\logs`; never hand-build one (exempt, as in [CORE-15]: the vPilot plugin and the updater). Full: docs/invariants/core.md#core-14
- [CORE-15] Never hand-build a log write (`File.AppendAllText`, a raw path): use `Log.Debug/Info/Warn/Error(category, msg)` for debug.log, or `Log.Channel(name)` for a named log. Exempt, as they cannot reference the app: the vPilot plugin's log ([VAT-6]) and the updater's own `%TEMP%` args log (`MSFSBlindAssistUpdater/Program.cs`). Full: docs/invariants/core.md#core-15
- [VAT-13] Status/diagnostic text in any settings panel is a read-only `TextBox`, never a `Label`: a `Label` is not in the tab order, so a screen-reader user has to hunt for it with the review cursor. Full: docs/invariants/vatsim.md#vat-13
- [A380C-6] Every form marshaling a background bridge push to the UI thread must wrap `BeginInvoke` in try/catch(InvalidOperationException) (`SafeBeginInvoke`); an `IsHandleCreated` check alone races handle destruction. Full: docs/invariants/a380-coherent.md#a380c-6

## Multi-Aircraft Architecture

**Core interfaces:**
- **IAircraftDefinition** - Contract for all aircraft
- **BaseAircraftDefinition** - Recommended base class (provides hotkey routing, caching, helpers)
- **`tests/MSFSBlindAssist.Tests/Walkthroughs/YourAircraftDefinition.cs`** - The compiled template to copy for a new aircraft; the walkthroughs in [adding-features.md](docs/adding-features.md) show its parts

**Each aircraft defines:**
- `BuildVariables()` - All simulator variables, starting from `GetBaseVariables()` (the base class caches them as `GetVariables()`)
- `GetPanelStructure()` - Section/panel hierarchy
- `BuildPanelControls()` - Panel-to-variables mapping (cached automatically by base class)
- `GetHotkeyVariableMap()` - Optional simple hotkey action → event name map (only the FBW A320 and A380 override it)
- `HandleHotkeyAction()` - Hotkey logic (most aircraft override it)

## Quick Reference

- Adding a panel control, background monitoring, an H-variable, a hotkey, an aircraft or a feature: follow its workflow in [adding-features.md](docs/adding-features.md); the short forms are in [QUICK-REFERENCE.md](docs/QUICK-REFERENCE.md). The rules for aircraft code load when you Read it.
- **`SimConnectManager.SetLVar` — GLOBAL MobiFlight calc-path routing (2026-06):** A `SetLVar` write takes the calculator path only once `CalcPathVerified` is set, which only an aircraft registering `MSFSBA_BRIDGE_PROBE` reaches (FBW A320/A380, Headwind A330, MD-11); a VERIFIED verdict then holds for any aircraft switched to ([MD11-16]). Otherwise, and for a name with a space or colon ([SIM-12]), it takes the native data-def write. Full routing rules, the H:/dotted event queue, and the RPN invariant-formatting rule: [docs/architecture.md](docs/architecture.md).

## Where things live

Each area's rules load automatically when Claude reads its code; their full text is `docs/invariants/<rule file>.md`. Read a doc when the task needs it. Planning from a doc? Read the rule files in its row too: rules load with code, never with docs.

| Doc | Read when | Rule files |
| --- | --- | --- |
| [architecture.md](docs/architecture.md) | Core components, FCU/MCP/display systems, the SimConnect data-definition ceiling, MobiFlight calc-path routing | core-simconnect, simconnect-data, simconnect-events, monitor-manager, navdata-build, flight-planning-efb |
| [aircraft-definitions.md](docs/aircraft-definitions.md) | The multi-aircraft definition API | variable-definitions |
| [QUICK-REFERENCE.md](docs/QUICK-REFERENCE.md) | Common patterns and workflows (read first for most tasks) | — |
| [adding-features.md](docs/adding-features.md) | Step-by-step workflows for common tasks | — |
| [variable-system.md](docs/variable-system.md) | The three variable patterns (panel, monitoring, hotkey) | — |
| [hotkey-system.md](docs/hotkey-system.md) | Adding or changing hotkeys | — |
| [development.md](docs/development.md) | Dependencies, key files, build output paths and traps, and the Claude Code hooks | claude-tooling |
| [tooling.md](docs/tooling.md) | Live debugging over the Coherent debugger (`:19999`), the probes in `tools/`, crash diagnosis | coherent-clients |
| [troubleshooting-playbook.md](docs/troubleshooting-playbook.md) | A control "doesn't work": read this FIRST, before calling it broken or unsettable | troubleshooting |
| [taxi-guidance.md](docs/taxi-guidance.md) | Taxi guidance, runway holds, landing exits and rollout, ground traffic, surroundings, takeoff assist | taxi-routing, taxi-graph, taxi-position, runway-holds, taxi-steering, landing-exits, landing-rollout, landing-touchdown, rollout-geometry, ground-traffic, surroundings, scenery-index, taxi-augmentation, takeoff-and-callouts |
| [gsx.md](docs/gsx.md) | GSX gate selection and Remote API, docking guidance, the metres/feet toggle | gsx-remote, gsx-stands-docking, gsx-stands, gsx-docking |
| [weather.md](docs/weather.md) | ActiveSky, the weather radar, METAR readouts, weather announcements | weather |
| [sayintentions.md](docs/sayintentions.md) | SayIntentions: clearance parsing, the taxi-route import, readouts | sayintentions-clearance, sayintentions-import, sayintentions-gates, sayintentions-readouts |
| [vatsim.md](docs/vatsim.md) | VATSIM: the vPilot plugin, pipe server, announcement settings | vatsim |
| [updates.md](docs/updates.md) | Release and preview channels, the updater, release workflows | updates |
| [visual-guidance.md](docs/visual-guidance.md) | Visual landing guidance (dual tone), hand fly, the liftoff handoff | visual-guidance |
| [audio.md](docs/audio.md) | Which Windows audio endpoint guidance tones play on | audio-output |
| [fenix-increment-decrement.md](docs/fenix-increment-decrement.md) | Fenix rotary encoders (RMP, FCU) | — |
| [a32nx.md](docs/a32nx.md) | FlyByWire A32NX, Fenix A320, Headwind A330: panels, MCDU, DCDU, cockpit controls | a32nx-fenix, fcu-callouts, tcas-ra, a32nx-mcdu, fbw-arinc |
| [a380x.md](docs/a380x.md) | FlyByWire A380X: FCU/EFIS, MFD/MCDU, OANS/BTV, RMP, ECAM, checklists | a380-fcu, fcu-callouts, a380-coherent, a380-systems, a380-panels, tcas-ra, fbw-arinc |
| [flypad.md](docs/flypad.md) | The shared FlyByWire flyPad EFB (A320 and A380) | flypad |
| [pmdg-777.md](docs/pmdg-777.md) | PMDG 777: CDA switches, CDU indexing, System Display | pmdg-777 |
| [pmdg-737.md](docs/pmdg-737.md) | PMDG 737-800 NG3: two CDUs, NG3 struct, EFB parity with the 777 (Shift+T) | pmdg-737 |
| [pmdg-efb.md](docs/pmdg-efb.md) | The PMDG (and HS787 CDU) Coherent-debugger EFB agent | pmdg-efb |
| [ifly-737.md](docs/ifly-737.md) | iFly 737 MAX8: SDK shared memory + WM_COPYDATA, no MobiFlight, no L:var writes except named clickspot replays | ifly-737 |
| [hs787.md](docs/hs787.md) | HorizonSim 787-9: CDU, IRS, EICAS over the Coherent debugger | hs787 |
| [md11.md](docs/md11.md) | TFDi MD-11: CEVENT transport, control state, layout, the control-map generator | md11, md11-controls |
| [gemini.md](docs/gemini.md) | AI providers (Gemini or Claude): display reads, scene and route description, route briefing | ai-display, route-briefing |

## Adding or changing a rule

A rule is a guardrail a future change could break: a "never", a "must", a measured trap. Write it as ONE line, at most 400 characters, in the rule file of the code it guards:

`- [PREFIX-n] <the rule, naming the key type or method> Full: docs/invariants/<stem>.md#<prefix-n>`

One mechanism per ID: an independent never, must or exemption gets its own line, unless a test whose failure explains it already pins it. Take the next unused number for that prefix; IDs are never renumbered or reused, so code comments and commit messages can cite them. The explanation, measurements and history go under `## PREFIX-n` in `docs/invariants/<stem>.md`. A new aircraft or subsystem gets its own rule file, with `paths:` globs for its code and its tests, and its own full-text file, plus a row above; until it has a rule, its rule file is a heading and one line naming its doc. Before writing the line, Read the file that DECLARES what it guards (for a partial class, the partial holding that member) and confirm one of the rule file's globs matches it: `TaxiGuidanceManager`, `TaxiGraph` and `MainForm` are split into partials by mechanism, not by area, and the test named below checks only the names a rule gives in backticks, not what it guards. When a rule's code lives in or is called from another area's files, MIRROR its line word for word into a rule file scoped to those files (`mainform-call-sites.md` for no-identifier rules on every partial, `mainform-announcers-call-sites.md`, `mainform-dialogs-call-sites.md`, `mainform-guidance-call-sites.md`, `mainform-surroundings-call-sites.md`, `mainform-services-call-sites.md`, `mainform-sayintentions-call-sites.md`, `mainform-panel-call-sites.md`, `mainform-fcu-echo-call-sites.md`, `mainform-main-call-sites.md`, `mainform-hotkeys-call-sites.md`, `mainform-switch-call-sites.md`, `visual-guidance-call-sites.md`, `a380-forms-call-sites.md`, `a380-definition-call-sites.md`, `a380-panel-controls-call-sites.md`, `flypad-call-sites.md`, `md11-generator-call-sites.md`, `taxi-call-sites.md`, `taxi-manager-call-sites.md`, `rollout-lateral-call-sites.md`, `stands-call-sites.md`, `surroundings-catalog-call-sites.md`, `stand-naming-call-sites.md`, `stand-bridge-call-sites.md`, `landing-exits-call-sites.md`, `go-around-call-sites.md`, `runway-holds-call-sites.md`, `held-runway-call-sites.md`, `runway-shape-call-sites.md`, `sayintentions-gates-call-sites.md`, `briefing-stand-picker-call-sites.md`, `heading-unit-call-sites.md`, `settings-call-sites.md`, `base-definition-call-sites.md`, or a "Mirrored from …" block in an area file that already loads them). CLAUDE.md takes only rules that apply to ANY file in the repository (two area rules are mirrored here for that reason). To change a rule, edit its line, every mirror and its full text together. To retire one, delete its lines and rename its heading `## PREFIX-n (retired: <why>)` so the number is never taken again. `ClaudeContextBudgetTests` (CI) enforces the limits, fails when a file loads a rule twice (a mirror reaching a file that loads its original), when shipped code loads no rule file outside its listed exemptions (an aircraft's own folders need one of their own, and a Coherent agent script is never exempt), or when code loads none while its test does, when a rule loads on no file declaring code it names in backticks (unless `PassingMentions` lists the pair, by reason: code the rule's own code only calls, reads or avoids), or when CLAUDE.md gains a section or a doc link outside its map, and says what to do when one is hit. An older code comment that cites CLAUDE.md for a rule ("see the CLAUDE.md flyPad note") means text that now lives in `docs/invariants/` or elsewhere in `docs/`: search `docs/invariants/`, then `docs/`, for the rule's key name.

## Technology Stack

.NET 10 (C# 13), Windows Forms, SimConnect SDK (MSFS), SQLite, NVDA/Tolk (screen readers)
