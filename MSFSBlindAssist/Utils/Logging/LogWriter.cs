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
    // Serializes the actual file-I/O/rotation path so the normal background RunLoop drain and the rare
    // synchronous crash/exit DrainSynchronously (called from AppDomain hooks while the writer thread may
    // still be alive after a Join timeout) can NEVER interleave a file write. Only ever held on the write
    // path — never on the Enqueue hot path, which stays lock-free (BlockingCollection.TryAdd).
    private readonly object _ioLock = new();
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

    public void RegisterTruncateOnLaunch(string fileName) { lock (_ioLock) _truncateOnLaunch.Add(fileName); }
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
        // The ENTIRE file-I/O/rotation path is serialized: the background RunLoop is the normal single
        // writer, but DrainSynchronously (crash/exit hook) can call this concurrently if the writer thread
        // outlived Shutdown's Join. This lock guarantees they never interleave a file write, and also guards
        // _truncateOnLaunch/_freshThisSession (touched inside WriteFile). Enqueue never takes this lock.
        lock (_ioLock)
        {
        // Group by file, preserving arrival order (order matters for the ordering test).
        var byFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var e in batch)
        {
            if (!byFile.TryGetValue(e.FileName, out var lines))
            {
                lines = new List<string>();
                byFile[e.FileName] = lines;
                order.Add(e.FileName);
            }
            lines.Add(e.Line);
        }

        long dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            string note = LogFormatter.Format(DateTime.Now, LogLevel.Warn, "Log", $"{dropped} log entries dropped (queue saturated)");
            if (!byFile.TryGetValue("debug.log", out var dlines))
            {
                dlines = new List<string>();
                byFile["debug.log"] = dlines;
                order.Add("debug.log");
            }
            dlines.Add(note);
        }

        foreach (var fileName in order)
            WriteFile(fileName, byFile[fileName]);
        }
    }

    /// <summary>
    /// Writes one file's lines from this batch. Flushes in chunks no larger than <see cref="_capBytes"/>
    /// (rather than joining the whole batch into one write) so a rotation boundary crossed WITHIN a single
    /// large consumed batch is still caught — a giant batch that already exceeds the cap by the time it's
    /// dequeued must not silently skip rotation just because it all arrived in one BlockingCollection drain.
    /// </summary>
    private void WriteFile(string fileName, List<string> lines)
    {
        try
        {
            string path = _resolvePath(fileName);
            int start = 0;
            if (_truncateOnLaunch.Contains(fileName) && _freshThisSession.Add(fileName))
            {
                File.WriteAllText(path, lines[0] + Environment.NewLine);
                start = 1;
            }

            var sb = new StringBuilder();
            for (int i = start; i < lines.Count; i++)
            {
                sb.Append(lines[i]).Append(Environment.NewLine);
                if (sb.Length >= _capBytes)
                    FlushChunk(path, sb);
            }
            if (sb.Length > 0) FlushChunk(path, sb);
        }
        catch { /* disk full / locked — drop this file's batch, never throw */ }
    }

    private void FlushChunk(string path, StringBuilder sb)
    {
        RotateIfNeeded(path);
        File.AppendAllText(path, sb.ToString());
        sb.Clear();
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
