// Plan 24 Task 6: the upper EICAS reader on the 757 definition: its monitor, its three Ctrl+M rows (after the FMA's) and
// Alt+E, which opens the "Upper EICAS" window once and shows it again, or falls back to the AI read when the reader
// cannot run. The window is a real WinForms form (no message loop is needed to show it, as in the display-form tests).

using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757UpperEicasWiringTests
{
    [Fact]
    public void Alt_E_opens_the_upper_eicas_window_while_the_reader_runs()
    {
        using var rig = new Rig(started: true);

        Assert.True(rig.PressAltE());

        Assert.Empty(rig.AiReads);
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.Equal("Upper EICAS", form.Text);
        Assert.Equal("Upper EICAS", form.AccessibleName);
        Assert.True(form.Visible);
        Assert.Empty(rig.Speech.All);   // the key says nothing itself: the window's title and list are what NVDA reads
        rig.Def.OnSwitchedAway();
    }

    [Fact]
    public void Alt_E_falls_back_to_the_ai_read_when_unavailable()
    {
        using var rig = new Rig(started: false);   // never started: the monitor cannot run

        Assert.True(rig.Def.UpperEicas.KeyFallsBackToAi());
        Assert.True(rig.PressAltE());

        Assert.Equal(new[] { HotkeyAction.ReadDisplayUpperECAM }, rig.AiReads);
        Assert.Empty(rig.Def.Displays.Windows);
        Assert.Empty(rig.Speech.All);   // the AI read says its own words, nothing of ours first
    }

    [Fact]
    public void The_ctrl_m_rows_are_listed()
    {
        var def = new BBS757Definition();
        var rows = def.MonitorRows();

        var expected = new[]
        {
            new MonitorRow(Bbs757UpperEicasDisplay.MessagesKey, "EICAS messages"),
            new MonitorRow(Bbs757UpperEicasDisplay.ThrustKey, "Thrust reference"),
            new MonitorRow(Bbs757UpperEicasDisplay.ColorsKey, "Engine gauge colors"),
        };
        foreach (var row in expected) Assert.Contains(row, rows);
        Assert.Equal(expected, def.Displays.Rows.Skip(1).Take(3));
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_upper_eicas_rows_come_after_the_fmas()
    {
        var def = new BBS757Definition();

        Assert.Equal(Bbs757FmaDisplay.MuteKey, def.Displays.Rows[0].Key);
        Assert.Equal(Bbs757UpperEicasDisplay.MessagesKey, def.Displays.Rows[1].Key);
        Assert.Same(def.Fma, def.Displays.Monitors[0]);
        Assert.Same(def.UpperEicas, def.Displays.Monitors[1]);
    }

    [Fact]
    public void A_second_Alt_E_shows_the_same_window_again()
    {
        using var rig = new Rig(started: true);
        rig.PressAltE();
        var first = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        first.Hide();

        rig.PressAltE();

        Assert.Same(first, Assert.Single(rig.Def.Displays.Windows));
        Assert.True(first.Visible);
        Assert.False(first.IsDisposed);
        rig.Def.OnSwitchedAway();
    }

    [Fact]
    public void Alt_E_exits_output_hotkey_mode_before_showing_the_window()
    {
        using var rig = new Rig(started: true);
        var outputMode = typeof(HotkeyManager).GetField("outputHotkeyModeActive", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(outputMode is not null,
            "HotkeyManager has no private field outputHotkeyModeActive: this test sets it to enter output hotkey mode; follow its rename");
        outputMode.SetValue(rig.Hotkeys, true);
        Assert.True(rig.Hotkeys.IsOutputHotkeyModeActive);

        rig.PressAltE();

        Assert.False(rig.Hotkeys.IsOutputHotkeyModeActive);
        rig.Def.OnSwitchedAway();
    }

    [Fact]
    public void Alt_E_makes_the_window_anew_when_it_was_disposed()
    {
        using var rig = new Rig(started: true);
        rig.PressAltE();
        var first = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        first.CloseForGood();

        rig.PressAltE();

        var second = rig.Def.Displays.Windows.OfType<BBS757DisplayForm>().Last();
        Assert.NotSame(first, second);
        Assert.True(second.Visible);
        rig.Def.OnSwitchedAway();
    }

    [Fact]
    public void Switching_away_stops_the_upper_eicas_monitor()
    {
        using var rig = new Rig(started: true);
        Assert.Equal("WaitingForSimulator", rig.Def.UpperEicas.StateName);

        rig.Def.OnSwitchedAway();

        Assert.Equal("Stopped", rig.Def.UpperEicas.StateName);
    }

    [Fact]
    public void Alt_E_after_switching_away_reaches_the_ai_read_with_every_window_disposed()
    {
        using var rig = new Rig(started: true);
        rig.PressAltE();
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());

        rig.Def.OnSwitchedAway();
        Assert.True(rig.PressAltE());

        // The stopped reader cannot run: the key falls back to the AI read, and no window is made again or shown.
        Assert.Equal(new[] { HotkeyAction.ReadDisplayUpperECAM }, rig.AiReads);
        Assert.True(form.IsDisposed);
        Assert.All(rig.Def.Displays.Windows, w => Assert.True(w.IsDisposed, $"{w.Text} is still open"));
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Switching_away_closes_the_upper_eicas_window_for_good()
    {
        using var rig = new Rig(started: true);
        rig.PressAltE();
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());

        rig.Def.OnSwitchedAway();

        Assert.True(form.IsDisposed);
    }

    [Fact]
    public void The_manual_thrust_flag_input_rides_the_batch_once()
    {
        // The reader's one simulator input (S10), BlueBird's L:var where its draw takes it, never a check (B757-30): read
        // from one streamed variable, one already on the batch or a hidden copy, so no name is batched twice ([VAR-7]).
        // The N1s it once compared with are not read at all.
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        var input = Assert.Single(Bbs757UpperEicasDisplay.SimInputs);
        Assert.Equal(Bbs757SimInput.LVar("BBS757_THRREF_PULL"), input);
        var carrier = vars[keys[input.Key]];
        Assert.Equal((input.Name, SimVarType.LVar), (carrier.Name, carrier.Type));
        Assert.Equal(UpdateFrequency.Continuous, carrier.UpdateFrequency);
        Assert.True(carrier.IsAnnounced);   // streamed: on the batch or on its own subscription
        Assert.True(vars.Count(kv => kv.Value.Name == input.Name && ContinuousBatchLayout.RidesBatch(kv.Value)) <= 1);
        if (keys[input.Key].StartsWith(BBS757Definition.DisplayInputCopyPrefix, StringComparison.Ordinal))
            Assert.True(carrier.ExcludeFromMonitorManager);   // a copy is never announced or listed in Ctrl+M ([VAR-9])
        Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + "TURB ENG N1:1", vars.Keys);
        Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + "TURB ENG N1:2", vars.Keys);
    }

    // ---- The window's cockpit buttons (task 8b) ------------------------------------------------

    private static readonly (string Node, string Caption, string Label)[] Buttons =
    {
        ("B757_MAIN_CANCEL_BUTTON", "&Cancel", "Cancel"),
        ("B757_MAIN_RECALL_BUTTON", "&Recall", "Recall"),
        ("B757_EICAS_MAX-IND_BUTTON", "&Max Ind", "EICAS Max Ind"),
    };

    private static (BBS757DisplayForm Form, List<Button> Buttons) OpenWindow(Rig rig)
    {
        rig.PressAltE();
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        var buttons = form.Controls.Cast<Control>().SelectMany(c => c.Controls.OfType<Button>()).ToList();
        return (form, buttons);
    }

    [Fact]
    public void The_window_holds_cancel_recall_and_max_ind_after_the_list_in_the_tab_order()
    {
        using var rig = new Rig(started: true);

        var (form, buttons) = OpenWindow(rig);

        Assert.Equal(Buttons.Select(b => b.Caption), buttons.Select(b => b.Text));
        var list = form.Controls.Cast<Control>().Single(c => c is not FlowLayoutPanel);
        var panel = form.Controls.OfType<FlowLayoutPanel>().Single();
        Assert.True(panel.TabIndex > list.TabIndex);
        Assert.Equal(new[] { 0, 1, 2 }, buttons.Select(b => b.TabIndex));
    }

    [Fact]
    public void A_click_writes_the_press_then_the_release_and_says_pressed_once()
    {
        foreach (var (node, _, label) in Buttons)
        {
            using var rig = new Rig(started: true);
            var (_, buttons) = OpenWindow(rig);
            var control = rig.Def.ControlFor(node)!;
            var press = Bbs757Write.PressFor(control)!;

            buttons[Array.FindIndex(Buttons, b => b.Node == node)].PerformClick();

            Assert.Equal(press.Up == null ? new[] { press.Down } : new[] { press.Down, press.Up }, rig.Io.Writes);
            Assert.Equal(new[] { $"{label} pressed" }, rig.Speech.All);
        }
    }

    [Fact]
    public void A_click_that_cannot_land_says_unavailable_and_writes_nothing()
    {
        using var rig = new Rig(started: true);
        rig.Io.CanWrite = false;
        var (_, buttons) = OpenWindow(rig);

        foreach (var b in buttons) b.PerformClick();

        Assert.Empty(rig.Io.Writes);
        Assert.Equal(Buttons.Select(b => $"{b.Label} unavailable"), rig.Speech.All);
    }

    [Fact]
    public void The_window_shown_again_keeps_one_set_of_buttons()
    {
        using var rig = new Rig(started: true);
        var (form, _) = OpenWindow(rig);
        form.Hide();

        var (again, buttons) = OpenWindow(rig);

        Assert.Same(form, again);
        Assert.Equal(3, buttons.Count);
    }

    private sealed class FakeIo
    {
        public bool CanWrite = true;
        public readonly List<string> Writes = new();

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (_, _) => Task.FromResult<double?>(null),
            Cached = _ => null,
            Write = rpn => Writes.Add(rpn),
            RequestRead = _ => { },
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        };
    }

    /// <summary>A definition with the reader started (never its timer) or not, and Alt+E pressed through its hotkey path.</summary>
    private sealed class Rig : IDisposable
    {
        public BBS757Definition Def { get; }
        public FakeIo Io { get; } = new();
        public SpeechCapture Speech { get; } = new();
        public List<HotkeyAction> AiReads { get; } = new();
        public HotkeyManager Hotkeys { get; } = new();
        private readonly SimConnectManager _sim = new(IntPtr.Zero);
        private readonly Form _parent = new();

        public Rig(bool started)
        {
            Def = new BBS757Definition { StartMonitor = StartWithoutTimer, NestRunning = () => true, WriteIo = _ => Io.Io };
            Def.AiReadOverride = a => { AiReads.Add(a); return true; };
            if (started) Def.StartDisplays(new NoFiles(), new NoLayouts(), _sim, Speech);
            else _ = Def.Displays;
        }

        public bool PressAltE() => Def.HandleHotkeyAction(HotkeyAction.ReadDisplayUpperECAM, _sim, Speech, _parent, Hotkeys);

        public void Dispose()
        {
            Def.OnSwitchedAway();
            _parent.Dispose();
            Hotkeys.Dispose();
        }
    }

    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        switch (monitor)
        {
            case Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma: fma.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> eicas: eicas.Start(env, startTimer: false); break;
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
