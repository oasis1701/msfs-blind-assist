// The 757's display monitor: the one state machine every BlueBird 757 memory reader drives (the PMDG 737 FMA
// monitor's, made generic over a reader). Driven by a fake reader, environment and samplers with a settable clock
// (100 ms per Tick()): no simulator, no timer, no module memory. A reading is trusted on structure alone (B757-30): the
// sampler read it and it is not corrupt; no simulator value ever judges it.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class Bbs757DisplayMonitorTests : IDisposable
{
    private const string LogChannel = "bbs757-x";
    private const string StartingRow = "X reader starting";
    private const string WaitingRow = "X reader waiting for the simulator";
    private const string UnpoweredRow = "X display not powered";

    private const string StoppedNotFound = "X announcements stopped: aircraft memory not found";
    private const string StoppedUnexpected = "X announcements stopped: unexpected values";
    private const string UnavailableNotFound = "X announcements unavailable: aircraft memory not found";
    private const string Resumed = "X announcements resumed";

    private static FakeReading R(string value) => new(value);

    private static FakeReading Corrupt(string value) => new(value, Corrupt: true);

    private static Task<ModuleAnalysis> OneBuild() =>
        Task.FromResult(new ModuleAnalysis(new[] { FakeEnvironment.Build() }, null));

    /// <summary>The session's analysis when no layout file loaded: no builds, and its (PMDG-worded) reason.</summary>
    private static Func<Task<ModuleAnalysis>> NoBuilds(string sessionFailure) =>
        () => Task.FromResult(new ModuleAnalysis(Array.Empty<ModuleBuild>(), sessionFailure));

    private static Task<ModuleLocation> Found() =>
        Task.FromResult(new ModuleLocation(new[] { FakeEnvironment.Located() }, null));

    private static Task<ModuleLocation> NotFound() =>
        Task.FromResult(new ModuleLocation(Array.Empty<LocatedModule>(), null));

    private static Task<ModuleLocation> NoAccess() =>
        Task.FromResult(new ModuleLocation(Array.Empty<LocatedModule>(), FmaReasons.NoAccess));

    // Every sampler any test's reader made: each must be disposed at most once (the real ones each hold a lease on
    // the one shared simulator handle).
    private readonly List<FakeReader> _readers = new();

    public void Dispose()
    {
        foreach (var reader in _readers)
            foreach (var s in reader.Samplers)
                Assert.True(s.DisposeCount <= 1, $"a sampler was disposed {s.DisposeCount} times");
    }

    private Harness New(params Bbs757SimInput[] inputs)
    {
        var h = new Harness(inputs);
        _readers.Add(h.Reader);
        return h;
    }

    /// <summary>The memory lost while running and not found for 11 s ("stopped" at 10 s), then found again reading
    /// <paramref name="next"/>; returns the sampler the reader runs on again.</summary>
    private static FakeSampler LoseAndFind(Harness h, FakeReading next)
    {
        h.Tick(60); // past the 5 s between searches
        h.Env.Locate = NotFound;
        h.Reader.Samplers.Single(s => !s.Disposed).Fail = true;
        h.Tick(110);
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(next);
        h.Env.Locate = Found;
        return h.RunUntilRunning(100);
    }

    [Fact]
    public void Waits_silently_while_the_simulator_reads_null()
    {
        var h = New();
        h.Env.Simulator = null;
        h.Start();
        h.Tick(300); // 30 s

        Assert.Equal("WaitingForSimulator", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Equal(0, h.Env.LocateCalls);
        Assert.Equal(new[] { WaitingRow }, h.Monitor.View());
        Assert.Equal(300, h.Env.ReadCalls); // asked every tick, though the reader takes no input of its own
        Assert.Empty(h.Env.KeysAsked);
    }

    [Fact]
    public void The_monitor_reads_the_simulator_by_its_readers_inputs()
    {
        // A reader's inputs (S10) are read under their keys every tick; an L:var input is BlueBird's, with no units.
        var h = New(Bbs757SimInput.LVar("X_INPUT")).Start();
        Assert.Equal(new[] { new Bbs757SimInput("X_INPUT", "X_INPUT", SimVarType.LVar, null) }, h.Monitor.SimInputs);
        h.Tick(3);
        Assert.Equal(new[] { "X_INPUT", "X_INPUT", "X_INPUT" }, h.Env.KeysAsked);
    }

    [Fact]
    public void A_reading_that_is_not_corrupt_runs_without_any_simulator_value()
    {
        // The FMA's and the lower EICAS's case: the reader takes no input, so nothing of the simulator's is asked for,
        // and its first reading that is not corrupt runs at once.
        var h = New().Start();
        Assert.Empty(h.Monitor.SimInputs);
        h.TickUntil(() => h.State == "Verifying");
        h.Tick(); // its first reading

        Assert.Equal("Running", h.State);
        Assert.Contains(h.Env.Logged, l => l.Line == "Verifying -> Running: a reading that is not corrupt");
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
        Assert.Empty(h.Env.KeysAsked);

        // Whatever the simulator holds is never looked at.
        h.Env.Simulator = new Bbs757SimValues(new Dictionary<string, double> { ["TURB ENG N2:1"] = double.NaN });
        h.Tick(100);
        Assert.Equal("Running", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Empty(h.Env.KeysAsked);
    }

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Tick(20);

        Assert.Equal("Running", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
        Assert.Equal(new[] { "X: A" }, h.Monitor.RowsWhileRunning());
        Assert.Equal(R("A"), h.Monitor.Latest);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.NotEmpty(h.Env.Logged);
        Assert.All(h.Env.Logged, l => Assert.Equal(LogChannel, l.Channel)); // the reader's own log
    }

    [Fact]
    public void A_change_is_spoken_once_after_300_ms_of_settling()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);

        long t = h.Env.NowMs;
        s.Reading = R("B");
        h.TickAt(t);
        h.TickAt(t + 100);
        h.TickAt(t + 299);
        Assert.Empty(h.Env.Spoken);

        h.TickAt(t + 300);
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken);
        h.Tick(30);
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken); // once
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());
    }

    [Fact]
    public void A_change_that_returns_before_settling_is_not_spoken()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);

        s.Reading = R("B");
        h.Tick(2); // B for 200 ms
        s.Reading = R("A");
        h.Tick(30);

        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
    }

    [Fact]
    public void A_candidate_with_no_reading_that_is_not_corrupt_for_2_s_is_dropped()
    {
        var h = New();
        var one = new FakeLayout("one");
        var two = new FakeLayout("two");
        h.Reader.Layouts = new[] { one, two };
        h.Reader.MakeSampler = (_, layout) => h.Reader.NewSampler(layout == one ? Corrupt("Z") : R("A"));
        h.Start();
        h.TickUntil(() => h.State == "Verifying");
        var first = h.Reader.Samplers[0];

        h.Tick(19); // the first candidate reads only corrupt, but has its 2 s
        Assert.Equal("Verifying", h.State);
        Assert.False(first.Disposed);
        Assert.True(first.Reads >= 19, $"{first.Reads} reads");
        Assert.Equal(new[] { StartingRow }, h.Monitor.View()); // a corrupt reading is never shown

        var running = h.RunUntilRunning(5); // then the next candidate runs
        Assert.Same(h.Reader.Samplers[1], running);
        Assert.True(first.Disposed);
        Assert.Equal(1, h.Env.LocateCalls); // the next candidate, not a new search
        Assert.Contains(h.Env.Logged,
            l => l.Line == "Verifying -> Verifying: no reading that is not corrupt for 2 s; trying the next candidate");
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());

        // Alone, it searches again in the same window, which then runs out with the reason it remembers.
        var alone = New();
        alone.Reader.MakeSampler = (_, _) => alone.Reader.NewSampler(Corrupt("Z"));
        alone.Start();
        alone.Tick(1300); // past the 120 s window
        Assert.True(alone.Env.LocateCalls > 20, $"{alone.Env.LocateCalls} searches");
        Assert.All(alone.Reader.Samplers.SkipLast(1), s => Assert.True(s.Disposed));
        Assert.NotEqual("Running", alone.State);
        Assert.Equal(new[] { "X announcements unavailable: unexpected values" }, alone.Env.Spoken);
    }

    [Fact]
    public void The_first_candidate_that_reads_wins()
    {
        // Two candidates, both reading: the first in queue order runs, the other is disposed unread.
        var one = new FakeLayout("one");
        var two = new FakeLayout("two");
        var h = New();
        h.Reader.Layouts = new[] { one, two };
        h.Reader.MakeSampler = (_, layout) => h.Reader.NewSampler(layout == one ? R("A") : R("B"));
        h.Start();
        var running = h.RunUntilRunning();
        Assert.Same(h.Reader.Samplers[0], running);
        Assert.True(h.Reader.Samplers[1].Disposed);
        Assert.Equal(0, h.Reader.Samplers[1].Reads);
        Assert.Contains(h.Env.Logged, l => l.Line == "Locating -> Verifying: 2 candidate(s) found");
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());

        // The first's read fails: the second runs, from the same search.
        var f = New();
        f.Reader.Layouts = new[] { one, two };
        f.Reader.MakeSampler = (_, layout) =>
        {
            var s = f.Reader.NewSampler(layout == one ? R("A") : R("B"));
            s.Fail = layout == one;
            return s;
        };
        f.Start();
        var second = f.RunUntilRunning();
        Assert.Same(f.Reader.Samplers[1], second);
        Assert.True(f.Reader.Samplers[0].Disposed);
        Assert.Equal(1, f.Env.LocateCalls);
        Assert.Contains(f.Env.Logged, l => l.Line == "Verifying -> Verifying: the memory read failed; trying the next candidate");
        Assert.Equal(new[] { "X: B" }, f.Monitor.View());
        Assert.Empty(f.Env.Spoken);
    }

    [Fact]
    public void A_running_reader_never_stops_for_a_simulator_value()
    {
        // A reader's input (S10, as the upper EICAS's manual thrust flag) is taken, never judged: whatever it reads, the
        // reader runs, shows and speaks as before. Only ten corrupt readings, or a failed read, stop it. (An input with no
        // cached value is the environment's silent wait for the simulator, ReadSimulator null: not this test's.)
        var input = Bbs757SimInput.LVar("X_INPUT");
        var h = New(input).Start();
        var s = h.RunUntilRunning();
        double[] values = { 0, 1, -1, 1e9, double.NaN, double.PositiveInfinity, 0.5, 42 };
        for (int i = 0; i < 600; i++) // a minute
        {
            h.Env.Simulator = new Bbs757SimValues(new Dictionary<string, double> { ["X_INPUT"] = values[i % values.Length] });
            if (i == 300) s.Reading = R("B");
            h.Tick();
            Assert.Equal("Running", h.State);
            Assert.Equal(new[] { i < 300 ? "X: A" : "X: B" }, h.Monitor.View());
            Assert.False(h.Monitor.KeyFallsBackToAi());
        }
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken); // the change, and nothing else

        // Ten corrupt readings stop it...
        s.Reading = Corrupt("Z");
        h.Tick(10);
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(new[] { "X: B", StoppedUnexpected }, h.Env.Spoken);

        // ...and a failed read sends it searching.
        var lost = New(input).Start();
        var t = lost.RunUntilRunning();
        t.Fail = true;
        lost.Tick();
        Assert.Equal("Locating", lost.State);
        Assert.True(t.Disposed);
    }

    [Fact]
    public void Unlit_readings_are_never_spoken_and_lighting_up_waits_10_s()
    {
        var h = New();
        h.Reader.LitNow = true;
        h.Start();
        var s = h.RunUntilRunning(); // lit at its first reading: A is the baseline at once

        h.Reader.LitNow = false; // the display goes dark
        s.Reading = R("B");
        h.Tick(30);
        s.Reading = R("C");
        h.Tick(30);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State); // dark is quiet, never a stop

        long t = h.Env.NowMs; // the next tick reads at t
        h.Reader.LitNow = true;
        s.Reading = R("D");
        h.Tick(30); // t to t+2.9 s
        s.Reading = R("E");
        h.Tick(69); // t+3 s to t+9.8 s
        s.Reading = R("F");
        h.Tick(); // t+9.9 s
        Assert.Empty(h.Env.Spoken);

        Assert.Equal(t + 10_000, h.Env.NowMs);
        s.Reading = R("G");
        h.Tick(); // t+10 s: the baseline, never spoken
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: G" }, h.Monitor.View());

        s.Reading = R("H");
        h.Tick(5);
        Assert.Equal(new[] { "X: H" }, h.Env.Spoken);
    }

    [Fact]
    public void A_sampler_is_given_each_ticks_simulator_values()
    {
        // Where the draw takes a simulator variable, the reader takes the same one (S10): the inputs read this tick
        // reach the sampler, in every state that reads memory.
        var h = New(Bbs757SimInput.LVar("X_INPUT"));
        var seen = new List<Bbs757SimValues>();
        h.Reader.Wrap = inner => new SimRecordingSampler(inner, seen);
        h.Start();
        var states = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            h.Env.Simulator = new Bbs757SimValues(new Dictionary<string, double> { ["X_INPUT"] = i });
            int before = seen.Count;
            string state = h.State;
            h.Tick();
            if (seen.Count == before) continue;
            Assert.Equal(before + 1, seen.Count); // one read a tick
            Assert.Equal(i, seen[^1]["X_INPUT"]); // with this tick's values
            states.Add(state);
        }
        Assert.Equal(new[] { "Running", "Verifying" }, states.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(h.Env.Logged, l => l.Line.Contains("internal error"));
    }

    [Fact]
    public void A_dark_display_shows_not_powered_and_powering_up_shows_starting()
    {
        // A sighted pilot sees a blank screen: the window never shows the last lit reading as current, and says nothing.
        var h = New();
        h.Reader.LitNow = true;
        h.Start();
        var s = h.RunUntilRunning();
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());

        h.Reader.LitNow = false; // the display goes dark
        h.Tick();
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { UnpoweredRow }, h.Monitor.View());
        Assert.Null(h.Monitor.RowsWhileRunning());
        Assert.Equal(default(FakeReading), h.Monitor.Latest);
        Assert.False(h.Monitor.KeyFallsBackToAi()); // the reader runs: the window opens and says so
        s.Reading = R("B");
        h.Tick(300);
        Assert.Equal(new[] { UnpoweredRow }, h.Monitor.View());

        h.Reader.LitNow = true; // powering up: quiet for 10 s
        h.Tick(); // t
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());
        Assert.Null(h.Monitor.RowsWhileRunning());
        Assert.Equal(default(FakeReading), h.Monitor.Latest);
        Assert.False(h.Monitor.KeyFallsBackToAi());
        h.Tick(99); // to t+9.9 s
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());

        h.Tick(); // t+10 s: the first good reading, a silent baseline
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());
        Assert.Equal(R("B"), h.Monitor.Latest);
        Assert.Empty(h.Env.Spoken); // neither row is ever spoken (S4)
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void No_reading_reason_is_starting_while_the_display_powers_up()
    {
        var h = New();
        h.Reader.LitNow = true;
        h.Start();
        h.RunUntilRunning();
        Assert.Null(h.Monitor.NoReadingReason); // a reading is shown

        h.Reader.LitNow = false;
        h.Tick();
        Assert.Equal("display dark", h.Monitor.NoReadingReason);

        h.Reader.LitNow = true; // powering up: lit, still in the 10 s quiet
        h.Tick();
        Assert.Equal("Running", h.State);
        Assert.Equal("starting", h.Monitor.NoReadingReason);
        h.Tick(99);
        Assert.Equal("starting", h.Monitor.NoReadingReason);

        h.Tick(); // the quiet ends: a reading again
        Assert.Null(h.Monitor.NoReadingReason);
    }

    [Fact]
    public void No_reading_reason_is_null_for_a_stopped_monitor()
    {
        var h = New();
        Assert.Null(h.Monitor.NoReadingReason); // never started

        h.Reader.LitNow = true;
        h.Start();
        h.RunUntilRunning();
        h.Reader.LitNow = false;
        h.Tick();
        Assert.Equal("display dark", h.Monitor.NoReadingReason);

        h.Monitor.Stop();
        Assert.Equal("Stopped", h.State);
        Assert.Null(h.Monitor.NoReadingReason);
    }

    [Fact]
    public void Newest_is_the_newest_reading_that_is_not_corrupt_whatever_the_displays_power()
    {
        // Plan 27: what a reader reads beside its display's picture (the ADI's FMC reference speeds) is wanted while the
        // display is dark too, where Latest gives nothing.
        var h = New();
        Assert.Equal(default(FakeReading), h.Monitor.Newest); // never started
        h.Reader.LitOf = reading => !reading.Value.StartsWith("dark", StringComparison.Ordinal);
        h.Start();
        h.Tick();
        Assert.Equal(default(FakeReading), h.Monitor.Newest); // searching: no reading yet
        var s = h.RunUntilRunning();
        Assert.Equal(R("A"), h.Monitor.Newest);
        Assert.Equal(R("A"), h.Monitor.Latest);

        s.Reading = R("B"); // lit: it is Latest's reading, at once (before the change settles)
        h.Tick();
        Assert.Equal(R("B"), h.Monitor.Newest);
        Assert.Equal(R("B"), h.Monitor.Latest);

        s.Reading = R("dark 1"); // the display goes dark: Latest gives nothing, Newest follows the readings
        h.Tick();
        Assert.Equal(default(FakeReading), h.Monitor.Latest);
        Assert.Equal(R("dark 1"), h.Monitor.Newest);
        s.Reading = R("dark 2");
        h.Tick();
        Assert.Equal(R("dark 2"), h.Monitor.Newest);

        s.Reading = R("C"); // powering up: quiet for 10 s, and Newest follows all the same
        h.Tick();
        Assert.Equal(default(FakeReading), h.Monitor.Latest);
        Assert.Equal(R("C"), h.Monitor.Newest);

        s.Reading = Corrupt("X"); // a corrupt reading is never it
        h.Tick(3);
        Assert.Equal("Running", h.State);
        Assert.Equal(R("C"), h.Monitor.Newest);

        s.Fail = true; // the memory lost: nothing, never the last reading before it
        h.Env.Locate = NotFound;
        h.Tick();
        Assert.NotEqual("Running", h.State);
        Assert.Equal(default(FakeReading), h.Monitor.Newest);
        Assert.Empty(h.Env.Spoken); // none of it is ever spoken
    }

    [Fact]
    public void Newest_is_nothing_once_the_reader_stops()
    {
        var h = New();
        h.Start();
        var s = h.RunUntilRunning();
        Assert.Equal(R("A"), h.Monitor.Newest);

        // Ten corrupt readings in a row: unavailable.
        s.Reading = Corrupt("X");
        h.Tick(10);
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(default(FakeReading), h.Monitor.Newest);

        // A dark candidate runs at once: its reading is the newest, though nothing is shown.
        var dark = New();
        dark.Reader.LitNow = false;
        dark.Start();
        dark.RunUntilRunning();
        Assert.Equal(R("A"), dark.Monitor.Newest);
        Assert.Equal(default(FakeReading), dark.Monitor.Latest);

        dark.Monitor.Stop();
        Assert.Equal(default(FakeReading), dark.Monitor.Newest);
    }

    [Fact]
    public void A_dark_candidate_runs_at_once_and_its_power_up_is_a_silent_baseline()
    {
        // A cold-and-dark load: the first reading is dark and not corrupt, so the reader runs at once and its window says
        // the display is not powered; nothing is spoken through the power-up, and the first reading after the 10 s quiet
        // is a silent baseline (S4).
        var h = New();
        h.Reader.LitNow = false;
        h.Start();
        h.TickUntil(() => h.State == "Verifying");
        h.Tick(); // its first reading
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { UnpoweredRow }, h.Monitor.View());
        Assert.Null(h.Monitor.RowsWhileRunning());
        Assert.False(h.Monitor.KeyFallsBackToAi());

        h.Tick(600); // a minute cold and dark
        Assert.Equal("Running", h.State);
        Assert.Equal(1, h.Env.LocateCalls);
        var s = Assert.Single(h.Reader.Samplers);
        Assert.False(s.Disposed);
        Assert.Equal(new[] { UnpoweredRow }, h.Monitor.View());

        h.Reader.LitNow = true; // it powers up, and what it shows changes inside the quiet
        s.Reading = R("B");
        h.Tick(50);
        s.Reading = R("C");
        h.Tick(50); // lit for 9.9 s
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());

        h.Tick(); // lit for 10 s: the baseline, silent
        Assert.Equal(new[] { "X: C" }, h.Monitor.View());
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);

        s.Reading = R("D"); // later changes are spoken
        h.Tick(5);
        Assert.Equal(new[] { "X: D" }, h.Env.Spoken);
    }

    [Fact]
    public void Running_on_a_powering_up_reading_keeps_the_next_good_one_a_silent_baseline()
    {
        // A candidate whose first reading is still in the power-up quiet runs at once. The quiet may end at its very next
        // reading, which must still be the silent baseline, never a change from the powering-up one: entering Running
        // on a quiet reading keeps the rebaseline, it does not rely on another quiet tick setting it again.
        var h = New();
        h.Reader.LitNow = true;
        h.Start();
        var s = h.RunUntilRunning(); // A
        h.Tick(60); // past the 5 s between searches
        h.Reader.LitNow = false;
        h.Tick(); // dark
        h.Reader.LitNow = true;
        long litAt = h.Env.NowMs;
        h.Tick(); // it powers up: quiet until litAt + 10 s
        h.Tick(96);
        Assert.Equal(litAt + 9_700, h.Env.NowMs);

        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B")); // what the display shows as it powers up
        s.Fail = true;
        h.Tick(); // litAt + 9.7 s: the memory lost, a new search
        Assert.Equal("Locating", h.State);
        h.Tick(); // + 9.8 s: found
        Assert.Equal("Verifying", h.State);
        h.Tick(); // + 9.9 s: its first reading, still powering up: it runs at once
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());

        h.Reader.Samplers.Single(x => !x.Disposed).Reading = R("C");
        h.Tick(); // + 10 s: the quiet ends, and the first good reading is the baseline
        Assert.Equal(new[] { "X: C" }, h.Monitor.View());
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void A_reader_with_no_power_state_shows_its_rows_throughout()
    {
        // The FMA's path (Lit null): no "not powered" or "starting" row while it runs.
        var h = New().Start();
        var s = h.RunUntilRunning();
        for (int i = 0; i < 200; i++)
        {
            if (i == 50) s.Reading = R("B");
            h.Tick();
            var shown = i < 50 ? "X: A" : "X: B"; // shown at once, spoken once settled
            Assert.Equal(new[] { shown }, h.Monitor.View());
            Assert.Equal(R(shown[3..]), h.Monitor.Latest);
        }
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken);
    }

    [Fact]
    public void A_change_missed_while_searching_again_is_spoken_when_found()
    {
        // A failed read searches again silently; what changed meanwhile is told once it reads again, never swallowed.
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Fail = true;
        h.Tick();
        Assert.Equal("Locating", h.State);
        Assert.True(s.Disposed);

        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B"));
        h.RunUntilRunning(100); // the next attempt is at most 5 s away
        Assert.Equal(new[] { "X: B" }, h.Env.Spoken);
        Assert.Equal(2, h.Env.LocateCalls);
    }

    [Fact]
    public void View_while_starting_is_one_starting_row()
    {
        var h = New();
        Assert.Equal(new[] { StartingRow }, h.Monitor.View()); // not started yet
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(Corrupt("Z"));
        h.Start();

        var seen = new HashSet<string>();
        for (int i = 0; i < 15; i++) // through Analyzing and Locating into Verifying
        {
            h.Tick();
            seen.Add(h.State);
            Assert.Equal(new[] { StartingRow }, h.Monitor.View());
            Assert.Null(h.Monitor.RowsWhileRunning());
            Assert.Equal(default(FakeReading), h.Monitor.Latest);
        }
        Assert.Contains("Analyzing", seen);
        Assert.Contains("Locating", seen);
        Assert.Contains("Verifying", seen);
        Assert.DoesNotContain("Running", seen);
    }

    // ---- stops, recovery and status lines ----

    [Fact]
    public void Ten_corrupt_readings_stop_with_unexpected_values()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);

        // Nine corrupt, one good: the count starts again.
        s.Reading = Corrupt("Z");
        h.Tick(9);
        s.Reading = R("A");
        h.Tick();
        s.Reading = Corrupt("Z");
        h.Tick(9);
        Assert.Empty(h.Env.Spoken); // a corrupt reading is never spoken...
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View()); // ...nor shown

        h.Tick(); // the tenth in a row
        Assert.Equal(new[] { StoppedUnexpected }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(s.Disposed);
        Assert.Contains(h.Env.Logged, l => l.Line.Contains("unexpected values for 10 readings"));
        h.Tick(50);
        Assert.Single(h.Env.Spoken);
        Assert.Equal(new[] { "X announcements unavailable: unexpected values" }, h.Monitor.View());
    }

    [Fact]
    public void A_lost_reading_searches_silently_and_says_memory_not_found_after_10_s()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(60); // past the 5 s between attempts
        h.Env.Locate = NotFound;
        s.Fail = true;
        h.Tick(); // lost: a new search, silently
        Assert.Equal("Locating", h.State);
        Assert.True(s.Disposed);
        Assert.Equal(2, h.Env.LocateCalls);

        h.Tick(99); // 9.9 s after the loss
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { StartingRow }, h.Monitor.View()); // a short search: nothing said yet

        h.Tick(); // 10 s
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        Assert.Equal(new[] { StoppedNotFound }, h.Monitor.View());

        h.Tick(1300); // the window runs out for the same reason: nothing more, and it keeps searching
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        Assert.Equal("Locating", h.State);
        Assert.Equal(new[] { StoppedNotFound }, h.Monitor.View());
    }

    [Fact]
    public void The_search_window_running_out_says_unavailable_once_then_retries_every_30_s()
    {
        var h = New();
        h.Env.Locate = NotFound;
        h.Start();
        h.Tick(1200); // to 119.9 s
        Assert.Equal(24, h.Env.LocateCalls); // 0 to 115 s after the first attempt
        var times = h.Env.LocateTimes.ToArray();
        Assert.All(times.Zip(times.Skip(1)), p => Assert.Equal(5000, p.Second - p.First));
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { StartingRow }, h.Monitor.View());

        h.Tick(10);
        Assert.Equal(new[] { UnavailableNotFound }, h.Env.Spoken);
        Assert.Equal(new[] { UnavailableNotFound }, h.Monitor.View());

        // Not final: a long load can outlast the window, so the search goes on, slowly and silently.
        h.Tick(3000); // to about 7 minutes
        times = h.Env.LocateTimes.ToArray();
        Assert.True(times.Length > 30, $"{times.Length} attempts");
        Assert.All(times.Skip(23).Zip(times.Skip(24)), p => Assert.Equal(30_000, p.Second - p.First));
        Assert.Equal(new[] { UnavailableNotFound }, h.Env.Spoken);
        Assert.Equal("Locating", h.State);

        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B"));
        h.Env.Locate = Found;
        h.RunUntilRunning(310); // the next attempt is at most 30 s away
        h.Tick(20);
        Assert.Equal(new[] { UnavailableNotFound, Resumed }, h.Env.Spoken); // it never ran: no change to tell
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Resumed_carries_what_changed_meanwhile(string culture)
    {
        var spoken = TestCulture.Run(culture, () =>
        {
            var h = New();
            h.Reader.ChangesOf = (heard, now, withPrefix) => new[]
            {
                new Bbs757SpokenLine("MUTE_X", (withPrefix ? "X: " : "") + heard + " to " + now),
                new Bbs757SpokenLine("MUTE_X", (withPrefix ? "X: " : "") + now + " armed"),
            };
            h.Start();
            h.RunUntilRunning(); // A

            // The memory lost for longer than 10 s ("stopped"), found again at B: what changed is never swallowed, the
            // first good reading tells it after "resumed".
            LoseAndFind(h, R("B"));
            h.Tick(30);
            string expected = Resumed + ", " +
                string.Join(", ", h.Reader.Changes(R("A"), R("B"), withPrefix: false).Select(l => l.Text));
            Assert.Equal("X announcements resumed, A to B, B armed", expected);
            Assert.Equal(new[] { StoppedNotFound, expected }, h.Env.Spoken);

            // Lost again, found at C.
            LoseAndFind(h, R("C"));
            h.Tick(20);
            return h.Env.Spoken;
        });
        Assert.Equal(new[]
        {
            StoppedNotFound, "X announcements resumed, A to B, B armed",
            StoppedNotFound, "X announcements resumed, B to C, C armed",
        }, spoken);
    }

    // A stop line the pilot heard survives a flight reload (Restart): the new flight's first reading is still a silent
    // baseline, so no change list, but it says "resumed" so the "stopped" is never left unanswered (Review Focus).

    [Theory]
    [InlineData("lost")]
    [InlineData("corrupt")]
    [InlineData("never found")]
    public void A_stop_heard_before_a_reload_is_answered_by_resumed_alone(string how)
    {
        var h = New().Start();
        string heard;
        switch (how)
        {
            case "lost": // the memory lost in flight: "stopped" at 10 s
            {
                var first = h.RunUntilRunning();
                h.Tick(60);
                h.Env.Locate = NotFound;
                first.Fail = true;
                h.Tick(110);
                heard = StoppedNotFound;
                break;
            }
            case "corrupt": // ten corrupt readings: the one stop that waits for a new flight
            {
                var s = h.RunUntilRunning();
                s.Reading = Corrupt("Z");
                h.Tick(10);
                heard = StoppedUnexpected;
                break;
            }
            default: // the search window ran out before the reader ever ran
                h.Env.Locate = NotFound;
                h.Tick(1300);
                heard = UnavailableNotFound;
                break;
        }
        Assert.Equal(new[] { heard }, h.Env.Spoken);

        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B")); // the new flight's display differs
        h.Env.Locate = Found;
        h.Monitor.Restart(); // a flight reload
        Assert.Equal(new[] { WaitingRow }, h.Monitor.View()); // a new search, as at a start
        h.RunUntilRunning(10);
        h.Tick(30);
        // Exactly one "resumed", with no change list: the new flight's reading is a baseline, not a change.
        Assert.Equal(new[] { heard, Resumed }, h.Env.Spoken);
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());

        h.Monitor.Restart(); // answered: the next reload is silent again
        h.RunUntilRunning(10);
        h.Tick(30);
        Assert.Equal(new[] { heard, Resumed }, h.Env.Spoken);
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("Stop")]
    public void A_stop_heard_is_forgotten_by_a_new_start_or_a_stop(string how)
    {
        // Selecting the 757 again (a new environment) or leaving it starts afresh: no "resumed".
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Reading = Corrupt("Z");
        h.Tick(10);
        Assert.Equal(new[] { StoppedUnexpected }, h.Env.Spoken);

        var first = h.Env;
        var next = how == "Stop" ? first : new FakeEnvironment();
        if (how == "Stop") h.Monitor.Stop();
        h.Monitor.Start(next, startTimer: false);
        for (int i = 0; i < 20 && h.State != "Running"; i++) { h.Monitor.Tick(); next.NowMs += 100; }
        Assert.Equal("Running", h.State);
        for (int i = 0; i < 30; i++) { h.Monitor.Tick(); next.NowMs += 100; }
        Assert.Equal(new[] { StoppedUnexpected }, first.Spoken);
        if (how == "Start") Assert.Empty(next.Spoken);
    }

    [Fact]
    public void Another_aircraft_loaded_waits_silently()
    {
        var h = New().Start();
        var values = h.Env.Simulator;
        var s = h.RunUntilRunning();
        h.Tick(60);
        h.Env.Locate = NotFound;
        s.Fail = true;
        h.Tick(); // the memory lost: a silent search
        Assert.Equal(2, h.Env.LocateCalls);

        h.Env.Simulator = null; // another aircraft (or the menus)...
        h.Monitor.Restart();    // ...after a flight reload
        h.Tick(6000); // 10 minutes
        Assert.Empty(h.Env.Spoken); // never "unavailable: aircraft memory not found"
        Assert.Equal("WaitingForSimulator", h.State);
        Assert.Equal(new[] { WaitingRow }, h.Monitor.View());
        Assert.Equal(2, h.Env.LocateCalls); // no search while it waits
        Assert.False(h.Monitor.KeyFallsBackToAi());

        // Without a reload too: the memory lost, then the simulator's values gone, is silent, and so is the new search
        // once they return (the loss was answered by the wait, which starts a new window).
        h.Env.Simulator = values;
        h.Env.Locate = Found;
        var again = h.RunUntilRunning(10);
        h.Tick(60);
        h.Env.Locate = NotFound;
        again.Fail = true;
        h.Tick();
        h.Env.Simulator = null;
        h.Tick(6000);
        h.Env.Simulator = values;
        h.Tick(600); // a minute of searching
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Locating", h.State);
    }

    [Fact]
    public void Every_wait_for_the_simulator_ends_in_a_silent_baseline()
    {
        // A flight change seen only as a wait (no reload): the new flight's first good reading is a baseline, never the
        // difference between the two flights (S4).
        var h = New().Start();
        h.RunUntilRunning(); // A
        var values = h.Env.Simulator;
        h.Env.Simulator = null;
        h.Tick(60);
        Assert.Equal("WaitingForSimulator", h.State);
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("B")); // the next flight's display differs
        h.Env.Simulator = values;
        h.RunUntilRunning(100);
        h.Tick(30);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "X: B" }, h.Monitor.View());

        // A stop the pilot heard before the wait is answered by "resumed" alone, as after a reload.
        h.Tick(60);
        h.Env.Locate = NotFound;
        h.Reader.Samplers.Single(s => !s.Disposed).Fail = true;
        h.Tick(110);
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        h.Env.Simulator = null;
        h.Tick(60);
        h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("C"));
        h.Env.Locate = Found;
        h.Env.Simulator = values;
        h.RunUntilRunning(100);
        h.Tick(30);
        Assert.Equal(new[] { StoppedNotFound, Resumed }, h.Env.Spoken);
        Assert.Equal(new[] { "X: C" }, h.Monitor.View());

        // Later changes are spoken again.
        h.Reader.Samplers.Single(s => !s.Disposed).Reading = R("D");
        h.Tick(5);
        Assert.Equal(new[] { StoppedNotFound, Resumed, "X: D" }, h.Env.Spoken);
    }

    [Theory]
    [InlineData("files not found")]
    [InlineData("file not recognised")]
    [InlineData("no access")]
    [InlineData("internal error")]
    public void Final_reasons_hold_until_restart(string how)
    {
        var h = New();
        string reason;
        string said;
        switch (how)
        {
            case "files not found": // no layout file at all: the 757 readers' one line is spoken (S14), the reason shown
                h.Env.Analyse = NoBuilds(FmaReasons.FilesNotFound);
                reason = Bbs757DisplayReasons.FilesNotFound;
                said = Bbs757DisplayFilesNotice.Line;
                break;
            case "file not recognised": // a layout file loaded, but without this reader's layout: its own line
                h.Reader.Layouts = Array.Empty<FakeLayout>();
                reason = Bbs757DisplayReasons.FileNotRecognised;
                said = "X announcements unavailable: BlueBird file not recognised";
                break;
            case "no access":
                h.Env.Locate = NoAccess;
                reason = Bbs757DisplayReasons.NoAccess;
                said = "X announcements unavailable: no access to the simulator";
                break;
            default:
                h.Env.Locate = () => Task.FromException<ModuleLocation>(new IOException("locate blew up"));
                reason = Bbs757DisplayReasons.InternalError;
                said = "X announcements unavailable: internal error, see the log";
                break;
        }
        h.Start();
        h.TickUntil(() => h.State == "Unavailable", 100);
        Assert.Equal(new[] { said }, h.Env.Spoken);
        int analyses = h.Env.AnalyseCalls;
        int locates = h.Env.LocateCalls;

        h.Tick(3000); // 5 minutes: no retry
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(analyses, h.Env.AnalyseCalls);
        Assert.Equal(locates, h.Env.LocateCalls);
        Assert.Equal(new[] { said }, h.Env.Spoken);
        Assert.Equal(new[] { "X announcements unavailable: " + reason }, h.Monitor.View());
        Assert.True(h.Monitor.KeyFallsBackToAi());

        // A flight reload tries again; an "unavailable" line is not a stop, so the reader runs without "resumed".
        h.Env.Analyse = OneBuild;
        h.Reader.Layouts = new[] { new FakeLayout("one") };
        h.Env.Locate = Found;
        h.Monitor.Restart();
        h.RunUntilRunning(20);
        h.Tick(20);
        Assert.Equal(new[] { said }, h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());

        // Only the no-layout-file case uses the session's one files line.
        var later = new List<string>();
        h.Env.FilesNotice.Say(muted: false, later.Add);
        Assert.Equal(how == "files not found" ? Array.Empty<string>() : new[] { Bbs757DisplayFilesNotice.Line }, later);
    }

    [Fact]
    public void No_layout_file_says_the_one_files_line_once_per_notice()
    {
        Assert.Equal("BlueBird display files could not be loaded; the display keys fall back to AI reading.",
            Bbs757DisplayFilesNotice.Line); // the owner's text (S14)

        // Two readers on one module session's notice: the first says the line, the second nothing, and neither its own.
        var session = new Bbs757DisplayFilesNotice();
        var a = New();
        var b = New();
        foreach (var h in new[] { a, b })
        {
            h.Env.FilesNotice = session;
            h.Env.Analyse = NoBuilds(FmaReasons.FileNotRecognised);
            h.Start();
        }
        for (int i = 0; i < 10; i++) { a.Tick(); b.Tick(); }
        Assert.Equal(new[] { Bbs757DisplayFilesNotice.Line }, a.Env.Spoken);
        Assert.Empty(b.Env.Spoken);
        foreach (var h in new[] { a, b })
        {
            Assert.Equal("Unavailable", h.State);
            Assert.Equal(0, h.Env.LocateCalls);
            // Each still logs and shows its own reason (window text is not speech).
            Assert.Equal(new[] { "X announcements unavailable: BlueBird file not recognised" }, h.Monitor.View());
            Assert.Contains(h.Env.Logged, l => l.Line == "Analyzing -> Unavailable: BlueBird file not recognised");
        }

        // A flight reload in the same session analyses again, and says nothing more.
        a.Monitor.Restart();
        b.Monitor.Restart();
        for (int i = 0; i < 10; i++) { a.Tick(); b.Tick(); }
        Assert.Equal(2, a.Env.AnalyseCalls);
        Assert.Equal(new[] { Bbs757DisplayFilesNotice.Line }, a.Env.Spoken);
        Assert.Empty(b.Env.Spoken);

        // In a new session, a muted reader leaves the line to the next.
        var next = new Bbs757DisplayFilesNotice();
        var c = New();
        var d = New();
        c.Env.Muted.Add("MUTE_X");
        foreach (var h in new[] { c, d })
        {
            h.Env.FilesNotice = next;
            h.Env.Analyse = NoBuilds(FmaReasons.FilesNotFound);
            h.Start();
        }
        for (int i = 0; i < 10; i++) { c.Tick(); d.Tick(); }
        Assert.Empty(c.Env.Spoken);
        Assert.Equal(new[] { Bbs757DisplayFilesNotice.Line }, d.Env.Spoken);
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        // The same flight twice, once with the reader's row muted: nothing spoken, the window the same at every step.
        var (spoken, views) = Flight(New());
        Assert.Equal(new[]
        {
            "X: B", StoppedNotFound, Resumed + ", C", StoppedUnexpected,
            "X announcements unavailable: no access to the simulator",
        }, spoken);

        var muted = New();
        muted.Env.Muted.Add("MUTE_X");
        var (mutedSpoken, mutedViews) = Flight(muted);
        Assert.Empty(mutedSpoken);
        Assert.Equal(views, mutedViews);

        static (string[] Spoken, List<string[]> Views) Flight(Harness h)
        {
            var views = new List<string[]>();
            void Look() => views.Add(h.Monitor.View().ToArray());
            h.Start();
            var s = h.RunUntilRunning();
            Look();
            s.Reading = R("B"); // a change
            h.Tick(5);
            Look();
            h.Tick(60);
            h.Env.Locate = NotFound; // lost for 10 s: stopped
            s.Fail = true;
            h.Tick(110);
            Look();
            h.Reader.MakeSampler = (_, _) => h.Reader.NewSampler(R("C")); // resumed, with what changed
            h.Env.Locate = Found;
            var next = h.RunUntilRunning(100);
            h.Tick(5);
            Look();
            next.Reading = Corrupt("Z"); // ten corrupt readings: stopped until a reload
            h.Tick(10);
            Look();
            h.Env.Locate = NoAccess; // the reload's search: unavailable
            h.Monitor.Restart();
            h.Tick(60);
            Look();
            Assert.Equal("Unavailable", h.State);
            return (h.Env.Spoken, views);
        }
    }

    [Fact]
    public void Each_change_line_is_muted_by_its_own_row()
    {
        var h = New();
        h.Reader.ChangesOf = (_, now, withPrefix) => new[]
        {
            new Bbs757SpokenLine("ROW_A", (withPrefix ? "A: " : "a ") + now),
            new Bbs757SpokenLine("ROW_B", (withPrefix ? "B: " : "b ") + now),
        };
        h.Env.Muted.Add("ROW_A");
        h.Start();
        var s = h.RunUntilRunning();
        s.Reading = R("C");
        h.Tick(5);
        Assert.Equal(new[] { "B: C" }, h.Env.Spoken);

        // "resumed" joins only the unmuted rows' parts.
        LoseAndFind(h, R("D"));
        Assert.Equal(new[] { "B: C", StoppedNotFound, Resumed + ", b D" }, h.Env.Spoken);

        // Both rows muted: "resumed" alone.
        h.Env.Muted.Add("ROW_B");
        LoseAndFind(h, R("E"));
        Assert.Equal(new[] { "B: C", StoppedNotFound, Resumed + ", b D", StoppedNotFound, Resumed }, h.Env.Spoken);

        // The status row muted: no stop or "resumed", but an unmuted row still tells what changed meanwhile, as a change.
        h.Env.Muted.Remove("ROW_B");
        h.Env.Muted.Add("MUTE_X");
        LoseAndFind(h, R("F"));
        Assert.Equal(new[] { "B: C", StoppedNotFound, Resumed + ", b D", StoppedNotFound, Resumed, "B: F" }, h.Env.Spoken);
    }

    [Fact]
    public void Key_falls_back_to_ai_only_when_unavailable_or_stopped_10_s()
    {
        var h = New();
        Assert.True(h.Monitor.KeyFallsBackToAi()); // not started: the AI read
        var values = h.Env.Simulator;
        h.Env.Simulator = null;
        h.Start();
        h.Tick(100);
        Assert.False(h.Monitor.KeyFallsBackToAi()); // waiting: the window says so
        h.Env.Simulator = values;
        h.Tick();
        Assert.Equal("Analyzing", h.State);
        Assert.False(h.Monitor.KeyFallsBackToAi()); // starting
        var s = h.RunUntilRunning();
        Assert.False(h.Monitor.KeyFallsBackToAi());

        // The memory lost: the window while it searches, the AI read from 10 s, through the slow retry.
        h.Tick(60);
        h.Env.Locate = NotFound;
        s.Fail = true;
        h.TickUntil(() => h.State == "Locating");
        long lostAt = h.Env.NowMs - 100;
        h.Env.NowMs = lostAt + 9_999;
        Assert.False(h.Monitor.KeyFallsBackToAi());
        h.Env.NowMs = lostAt + 10_000;
        Assert.True(h.Monitor.KeyFallsBackToAi());
        h.Tick(1300);
        Assert.Equal("Locating", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());

        h.Env.Locate = Found;
        s = h.RunUntilRunning(310);
        Assert.False(h.Monitor.KeyFallsBackToAi()); // found again

        s.Reading = Corrupt("Z");
        h.Tick(10);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());
        h.Monitor.Stop();
        Assert.True(h.Monitor.KeyFallsBackToAi());

        // A search window that ran out before the reader ever ran: the AI read.
        var never = New();
        never.Env.Locate = NotFound;
        never.Start();
        never.Tick(1200); // to 119.9 s
        Assert.False(never.Monitor.KeyFallsBackToAi());
        never.Tick(10);
        Assert.Equal(new[] { UnavailableNotFound }, never.Env.Spoken);
        Assert.True(never.Monitor.KeyFallsBackToAi());
    }

    [Fact]
    public void An_exception_in_a_tick_ends_in_internal_error_and_restart_recovers()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Tick(5);
        s.Throw = new InvalidOperationException("boom");
        h.Tick();
        Assert.Equal(new[] { "X announcements unavailable: internal error, see the log" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(s.Disposed);
        // Logged with its stack, on the reader's own log.
        Assert.Contains(h.Env.Logged, l => l.Channel == LogChannel && l.Line.Contains("boom") && l.Line.Contains(nameof(FakeSampler.TryRead)));
        h.Tick(20);
        Assert.Single(h.Env.Spoken);
        Assert.Equal(new[] { "X announcements unavailable: internal error, see the log" }, h.Monitor.View());

        // The timer keeps ticking, so a flight reload recovers.
        h.Monitor.Restart();
        h.RunUntilRunning(10);
        h.Tick(20);
        Assert.Single(h.Env.Spoken);
        Assert.Equal(new[] { "X: A" }, h.Monitor.View());
    }

    [Fact]
    public void Stop_releases_every_sampler_once()
    {
        var h = New();
        h.Reader.Layouts = new[] { new FakeLayout("one"), new FakeLayout("two") };
        h.Start();
        h.TickUntil(() => h.State == "Verifying"); // one candidate read, one waiting
        Assert.Equal(2, h.Reader.Samplers.Count);
        Assert.All(h.Reader.Samplers, s => Assert.False(s.Disposed));
        h.Monitor.Stop();
        Assert.Equal("Stopped", h.State);
        Assert.All(h.Reader.Samplers, s => Assert.Equal(1, s.DisposeCount));

        h.Monitor.Stop(); // twice, and Dispose, release nothing again
        h.Monitor.Dispose();
        h.Tick(50); // a stopped monitor does nothing
        Assert.Equal(2, h.Reader.Samplers.Count);
        Assert.All(h.Reader.Samplers, s => Assert.Equal(1, s.DisposeCount));

        // Running (the waiting candidate dropped when the first read), then stopped.
        h.Start();
        h.RunUntilRunning();
        Assert.Equal(4, h.Reader.Samplers.Count);
        h.Monitor.Stop();
        Assert.All(h.Reader.Samplers, s => Assert.Equal(1, s.DisposeCount));

        // Through a reload: the old ones released by Restart, the new ones by Stop.
        h.Start();
        h.RunUntilRunning();
        h.Monitor.Restart();
        h.RunUntilRunning(10);
        h.Monitor.Stop();
        Assert.Equal(8, h.Reader.Samplers.Count);
        Assert.All(h.Reader.Samplers, s => Assert.Equal(1, s.DisposeCount));
        Assert.Empty(h.Env.Spoken); // stopping is silent
    }

    [Theory]
    [InlineData("analysis", FmaReasons.FileNotRecognised, "X announcements unavailable: BlueBird file not recognised")]
    [InlineData("analysis", FmaReasons.FilesNotFound, "X announcements unavailable: BlueBird 757 files not found")]
    [InlineData("search", FmaReasons.FileNotRecognised, "X announcements unavailable: BlueBird file not recognised")]
    [InlineData("search", FmaReasons.FilesNotFound, "X announcements unavailable: BlueBird 757 files not found")]
    public void Session_failures_are_worded_for_the_757(string where, string sessionFailure, string line)
    {
        // The shared module session words its reasons for the PMDG 737 whatever the module.
        Assert.Contains("PMDG", sessionFailure);
        var h = New();
        if (where == "analysis")
            h.Env.Analyse = NoBuilds(sessionFailure);
        else
            h.Env.Locate = () => Task.FromResult(new ModuleLocation(Array.Empty<LocatedModule>(), sessionFailure));
        h.Start();
        h.TickUntil(() => h.State == "Unavailable", 100);
        h.Tick(10);

        Assert.Equal(new[] { line }, h.Monitor.View());
        // No layout file is the one files line (S14); a search's failure is the reader's own line.
        Assert.Equal(new[] { where == "analysis" ? Bbs757DisplayFilesNotice.Line : line }, h.Env.Spoken);
        Assert.DoesNotContain(h.Env.Spoken, l => l.Contains("PMDG"));
        Assert.DoesNotContain(h.Env.Logged, l => l.Line.Contains("PMDG"));
    }

    // ---- fakes ----

    private sealed class Harness
    {
        public Harness(params Bbs757SimInput[] inputs)
        {
            Reader = new FakeReader(inputs);
            Monitor = new Bbs757DisplayMonitor<FakeLayout, FakeReading>(Reader);
        }

        public FakeEnvironment Env { get; } = new();
        public FakeReader Reader { get; }
        public Bbs757DisplayMonitor<FakeLayout, FakeReading> Monitor { get; }
        public string State => Monitor.StateName;

        public Harness Start()
        {
            Monitor.Start(Env, startTimer: false);
            return this;
        }

        /// <summary>One Tick() at the current time per count, each followed by 100 ms on the clock.</summary>
        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += 100;
            }
        }

        /// <summary>One Tick() at <paramref name="ms"/>; the clock stays there.</summary>
        public void TickAt(long ms)
        {
            Env.NowMs = ms;
            Monitor.Tick();
        }

        public void TickUntil(Func<bool> done, int maxTicks = 300)
        {
            for (int i = 0; i < maxTicks && !done(); i++) Tick();
            Assert.True(done(), $"not reached within {maxTicks} ticks; state {State}");
        }

        /// <summary>Ticks until Running and returns the one sampler still alive (the one being read).</summary>
        public FakeSampler RunUntilRunning(int maxTicks = 300)
        {
            TickUntil(() => State == "Running", maxTicks);
            return Reader.Samplers.Single(s => !s.Disposed);
        }
    }

    internal sealed record FakeLayout(string Name);

    internal readonly record struct FakeReading(string Value, bool Corrupt = false) : IBbs757Reading
    {
        public override string ToString() => Value;
    }

    internal sealed class FakeSampler : IBbs757DisplaySampler<FakeReading>
    {
        private int _disposeCount;

        public FakeSampler(FakeReading reading) => Reading = reading;

        /// <summary>What a read returns once <see cref="Script"/> is used up.</summary>
        public FakeReading Reading { get; set; }

        public Queue<FakeReading> Script { get; } = new();
        public bool Fail { get; set; }

        /// <summary>When set, every read throws it (a bug in a reader).</summary>
        public Exception? Throw { get; set; }

        public int Reads { get; private set; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out FakeReading reading)
        {
            // A read after Dispose is a monitor bug; throwing turns it into a visible internal error.
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            if (Throw is { } ex) throw ex;
            Reads++;
            reading = Fail ? default : Script.Count > 0 ? Script.Dequeue() : Reading;
            return !Fail;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    /// <summary>A sampler that takes the simulator's values (S10): records each tick's, then reads as its inner one.</summary>
    internal sealed class SimRecordingSampler(FakeSampler inner, List<Bbs757SimValues> seen) : IBbs757DisplaySampler<FakeReading>
    {
        public bool TryRead(Bbs757SimValues sim, out FakeReading reading)
        {
            seen.Add(sim);
            return inner.TryRead(out reading);
        }

        public bool TryRead(out FakeReading reading) => throw new InvalidOperationException("read without the simulator's values");

        public void Dispose() => inner.Dispose();
    }

    internal sealed class FakeReader : IBbs757DisplayReader<FakeLayout, FakeReading>
    {
        /// <param name="inputs">The simulator variables the reader takes (S10); none by default, as the FMA.</param>
        public FakeReader(params Bbs757SimInput[] inputs)
        {
            SimInputs = inputs;
            MakeSampler = (_, _) => NewSampler(R("A"));
        }

        public string Name => "X";
        public string MuteKey => "MUTE_X";
        public string LogChannel => Bbs757DisplayMonitorTests.LogChannel;
        public int TickMs => 100;
        public IReadOnlyList<Bbs757SimInput> SimInputs { get; }

        public IReadOnlyList<FakeLayout> Layouts { get; set; } = new[] { new FakeLayout("one") };

        /// <summary>The sampler for a located module and one of <see cref="Layouts"/>, or null.</summary>
        public Func<LocatedModule, FakeLayout, FakeSampler?> MakeSampler { get; set; }

        public List<FakeSampler> Samplers { get; } = new();

        /// <summary>The power state every reading reports (null: no power state), unless <see cref="LitOf"/> is set.</summary>
        public bool? LitNow { get; set; }

        /// <summary>When set, the power state as decoded from the reading itself.</summary>
        public Func<FakeReading, bool?>? LitOf { get; set; }

        /// <summary>When set, the change lines; by default one line on the status row, "X: " + now with the prefix and
        /// the bare reading without it.</summary>
        public Func<FakeReading, FakeReading, bool, IReadOnlyList<Bbs757SpokenLine>>? ChangesOf { get; set; }

        public FakeSampler NewSampler(FakeReading reading)
        {
            var s = new FakeSampler(reading);
            Samplers.Add(s);
            return s;
        }

        /// <summary><see cref="Layouts"/> from any build, as a real reader takes its layout from each; none without builds.</summary>
        public IReadOnlyList<FakeLayout> LayoutsIn(ModuleAnalysis analysis) =>
            analysis.Builds.Count > 0 ? Layouts : Array.Empty<FakeLayout>();

        /// <summary>When set, wraps each sampler <see cref="MakeSampler"/> made (which stays in <see cref="Samplers"/>).</summary>
        public Func<FakeSampler, IBbs757DisplaySampler<FakeReading>>? Wrap { get; set; }

        public IBbs757DisplaySampler<FakeReading>? SamplerFor(LocatedModule module, FakeLayout layout) =>
            MakeSampler(module, layout) is { } sampler ? Wrap?.Invoke(sampler) ?? sampler : null;

        public bool? Lit(FakeReading reading) => LitOf is { } of ? of(reading) : LitNow;

        public IReadOnlyList<Bbs757SpokenLine> Changes(FakeReading heard, FakeReading now, bool withPrefix) =>
            ChangesOf is { } of
                ? of(heard, now, withPrefix)
                : new[] { new Bbs757SpokenLine("MUTE_X", withPrefix ? "X: " + now : now.ToString()) };

        public IReadOnlyList<string> Rows(FakeReading reading) => new[] { "X: " + reading };
    }

    internal sealed class FakeEnvironment : IBbs757DisplayEnvironment
    {
        private readonly List<string> _spoken = new();
        private readonly List<(string Channel, string Line)> _logged = new();
        private readonly object _gate = new();

        public FakeEnvironment()
        {
            Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { Build() }, null));
            Locate = () => Task.FromResult(new ModuleLocation(new[] { Located() }, null));
        }

        public long NowMs { get; set; }

        /// <summary>What every read of the simulator returns (null: disconnected, no flight, or another aircraft); by
        /// default a simulator that is up, with no value of any reader input.</summary>
        public Bbs757SimValues? Simulator { get; set; } = new Bbs757SimValues(new Dictionary<string, double>());

        /// <summary>Every key the monitor asked the simulator for, in order.</summary>
        public List<string> KeysAsked { get; } = new();

        /// <summary>How many times the monitor read the simulator.</summary>
        public int ReadCalls { get; private set; }

        public HashSet<string> Muted { get; } = new();

        /// <summary>One per module session; settable so several monitors share one, as the definition wires them.</summary>
        public Bbs757DisplayFilesNotice FilesNotice { get; set; } = new();

        public Func<Task<ModuleAnalysis>> Analyse { get; set; }
        public Func<Task<ModuleLocation>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public int LocateCalls => LocateTimes.Count;
        public List<long> LocateTimes { get; } = new();

        public string[] Spoken { get { lock (_gate) return _spoken.ToArray(); } }
        public (string Channel, string Line)[] Logged { get { lock (_gate) return _logged.ToArray(); } }

        public Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys)
        {
            ReadCalls++;
            KeysAsked.AddRange(keys);
            return Simulator;
        }

        public bool IsMuted(string muteKey) => Muted.Contains(muteKey);

        public void Speak(string text) { lock (_gate) _spoken.Add(text); }

        public void Log(string channel, string line) { lock (_gate) _logged.Add((channel, line)); }

        public Task<ModuleAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<ModuleLocation> LocateAsync()
        {
            LocateTimes.Add(NowMs);
            return Locate();
        }

        /// <summary>A 757 build; the fake reader does not look at it.</summary>
        public static ModuleBuild Build() =>
            new(Bbs757Module.Description, "BBS752.wasm", "BBS752.json", Array.Empty<ReaderResult>());

        /// <summary>A located build on its own shared handle; the fake reader's samplers do not read it.</summary>
        public static LocatedModule Located()
        {
            var source = new FakeProcessMemory();
            var handle = SharedHandle.Open(source, out _);
            return new LocatedModule(Build(), new ModuleAnchor(0, new byte[] { 1 }), new ModuleMemory(source, 0), handle);
        }
    }
}
