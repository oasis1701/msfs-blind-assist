# Logging Infrastructure Unification — Design

**Date:** 2026-07-07
**Status:** Approved design → implementation plan (user waived the spec-review gate; execute immediately).
**Scope:** Introduce a single logging facade over `AppLogs`, fix the multi-writer corruption bug, capture Debug traces to a rotating file, and standardize every diagnostic-log writer through it.

## Problem

A 2026-07-07 survey of the codebase found the logging is fragmented at the write layer:

- **790 `Debug.WriteLine`** calls are the de-facto logger — but they only reach a debugger/DebugView, so they are **invisible in a normal Release run**. For a blind user whose main support channel is "send me your logs," most of the app's trace never reaches a file.
- **`AppLogs` is only a path resolver** (`PathFor` returns a path + ensures the folder exists). There is **no logging API** — no levels, no formatting, no locking. Every subsystem hand-rolls "get path → format a line → `File.AppendAllText`," via 7 different `static readonly XLogPath` variants and ~41 raw file-write call sites.
- **Multi-writer corruption (the one real defect):** `docking-aircraft.log`, `landing_exit.log`, and `registration.log` are each appended from **4+ independent call sites on different threads** (SimConnect position thread + UI thread) with no coordination → interleaved/garbled lines.
- **No levels, no rotation, inconsistent/absent timestamps**; only `startup.log` is size-managed (truncate per launch).
- **13 `Console.WriteLine`** in a WinForms app = dead output.
- **No logging framework** (`ILogger`/Serilog/NLog absent) and one one-off `StartupLogger` class.

## Goals

One lightweight logging facade that: fixes the multi-writer corruption; makes Release-run trace persist to disk; and gives every diagnostic log a uniform, leveled, greppable, rotation-managed format — without harming the app's real-time audio guidance.

## Decisions (settled during brainstorming)

- **Scope:** *Full standardization* — the facade, the corruption fix, killing dead `Console.WriteLine`, migrating the 790 `Debug.WriteLine`, AND standardizing every named-file writer's format/levels/rotation (retiring the per-subsystem `XLogPath` idiom).
- **File layout:** *Keep the named per-subsystem files* (`taxi_guidance.log`, `docking.log`, `registration.log`, …) — routed through the facade — **plus one new rotating `debug.log`** for the migrated `Debug.WriteLine` traces. Preserves the established "send me your logs" workflow.
- **Debug capture:** *Always capture `Debug+` every run, rotation-managed.* A blind user's logs always contain the trace with zero setup; rotation keeps the folder bounded.
- **Writer model:** *Async background writer (approach B).* Callers format+enqueue and return; a single background thread does all disk I/O. Fixes both the corruption and hot-path latency (this app logs from per-frame SimConnect callbacks, and synchronous disk I/O there would risk audio/tone stutter).
- **Levels:** `Debug, Info, Warn, Error` (four; no Trace/Fatal).

## Out of scope

- `*_Checklist.txt`, `*_Hotkeys.txt`, `bridge-version.txt`, `msfsba-bridge-version.txt` — these are **feature outputs**, not diagnostics; left as-is.
- Adopting a logging framework (Serilog/NLog/Microsoft.Extensions.Logging) — a tiny custom facade fits this single WinForms app and the existing `AppLogs` "one folder" philosophy.
- Structured/JSON logging, remote log shipping, per-user log config UI.

---

## Architecture

New files under `MSFSBlindAssist/Utils/Logging/`, each with one responsibility:

| Component | Responsibility | Testable in isolation |
|---|---|---|
| `Log` (static facade) | Public API; formats + enqueues. Never blocks, never throws into callers. | via its effects |
| `LogLevel` (enum) | `Debug, Info, Warn, Error` | — |
| `LogFormatter` (pure) | `(timestamp, level, category, message) → string` | **yes** (unit) |
| `LogRotator` (pure decision) | `(filePath, currentSize, policy) → roll? + target sequence` | **yes** (unit) |
| `LogChannel` | A per-file handle; `Info/Warn/Error(msg)` enqueue a `LogEntry{file, line}` | via writer |
| `LogWriter` (background) | Single consumer thread; drains bounded queue, batch-appends per file, applies rotation, drop-accounting | **yes** (integration, temp dir) |

`AppLogs` stays as the path/folder authority; `LogWriter` resolves every file via `AppLogs.PathFor(...)`.

### Public API

```csharp
// App-wide diagnostic stream → debug.log
Log.Debug(string category, string message);   // also mirrors to Debug.WriteLine (IDE Output)
Log.Info (string category, string message);
Log.Warn (string category, string message);
Log.Error(string category, string message, Exception? ex = null);

// Dedicated named files (one channel == one file)
LogChannel ch = Log.Channel("taxi_guidance");  // → taxi_guidance.log; cached per name
ch.Info(string message);  ch.Warn(...);  ch.Error(...);  ch.Debug(...);
```

- **Category** is a free-text tag (`"SimConnect"`, `"Docking"`, …) — appears in the line, does not create a file. The `Log.*` methods write to the shared `debug.log`.
- **Channels** map 1:1 to the existing named files. `Log.Channel(name)` is cached (one `LogChannel` per name).
- A channel MAY opt into **truncate-on-launch** (for `startup.log`, preserving current behavior): `Log.Channel("startup", truncateOnLaunch: true)`.

### Line format

`2026-07-07 14:03:12.417 [INFO ] [Docking] message` — local time, invariant culture, level left-padded to 5. `Log.Error(..., ex)` appends the exception on following indented lines. One formatter, used by all channels including `debug.log`.

### Async writer contract

- `Log.*`/`channel.*` build the formatted line and `TryAdd` it to a **bounded** `BlockingCollection<LogEntry>` (capacity ~10,000). Returns immediately.
- **On full queue:** drop the new entry, `Interlocked`-increment a dropped counter. Never block a caller. When the writer next drains, if the counter > 0 it emits one `[WARN ] [Log] N entries dropped (log queue saturated)` line to `debug.log` and resets it.
- **One consumer thread** takes from the collection, groups the batch by file, and for each file: check rotation (`LogRotator`), roll if needed, `File.AppendAllText`. All file I/O — append and roll — happens only here, so no locks and no rotation races.
- **Rotation policy:** per file, size cap `5 MB`, retention `3` (`x.log` → `x.1.log` → `x.2.log` → `x.3.log`, oldest dropped). `LogRotator` is a pure function of (path, size, cap, retention).

### Lifecycle & crash safety

- `Log.Init()` — called **early** in `Program.Main` (before `AppLogs.MigrateLegacyLogs()` stays as-is): starts the writer thread. Calls before `Init` still enqueue (drained once the thread runs).
- `Log.Shutdown()` — in `Program.Main`'s `finally`: `CompleteAdding()`, join the writer (bounded timeout, e.g. 2 s), so a clean exit flushes.
- `AppDomain.CurrentDomain.UnhandledException` + `ProcessExit` hooks → best-effort synchronous drain-and-write of whatever is queued, so a crash still leaves the trace on disk.

---

## Migration

Facade + tests land first; then everything routes through it, mechanically, per-file, build-verified:

1. **Named-file writers** (the `static readonly XLogPath` + `File.AppendAllText` idiom across ~7 subsystems and the inline `AppLogs.PathFor` appends): replace with `Log.Channel("<name>").Info/Warn/Error(...)`. **Message content preserved**; ad-hoc prefixes replaced by the uniform one; where a writer hand-stamps its own timestamp, remove it (the formatter adds one). This funnels the multi-writer files through the single writer thread — **the corruption fix**.
2. **The 790 `Debug.WriteLine(x)`** → `Log.Debug("<category>", x)`, category derived mechanically per class/file (`MainForm.*` → `"MainForm"`, `SimConnectManager.*` → `"SimConnect"`, each aircraft def → its code, etc.). Done per-file/per-directory so each batch is small and independently reviewable.
3. **The 13 `Console.WriteLine`:** delete the dead ones; convert any with useful content to `Log.Debug`.
4. **`StartupLogger`:** re-home onto the facade as `Log.Channel("startup", truncateOnLaunch: true)`; keep `startup.log` + its per-launch truncate. Remove the bespoke class.
5. **Docs:** update CLAUDE.md's "Diagnostic Logs — ONE folder, via `Utils/AppLogs`" section and the logging invariants to describe the `Log` facade as the single entry point, the new format, `debug.log`, and rotation. `AppLogs` remains the path authority.

**Accepted caveat:** standardizing the prefix changes the exact line format anyone currently greps in the named logs. Full standardization accepts this; filenames and message text are preserved so "send me your logs" still works.

---

## Error handling & edge cases

- **Never throws into callers** — enqueue is exception-free; all `try/catch` is on the consumer thread, per write.
- **Disk full / file locked** → consumer catches, drops the line, counts it; emits one recovery notice when writes resume. Never blocks/crashes.
- **Queue overflow** → bounded queue drops the newest with a counter; a stalled disk can never grow the queue into OOM.
- **Crash/exit** → `Shutdown()` flush in `finally` + unhandled-exception/`ProcessExit` best-effort drain.
- **Rotation races** → impossible; all file I/O is on the single writer thread.
- **Pre-`Init` calls** → enqueue works before the writer starts; drained once `Init` runs.
- **Reentrancy** → the writer never calls back into `Log` except the single drop-notice line (guarded so it can't recurse).

---

## Testing

Uses the Phase-2 `MSFSBlindAssist.Tests` project.

**Pure-logic unit tests (deterministic, no threads/files):**
- `LogFormatter`: exact string for each level/category/message; exception rendering; padding; invariant formatting.
- `LogRotator`: at/under/over the cap → roll decision + the correct target-filename sequence and retention drop.
- Category-derivation helper (if extracted): class/file → category.
- Drop-accounting math.

**Focused integration tests (temp dir, real files):**
- Writer drains a batch → lines land intact, in order, correctly formatted.
- Rotation actually rolls at the cap with correct retention (`x.log`/`x.1.log`/…).
- **Concurrency regression test for the corruption bug:** N threads each enqueue M lines to the *same* file; after flush assert total line count == N×M and **zero partial/interleaved lines**.
- Shutdown flushes the queue; a simulated overflow increments the drop counter and emits the recovery notice.

**Migration safety net:** build stays **0 warnings**; the existing **214 tests stay green**; and an in-sim smoke check (by the human) that each named log still populates and audio guidance is unaffected.

## Verification & rollout

- Branch `feat/logging-infrastructure` off `main`; single PR to `origin`.
- Per the CLAUDE.md build rule: verify on `-p:Platform=x64`; a **Release build** (`dotnet build MSFSBlindAssist.sln -c Release`) is produced for the human to test.
- The facade + writer must be behavior-safe: logging must never change app behavior or add hot-path latency.
