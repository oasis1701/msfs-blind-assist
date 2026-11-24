// The 757 ADI as one reader of the 757's display monitor (plan 27, Task 5): its names and keys, its four simulator
// inputs, its layouts and samplers from the shared module session, its power, its rows and its spoken flags; then the
// shared monitor driving the real reader over scripted readings, so the settle, the Ctrl+M row and the dark display act
// as the pilot hears them. Nothing judges a reading by a simulator value (B757-30).

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757AdiTextTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757AdiDisplayTests
{
    private const ulong Base = 0x20000000;
    private const string Key = "MSFSBA_BBS757_ADI_MESSAGES";

    private static readonly Bbs757AdiLayout Layout = Bbs757AdiLayoutTests.Sample();

    private static ModuleBuild Build(Bbs757AdiLayout? layout, string layoutFile = "BBS752.json") =>
        new(Bbs757Module.Description, "BBS752.wasm", layoutFile,
            layout is null ? Array.Empty<ReaderResult>() : new[] { new ReaderResult(Bbs757Module.Adi, layout, null) });

    private static Bbs757AdiReading Flagged() => Reading(r => r with { Attitude = r.Attitude! with { IrsState = 0 } });

    private static Bbs757AdiReading Dark() => Reading(r => r with { Lit = false });

    private sealed class CountingHandle : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    [Fact]
    public void The_reader_is_the_adi_on_the_adi_messages_row()
    {
        Assert.Equal("ADI", Bbs757AdiDisplay.Name);
        Assert.Equal("bbs757-adi", Bbs757AdiDisplay.LogChannel);
        Assert.Equal(500, Bbs757AdiDisplay.TickMs);
        Assert.Equal(Key, Bbs757AdiDisplay.MuteKey);
        Assert.Equal("ADI messages", Bbs757AdiDisplay.MonitorRowLabel);

        IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading> reader = new Bbs757AdiDisplay();
        Assert.Equal("ADI", reader.Name);
        Assert.Equal(Key, reader.MuteKey);
        Assert.Equal("bbs757-adi", reader.LogChannel);
        Assert.Equal(500, reader.TickMs);
    }

    [Fact]
    public void The_reader_takes_the_four_simulator_variables_the_draw_takes()
    {
        // S10: the pfd's four simulator handles hold no value in memory, so the sampler takes the same variables: stock
        // ones, in the units BlueBird registers them in, never L:vars of those names ([VAR-2]) and never a check of
        // memory (B757-30).
        var inputs = new[]
        {
            new Bbs757SimInput("PLANE BANK DEGREES", "PLANE BANK DEGREES", SimVarType.SimVar, "radians"),
            new Bbs757SimInput("PLANE PITCH DEGREES", "PLANE PITCH DEGREES", SimVarType.SimVar, "radians"),
            new Bbs757SimInput("GROUND VELOCITY", "GROUND VELOCITY", SimVarType.SimVar, "knots"),
            new Bbs757SimInput("AIRSPEED MACH", "AIRSPEED MACH", SimVarType.SimVar, "mach"),
        };
        IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading> reader = new Bbs757AdiDisplay();
        Assert.Equal(inputs, Bbs757AdiDisplay.SimInputs);
        Assert.Equal(inputs, reader.SimInputs);
        Assert.False(Bbs757AdiDisplay.SimInputs is Bbs757SimInput[], "the inputs cannot be changed through a cast");
        // The sampler looks each up by the key the display declares.
        Assert.Equal(new[] { Bbs757AdiReader.BankKey, Bbs757AdiReader.PitchKey, Bbs757AdiReader.GroundSpeedKey, Bbs757AdiReader.MachKey },
            inputs.Select(i => i.Key));

        // The monitor asks the simulator for exactly these each tick and hands them to the sampler.
        var f = new Flight(Reading()).RunUntilRunning();
        Assert.Equal(inputs.Select(i => i.Key).Order(), f.Env.KeysAsked.Distinct().Order());
        Assert.Equal(0.78, f.Sampler.Seen[^1]["AIRSPEED MACH"]);
    }

    [Fact]
    public void Lit_is_the_displays_power()
    {
        var reader = new Bbs757AdiDisplay();
        Assert.True(reader.Lit(Reading()));
        Assert.False(reader.Lit(Dark()));
    }

    [Fact]
    public void Layouts_are_each_builds_adi_layout_once_in_file_order()
    {
        var other = Layout with { IlsBlock = 140 };
        var analysis = new ModuleAnalysis(new[]
        {
            Build(Layout, "a.json"), Build(null, "b.json"), Build(other, "c.json"), Build(Layout with { SourceFile = "x" }, "d.json"),
        }, null);
        Assert.Equal(new[] { Layout, other }, new Bbs757AdiDisplay().LayoutsIn(analysis));
        Assert.Empty(new Bbs757AdiDisplay().LayoutsIn(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "BlueBird 757 files not found")));
    }

    [Fact]
    public void A_sampler_reads_the_builds_own_layout_on_its_own_lease()
    {
        var handle = new CountingHandle();
        var shared = SharedHandle.Open(handle, out var sessionLease);
        var bytes = new byte[0x1000];
        var source = new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);
        var module = new LocatedModule(Build(Layout), new ModuleAnchor(Layout.AnchorAddress, Layout.Anchor),
            new ModuleMemory(source, Base), shared);
        var display = new Bbs757AdiDisplay();

        Assert.Null(display.SamplerFor(module, Layout with { IlsBlock = 140 }));

        var reader = Assert.IsType<Bbs757AdiReader>(display.SamplerFor(module, Layout));
        Assert.Equal(Layout, reader.Layout);
        Assert.False(reader.TryRead(out _));   // this module holds no pfd: not found

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
    public void Rows_and_changes_are_the_adi_text(string culture)
    {
        IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading> reader = new Bbs757AdiDisplay();
        Assert.Equal(TestCulture.Run(culture, () => Bbs757AdiText.Rows(Reading())), TestCulture.Run(culture, () => reader.Rows(Reading())));
        Assert.NotEmpty(reader.Rows(Reading()));

        // Every line is on the one Ctrl+M row.
        Assert.Equal(new[] { new Bbs757SpokenLine(Key, "ADI flag: ATT") },
            TestCulture.Run(culture, () => reader.Changes(Reading(), Flagged(), withPrefix: true)));
        Assert.Equal(new[] { new Bbs757SpokenLine(Key, "flag gone: ATT") },
            TestCulture.Run(culture, () => reader.Changes(Flagged(), Reading(), withPrefix: false)));
        Assert.Empty(reader.Changes(Flagged(), Flagged(), withPrefix: true));
    }

    // ---- through the monitor ------------------------------------------------------------------------------------

    private sealed class ScriptedSampler(Bbs757AdiReading reading) : IBbs757DisplaySampler<Bbs757AdiReading>
    {
        public Bbs757AdiReading Reading { get; set; } = reading;

        /// <summary>The simulator's values of each read the monitor made, oldest first.</summary>
        public List<Bbs757SimValues> Seen { get; } = new();

        public bool TryRead(Bbs757SimValues sim, out Bbs757AdiReading read)
        {
            Seen.Add(sim);
            return TryRead(out read);
        }

        public bool TryRead(out Bbs757AdiReading read)
        {
            read = Reading;
            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>The real display in every member but its sampler, which is <paramref name="sampler"/> whatever the module.</summary>
    private sealed class OverScript(Bbs757AdiDisplay real, ScriptedSampler sampler) : IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading>
    {
        private readonly IBbs757DisplayReader<Bbs757AdiLayout, Bbs757AdiReading> _real = real;

        public string Name => _real.Name;
        public string MuteKey => _real.MuteKey;
        public string LogChannel => _real.LogChannel;
        public int TickMs => _real.TickMs;
        public IReadOnlyList<Bbs757SimInput> SimInputs => _real.SimInputs;

        public IReadOnlyList<Bbs757AdiLayout> LayoutsIn(ModuleAnalysis analysis) => _real.LayoutsIn(analysis);

        public IBbs757DisplaySampler<Bbs757AdiReading>? SamplerFor(LocatedModule module, Bbs757AdiLayout layout) => sampler;

        public bool? Lit(Bbs757AdiReading reading) => _real.Lit(reading);

        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757AdiReading heard, Bbs757AdiReading now, bool withPrefix) =>
            _real.Changes(heard, now, withPrefix);

        public IReadOnlyList<string> Rows(Bbs757AdiReading reading) => _real.Rows(reading);
    }

    /// <summary>One flight: the monitor on the real display, the session's build with the ADI layout, a tick every 500 ms
    /// (the reader's own interval).</summary>
    private sealed class Flight
    {
        public Flight(Bbs757AdiReading first, params string[] muted)
        {
            Sampler = new ScriptedSampler(first);
            Monitor = new Bbs757DisplayMonitor<Bbs757AdiLayout, Bbs757AdiReading>(new OverScript(Display, Sampler));
            Env = new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Simulator = new Bbs757SimValues(new Dictionary<string, double>
                {
                    ["PLANE PITCH DEGREES"] = -0.04, ["PLANE BANK DEGREES"] = 0.1, ["GROUND VELOCITY"] = 240, ["AIRSPEED MACH"] = 0.78,
                }),
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { Build(Layout) }, null)),
            };
            foreach (string key in muted) Env.Muted.Add(key);
            Monitor.Start(Env, startTimer: false);
        }

        public Bbs757AdiDisplay Display { get; } = new();
        public ScriptedSampler Sampler { get; }
        public Bbs757DisplayMonitor<Bbs757AdiLayout, Bbs757AdiReading> Monitor { get; }
        public Bbs757DisplayMonitorTests.FakeEnvironment Env { get; }
        public string State => Monitor.StateName;
        public string[] Spoken => Env.Spoken;

        public void Show(Bbs757AdiReading reading) => Sampler.Reading = reading;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Bbs757AdiDisplay.TickMs;
            }
        }

        public Flight RunUntilRunning(int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.Equal("Running", State);
            return this;
        }
    }

    [Fact]
    public void A_flag_is_said_once_after_it_has_settled()
    {
        // The display is lit from the start (its power-up was not seen): the first good reading is the silent baseline.
        var f = new Flight(Reading()).RunUntilRunning();
        f.Tick(4);
        Assert.Empty(f.Spoken);

        f.Show(Flagged());
        f.Tick(3);
        Assert.Equal(new[] { "ADI flag: ATT" }, f.Spoken);
        f.Tick(20);
        Assert.Single(f.Spoken);
        Assert.Contains("Center: ATT flag", f.Monitor.View());

        f.Show(Reading());
        f.Tick(3);
        Assert.Equal(new[] { "ADI flag: ATT", "ADI flag gone: ATT" }, f.Spoken);
    }

    [Fact]
    public void A_flag_already_showing_at_the_first_reading_is_not_spoken()
    {
        var f = new Flight(Flagged()).RunUntilRunning();
        f.Tick(20);
        Assert.Empty(f.Spoken);
        Assert.Contains("Center: ATT flag", f.Monitor.View());
    }

    [Fact]
    public void A_muted_row_says_nothing_and_still_lists_the_flag()
    {
        var f = new Flight(Reading(), Key).RunUntilRunning();
        f.Show(Flagged());
        f.Tick(10);
        Assert.Empty(f.Spoken);
        Assert.Contains("Center: ATT flag", f.Monitor.View());
    }

    [Fact]
    public void Numbers_that_move_every_reading_say_nothing_and_keep_the_reader_running()
    {
        // An approach: the airspeed, the pointers, the radio altitude and the attitude change at every reading. Nothing
        // is spoken, the window follows, and the reader never stops.
        var f = new Flight(Reading()).RunUntilRunning();
        for (int i = 0; i <= 120; i++)
        {
            double feet = 1500 - 12.0 * i;
            double gs = -0.5 + i * 0.01;
            double pitch = -2.5 + 0.02 * i;
            f.Show(Reading(
                r => r with
                {
                    RadioAltitude = r.RadioAltitude! with { RaAltitude = feet + 17 },
                    Ils = r.Ils! with { GsPointer = gs },
                    Speed = r.Speed! with { Airspeed = 250 - i },
                },
                Bbs757AdiReaderTests.In(pitch: pitch)));
            f.Tick();
            Assert.Equal("Running", f.State);
        }
        Assert.Empty(f.Spoken);
        Assert.Contains("Left: airspeed 130", f.Monitor.View());
        Assert.Contains("Right: radio altitude 60", f.Monitor.View());   // 1,500 − 1,440 = 60, stored 77: 60.07
    }

    [Fact]
    public void A_dark_display_lists_not_powered_and_lighting_up_is_a_silent_baseline()
    {
        var f = new Flight(Reading()).RunUntilRunning();
        f.Tick(2);
        f.Show(Dark());
        f.Tick(3);
        Assert.Equal(new[] { "ADI display not powered" }, f.Monitor.View());
        Assert.Empty(f.Spoken);
        Assert.False(f.Monitor.KeyFallsBackToAi());   // the reader runs: the window opens and says so

        // It lights with the ATT flag showing: never spoken, in its quiet or after it.
        f.Show(Flagged());
        f.Tick(30);
        Assert.Empty(f.Spoken);
        Assert.Contains("Center: ATT flag", f.Monitor.View());
        // A change after that is.
        f.Show(Reading());
        f.Tick(3);
        Assert.Equal(new[] { "ADI flag gone: ATT" }, f.Spoken);
    }
}
