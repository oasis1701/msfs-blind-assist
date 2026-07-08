# Logging Infrastructure Unification — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A single async-background-writer logging facade over `AppLogs` that fixes the multi-writer corruption, captures `Debug` traces to a rotating `debug.log`, and standardizes every diagnostic-log writer with a uniform leveled format.

**Architecture:** New `MSFSBlindAssist/Utils/Logging/` package: pure `LogFormatter` + `LogRotator`, a single-consumer background `LogWriter` draining a bounded queue, `LogChannel` per file, and a static `Log` facade. Callers format+enqueue and return; one thread does all disk I/O. Companion spec: `docs/design/2026-07-07-logging-infrastructure-design.md`.

**Tech Stack:** .NET 10 (C# 13), WinForms, xUnit. Builds x64. No new NuGet dependencies.

## Global Constraints

- **Behavior-preserving:** logging must never change app behavior, throw into callers, or add hot-path (SimConnect/UI thread) latency. All disk I/O is on the single writer thread.
- **Build the SOLUTION with `-p:Platform=x64`:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`. Must stay **0 Warning(s), 0 Error(s)**. Verify the exe lands in `bin\x64\Debug\net10.0-windows\`. A bare `.sln` build can go to AnyCPU `bin\Debug\` on this machine — always pass `-p:Platform=x64`.
- **Tests:** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` — the existing 214 must stay green; new tests add to that count.
- **Log format (exact):** `yyyy-MM-dd HH:mm:ss.fff [LEVEL] [category] message`, local time, `CultureInfo.InvariantCulture`, level tag padded to 5 chars (`DEBUG`,`INFO `,`WARN `,`ERROR`). Example: `2026-07-07 14:03:12.417 [INFO ] [Docking] docked`.
- **Rotation:** per file, size cap **5 MB** (5*1024*1024), retention **3** (`x.log`→`x.1.log`→`x.2.log`→`x.3.log`, oldest deleted).
- **Queue:** bounded capacity **10000**; overflow drops the new entry and increments a counter (never blocks).
- **NOT in scope — do not touch:** `Console.WriteLine` anywhere (the app `AllocConsole()`s a hidden console "for NVDA to monitor" in `Program.cs:52`, so console output may be an intentional screen-reader channel — leave all `Console.WriteLine` as-is); the `*_Checklist.txt`/`*_Hotkeys.txt`/`bridge-version.txt` feature outputs; `AppLogs` (stays the path authority).
- **Commit trailer:** every commit ends with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Never push without permission (the controller opens the PR at the end).

## File structure

Create under `MSFSBlindAssist/Utils/Logging/`:
- `LogLevel.cs` — `enum LogLevel { Debug, Info, Warn, Error }`
- `LogFormatter.cs` — pure: `(DateTime, LogLevel, category, message, Exception?) → string`
- `LogRotator.cs` — pure: rotation decision + rename plan
- `LogEntry.cs` — `internal readonly struct { string FileName; string Line; }`
- `LogWriter.cs` — background single-consumer writer
- `LogChannel.cs` — per-file handle
- `Log.cs` — static facade + `Init`/`Shutdown`
Tests under `tests/MSFSBlindAssist.Tests/`: `LogFormatterTests.cs`, `LogRotatorTests.cs`, `LogWriterTests.cs`.

---

### Task 1: LogLevel + LogEntry + LogFormatter (pure, TDD)

**Files:**
- Create: `MSFSBlindAssist/Utils/Logging/LogLevel.cs`, `LogEntry.cs`, `LogFormatter.cs`
- Test: `tests/MSFSBlindAssist.Tests/LogFormatterTests.cs`

**Interfaces produced:**
- `enum LogLevel { Debug, Info, Warn, Error }`
- `internal readonly struct LogEntry { public readonly string FileName; public readonly string Line; public LogEntry(string fileName, string line); }`
- `static class LogFormatter { static string Format(DateTime tsLocal, LogLevel level, string category, string message, Exception? ex = null); static string LevelTag(LogLevel level); }`

- [ ] **Step 1: Write the failing tests** `tests/MSFSBlindAssist.Tests/LogFormatterTests.cs`:

```csharp
using System;
using MSFSBlindAssist.Utils.Logging;
using Xunit;

public class LogFormatterTests
{
    static readonly DateTime T = new(2026, 7, 7, 14, 3, 12, 417, DateTimeKind.Local);

    [Theory]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Info,  "INFO ")]
    [InlineData(LogLevel.Warn,  "WARN ")]
    [InlineData(LogLevel.Error, "ERROR")]
    public void LevelTag_is_padded_to_five(LogLevel level, string expected)
        => Assert.Equal(expected, LogFormatter.LevelTag(level));

    [Fact]
    public void Format_produces_exact_line()
        => Assert.Equal("2026-07-07 14:03:12.417 [INFO ] [Docking] docked",
                        LogFormatter.Format(T, LogLevel.Info, "Docking", "docked"));

    [Fact]
    public void Empty_category_renders_as_dash()
        => Assert.Equal("2026-07-07 14:03:12.417 [DEBUG] [-] hi",
                        LogFormatter.Format(T, LogLevel.Debug, "", "hi"));

    [Fact]
    public void Exception_is_appended_indented()
    {
        var ex = new InvalidOperationException("boom");
        string line = LogFormatter.Format(T, LogLevel.Error, "X", "failed", ex);
        Assert.StartsWith("2026-07-07 14:03:12.417 [ERROR] [X] failed", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains(Environment.NewLine + "    ", line); // indented continuation
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (types not defined): `dotnet test tests/MSFSBlindAssist.Tests --filter FullyQualifiedName~LogFormatterTests -p:Platform=x64`

- [ ] **Step 3: Implement.** `LogLevel.cs`:
```csharp
namespace MSFSBlindAssist.Utils.Logging;

public enum LogLevel { Debug, Info, Warn, Error }
```
`LogEntry.cs`:
```csharp
namespace MSFSBlindAssist.Utils.Logging;

internal readonly struct LogEntry
{
    public readonly string FileName;
    public readonly string Line;
    public LogEntry(string fileName, string line) { FileName = fileName; Line = line; }
}
```
`LogFormatter.cs`:
```csharp
using System;
using System.Globalization;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>Pure formatting of one log line. No I/O, no clock — timestamp is passed in so it is deterministically testable.</summary>
public static class LogFormatter
{
    public static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info  => "INFO ",
        LogLevel.Warn  => "WARN ",
        LogLevel.Error => "ERROR",
        _              => "?????",
    };

    public static string Format(DateTime tsLocal, LogLevel level, string category, string message, Exception? ex = null)
    {
        string ts  = tsLocal.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string cat = string.IsNullOrEmpty(category) ? "-" : category;
        string line = $"{ts} [{LevelTag(level)}] [{cat}] {message}";
        if (ex != null)
            line += Environment.NewLine + "    " + ex.ToString().Replace("\n", "\n    ");
        return line;
    }
}
```

- [ ] **Step 4: Run — expect PASS.** Same command as Step 2.
- [ ] **Step 5: Commit** `git add MSFSBlindAssist/Utils/Logging/ tests/MSFSBlindAssist.Tests/LogFormatterTests.cs && git commit -m "feat(logging): LogLevel + LogEntry + pure LogFormatter"`

---

### Task 2: LogRotator (pure, TDD)

**Files:**
- Create: `MSFSBlindAssist/Utils/Logging/LogRotator.cs`
- Test: `tests/MSFSBlindAssist.Tests/LogRotatorTests.cs`

**Interfaces produced:**
- `static class LogRotator { static bool ShouldRotate(long sizeBytes, long capBytes); static string RotatedName(string basePath, int index); static RotationPlan Plan(string basePath, int retention); }`
- `sealed record RotationPlan(string DeleteFirst, System.Collections.Generic.IReadOnlyList<(string From, string To)> Moves);`

- [ ] **Step 1: Failing tests** `LogRotatorTests.cs`:
```csharp
using MSFSBlindAssist.Utils.Logging;
using Xunit;

public class LogRotatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(5*1024*1024 - 1, false)]
    [InlineData(5*1024*1024, true)]
    [InlineData(6*1024*1024, true)]
    public void ShouldRotate_at_cap(long size, bool expected)
        => Assert.Equal(expected, LogRotator.ShouldRotate(size, 5*1024*1024));

    [Theory]
    [InlineData(@"C:\logs\taxi_guidance.log", 1, @"C:\logs\taxi_guidance.1.log")]
    [InlineData(@"C:\logs\taxi_guidance.log", 3, @"C:\logs\taxi_guidance.3.log")]
    [InlineData(@"C:\logs\input_events.txt", 2, @"C:\logs\input_events.2.txt")]
    public void RotatedName_inserts_index_before_extension(string basePath, int i, string expected)
        => Assert.Equal(expected, LogRotator.RotatedName(basePath, i));

    [Fact]
    public void Plan_deletes_oldest_then_shifts_down()
    {
        var plan = LogRotator.Plan(@"C:\logs\d.log", 3);
        Assert.Equal(@"C:\logs\d.3.log", plan.DeleteFirst);
        Assert.Equal(new[]{ (@"C:\logs\d.2.log", @"C:\logs\d.3.log"),
                            (@"C:\logs\d.1.log", @"C:\logs\d.2.log"),
                            (@"C:\logs\d.log",   @"C:\logs\d.1.log") },
                     plan.Moves);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.** `dotnet test tests/MSFSBlindAssist.Tests --filter FullyQualifiedName~LogRotatorTests -p:Platform=x64`
- [ ] **Step 3: Implement** `LogRotator.cs`:
```csharp
using System.Collections.Generic;
using System.IO;

namespace MSFSBlindAssist.Utils.Logging;

public sealed record RotationPlan(string DeleteFirst, IReadOnlyList<(string From, string To)> Moves);

/// <summary>Pure rotation policy. No file I/O — the LogWriter applies the plan, skipping any From that doesn't exist.</summary>
public static class LogRotator
{
    public static bool ShouldRotate(long sizeBytes, long capBytes) => sizeBytes >= capBytes;

    public static string RotatedName(string basePath, int index)
    {
        string dir  = Path.GetDirectoryName(basePath) ?? "";
        string name = Path.GetFileNameWithoutExtension(basePath);
        string ext  = Path.GetExtension(basePath); // includes leading dot
        return Path.Combine(dir, $"{name}.{index}{ext}");
    }

    public static RotationPlan Plan(string basePath, int retention)
    {
        var moves = new List<(string, string)>();
        for (int i = retention - 1; i >= 1; i--)
            moves.Add((RotatedName(basePath, i), RotatedName(basePath, i + 1)));
        moves.Add((basePath, RotatedName(basePath, 1)));
        return new RotationPlan(RotatedName(basePath, retention), moves);
    }
}
```
- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** `git commit -am "feat(logging): pure LogRotator (size cap + rename plan)"`

---

### Task 3: LogWriter (background single-consumer, integration TDD)

**Files:**
- Create: `MSFSBlindAssist/Utils/Logging/LogWriter.cs`
- Test: `tests/MSFSBlindAssist.Tests/LogWriterTests.cs`

**Interfaces produced (internal — needs `InternalsVisibleTo`):**
- `internal sealed class LogWriter { LogWriter(Func<string,string> resolvePath, int capacity=10000, long capBytes=5*1024*1024, int retention=3); void Start(); void Enqueue(in LogEntry e); void RegisterTruncateOnLaunch(string fileName); void Shutdown(TimeSpan timeout); void DrainSynchronously(); long Dropped {get;} }`

**Note:** `LogWriter`/`LogEntry` are `internal`. Add `[assembly: InternalsVisibleTo("MSFSBlindAssist.Tests")]` — create `MSFSBlindAssist/Properties/InternalsVisibleTo.cs` if absent:
```csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("MSFSBlindAssist.Tests")]
```

- [ ] **Step 1: Failing tests** `LogWriterTests.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Utils.Logging;
using Xunit;

public class LogWriterTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-logtest-" + Guid.NewGuid().ToString("N"));
    public LogWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    string Path4(string f) => Path.Combine(_dir, f);

    [Fact]
    public void Drains_batch_to_file_in_order()
    {
        var w = new LogWriter(Path4); w.Start();
        for (int i = 0; i < 100; i++) w.Enqueue(new LogEntry("a.log", "line" + i));
        w.Shutdown(TimeSpan.FromSeconds(5));
        var lines = File.ReadAllLines(Path4("a.log"));
        Assert.Equal(100, lines.Length);
        Assert.Equal("line0", lines[0]);
        Assert.Equal("line99", lines[99]);
    }

    [Fact]
    public void Concurrent_writers_never_interleave_a_line() // the corruption-bug regression test
    {
        var w = new LogWriter(Path4); w.Start();
        const int threads = 8, per = 500;
        Parallel.For(0, threads, t => { for (int i = 0; i < per; i++) w.Enqueue(new LogEntry("c.log", $"T{t}-{i}-PAYLOAD")); });
        w.Shutdown(TimeSpan.FromSeconds(10));
        var lines = File.ReadAllLines(Path4("c.log"));
        Assert.Equal(threads * per, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^T\d+-\d+-PAYLOAD$", l)); // every line intact, none spliced
    }

    [Fact]
    public void Rotates_at_cap_with_retention()
    {
        var w = new LogWriter(Path4, capBytes: 200, retention: 2); w.Start();
        for (int i = 0; i < 60; i++) w.Enqueue(new LogEntry("r.log", new string('x', 40))); // 60*~42B >> 200B
        w.Shutdown(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(Path4("r.log")));
        Assert.True(File.Exists(Path4("r.1.log")));      // rolled at least once
        Assert.False(File.Exists(Path4("r.3.log")));     // retention 2 → never a .3
    }

    [Fact]
    public void Overflow_drops_and_counts_without_blocking()
    {
        var w = new LogWriter(Path4, capacity: 10); // do NOT Start — nothing drains, so it fills
        for (int i = 0; i < 1000; i++) w.Enqueue(new LogEntry("o.log", "x"));
        Assert.True(w.Dropped > 0);
    }

    [Fact]
    public void TruncateOnLaunch_starts_the_file_fresh()
    {
        File.WriteAllText(Path4("s.log"), "STALE" + Environment.NewLine);
        var w = new LogWriter(Path4); w.RegisterTruncateOnLaunch("s.log"); w.Start();
        w.Enqueue(new LogEntry("s.log", "fresh"));
        w.Shutdown(TimeSpan.FromSeconds(5));
        var lines = File.ReadAllLines(Path4("s.log"));
        Assert.Equal(new[]{ "fresh" }, lines); // stale content gone
    }
}
```

- [ ] **Step 2: Run — expect FAIL.** `dotnet test tests/MSFSBlindAssist.Tests --filter FullyQualifiedName~LogWriterTests -p:Platform=x64`
- [ ] **Step 3: Implement** `LogWriter.cs`:
```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>Single-consumer background writer. All file I/O (append + rotate) happens on ONE thread,
/// so there are no locks and no interleaving. Callers only Enqueue (bounded, non-blocking).</summary>
internal sealed class LogWriter
{
    private readonly BlockingCollection<LogEntry> _queue;
    private readonly Func<string, string> _resolvePath;
    private readonly long _capBytes;
    private readonly int _retention;
    private readonly Thread _thread;
    private readonly HashSet<string> _truncateOnLaunch = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _freshThisSession = new(StringComparer.OrdinalIgnoreCase);
    private long _dropped;

    public long Dropped => Interlocked.Read(ref _dropped);

    public LogWriter(Func<string, string> resolvePath, int capacity = 10000, long capBytes = 5 * 1024 * 1024, int retention = 3)
    {
        _resolvePath = resolvePath;
        _capBytes = capBytes;
        _retention = retention;
        _queue = new BlockingCollection<LogEntry>(capacity);
        _thread = new Thread(RunLoop) { IsBackground = true, Name = "LogWriter" };
    }

    public void RegisterTruncateOnLaunch(string fileName) => _truncateOnLaunch.Add(fileName);
    public void Start() => _thread.Start();
    public void Enqueue(in LogEntry e) { if (!_queue.TryAdd(e)) Interlocked.Increment(ref _dropped); }

    public void Shutdown(TimeSpan timeout)
    {
        try { _queue.CompleteAdding(); } catch { }
        if (_thread.IsAlive) _thread.Join(timeout);
    }

    /// <summary>Best-effort synchronous drain for crash/exit hooks (writer thread may be dead).</summary>
    public void DrainSynchronously()
    {
        var batch = new List<LogEntry>();
        while (_queue.TryTake(out var e)) batch.Add(e);
        if (batch.Count > 0) WriteBatch(batch);
    }

    private void RunLoop()
    {
        var batch = new List<LogEntry>(256);
        try
        {
            foreach (var first in _queue.GetConsumingEnumerable())
            {
                batch.Clear();
                batch.Add(first);
                while (batch.Count < 256 && _queue.TryTake(out var next)) batch.Add(next);
                WriteBatch(batch);
            }
        }
        catch { /* never let the writer thread crash the process */ }
    }

    private void WriteBatch(List<LogEntry> batch)
    {
        var byFile = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in batch)
        {
            if (!byFile.TryGetValue(e.FileName, out var sb)) byFile[e.FileName] = sb = new StringBuilder();
            sb.Append(e.Line).Append(Environment.NewLine);
        }

        long dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            string note = LogFormatter.Format(DateTime.Now, LogLevel.Warn, "Log", $"{dropped} log entries dropped (queue saturated)");
            if (!byFile.TryGetValue("debug.log", out var dsb)) byFile["debug.log"] = dsb = new StringBuilder();
            dsb.Append(note).Append(Environment.NewLine);
        }

        foreach (var kv in byFile)
        {
            try
            {
                string path = _resolvePath(kv.Key);
                if (_truncateOnLaunch.Contains(kv.Key) && _freshThisSession.Add(kv.Key))
                {
                    File.WriteAllText(path, kv.Value.ToString());
                    continue;
                }
                RotateIfNeeded(path);
                File.AppendAllText(path, kv.Value.ToString());
            }
            catch { /* disk full / locked — drop this file's batch, never throw */ }
        }
    }

    private void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path) || !LogRotator.ShouldRotate(new FileInfo(path).Length, _capBytes)) return;
            var plan = LogRotator.Plan(path, _retention);
            if (File.Exists(plan.DeleteFirst)) File.Delete(plan.DeleteFirst);
            foreach (var (from, to) in plan.Moves)
                if (File.Exists(from)) File.Move(from, to, overwrite: true);
        }
        catch { /* rotation is best-effort; keep appending */ }
    }
}
```
- [ ] **Step 4: Run — expect PASS** (all 5 LogWriter tests). Run the full suite once too: `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64`.
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(logging): background single-consumer LogWriter + InternalsVisibleTo"`

---

### Task 4: LogChannel + Log facade + Program.cs wiring

**Files:**
- Create: `MSFSBlindAssist/Utils/Logging/LogChannel.cs`, `MSFSBlindAssist/Utils/Logging/Log.cs`
- Modify: `MSFSBlindAssist/Program.cs` (Init early, Shutdown in finally, extend the existing exception hooks)

**Interfaces produced:**
- `static class Log { void Init(); void Shutdown(); void Debug/Info/Warn(string category,string message); void Error(string category,string message,Exception? ex=null); LogChannel Channel(string name, bool truncateOnLaunch=false); }`
- `sealed class LogChannel { void Debug/Info/Warn(string message); void Error(string message, Exception? ex=null); }`

- [ ] **Step 1: Implement** `LogChannel.cs`:
```csharp
using System;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>A handle to one named log file (e.g. "taxi_guidance.log"). The channel's base name is its category tag.</summary>
public sealed class LogChannel
{
    private readonly string _fileName; // e.g. "taxi_guidance.log"
    private readonly string _category; // e.g. "taxi_guidance"
    internal LogChannel(string fileName, string category) { _fileName = fileName; _category = category; }

    public void Debug(string message)                 => Log.Emit(_fileName, LogLevel.Debug, _category, message, null);
    public void Info(string message)                  => Log.Emit(_fileName, LogLevel.Info,  _category, message, null);
    public void Warn(string message)                  => Log.Emit(_fileName, LogLevel.Warn,  _category, message, null);
    public void Error(string message, Exception? ex = null) => Log.Emit(_fileName, LogLevel.Error, _category, message, ex);
}
```
`Log.cs` — note the writer is created in the static initializer so `Enqueue` works before `Init()` starts the thread (handles pre-Init calls):
```csharp
using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>The single logging entry point. Log.Debug/Info/Warn/Error write the app-wide debug.log;
/// Log.Channel(name) returns a handle to a dedicated named file. Formatting+enqueue only — one
/// background thread does all disk I/O, so callers never block and logging never adds hot-path latency.</summary>
public static class Log
{
    private const string DebugFile = "debug.log";
    // Created eagerly so calls BEFORE Init() still enqueue; Init() starts the drain thread.
    private static readonly LogWriter Writer = new(MSFSBlindAssist.Utils.AppLogs.PathFor);
    private static readonly ConcurrentDictionary<string, LogChannel> Channels = new(StringComparer.OrdinalIgnoreCase);
    private static int _started;

    public static void Init()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;
        Writer.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Writer.DrainSynchronously();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Writer.DrainSynchronously();
    }

    public static void Shutdown() => Writer.Shutdown(TimeSpan.FromSeconds(2));

    public static void Debug(string category, string message) { System.Diagnostics.Debug.WriteLine(message); Emit(DebugFile, LogLevel.Debug, category, message, null); }
    public static void Info (string category, string message) => Emit(DebugFile, LogLevel.Info,  category, message, null);
    public static void Warn (string category, string message) => Emit(DebugFile, LogLevel.Warn,  category, message, null);
    public static void Error(string category, string message, Exception? ex = null) => Emit(DebugFile, LogLevel.Error, category, message, ex);

    /// <summary>Handle to a dedicated named file. `name` may be given with or without ".log"; a bare name gets ".log".</summary>
    public static LogChannel Channel(string name, bool truncateOnLaunch = false)
        => Channels.GetOrAdd(name, n =>
        {
            bool hasExt = n.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            string file = hasExt ? n : n + ".log";
            string category = System.IO.Path.GetFileNameWithoutExtension(file);
            if (truncateOnLaunch) Writer.RegisterTruncateOnLaunch(file);
            return new LogChannel(file, category);
        });

    internal static void Emit(string fileName, LogLevel level, string category, string message, Exception? ex)
        => Writer.Enqueue(new LogEntry(fileName, LogFormatter.Format(DateTime.Now, level, category, message, ex)));
}
```

- [ ] **Step 2: Wire into `Program.cs`.**
  - Add `using MSFSBlindAssist.Utils.Logging;` at the top.
  - As the **first statement inside `Main`'s `try`** (before the `StartupLogger.Log(...)` header lines), add: `Log.Init();`
  - Add a `finally` to `Main`'s try/catch (currently there is only `catch`) that calls `Log.Shutdown();`:
    ```csharp
    catch (Exception ex) { /* existing body unchanged */ }
    finally { Log.Shutdown(); }
    ```
  - Do NOT alter the existing `InstallGlobalExceptionHandlers` StartupLogger calls in this task (StartupLogger is migrated in Task 6). The `Log.Init()` hooks already add a best-effort drain on `UnhandledException`/`ProcessExit`.

- [ ] **Step 3: Build + test.** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` → 0 warn/0 err; `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all green (214 + the new logging tests).
- [ ] **Step 4: Smoke the facade** with a throwaway test asserting `Log.Channel("probe").Info("hi")` then `Log.Shutdown()` produces a `debug.log`/`probe.log` under `%APPDATA%\MSFSBlindAssist\logs` with a correctly-formatted line, then delete that test. (Optional — skip if the LogWriter integration tests already cover it.)
- [ ] **Step 5: Commit** `git add -A && git commit -m "feat(logging): Log facade + LogChannel; wire Init/Shutdown into Program.Main"`

---

### Task 5: Migrate named-file writers → channels (the corruption fix)

**Files (all the `AppLogs.PathFor(...)` + `File.AppendAllText/WriteAllText` diagnostic writers):**
`Services/DockingGuidanceManager.cs`, `Services/LandingExitPlanner.cs`, `Services/TakeoffAssistManager.cs`, `Services/TaxiGuidanceManager.cs`, `Services/Gsx/GsxGateSelector.cs`, `Services/TaxiAugment/AugmentingAirportDataProvider.cs`, `Navigation/TaxiRouter.cs`, `Forms/TaxiAssistForm.cs`, `MainForm.cs`, `MainForm.AircraftSwitch.cs`, `MainForm.Announcers.cs`, `SimConnect/SimConnectManager.Dispatch.cs`, `SimConnect/SimConnectManager.Setup.cs`.

**Procedure (per writer):**
- [ ] **Step 1: Enumerate** every diagnostic file-write: `grep -rnE 'AppLogs\.PathFor|File\.AppendAllText|File\.WriteAllText' MSFSBlindAssist --include=*.cs` (exclude the `*_Checklist.txt`/`*_Hotkeys.txt`/`bridge-version.txt` feature outputs and `AppLogs.cs`/`StartupLogger.cs` themselves).
- [ ] **Step 2:** For each subsystem, replace the `static readonly string XLogPath = AppLogs.PathFor("name.log")` field + its `File.AppendAllText(XLogPath, line)` calls with a channel: `private static readonly LogChannel _log = Log.Channel("name");` and `_log.Info(message)` (or `.Warn`/`.Error`). **Preserve the message text**; drop any hand-rolled timestamp prefix in the message (the formatter adds one) and any manual `Environment.NewLine` (the writer adds it). Multiple files writing the SAME log (e.g. `docking-aircraft.log` from TaxiAssistForm + MainForm.AircraftSwitch + SimConnectManager.Dispatch) each call `Log.Channel("docking-aircraft")` — now serialized through the one writer thread (this is the corruption fix).
- [ ] **Step 3:** `input_events.txt` (SimConnectManager.Setup.cs) and `registration.log` become channels the same way (`Log.Channel("input_events.txt")`, `Log.Channel("registration")`).
- [ ] **Step 4: Build + test** after each 2-3 files: 0 warn/0 err; 214+ green.
- [ ] **Step 5: Commit** in logical batches, e.g. `git commit -am "refactor(logging): route taxi/docking/gsx diagnostic logs through Log channels (fixes multi-writer interleaving)"`

---

### Task 6: StartupLogger → startup channel; remove the class

**Files:** Modify `Program.cs` (all `StartupLogger.*` call sites), delete `MSFSBlindAssist/Utils/StartupLogger.cs`, update any other `StartupLogger` references.

- [ ] **Step 1:** `grep -rn 'StartupLogger' MSFSBlindAssist --include=*.cs` — enumerate all call sites (Program.cs has the bulk; the exception handlers use `StartupLogger.LogError`).
- [ ] **Step 2:** Add near the top of `Program.cs`: `private static readonly LogChannel Startup = Log.Channel("startup", truncateOnLaunch: true);` (after `Log.Init()` sets up the writer; `Channel` is safe to call anytime).
- [ ] **Step 3:** Replace: `StartupLogger.Log(x)` → `Startup.Info(x)`; `StartupLogger.LogError(x, ex)` → `Startup.Error(x, ex)`; `StartupLogger.LogSystemInfo()` → move its body into a local method that emits the same lines via `Startup.Info(...)`; `StartupLogger.GetLogFilePath()` → `MSFSBlindAssist.Utils.AppLogs.PathFor("startup.log")` (used in the MessageBox strings). Preserve every startup log line's message text.
- [ ] **Step 4:** Delete `Utils/StartupLogger.cs` (`git rm`). Build → 0 warn/0 err (no dangling references).
- [ ] **Step 5: Test + commit** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` green; `git add -A && git commit -m "refactor(logging): re-home StartupLogger onto the startup channel; remove the class"`

---

### Task 7: Migrate `Debug.WriteLine` → `Log.Debug(category, ...)` (4 area batches)

For each batch: replace `Debug.WriteLine($"...")` / `Debug.WriteLine("...")` with `Log.Debug("<Category>", $"...")`. Category = the subsystem (below). Add `using MSFSBlindAssist.Utils.Logging;` to each touched file if absent. **Preserve the message string exactly.** Leave `System.Diagnostics.Debug.WriteLine` calls that are NOT diagnostics alone only if any exist (there are none expected — all are traces). Build + test after each batch; commit per batch.

- [ ] **Task 7a — SimConnect (315 calls):** all `.cs` under `MSFSBlindAssist/SimConnect/`. Category `"SimConnect"`. Build 0 warn, tests green, commit `refactor(logging): migrate SimConnect Debug.WriteLine to Log.Debug`.
- [ ] **Task 7b — Services (223 calls):** all `.cs` under `MSFSBlindAssist/Services/` (incl. `Gsx/`, `TaxiAugment/`). Category = the class's subsystem where obvious (`"Docking"`, `"Taxi"`, `"Gsx"`, `"TakeoffAssist"`), else `"Services"`. Commit similarly.
- [ ] **Task 7c — Accessibility + MainForm (66 + 63 calls):** `MSFSBlindAssist/Accessibility/*.cs` (category `"Accessibility"`) and `MainForm*.cs` (category `"MainForm"`). **Do NOT touch `Console.WriteLine` in `ScreenReaderAnnouncer.cs`** (screen-reader channel — out of scope). Commit.
- [ ] **Task 7d — remaining (Aircraft 41, Database 21, Patching 16, Hotkeys 16, Forms 13, Utils 5):** categories `"Aircraft"`/the aircraft code, `"Database"`, `"Patching"`, `"Hotkeys"`, `"Forms"`, `"Utils"`. Commit.
- [ ] **After all four:** `grep -rc 'Debug\.WriteLine' MSFSBlindAssist --include=*.cs | awk -F: '{s+=$2} END{print s}'` → expect ~0 remaining (any left must be deliberately-kept; note them). Full suite green.

---

### Task 8: Docs — update CLAUDE.md logging section

**Files:** Modify `CLAUDE.md` (the "Diagnostic Logs — ONE folder, via `Utils/AppLogs`" section + the related Invariants bullet), and the logging note in `docs/architecture.md` if present.

- [ ] **Step 1:** Update the Diagnostic Logs section to state: **`Utils/Logging/Log` is the single entry point** (`Log.Debug/Info/Warn/Error(category, msg)` → `debug.log`; `Log.Channel("name")` → named file); `AppLogs` remains the path authority; the uniform format `yyyy-MM-dd HH:mm:ss.fff [LEVEL] [category] message`; async single-writer (no hand-rolled `File.AppendAllText` for logs — route through `Log`); size-rotation (5 MB × 3); the new `debug.log` captures all former `Debug.WriteLine` trace in Release.
- [ ] **Step 2:** Add/adjust the Invariants one-liner: "Never hand-build a log write — every diagnostic log goes through `Utils/Logging/Log` (channel or Debug/Info/Warn/Error); `AppLogs.PathFor` is the path layer only. → CLAUDE.md". Keep the existing "one folder / `%APPDATA%\MSFSBlindAssist\logs`" guidance.
- [ ] **Step 3: Commit** `git commit -am "docs: document the Log facade as the single logging entry point"`

---

### Task 9: Final verification, Release build, PR

- [ ] **Step 1: Clean full build** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64 --no-incremental` → **0 Warning(s), 0 Error(s)**.
- [ ] **Step 2: Full test suite** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → all green (214 + new logging tests), output pristine.
- [ ] **Step 3: Confirm migration completeness:** `grep -rc 'Debug\.WriteLine' MSFSBlindAssist --include=*.cs` sums to ~0; no `static readonly string \w*LogPath` diagnostic-write idiom remains (`grep -rnE 'File\.AppendAllText\(.*AppLogs'`); `StartupLogger.cs` gone.
- [ ] **Step 4: Release build (the deliverable to test)** `dotnet build MSFSBlindAssist.sln -c Release -p:Platform=x64` → Build succeeded; verify the exe timestamp in `MSFSBlindAssist\bin\x64\Release\net10.0-windows\MSFSBlindAssist.exe` is current.
- [ ] **Step 5: Push + PR** (controller does this): push `feat/logging-infrastructure`, open a PR to `main` on `origin` with the summary, verification results, the Console.WriteLine/NVDA out-of-scope note, and an in-sim smoke-test plan (each named log still populates; `debug.log` appears and fills; audio guidance unaffected; app starts/stops cleanly with logs flushed).

---

## Self-review notes (author)

- **Spec coverage:** facade (T1-4), corruption fix + named-file standardization (T5), StartupLogger (T6), Debug.WriteLine capture (T7a-d), rotation (T2/T3), crash-flush (T4), docs (T8), Release build (T9). All spec sections mapped.
- **Scope change from spec:** `Console.WriteLine` cleanup is **dropped** — `Program.cs` `AllocConsole()`s a hidden console "for NVDA to monitor," so console output may be an intentional screen-reader channel; touching it risks a blind user's accessibility. Documented in Global Constraints + T9's PR note.
- **Type consistency:** `LogFormatter.Format`/`LevelTag`, `LogRotator.Plan`/`RotatedName`/`ShouldRotate`, `RotationPlan(DeleteFirst, Moves)`, `LogWriter(resolvePath, capacity, capBytes, retention)`/`Enqueue`/`RegisterTruncateOnLaunch`/`Shutdown`/`DrainSynchronously`/`Dropped`, `Log.Emit`/`Channel`/`Debug/Info/Warn/Error`, `LogChannel.Debug/Info/Warn/Error` — consistent across tasks.
- **No placeholders:** all component code is complete; migration tasks carry exact grep/transform procedures with the real file lists.
