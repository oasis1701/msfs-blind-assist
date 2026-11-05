// The PMDG 737's wiring of the primary flight display reader: the "PFD messages" Ctrl+M row, Output Alt+P (the
// Primary flight display window or, when the reader cannot work, straight to the AI read; with the classic EFIS look
// selected, a line saying so and then the AI read), the window's controls (the Captain's EFIS minimums, baro and buttons, the flight director switch, the speed
// reference selector and knob), the Ctrl+B altimeter code the window's field reuses, and the reader's life with the
// aircraft. Inside the CDU window Alt+P stays the PROG key.

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdWiringTests
{
    [Fact]
    public void Pfd_mute_row_with_its_name()
    {
        var v = new PMDG737Definition().GetVariables()[Pmdg737PfdMonitor.MuteKey];
        Assert.Equal("PFD messages", v.DisplayName);
        Assert.Equal(Pmdg737PfdMonitor.MuteKey, v.Name);
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.False(v.ExcludeFromMonitorManager);
    }

    [Fact]
    public void Output_p_is_free_again()
    {
        // Owner, 2026-10-30: the FMA announcements and this window give the FMA, so Output P no longer reads it, on any
        // aircraft, and the 737's hotkey guide no longer lists it.
        Assert.DoesNotContain("ReadFma", Enum.GetNames<HotkeyAction>());
        var guide = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", "PMDG_737_Hotkeys.txt"));
        Assert.DoesNotContain(guide, l => l.Contains("Read the flight mode annunciator", StringComparison.Ordinal)
            || l.Contains("] then P reads the FMA", StringComparison.Ordinal));
    }

    [Fact]
    public void Alt_p_with_the_reader_unavailable_falls_back()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        Start(def, new FakeEnvironment(layouts: false));
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayPFD, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All); // straight to the AI read, nothing said first (WASM-29)
        Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, aiReads);
        def.Pfd.Stop();
    }

    [Fact]
    public void Alt_p_stopped_for_10_s_falls_back_saying_nothing()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var env = new Pmdg737PfdMonitorTests.FakeEnvironment();
        def.Pfd.Start(env, startTimer: false);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                def.Pfd.Tick();
                env.NowMs += Pmdg737PfdMonitor.TickMs;
            }
        }
        for (int i = 0; i < 50 && def.Pfd.StateName != "Running"; i++) Tick(1);
        Assert.Equal("Running", def.Pfd.StateName);
        env.Locate = () => Task.FromResult(new PfdLocateResult(Array.Empty<IPfdSampler>(), null));
        env.Samplers.Single(s => !s.Disposed).Fail = true;
        Tick(21); // the memory lost for 10 s
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayPFD, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All);
        Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, aiReads);
        def.Pfd.Stop();
    }

    [Fact]
    public void Alt_p_with_the_classic_look_falls_back()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        var speech = new SpeechCapture();
        int linesBeforeAiRead = -1;
        def.AiReadOverride = a => { aiReads.Add(a); linesBeforeAiRead = speech.All.Count; return true; };
        var classic = Pmdg737PfdSpeechTests.With() with
        {
            UnitPages = new[] { Pmdg737PfdTestMemory.Layout.Units.ClassicPages[0], 3, 5, 6, 3, 2 },
        };
        Start(def, new FakeEnvironment(layouts: true, classic));
        Assert.Equal("Running", def.Pfd.StateName);

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayPFD, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        // The owner's line, interrupting as a key-press answer, then the AI read.
        Assert.Equal(new[] { "Classic EFIS is selected, falling back to AI reading" }, speech.Interrupts);
        Assert.Equal(new[] { "Classic EFIS is selected, falling back to AI reading" }, speech.All);
        Assert.Equal(1, linesBeforeAiRead);
        Assert.Equal(new[] { HotkeyAction.ReadDisplayPFD }, aiReads);
        def.Pfd.Stop();
    }

    [Fact]
    public void Alt_p_while_the_reader_starts_opens_the_window_never_the_ai_read()
    {
        // Not connected here, so the window is refused out loud; the point is that no AI read runs.
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayPFD, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(aiReads);
        Assert.Equal(new[] { "Not connected to simulator." }, speech.All);
    }

    [Theory]
    [InlineData(PfdButton.MinsReset, "EVT_EFIS_CPT_MINIMUMS_RST")]
    [InlineData(PfdButton.Std, "EVT_EFIS_CPT_BARO_STD")]
    [InlineData(PfdButton.Fpv, "EVT_EFIS_CPT_FPV")]
    [InlineData(PfdButton.Mtrs, "EVT_EFIS_CPT_MTRS")]
    public void Button_events_are_the_captain_efis_events(PfdButton button, string eventName)
    {
        Assert.Equal(eventName, PMDG737Definition.PfdButtonEvent(button));
        Assert.True(PMDG737Definition.EventIds.ContainsKey(eventName));
    }

    [Fact]
    public void The_flight_director_is_the_captain_s_fd_switch()
    {
        // Not an EFIS button: MCP_FDSw_0's existing toggle path (EVT_MCP_FD_SWITCH_L, LEFTSINGLE).
        Assert.Null(PMDG737Definition.PfdButtonEvent(PfdButton.FlightDirector));
        Assert.True(PMDG737Definition.EventIds.ContainsKey("EVT_MCP_FD_SWITCH_L"));
        Assert.True(PMDG737Definition.EventIds.ContainsKey("EVT_MPM_SPEED_REFERENCE_CONTROL"));
    }

    [Fact]
    public void The_window_s_controls_are_the_captain_s()
    {
        var def = new PMDG737Definition();
        var controls = def.PfdControlsFor(new SimConnectManager(IntPtr.Zero), new SpeechCapture(), null!);
        Assert.Equal(new[] { "SET", "AUTO", "V1", "VR", "WT", "VREF", "BUG" }, controls.SpeedReferencePositions);
        // Not connected: nothing to read; no reading: no button state.
        Assert.Null(controls.ReadMinsMode());
        Assert.Null(controls.ReadBaroUnits());
        Assert.Null(controls.ReadSpeedReference());
        Assert.Null(controls.ReadButtons());
    }

    [Fact]
    public void Button_state_is_the_reading_s_panel_and_the_fd_switch()
    {
        var sample = Pmdg737PfdSpeechTests.With() with { Panel = new PfdPanelState(Fpv: true, Mtrs: false, BaroStd: true, Powered: true) };
        Assert.Equal(new PfdButtonState(BaroStd: true, Fpv: true, Mtrs: false, FlightDirector: true), PMDG737Definition.PfdButtonStateOf(sample, 1));
        Assert.Equal(new PfdButtonState(BaroStd: true, Fpv: true, Mtrs: false, FlightDirector: null), PMDG737Definition.PfdButtonStateOf(sample, null));
        Assert.Null(PMDG737Definition.PfdButtonStateOf(null, 1));
    }

    [Theory]
    [InlineData(SpeedReferenceClick.CoarseUp, 0x80000000u)]   // a right click, 5 steps up (swept at ground session 2)
    [InlineData(SpeedReferenceClick.CoarseDown, 0x20000000u)] // a left click, 5 steps down
    [InlineData(SpeedReferenceClick.FineUp, 0x00004000u)]     // a wheel turn up, 1 step
    [InlineData(SpeedReferenceClick.FineDown, 0x00002000u)]   // a wheel turn down, 1 step
    public void Speed_reference_clicks_are_the_knob_s_mouse_actions(SpeedReferenceClick click, uint flag) =>
        Assert.Equal(flag, PMDG737Definition.SpeedReferenceClickFlag(click));

    [Theory]
    [InlineData("", "Enter a value in hPa or inHg")]
    [InlineData("abc", "Enter a numeric value in hPa or inHg")]
    [InlineData("1200", "Enter hPa between 900 and 1060")]
    [InlineData("25", "Enter inHg between 26.50 and 31.30")]
    [InlineData("1013", null)]
    [InlineData("29,92", null)]
    public void Altimeter_field_takes_ctrl_b_s_checks(string typed, string? error) =>
        Assert.Equal(error, PMDG737Definition.AltimeterInputError(typed));

    [Fact]
    public void Altimeter_field_says_why_it_cannot_set()
    {
        var def = new PMDG737Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        Assert.Equal("Enter hPa between 900 and 1060", def.SetAltimeterFromText("1200", sim, new SpeechCapture()));
        Assert.Equal("Not connected to simulator.", def.SetAltimeterFromText("1013", sim, new SpeechCapture()));
    }

    [Fact]
    public void Captain_minimums_without_the_pfd_reader_say_so()
    {
        // The Captain's minimums (the window's field and the Ctrl+P panel's) are set from PMDG's own value, read by the
        // PFD reader: without it the setter says so at once and turns nothing.
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();
        Assert.True(def.HandleUIVariableSet("EFIS_MinsValueFt_0_SET", 300, def.GetVariables()["EFIS_MinsValueFt_0_SET"],
            new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { "Minimums need the PFD reader" }, speech.Interrupts);
    }

    [Fact]
    public void Other_aircraft_keep_alt_p()
    {
        // The 737 keeps its AI read of the PFD as Alt+P's fallback; the 777 has no PFD reader and keeps its own.
        Assert.Contains(Pmdg737DisplayReads.All, r => r.Action == HotkeyAction.ReadDisplayPFD);
        Assert.Contains(Pmdg777DisplayReads.All, r => r.Action == HotkeyAction.ReadDisplayPFD);
    }

    [Fact]
    public void Cdu_window_alt_p_is_still_prog()
    {
        Assert.Equal("PROG", PMDG737CDUForm.AltPageKey(Keys.P));
        Assert.Equal("N1_LIMIT", PMDG737CDUForm.AltPageKey(Keys.N));
        Assert.Equal("EXEC", PMDG737CDUForm.AltPageKey(Keys.E));
        Assert.Null(PMDG737CDUForm.AltPageKey(Keys.Q));
    }

    [Fact]
    public void Switching_away_stops_the_pfd_reader_and_a_reload_restarts_it()
    {
        var def = new PMDG737Definition();
        Start(def, new FakeEnvironment(layouts: false));
        Assert.Equal("Unavailable", def.Pfd.StateName);
        def.OnSimContextReset();
        Assert.Equal("WaitingForSimulator", def.Pfd.StateName);
        def.OnSwitchedAway();
        Assert.Equal("Stopped", def.Pfd.StateName);
    }

    private static void Start(PMDG737Definition def, FakeEnvironment env)
    {
        def.Pfd.Start(env, startTimer: false);
        for (int i = 0; i < 6 && def.Pfd.StateName is not ("Running" or "Unavailable"); i++)
        {
            def.Pfd.Tick();
            env.NowMs += Pmdg737PfdMonitor.TickMs;
        }
    }

    /// <summary>An environment whose file analysis recognises nothing (unavailable at once), or derives the test layout
    /// and finds one candidate reading <paramref name="sample"/>.</summary>
    private sealed class FakeEnvironment(bool layouts, PfdSample? sample = null) : IPfdEnvironment
    {
        public long NowMs { get; set; }
        public PfdSimSnapshot? ReadSimulator() => new PfdSimSnapshot(161, 5010);
        public bool IsMuted() => false;
        public Pmdg737DisplayFilesNotice FilesNotice { get; } = new();
        public void Speak(string text) { }
        public void Log(string line) { }

        public Task<PfdAnalysis> AnalyseAsync() => Task.FromResult(layouts
            ? new PfdAnalysis(new[] { Pmdg737PfdTestMemory.Layout }, null)
            : new PfdAnalysis(Array.Empty<Pmdg737PfdLayout>(), FmaReasons.FileNotRecognised));

        public Task<PfdLocateResult> LocateAsync(IReadOnlyList<Pmdg737PfdLayout> found) =>
            Task.FromResult(new PfdLocateResult(sample is null ? Array.Empty<IPfdSampler>() : new IPfdSampler[] { new Sampler(sample) }, null));
    }

    private sealed class Sampler(PfdSample sample) : IPfdSampler
    {
        public Pmdg737PfdLayout Layout => Pmdg737PfdTestMemory.Layout;
        public bool Full { get; set; }

        public bool TryRead(out PfdSample read, out string? corrupt)
        {
            read = sample;
            corrupt = null;
            return true;
        }

        public void Dispose() { }
    }
}
