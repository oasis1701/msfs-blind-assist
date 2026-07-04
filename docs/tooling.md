# Developer Tooling Guide — Coherent debugger, probes, and crash diagnosis

A detailed reference to every dev/debug tool in `tools/`, written for **agents (Claude/Codex) and human contributors**. It explains the one transport almost all of them share (the MSFS Coherent GT remote debugger), which tool to reach for, how to run it, and how to diagnose the app's crashes.

> **Read this when:** you need to read/write a live FlyByWire L:var, scrape or drive a cockpit display / MCDU / flyPad, verify whether a control's write "sticks", reproduce a `tools/_probe` finding, **adapt a scraper to a different aircraft** (§9), or investigate a crash (§8). For the *methodology* of proving a control works (write-stick rules, the write-mechanism decision tree), this guide points you at the **VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK** in `CLAUDE.md` — that is the source of truth; this guide is the tool catalogue.

---

## Prerequisites (what you need installed to run these tools)

These tools assume **MSFS is running with a Coherent aircraft loaded** (the sim opens the debugger port `:19999` itself — no Developer Mode needed). Host-side dependencies:

| Dependency | Install | Needed for |
|---|---|---|
| **PowerShell 7+ (`pwsh`)** — *the assumed/preferred shell* | `winget install Microsoft.PowerShell` | Every `*.ps1` tool: `coherent.ps1`, `coherent-eval.ps1`, `fcu/`, `sd-page-tour.ps1`, `mcdu_*`, `fp_*`, `probe-*.ps1`. |
| **Node.js 18+ (LTS)** | `winget install OpenJS.NodeJS.LTS` | The offline test harnesses (`flypad-settings-test/`, `flypad-shell-test/`, `perf-builder-test/`) and the Node probes (`fbw-mcdu-probe/`, `efb-dom-tool.js`). Run a one-time `npm install` in each jsdom harness folder. |
| **.NET 10 SDK** | (see `CLAUDE.md` Build Commands) | Building MSFSBA + the .NET probe apps (`PMDGDispatchTester`, `CDUTest`). |
| **MSFS 2020 or 2024 — running** | — | Hosts the Coherent views + the SimConnect/MobiFlight surface. |
| **MobiFlight WASM** (optional) | (FBW/community installer) | Only for reliable L:var **writes** via the calculator path (`(>L:VAR)`); not needed for read/scrape. |

> ### Use PowerShell 7 (`pwsh`), not Windows PowerShell 5.1
> **PowerShell 7 is strongly preferred and is what every example here assumes** — invoke the tools as `pwsh -File ./coherent.ps1 …` (or `./coherent.ps1 …` from a `pwsh` prompt). 5.1 (`powershell.exe`) is the legacy, **no-longer-maintained** Windows-bundled shell; it has an older/stricter parser (e.g. it mis-parses any non-ASCII byte in a no-BOM script and rejects some modern syntax). The `.ps1` tools here are deliberately kept **ASCII-only** so they *do* still run under 5.1 as a fallback — but if anything misbehaves, **switch to `pwsh` first**. pwsh installs side-by-side with 5.1 (it doesn't replace it), is free, and is strictly better. Do **not** try to uninstall 5.1 — it's a built-in Windows OS component and removing it breaks Windows features/installers; just prefer `pwsh`.

---

## 0. Scope & provenance (what this guide governs)

Everything under `tools/` falls into two buckets:

| Bucket | Tools | Status |
|---|---|---|
| **Created during the A320/A380 work** (this branch — mine + Gus's) | **`coherent.ps1` (the unified driver)**, `coherent-eval.ps1`, `_probe/`, `fcu/`, `fp_*.{js,ps1}`, `mcdu_*.{js,ps1}`, `mfd_import_and_scrape.ps1`, `sd-page-tour.ps1`, `efb-dom-tool.js`, `fbw-mcdu-probe/`, `flypad-shell-test/`, `flypad-settings-test/`, `probe-*.ps1`, `prove-coherent-scrape.ps1`, `test-coherent-ws.ps1`, and the `*.md` reference catalogs | **Documented + organized here.** Improve/reorganize freely. |
| **Pre-existing** (on `origin/main` before the FBW work) | `tools/CDUTest`, `tools/PMDGDispatchTester` | **Leave untouched.** Documented in `CLAUDE.md` (Build Commands) only. Not part of the Coherent tooling. See §7. |

This guide only reorganizes/improves the first bucket. The two PMDG tools are independent console apps with their own build story and are deliberately out of scope here.

---

## 1. The shared transport — MSFS Coherent GT remote debugger

Every A380/A32NX cockpit surface that isn't a plain SimVar — the **MFD/MCDU, flyPad EFB, ND/OANS, PFD, SD, E/WD, ISIS, FCU, and the systems-host** — is a **Coherent GT web view** (Chromium 49 / WebKit-class) rendered by the sim. The sim exposes all of them over a **remote inspector on `http://127.0.0.1:19999`**, the same WebKit/Chrome-DevTools-Protocol (CDP) endpoint that `SimConnect/CoherentDebuggerClient.cs`, `CoherentEFBClient.cs`, `CoherentEWDClient.cs`, etc. use in the app.

**This is the single most important fact for A380/A32NX debugging:** from this port you can run arbitrary JavaScript inside any cockpit view via `Runtime.evaluate` — which means you can read/write **any** L:var (`SimVar.GetSimVarValue`/`SetSimVarValue`), scrape/click **any** DOM, and call the in-page agents' methods — completely independent of the SimConnect MCP (which goes stale on focus loss).

### Key facts (memorize these)

- **No Developer Mode required.** Verified empirically on MSFS 2024 (cold boot, Dev Mode off): the sim opens port 19999 itself. *(Several of the older `probe-*.ps1` headers still say "DevMode ON" — that instruction is obsolete; it is not needed. See §6.)*
- **Resolve views by TITLE, never by id.** Page ids in `/pagelist.json` **shuffle every session**. Always fetch `/pagelist.json` and match a title substring.
- **One inspector socket per page.** Coherent GT (Chromium 49) accepts **only ONE** devtools WebSocket per view. If the app already holds a view's socket (e.g. `CoherentEWDClient` is always connected to `A380X_EWD` while the A380 is loaded; `CoherentEFBClient` owns `- EFB` while the flyPad form is open), a second connection from a tool is **rejected**. Close the relevant app window before driving that view from a tool, and vice-versa.
- **ES5 only inside agents.** Coherent GT = Chromium 49: `var` (no `let`/`const` arrow funcs), no `String.includes`/`AbortSignal.timeout`, `.indexOf()` instead, top-level `try/catch`. This constrains `Resources/coherent-*-agent.js` and any `-PreFile` you inject — but **not** the WebView2 shell or your PowerShell/Node host code.
- **Writes: use the calculator path, not data-def.** To make an L:var write stick reliably for FBW vars, write via `SimVar.SetSimVarValue('L:VAR','number',v)` inside a Coherent view, or `(>L:VAR)` / `execute_calculator_code`. The MCP `set_lvar` (native data-def) silently fails for many FBW L:vars and produces false "reverts/uncontrollable" verdicts. (Full rationale: the PLAYBOOK in `CLAUDE.md`.) **Caveat:** a `SimVar.SetSimVarValue` issued *from a Coherent view* is sometimes view-local and lost on disconnect (notably the SD page index — see `sd-page-tour.ps1`); for those, drive the write from the app or the MCP calculator path.

### View title-needles (pass to `-Title`)

```
A380X_MFD        the MFD / MCDU
A380X_ND_1       the Navigation Display (also hosts OANS / BTV)
A380X_FCU        the FCU
A380X_PFD_1      the Primary Flight Display
A380X_SDv2       the System Display (needle "A380X_SD")
A380X_EWD        the Engine/Warning Display (E/WD; ECL renders here too)
A380X_SYSTEMSHOST  the FWS systems host (fwsCore: inop/limitations/status Subjects)
ISISlegacy       the standby instrument (ISIS)
- EFB            the flyPad EFB (A32NX and A380X both match)
```

For the A32NX the needles are (verified live — note the MCDU is `A32NX_MCDU`, not `_MFD`,
and the SD / ISIS are **bare** needles, not `A32NX_*`):

```
A32NX_MCDU       the MCDU      A32NX_PFD_1   the PFD
A32NX_ND_1       the ND        A32NX_EWD_1   the E/WD
A32NX_FCU        the FCU       A32NX_SYSTEMSHOST  the systems host
SD               the System Display      ISIS   the standby instrument
- EFB            the flyPad (shared)
```

**Don't memorize these — run `./coherent.ps1 views` (see §2) to print every view's
exact title + id for whatever aircraft is loaded.**

---

## 2. The canonical tools — `coherent.ps1` (driver) + `coherent-eval.ps1` (primitive)

There are two layers, and you almost always want the first:

### 2.0 `tools/coherent.ps1` — the UNIFIED, aircraft-agnostic driver (start here)

One entry point for the four operations you actually repeat, on **any** Coherent aircraft.
It resolves views by title, obeys the one-socket rule, and — crucially — **auto-detects the
agent's window global** (any `window.__MSFSBA_*` exposing `scrape()`), so the *same* command
works for every agent we ship (flypad, a380 MFD, display, EWD, ECL, RMP, OANS). No per-aircraft
flags; no retyping inline PowerShell.

```powershell
# 1. Discover every Coherent view (title + id) for whatever aircraft is loaded
./coherent.ps1 views

# 2. Scrape a view through its agent — prints readable lines (handles BOTH agent output
#    shapes: {elements:[{kind,text,value}]} and {rows:[...]}). Add -Raw for the JSON.
./coherent.ps1 scrape  -Title "- EFB"    -Agent ..\MSFSBlindAssist\Resources\coherent-flypad-agent.js
./coherent.ps1 scrape  -Title A380X_EWD  -Agent ..\MSFSBlindAssist\Resources\coherent-ewd-agent.js

# 3. Click the first element whose text matches (re-scrapes first, clicks by stamped idx)
./coherent.ps1 click   -Title "- EFB"    -Agent ...coherent-flypad-agent.js -Text "Ground"

# 4. Capture a jsdom fixture (live geometry/visibility baked in) for the offline harnesses
./coherent.ps1 capture -Title "- EFB"    -Agent ...coherent-flypad-agent.js -Out fixtures\a320-ground.html

# 5. Arbitrary JS (read/write an L:var, call any agent method) — same as coherent-eval.ps1
./coherent.ps1 eval    -Title A32NX_MCDU -Expr "SimVar.GetSimVarValue('L:A32NX_EFB_USING_METRIC_UNIT','number')"
```

`coherent.ps1` delegates the actual CDP round-trip to `coherent-eval.ps1`, so there is exactly
ONE transport implementation. It supersedes the per-aircraft convenience wrappers (`fp_run`/
`fp_tour`/`mcdu_run`/`mcdu_tour` and the `_probe/_click_by_text.js` / `_capture_flypad_fixture.js`
one-offs) for everyday work; those remain as worked examples.

### 2.1 `tools/coherent-eval.ps1` — the raw `Runtime.evaluate` primitive

The low-level transport (L1). Use it directly when you need arbitrary one-off JS and don't want
the driver's scrape/click/capture conveniences. Everything else is either a thin wrapper around
the same transport, a standalone Node project, or an early bootstrap script it superseded.

```powershell
# Read an L:var from inside the MFD view
./coherent-eval.ps1 -Title A380X_MFD -Expr "SimVar.GetSimVarValue('L:A32NX_BTV_STATE','number')"

# Inject an in-page agent first, then call it (scrape the MCDU)
./coherent-eval.ps1 -Title A380X_MFD `
  -PreFile ..\MSFSBlindAssist\Resources\coherent-a380-agent.js `
  -ExprFile _probe\fpln_sanity.js

# Write an L:var via the calculator path (reliable for FBW L:vars)
./coherent-eval.ps1 -Title A380X_MFD -Expr "SimVar.SetSimVarValue('L:A32NX_FOO','number',1)"
```

**Parameters:** `-Title <needle>` (resolve the view; never hardcode ids) · `-Expr "<js>"` or `-ExprFile <file>` (the expression to evaluate) · `-PreFile <file>` (JS evaluated *before* the expression — usually the agent). It prints the expression's return value.

### `tools/_probe/` — the worked probe catalogue

42 small, self-contained JS snippets (each an IIFE returning a string) that run inside a live view via `coherent-eval.ps1`. **Almost every A380 feature in MSFSBA was discovered, verified, and debugged with one of these.** They are reference examples, not a test suite — copy one, tweak the var/page, run it. `tools/_probe/README.md` has the full table; highlights:

| Probe(s) | View | Demonstrates |
|---|---|---|
| `fpln_sanity.js` | A380X_MFD | agent-injected F-PLN scrape |
| `atccom_*.js` | A380X_MFD | navigate + scrape ATC COM / CPDLC / D-ATIS (UIService nav) |
| `surv_*.js`, `radio_diag.js` | A380X_MFD | SURV RadioButtonGroup enabled/disabled + click-actuation |
| `btv_dist.js`, `rudtrim.js` | A380X_MFD | ARINC429 / plain-metres readouts |
| `pack_write.js`, `ovhd_flip*.js`, `ovhd_check.js` | A380X_MFD | **write-stick test** (proved the "computed-output" PBs are settable — `CLAUDE.md` #103) |
| `efb_*.js`, `metric*.js` | `- EFB` | flyPad scrape/navigate, persistent-store metric toggle |
| `ecl_*.js` | A380X_EWD | ECL `.EclLine` scrape |
| `uisvc.js`, `sec_read.js` | A380X_MFD | resolve `uiService`; SEC plans read like the active plan |

**When writing a new probe:** name it descriptively, keep it an IIFE that returns a JSON or plain string, wrap the body in `try{…}catch(e){return 'ERR '+e+' '+(e&&e.stack)}`, and (if it calls `__MSFSBA_A380`/`__MSFSBA_FLYPAD`) inject the matching agent with `-PreFile`.

---

## 3. Convenience wrappers (thin drivers over the same transport)

These re-implement the WebSocket plumbing for a specific recurring job. They predate or complement `coherent-eval.ps1`; keep them as ready-made drivers, but for ad-hoc work prefer `coherent-eval.ps1 -PreFile … -ExprFile …`.

| Tool | View | What it does |
|---|---|---|
| `mcdu_tour.ps1` | A380X_MFD | Tours every MFD page via the agent's `navigateById` and scrapes each. The original WebSocket template the others copied. |
| `mcdu_run.ps1` | A380X_MFD | Injects `coherent-a380-agent.js`, optionally clicks a nav link by text, then evals an `-ExprFile`. MFD analogue of `fp_run.ps1`. |
| `mcdu_scrape.js` | — | One-liner: `__MSFSBA_A380.scrape(1)`. Use as an `-ExprFile`. |
| `fp_tour.ps1` | `- EFB` | Tours every flyPad nav page (clicks the nav-rail link **by text**, scrapes). Reuses one socket. |
| `fp_run.ps1` | `- EFB` | Injects `coherent-flypad-agent.js`, optional nav-by-text, evals an `-ExprFile`. |
| `fp_scrape.js` / `fp_inspect.js` | — | `__MSFSBA_FLYPAD.scrape()` one-liner / a deeper element-tree dump. Use as `-ExprFile`. |
| `mfd_import_and_scrape.ps1` | A380X_MFD | Drives the MFD end-to-end: navigates to INIT, **scrapes first to build the index→element map** (required before `sendToField`), types a city pair into the company-route field, re-scrapes INIT/FUEL&LOAD/F-PLN. Worked example of `sendToField` + `navigateById`. |
| `sd-page-tour.ps1` | A380X_SDv2 | Scrapes the **decoded** content of whatever SD page is currently shown (via `coherent-display-agent.js`) — the exact scrape the in-app SD feature performs. **Switching pages needs the MobiFlight calculator write** to `A32NX_ECAM_SD_CURRENT_PAGE_INDEX` (a Coherent-view `SetSimVarValue` is view-local and lost) — drive the page from MSFSBA's ECAM-CP combo or the MCP, then run this to read it. |

### `tools/fcu/` — FCU verification harness (has its own `README.md`)

| Tool | Purpose |
|---|---|
| `fcu/fcu-read.ps1` | Read + print the full live A380 FCU state (`fcu-probe.js` via `coherent-eval.ps1 -Title A380X_FCU`). The **verification of record** for FCU work. |
| `fcu/fcu-roundtrip.ps1` | Read → set → wait → read, PASS/FAIL per FCU value control. Standalone only if probe-side setting works for that event. |
| `fcu/fcu-set.ps1` | Best-effort fire of one FCU event/value **from the Coherent view**, to test whether probe-side setting works. Note: some `A32NX.FCU_*` events only respond to the app's SimConnect `TransmitClientEvent`, so a probe-side set can be a no-op — set via the app window and verify with `fcu-read.ps1`. |
| `fcu/fcu-probe.js` | The shared read expression. |

---

## 4. Standalone Node projects (own transport, own tests)

These are full mini-projects with `package.json` and tests — not Coherent-eval wrappers.

| Project | Transport | Purpose |
|---|---|---|
| `tools/fbw-mcdu-probe/` | SimBridge MCDU websocket `ws://localhost:8380/interfaces/v1/mcdu` | Node CLI to inspect/drive/capture the **A32NX** MCDU without the C# app: `watch` / `press` / `type` / `replay` (`--export` JSONL captures; `replay` re-renders a capture offline). **`mcdu-format.js` is the authoritative decode reference** and must stay in sync with `Services/FbwMcduFormat.cs`; `node --test` (`mcdu-format.test.js`) covers it. See its `README.md`. |
| `tools/flypad-shell-test/` | jsdom (no sim) | The canonical **keyed-DOM-reconcile spec** for the flyPad WebView2 shell. `reconcile.test.js` fails on idx-keying and passes on content-keying — it encodes why the shell keys by content, not scrape idx (stops NVDA focus jumps). Mirror any reconcile change here. The reference shell is `Resources/flypad-shell.html`. |
| `tools/efb-dom-tool.js` | CDP over `:19999` (Node 18+) | Live CDP scraper/clicker for the **A32NX EFB**: `node tools/efb-dom-tool.js state|scrape|click …`. A Node counterpart to `fp_run.ps1` for the flyPad bridge. |

---

## 5. Reference catalogs (`*.md` in `tools/`)

Static research notes mined from the FBW source, used while building the A380 definition. Not executable; read them when you need a var name or page-index map.

- `a380-simvars-catalog.md` — the master A380 SimVar/L:var catalog.
- `a380-fcu-vars.md` — FCU mapping (the A380 has **no** `A32NX_FCU_AFS_DISPLAY_*` vars).
- `a380-engine-start.md` — engine master / mode-selector mechanics.
- `a380-fault-status-vars.md` — fault/annunciator L:vars.
- `a380-sd-pages.md` — SD page index map + per-page var sources.
- `fbw-a380-mfd-source-findings.md` — MFD/MCDU structure dug out of the instrument TSX.
- `ecam-display-readout-notes.md` — ECAM/EWD readout notes.
- `a320-panel-structure.md` — A32NX panel hierarchy reference.

The live equivalent of these is the **runtime L:var dump** `tools/_probe/fbw_lvars_clean.txt` (every L:var in the running aircraft) plus the diff artefacts `tools/_probe/*.txt` from the #104 catalog diff.

---

## 6. Early bootstrap probes — kept, but superseded

These were the **first exploratory scripts** that discovered the no-injection Coherent architecture. They are superseded by `coherent-eval.ps1` + `_probe/` and their headers still say "Developer Mode ON" (now known to be unnecessary — see §1). **Kept** as historical record / occasional one-off diagnostics; **prefer `coherent-eval.ps1` for new work.**

| Tool | Original purpose |
|---|---|
| `probe-coherent-debugger.ps1` | Enumerate every inspectable Coherent view (writes `coherent-debugger-dump.txt`). The first "what's on port 19999?" probe. |
| `test-coherent-ws.ps1` | Prove a raw WebSocket `Runtime.evaluate` against a view and dump the DOM. The first working CDP round-trip. |
| `prove-coherent-scrape.ps1` | End-to-end proof of the no-injection architecture (resolve MFD+EFB by title, scrape both). |
| `probe-efb-frames.ps1` | Locate where the flyPad content lives (top doc + iframes). |
| `probe-flypad-elements.ps1` | Enumerate the flyPad control tree (the seed for `coherent-flypad-agent.js`). |

> If you ever consolidate these, fold them into `coherent-eval.ps1` (or a `_probe/` script) rather than maintaining parallel WebSocket plumbing — but they are harmless as-is and a couple still serve as quick "is the port up / what views exist" smoke tests.

---

## 7. Pre-existing tools (out of scope — do not modify)

Two console apps predate the FBW work and are **not** Coherent tools. Documented here only so you don't mistake them for debugger tooling:

- **`tools/PMDGDispatchTester/`** — a console REPL that probes which PMDG NG3 dispatch shape a switch accepts against a live sim. Compiles the main app's `SimConnect/PMDGNG3DataStruct.cs` via a **linked** `<Compile>`. Builds as part of `MSFSBlindAssist.sln`.
- **`tools/CDUTest/`** — fires a single CDA-write or `TransmitClientEvent` at one chosen PMDG event. Builds on its own (`dotnet build tools/CDUTest`).

Leave these alone. (Details in `CLAUDE.md` → Build Commands.)

---

## 8. Crash diagnosis

> Symptom (reported): the app crashes unpredictably — sometimes while navigating A380 panels, sometimes idle, sometimes with the flyPad open or another window opening. Not reliably reproducible. "The log doesn't really do anything."

### What's already in place

**Global exception handlers** are installed at startup (`Program.cs` → `InstallGlobalExceptionHandlers`), with three distinct CLR fault channels:

| Handler | Covers | Behavior |
|---|---|---|
| `Application.ThreadException` (with `SetUnhandledExceptionMode(CatchException)`) | UI-thread faults: WinForms timer ticks, event handlers, `BeginInvoke` callbacks | **Logs and KEEPS RUNNING.** For a screen-reader tool, staying alive beats dying on a non-fatal glitch. |
| `AppDomain.CurrentDomain.UnhandledException` | Background-thread faults: SimConnect receive pump, `Task.Run` poll loops | Logs the cause (with `IsTerminating`). The CLR still tears the process down — this can't *prevent* the crash, only record it. |
| `TaskScheduler.UnobservedTaskException` | A faulted `Task` whose exception was never awaited | Observes it (prevents a finalizer-time crash) and logs. |

**The startup log DOES capture managed crashes.** `Utils/StartupLogger.Log` writes each line with **`File.AppendAllText`**, which opens→writes→closes per call — i.e. it **flushes to disk on every line**. So if a crash is a managed .NET exception on any of the three channels above, its stack trace **is** on disk at the moment of death.

- **Log location:** `%TEMP%\MSFSBlindAssist_Startup_<yyyyMMdd_HHmmss>.log` (one per launch). Get the exact path at runtime from `StartupLogger.GetLogFilePath()`; it's also shown in the startup-error dialog.

### Why a crash can still leave "nothing useful" in the log

If the log shows no exception before the process vanished, the crash was almost certainly **native (unmanaged)** — outside the CLR's reach, so none of the three handlers fire and nothing is logged. The likely native sources, given this app:

- **WebView2** (flyPad EFB host, PMDG EFB) — native Edge/Chromium process; a renderer or host crash can take the process down.
- **Coherent GT debugger socket churn** — connect/disconnect races against `:19999` (especially around the one-socket-per-page limit on aircraft swap / window open-close).
- **SimConnect.dll** — native interop; a bad marshal or a call after the sim drops can fault natively.

### How to diagnose the next occurrence

1. **Read the per-launch log first.** `%TEMP%\MSFSBlindAssist_Startup_*.log` (newest). A logged `Unhandled …exception` line with a stack trace = a **managed** crash → fix at the source named in the trace.
2. **No exception line, log ends mid-stream = native crash.** Correlate with **Windows Event Viewer → Windows Logs → Application** for an *Application Error* / *.NET Runtime* / *Faulting module* entry at the crash timestamp. The faulting module name (e.g. `WebView2Loader.dll`, `EmbeddedBrowserWebView.dll`, a Coherent module, `SimConnect.dll`) points at the native culprit.
3. **Note the activity context** the reporter gave (panel nav / idle / flyPad open / another window opening) and check whether it lines up with a WebView2/Coherent lifecycle event (form open/close, aircraft swap disposing a Coherent client, a poll firing while a view is being torn down).
4. **Reproduce under a debugger** if possible: run the Debug x64 build from Visual Studio so a native first-chance exception breaks instead of vanishing.

### Hardening directions (when this becomes a focused task)

- Guard every Coherent client's connect/dispose against use-after-dispose and double-connect (the one-socket limit makes races here likely); ensure polls stop *before* the socket is disposed on window close / aircraft swap.
- Wrap WebView2 host interactions so a renderer-process-gone event is caught and the form recovers (re-init) instead of propagating.
- Consider a native crash dump hook (`AppDomain` can't catch these, but a minidump via Windows Error Reporting LocalDumps or a SetUnhandledExceptionFilter shim would capture the faulting native stack).

*(Task #143 "Fix idle crashes" tracks this; the handlers + flushing log above are the work done so far. The remaining work is identifying the specific native culprit from a captured occurrence.)*

---

## 9. Adaptability to other aircraft (PMDG / Fenix / WT / future add-ons)

> **The question to always ask of a scraper/driver: can this serve another aircraft, yes or no — and if yes, how; if no, why; and how *could* a future agent make it adaptable?** This section answers it for every tool. The short version: the **transport and the generic scrape core are universal**; the **aircraft-specific selector/navigation/input layer is not** and must be re-derived per add-on. Whether that re-derivation is cheap or expensive depends entirely on how the target add-on is built.

### 9.1 The three layers (where adaptability lives)

Every Coherent scraping tool is really three stacked layers. Adaptability is a per-layer property:

| Layer | What it is | Adaptable? |
|---|---|---|
| **L1 — Transport** | CDP `Runtime.evaluate` over `:19999`, resolve-by-title, one-socket-per-page. `coherent-eval.ps1`, `CoherentDebuggerClient.cs`, and the WebSocket plumbing copied into every `*.ps1` driver. | **Universal.** Works for **any** MSFS glass cockpit that renders in Coherent GT — PMDG, Fenix, WT/Asobo, FBW, all of them. Nothing here is aircraft-specific except the `-Title` needle you pass. |
| **L2 — Generic scrape core** | Collect leaf text nodes, cluster by Y (row), sort by X (reading order), join. `coherent-display-agent.js` is *almost entirely* this layer (verified: ~0 aircraft-specific class needles). | **Universal for any DOM.** Point it at any Coherent view and it returns the on-screen text in reading order. The only inherent limit is **graphical/positional displays** (PFD/ND/ISIS tapes) — a flat scrape returns scale tick-marks, not the needle reading, on *every* aircraft. |
| **L3 — Aircraft-specific semantics** | DOM-class selectors, element classification (combo vs radio vs field), page navigation, and the **input mechanism**. `coherent-a380-agent.js` is mostly this layer: `mfd-fms-fpln-*`, `mfd-dropdown-*`, `mfd-radio-button`, `_MFD_pageSelector`, the FBW `InputField` keypress path, `uiService.navigateTo`, `a380x-mfd`. | **NOT portable as-is.** These needles exist only in the FBW A380 instrument DOM. To serve another aircraft you keep L1+L2 and **rewrite L3** against the target's DOM/source. |

The whole "is it adaptable" answer for any tool reduces to: *how much of it is L3, and how easy is the target add-on's L3 to re-derive?*

### 9.2 Per-tool adaptability verdict

| Tool / agent | Adaptable to other aircraft? | Why / How |
|---|---|---|
| **`coherent-eval.ps1`** (+ the `*.ps1` WebSocket plumbing) | **Yes — fully, no changes.** | Pure L1. Pass a different `-Title` (e.g. a PMDG/WT view) and any JS. It already reads/writes L:vars and scrapes DOM on any aircraft. This is the substrate every other adaptation builds on. |
| **`coherent-display-agent.js`** (SD/EWD/PFD/ND/ISIS generic scraper) | **Yes — works on any Coherent display today.** | Almost pure L2. The header even says "Works on any display view." For a new aircraft: just point a `CoherentDisplayClient`/`coherent-eval.ps1 -PreFile` at that aircraft's display view. **Caveat (universal):** tape/graphical displays read as tick-marks — fall back to SimVars there, same as we do for the A380 PFD/ND/ISIS. |
| **`coherent-flypad-agent.js`** (flyPad EFB) | **Yes for FBW aircraft (already proven); No for other vendors' EFBs.** | This agent serves **both** the A32NX and the A380X flyPad **with zero changes** — same React flyPadOS 3, same `- EFB` view, same `L:A32NX_EFB_TURNED_ON` power L:var. That *is* a successful cross-aircraft adaptation. It will **not** work on a non-FBW EFB (the native MSFS EFB) — different DOM and framework. PMDG's tablet has its own generic Coherent agent (`coherent-pmdg-efb-agent.js` via `CoherentPmdgEfbClient`, same `:19999` debugger transport — the HTTP-bridge `EFBBridgeServer` was retired). *(Verified live this session: scraped the A380 flyPad Payload page, set Passengers + Cargo via `setValue`, and watched the loadsheet ZFW recompute — see 9.4.)* |
| **`coherent-a380-agent.js`** (MFD/MCDU scraper — **the one to study**) | **Partially — keep L1+L2, rewrite L3.** | The generic row-reconstruction (`enumerateLines`, the same-row merge) ports directly; everything else (`buildFplnLines`, the dropdown/radio/context-menu classifiers, `navigateById`/`navigateUri`, the `InputField` keypress input, the F-PLN special-line handling) is keyed to FBW A380 DOM classes and the FBW input path. To serve another aircraft's MFD/CDU you re-derive those from the target's source/DOM — see the recipe in 9.3. |
| **`coherent-ewd-agent.js` / `coherent-ecl-agent.js`** | **No (FBW-specific), re-derivable.** | Keyed to FBW classes (`.EclLine`, `.AbnormalItem`, `.StsArea`, `.SelfTest`). The *pattern* (scrape rows, edge-detect persistent boxes, baseline silently) is reusable, but the selectors must be replaced for any other aircraft's warning display. |
| **`coherent-oans-agent.js`** | **No — A380 ND/OANS-specific.** | The OANS control panel (`.oans-control-panel`, the ND "MAP DATA" menu) is an A380/A350-class feature. Only relevant to aircraft that model an equivalent airport-nav system. |
| **`fbw-mcdu-probe/`** (Node, SimBridge websocket) | **No beyond FBW.** | Transport is the **SimBridge MCDU relay** `ws://localhost:8380/interfaces/v1/mcdu`, which only FBW aircraft expose. A non-FBW CDU has no SimBridge socket; you'd use L1 (CDP) instead. The `mcdu-format.js` decode logic is FBW curly-brace markup, also FBW-only. |
| **`efb-dom-tool.js`** (Node CDP, A32NX EFB) | **Same as the flypad agent** — FBW EFB only; the CDP transport underneath is universal. |
| **`flypad-shell-test/`** (keyed-reconcile spec) | **Yes — pattern is framework-agnostic.** | Pure jsdom; encodes the "key by content, not scrape idx" rule for any WebView2 accessible-mirror. Reuse it as the reconcile spec for *any* aircraft's scraped-DOM mirror form. |
| **Convenience drivers** (`mcdu_*`, `fp_*`, `sd-page-tour`, `mfd_import_and_scrape`, `fcu/`) | **Plumbing yes; payload no.** | The WebSocket loop is L1 (universal). Each driver is hard-wired to one `-Title` + one `-AgentFile`; to retarget, swap those two and the agent-method calls. Easiest path for a new aircraft is usually `coherent-eval.ps1 -Title <new view> -PreFile <new agent>` rather than cloning a driver. |

### 9.3 Recipe — adapt the MFD/CDU scraper to a new aircraft

This is the concrete "how could another agent make it adaptable" answer for the MFD scrapers the user asked about. The effort is **proportional to how the target add-on is built**, so step 0 is triage.

**Step 0 — Triage the add-on's architecture (decides feasibility):**
- **Open-source instruments (FBW, headwind, any community jet with a public repo)** → *cheap.* You can read the TSX/JS to get exact DOM classes, the page-navigation API, and the input mechanism. This is how `coherent-a380-agent.js` was built.
- **Closed glass that still renders in Coherent (WT/Asobo G1000/G3000, many study sims)** → *medium.* No source, but the DOM is live-inspectable via L1 — scrape `document.body.outerHTML` and reverse-engineer the class names and structure.
- **Closed add-on with its own SDK surface (PMDG, Fenix)** → *don't scrape — use the SDK.* PMDG exposes a documented data struct + custom events (`get_pmdg_cdu`, `send_pmdg_event`); Fenix uses settable L:vars. The CDU text comes from the SDK far more reliably than from a DOM scrape. (PMDG's EFB *is* made accessible via a DOM bridge, but over its own HTTP server, not CDP.)

**Step 1 — Find the view.** `coherent-eval.ps1`/pagelist → the target's MFD/CDU title needle (e.g. a WT `AS3000_*` view). Resolve by title; ids shuffle.

**Step 2 — Dump the raw DOM** to learn the structure:
```powershell
./coherent-eval.ps1 -Title <NEEDLE> -Expr "document.body.outerHTML.slice(0,8000)"
```
Identify: the container class for a "line/cell", the field/input elements, the dropdown/menu markup, and how the page-selector is represented.

**Step 3 — Start from the generic core.** Copy `coherent-display-agent.js` (L2) as the base — its Y-cluster/X-sort already gives readable rows for most pages with **zero** aircraft knowledge. For many displays that alone is enough.

**Step 4 — Add L3 only where the generic scrape falls short** (grid pages, combos, the flight plan). Port the *shape* of the A380 agent's helpers but swap the selectors:
- Row/grid merge → reuse as-is (geometry, not classes).
- Combo/radio/field classification → replace `mfd-dropdown-*` / `mfd-radio-button` with the target's classes.
- Flight-plan builder → replace `mfd-fms-fpln-*` with the target's leg-line container.

**Step 5 — Find the input mechanism (the hardest part, always aircraft-specific).** The A380 lesson generalizes: *the visible widget is often not the thing you poke.* The A380 MFD takes text via the FBW `InputField` (focus-click + synthetic `keypress` with `keyCode`, Enter=13), **not** the KCCU cursor H-events. For a new aircraft, determine empirically (or from source) whether input is: a native `<input>` (set value + dispatch `input`/`change`), a synthetic keypress path, a SimVar/L:var the keypad writes, or an SDK event (PMDG). Prove it with a write-then-rescrape, exactly like `mfd_import_and_scrape.ps1` does for the A380.

**Step 6 — Find the navigation mechanism.** A380 = click a stable page-selector id, or `uiService.navigateTo(uri)` for cross-system jumps. A new aircraft may use page-button clicks, a hardware-key event, or an SDK page command. Whatever it is, expose it as the agent's `navigate*` method so a driver can walk every page.

**Step 7 — Verify by round-trip**, never by assumption: scrape → act → re-scrape and confirm the change landed (the universal write-stick discipline from the `CLAUDE.md` PLAYBOOK).

### 9.4 Worked evidence — the flyPad scraper IS cross-aircraft (verified live)

To ground the "flyPad agent serves both FBW jets" claim, this session drove the **A380X** flyPad Payload page with the *same* `coherent-flypad-agent.js` used for the A32NX:

- **Scrape** read the page cleanly: actionable fields as `label = value` (`Passengers = 415`, `Cargo (KGS) = 8300`, `ZFW (KGS) = 343167`, `Per Passenger Weight (84) = 84`) plus limits (`Maximum Passengers 484`, `Maximum ZFW 373000 KGS`, `Maximum ZFWCG 43%`).
- **Click** navigated the nav-rail (`Ground`) and the sub-tabs (`Services`/`Fuel`/`Payload`/`Pushback`).
- **`setValue`** inserted test values (Passengers→484, Cargo→45000); the agent returned `ok` and the React form accepted them.
- **Loadsheet recompute** was correct and live: **ZFW 343167 → 350343 KGS**. Values were then restored.

**Formatting note (honest):** the *actionable inputs* and the *headline loadsheet numbers* read perfectly. The **CG-envelope chart** values (ZFWCG % planned/current, the axis tick labels, the MTOW/MLDW/MZFW markers) come through as **positioned text fragments** — inherent to scraping a graph; the key numbers are present but not joined to their label/unit on one line. The in-app WebView2 view bands them by row so they read in sequence; a pilot gets the loadsheet, just not a tidy single sentence for the CG chart. This is a display-is-a-graph limitation, not an agent bug, and it is identical across both FBW jets.

---

## 10. Quick decision guide

- **List every Coherent view (any aircraft)** → `coherent.ps1 views`.
- **Scrape / click / capture a view (any aircraft, any agent)** → `coherent.ps1 scrape|click|capture -Title … -Agent …` (auto-detects the agent global).
- **Read/write one L:var, or arbitrary one-off JS** → `coherent.ps1 eval -Title … -Expr …` (or `coherent-eval.ps1 -Title … -Expr/-ExprFile`, injecting an agent with `-PreFile`).
- **Reproduce a known finding** → the matching `tools/_probe/*.js` (see `_probe/README.md`).
- **Tour all MFD / flyPad pages** → `mcdu_tour.ps1` / `fp_tour.ps1` (or loop `coherent.ps1 click`/`scrape`).
- **Read the SD** → `sd-page-tour.ps1` (drive the page from the app/MCP first).
- **Verify FCU** → `tools/fcu/fcu-read.ps1` (+ roundtrip/set).
- **Drive the A32NX MCDU without the app** → `tools/fbw-mcdu-probe/` (Node).
- **Touch the flyPad reconcile** → mirror `tools/flypad-shell-test/`.
- **A control "doesn't work"** → STOP. Read the **VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK** in `CLAUDE.md` (calculator-path write-stick test, write-mechanism decision tree, the case studies) before concluding anything.
- **"Can I reuse this scraper for another aircraft?"** → §9: transport + generic scrape core are universal; the aircraft-specific selector/nav/input layer must be re-derived (recipe in §9.3).
- **Crash** → §8: read `%TEMP%\MSFSBlindAssist_Startup_*.log`, then Event Viewer for native faults.
