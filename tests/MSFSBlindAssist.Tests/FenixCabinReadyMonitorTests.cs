// Characterization tests for the app-wide cabin-ready call (plan 08 Task 6): FenixCabinReadyEdge, the
// known false-to-true change after a silent baseline, and FenixCabinReadyMonitor, which speaks it once on
// the UI thread. The monitor is built with fakes: a source that captures the value callback and counts its
// disposal, a one-off read, and a SynchronizationContext that queues posts until RunAll(). No test builds a
// FenixDataService or a FenixDataPoller (a live start would open a socket to 127.0.0.1:8083), so the
// definition's wiring is checked by reading FenixA320Definition.Cabin.cs.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinReadyMonitorTests
{
    private const string CabinReady = "aircraft.communication.cids.cabinReady";
    private const string CabinReadyAvailable = "aircraft.communication.cids.cabinReady.available";

    private static readonly FenixDataValue True = new(FenixValueKind.Bool, 0, true, null, "true");
    private static readonly FenixDataValue False = new(FenixValueKind.Bool, 0, false, null, "false");
    private static readonly FenixDataValue Unknown = FenixDataValue.Unknown;
    private static readonly FenixDataValue JsonNull = new(FenixValueKind.Null, 0, false, null, "null");
    private static readonly FenixDataValue NumberOne = new(FenixValueKind.Number, 1, false, null, "1");

    // ---- Fakes ----------------------------------------------------------------------------------

    /// <summary>A UI thread that queues each post and runs the queue on <see cref="RunAll"/>.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int Pending { get { lock (_posted) return _posted.Count; } }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted) _posted.Add((d, state));
        }

        public void RunAll()
        {
            List<(SendOrPostCallback Callback, object? State)> batch;
            lock (_posted)
            {
                batch = new(_posted);
                _posted.Clear();
            }
            foreach (var (callback, state) in batch) callback(state);
        }
    }

    /// <summary>A UI thread that is gone: WinForms' BeginInvoke once the window handle is destroyed.</summary>
    private sealed class ClosedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException(
                "Invoke or BeginInvoke cannot be called on a control until the window handle has been created.");
    }

    /// <summary>Stands in for the poller: captures the value callback and counts disposals.</summary>
    private sealed class FakeSource
    {
        private int _disposed;

        public Action<string, FenixDataValue>? Callback { get; private set; }
        public int Disposed => Volatile.Read(ref _disposed);

        public IDisposable Subscribe(Action<string, FenixDataValue> onValue)
        {
            Callback = onValue;
            return new Handle(this);
        }

        public void Send(string name, FenixDataValue value) => Callback!(name, value);

        private sealed class Handle(FakeSource owner) : IDisposable
        {
            public void Dispose() => Interlocked.Increment(ref owner._disposed);
        }
    }

    private sealed class Spoken
    {
        private readonly List<string> _texts = new();

        public void Add(string text)
        {
            lock (_texts) _texts.Add(text);
        }

        public IReadOnlyList<string> Texts
        {
            get { lock (_texts) return _texts.ToArray(); }
        }
    }

    private static Func<Task<FenixDataValue>> Reads(FenixDataValue value) => () => Task.FromResult(value);

    private static bool[] Run(params FenixDataValue[] values)
    {
        var edge = new FenixCabinReadyEdge();
        return values.Select(edge.Next).ToArray();
    }

    // ---- The edge -------------------------------------------------------------------------------

    [Fact]
    public void Edge_speaks_only_on_a_known_rise()
    {
        Assert.Equal([false, true], Run(False, True));
        // A cabin already ready at the first value is the silent baseline.
        Assert.Equal([false], Run(True));
        Assert.Equal([false, false, true], Run(True, False, True));
        // An unknown value between keeps the known false.
        Assert.Equal([false, false, true], Run(False, Unknown, True));
        // Unknown is no baseline: the first Bool is, and says nothing.
        Assert.Equal([false, false], Run(Unknown, True));
        Assert.Equal([false, false], Run(JsonNull, True));
        // A number is not a Bool: ignored, neither a rise nor a baseline.
        Assert.Equal([false, false, true], Run(False, NumberOne, True));
        Assert.Equal([false, false], Run(NumberOne, True));
        // Steady true never repeats.
        Assert.Equal([false, true, false, false], Run(False, True, True, True));
    }

    [Fact]
    public void Seed_sets_the_baseline_once()
    {
        var edge = new FenixCabinReadyEdge();
        edge.Seed(True);
        Assert.False(edge.Next(True));

        edge = new FenixCabinReadyEdge();
        edge.Seed(False);
        Assert.True(edge.Next(True));

        // A value before the seed is the baseline; the seed is ignored then.
        edge = new FenixCabinReadyEdge();
        Assert.False(edge.Next(False));
        edge.Seed(True);
        Assert.True(edge.Next(True));

        // Only the first Bool seed counts.
        edge = new FenixCabinReadyEdge();
        edge.Seed(True);
        edge.Seed(False);
        Assert.False(edge.Next(True));

        // An unknown seed sets nothing: the first value is the silent baseline.
        edge = new FenixCabinReadyEdge();
        edge.Seed(Unknown);
        Assert.False(edge.Next(True));
    }

    // ---- The monitor ----------------------------------------------------------------------------

    [Fact]
    public async Task Speaks_on_the_UI_context()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(False), ui, spoken.Add);

        await Task.Run(() => source.Send(CabinReady, True));

        Assert.Empty(spoken.Texts);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Speaks_once_per_rise_and_never_at_connect()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        // Already ready when the app connects: the seed is the silent baseline.
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(True), ui, spoken.Add);

        source.Send(CabinReady, True);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public async Task A_value_before_the_seed_is_the_baseline()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var order = new List<string>();
        var seed = new TaskCompletionSource<FenixDataValue>();
        using var monitor = new FenixCabinReadyMonitor(
            onValue => { order.Add("subscribe"); return source.Subscribe(onValue); },
            () => { order.Add("read"); return seed.Task; },
            ui,
            spoken.Add);

        // The source starts first, then the one-off read.
        Assert.Equal(["subscribe", "read"], order);

        // The source's first value beats the read: it is the silent baseline, and the late seed is ignored.
        source.Send(CabinReady, True);
        // Completed on a pool thread, which has no SynchronizationContext, the read's continuation (the
        // seed) runs inline, so it has landed when Task.Run returns. On the test thread xUnit's context
        // would send it to the pool and the asserts below could run first.
        await Task.Run(() => seed.SetResult(False));
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Ignores_other_names()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(Unknown), ui, spoken.Add);

        source.Send(CabinReadyAvailable, False);
        source.Send(CabinReadyAvailable, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        // The other name's false set no baseline: the first cabin-ready value is the silent one.
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);
        Assert.Equal(0, ui.Pending);
    }

    [Fact]
    public void Dispose_stops_speech_and_disposes_the_subscription()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(False), ui, spoken.Add);

        source.Send(CabinReady, True);
        Assert.Equal(1, ui.Pending);

        monitor.Dispose();
        Assert.Equal(1, source.Disposed);

        // The post queued before Dispose says nothing.
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        // A late value from the source (its own Dispose is best effort) says nothing either.
        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        monitor.Dispose();
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public void A_closed_context_does_not_throw()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(False), new ClosedContext(), spoken.Add);

        var thrown = Record.Exception(() => source.Send(CabinReady, True));

        Assert.Null(thrown);
        Assert.Empty(spoken.Texts);
    }

    [Fact]
    public void Without_a_UI_context_it_speaks_inline()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, Reads(False), null, spoken.Add);

        source.Send(CabinReady, True);

        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Spoken_text_is_cabin_ready()
    {
        Assert.Equal("Cabin ready", FenixCabinReadyMonitor.SpokenText);
    }

    // ---- The definition's seams -----------------------------------------------------------------

    [Fact]
    public void The_definition_starts_and_stops_the_monitor()
    {
        string path = Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.Cabin.cs");
        string source = File.ReadAllText(path);

        string start = MethodBody(source, "partial void StartCabinBackground(");
        Assert.Contains("FenixCabinReadyMonitor.Start(DataService, announcer)", start);

        string stop = MethodBody(source, "partial void StopCabinBackground(");
        Assert.Contains("_cabinReadyMonitor?.Dispose()", stop);
        Assert.Contains("_cabinReadyMonitor = null", stop);
    }

    // The braces of the method whose declaration starts with `declaration`, inclusive.
    private static string MethodBody(string source, string declaration)
    {
        int at = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(at >= 0, $"`{declaration}` is not in FenixA320Definition.Cabin.cs");
        int open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"`{declaration}` has no body");
        Assert.DoesNotContain(";", source[at..open]);

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"`{declaration}` has no closing brace");
        return "";
    }
}
