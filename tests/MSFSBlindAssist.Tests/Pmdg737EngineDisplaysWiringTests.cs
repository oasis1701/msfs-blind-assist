// The PMDG 737's wiring of the engine display reader: the MFD ENG and SYS buttons (momentary, in the
// Instruments panel, confirmed once after the press), the three Ctrl+M rows, and Output Alt+E, which
// opens the Engine displays window or, when the reader cannot work, goes straight to the AI read, saying nothing.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplaysWiringTests
{
    [Theory]
    [InlineData("MPM_MFD_ENG", "MFD ENG", "EVT_MPM_MFD_ENG_BUTTON")]
    [InlineData("MPM_MFD_SYS", "MFD SYS", "EVT_MPM_MFD_SYS_BUTTON")]
    public void Mfd_buttons_are_momentary_and_mapped(string key, string name, string eventName)
    {
        var v = new PMDG737Definition().GetVariables()[key];
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        Assert.True(v.RenderAsButton);
        Assert.True(v.IsMomentary);
        Assert.Equal(name, v.DisplayName);
        Assert.Equal(eventName, PMDG737Definition.SimpleEventFor(key));
        Assert.True(PMDG737Definition.EventIds.ContainsKey(eventName));
    }

    [Fact]
    public void Mfd_buttons_are_in_the_instruments_panel()
    {
        var instruments = new PMDG737Definition().GetPanelControls()["Instruments"];
        int fuelFlow = instruments.IndexOf("MPM_FUEL_FLOW_RESET");
        Assert.True(fuelFlow >= 0);
        Assert.Equal(new[] { "MPM_MFD_ENG", "MPM_MFD_SYS" }, instruments.Skip(fuelFlow + 1).Take(2));
    }

    // The Fuel Flow switch is spring-loaded (USED and RESET return to RATE), and PMDG tells its display a
    // position was left only on the mouse release: a lone click left the display on fuel used for good
    // (live 2026-10-24). So it is two press-and-release buttons, swept live: USED is a left press and
    // release, RESET a right one; the display shows fuel used for 10 s after USED, 2 s after RESET.
    [Theory]
    [InlineData("MPM_FUEL_FLOW_USED", "Fuel Flow Used", 1)]
    [InlineData("MPM_FUEL_FLOW_RESET", "Fuel Flow Reset", 0)]
    public void Fuel_flow_is_two_press_and_release_buttons(string key, string name, int clickTarget)
    {
        var def = new PMDG737Definition();
        var v = def.GetVariables()[key];
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        Assert.True(v.RenderAsButton);
        Assert.True(v.IsMomentary);
        Assert.Equal(name, v.DisplayName);
        Assert.Equal("EVT_MPM_FUEL_FLOW_SWITCH", PMDG737Definition.SimpleEventFor(key));
        Assert.Equal(clickTarget, PMDG737Definition.MomentaryClickTarget(key));
    }

    [Fact]
    public void The_fuel_flow_switch_is_no_combo_and_says_nothing_by_itself()
    {
        var def = new PMDG737Definition();
        var instruments = def.GetPanelControls()["Instruments"];
        Assert.DoesNotContain("MAIN_FuelFlowSelector", instruments);
        int spdRef = instruments.IndexOf("MAIN_SpdRefSelector");
        Assert.Equal(new[] { "MPM_FUEL_FLOW_USED", "MPM_FUEL_FLOW_RESET" }, instruments.Skip(spdRef + 1).Take(2));
        // It rests at RATE: each press would otherwise be "Fuel Flow: USED", then "Fuel Flow: RATE".
        Assert.False(def.GetVariables()["MAIN_FuelFlowSelector"].IsAnnounced);
        Assert.Equal(1, PMDG737Definition.MomentaryClickTarget("MPM_MFD_ENG"));
    }

    [Fact]
    public void Fuel_flow_press_reads_back_the_fuel_used_row()
    {
        var view = new EngineDisplaysView(new[] { "Upper display: primary engine page", "FUEL USED: 1.23, 1.20", "Alerts: none" },
            new[] { "Lower display: secondary engine page", "FUEL USED: 1.23, 1.20" }, EngineDisplaySampleFactory.Idle(), null);
        Assert.Equal("FUEL USED: 1.23, 1.20", PMDG737Definition.FuelUsedReadback(view));
        Assert.Null(PMDG737Definition.FuelUsedReadback(view with { Upper = new[] { "Upper display: SYS page" }, Lower = new[] { "Lower display: off" } }));
        Assert.Null(PMDG737Definition.FuelUsedReadback(view with { Sample = null }));
    }

    [Fact]
    public void Fuel_flow_press_with_no_reader_says_pressed_at_once()
    {
        var def = new PMDG737Definition();
        var scheduled = new List<int>();
        def.ScheduleOnUiThread = (ms, _) => scheduled.Add(ms);
        var speech = new SpeechCapture();

        Assert.True(def.HandleUIVariableSet("MPM_FUEL_FLOW_RESET", 1, def.GetVariables()["MPM_FUEL_FLOW_RESET"], new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { "Fuel Flow Reset pressed" }, speech.All);
        Assert.Empty(scheduled);
    }

    [Theory]
    [InlineData(Pmdg737EngineDisplayMonitor.MuteKeyAlerts, "Engine display alerts")]
    [InlineData(Pmdg737EngineDisplayMonitor.MuteKeyColors, "Engine gauge colors")]
    [InlineData(Pmdg737EngineDisplayMonitor.MuteKeyThrustMode, "Thrust mode")]
    public void Three_mute_rows_with_their_names(string key, string name)
    {
        var v = new PMDG737Definition().GetVariables()[key];
        Assert.Equal(name, v.DisplayName);
        Assert.Equal(key, v.Name);
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.False(v.ExcludeFromMonitorManager);
    }

    [Fact]
    public void Alt_e_with_the_reader_unavailable_falls_back()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        def.EngineDisplays.Start(new UnavailableEnvironment(), startTimer: false);
        def.EngineDisplays.Tick();
        def.EngineDisplays.Tick();
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayUpperECAM, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All); // straight to the AI read, nothing said first (WASM-29)
        Assert.Equal(new[] { HotkeyAction.ReadDisplayUpperECAM }, aiReads);
        def.EngineDisplays.Stop();
    }

    [Fact]
    public void Alt_e_stopped_for_10_s_falls_back_saying_nothing()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var env = new Pmdg737EngineDisplayMonitorTests.FakeEnvironment();
        def.EngineDisplays.Start(env, startTimer: false);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                def.EngineDisplays.Tick();
                env.NowMs += Pmdg737EngineDisplayMonitor.TickMs;
            }
        }
        for (int i = 0; i < 50 && def.EngineDisplays.StateName != "Running"; i++) Tick(1);
        Assert.Equal("Running", def.EngineDisplays.StateName);
        env.Locate = () => Task.FromResult(new EngineDisplayLocateResult(Array.Empty<IEngineDisplaySampler>(), null));
        env.Samplers.Single(s => !s.Disposed).Fail = true;
        Tick(101); // the memory lost for 10 s
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayUpperECAM, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All);
        Assert.Equal(new[] { HotkeyAction.ReadDisplayUpperECAM }, aiReads);
        def.EngineDisplays.Stop();
    }

    [Fact]
    public void Alt_e_while_the_reader_starts_opens_the_window_never_the_ai_read()
    {
        // Not connected here, so the window is refused out loud; the point is that no AI read runs.
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayUpperECAM, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(aiReads);
        Assert.Equal(new[] { "Not connected to simulator." }, speech.All);
    }

    [Fact]
    public void Mfd_press_with_no_reader_says_pressed_at_once()
    {
        var def = new PMDG737Definition();
        var scheduled = new List<int>();
        def.ScheduleOnUiThread = (ms, _) => scheduled.Add(ms);
        var speech = new SpeechCapture();
        var v = def.GetVariables()["MPM_MFD_SYS"];

        Assert.True(def.HandleUIVariableSet("MPM_MFD_SYS", 1, v, new SimConnectManager(IntPtr.Zero), speech));

        Assert.Equal(new[] { "MFD SYS pressed" }, speech.All);
        Assert.Empty(speech.Interrupts);
        Assert.Empty(scheduled);
    }

    [Fact]
    public void Lower_display_read_back_names_its_page_from_the_view()
    {
        var view = new EngineDisplaysView(new[] { "Upper display: compact engine page" }, new[] { "Lower display: SYS page", "HYDRAULIC: A, B" },
            EngineDisplaySampleFactory.Idle(), null);
        Assert.Equal("Lower display: SYS page", PMDG737Definition.LowerDisplayReadback(view));
        Assert.Null(PMDG737Definition.LowerDisplayReadback(view with { Sample = null }));
    }

    [Fact]
    public void Manual_n1_needs_a_reading_taken_after_the_aircraft_initialised()
    {
        var view = new EngineDisplaysView(new[] { "Upper display: off" }, new[] { "Lower display: off" },
            EngineDisplaySampleFactory.Idle() with { Initialised = false }, null);
        Assert.False(PMDG737Definition.ManualN1Available(view));
        Assert.True(PMDG737Definition.ManualN1Available(view with { Sample = EngineDisplaySampleFactory.Idle() }));
        Assert.False(PMDG737Definition.ManualN1Available(view with { Sample = null }));
    }

    // ---- the manual N1 ----

    [Fact]
    public void Manual_n1_button_follows_n1_set_in_the_instruments_panel()
    {
        var def = new PMDG737Definition();
        var v = def.GetVariables()["MSFSBA_PMDG737_MANUAL_N1"];
        Assert.Equal("Manual N1", v.DisplayName);
        Assert.True(v.RenderAsButton);
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        var instruments = def.GetPanelControls()["Instruments"];
        Assert.Equal(instruments.IndexOf("MAIN_N1SetSelector") + 1, instruments.IndexOf("MSFSBA_PMDG737_MANUAL_N1"));
    }

    [Theory]
    [InlineData(ManualN1Click.CoarseUp, 0x80000000u)]   // a right click: +0.5 (swept live)
    [InlineData(ManualN1Click.CoarseDown, 0x20000000u)] // a left click: -0.5
    [InlineData(ManualN1Click.FineUp, 0x00004000u)]     // a wheel turn up: +0.1
    [InlineData(ManualN1Click.FineDown, 0x00002000u)]   // a wheel turn down: -0.1
    public void Manual_n1_clicks_are_the_swept_mouse_actions(ManualN1Click click, uint flag) =>
        Assert.Equal(flag, PMDG737Definition.ManualN1ClickFlag(click));

    [Fact]
    public void Manual_n1_reads_pmdgs_own_value_from_an_initialised_reading()
    {
        var sample = EngineDisplaySampleFactory.Idle() with { N1SetSelector = 3 };
        sample = sample with { Engines = new[] { sample.Engines[0] with { ManualN1 = 95.5 }, sample.Engines[1] with { ManualN1 = 96.0 } } };
        var view = new EngineDisplaysView(new[] { "Upper display: primary engine page" }, new[] { "Lower display: off" }, sample, null);

        var reading = PMDG737Definition.ManualN1ReadingOf(view);
        Assert.NotNull(reading);
        Assert.Equal(3, reading!.Value.N1SetPosition);
        Assert.Equal(new[] { 95.5, 96.0 }, reading.Value.Values);
        Assert.Null(PMDG737Definition.ManualN1ReadingOf(view with { Sample = sample with { Initialised = false } }));
        Assert.Null(PMDG737Definition.ManualN1ReadingOf(view with { Sample = null }));
    }

    [Fact]
    public void Manual_n1_button_while_not_connected_says_so()
    {
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();
        Assert.True(def.HandleUIVariableSet("MSFSBA_PMDG737_MANUAL_N1", 1, def.GetVariables()["MSFSBA_PMDG737_MANUAL_N1"],
            new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { "Not connected to simulator." }, speech.All);
    }

    /// <summary>An environment whose file analysis recognises nothing: the reader is unavailable at once.</summary>
    private sealed class UnavailableEnvironment : IEngineDisplayEnvironment
    {
        public long NowMs => 0;
        public EngineDisplaySdkSnapshot? ReadSdk() => new EngineDisplaySdkSnapshot(0, 0, 0);
        public bool IsMuted(EngineDisplayMuteRow row) => false;
        public Pmdg737DisplayFilesNotice FilesNotice { get; } = new();
        public void Speak(string text) { }
        public void Log(string line) { }
        public Task<EngineDisplayAnalysis> AnalyseAsync() =>
            Task.FromResult(new EngineDisplayAnalysis(Array.Empty<Pmdg737EngineDisplayLayout>(), FmaReasons.FileNotRecognised));
        public Task<EngineDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737EngineDisplayLayout> layouts) =>
            Task.FromResult(new EngineDisplayLocateResult(Array.Empty<IEngineDisplaySampler>(), null));
    }
}
