// The 757 EHSI as one reader of the 757's display monitor: its names, rows and log, its layouts and samplers from the
// shared module session (each handed the display's FullWanted), its power, its words and its Ctrl+W line; then the
// monitor driving the real reader over scripted readings, so the settle and the Ctrl+M row act as the pilot hears them.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757EhsiTextTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiDisplayTests
{
    private const ulong Base = 0x20000000;

    private static readonly Bbs757EhsiLayout Layout = Bbs757EhsiLayoutTests.Sample();

    private static ModuleBuild Build(Bbs757EhsiLayout? layout, string layoutFile = "BBS752.json") =>
        new(Bbs757Module.Description, "BBS752.wasm", layoutFile,
            layout is null ? Array.Empty<ReaderResult>() : new[] { new ReaderResult(Bbs757Module.Ehsi, layout, null) });

    private sealed class CountingHandle : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void The_reader_is_named_ehsi_on_its_own_row_and_log()
    {
        Assert.Equal("EHSI", Bbs757EhsiDisplay.Name);
        Assert.Equal("bbs757-ehsi", Bbs757EhsiDisplay.LogChannel);
        Assert.Equal(500, Bbs757EhsiDisplay.TickMs);
        Assert.Equal("MSFSBA_BBS757_EHSI_MESSAGES", Bbs757EhsiDisplay.MuteKey);

        IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> reader = new Bbs757EhsiDisplay();
        Assert.Equal("EHSI", reader.Name);
        Assert.Equal("MSFSBA_BBS757_EHSI_MESSAGES", reader.MuteKey);
        Assert.Equal("bbs757-ehsi", reader.LogChannel);
        Assert.Equal(500, reader.TickMs);
        Assert.False(new Bbs757EhsiDisplay().FullWanted());
    }

    [Fact]
    public void The_reader_takes_the_simulators_position_for_the_top_line()
    {
        // S10: BlueBird's top line reads PLANE LATITUDE and PLANE LONGITUDE fresh, so the sampler takes them as inputs,
        // never as a check of memory (B757-30). Plan 28's map extras add the three their draw reads through simulator
        // handles: the wind's speed and direction and the gear position, in the units BlueBird registers them in.
        var inputs = new[]
        {
            new Bbs757SimInput("PLANE LATITUDE", "PLANE LATITUDE", SimVarType.SimVar, "degrees"),
            new Bbs757SimInput("PLANE LONGITUDE", "PLANE LONGITUDE", SimVarType.SimVar, "degrees"),
            new Bbs757SimInput("AMBIENT WIND VELOCITY", "AMBIENT WIND VELOCITY", SimVarType.SimVar, "knots"),
            new Bbs757SimInput("AMBIENT WIND DIRECTION", "AMBIENT WIND DIRECTION", SimVarType.SimVar, "degrees"),
            new Bbs757SimInput("GEAR POSITION", "GEAR POSITION", SimVarType.SimVar, "enum"),
        };
        IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> reader = new Bbs757EhsiDisplay();
        Assert.Equal(inputs, Bbs757EhsiDisplay.SimInputs);
        Assert.Equal(inputs, reader.SimInputs);
        Assert.False(Bbs757EhsiDisplay.SimInputs is Bbs757SimInput[], "the inputs cannot be changed through a cast");

        // The monitor asks the simulator for exactly these each tick and hands them to the sampler.
        var f = new Flight(Light()).RunUntilRunning();
        Assert.NotEmpty(f.Env.KeysAsked);
        Assert.All(f.Env.KeysAsked, key => Assert.Contains(key, inputs.Select(i => i.Key)));
        Assert.Contains("PLANE LATITUDE", f.Env.KeysAsked);
        Assert.Contains("PLANE LONGITUDE", f.Env.KeysAsked);
        foreach (var input in inputs) Assert.Contains(input.Key, f.Env.KeysAsked);
        Assert.Equal(47.442946, f.Sampler.Seen[^1]["PLANE LATITUDE"]);
        Assert.Equal(-122.303648, f.Sampler.Seen[^1]["PLANE LONGITUDE"]);
    }

    [Fact]
    public void Lit_is_the_screens_power()
    {
        var reader = new Bbs757EhsiDisplay();
        Assert.False(reader.Lit(Dark()));
        Assert.True(reader.Lit(Light()));
    }

    [Fact]
    public void Layouts_are_each_builds_ehsi_layout_once_in_file_order()
    {
        var other = Layout with { LegStride = 1640 };
        var analysis = new ModuleAnalysis(new[]
        {
            Build(Layout, "a.json"), Build(null, "b.json"), Build(other, "c.json"), Build(Layout with { SourceFile = "x" }, "d.json"),
        }, null);
        Assert.Equal(new[] { Layout, other }, new Bbs757EhsiDisplay().LayoutsIn(analysis));
        Assert.Empty(new Bbs757EhsiDisplay().LayoutsIn(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "BlueBird 757 files not found")));
    }

    [Fact]
    public void A_sampler_reads_the_builds_own_layout_on_its_own_lease_and_asks_the_display_for_the_full_set()
    {
        var handle = new CountingHandle();
        var shared = SharedHandle.Open(handle, out var sessionLease);
        var bytes = new byte[0x1000];
        var source = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        var module = new LocatedModule(Build(Layout), new ModuleAnchor(Layout.AnchorAddress, Layout.Anchor),
            new ModuleMemory(source, Base), shared);
        var display = new Bbs757EhsiDisplay();

        Assert.Null(display.SamplerFor(module, Layout with { LegStride = 1640 }));

        var reader = Assert.IsType<Bbs757EhsiReader>(display.SamplerFor(module, Layout));
        Assert.Equal(Layout, reader.Layout);
        // The display's FullWanted, asked at each reading: set later (the window opening), the sampler follows.
        Assert.False(reader.FullWanted());
        display.FullWanted = () => true;
        Assert.True(reader.FullWanted());
        // This module holds no nd: not found.
        Assert.False(reader.TryRead(out _));

        sessionLease.Dispose();
        Assert.Equal(0, handle.Disposed);
        reader.Dispose();
        Assert.Equal(1, handle.Disposed);
        Assert.Throws<ObjectDisposedException>(() => display.SamplerFor(module, Layout));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Rows_and_changes_are_the_ehsi_text(string culture)
    {
        IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> reader = new Bbs757EhsiDisplay();
        Assert.Equal(TestCulture.Run(culture, () => Bbs757EhsiText.Rows(Ehsi())), TestCulture.Run(culture, () => reader.Rows(Ehsi())));
        var heard = Light() with { Active = Btg };
        var now = Light() with { ExcessCount = 100 };
        var lines = new[]
        {
            new Bbs757SpokenLine("MSFSBA_BBS757_EHSI_MESSAGES", "EHSI message: EXCESS DATA"),
            new Bbs757SpokenLine("MSFSBA_BBS757_EHSI_MESSAGES", "EHSI: KOLM, 37 NM"),
        };
        // "EHSI" names the display: the words are the same after "resumed".
        Assert.Equal(lines, TestCulture.Run(culture, () => reader.Changes(heard, now, withPrefix: true)));
        Assert.Equal(lines, TestCulture.Run(culture, () => reader.Changes(heard, now, withPrefix: false)));
        Assert.Empty(reader.Changes(now, now, withPrefix: true));
    }

    [Fact]
    public void The_waypoint_line_while_unavailable_names_the_reason()
    {
        var display = new Bbs757EhsiDisplay();
        Assert.Equal("EHSI waypoint unavailable: aircraft memory not found",
            display.WaypointLine(null, Bbs757DisplayReasons.MemoryNotFound));
        Assert.Equal("EHSI waypoint unavailable: no reading yet", display.WaypointLine(null, null));
        Assert.Equal("EHSI waypoint unavailable: unexpected values", display.WaypointLine(Corrupt(), Bbs757DisplayReasons.UnexpectedValues));
        Assert.Equal("EHSI waypoint unavailable: no reading yet", display.WaypointLine(Corrupt(), null));
        Assert.Equal("EHSI waypoint unavailable: display dark", display.WaypointLine(Dark(), null));
        Assert.Equal("KOLM, 37.5 NM, no ETA", display.WaypointLine(Light(), Bbs757DisplayReasons.MemoryNotFound));

        // Through the monitor: before it runs there is no reading, and the line says why.
        var f = new Flight(Light());
        Assert.Equal("EHSI waypoint unavailable: no reading yet", f.Display.WaypointLine(f.Monitor.Latest, null));
        f.RunUntilRunning();
        Assert.Equal("KOLM, 37.5 NM, no ETA", f.Display.WaypointLine(f.Monitor.Latest, null));
    }

    // ---- through the monitor ----

    private sealed class ScriptedSampler(Bbs757EhsiReading reading) : IBbs757DisplaySampler<Bbs757EhsiReading>
    {
        public Bbs757EhsiReading Reading { get; set; } = reading;

        /// <summary>The simulator's values of each read the monitor made, oldest first.</summary>
        public List<Bbs757SimValues> Seen { get; } = new();

        public bool TryRead(Bbs757SimValues sim, out Bbs757EhsiReading read)
        {
            Seen.Add(sim);
            return TryRead(out read);
        }

        public bool TryRead(out Bbs757EhsiReading read)
        {
            read = Reading;
            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>The real display in every member but its sampler, which is <paramref name="sampler"/> whatever the module.</summary>
    private sealed class OverScript(Bbs757EhsiDisplay real, ScriptedSampler sampler) : IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>
    {
        private readonly IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> _real = real;

        public string Name => _real.Name;
        public string MuteKey => _real.MuteKey;
        public string LogChannel => _real.LogChannel;
        public int TickMs => _real.TickMs;
        public IReadOnlyList<Bbs757SimInput> SimInputs => _real.SimInputs;

        public IReadOnlyList<Bbs757EhsiLayout> LayoutsIn(ModuleAnalysis analysis) => _real.LayoutsIn(analysis);

        public IBbs757DisplaySampler<Bbs757EhsiReading>? SamplerFor(LocatedModule module, Bbs757EhsiLayout layout) => sampler;

        public bool? Lit(Bbs757EhsiReading reading) => _real.Lit(reading);

        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix) =>
            _real.Changes(heard, now, withPrefix);

        public IReadOnlyList<string> Rows(Bbs757EhsiReading reading) => _real.Rows(reading);
    }

    /// <summary>One flight: the monitor on the real display, the session's build with the EHSI layout, a tick every 500 ms
    /// (the reader's own interval).</summary>
    private sealed class Flight
    {
        public Flight(Bbs757EhsiReading first, params string[] muted)
        {
            Sampler = new ScriptedSampler(first);
            Monitor = new Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>(new OverScript(Display, Sampler));
            Env = new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Simulator = new Bbs757SimValues(new Dictionary<string, double>
                {
                    ["PLANE LATITUDE"] = 47.442946, ["PLANE LONGITUDE"] = -122.303648,
                }),
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { Build(Layout) }, null)),
            };
            foreach (string key in muted) Env.Muted.Add(key);
            Monitor.Start(Env, startTimer: false);
        }

        public Bbs757EhsiDisplay Display { get; } = new();
        public ScriptedSampler Sampler { get; }
        public Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> Monitor { get; }
        public Bbs757DisplayMonitorTests.FakeEnvironment Env { get; }
        public string State => Monitor.StateName;
        public string[] Spoken => Env.Spoken;

        public void Show(Bbs757EhsiReading reading) => Sampler.Reading = reading;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Bbs757EhsiDisplay.TickMs;
            }
        }

        public Flight RunUntilRunning(int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.Equal("Running", State);
            return this;
        }
    }

    // Review Focus: the active waypoint sequencing every few seconds on an approach: a waypoint the top line passes
    // through for less than the settle is never said; the one it rests on is said once, with its distance then; each
    // later change once, never a backlog.
    [Fact]
    public void Quick_sequencing_says_only_the_waypoint_it_rests_on()
    {
        var f = new Flight(Light()).RunUntilRunning();
        f.Tick(4);
        Assert.Empty(f.Spoken);

        // BTG for one reading, then KPDX: only KPDX.
        f.Show(Light() with { Active = Btg });
        f.Tick();
        Assert.Empty(f.Spoken);
        // The window and Ctrl+W follow at once; only speech waits for the settle.
        Assert.StartsWith("BTG, ", f.Display.WaypointLine(f.Monitor.Latest, null));
        f.Show(Light() with { Active = Kpdx });
        f.Tick(6);
        Assert.Equal(new[] { "EHSI: KPDX, 111 NM" }, f.Spoken);

        // Held for seconds while the aircraft moves (every reading differs): nothing more.
        for (int i = 0; i < 10; i++)
        {
            f.Show(Light() with { Active = Kpdx, Latitude = 47.442946 - i * 0.001 });
            f.Tick();
        }
        Assert.Equal(new[] { "EHSI: KPDX, 111 NM" }, f.Spoken);

        // A waypoint and back inside the settle: nothing.
        f.Show(Light() with { Active = Kolm });
        f.Tick();
        f.Show(Light() with { Active = Kpdx });
        f.Tick(6);
        Assert.Equal(new[] { "EHSI: KPDX, 111 NM" }, f.Spoken);

        // Each held change once.
        f.Show(Light() with { Active = Kolm });
        f.Tick(6);
        f.Show(Light() with { Active = null });
        f.Tick(6);
        Assert.Equal(new[] { "EHSI: KPDX, 111 NM", "EHSI: KOLM, 37 NM", "EHSI: no active waypoint" }, f.Spoken);
        Assert.Equal("Running", f.State);
    }

    // Review Focus: the pilot turns the mode or range selector with the window open: the rows follow at the next reading,
    // and nothing is said (the screen reader speaks the panel's combo, CORE-7).
    [Fact]
    public void A_mode_or_range_change_is_shown_not_spoken()
    {
        var f = new Flight(Ehsi()).RunUntilRunning();
        Assert.Contains("Map: KOLM, active, 37.5 NM, 86° right", f.Monitor.View());

        f.Show(Ehsi() with { Range = 40 });
        f.Tick();
        Assert.Contains("EHSI: MAP, range 40", f.Monitor.View());
        Assert.DoesNotContain("Map: KOLM, active, 37.5 NM, 86° right", f.Monitor.View());

        f.Show(Ehsi() with { Range = 80, RangeChanging = true });
        f.Tick();
        Assert.Equal(new[] { "EHSI: MAP, range 80", "Top: HDG 119 M" }, f.Monitor.View());

        f.Show(InMode(Ehsi(), "PLAN") with { Range = 80, Map = PlanFrame, Centered = true });
        f.Tick(4);
        Assert.Contains("EHSI: PLAN, range 80, centered", f.Monitor.View());
        Assert.Contains("Map: T/C, 13.4 NM north-east of KOLM", f.Monitor.View());

        f.Show(InMode(Ehsi(), "VOR") with { ApproachMissing = "not in this layout file" });
        f.Tick(4);
        Assert.Contains("VOR and ILS: not read: not in this layout file", f.Monitor.View());

        // With the IRS off and EXCESS DATA up, a mode change does not speak the MAP flag or the message it hides.
        f.Show(InMode(Ehsi(), "VOR") with { Valid = false, ExcessCount = 100 });
        f.Tick(6);
        int said = f.Spoken.Length;
        f.Show(Ehsi() with { Valid = false, ExcessCount = 100 });
        f.Tick(6);
        f.Show(InMode(Ehsi(), "PLAN") with { Valid = false, ExcessCount = 100, Map = PlanFrame });
        f.Tick(6);
        Assert.Equal(said, f.Spoken.Length);

        Assert.Equal(new[] { "EHSI flag: HDG" }, f.Spoken);
        Assert.Equal("Running", f.State);
    }

    // Review Focus: no route at all (cold and dark, or the FMC empty): the window shows what the EHSI shows, Ctrl+W says
    // "No active waypoint", no stale route.
    [Fact]
    public void No_route_reads_no_active_waypoint()
    {
        var empty = Ehsi() with { Active = null, Route = Array.Empty<Bbs757EhsiLeg>(), Points = new[] { Ksea, KpdxAirport } };
        var f = new Flight(empty).RunUntilRunning();
        var rows = f.Monitor.View();
        Assert.Contains("Top left: no distance", rows);
        Assert.Contains("Top right: no ETA", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Map: KOLM", StringComparison.Ordinal));
        Assert.Contains("Map: KSEA airport, 0.5 NM, 173° left", rows);
        Assert.Equal("No active waypoint", f.Display.WaypointLine(f.Monitor.Latest, null));
        Assert.Empty(f.Spoken);

        // A route entered, then erased: said once each way, and the erased route's rows go with it.
        f.Show(Ehsi());
        f.Tick(4);
        f.Show(empty);
        f.Tick(4);
        Assert.Equal(new[] { "EHSI: KOLM, 37 NM", "EHSI: no active waypoint" }, f.Spoken);
        Assert.DoesNotContain(f.Monitor.View(), r => r.Contains("KOLM", StringComparison.Ordinal));
        Assert.Equal("No active waypoint", f.Display.WaypointLine(f.Monitor.Latest, null));
    }

    [Fact]
    public void A_new_message_is_spoken_once()
    {
        var f = new Flight(Light()).RunUntilRunning();
        f.Show(Light() with { ExcessCount = 140 });
        f.Tick();
        Assert.Empty(f.Spoken);
        Assert.Contains("Message: EXCESS DATA", f.Monitor.View());
        f.Tick(10);
        // The count changes every frame: still one line.
        for (int i = 0; i < 10; i++)
        {
            f.Show(Light() with { ExcessCount = 100 + i * 7 });
            f.Tick();
        }
        Assert.Equal(new[] { "EHSI message: EXCESS DATA" }, f.Spoken);
        f.Show(Light() with { ExcessCount = 20 });
        f.Tick(4);
        Assert.Equal(new[] { "EHSI message: EXCESS DATA", "EHSI message gone: EXCESS DATA" }, f.Spoken);
    }

    // Review Focus (plan 28): BlueBird zeroes and rebuilds the map options' count inside each frame, so a reading can catch
    // it just under or just over the limit. EXCESS DATA is spoken only once it has stood for the monitor's settle, and
    // "gone" only once it has been away that long: a one-reading flicker either way says nothing.
    [Fact]
    public void Excess_data_that_flickers_is_spoken_once()
    {
        var f = new Flight(Light() with { ExcessCount = 99 }).RunUntilRunning();
        f.Tick(4);
        // Over the limit for one reading, three times: nothing.
        for (int i = 0; i < 3; i++)
        {
            f.Show(Light() with { ExcessCount = 100 });
            f.Tick();
            f.Show(Light() with { ExcessCount = 99 });
            f.Tick(2);
        }
        Assert.Empty(f.Spoken);
        // It stands: once.
        f.Show(Light() with { ExcessCount = 101 });
        f.Tick(6);
        Assert.Equal(new[] { "EHSI message: EXCESS DATA" }, f.Spoken);
        // Under the limit for one reading, three times: nothing more.
        for (int i = 0; i < 3; i++)
        {
            f.Show(Light() with { ExcessCount = 98 });
            f.Tick();
            f.Show(Light() with { ExcessCount = 100 });
            f.Tick(2);
        }
        Assert.Equal(new[] { "EHSI message: EXCESS DATA" }, f.Spoken);
        // Gone for good: once.
        f.Show(Light() with { ExcessCount = 60 });
        f.Tick(6);
        Assert.Equal(new[] { "EHSI message: EXCESS DATA", "EHSI message gone: EXCESS DATA" }, f.Spoken);
    }

    [Fact]
    public void A_muted_row_says_nothing_and_the_window_still_shows()
    {
        var f = new Flight(Light(), Bbs757EhsiDisplay.MuteKey).RunUntilRunning();
        f.Show(Light() with { Active = Kpdx, ExcessCount = 120, Valid = false });
        f.Tick(10);
        Assert.Empty(f.Spoken);
        Assert.Contains("Message: EXCESS DATA", f.Monitor.View());
        Assert.Contains("Flag: HDG", f.Monitor.View());
        // Ctrl+W still answers: it is the pilot's question, not a background line.
        Assert.StartsWith("KPDX, 111 NM", f.Display.WaypointLine(f.Monitor.Latest, null));
    }

    [Fact]
    public void Powering_up_is_a_silent_baseline()
    {
        var f = new Flight(Dark());
        f.Tick(20);
        Assert.Equal("Running", f.State);
        Assert.Equal(new[] { "EHSI display not powered" }, f.Monitor.View());
        f.Show(Light() with { ExcessCount = 120, Valid = false });
        f.Tick(30);
        Assert.Empty(f.Spoken);
        Assert.Contains("Message: EXCESS DATA", f.Monitor.View());
    }
}
