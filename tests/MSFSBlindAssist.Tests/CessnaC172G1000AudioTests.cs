// The Cessna 172 G1000's audio panel (the GMA 1347) and the two bezel controls added to its GFC 700
// panel, as measured live on MSFS 2024's stock aircraft (2026-10-17, docs/invariants/c172.md
// C172-10 to C172-12). Each audio key is a push that toggles, read from the SimVar it drives; the
// intercom's two lamps live in two SimVars; the typed course goes to whatever the CDI is on.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CessnaC172G1000AudioTests
{
    private static readonly CessnaC172G1000Definition Def = new();

    [Fact]
    public void The_audio_panel_is_in_the_radios_section_of_the_g1000_alone()
    {
        Assert.Contains("Audio", Def.GetPanelStructure()["Radios"]);
        Assert.DoesNotContain("Audio", new CessnaC172ClassicDefinition().GetPanelStructure()["Radios"]);
    }

    [Theory]
    [InlineData("C172G_AUDIO_TRANSMIT", "COM TRANSMIT:2")]
    [InlineData("C172G_AUDIO_COM1", "COM RECEIVE:1")]
    [InlineData("C172G_AUDIO_COM2", "COM RECEIVE:2")]
    [InlineData("C172G_AUDIO_NAV1", "NAV SOUND:1")]
    [InlineData("C172G_AUDIO_NAV2", "NAV SOUND:2")]
    [InlineData("C172G_AUDIO_DME", "DME SOUND")]
    [InlineData("C172G_AUDIO_ADF", "ADF SOUND:1")]
    [InlineData("C172G_AUDIO_MKR_MUTE", "MARKER BEACON TEST MUTE")]   // MKR/MUTE mutes; MARKER SOUND stays 1
    [InlineData("C172G_AUDIO_HI_SENS", "MARKER BEACON SENSITIVITY HIGH")]
    [InlineData("C172G_AUDIO_SPKR", "SPEAKER ACTIVE")]
    public void Each_audio_key_reads_what_it_drives(string key, string simVar)
    {
        var d = Def.GetVariables()[key];
        Assert.Equal(simVar, d.Name);
        Assert.Equal(SimVarType.SimVar, d.Type);
        Assert.Contains(key, Def.GetPanelControls()["Audio"]);
    }

    // C172-10: every key is a momentary push on its own `_MIDCOM` input event that flips it, so each row
    // is an Off/On combo the definition presses only when the sim says it differs, never a direct set.
    [Fact]
    public void Each_audio_key_is_a_combo_that_pushes_its_own_key()
    {
        foreach (var (key, inputEvent) in CessnaC172G1000Definition.AudioKeyInputEvents)
        {
            var d = Def.GetVariables()[key];
            Assert.EndsWith("_MIDCOM", inputEvent);
            Assert.Equal(new[] { "Off", "On" }, d.ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
            Assert.False(d.RenderAsButton, key);
        }
        Assert.Equal(new[] { "COM 1", "COM 2" },
            Def.GetVariables()["C172G_AUDIO_TRANSMIT"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal("AS1000_COM_1_MIC_MIDCOM", CessnaC172G1000Definition.MicKeys[1]);
        Assert.Equal("AS1000_COM_2_MIC_MIDCOM", CessnaC172G1000Definition.MicKeys[2]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void Com_transmit_2_names_the_transmitting_radio(double comTransmit2, int radio)
    {
        Assert.Equal(radio, CessnaC172G1000Definition.TransmitRadio(comTransmit2));
    }

    // Measured pressing PILOT and COPLT in every order: SYSTEM ACTIVE 0 is neither lit; with it 1,
    // MODE 0 is PILOT alone, 1 both, 2 COPLT alone. The mode is left where it was when both go out.
    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(0, 2, false, false)]
    [InlineData(1, 0, true, false)]
    [InlineData(1, 1, true, true)]
    [InlineData(1, 2, false, true)]
    public void The_intercom_lamps_come_from_two_simvars(double active, double mode, bool pilot, bool copilot)
    {
        Assert.Equal(((bool?)pilot, (bool?)copilot), CessnaC172G1000Definition.IntercomLamps(active, mode));
    }

    [Fact]
    public void An_intercom_mode_not_yet_read_or_never_measured_is_unknown()
    {
        Assert.Equal(((bool?)null, (bool?)null), CessnaC172G1000Definition.IntercomLamps(1, null));
        Assert.Equal(((bool?)null, (bool?)null), CessnaC172G1000Definition.IntercomLamps(1, 3));
        Assert.Equal(((bool?)false, (bool?)false), CessnaC172G1000Definition.IntercomLamps(0, null));
    }

    [Fact]
    public void The_intercom_keys_are_buttons_and_their_lamps_are_the_status_list()
    {
        var vars = Def.GetVariables();
        Assert.True(vars["C172G_AUDIO_PILOT"].RenderAsButton);
        Assert.Equal("AS1000_ISOLATEPILOT_MIDCOM", vars["C172G_AUDIO_PILOT"].Name);
        Assert.True(vars["C172G_AUDIO_COPLT"].RenderAsButton);
        Assert.Equal("AS1000_ISOLATECOPILOT_MIDCOM", vars["C172G_AUDIO_COPLT"].Name);

        Assert.Equal(new[] { CessnaC172G1000Definition.IntercomActiveKey, CessnaC172G1000Definition.IntercomModeKey },
            Def.GetPanelDisplayVariables()["Audio"]);
        Assert.Equal("INTERCOM SYSTEM ACTIVE", vars[CessnaC172G1000Definition.IntercomActiveKey].Name);
        Assert.Equal("INTERCOM MODE", vars[CessnaC172G1000Definition.IntercomModeKey].Name);
    }

    [Fact]
    public void The_status_list_shows_both_intercom_lamps()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();
        def.ProcessSimVarUpdate(CessnaC172G1000Definition.IntercomActiveKey, 1, speech);
        def.ProcessSimVarUpdate(CessnaC172G1000Definition.IntercomModeKey, 2, speech);

        Assert.True(def.TryGetDisplayOverride(CessnaC172G1000Definition.IntercomActiveKey, 1, out string pilot));
        Assert.True(def.TryGetDisplayOverride(CessnaC172G1000Definition.IntercomModeKey, 2, out string copilot));
        Assert.Equal("Off", pilot);
        Assert.Equal("On", copilot);
        Assert.Empty(speech.All);   // the intercom SimVars are never spoken themselves
    }

    [Fact]
    public void A_status_list_painted_before_any_update_learns_both_lamps_from_the_cache()
    {
        var def = new CessnaC172G1000Definition();

        def.TryGetDisplayOverride(CessnaC172G1000Definition.IntercomActiveKey, 1, out _);
        Assert.True(def.TryGetDisplayOverride(CessnaC172G1000Definition.IntercomModeKey, 1, out string copilot));
        Assert.True(def.TryGetDisplayOverride(CessnaC172G1000Definition.IntercomActiveKey, 1, out string pilot));
        Assert.Equal("On", copilot);
        Assert.Equal("On", pilot);
    }

    // C172-12: a key moved outside MSFSBA is spoken after its first reading; the definition speaks it
    // and keeps its own baseline, so a change the echo window hid still moves the baseline.
    [Fact]
    public void An_audio_key_moved_outside_msfsba_is_spoken_after_its_first_reading()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate("C172G_AUDIO_NAV1", 0, speech));   // true: MainForm's generic announcer stays out
        def.ProcessSimVarUpdate("C172G_AUDIO_NAV1", 1, speech);
        def.ProcessSimVarUpdate("C172G_AUDIO_NAV1", 1, speech);
        def.ProcessSimVarUpdate("C172G_AUDIO_TRANSMIT", 0, speech);
        def.ProcessSimVarUpdate("C172G_AUDIO_TRANSMIT", 1, speech);

        Assert.Equal(new[] { "NAV1: On", "Transmit Radio: COM 2" }, speech.All);

        def.OnSimContextReset();
        def.ProcessSimVarUpdate("C172G_AUDIO_NAV1", 0, speech);   // a new flight's first reading
        Assert.Equal(2, speech.All.Count);
    }

    [Fact]
    public void The_pilots_own_pick_is_silent_and_a_later_change_back_still_speaks()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();
        def.ProcessSimVarUpdate("C172G_AUDIO_SPKR", 0, speech);

        speech.Suppressed = true;   // as MainForm sets it while the pilot's combo set echoes back (CORE-8)
        def.ProcessSimVarUpdate("C172G_AUDIO_SPKR", 1, speech);
        speech.Suppressed = false;
        def.ProcessSimVarUpdate("C172G_AUDIO_SPKR", 0, speech);

        Assert.Equal(new[] { "SPKR: Off" }, speech.All);
    }

    [Fact]
    public void Every_audio_combo_follows_the_sim_on_the_def_handled_path()
    {
        foreach (var key in CessnaC172G1000Definition.AudioKeyInputEvents.Keys.Append(CessnaC172G1000Definition.TransmitKey))
        {
            var d = Def.GetVariables()[key];
            Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
            Assert.True(d.IsAnnounced, key);
            Assert.True(d.RefreshesControlWhenDefHandled(0), key);
            Assert.True(d.RefreshesControlWhenDefHandled(1), key);
        }
    }

    // On the 1 s batch every pick waited for the next batch before it pressed anything, and COM1's
    // refusal waited a second batch to show On again (the owner's first test, 2026-10-17). Their own
    // SIM_FRAME subscriptions make the fresh read answer from the cache at once.
    [Fact]
    public void The_audio_rows_and_the_intercom_simvars_are_fast_sampled()
    {
        var keys = CessnaC172G1000Definition.AudioKeyInputEvents.Keys
            .Append(CessnaC172G1000Definition.TransmitKey)
            .Append(CessnaC172G1000Definition.IntercomActiveKey)
            .Append(CessnaC172G1000Definition.IntercomModeKey);
        foreach (var key in keys)
        {
            var d = Def.GetVariables()[key];
            Assert.True(d.ExcludeFromBatch && d.HighFrequency, key);
            Assert.True(FreshReadPolicy.CacheIsFresh(d), key);
        }
    }

    // C172-12: COM2 MIC also moved COM 2's receive (and once COM 1's); a knock-on of MSFSBA's own COM
    // audio set stays quiet for the grace window, any other change speaks.
    [Theory]
    [InlineData("C172G_AUDIO_COM2", "C172G_AUDIO_TRANSMIT", 300, true)]
    [InlineData("C172G_AUDIO_COM1", "C172G_AUDIO_TRANSMIT", 2400, true)]
    [InlineData("C172G_AUDIO_TRANSMIT", "C172G_AUDIO_COM2", 300, true)]
    [InlineData("C172G_AUDIO_COM2", "C172G_AUDIO_TRANSMIT", 2600, false)]   // past the grace
    [InlineData("C172G_AUDIO_TRANSMIT", "C172G_AUDIO_TRANSMIT", 300, false)]  // the set row is the echo's
    [InlineData("C172G_AUDIO_NAV1", "C172G_AUDIO_TRANSMIT", 300, false)]   // not a COM audio row
    [InlineData("C172G_AUDIO_COM2", "", 300, false)]                       // no write of ours
    public void Only_a_knock_on_of_our_own_com_audio_set_is_quiet(string varName, string ownKey, int sinceMs, bool quiet)
    {
        Assert.Equal(quiet, CessnaC172G1000Definition.IsComAudioSideEffect(varName, ownKey, TimeSpan.FromMilliseconds(sinceMs)));
    }

    [Fact]
    public void The_grace_outlasts_the_one_second_batch()
    {
        Assert.InRange(CessnaC172G1000Definition.ComAudioGraceMs, 1001, 5000);
    }

    [Fact]
    public void The_volumes_are_percent_sliders_set_through_the_stock_events()
    {
        Assert.Equal("COM1_VOLUME_SET", CessnaC172G1000Definition.VolumeEvents["C172G_AUDIO_COM1_VOL"]);
        Assert.Equal("COM2_VOLUME_SET", CessnaC172G1000Definition.VolumeEvents["C172G_AUDIO_COM2_VOL"]);
        Assert.Equal("NAV1_VOLUME_SET_EX1", CessnaC172G1000Definition.VolumeEvents["C172G_AUDIO_NAV1_VOL"]);
        Assert.Equal("NAV2_VOLUME_SET_EX1", CessnaC172G1000Definition.VolumeEvents["C172G_AUDIO_NAV2_VOL"]);
        foreach (var key in CessnaC172G1000Definition.VolumeEvents.Keys)
        {
            var d = Def.GetVariables()[key];
            Assert.True(d.RenderAsSlider, key);
            Assert.Equal("percent", d.Units);
            Assert.Equal((0.0, 100.0), (d.SliderMin, d.SliderMax));
            Assert.False(d.IsAnnounced, key);
        }
    }

    [Fact]
    public void Audio_names_are_unique_on_the_panel()
    {
        var names = Def.GetPanelControls()["Audio"].Select(k => Def.GetVariables()[k].DisplayName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // ---------- GFC 700: course and heading sync ----------

    // C172-11: the course is the CDI's (measured stepping the CDI softkey: GPS moved GPS OBS VALUE,
    // LOC1 NAV OBS:1, LOC2 NAV OBS:2), set through the stock events, never the CRS knob's input event.
    [Theory]
    [InlineData(true, 1, "GPS_OBS_SET")]
    [InlineData(true, 2, "GPS_OBS_SET")]
    [InlineData(false, 1, "VOR1_SET")]
    [InlineData(false, 2, "VOR2_SET")]
    public void The_course_goes_to_whatever_the_cdi_is_on(bool onGps, int navRadio, string evt)
    {
        Assert.Equal(evt, CessnaC172G1000Definition.CourseEventFor(onGps, navRadio));
    }

    [Fact]
    public void The_course_is_a_typed_field_the_definition_handles()
    {
        var d = Def.GetVariables()["C172G_AP_CRS_SET"];
        Assert.False(d.RenderAsButton);
        Assert.False(d.PreventTextInput);
        Assert.True(d.UnparseableTextAsNaN);   // a typo must never become 0, north
        Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency);
        Assert.Contains("C172G_AP_CRS_SET", Def.GetPanelControls()["GFC 700"]);
        // Never the CRS knob's own input event: the same +1 raised NAV 1's course and lowered NAV 2's.
        Assert.DoesNotContain("AS1000_COURSE_PFD", Def.GetVariables().Values.Select(v => v.Name));
        Assert.DoesNotContain("AS1000_COURSE_PFD", CessnaC172G1000Definition.AutopilotKeys.Values);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(361)]
    public void A_course_outside_0_to_360_is_refused_aloud(double typed)
    {
        var speech = new GatedSpeechCapture();
        var course = Def.GetVariables()["C172G_AP_CRS_SET"];

        Assert.True(Def.HandleUIVariableSet("C172G_AP_CRS_SET", typed, course, new SimConnectManager(IntPtr.Zero), speech));
        Assert.Equal(new[] { "Enter a course between 0 and 360" }, speech.All);
    }

    [Fact]
    public void A_course_with_no_simulator_says_so()
    {
        var speech = new GatedSpeechCapture();
        var course = Def.GetVariables()["C172G_AP_CRS_SET"];

        Def.HandleUIVariableSet("C172G_AP_CRS_SET", 90, course, new SimConnectManager(IntPtr.Zero), speech);
        Assert.Equal(new[] { "Not connected to simulator." }, speech.All);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Headings_read_as_three_digits_with_north_360_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("039", CessnaC172G1000Definition.HeadingDigits(39.4));
            Assert.Equal("090", CessnaC172G1000Definition.HeadingDigits(90));
            Assert.Equal("360", CessnaC172G1000Definition.HeadingDigits(0));
            Assert.Equal("360", CessnaC172G1000Definition.HeadingDigits(359.6));
            Assert.Equal("359", CessnaC172G1000Definition.HeadingDigits(-1));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Heading_sync_is_the_hdg_knob_push_on_the_gfc_700_panel()
    {
        Assert.Equal("AS1000_HEADING_SYNC_PFD", CessnaC172G1000Definition.AutopilotKeys["C172G_AP_HDG_SYNC"]);
        var d = Def.GetVariables()["C172G_AP_HDG_SYNC"];
        Assert.True(d.RenderAsButton);
        Assert.Equal(SimVarType.InputEvent, d.Type);
        Assert.Contains("C172G_AP_HDG_SYNC", Def.GetPanelControls()["GFC 700"]);
    }
}
