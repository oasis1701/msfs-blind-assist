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

    [Fact]
    public void Enqueue_after_shutdown_does_not_throw()
    {
        var w = new LogWriter(Path4); w.Start();
        w.Enqueue(new LogEntry("z.log", "before"));
        w.Shutdown(TimeSpan.FromSeconds(5));
        var ex = Record.Exception(() => w.Enqueue(new LogEntry("z.log", "after-shutdown")));
        Assert.Null(ex); // must not throw even though the queue is CompleteAdding
    }
}
