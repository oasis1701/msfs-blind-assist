// Plan 27 Task 6: the ADI reader on the 757 definition: its monitor (registered last, S16), its "ADI messages" Ctrl+M row,
// its four simulator inputs on SimConnect's batch, and Alt+P, which opens the one "ADI" window (the FMA's rows first, then
// the ADI's) while the ADI reader can run and falls back to the AI read of the ADI when it cannot, whatever the FMA does.
// The monitors here are the definition's own, each run over synthetic module memory (the FMA's of the FMA reader's
// tests, the ADI's gate of the ADI reader's tests); the window is a real WinForms form, as in the other readers' wiring
// tests.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757AdiWiringTests
{
    private static readonly string[] FmaRows = { "A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: TO, armed G/S", "AFDS: CMD" };

    /// <summary>The gate's rows (Bbs757AdiTextTests.The_gate_reads_as_the_capture_showed).</summary>
    private static readonly string[] GateRows =
    {
        "Left: airspeed 30",
        "Left: target speed 200",
        "Left: V1 INOP",
        "Left: ground speed 0",
        "Center: pitch 0.5 down, wings level",
        "Center: flight director, pitch bar 8.5 above, roll bar centered",
        "Right: glideslope, pointer at full scale, up",
        "Right: DH 200",
        "Right: radio altitude -6",
        "Bottom: localizer, pointer at full scale, right",
    };

    private static string[] Lines(BBS757DisplayForm form) =>
        form.Controls.OfType<DisplayListBox>().Single().Items.Cast<object>().Select(o => o.ToString()!).ToArray();

    [Fact]
    public void Alt_P_opens_the_adi_window_with_the_fma_first()
    {
        using var rig = new Rig();
        rig.RunFma();
        rig.RunAdi();

        Assert.True(rig.PressAltP());

        Assert.Empty(rig.AiReads);
        var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(rig.Def.Displays.Windows));
        Assert.Equal("ADI", window.Text);
        Assert.Equal("ADI", window.AccessibleName);
        Assert.True(window.Visible);
        var list = window.Controls.OfType<DisplayListBox>().Single();
        Assert.Equal("Captain ADI", list.AccessibleName);
        Assert.Empty(window.Controls.OfType<FlowLayoutPanel>());   // no controls area: the list alone
        // The FMA's rows (plan 23's, from its own monitor), then everything else the ADI draws.
        Assert.Equal(FmaRows.Concat(GateRows), Lines(window));
        Assert.Empty(rig.Speech.All);   // the window's title and list are what the screen reader says (CORE-7)

        // Both follow their displays: the next refresh shows a change of either.
        Bbs757FmaReaderTests.Put(rig.FmaBytes!, Bbs757FmaReaderTests.Tmc + 444, 2);
        rig.Tick();
        window.RefreshRows();
        Assert.Equal("A/T: EPR", Lines(window)[0]);
        Assert.Equal(GateRows, Lines(window).Skip(4));

        // Again: the same window, made once and shown again.
        window.Hide();
        Assert.True(rig.PressAltP());
        Assert.Same(window, Assert.Single(rig.Def.Displays.Windows));
        Assert.True(window.Visible);
        Assert.Empty(rig.AiReads);

        // Owned: a switch away closes it for good and stops the monitor.
        rig.Def.OnSwitchedAway();
        Assert.True(window.IsDisposed);
        Assert.Equal("Stopped", rig.Def.Adi.StateName);
    }

    [Fact]
    public void Alt_P_exits_output_hotkey_mode_before_showing_the_window()
    {
        // As Alt+E and Alt+N do: the window takes the keyboard, so output mode would otherwise eat its first key.
        using var rig = new Rig();
        rig.RunFma();
        rig.RunAdi();
        var outputMode = typeof(HotkeyManager).GetField("outputHotkeyModeActive",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.True(outputMode is not null,
            "HotkeyManager has no private field outputHotkeyModeActive: this test sets it to enter output hotkey mode; follow its rename");
        outputMode.SetValue(rig.Hotkeys, true);
        Assert.True(rig.Hotkeys.IsOutputHotkeyModeActive);

        Assert.True(rig.PressAltP());

        Assert.False(rig.Hotkeys.IsOutputHotkeyModeActive);
    }

    [Fact]
    public void Alt_P_falls_back_when_the_adi_reader_cannot_run_even_with_the_fma_running()
    {
        // The ADI reader never started.
        using (var rig = new Rig())
        {
            rig.RunFma();
            Assert.True(rig.Def.Adi.KeyFallsBackToAi());
            Assert.False(rig.Def.Fma.KeyFallsBackToAi());

            Assert.True(rig.PressAltP());

            Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, rig.AiReads);
            Assert.Empty(rig.Def.Displays.Windows);   // no window made
            Assert.Empty(rig.Speech.All);             // the AI read says its own words, nothing of ours first
            Assert.Equal(FmaRows, rig.Def.Fma.View());   // Ctrl+P still lists the FMA
        }

        // The ADI reader unavailable (the layout file has no ADI layout), the FMA running beside it.
        using (var rig = new Rig())
        {
            rig.RunFma();
            rig.FailAdi();
            Assert.Equal("Unavailable", rig.Def.Adi.StateName);
            Assert.Equal("Running", rig.Def.Fma.StateName);

            Assert.True(rig.PressAltP());

            Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, rig.AiReads);
            Assert.Empty(rig.Def.Displays.Windows);
            Assert.Equal(FmaRows, rig.Def.Fma.View());
        }

        // A window opened while the reader ran is not shown again once it cannot.
        using (var rig = new Rig())
        {
            rig.RunFma();
            rig.RunAdi();
            rig.PressAltP();
            var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(rig.Def.Displays.Windows));
            window.Hide();
            rig.Def.Adi.Stop();

            Assert.True(rig.PressAltP());

            Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, rig.AiReads);
            Assert.False(window.Visible);
        }
    }

    [Fact]
    public void The_fma_status_row_heads_the_window_when_the_fma_is_down()
    {
        using var rig = new Rig();
        rig.FailFma();
        rig.RunAdi();
        string status = Bbs757DisplaySpeech.Unavailable("FMA", Bbs757DisplayReasons.FileNotRecognised);
        Assert.Equal(new[] { status }, rig.Def.Fma.View());

        Assert.True(rig.PressAltP());

        Assert.Empty(rig.AiReads);   // the ADI reader runs: the window, whatever the FMA does
        var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(rig.Def.Displays.Windows));
        Assert.Equal(new[] { status }.Concat(GateRows), Lines(window));
    }

    [Fact]
    public void Alt_P_opens_the_window_while_the_readers_wait_and_it_says_so()
    {
        // Started, the simulator not up yet: the window opens and each reader's one row says it waits.
        using var rig = new Rig();
        rig.Def.StartDisplays(new NoFiles(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), rig.Speech);
        Assert.False(rig.Def.Adi.KeyFallsBackToAi());

        Assert.True(rig.PressAltP());

        Assert.Empty(rig.AiReads);
        var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(rig.Def.Displays.Windows));
        Assert.Equal(new[] { "FMA reader waiting for the simulator", "ADI reader waiting for the simulator" }, Lines(window));
    }

    [Fact]
    public void A_dark_adi_lists_not_powered_under_the_fma()
    {
        using var rig = new Rig();
        rig.RunFma();
        rig.RunAdi(Bbs757AdiReaderTests.Gate(b => b[Bbs757AdiReaderTests.Pfd + Bbs757AdiLayoutTests.Sample().Powered] = 0));

        Assert.True(rig.PressAltP());

        Assert.Empty(rig.AiReads);   // the reader runs: its display is dark, and the window says so
        var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(rig.Def.Displays.Windows));
        Assert.Equal(FmaRows.Append("ADI display not powered"), Lines(window));
    }

    [Fact]
    public void The_ctrl_m_row_is_listed()
    {
        var def = new BBS757Definition();
        var rows = def.MonitorRows();

        var row = new MonitorRow(Bbs757AdiDisplay.MuteKey, "ADI messages");
        Assert.Contains(row, rows);
        Assert.Contains(row, def.Displays.Rows);
        Assert.Equal(row, def.Displays.Rows[^1]);   // the last reader's row
        Assert.Equal(row, rows[^1]);
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_adi_monitor_registers_last_and_cannot_run_before_the_readers_start()
    {
        var def = new BBS757Definition();

        Assert.Same(def.Adi, def.Displays.Monitors[^1]);   // S16: after the FMA, both EICAS and the EHSI
        Assert.Same(def.Fma, def.Displays.Monitors[0]);
        Assert.Single(def.Displays.Monitors.OfType<Bbs757DisplayMonitor<Bbs757AdiLayout, Bbs757AdiReading>>());
        Assert.True(def.Adi.KeyFallsBackToAi());
        Assert.Equal(Bbs757AdiDisplay.SimInputs, def.Adi.SimInputs);
    }

    [Fact]
    public void The_adis_inputs_get_their_hidden_batched_copies_in_their_own_units()
    {
        // BBS Nest "running", or the first delivery below would speak the PC's real answer (none on a CI runner).
        var def = new BBS757Definition { NestRunning = () => true };
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        // Each of the four is read from one streamed variable of its name, type and units ([VAR-7]: one batched variable
        // of a name), never an L:var of that name ([VAR-2]).
        Assert.Equal(4, Bbs757AdiDisplay.SimInputs.Count);
        foreach (var input in Bbs757AdiDisplay.SimInputs)
        {
            var carrier = vars[keys[input.Key]];
            Assert.Equal((input.Name, SimVarType.SimVar), (carrier.Name, carrier.Type));
            Assert.Equal(input.Units, carrier.Units, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(UpdateFrequency.Continuous, carrier.UpdateFrequency);
            Assert.True(carrier.IsAnnounced);
            Assert.True(ContinuousBatchLayout.RidesBatch(carrier));
            Assert.Single(vars, kv => kv.Value.Name == input.Name && ContinuousBatchLayout.RidesBatch(kv.Value));
            Assert.DoesNotContain(vars.Values, v => v.Type == SimVarType.LVar && v.Name == input.Name);
        }

        // The pitch, the bank and the Mach number get a hidden copy: nothing streamed them (the base's pitch and bank are
        // read on request, for hand fly). In radians and mach, as BlueBird registers them.
        foreach (var (name, units) in new[] { ("PLANE BANK DEGREES", "radians"), ("PLANE PITCH DEGREES", "radians"), ("AIRSPEED MACH", "mach") })
        {
            string copyKey = BBS757Definition.DisplayInputCopyPrefix + name;
            Assert.Equal(copyKey, keys[name]);
            var copy = vars[copyKey];
            Assert.Equal((name, SimVarType.SimVar, units), (copy.Name, copy.Type, copy.Units));
            Assert.True(copy.ExcludeFromMonitorManager);   // hidden: no Ctrl+M row ([VAR-9])
            Assert.False(copy.ExcludeFromBatch);
            Assert.EndsWith(" (display reader input)", copy.DisplayName);
            Assert.DoesNotContain(def.MonitorRows(), r => r.Key == copyKey);

            // And it is consumed silently.
            var speech = new SpeechCapture();
            Assert.True(def.ProcessSimVarUpdate(copyKey, 0.1, speech));
            Assert.Empty(speech.All);
        }
        // The ground speed already rides the batch in knots (the ground speed callouts): read from it, with no copy.
        Assert.Equal("GROUND_VELOCITY", keys["GROUND VELOCITY"]);
        Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + "GROUND VELOCITY", vars.Keys);

        // The readers' inputs in all: the upper EICAS's one, the EHSI's five and these four.
        Assert.Equal(10, def.Displays.SimInputs.Count);
        Assert.Equal(10, keys.Count);
        Assert.True(vars.Count < 900, $"{vars.Count} variables");   // the data-definition ceiling's test
    }

    [Fact]
    public void The_environment_hands_the_sampler_all_four_inputs_from_the_cache()
    {
        // The definition's own keys, through the real environment: the four values reach the sampler under the names it
        // looks them up by, and one not delivered yet makes the monitor wait.
        var def = new BBS757Definition();
        var keys = def.DisplayCacheKeys();
        var cache = new Dictionary<string, double>
        {
            [keys["PLANE PITCH DEGREES"]] = -0.04, [keys["PLANE BANK DEGREES"]] = 0.1,
            [keys["GROUND VELOCITY"]] = 142, [keys["AIRSPEED MACH"]] = 0.21,
        };
        using var session = new ModuleSession(Bbs757Module.Description, new NoFiles(), new NoLayouts());
        var env = new Bbs757DisplayEnvironment(() => true, () => true, () => "BlueBird 757-200 RR", key => cache.TryGetValue(key, out double v) ? v : null,
            new Dictionary<string, string>(keys), () => new UserSettings(), new SpeechCapture(), session, new Bbs757DisplayFilesNotice());
        var asked = def.Adi.SimInputs.Select(i => i.Key).ToArray();

        var sim = Bbs757AdiReader.SimOf(env.ReadSimulator(asked)!.Value);

        Assert.Equal(new Bbs757AdiSim(-0.04, 0.1, 142, 0.21), sim);
        cache.Remove(keys["AIRSPEED MACH"]);
        Assert.Null(env.ReadSimulator(asked));
    }

    // ---- the rig ----

    /// <summary>A definition whose FMA and ADI monitors are each run over synthetic module memory, on a fake
    /// environment of its own, and a key pressed through the definition's hotkey path.</summary>
    internal sealed class Rig : IDisposable
    {
        public BBS757Definition Def { get; } = new() { NestRunning = () => true, StartMonitor = StartWithoutTimer };
        public SpeechCapture Speech { get; } = new();
        public List<HotkeyAction> AiReads { get; } = new();
        public byte[]? FmaBytes { get; private set; }
        public HotkeyManager Hotkeys { get; } = new();

        /// <summary>The memory the ADI monitor reads, once <see cref="RunAdi"/> has run: a test changes it in place.</summary>
        public byte[]? AdiBytes { get; private set; }

        private readonly SimConnectManager _sim = new(IntPtr.Zero);
        private readonly List<IDisposable> _leases = new();
        private readonly List<(Action Tick, Bbs757DisplayMonitorTests.FakeEnvironment Env)> _running = new();

        public Rig()
        {
            Def.AiReadOverride = a => { AiReads.Add(a); return true; };
            _ = Def.Displays;
        }

        public bool PressAltP() => Press(HotkeyAction.ReadDisplayPFD);

        /// <summary>A key through the definition's hotkey path, on a simulator that is not connected.</summary>
        public bool Press(HotkeyAction action) => Def.HandleHotkeyAction(action, _sim, Speech, null!, Hotkeys);

        /// <summary>The FMA monitor running over the FMA reader's test memory.</summary>
        public void RunFma()
        {
            FmaBytes = Bbs757FmaReaderTests.Memory();
            var layout = Bbs757FmaReaderTests.Layout;
            var env = Environment(Bbs757Module.Fma, layout, new ModuleAnchor(layout.AnchorAddress, layout.Anchor),
                new ModuleMemory(Bbs757FmaReaderTests.Source(FmaBytes), Bbs757FmaReaderTests.Base));
            Def.Fma.Start(env, startTimer: false);
            Run(Def.Fma.Tick, env, () => Def.Fma.StateName);
        }

        /// <summary>The ADI monitor running over the ADI reader's gate (or <paramref name="bytes"/>), through the sample
        /// layout (or <paramref name="layout"/>), with the gate's simulator values.</summary>
        public void RunAdi(byte[]? bytes = null, Bbs757AdiLayout? layout = null)
        {
            layout ??= Bbs757AdiLayoutTests.Sample();
            var source = new FakeProcessMemory();
            AdiBytes = bytes ??= Bbs757AdiReaderTests.Gate();
            source.Add(new MemoryRegion(Bbs757AdiReaderTests.Base, Bbs757AdiReaderTests.Base, (ulong)bytes.Length, true, true, true), bytes);
            var env = Environment(Bbs757Module.Adi, layout, new ModuleAnchor(layout.AnchorAddress, layout.Anchor),
                new ModuleMemory(source, Bbs757AdiReaderTests.Base));
            env.Simulator = Bbs757AdiReaderTests.Sim();
            Def.Adi.Start(env, startTimer: false);
            Run(Def.Adi.Tick, env, () => Def.Adi.StateName);
        }

        /// <summary>The FMA monitor unavailable: the layout file loaded, with no FMA layout in it.</summary>
        public void FailFma()
        {
            var env = new Bbs757DisplayMonitorTests.FakeEnvironment();
            Def.Fma.Start(env, startTimer: false);
            Run(Def.Fma.Tick, env, () => Def.Fma.StateName, until: "Unavailable");
        }

        /// <summary>The ADI monitor unavailable: the layout file loaded, with no ADI layout in it.</summary>
        public void FailAdi()
        {
            var env = new Bbs757DisplayMonitorTests.FakeEnvironment();
            Def.Adi.Start(env, startTimer: false);
            Run(Def.Adi.Tick, env, () => Def.Adi.StateName, until: "Unavailable");
        }

        /// <summary>One more tick of every running monitor, past its settle.</summary>
        public void Tick()
        {
            foreach (var (tick, env) in _running)
            {
                for (int i = 0; i < 5; i++)
                {
                    env.NowMs += 500;
                    tick();
                }
            }
        }

        private Bbs757DisplayMonitorTests.FakeEnvironment Environment(string reader, object layout, ModuleAnchor anchor, ModuleMemory memory)
        {
            var build = new ModuleBuild(Bbs757Module.Description, "BBS752.wasm", "BBS752.json", new[] { new ReaderResult(reader, layout, null) });
            var shared = SharedHandle.Open(new Handle(), out var sessionLease);
            _leases.Add(sessionLease);
            var located = new LocatedModule(build, anchor, memory, shared);
            return new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { build }, null)),
                Locate = () => Task.FromResult(new ModuleLocation(new[] { located }, null)),
            };
        }

        private void Run(Action tick, Bbs757DisplayMonitorTests.FakeEnvironment env, Func<string> state, string until = "Running")
        {
            for (int i = 0; i < 40 && state() != until; i++)
            {
                env.NowMs += 500;
                tick();
            }
            Assert.Equal(until, state());
            _running.Add((tick, env));
        }

        public void Dispose()
        {
            Def.OnSwitchedAway();
            foreach (var lease in _leases) lease.Dispose();
            Hotkeys.Dispose();
        }

        private sealed class Handle : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>Starts the FMA and the ADI without their WinForms timers (the tests tick them); any other monitor as the
    /// app would.</summary>
    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        switch (monitor)
        {
            case Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma: fma.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757AdiLayout, Bbs757AdiReading> adi: adi.Start(env, startTimer: false); break;
            default: monitor.Start(env); break;
        }
    }

    private sealed class NoFiles : IModuleHost
    {
        public long NowMs => 0;
        public string SimulatorVersion => "FS2024";
        public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log) => Array.Empty<string>();
        public byte[] ReadFile(string path) => throw new InvalidOperationException($"the session read {path}");

        public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
        {
            failure = null;
            return null;
        }

        public void Log(string line) { }

        public Task<T> RunInBackground<T>(string name, Func<T> work)
        {
            try { return Task.FromResult(work()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }
    }

    private sealed class NoLayouts : ILayoutSource
    {
        public LayoutLookup Find(string wasmFile) => new(null, "no layout");
    }
}
