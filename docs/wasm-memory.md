# WASM module memory (the PMDG 737 FMA, engine displays, navigation display and primary flight display)

> **Read this when:** reading an aircraft's WASM module memory from the simulator process: the
> PMDG 737 flight mode annunciator (FMA) reader, the engine display reader (the Engine displays
> window and its announcements), the navigation display reader (the Navigation display window,
> Output Ctrl+W and the ND announcements), the primary flight display reader (the Primary flight display
> window and the PFD announcements), the layout files they read memory by and the layout store that
> finds them, the shared module session they use, or the safety model. The layout files come from the
> layouts store, which a release carries beside the exe, or from the generator, an optional program beside
> the app that writes layout files.
> Rules: `.claude/rules/wasm-memory.md` (WASM-n), full text in
> [invariants/wasm-memory.md](invariants/wasm-memory.md).

## Why module memory

The FMA (the top of the PFD) is the only place the 737 says which autothrottle, roll and pitch
modes are active or armed, and whether the AFDS is in FD, CMD or SINGLE CH. A blind pilot had none
of it:

- The PMDG NG3 SDK sends only the MCP button lights (`MCP_annun*`). They cannot tell VNAV PTH from
  VNAV SPD, or armed from engaged, and the APP light stays lit from arming to capture, so localizer
  and glideslope capture gave no indication at all.
- PMDG's WASM module (`DU_LeftOutboard` / `DU_LeftInboard` in `737NG3.wasm`) draws the PFD
  natively, not as HTML, so the Coherent debugger has nothing to read.
- PMDG registers no FMA L:vars, and a WASM module of our own gets its own sealed linear memory.
- AI display reading (Output Alt+P) works, but slowly and only on demand.

So the app reads the FMA from PMDG's module memory in the running simulator, read-only (owner
decision, 2026-10-23). The app holds no PMDG address, offset or word: each comes from a layout file
for the installed `737NG3.wasm` ([the layout file](#the-layout-file)), and the PMDG SDK the app already
receives checks every reading. A release carries the layout files from the layouts store beside the exe;
when the generator, an optional program beside the app that writes layout files, is there too, the app
runs it for a PMDG build that has no layout file.

## What the pilot gets

- **Every FMA change, spoken.** Each part that changed, in PMDG's own printed words (verbatim from
  its word tables, never rewritten for speech), joined in PFD order once the change has settled for
  300 ms, after *"FMA: "*: *"FMA: Roll: VOR/LOC, Pitch: G/S, AFDS: SINGLE CH"*. A/T, roll, roll
  armed, pitch, pitch armed, AFDS. An armed mode that becomes active is spoken once, as the active
  mode, not also as *"Roll armed: none"*; a part going blank is *"none"*; a value with no word is
  *"unrecognised"* (*"FMA: Roll: unrecognised"*), never dropped, because a TO/GA go-around from LOC
  sets roll to such a value and silence would leave the pilot believing LOC. The prefix
  (`Pmdg737FmaSpeech.ChangePrefix`, owner 2026-10-23) tells an FMA line from the *"MCP: "* lines and
  other call-outs; `DescribeChange` stays bare, and the monitor adds it where it speaks a change
  (`ChangeAnnouncement`). The first good reading after `Start` or `Restart` is the baseline and is
  never spoken as a change (`Pmdg737FmaSpeech.DescribeChange`); a later return to Running in the
  same session speaks what changed meanwhile (see the status lines).
- **The FMA in the Primary flight display window** (Output Alt+P): its first rows are the FMA's
  columns (`Pmdg737FmaMonitor.ColumnTexts`), *"A/T: MCP SPD"*, *"Roll: HDG SEL, armed VOR/LOC"*.
  While the reader does not run, one row says why (`Pmdg737FmaMonitor.DescribeForReadout`):
  *"FMA reader waiting for PMDG SDK data"* in WaitingForSdk (PMDG's SDK broadcast off, or the
  simulator disconnected); *"FMA reader starting"* while analysing, locating and verifying; when
  unavailable, *"FMA announcements unavailable: <reason>"*. Once a stop line has been said (a
  mismatch stop, the 10 s stop after lost memory, or the search window running out), the row repeats
  that line in every state until the reader runs again, so the pilot never reads "starting" or
  "waiting" after "stopped". After a mismatch it adds the last good reading: *"FMA announcements
  stopped: values do not match the SDK. Last known: <the whole FMA>"* (`DescribeAll`), so a possibly
  stale reading never passes for the current one. Output P (`HotkeyAction.ReadFma`) read that line
  aloud until 2026-10-30, when the owner freed the key: the announcements and the window already give
  the FMA.
- **Status lines**: *"FMA announcements unavailable: <reason>"*, *"FMA announcements stopped:
  <reason>"* and *"FMA announcements resumed"*. A search that runs out says "stopped" if the reader
  has run in this session, else "unavailable"; a final reason (below) says "unavailable", except
  "unexpected values", which can only stop a running reader. A stop line is spoken once per reason
  until the reader runs again. "Resumed" carries whatever
  changed while the reader was not running: *"FMA announcements resumed, Roll: VOR/LOC, Pitch:
  G/S"* (no second "FMA: "); after a quick silent search the change is spoken alone, with its
  "FMA: ". With no layout file loaded (the module analysis has no builds) the reader says no line of
  its own: the four readers share one, *"PMDG display files could not be loaded; the display keys fall
  back to AI reading."*, once per module session ([WASM-29]); a file that loaded without the FMA's
  layout gives the FMA's own "unavailable" line. A stop or unavailable line
  said by the search or a stop survives `Restart` (a flight reload): the new flight's first reading is still a baseline, but it says *"FMA announcements
  resumed"* alone, so the line the pilot heard is answered (`_resumeOwed`). `Start` with another
  environment and `Stop` forget it; the readout and the once-per-reason rule stay per session, so
  after a reload the window's FMA row says "waiting" or "starting" again. The reasons are `FmaReasons`:
  "PMDG 737 files not found", "PMDG file not recognised", "aircraft memory not found", "no access
  to the simulator", "values do not match the SDK", "unexpected values", "internal error, see the
  log". "Aircraft memory not found" and "values do not match the SDK" are never final (the reader
  keeps searching); the others hold until `Restart` or `Start`.
- **One Ctrl+M row, "Flight Mode Annunciator"**: the synthetic key `MSFSBA_PMDG737_FMA`
  (`Pmdg737FmaMonitor.MuteKey`), a `PMDGVar` definition no struct field has, so nothing ever fires
  for it. The monitor speaks from its own timer, outside `ProcessSimVarUpdate`'s mute wrap, so it
  checks the row itself ([VAR-8]). The mute silences everything the monitor says, the status lines
  included; the PFD window still lists the FMA.
- **Speech paths.** Changes and status lines use the announcer's non-interrupting `Announce`, as
  the MCP-light announcements do, so neither cuts the other off. Nothing orders them: the FMA
  speaks about 300 ms after memory changes and an MCP light on the next SDK update, so the FMA often
  comes first. The MCP-light announcements are never suppressed
  ([WASM-5]); since the owner's smoke test (2026-10-23) they start with *"MCP: "*
  (*"MCP: Level Change: engaged"*), as does everything else BA says by itself about the MCP. The
  same test found the heading, speed, altitude and V/S windows reading a pressed toggle back 1.2 s
  later with an interrupting announce, which cut the FMA line off; those toggles now read back only
  a press that changed nothing, queued, 3 s later ([P737-7]). This deviates from the design spec, which asked for FMA speech "queued after" the
  MCP lights: it is built as "neither interrupts the other" (both non-interrupting, the FMA often
  first), since the FMA learns of a change before the SDK reports the MCP light and many FMA
  changes, a localizer or glideslope capture among them, have no MCP-light change to wait for. The
  PR asks the owner to accept this.

## The layers

The reading side lives in three places.

`MSFSBlindAssist.DisplayLayouts/` is a class library the app and the generator share. It references
nothing in the app:

| File | Role |
| --- | --- |
| `ModuleDescription.cs` | `ModuleDescription`: where a WASM module's file lives (a package name pattern, the folders inside the package, with `OtherPathsInPackage` for a package laid out another way, the file name), its id and its readers. Each `LayoutReader` gives a reader's id, log name, layout type, anchor, layout dump and failed parts. |
| `ModuleCatalog.cs`, `Pmdg737/Pmdg737Module.cs` | Every module the app knows. So far one: `pmdg737`, the `737NG3.wasm` of the `pmdg-aircraft-73[6-9]` packages, with the readers `fma`, `engineDisplays`, `navDisplay` and `pfd` in that order. |
| `LayoutFile.cs`, `LayoutFileFormat.cs`, `ReaderResult.cs` | The layout file: its records, and the writer and reader of its text ([WASM-21]). |
| `WasmFileImage.cs`, `LayoutFit.cs` | The fingerprint: a `.wasm` file's code hash, data segments and searches, and whether a layout file fits it ([the fingerprint](#the-fingerprint)). |
| `ModuleAnchor.cs` | The bytes a reader's memory is found by, and their wasm address. |
| `Pmdg737/` | The 737's layout records: `Pmdg737FmaLayout` with `FmaColumnWords`, `FmaDerivation` and `FmaReasons` (the spoken reasons every reader uses), the engine display, ND and PFD layouts, and `PfdExpr`. Also the log's layout dumps (`Pmdg737LayoutDescriptions`) and the PFD's part names (`Pmdg737PfdParts`). These keep the namespace `MSFSBlindAssist.Services.PMDG`. |

`MSFSBlindAssist/Services/WasmMemory/` holds the generic groundwork. `SimProcess.cs`, `ModuleMemory.cs`,
`WasmMemoryLocator.cs` and `IProcessMemorySource.cs` use no app types and do no logging:

| File | Role |
| --- | --- |
| `IProcessMemorySource.cs` | The read-only view of another process (`Regions`, `RegionAt`, `TryRead`) and `MemoryRegion`. Tests supply `FakeProcessMemory`. |
| `SimProcess.cs` | The Win32 implementation: opens the simulator with `PROCESS_QUERY_LIMITED_INFORMATION \| PROCESS_VM_READ` only ([WASM-1]), walks regions with `VirtualQueryEx`, reads with `ReadProcessMemory`. |
| `WasmMemoryLocator.cs` | `FindLinearBase`: the one committed, `MEM_PRIVATE`, readable allocation whose bytes at the anchor's address equal the anchor ([WASM-3]). |
| `ModuleMemory.cs` | Reads i32 and u8 at wasm addresses (`base + address`), each read bounds-checked by `IsInsideModule` ([WASM-4]); inside a `RememberRegions` scope (one ND reading) each region is asked about once ([WASM-15]). |
| `ModuleFiles.cs` | `FindInstalled`: every installed copy of a module's file, in the packages matching its pattern under Community or an Official folder, at any of the module's paths (`ModuleDescription.PathsInPackage`). The 737's MSFS 2024 package keeps the file under each airplane's `attachments\pmdg\Function_Interior_*\panel`, its MSFS 2020 package in each airplane's own `panel` folder (one copy each for the -800, BBJ2, BCF and BDSF, identical, so one build). |
| `LayoutStore.cs` | Each installed file's build, from its layout file, running the generator when there is none ([WASM-26]; [the layout store](#the-layout-store)). |
| `ILayoutGenerator.cs`, `ProcessLayoutGenerator.cs` | The generator as the store sees it, and `LayoutGenerator.exe` run as a child process with no window. |
| `KillOnCloseJob.cs` | The Windows job object every generator process joins, which ends it when the app ends ([WASM-30]). |
| `ModuleBuild.cs` | One build: each reader's layout or failure, from one layout file. |
| `ModuleSession.cs`, `ModuleHost.cs` | One load of the layouts and one memory search for every reader of a module, on one shared read-only handle ([WASM-11]); the host is the app side (the files, the simulator, background threads, the `<module id>-module` log, for the 737 `pmdg737-module`). |

`MSFSBlindAssist/Services/PMDG/` holds the 737's readers, beside the other PMDG code:

| File | Role |
| --- | --- |
| `ModuleFmaSampler.cs` | Reads one `FmaSample` (the six FMA values, the cross-check fields, `PointersAgree`) through a layout. |
| `Pmdg737FmaCrossCheck.cs` | The FMA's SDK cross-check ([WASM-6]). |
| `Pmdg737FmaSpeech.cs` | Word lookup and every FMA sentence. Pure. |
| `IFmaEnvironment.cs`, `Pmdg737FmaMonitor.cs` | The FMA state machine and everything it needs from outside, so it runs against fakes. |
| `Pmdg737FmaEnvironment.cs` | The FMA's app side: the PMDG SDK ([WASM-7]), the mute row, speech, the `pmdg737-fma` log, and the session's results as FMA layouts and samplers. |
| `Pmdg737EngineDisplaySample.cs`, `ModuleEngineDisplaySampler.cs` | One `EngineDisplaySample` (both engines, limits, alerts, tanks, pages, SYS page, options, `Initialised`) through a layout. |
| `Pmdg737EngineDisplayCrossCheck.cs` | The engine display gate's SDK and fingerprint checks ([WASM-10]). |
| `Pmdg737EngineDisplayPages.cs`, `Pmdg737EngineDisplayColors.cs`, `Pmdg737EngineDisplaySpeech.cs` | The window's rows, PMDG's colour tests, every sentence. Pure. |
| `IEngineDisplayEnvironment.cs`, `Pmdg737EngineDisplayMonitor.cs`, `Pmdg737EngineDisplayEnvironment.cs` | The engine display state machine (a copy of the FMA's), its outside world, and the app side (SDK tanks, three mute rows, speech, the `pmdg737-engine-displays` log). |
| `Pmdg737NavDisplaySample.cs`, `ModuleNavDisplaySampler.cs` | One raw `NavDisplaySample` through a layout, the route a bounded walk ([WASM-14]). |
| `Pmdg737NavDisplayCrossCheck.cs` | The ND gate's SDK and fingerprint checks, `DisplaysLit` ([WASM-13]). |
| `Pmdg737NavDisplayGeometry.cs`, `Pmdg737NavDisplayText.cs`, `Pmdg737NavDisplaySpeech.cs` | PMDG's projection and clips, the window's rows, every sentence. Pure. |
| `INavDisplayEnvironment.cs`, `Pmdg737NavDisplayMonitor.cs`, `Pmdg737NavDisplayEnvironment.cs` | The ND state machine (a copy of the engine display's), its outside world, and the app side (the SDK's EFIS selectors, one mute row, speech, the `pmdg737-nd` log). |
| `Pmdg737PfdSample.cs`, `ModulePfdSampler.cs` | One PFD reading through a layout: a light set every tick, the full set while the window is open or a setter needs it. |
| `Pmdg737PfdCrossCheck.cs` | The PFD gate's speed and altitude check against the simulator ([WASM-19]). |
| `Pmdg737PfdGeometry.cs`, `Pmdg737PfdText.cs`, `Pmdg737PfdSpeech.cs` | Bugs and bands on the visible slice, the window's rows, every sentence. Pure. |
| `IPfdEnvironment.cs`, `Pmdg737PfdMonitor.cs`, `Pmdg737PfdEnvironment.cs` | The PFD state machine (a copy of the ND's), its outside world, and the app side (the `pmdg737-pfd` log). |
| `Pmdg737PfdMinimums.cs`, `Pmdg737SpeedReference.cs` | The Captain's minimums knob and the speed reference knob, set from the PFD reading's values. |

Wiring: `PMDG737Definition` owns the session and the four monitors (`Fma`, `EngineDisplays`,
`NavDisplay`, `Pfd`: `Stop` and the session's disposal in `OnSwitchedAway`, `Invalidate` and `Restart` in
`OnSimContextReset`, Output Alt+E, Output Alt+N, Output Alt+P and Output Ctrl+W in `HandleHotkeyAction`);
`MainForm.Pmdg737Displays.cs`'s `StartPmdg737Displays` makes the layout store (`LayoutStore.DefaultRoot`,
`LayoutStore.BundledRoot`, with `ProcessLayoutGenerator.NextToApp()`, null when `LayoutGenerator.exe` is not beside the app) and a new
session (`PMDG737Definition.ResetModuleSession`), and starts all four at startup and after an aircraft switch
(a no-op on other aircraft). The windows are `Forms/PMDG737/PMDG737EngineDisplaysForm.cs`,
`PMDG737NavDisplayForm.cs` and `PMDG737PfdForm.cs`.

## The layout file

One file per PMDG build per module holds every reader's result for that build: a layout, or the reason
the generator could not derive one. The generator, an optional program beside the app, writes it; the app only reads it.

- **Where.** `%APPDATA%\MSFSBlindAssist\layouts\<module id>\<code hash>-<first 16 characters of the data
  hash>.json` (`LayoutStore.DefaultRoot`), and the same layout beside the exe in `layouts\<module id>\`
  (`LayoutStore.BundledRoot`), which a release carries and the store only reads ([WASM-31]). The `%APPDATA%`
  folder serves every profile, since a layout belongs to the PMDG build.
- **The text.** One JSON object, UTF-8 without a byte order mark (`LayoutFileFormat`): `formatVersion`
  (`LayoutFileFormat.Version`, 1), `module` (`pmdg737`), `generatorVersion`, `codeHash`, `dataRanges` (each
  an `address` and a `length`), `dataHash`, `searches` (each a base64 `pattern` and its `hits`), then
  `readers`, one entry per reader in the module's order, `{ "layout": … }` or `{ "failure": "…" }`. The ND's
  and PFD's optional parts keep their null-and-reason form inside the layout. The same layouts give
  byte-identical text on any PC: a fixed property order, culture-invariant numbers, two-space indents, `\n`
  line ends, no timestamp and no path. Each layout's source file is saved empty, and the store fills it in
  for the log ([WASM-21]).
- **Loading.** `LayoutFileFormat.TryRead` refuses text that is not JSON, another format version or module,
  a reader missing or extra, a reader holding both or neither of a layout and a failure, a layout record
  missing any member, and null where a record allows none, so a damaged file never loads as offsets of 0.
- **Size.** The file the installed 737-800 and 737-900 share is 1.8 MB.

### The fingerprint

A layout fits a `.wasm` file when the file still holds everything the generator looked at
(`LayoutFit.Fits`, on a `WasmFileImage` of the installed file):

- `codeHash`: the SHA-256 (lowercase hex) of the raw bytes of every section but the data section, in file
  order: the code, types, imports, functions, tables, elements, globals, exports and the custom sections,
  such as the function names.
- `dataRanges` and `dataHash`: the data the generator read, as wasm address and length, and the SHA-256 of
  those bytes in order. Bytes it did not read may differ.
- `searches`: each byte pattern the generator searched the whole data section for, with every address it
  was found at, which the installed file must give again exactly. The memory search in the simulator cannot
  be misled by a second copy (it checks an anchor at its fixed address in each allocation,
  `WasmMemoryLocator.FindLinearBase`), but without the searches a file could fit a build where a pattern
  the generator needed to find once now occurs more than once.

Two builds with the same code but different bytes where the generator read get a file each, named apart
by the data hash. Measured 2026-10-31: the installed 737-800 and 737-900 `737NG3.wasm` are both 19,290,681
bytes and differ in 17 bytes, one ID string in the data section that the generator does not read, so they
share one file. `WasmFileImage` parses the data segments itself and must read them exactly as the
generator does.

## The layout store

`LayoutStore` ([WASM-26]) is where the session gets each installed file's build. It runs on the session's
background thread, once per installed file:

1. It reads the `.wasm` file, works out its code hash, and lists `<code hash>-*.json` in the module's
   folder under `%APPDATA%` and in the bundled folder beside the exe, each in ordinal order. It reads
   nothing else there, not even the `.tmp` a killed generator leaves. A bundled folder that is missing
   has no files and logs nothing.
2. It uses the current file with the highest generator version, `%APPDATA%` first on a tie ([WASM-31]).
   Current means the file reads, `LayoutFileFormat.TryRead` accepts it, it fits the installed file, and its
   generator version is not older than the generator's, when there is a generator and it gave one. Each
   file skipped is logged with the reason; the "using" line says `bundled` for a file from the bundled
   folder and `saved` for one from `%APPDATA%`.
3. With no current file and `LayoutGenerator.exe` beside the app (`ProcessLayoutGenerator.NextToApp`), it
   runs the generator (given the `%APPDATA%` root: a bundled file older than the generator is passed over,
   and the new file wins) with no window, waits up to 60 s (a longer run is killed with everything it started),
   copies the generator's lines into the log and looks again. The generator's `--version` is asked once
   per app session, with 10 s to answer. Each generator process joins a kill-on-close job the app holds,
   so one still running when the app exits, crashes or is killed ends with it; a profile switch leaves a
   run to finish ([WASM-30]).
4. With no current file in either folder and no generator, the lookup fails with "no layout for this
   build and no generator": the file is not recognised, the readers are unavailable with "PMDG file not
   recognised", the pilot hears one line for all four, *"PMDG display files could not be loaded; the display
   keys fall back to AI reading."* ([WASM-29]), and Alt+E, Alt+N and Alt+P use the AI read.
5. A run that timed out, could not start, exited non-zero or wrote nothing that fits makes the readers
   unavailable for this load, and the log says why. The store writes and deletes nothing itself, so the
   next load (a flight reload runs a failed analysis again) tries again.

Every step goes to `pmdg737-module.log` with a `layouts: ` prefix: the code hash and the generator's
version, each file skipped and why, the generator's lines, and the file used, "saved", "bundled" or "generated",
with its generator version and data hash. A loaded layout gets the installed file as its source file, for the
layout dump that follows it in the log.

One known edge: if the pilot switches away from the 737 and back while a generator run is still going,
two stores can run the generator for one build at once; the run that loses may fail, its readers are then
unavailable for that load only, and the next load finds the file.

## The shared module session

`ModuleSession` ([WASM-11]) does the two slow jobs once for every reader of a module. It takes the
module's description, so the PMDG 777 can later use it with a reader list of its own:

- **`AnalyseAsync`** finds the installed files (`ModuleHost.FindInstalledFiles`: `MsfsPackagesLocator`,
  then `ModuleFiles.FindInstalled`) and gets each one's build from the layout store. A build is kept when
  any reader has a layout; two installed copies loaded from the same layout file are one build
  (`ModuleBuild.SameLayoutsAs`). Every file is logged to `pmdg737-module.log`: each reader's layout in full
  (`file analysis: …`) with each optional part that was not recognised, "same layout as", or why each
  reader, or the whole file, was not recognised. A reader whose result is a failure costs only that reader.
  It starts once per session and returns the same task afterwards; `Invalidate` forgets an analysis that
  failed. A good analysis belongs to the simulator it found its files for (MSFS 2020 and 2024 install the
  module apart): when a flight loads in another one with the app left running, `AnalysedForOtherSimulator`
  is true and MainForm starts the readers again on a new session (`RestartPmdg737DisplaysIfSimulatorChanged`,
  from `OnAircraftLoaded`), since each reader also keeps the layouts it derived.
- **`LocateAsync`** opens the simulator read-only and runs `FindLinearBase` once per distinct anchor
  (each build's anchors in the module's reader order, so a build whose FMA layout failed
  is still found by another). Every build found gets one `LocatedModule` on one shared handle whose
  reference count starts with the session's own lease. A search in flight is joined; one that found
  something is reused for 5 s from its start (`ReuseMs`), so the second reader's attempt does not
  search again; `Invalidate` (a flight reload or reconnect) forgets it.
- **Each reader's view**: `Pmdg737FmaEnvironment.ToFmaAnalysis` / `ToFmaCandidates`,
  `Pmdg737EngineDisplayEnvironment.ToEngineAnalysis` / `ToEngineCandidates` and
  `Pmdg737NavDisplayEnvironment.ToNavAnalysis` / `ToNavCandidates` and
  `Pmdg737PfdEnvironment.ToPfdAnalysis` / `ToPfdCandidates` take their own layouts from each build by
  reader id (`ModuleBuild.Layout`) and
  make one sampler per requested layout found, each holding its own lease (a handle closed meanwhile
  is "not found yet"). The handle closes after the last lease and the session have let go.

## How an FMA value reads

How a value reads (`Pmdg737FmaSpeech.Lookup`): 0 is blank; a value in the table is its word; 1 is
the value-one word when there is one; any other positive value up to 8 past the table end has no
word (spoken as "unrecognised", in a change and in the readout, and logged once per column and value;
never fatal, so a first CWS engagement cannot silence the reader); a negative value or one beyond
that is corrupt (never spoken; ten corrupt readings in a row stop the reader).

## The monitor at runtime

`Pmdg737FmaMonitor` is a state machine ticked every 100 ms by a WinForms timer on the UI thread. The
two slow jobs run in the shared session, on the host's below-normal background threads
(`ModuleHost.RunInBackground`); the tick only polls `Task.IsCompleted`, so the UI never waits
on them. The engine display monitor is a copy of this machine (see [the engine displays](#the-engine-displays)).

- **WaitingForSdk.** `ReadSdk` returns null while SimConnect is disconnected ([WASM-7]) or the PMDG
  data manager is missing or not ready. Silent. Whenever the SDK goes away the monitor drops the
  memory and comes back here, keeping the layouts.
- **Analysing.** The session's `AnalyseAsync` finds the installed files (`MsfsPackagesLocator`,
  `ModuleFiles.FindInstalled`) and gets each one's layouts from the layout store, which reads the file
  shared for read, write and delete and loads its layout file, or has the generator write one first
  ([the layout store](#the-layout-store)); the FMA keeps each distinct FMA layout once (the same build
  installed twice gives one). Every file is logged to `pmdg737-module.log`: its layouts in full, "same
  layout as", or why a reader's layout is missing. No FMA layout means unavailable at once. The layouts
  are kept across `Restart` and loaded again on `Start`, from the saved layout file.
- **Locating.** The session's `LocateAsync` opens the process (`SimulatorDetector.GetProcessName`,
  `SimProcess.TryOpen`) and runs `FindLinearBase` once per distinct anchor, joined or reused by the
  other reader ([the shared module session](#the-shared-module-session)). "No access" is final; a
  simulator not running, or no match, is "not found yet". Attempts are at least 5 s apart within a
  120 s search window, which remembers its last failure: "aircraft memory not found" (nothing
  found, or a candidate's read failed) or "values do not match the SDK" (a candidate disagreed).
  When the window runs out, that reason is spoken once, "unavailable" (or "stopped" if the reader
  had been running in this session), and the search goes on every 30 s (`SlowRetryMs`), silently,
  until it is found, `Stop` or `Restart`: a long MSFS 2024 load, or a reload whose SDK snapshot
  lags, can outlast the window. The PFD window's FMA row then repeats that line. Found later: "resumed".
- **Verifying.** Each candidate (one per layout whose anchor matched exactly one allocation; they
  share one read-only handle through reference-counted leases) has 2 s to give one good reading. A
  candidate whose read fails gives way to the next; with none left, the search starts again in the
  same window (a new window per attempt would search every 5 s forever and never say why). When
  one passes, the others are disposed, and its reading becomes the baseline.
- **Running.** Every tick re-reads both pointers (so a re-created object is followed), range-checks
  the object and reads it. Only a good reading counts ([WASM-6]): it alone updates what the PFD window
  lists and opens or advances the 300 ms settle. No reading agrees with the SDK for 2 s: Mismatched,
  "FMA announcements stopped: values do not match the SDK". Ten corrupt readings in a row: unavailable,
  "FMA announcements stopped: unexpected values". A failed read searches again, silently, in a new
  120 s window; if that search has not verified within 10 s (`LostStopMs`), the pilot hears "FMA
  announcements stopped: aircraft memory not found".
- **Back to Running.** The first good reading after `Start` or `Restart` is a baseline (a new
  flight's initial modes are not a change): silent, unless a stop line said before a `Restart` is
  still unanswered, which it answers with "FMA announcements resumed" alone. Any later one in the
  session is compared with what the pilot last heard: after a stop line, "FMA announcements resumed"
  followed by the change, if any; after a quick silent search, the change alone, after "FMA: ".
  What changed in a gap is never swallowed.
- **Mismatched.** Nothing is spoken while stopped; the next good reading resumes, as above.
- **Unavailable.** Final: "PMDG 737 files not found", "PMDG file not recognised", "no access to the
  simulator", "unexpected values" and "internal error, see the log". Spoken once on entry (with no layout
  file loaded, as the four readers' one line, once per module session: [WASM-29]); holds
  until `Restart` (a flight reload) or `Start`. After "stopped: unexpected values", a reload's first
  reading says "resumed"; the "unavailable" lines of a final reason are not answered.

The cross-check (`Pmdg737FmaCrossCheck.Passes`) compares memory with the SDK fields the app already
receives: the two FD switches against `MCP_FDSw_0` and `MCP_FDSw_1`, the MCP altitude against
`MCP_Altitude` and the MCP heading against `MCP_Heading` (0 and 360 equal). The SDK lags memory by about
a second, so each field must match at least one memory sample of the last 3 s, and different fields
may match different samples.

Lifecycle: `StartPmdg737Displays` at startup and after a switch to the 737 makes a new session and
starts both monitors (a monitor already running starts again); `OnSimContextReset` (flight reload,
reconnect) invalidates the session and restarts both; `OnSwitchedAway` (aircraft switch, app exit)
stops both and disposes the session, which releases the process handle once the last lease goes. A
search or analysis abandoned by `Stop` or `Restart` disposes whatever it returns when it completes.

## The engine displays

The PMDG 737's upper and lower center display units (the engine indications, the crew alerts, the SYS
page) are read from PMDG's engine display, EFIS, glass data, options and aircraft objects in the same
module memory, on the same session as the FMA. Built 2026-10-24 (spec and plan
`2026-10-23-pmdg-737-engine-displays`, local); live-checked on the -800 the same day.

### What the pilot gets

- **Output Alt+E opens "Engine displays"** (`PMDG737Definition.HandleHotkeyAction`,
  `PMDG737EngineDisplaysForm`): two live `DisplayListBox` lists, "Upper display" and "Lower display",
  each naming the page its unit shows ("Upper display: primary engine page", "secondary engine page",
  "compact engine page", "side by side engine page", "SYS page"; a page the reader does not read is
  named only: "navigation display", "PFD", "map", "blank", "off", a new value as "page 14"), then the
  page's rows in PMDG's draw order, labels and printed format (`Pmdg737EngineDisplayPages.Lines`), for
  example "N1: 21.0, 21.0", "EGT: 960 red, 466", "FF: 0.67, 0.67", "OIL QTY %: 80, 80", "Alerts: engine
  1 LOW OIL PRESSURE, engine 2 LOW OIL PRESSURE", "FUEL LB: 1: 8.63, CTR: 7.81, 2: 8.63, TOTAL: 25.1".
  Below them: MFD ENG, MFD SYS, the Lower DU Captain combo, Fuel Flow Used and Fuel Flow Reset, the N1
  SET combo, and the manual N1 field. Escape returns to the previous window. When the reader cannot work
  (unavailable, or stopped for 10 s), Alt+E says nothing of its own and goes straight to the AI read of
  the upper display ([WASM-29]); while it starts, or during a short stop, the window opens and both lists say "Engine displays starting" or "Engine displays
  stopped: <reason>, trying again", never a stale reading.
- **Announced by themselves, never interrupting ([WASM-9])**, each kind on its own Ctrl+M row:
  "Engine display alerts" ("Upper DU alert: engine 1 START VALVE OPEN", going: "Upper DU alert gone:
  engine 1 LOW OIL PRESSURE", "Upper DU alert: fuel CONFIG", "tank 1 fuel LOW", "engine 1 REV amber",
  "NO AUTOLAND", "A/T LIM", "ATC", the status lines), "Engine gauge colors" ("Upper DU: engine 1 EGT
  amber", "… red", "… normal", vibration "high") and "Thrust mode" ("Upper DU: thrust mode CLB")
  (`Pmdg737EngineDisplaySpeech.Changes`). The prefix names the display that draws the item now ("Lower
  DU" under ENG PRI, or for a secondary-page item). One settle window's items of one form share a line;
  appearing and going are separate lines. The SDK start-valve and overhead-light announcements are kept
  unchanged beside them ([WASM-9]).
- **Silence while the displays are dark ([WASM-10])**, and for 10 s after they light up: the first
  reading after Start or a flight reload, and the first after power-up, are silent baselines.
- **The MFD ENG and SYS buttons** (Instruments panel and window) read the lower display's page back
  1.5 s after a press ("Lower display: SYS page"; "MFD SYS pressed" when the reader is not running).
  **Fuel Flow Used and Reset** replace the spring-loaded switch's combo: a press and release each (USED a
  left click, RESET a right one, swept live), read back by the "FUEL USED: …" row PMDG then shows for
  10 s (2 s after RESET). The switch's position (RATE at rest) is no longer announced. See
  [pmdg-737.md](pmdg-737.md#engine-displays).

### The reader at runtime

`Pmdg737EngineDisplayMonitor` is a copy of the FMA monitor's state machine (unified when the 777
reader arrives): the same states, 100 ms tick, 300 ms settle, 5 s / 120 s / 30 s search, 2 s mismatch and
10-tick corrupt stops, 10 s lost-memory stop, and "resumed" followed by what changed. Its gate
([WASM-10]): the display units' fingerprint, the three tanks against the SDK's `FUEL_Qty*` within 1 lb in 3 s
(measured: memory and SDK agree to 0.02 lb), no corrupt value. Change detection compares item sets
(`Changes`), not samples. `View()` gives the window both units' `Lines` from the latest good reading, or
one status line; `KeyFallsBackToAi()` tells Alt+E when to fall back.

### Known differences from PMDG's displays

Recorded, not read (owner: "record unless cheap"): PMDG blanks the EGT digits, fuel flow and oil
readouts under their own valid flags and shows the N1
reference as "---.-" under its own conditions; the list prints the numbers, and shows the reference only
when N1 SET is not AUTO. PMDG's oil quantity LO appears after 35 s below 1.03 × the limit; the reader
says LO as soon as the quantity is below the limit. PMDG inhibits the EGT amber for the first minutes of
a takeoff under two options; the reader does not. "Upper DU: thrust mode ---" is spoken as written.

### Not yet seen live

OIL FILTER BYPASS, fuel IMBAL, REV (which flag is amber and which green), ENG FAIL, X-BLD START, amber
TAI, the exceedance latches, NO AUTOLAND / NO LAND 3, A/T LIM with the displays lit, ATC / COMM, any amber
or red colour, the side-by-side page, the SYS brake-temperature status, PMDG's KG and oil-unit options,
the thrust words in flight.

## The navigation display

The PMDG 737 Captain's navigation display (ND), read from the objects PMDG's ND drawing reads (its EFIS,
FMS, glass data, simulator interface, TCAS, GPWS, weather radar, IRS, options and systems objects), never
from the drawing object's own fields: one instance draws both sides and its fields are per draw. The third
reader on the shared session ([WASM-11]). Built 2026-10-25 (spec and plan
`2026-10-25-pmdg-737-navigation-display`, local); the sources were checked live on the -800 at KATL the
same day. MAP and PLN are read fully. APP and VOR were added on 2026-10-28 (spec and plan
`2026-10-28-pmdg-737-nd-app-vor`, local) as an optional part of the layout, and the VOR/ADF
bearing pointers with POS's lines and positions as a second one, neither seen live yet. The First
Officer's ND is not read (owner: dropped).

### What the pilot gets

- **Output Alt+N opens "Navigation display"** (`PMDG737Definition.HandleHotkeyAction`,
  `Forms/PMDG737/PMDG737NavDisplayForm.cs`): one live `DisplayListBox`, "Captain ND", then Mode, Range,
  the CTR button (normal, centred, VSD on: PMDG cycles it in that order), TFC, WXR, STA, WPT, ARPT, DATA,
  POS, TERR and VOR/ADF 1 and 2. The buttons show their state as the reading holds it ("STA: on"); NVDA
  reads a pressed button's new text, and a press that changed nothing is read back once, non-interrupting,
  3 s later ("STA off": never silent, the 737 MCP windows' rule [P737-7], owner 2026-10-30; before, every
  press was read back and doubled NVDA's line); the combos are spoken by the screen
  reader. When the reader cannot work (unavailable, or stopped for 10 s), Alt+N says nothing of its own and
  goes straight to the AI read of the ND ([WASM-29]); while it starts, or
  during a short stop, the window opens and its list says "Navigation display starting" or "Navigation
  display stopped: <reason>, trying again", never stale rows.
- **The rows** (`Pmdg737NavDisplayText.Lines`) follow the screen top to bottom, each starting with where
  it sits and carrying the ND's printed text, and exist only where PMDG's ND draws them at this mode,
  format and CTR: "Captain ND: MAP, range 20" (", centred", ", VSD on"; PLN ", centred on <the LEGS page's
  STEP waypoint>"; "Captain ND: not on any display"; "Captain ND: off"), "Top left: GS 0, TAS ---, wind
  ---°/---" (with the wind's arrow in words, ", from 88° left"), "Top centre: TRK 079 MAG" (in PLN "Top
  centre: N↑": PLN's compass letter there, its arrow's lines written as ↑), "Top right: EMPAD, 1533.3Z,
  6.8 NM" (PMDG's "------Z, --- NM" with no TO waypoint), "Left side: ARPT, STA" (with the WXR label, the
  radar's lines: "tilt +2A", the radar's tilt for the panel printed "%+d%c" or "0%c" with 'A' while its auto
  flag is set, else 'M', then "VAR" ("VAR/" on the half format) while another radar flag is clear; PMDG also gates
  them by a per-unit copy of WXR and the radar's two failure tests, which the reader does not read: it
  follows its WXR label, the panel's switch; with the TERR label, while PMDG's [GPWS] GPWS_TerrPeaksMode
  option is set and a GPWS test flag is clear, "highest 045 (amber), lowest 012 (green)": the two
  "%03d" prints of floor(feet × 0.01 + 0.5) from the ND's own terrain record for the unit, which PMDG keeps
  nowhere else, so the sampler reads it there, while TERR is on; the highest above the lowest, each while
  above −999999, the lowest only while the highest's colour code is not 0, colour codes 3 red, 2 amber, else
  green (the brushes' colours are loaded at run time: the words follow the codes' EGPWS bands); that block
  returns before the radar lines, so once it is entered no radar line is listed, numbers or not; in PLN then "Left side: W", and after the map rows "Right side: E" and,
  last before the messages, "Bottom centre: S": PLN's other compass letters, at the screen's edges level
  with the centre and at its bottom), then the map: "Map: half-range ring 10" (in PLN "Map: range rings 5
  and 10": the range label table at the range plus each ring's shift, inner ring first), "Map: track line,
  straight ahead, marks at 5, 10 and 15 NM" (every mode but PLN, right after the half-range ring: the line
  while the track is valid, turned by track − heading while heading-up with the readout valid, "4° right";
  its marks at 1/4, 1/2 and 3/4 of the NM the compass spans while the track is valid or WXR, TERR, TFC or
  the TCAS self-test is on, unless PMDG draws its range arcs instead (PMDG's [Displays] ND_RangeArcs, not
  PLN, not CTR, and one of the same four); "Centre:" in APP and VOR; centred there on the half format
  "Centre: track pointer, 4° right" alone, with the readout valid), each route waypoint on the map ("Map:
  EMPAD, 8.1 NM, 103° left", with DATA its constraint and ETA after a ";"), each hold right after its fix
  ("Map: hold at HRMBE, inbound 270°, right turns, legs 4.0 NM": a hold node, which PMDG draws as a holding
  pattern and never as a waypoint; listed while the node's position, the fix's, is on the map; the inbound
  course the hold record's true course less the node's variation, rounded, "%03d" as the HOLD page prints
  it; left turns while the record says 'L'; the HOLD page's LEG DIST ("% 4.1f" NM) when above −999999, else
  its LEG TIME ("%03.1f" minutes)), "Map: route goes off the left edge, toward PT753, 26.0 NM, 38° left"
  (where a piece of PMDG's own route line leaves the map, named by the waypoint that piece heads for; a
  discontinuity has no piece, so nothing is said across it), the VNAV markers (T/C, T/D, S/C, DECEL, E/D;
  like VDEV, only while the route's performance data has a VNAV path), the route's airports ("Map: airport
  KATL, runway …"; close up "Map: runway 08R, …; centreline 14.2 NM each way, running 5° right of the top,
  you are 1.2 NM left of it": PMDG's dashed centreline, 14.2 NM beyond each end of the outline, which runs
  from the runway's position along its course, turned by its position's variation, for its length; the
  direction folded within 90° of the top, as the line looks on the screen; the aircraft's side and distance
  from the line on the screen while it is within the line's extent, "on it" below 0.05 NM; the far tick not
  listed), a pending modification's items ("Map: modified route: …"), "Map: heading bug, 30° right", "Map:
  dashed line to the heading bug, 30° right" (the line to the compass whether the bug is on the screen or
  not; never centred in VOR or APP; with PMDG's [Displays] Glass_PFD_ND option, while the FMA roll mode is
  LNAV or VOR/LOC, only for 10 s on the sim's clock after the selected heading changed: a time the ND keeps
  itself, read there and only then), "Map: altitude arc, 11.6 NM ahead", "Map: trend vector, straight
  ahead, 6.3 NM in 90 seconds" or "…, curving right through 180° in 90 seconds, 2.0 NM radius" (in MAP above
  80 kt with the map valid: 1, 2 or 3 segments by the range index, each an 8 s gap then 22 s drawn, so the
  seconds are 30 × the segments; curving while the turn rate is at least 0.03 °/s, read at its source in
  radians a second, which PMDG smooths 5 % a frame: the smoothing is not modelled; listed whole), tuned VORs
  with their course, the CDU FIX page's rings and radials (and "; abeam line to the route at 8.2 NM, 10°
  left": the line from the fix to the entry's abeam point, the FIX page's fourth intersection, which its
  ABEAM key fills, drawn while valid; PMDG puts it in the route only if the pilot copies it from the
  scratchpad), navaids, fixes and airports nearest first ("Map: VOR/DME CAG, …", "Map: fix ABCDE, …", "Map:
  airport LPCS, …"; one the clip cuts with part of its symbol or name showing, "…, at the edge", as for
  route waypoints and markers (drawn while on the screen), tuned VORs (culled only while PMDG's course-line
  option is clear), traffic and PLN's aircraft, each by its own symbol and texts as PMDG places them), "Map:
  TCAS ring, 3 NM" (the ring of dots
  round the aircraft, with TFC or the self-test, not in the TCAS test, at range 80 and below; "Centre:" in APP
  and VOR), traffic ("Map: traffic advisory, 6.2 NM, 30° right, +12
  climbing"; in the TCAS test the four test symbols, "Map: other traffic, 3.6 NM, 34° left, +10"); "Right side: VDEV 250, above the path" (the side the pointer shows: drawn up the scale, below
  the path; the way it moves read from the pointer's shape; "VDEV, on the path" where PMDG prints no
  number; none while the FMC's VTK flag is set: then "Right side: VTK (amber, boxed)", the text PMDG draws
  under that flag, taken out of the messages as the scale's own flag, boxed where PMDG draws a rectangle,
  spoken as a message; under the bottom RNP's option the row ends ", vertical RNP 400, ANP 154": the scale's
  two "%d" prints of the CDU position data's vertical RNP and ANP, "---" and "-.--" at or below -999999,
  their colour not read), "Below the aircraft: XTK 9.7 R" (the number PMDG prints under the aircraft with
  the RNP option, in MAP, with the reference route active: the TO plan's cross-track error, none while the
  FMS position is invalid or the TO leg is not one PMDG prints it for; a conditional key node's value PMDG
  computes, not read; its formats, zeros and side letters PMDG's; "XTK" is the app's word), "Bottom: RNP
  2.00, ANP 0.05", "Bottom left: FMC L" (MAP and PLN while the map is valid: PMDG's label for the FMC the
  map shows), "Bottom left: VOR 1, CAG, DME 23.4"
  (a failed receiver "VOR 1 (amber, boxed)": its label in amber with the rectangle PMDG's failed arm
  draws, and no value), then "Message: TCAS OFF" per message or "Messages: none". A map item's place is its straight-line
  distance and whole degrees off the top of the map (the track; the heading with the IRS degraded or
  PMDG's track-up option clear); in
  PLN, the distance and compass point from the centre waypoint ("5.0 NM north-west of EMPAD").
- **APP and VOR** (`Pmdg737NavDisplayText.Approach.cs`) are heading-up ("Top centre: HDG 268 MAG"); they
  keep the header, the top left, the bottom corners and the messages, and draw no route, map options or
  map items. After the top centre:
  "Top right: ILS 1, IATL, CRS 268, DME 12.3" (the ILS/VOR block joined by ", ": the
  label "ILS 1" / "ILS 2" in APP, "FMC L" while an FMC source drives it, "VOR 1" / "VOR 2" in VOR; the
  ident in APP only for an FMC source or an ILS frequency, in VOR only for a frequency that is not ILS,
  the station's frequency text while its ident is empty, else PMDG's "------"; "CRS" and the course
  truncated, full format only; "DME" and "%4.1f" below 100, else whole miles, " ---" below 0, or a
  failed DME's amber label "DME 1 (amber, boxed)"), "Centre: course pointer 268, ahead" (the side's
  course, and where its head points off the top of the screen; only while the readout is valid),
  "Centre: deviation bar 0.6 dots left, filled" ("centred" at 0.0 after rounding, "at full scale, left" where
  PMDG clamps it; no row while the receiver's state is below PMDG's or the VOR has failed without an FMC
  source; "filled" in APP while |deviation| < 1.18 units, 2.36 dots, else "outlined"), "Centre: heading bug, 30°
  right" (drawn in every mode but PLN; on the
  compass of MAP's picture with the same CTR; while the readout is valid), in APP "Right side: glideslope pointer 1.2 dots below" (plus is down the screen; "glideslope
  scale, no pointer" while the receiver's state is not the pointer's; "G/S (amber, boxed)" while failed
  without an FMC source, also spoken as "ND message: G/S" but listed only there), in VOR "Centre: TO" /
  "Centre: FROM" (none while the VOR has failed), traffic as in expanded MAP with "Centre:" while not
  centred, then the corners. The messages gain "VOR 1" (VOR mode, the VOR failed) and "LOC 1" (APP, the
  localizer failed), "VOR" / "LOC" on the half format, and "EFIS MODE/NAV FREQ DISAGREE" (APP with a
  frequency that is not ILS, VOR with one that is), spoken like the others. Without the part,
  one row in their place, after the top centre: "ILS data: not read: PMDG file not recognised" (APP) or "VOR data: not
  read: …" (VOR).
- **The bearing pointers and POS** (`Pmdg737NavDisplayText.Pointers.cs`), after the heading bug: per
  receiver the pointer its VOR/ADF switch selects while PMDG draws it, "Map: VOR 1 pointer, 34° left" in
  MAP and "Centre: ADF 2 pointer, 20° right" in APP and VOR (none in PLN), by where its head points (the
  bearing from the nose, turned by heading − up: the drift on a track-up MAP); with POS on in MAP, "Map:
  VOR 1 line toward CAS, R-090, 30° right" per tuned VOR while the readout is valid (PMDG's radial label,
  "V1" / "V2" on the half format; by the station's place on the map), "Map: ADF 2 line, A2, 20° right"
  per ADF received, "Map: IRS position, 0.2 NM, 30° left" and "Map: GPS position, 0.1 NM, ahead" where the
  map shows them (the app's words: PMDG draws a star and a circle). The VOR and ADF lines follow
  PMDG's tuned and ADF flags, not the switches. Without the part, one row, "Pointers and
  POS: not read: PMDG file not recognised", while a switch is on VOR or ADF (not PLN) or POS is on in MAP.
- **Output Ctrl+W** says the top-right corner as printed ("EMPAD, 1533.3Z, 6.8 NM"), "No active waypoint",
  or "ND waypoint unavailable: <reason>", interrupting, from the FMC whether or not a display shows the ND.
- **Announced by themselves, never interrupting ([WASM-12])**, under one Ctrl+M row, "ND messages"
  (`Pmdg737NavDisplayMonitor.MuteKey`): "ND message: EXCESS DATA", "ND message gone: TCAS OFF" (one line each
  way, items joined, the prefix once), "ND: PT753, 23.0 NM" when the active waypoint changes, "ND: no
  active waypoint", and the status lines ("ND announcements unavailable / stopped: <reason>", "ND
  announcements resumed"). Messages are compared only while a display draws the Captain's ND.

### The reader at runtime

`Pmdg737NavDisplayMonitor` is a copy of the engine display monitor's state machine (not shared), ticked
every 500 ms: the same states, a 500 ms settle, 5 s / 120 s / 30 s search, 2 s mismatch and 10-reading
corrupt stops, 10 s lost-memory stop, "resumed" followed by what changed, 10 s of quiet after the displays
light up. Its gate ([WASM-13]): the display units' fingerprint, the Captain's EFIS mode and range against
the SDK, and no `corrupt` from `ModuleNavDisplaySampler`, whose walks are all-or-nothing ([WASM-14]). The
APP/VOR fields are read only in APP or VOR with the part derived, each checked where PMDG prints it: a NAV
index past PMDG's receivers, an ident record outside the glass data object, an unprintable ident, a course that is
not a number, a DME that is not one while not failed, or a deviation that is not one while PMDG draws its
bar or pointer makes the reading corrupt. The pointers' fields are read outside PLN with the part derived,
a pointer's bearing only while PMDG draws it; POS only in MAP with POS on: a bearing or radial that is not
a number while drawn, or a GPS pointer outside the module, is corrupt. The sample is raw (every gate's
inputs); `Pmdg737NavDisplayGeometry` (projection, clips, edges, compass
words) and `Pmdg737NavDisplayText` (rows) do the formatting and gating, `Pmdg737NavDisplaySpeech` the
sentences. `View()` gives the window its rows or one status line, `KeyFallsBackToAi()` tells Alt+N when
to fall back, `WaypointLine()` answers Ctrl+W. Logs: `pmdg737-nd.log`.

### Decisions that differ from the spec's examples

Recorded in the build's ledger, each a ruling the in-sim test checks:

- **The expanded map shows more than the area ahead.** PMDG's clip is an arc over the top joined to a line
  below the screen, so everything under the arc down to the screen's bottom is on the map, beside and
  behind the aircraft too: the spike's EMPAD (8.1 NM, 103° left of the track) is on the map at range 20,
  not off its left edge; at range 20 the map ends 18.1 NM ahead (635 of the 700 units), not at the ring.
- **Degrees include the magnetic variation** (PMDG turns each true bearing magnetic, or the up direction
  true): EMPAD is "103° left", not the spike's 104°.
- **On the ground the wind prints "---°/---"** (PMDG blanks it until the TAS passes 101 kt).
- **DATA's ETA is "HHMMZ"** (PMDG's DATA text prints no tenths).
- **EXCESS DATA follows PMDG's count of drawn items** (more than 40 drawn on the map, then one more in the
  list), not the lists' length.
- **The heading bug is never parked**: beyond the screen it has no row.
- **"TFC" follows PMDG's code**: shown while TCAS is normal and its gate holds: the TFC button on, and MAP
  (centred or not) or VOR/APP not centred; TCAS FAIL needs the same gate. Until 2026-10-29 the layout held
  only the gate's "not centred", and the flight showed "TFC" with the button off (finding T1).
- **APP and VOR are heading-up**, and so is MAP while PMDG's track-up option is clear: "Top centre: HDG
  nnn MAG", and every direction measured off the heading. The first layout said APP and VOR were
  track-up, a part of the flag's gate missing from it (the Stage 1 review found it, 2026-10-28). The live
  MAP at KATL printed TRK, so the owner's option is set.
- **The block's CRS is truncated**, as PMDG truncates it (268.7 prints "CRS 268").
- **The glideslope scale drawn with no pointer has a row** ("glideslope scale, no pointer"); the spec named
  only the pointer and the flag.
- **The G/S flag is listed at its place** ("Right side: G/S (amber, boxed)") and spoken as an ND message,
  with no second "Message:" row; a failed DME in the block is not spoken, like the failed corners.
- **TO/FROM is its own row**, "Centre: TO": PMDG prints it outside the block, below and right of the
  aircraft symbol.
- **Not listed:** the bar's filled or outlined look and centred VOR's TO/FROM triangle on the pointer.
- **A tuned VOR is on the map only while its receiver has power** (part of the tuned flag's gate, Stage 2
  Task 7b); the first build listed it on the state alone.
- **POS shows one IRS position**, the side's IRU, as PMDG draws it (the plan expected IRS L and R).
- **A VOR line's direction is the station's place on the map**; PMDG draws it toward a screen point it
  stores for that station (not read).

### MSFS 2020

The MSFS 2020 `737NG3.wasm` is an older build of PMDG's code, with its own layout file. Two things differ for
the ND there:

- **POS prints the VOR radial uncorrected.** PMDG's 2020 code copies each receiver's radial as the simulator
  gives it, without the station's variation correction the 2024 code applies. Its layout says so with a null
  `NdPosFields.StationCorrection`, and `ModuleNavDisplaySampler` then leaves the radial as it is. A 2024
  layout always has the correction, so its reading is unchanged.
- **The VDEV row also waits for the perf info's path byte**, which PMDG's 2020 VDEV doesn't test (its phase test
  is the same as 2024's, made in `updateVars`). Where they differ, the row is missing, never wrong.

The 2020 layout file holds `"stationCorrection": null`, which an app without the nullable field refuses. So the
public change has to be in public main before that file goes on the layouts branch, or the next preview build
stops at bundling the display layouts.

### Seen live, and not yet

Checked on the ground at KATL on the -800 (2026-10-25), every row against a capture of the real ND: the
route against the CDU's LEGS page; the route's airports and runway names ("08R", "18L"); a waypoint
behind the aircraft on the expanded map; STA listing only what PMDG draws (no ILS, NDB or TACAN); WPT at
range 40 with 41 fix rows and EXCESS DATA, spoken; DATA's "HHMMZ"; a pending modification with no DATA
text; PLN at range 640 with its rings at half the range; the centred ring label; the ADF corners. NVDA
does not re-read a focused row that changes, which the owner wants for a display list.

Checked in one flight on the -800 (2026-10-28/29, KATL to KCVG, the ILS 18C hand-flown), against
captures of the real ND: the top centre's TRK and HDG; the heading bug, off the arc too; a DME-only
station (no symbol, no pointer); VOR mode's block, course pointer and TO/FROM, with no bar for a VOR in
state 1; a VOR pointer whose head is behind (its tail shown); POS's VOR line with its radial, and the IRS
and GPS positions; PLN on the aircraft and on a waypoint, with N, E, S and W; APP's block, the deviation
bar left, right, centred and at full scale both sides, and the glideslope (the scale without a pointer,
above, below, centred, at full scale); EFIS MODE/NAV FREQ DISAGREE, spoken; the CTR cycle; the TCAS
self-test with TRAFFIC; TCAS OFF and TA ONLY, set by hand and by PMDG; the waypoint sentences; Ctrl+W;
the route's edge rows; DATA's times; T/C and T/D; the altitude arc; VDEV's number on, below and above the
path; WPT, STA and ARPT objects with EXCESS DATA; a traffic row on the expanded map.

The flight found what the list then had wrong or missing, fixed since (each against the flight's captures
and PMDG's code, not yet in the sim): TCAS FAIL, TFC and OFFSCALE need the TFC button (the gate read as
"not centred" alone); traffic on a centred MAP; VDEV's side of the path; the active waypoint's constraint
with DATA off; the aircraft's place in PLN centred on a waypoint; FMC L or R at the bottom left; XTK under
the aircraft; the TCAS test's four symbols; the ARPT circle of a route airport whose runway is set; a
navaid, fix or airport the map's edge cuts ("at the edge"), with EXCESS DATA counted on PMDG's cull, not the
clip. Two more came from the code while fixing those: the map is invalid without an FMS position too
(PMDG's map-invalid flag, not reached in flight), and the FMC's VTK flag hides VDEV and shows "VTK", boxed, in the
scale's place.

Not yet seen: the ADF pointer and POS's ADF line (the app cannot tune the 737's ADF); MAP with PMDG's
track-up option clear; a traffic symbol against a capture, OFFSCALE, and TRAFFIC from real traffic; a
discontinuity's gap and a turn's arc values against a capture (turns were seen drawn, not measured); a
node drawn only as the TO waypoint (`UndrawnUnlessTo` set); the TO-start case (key node 2 conditional); the ETA's tenths capped at 9; the G/S
and LOC flags; VTK; FMC R; the map-invalid flag's FMS half; a tuned VOR hidden with its receiver
unpowered; the inboard map pages (9, 11; the list reads the full format); and the VSD's content, which
the list does not read.

The drawn-but-not-listed build (2026-10-29) was checked against PMDG's code, and the owner said no sim test is
required for it. The flight's captures already showed three of its symbols: the TCAS ring at 3 NM (78 px against 128 px
for the 5 NM arc at range 20), the dashed line to the heading bug on the ground, and the range arcs that replace the
track line's marks with TFC on. Not yet seen in the sim: PLN's ring labels, the WXR tilt and VAR, the vertical RNP and
ANP, the deviation bar filled or outlined, the trend vector, the track line and its marks, the terrain numbers, the
runway's extended centreline, the FIX page's abeam line, and holds (a hold node no longer listed as a second waypoint).

## The primary flight display

The Captain's glass PFD, the fourth reader on the module session (2026-10-29/30; spec and plan in
`docs/superpowers/`, local). The window, Alt+P, the controls and the Ctrl+M row are in
[pmdg-737.md](pmdg-737.md#primary-flight-display); the pilot's description, every row's wording, is the
guide's "The Primary flight display window".

### What the pilot gets

- **The window** (Output Alt+P): one list in screen order, each row starting with its part
  (`PfdParts`: "Above the speed tape:", "Speed tape:", "Below the speed tape:", "Attitude:",
  "Above the altitude tape:", "Altitude tape:", "Below the altitude tape:", "Vertical speed:", "Heading:",
  "Message:"), refreshed every second in place; one-row states for off, not on any display, the classic
  EFIS look, starting and stopped. The FMA rows come first with no part name ("A/T: N1", "AUTOPILOT":
  the columns name themselves, owner 2026-10-30; "FMA:" only before a reader state that does not say
  FMA). They are the FMA reader's columns
  (`Pmdg737FmaMonitor.ColumnTexts`), the roll column's armed part replaced by what PMDG draws there
  (it shows LNAV armed, which the autopilot's armed roll never holds), and the strip's extra literals
  (AUTOPILOT, CWS P, CWS R) as rows.
- **Messages spoken** by themselves ("PFD message: ALT DISAGREE", "PFD message gone: …"), a flag
  that replaces an instrument included (SPD, ALT, ATT, VERT, HDG, F/D, FPV, RA, LOC, G/S, DME), WINDSHEAR
  and PULL UP never ([WASM-18]); under the Ctrl+M row "PFD messages".

### Optional parts

An optional part missing from the layout (approach, markers, rising runway, navigation performance
scales, TCAS zones, angle of attack, pitch limit indicator) costs only its rows, which say "<part>, not
read".

### The reader at runtime

`Pmdg737PfdMonitor` is a copy of the ND monitor's state machine, ticked every 500 ms, waiting first for
the simulator's `AIRSPEED INDICATED` (the hidden cached var `MSFSBA_PMDG737_PFD_CHECK_IAS`) and
`INDICATED ALTITUDE`. `ModulePfdSampler` reads a light set (the units, the panel, IAS, altitude and every
message's gate) every tick and the full set (every layout expression, the flap speeds, the ident text)
while the window is open or a setter needs it (`WindowOpen`, `KeepFull`); objects are found again from
their globals each reading and read in 4 KB blocks. The gate is the speed and altitude check
([WASM-19]), measured at ground session 2: settled, the altitudes differ by at most 0.6 ft (the
simulator's follows PMDG's baro); a baro turn opens up to about 250 ft for a second, absorbed by the 3 s
window; cold and dark, PMDG's values follow too. `Pmdg737PfdGeometry` places bugs and bands on the
visible slice; `Pmdg737PfdText` builds the rows; `Pmdg737PfdSpeech` the sentences. `View()` gives the
window its rows or one line, `KeyFallsBackToAi(out sayFirst)` tells Alt+P when to fall back (with the
classic look, the line to say first), `Latest` feeds the
buttons and setters. The Captain's minimums setter reads its value from here ([P737-8],
`Pmdg737PfdMinimums`). Logs: `pmdg737-pfd.log`, with a `check:` line every 5 s and on every reading half a
tolerance off (the instrument the tolerances were measured with).

### Seen live, and not yet

Ground session 1 (2026-10-30, KATL, -800): 21 cockpit states against captures of the real PFD, every field
matching: unaligned and aligned IRS, FPV, MTRS, the FD, minimums BARO and RADIO, IN, HPA and STD with its
preselect, the speed reference WT, the FMC's V-speeds and landing altitude, TO/GA's pitch bar, and in a
climb the trend arrow, Mach, the V/S number, the FPV and the bank's sign (positive left). Ground session 2
(2026-10-30): cold and dark, battery, ground power; the window's every control; MINS RST's toggle; the
knob sweeps; INSTR SWITCH; INVALID ENTRY. The flight (2026-10-30, KATL, -800, ended on the ILS before
the flap settings): the takeoff roll, the climb, a climbing turn (the FD roll bar's side, heading and
track), STD, Mach, cruise at FL330, the descent, both navigation performance scales, "DME, no distance",
the speed bands at cruise and the IAS tolerance (21 kt from its check lines, no stop line all flight);
the aircraft's ground contact on the approach froze PMDG's PFD with the ILS rows drawn (the glideslope
pointer at the top edge with the aircraft far below the path, the localizer and the rising runway on the
same side, PULL UP, the minimums bug). Not yet seen live: the flap bugs and bands at each setting, a
stabilised ILS to touchdown (markers, the minimums bug sliding on, the landing bars), the landing roll's
IAS difference, the slip's side, the TCAS zones, and the classic look.

## Safety model

**The simulator.**

- The handle has read rights only, so Windows refuses any write, even from a bug ([WASM-1]). No
  debugger, code injection, thread suspension or hooks.
- The search reads the 96-byte anchor at each distinct committed, `MEM_PRIVATE`, readable allocation's
  base, once per attempt, after checking that the range lies in committed readable memory of that
  same allocation. `MEM_IMAGE` is never searched: the compiled module DLL
  (`%APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2024\pmdg-aircraft-738\m*.dll`) embeds a copy
  of the data segment that must never match ([WASM-3]). There is no full-memory scan in the app.
- Once found, it reads only inside PMDG's allocation: a few dozen bytes, ten times a second. Every
  read is bounds-checked against the allocation as it is now, re-queried each time, because the
  object is heap memory PMDG commits after start ([WASM-4]).
- A read of freed memory fails cleanly (`ReadProcessMemory` returns false): "lost", and the app
  searches again.
- PMDG's files are opened read-only, shared so PMDG's own writes never fail; nothing is written to
  the simulator's or PMDG's folders.

**The generator.** `LayoutGenerator.exe` is a separate process, which the layout store starts with no
window when a build has no layout file. It reads only the `.wasm` file it is given and writes only under
the layouts folder it is given, and it never opens the simulator or any other process. It writes each
file as a `.tmp` and renames it over the final name, so a crash, or the store killing it at 60 s, never
leaves half a file. It does not outlive the app: Windows ends it when the app's
handle to its job closes, at the app's exit, crash or kill; if Windows refuses the job, debug.log says so
once and the generator runs as before, without one ([WASM-30]). The app itself only reads layout files.

**Never a wrong mode.** Strict derivation in the generator and no number in the app ([WASM-2]), a
layout used only when its file fits the installed `.wasm` ([WASM-26]), table content checks,
the one gate on every reading ([WASM-6]), values with no word spoken as "unrecognised" (never as a guessed
word), and corrupt values never spoken and stopping the reader. It runs only for the PMDG 737
profile, against 737 files. The 777 does not match the anchor: the locator compares all 96 bytes at the
737 file's own anchor address, and the 777, a separately built module, keeps its data elsewhere.

**The app.** Every exception in a tick is caught, logged and ends in "internal error, see the log";
`Restart` recovers. Logs go through `Log.Channel`, in `%APPDATA%\MSFSBlindAssist\logs` ([CORE-14],
[CORE-15]): `pmdg737-module.log` has the layout store's steps with the generator's lines (`layouts:`),
each file's layouts (`file analysis:`) and the memory search (`memory search:`); `pmdg737-fma.log` the FMA's state changes with their reasons and each value
with no word; `pmdg737-engine-displays.log` the engine display reader's state changes, its first
corrupt value per stop, and each page value outside the derived map; `pmdg737-nd.log` the navigation
display reader's state changes and the first corrupt check per stop; `pmdg737-pfd.log` the primary flight
display reader's state changes, its first corrupt check per stop and the speed and altitude check's
`check:` lines.

## Known gaps and open checks

- **Not yet confirmed live:** that the two FD fields are the FD switches, not FD-active flags (both read 1
  with both switches on in the spike; confirm by toggling one); value 1 as CWS P / CWS R; ROLLOUT
  armed on an autoland. Roll value 2 (TO/GA in the setters) and status value 1 have no word, so they
  are spoken as "unrecognised" and logged.
- **After a flight reload** the PMDG data manager keeps `IsReady` on a stale snapshot, so the search
  can start during the loading screen. The 5 s retries over 120 s absorb a short load; a longer one
  says "unavailable" (or "stopped") once, and the 30 s retries then find it and say "resumed". The
  owner's smoke test (2026-10-23) measured a reload recovering silently in 18 s, after a stale-SDK
  mismatch was retried. A stop heard before the reload is answered by "resumed" when the new
  flight's FMA is read (see the status lines).
- **A candidate that agrees with the SDK but carries corrupt values** for its 2 s counts as
  "values do not match the SDK"; the log has the real cause.
- **By design, untested:** the MS Store sim, FS2020, a simulator running as administrator, and
  security software that watches cross-process reads; each would report unavailable with a reason.

## After a PMDG update

A PMDG update that changes the code of `737NG3.wasm`, or the data the generator read, leaves no layout
file the app has that fits it ([the fingerprint](#the-fingerprint)). Without the generator, the four readers are unavailable with "PMDG
file not recognised", the pilot hears the one line of [WASM-29], and Output Alt+E, Alt+N and Alt+P use the
AI read, until a release or preview build carries the new build's layout file from the layouts store. With
the generator beside the app, the store runs it on the first load of the new build and uses the file it
writes. The log (`pmdg737-module.log`) shows which happened: the code hash, each file skipped and why, and
the file used.

## Tests

The app's tests never build a `.wasm` file with PMDG's shapes. The reading tests load the six golden
layout files in `tests/MSFSBlindAssist.Tests/Layouts/pmdg737/` (`GoldenLayouts`), which the generator wrote
from synthetic test modules, never by hand or from PMDG's real file.

- The layout file and store: `WasmLayoutFileTests` (every golden loads and saves back to its own text, also
  under sv-SE and de-DE; an unknown format version and damaged text are refused), `WasmLayoutStoreTests` (on
  `TinyWasmFile`, a minimal hand-built `.wasm`, with a fake generator: a current file, a missing, damaged or
  misfitting one, an older generator version, a timeout, a non-zero exit, two variants sharing code),
  `WasmProcessLayoutGeneratorTests`, `WasmKillOnCloseJobTests` and `WasmModuleDescriptionTests`.
- The memory and the session: `WasmMemoryLocatorTests` (with `FakeProcessMemory`), `WasmModuleFilesTests`
  and `WasmModuleSessionTests`.
- The FMA: `Pmdg737FmaSpeechTests` (one under a comma-decimal culture), `ModuleFmaSamplerTests`,
  `Pmdg737FmaCrossCheckTests`, `Pmdg737FmaMonitorTests` and `Pmdg737FmaEnvironmentTests`.
- The engine displays: `ModuleEngineDisplaySamplerTests`, `Pmdg737EngineDisplayCrossCheckTests`,
  `Pmdg737EngineDisplayPagesTests`, `Pmdg737EngineDisplayColorsTests`, `Pmdg737EngineDisplaySpeechTests`
  (with de-DE and sv-SE), `Pmdg737EngineDisplayMonitorTests`, `Pmdg737EngineDisplayEnvironmentTests`,
  `Pmdg737EngineDisplaysFormTests` and `Pmdg737EngineDisplaysWiringTests`.
- The navigation display: `Pmdg737NavDisplayApproachTests` (the APP/VOR part: the sampler's reads, the rows
  and messages, de-DE and sv-SE), `Pmdg737NavDisplayPointerTests` (the pointer and POS part likewise),
  `Pmdg737NavDisplayDetailTests`, `ModuleNavDisplaySamplerTests` (on `NdTestMemory`, the live reading of
  2026-10-25 placed through the layout), `Pmdg737NavDisplayCrossCheckTests`, `Pmdg737NavDisplayGeometryTests`,
  `Pmdg737NavDisplayTextTests` (with de-DE and sv-SE), `Pmdg737NavDisplaySpeechTests`,
  `Pmdg737NavDisplayMonitorTests`, `Pmdg737NavDisplayEnvironmentTests`, `Pmdg737NavDisplayFormTests` and
  `Pmdg737NavDisplayWiringTests`.
- The primary flight display: `Pmdg737PfdExprTests`, `Pmdg737PfdOptionalTests`, `Pmdg737PfdSamplerTests` (on
  `Pmdg737PfdTestMemory`), `Pmdg737PfdCrossCheckTests`, `Pmdg737PfdGeometryTests`, `Pmdg737PfdTextTests`
  (with de-DE), `Pmdg737PfdSpeechTests`, `Pmdg737PfdMonitorTests`, `Pmdg737PfdEnvironmentTests`,
  `Pmdg737PfdFormTests`, `Pmdg737SpeedReferenceTests`, `Pmdg737PfdMinimumsTests` and `Pmdg737PfdWiringTests`.

Everything that touches the simulator is checked by the in-sim test plan in the PR.
