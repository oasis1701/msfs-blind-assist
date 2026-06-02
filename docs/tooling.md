# Developer Tooling Guide — Coherent debugger, probes, and crash diagnosis

A detailed reference to every dev/debug tool in `tools/`, written for **agents (Claude/Codex) and human contributors**. It explains the one transport almost all of them share (the MSFS Coherent GT remote debugger), which tool to reach for, how to run it, and how to diagnose the app's crashes.

> **Read this when:** you need to read/write a live FlyByWire L:var, scrape or drive a cockpit display / MCDU / flyPad, verify whether a control's write "sticks", reproduce a `tools/_probe` finding, or investigate a crash. For the *methodology* of proving a control works (write-stick rules, the write-mechanism decision tree), this guide points you at the **VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK** in `CLAUDE.md` — that is the source of truth; this guide is the tool catalogue.

---

## 0. Scope & provenance (what this guide governs)

Everything under `tools/` falls into two buckets:

| Bucket | Tools | Status |
|---|---|---|
| **Created during the A320/A380 work** (this branch — mine + Gus's) | `coherent-eval.ps1`, `_probe/`, `fcu/`, `fp_*.{js,ps1}`, `mcdu_*.{js,ps1}`, `mfd_import_and_scrape.ps1`, `sd-page-tour.ps1`, `efb-dom-tool.js`, `fbw-mcdu-probe/`, `flypad-shell-test/`, `probe-*.ps1`, `prove-coherent-scrape.ps1`, `test-coherent-ws.ps1`, and the `*.md` reference catalogs | **Documented + organized here.** Improve/reorganize freely. |
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

For the A32NX the analogous needles are `A32NX_*` (e.g. `A32NX_EWD_1`).

---

## 2. The canonical tool — `coherent-eval.ps1` (+ `_probe/`)

**`tools/coherent-eval.ps1` is the preferred entry point for all new probing.** Everything else is either a thin wrapper around the same transport, a standalone Node project, or an early bootstrap script it superseded.

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

## 9. Quick decision guide

- **Read/write one L:var, or scrape/click one view, ad-hoc** → `coherent-eval.ps1 -Title … -Expr/-ExprFile` (inject an agent with `-PreFile` if you call `__MSFSBA_*`).
- **Reproduce a known finding** → the matching `tools/_probe/*.js` (see `_probe/README.md`).
- **Tour all MFD / flyPad pages** → `mcdu_tour.ps1` / `fp_tour.ps1`.
- **Read the SD** → `sd-page-tour.ps1` (drive the page from the app/MCP first).
- **Verify FCU** → `tools/fcu/fcu-read.ps1` (+ roundtrip/set).
- **Drive the A32NX MCDU without the app** → `tools/fbw-mcdu-probe/` (Node).
- **Touch the flyPad reconcile** → mirror `tools/flypad-shell-test/`.
- **A control "doesn't work"** → STOP. Read the **VARIABLE / CONTROL TROUBLESHOOTING PLAYBOOK** in `CLAUDE.md` (calculator-path write-stick test, write-mechanism decision tree, the case studies) before concluding anything.
- **Crash** → §8: read `%TEMP%\MSFSBlindAssist_Startup_*.log`, then Event Viewer for native faults.
