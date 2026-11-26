// The 757 definition's lower EICAS seam (plan 25 Task 6): the monitor, which takes no simulator input (B757-30), its
// Ctrl+M row, and Alt+S, which opens the one "Lower EICAS" window while the reader runs and falls back to the AI read when
// it cannot.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757LowerEicasWiringTests
{
    private static Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> LowerMonitor(BBS757Definition def) =>
        def.Displays.Monitors.OfType<Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>>().Single();

    // Starts every monitor without its WinForms timer (the tests never tick them).
    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        if (monitor is Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> lower) lower.Start(env, startTimer: false);
        else if (monitor is Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma) fma.Start(env, startTimer: false);
    }

    [Fact]
    public void Alt_S_opens_the_lower_eicas_window_while_the_reader_runs()
    {
        var def = new BBS757Definition { StartMonitor = StartWithoutTimer };
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        def.StartDisplays(new FakeHost(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var monitor = LowerMonitor(def);
        Assert.False(monitor.KeyFallsBackToAi());   // started: it waits for the simulator, and the window says so

        try
        {
            Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
                new SpeechCapture(), null!, null!));

            Assert.Empty(aiReads);
            var window = Assert.IsType<BBS757DisplayForm>(Assert.Single(def.Displays.Windows));
            Assert.Equal("Lower EICAS", window.Text);
            Assert.True(window.Visible);

            // Again: the same window, made once.
            window.Hide();
            Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
                new SpeechCapture(), null!, null!));
            Assert.Same(window, Assert.Single(def.Displays.Windows));
            Assert.True(window.Visible);
            Assert.Empty(aiReads);
        }
        finally
        {
            def.OnSwitchedAway();
        }
        Assert.All(def.Displays.Windows, w => Assert.True(w.IsDisposed));   // owned: a switch away closes it for good
    }

    [Fact]
    public void Alt_S_falls_back_to_the_ai_read_when_unavailable()
    {
        var def = new BBS757Definition();   // the readers never started: the AI read
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        Assert.True(LowerMonitor(def).KeyFallsBackToAi());

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayLowerECAM, new SimConnectManager(IntPtr.Zero),
            new SpeechCapture(), null!, null!));

        Assert.Equal(new[] { HotkeyAction.ReadDisplayLowerECAM }, aiReads);
        Assert.Empty(def.Displays.Windows);   // no window made
    }

    [Fact]
    public void The_ctrl_m_row_is_listed()
    {
        var def = new BBS757Definition();

        var row = new MonitorRow(Bbs757LowerEicasDisplay.MuteKey, "EICAS status messages");

        Assert.Contains(row, def.Displays.Rows);
        Assert.Contains(row, def.MonitorRows());
    }

    [Fact]
    public void The_lower_eicas_takes_no_simulator_input_and_adds_no_variable()
    {
        // Its N2 and page gate is gone (B757-30, the owner's flight of 2026-11-10): the reader takes nothing of the
        // simulator's, so the definition adds no copy for it. The copy-building it used (an input whose name the Engines
        // panel's N2 rows stream off the batch) is still covered against a list of inputs, in Bbs757DisplaysWiringTests.
        var def = new BBS757Definition();
        var vars = def.GetVariables();

        Assert.Empty(LowerMonitor(def).SimInputs);
        IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> reader = new Bbs757LowerEicasDisplay();
        Assert.Empty(reader.SimInputs);
        foreach (string gone in new[] { "BBS757_LOWER_EICAS_N2_1", "BBS757_LOWER_EICAS_N2_2", "BBS_ECAM_Mode" })
            Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + gone, vars.Keys);
        Assert.DoesNotContain(vars.Values, v => v.Name.StartsWith("TURB ENG N2", StringComparison.Ordinal)
            && v.DisplayName.EndsWith("(display reader input)", StringComparison.Ordinal));
    }

    // ENGINE and STATUS buttons (Task 9): built in memory, never from the generated map's labels.
    private static Bbs757Control Key(string node, string label) => new()
    {
        Node = node, Label = label, Kind = Bbs757Kinds.Button,
        Press = $"1 (>L:{node}, bool)", Release = $"0 (>L:{node}, bool)",
    };

    private sealed class Rig
    {
        public readonly List<string> Writes = new();
        public bool CanWrite = true;
        public readonly SpeechCapture Speech = new();
        public BBS757Definition Def;
        public Rig(bool omitStatus = false)
        {
            var controls = new List<Bbs757Control> { Key("B757_EICAS_L_STATUS_BUTTON", "EICAS L Status") };
            if (!omitStatus) controls.Add(Key("B757_EICAS_R_STATUS_BUTTON", "EICAS R Status"));
            Def = new BBS757Definition(new Bbs757ControlMap { Controls = controls })
            {
                NestRunning = () => true,
                WriteIo = _ => new Bbs757SimIo
                {
                    CanWrite = () => CanWrite,
                    ReadFresh = (_, _) => Task.FromResult<double?>(null),
                    Cached = _ => null,
                    Write = rpn => Writes.Add(rpn),
                    RequestRead = _ => { },
                    Delay = (_, ct) => Task.CompletedTask,
                },
            };
        }

        public System.Windows.Forms.FlowLayoutPanel Panel(bool running) =>
            Def.LowerEicasButtons(() => running, new SimConnectManager(IntPtr.Zero), Speech);
    }

    private static void Click(System.Windows.Forms.FlowLayoutPanel panel, string name) =>
        typeof(System.Windows.Forms.Button).GetMethod("OnClick",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(panel.Controls.Cast<System.Windows.Forms.Button>().Single(b => b.Text == name), new object[] { EventArgs.Empty });

    [Fact]
    public void The_window_has_engine_and_status_buttons_after_the_list()
    {
        var rig = new Rig();
        using var panel = rig.Panel(true);
        using var form = new BBS757DisplayForm("Lower EICAS", "Lower EICAS", () => new[] { "x" }, panel);

        Assert.Equal(new[] { "Engine", "Status" }, panel.Controls.Cast<System.Windows.Forms.Control>().Select(c => c.Text));
        Assert.Equal(new[] { "Engine", "Status" }, panel.Controls.Cast<System.Windows.Forms.Control>().Select(c => c.AccessibleName));
        var list = form.Controls.Cast<System.Windows.Forms.Control>().Single(c => c.AccessibleName == "Lower EICAS");
        Assert.True(list.TabIndex < panel.TabIndex);
        Assert.Contains(panel, form.Controls.Cast<System.Windows.Forms.Control>());
        Assert.True(panel.Controls[0].TabIndex < panel.Controls[1].TabIndex);
    }

    [Fact]
    public void The_status_button_presses_bluebirds_status_key()
    {
        var rig = new Rig();
        using var panel = rig.Panel(true);

        Click(panel, "Status");

        Assert.Equal(new[] { "1 (>L:B757_EICAS_R_STATUS_BUTTON, bool)", "0 (>L:B757_EICAS_R_STATUS_BUTTON, bool)" }, rig.Writes);
    }

    [Fact]
    public void The_engine_button_presses_bluebirds_engine_key()
    {
        var rig = new Rig();
        using var panel = rig.Panel(true);

        Click(panel, "Engine");

        Assert.Equal(new[] { "1 (>L:B757_EICAS_L_STATUS_BUTTON, bool)", "0 (>L:B757_EICAS_L_STATUS_BUTTON, bool)" }, rig.Writes);
    }

    [Fact]
    public void A_press_while_the_reader_runs_says_nothing_itself()
    {
        var rig = new Rig();
        using var panel = rig.Panel(true);

        Click(panel, "Engine");

        Assert.Equal(2, rig.Writes.Count);
        Assert.Empty(rig.Speech.All);   // the reader's own page change is the confirmation
    }

    [Fact]
    public void A_press_while_the_reader_is_not_running_says_pressed()
    {
        var rig = new Rig();
        using var panel = rig.Panel(false);

        Click(panel, "Status");

        Assert.Equal(new[] { "EICAS R Status pressed" }, rig.Speech.All);
    }

    [Fact]
    public void A_press_that_cannot_land_says_unavailable()
    {
        var rig = new Rig { CanWrite = false };
        using var panel = rig.Panel(true);
        Click(panel, "Engine");
        Assert.Empty(rig.Writes);
        Assert.Equal(new[] { "EICAS L Status unavailable" }, rig.Speech.All);

        var missing = new Rig(omitStatus: true);   // a control the map lacks: no press, nothing thrown
        using var panel2 = missing.Panel(true);
        Click(panel2, "Status");
        Assert.Empty(missing.Writes);
    }

    private sealed class FakeHost : IModuleHost
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
