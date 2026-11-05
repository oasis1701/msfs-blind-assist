// The PMDG 737's wiring of the navigation display reader: the "ND messages" Ctrl+M row, Output Alt+N (the
// Navigation display window or, when the reader cannot work, straight to the AI read), Output Ctrl+W (the
// active waypoint as the ND prints it), the window's controls (the Captain's EFIS selectors and map buttons)
// and the reader's life with the aircraft.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayWiringTests
{
    [Fact]
    public void Nd_mute_row_with_its_name()
    {
        var v = new PMDG737Definition().GetVariables()[Pmdg737NavDisplayMonitor.MuteKey];
        Assert.Equal("ND messages", v.DisplayName);
        Assert.Equal(Pmdg737NavDisplayMonitor.MuteKey, v.Name);
        Assert.Equal(SimVarType.PMDGVar, v.Type);
        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.False(v.ExcludeFromMonitorManager);
    }

    [Fact]
    public void Alt_n_with_the_reader_unavailable_falls_back()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        StartUnavailable(def);
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayND, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All); // straight to the AI read, nothing said first (WASM-29)
        Assert.Equal(new[] { HotkeyAction.ReadDisplayND }, aiReads);
        def.NavDisplay.Stop();
    }

    [Fact]
    public void Alt_n_stopped_for_10_s_falls_back_saying_nothing()
    {
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var env = new Pmdg737NavDisplayMonitorTests.FakeEnvironment();
        def.NavDisplay.Start(env, startTimer: false);
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                def.NavDisplay.Tick();
                env.NowMs += Pmdg737NavDisplayMonitor.TickMs;
            }
        }
        for (int i = 0; i < 50 && def.NavDisplay.StateName != "Running"; i++) Tick(1);
        Assert.Equal("Running", def.NavDisplay.StateName);
        env.Locate = () => Task.FromResult(new NavDisplayLocateResult(Array.Empty<INavDisplaySampler>(), null));
        env.Samplers.Single(s => !s.Disposed).Fail = true;
        Tick(21); // the memory lost for 10 s
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayND, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(speech.All);
        Assert.Equal(new[] { HotkeyAction.ReadDisplayND }, aiReads);
        def.NavDisplay.Stop();
    }

    [Fact]
    public void Alt_n_while_the_reader_starts_opens_the_window_never_the_ai_read()
    {
        // Not connected here, so the window is refused out loud; the point is that no AI read runs.
        var def = new PMDG737Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayND, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Empty(aiReads);
        Assert.Equal(new[] { "Not connected to simulator." }, speech.All);
    }

    [Fact]
    public void Ctrl_w_speaks_the_waypoint_line()
    {
        var def = new PMDG737Definition();
        StartUnavailable(def);
        var speech = new SpeechCapture();

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadNDWaypoint, new SimConnectManager(IntPtr.Zero), speech, null!, null!));

        Assert.Equal(new[] { "ND waypoint unavailable: PMDG file not recognised" }, speech.Interrupts);
        def.NavDisplay.Stop();
    }

    [Theory]
    [InlineData(NdButton.Ctr, "EVT_EFIS_CPT_MODE_CTR")]
    [InlineData(NdButton.Tfc, "EVT_EFIS_CPT_RANGE_TFC")]
    [InlineData(NdButton.Wxr, "EVT_EFIS_CPT_WXR")]
    [InlineData(NdButton.Sta, "EVT_EFIS_CPT_STA")]
    [InlineData(NdButton.Wpt, "EVT_EFIS_CPT_WPT")]
    [InlineData(NdButton.Arpt, "EVT_EFIS_CPT_ARPT")]
    [InlineData(NdButton.Data, "EVT_EFIS_CPT_DATA")]
    [InlineData(NdButton.Pos, "EVT_EFIS_CPT_POS")]
    [InlineData(NdButton.Terr, "EVT_EFIS_CPT_TERR")]
    public void Button_events_are_the_captain_efis_events(NdButton button, string eventName)
    {
        Assert.Equal(eventName, PMDG737Definition.NavDisplayButtonEvent(button));
        Assert.True(PMDG737Definition.EventIds.ContainsKey(eventName));
    }

    [Fact]
    public void Every_button_has_its_event()
    {
        foreach (var b in Enum.GetValues<NdButton>())
            Assert.True(PMDG737Definition.EventIds.ContainsKey(PMDG737Definition.NavDisplayButtonEvent(b)));
    }

    [Fact]
    public void The_window_s_selectors_are_the_captain_s_efis_knobs()
    {
        var def = new PMDG737Definition();
        var controls = def.NavDisplayControlsFor(new SimConnectManager(IntPtr.Zero), new SpeechCapture(), null!);
        Assert.Equal(new[] { "APP", "VOR", "MAP", "PLAN" }, controls.ModePositions);
        Assert.Equal(new[] { "5", "10", "20", "40", "80", "160", "320", "640" }, controls.RangePositions);
        Assert.Equal(new[] { "VOR", "OFF", "ADF" }, controls.VorAdfPositions);
        // Not connected: nothing to read; no reading: no panel.
        Assert.Null(controls.ReadMode());
        Assert.Null(controls.ReadRange());
        Assert.Null(controls.ReadVorAdf1());
        Assert.Null(controls.ReadPanel());
    }

    [Fact]
    public void Other_aircraft_keep_alt_n()
    {
        // The 737 keeps its AI read of the ND as Alt+N's fallback; the 777 has no ND reader and keeps its own.
        Assert.Contains(Pmdg737DisplayReads.All, r => r.Action == HotkeyAction.ReadDisplayND);
        Assert.Contains(Pmdg777DisplayReads.All, r => r.Action == HotkeyAction.ReadDisplayND);
    }

    [Fact]
    public void Switching_away_stops_the_nd_reader_and_a_reload_restarts_it()
    {
        var def = new PMDG737Definition();
        StartUnavailable(def);
        Assert.Equal("Unavailable", def.NavDisplay.StateName);
        def.OnSimContextReset();
        Assert.Equal("WaitingForSdk", def.NavDisplay.StateName);
        def.OnSwitchedAway();
        Assert.Equal("Stopped", def.NavDisplay.StateName);
    }

    private static void StartUnavailable(PMDG737Definition def)
    {
        def.NavDisplay.Start(new UnavailableEnvironment(), startTimer: false);
        def.NavDisplay.Tick();
        def.NavDisplay.Tick();
    }

    /// <summary>An environment whose file analysis recognises nothing: the reader is unavailable at once.</summary>
    private sealed class UnavailableEnvironment : INavDisplayEnvironment
    {
        public long NowMs => 0;
        public NavDisplaySdkSnapshot? ReadSdk() => new NavDisplaySdkSnapshot(2, 2);
        public bool IsMuted() => false;
        public Pmdg737DisplayFilesNotice FilesNotice { get; } = new();
        public void Speak(string text) { }
        public void Log(string line) { }
        public Task<NavDisplayAnalysis> AnalyseAsync() =>
            Task.FromResult(new NavDisplayAnalysis(Array.Empty<Pmdg737NavDisplayLayout>(), FmaReasons.FileNotRecognised));
        public Task<NavDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737NavDisplayLayout> layouts) =>
            Task.FromResult(new NavDisplayLocateResult(Array.Empty<INavDisplaySampler>(), null));
    }
}
