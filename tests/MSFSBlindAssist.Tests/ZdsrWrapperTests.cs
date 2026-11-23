using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Tests;

/// <summary>ZDSR speech output (ZdsrWrapper) against a fake ZDSRAPI: when ZDSR counts as running, what each call
/// passes through, and how a failure hands the message back to the next output. The real DLL is exercised only
/// live, with ZDSR running.</summary>
public class ZdsrWrapperTests
{
    private sealed class FakeApi : IZdsrApi
    {
        public string Location => @"C:\Program Files (x86)\zdsr\zdsr\ZDSRAPI_x64.dll";
        public int SpeakState = ZdsrWrapper.StateNotSpeaking;
        public int SpeakResult = ZdsrWrapper.ResultSuccess;
        public int BrailleResult = ZdsrWrapper.ResultSuccess;
        public bool Throw;
        public readonly List<(int Type, string? Name, bool KeyDownInterrupt)> Inits = new();
        public readonly List<(string Text, bool Interrupt)> Spoken = new();
        public readonly List<(string Text, bool Flash)> Brailled = new();
        public int StateQueries;

        public int InitTTS(int channelType, string? channelName, bool keyDownInterrupt)
        {
            Inits.Add((channelType, channelName, keyDownInterrupt));
            return ZdsrWrapper.ResultSuccess;
        }

        public int Speak(string text, bool interrupt)
        {
            if (Throw) throw new System.Runtime.InteropServices.SEHException("boom");
            Spoken.Add((text, interrupt));
            return SpeakResult;
        }

        public int GetSpeakState()
        {
            StateQueries++;
            if (Throw) throw new System.Runtime.InteropServices.SEHException("boom");
            return SpeakState;
        }

        public void StopSpeak() { }

        public int Braille(string text, bool flashMessage)
        {
            Brailled.Add((text, flashMessage));
            return BrailleResult;
        }
    }

    private sealed class Rig
    {
        public long Now = 100_000;
        public readonly FakeApi Api = new();
        public bool Found = true;
        public bool ProcessRunning = true;
        public int Locates;
        public readonly ZdsrWrapper Zdsr;

        public Rig()
        {
            Zdsr = new ZdsrWrapper(() => { Locates++; return Found ? Api : null; }, () => Now, () => ProcessRunning);
        }

        public void Advance(long ms) => Now += ms;
    }

    [Theory]
    [InlineData(3, true)]   // speaking
    [InlineData(4, true)]   // not speaking
    [InlineData(0, false)]
    [InlineData(1, false)]  // version mismatch
    [InlineData(2, false)]  // not running or not licensed
    [InlineData(8, false)]  // command not delivered
    [InlineData(9, false)]  // separate channel not ready
    [InlineData(-1, false)] // the call threw
    public void Only_speaking_and_not_speaking_mean_ZDSR_is_running(int state, bool running)
        => Assert.Equal(running, ZdsrWrapper.IsRunningState(state));

    [Fact]
    public void The_running_ZDSRs_own_folder_comes_before_the_standard_install()
    {
        var folders = ZdsrWrapper.CandidateFolders(@"D:\Portable\ZDSR\ZDSRMain.exe", @"C:\Program Files (x86)");
        Assert.Equal(new[] { @"D:\Portable\ZDSR", @"C:\Program Files (x86)\zdsr\zdsr" }, folders);
    }

    [Fact]
    public void A_ZDSR_running_from_the_standard_install_is_looked_up_once()
    {
        var folders = ZdsrWrapper.CandidateFolders(@"C:\Program Files (x86)\ZDSR\ZDSR\ZDSRMain.exe", @"C:\Program Files (x86)\");
        Assert.Equal(new[] { @"C:\Program Files (x86)\ZDSR\ZDSR" }, folders);
    }

    [Fact]
    public void Without_a_readable_process_path_only_the_standard_install_is_tried()
    {
        Assert.Equal(new[] { @"C:\Program Files (x86)\zdsr\zdsr" },
            ZdsrWrapper.CandidateFolders(null, @"C:\Program Files (x86)"));
        Assert.Empty(ZdsrWrapper.CandidateFolders(null, null));
    }

    [Fact]
    public void Not_running_while_ZDSR_is_not_found_and_the_process_list_is_checked_only_every_few_seconds()
    {
        var rig = new Rig { Found = false };

        Assert.False(rig.Zdsr.IsRunning);
        Assert.Equal(1, rig.Locates);

        rig.Advance(ZdsrWrapper.LocateIntervalMs - 1);
        Assert.False(rig.Zdsr.IsRunning);
        Assert.Equal(1, rig.Locates);

        rig.Advance(1);
        rig.Found = true;
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Equal(2, rig.Locates);
    }

    [Fact]
    public void Speech_goes_to_ZDSRs_own_channel_with_key_presses_interrupting_it()
    {
        var rig = new Rig();

        Assert.True(rig.Zdsr.IsRunning);

        Assert.Equal(new[] { (ZdsrWrapper.ZdsrChannel, (string?)null, true) }, rig.Api.Inits);
        Assert.Equal(rig.Api.Location, rig.Zdsr.Location);
    }

    [Fact]
    public void Once_found_the_API_is_kept_and_the_process_list_is_not_checked_again()
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);

        rig.Advance(ZdsrWrapper.LocateIntervalMs * 3);
        Assert.True(rig.Zdsr.IsRunning);

        Assert.Equal(1, rig.Locates);
    }

    [Fact]
    public void A_verdict_is_reused_for_a_second_so_an_announcement_costs_one_call()
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Equal(1, rig.Api.StateQueries);

        rig.Advance(ZdsrWrapper.VerdictCacheMs - 1);
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Equal(1, rig.Api.StateQueries);

        rig.Advance(1);
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Equal(2, rig.Api.StateQueries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_interrupt_flag_is_passed_through_and_the_text_is_brailled_as_a_flash_message(bool interrupt)
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);

        Assert.True(rig.Zdsr.Speak("Gear down, three green", interrupt));

        Assert.Equal(new[] { ("Gear down, three green", interrupt) }, rig.Api.Spoken);
        Assert.Equal(new[] { ("Gear down, three green", true) }, rig.Api.Brailled);
    }

    [Fact]
    public void No_braille_display_still_counts_as_spoken()
    {
        var rig = new Rig();
        rig.Api.BrailleResult = 3; // no braille display connected
        Assert.True(rig.Zdsr.IsRunning);

        Assert.True(rig.Zdsr.Speak("Flaps 1", false));
    }

    [Fact]
    public void A_refused_message_returns_false_and_ZDSR_counts_as_stopped_until_the_next_check()
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);
        rig.Api.SpeakResult = ZdsrWrapper.ResultNotRunning;
        rig.Api.SpeakState = ZdsrWrapper.ResultNotRunning;

        Assert.False(rig.Zdsr.Speak("Cabin ready", true));
        Assert.Empty(rig.Api.Brailled);

        // Even inside the old verdict's second: the next message goes straight to the fallback.
        int queries = rig.Api.StateQueries;
        Assert.False(rig.Zdsr.IsRunning);
        Assert.Equal(queries, rig.Api.StateQueries);
    }

    [Fact]
    public void When_ZDSR_comes_back_its_channel_settings_are_applied_again()
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Single(rig.Api.Inits);

        rig.Api.SpeakState = ZdsrWrapper.ResultNotRunning; // ZDSR closed
        rig.Advance(ZdsrWrapper.VerdictCacheMs);
        Assert.False(rig.Zdsr.IsRunning);

        rig.Api.SpeakState = ZdsrWrapper.StateSpeaking; // ZDSR restarted
        rig.Advance(ZdsrWrapper.VerdictCacheMs);
        Assert.True(rig.Zdsr.IsRunning);

        Assert.True(rig.Api.Inits.Count >= 2);
        Assert.All(rig.Api.Inits, init => Assert.Equal((ZdsrWrapper.ZdsrChannel, (string?)null, true), init));
    }

    [Fact]
    public void An_API_that_throws_is_reported_as_not_running_and_never_throws_into_the_announcer()
    {
        var rig = new Rig();
        rig.Api.Throw = true;

        Assert.False(rig.Zdsr.IsRunning);
        Assert.False(rig.Zdsr.Speak("Stop", true));
    }

    [Fact]
    public void Nothing_is_spoken_before_ZDSR_has_been_found_or_for_empty_text()
    {
        var rig = new Rig();
        Assert.False(rig.Zdsr.Speak("Too early", false));
        Assert.Equal(0, rig.Locates);

        Assert.True(rig.Zdsr.IsRunning);
        Assert.False(rig.Zdsr.Speak("", false));
        Assert.Empty(rig.Api.Spoken);
    }

    [Fact]
    public void A_restart_nobody_saw_gets_the_channel_settings_again()
    {
        // The check only runs when something is announced: ZDSR can close and come back in a quiet stretch
        // with the wrapper still believing it is up.
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);
        Assert.Single(rig.Api.Inits);

        rig.Advance(ZdsrWrapper.ReinitAfterIdleMs);
        Assert.True(rig.Zdsr.IsRunning);

        Assert.Equal(2, rig.Api.Inits.Count);
    }

    [Fact]
    public void While_ZDSR_keeps_answering_the_channel_settings_are_not_sent_again()
    {
        var rig = new Rig();
        Assert.True(rig.Zdsr.IsRunning);

        // A busy stretch: a check each second.
        for (int i = 0; i < 6; i++)
        {
            rig.Advance(ZdsrWrapper.VerdictCacheMs);
            Assert.True(rig.Zdsr.IsRunning);
        }
        // A spoken message counts as an answer too: 4 s since the last check, 2 s since the message.
        rig.Advance(2000);
        Assert.True(rig.Zdsr.Speak("Flaps 2", false));
        rig.Advance(2000);
        Assert.True(rig.Zdsr.IsRunning);

        Assert.Single(rig.Api.Inits);
    }

    [Theory]
    [InlineData(false, @"D:\Portable\ZDSR", true)]                       // not elevated: anywhere
    [InlineData(true, @"C:\Program Files (x86)\zdsr\zdsr", true)]
    [InlineData(true, @"c:\program files\ZDSR", true)]
    [InlineData(true, @"D:\Portable\ZDSR", false)]
    [InlineData(true, @"C:\Users\pilot\AppData\Local\Temp", false)]
    [InlineData(true, @"C:\Program Files (x86)\..\Users\pilot\zdsr", false)] // ".." resolved first
    [InlineData(true, @"C:\Program Files (x86) evil\zdsr", false)]          // a prefix, not a parent
    [InlineData(true, @"C:\Program Files (x86)", false)]                    // the root itself holds no ZDSR
    public void An_elevated_BA_loads_ZDSRs_API_only_from_under_Program_Files(bool elevated, string folder, bool allowed)
    {
        string?[] roots = { @"C:\Program Files", @"C:\Program Files (x86)\", null, "" };
        Assert.Equal(allowed, ZdsrWrapper.MayLoadFrom(folder, elevated, roots));
    }

    [Fact]
    public void A_running_ZDSR_that_cannot_be_used_is_named_with_its_reason()
    {
        // No process: nothing to say, whatever the rest.
        Assert.Null(ZdsrWrapper.DescribeUnavailable(false, false, int.MinValue));
        Assert.Null(ZdsrWrapper.DescribeUnavailable(false, true, ZdsrWrapper.ResultNotRunning));
        // Loaded but never asked: nothing known yet.
        Assert.Null(ZdsrWrapper.DescribeUnavailable(true, true, int.MinValue));

        Assert.Contains("ZDSRAPI_x64.dll", ZdsrWrapper.DescribeUnavailable(true, false, int.MinValue));
        Assert.Contains("not licensed", ZdsrWrapper.DescribeUnavailable(true, true, ZdsrWrapper.ResultNotRunning));
        Assert.Contains("different versions", ZdsrWrapper.DescribeUnavailable(true, true, 1));
        Assert.Contains("8 (command not delivered)", ZdsrWrapper.DescribeUnavailable(true, true, 8));
    }

    [Fact]
    public void The_wrapper_gives_the_reason_only_while_ZDSR_runs_and_does_not_speak()
    {
        // Running but unlicensed.
        var rig = new Rig();
        rig.Api.SpeakState = ZdsrWrapper.ResultNotRunning;
        Assert.False(rig.Zdsr.IsRunning);
        Assert.Contains("not licensed", rig.Zdsr.UnavailableReason);

        // Its API was loaded earlier, and ZDSR has since been closed.
        rig.ProcessRunning = false;
        Assert.Null(rig.Zdsr.UnavailableReason);

        // Running, and its API cannot be loaded.
        var noApi = new Rig { Found = false };
        Assert.False(noApi.Zdsr.IsRunning);
        Assert.Contains("could not be loaded", noApi.Zdsr.UnavailableReason);

        // Speaking: no reason.
        var fine = new Rig();
        Assert.True(fine.Zdsr.IsRunning);
        Assert.Null(fine.Zdsr.UnavailableReason);
    }

    [Fact]
    public void ZDSR_speaks_in_Screen_Reader_mode_only_so_SAPI_mode_keeps_the_Windows_voice()
    {
        Assert.True(ScreenReaderAnnouncer.ZdsrAllowed(AnnouncementMode.ScreenReader));
        Assert.False(ScreenReaderAnnouncer.ZdsrAllowed(AnnouncementMode.SAPI));
    }

    [Fact]
    public void The_status_box_says_why_a_running_ZDSR_is_not_used_instead_of_no_screen_reader()
    {
        string nl = Environment.NewLine;
        Assert.Equal($"Screen reader detected: ZDSR{nl}Choose 'Screen Reader' for best experience.",
            ScreenReaderAnnouncer.DetectionStatusText("ZDSR", null, true));
        // A detected reader wins over a ZDSR problem (JAWS running beside an unlicensed ZDSR).
        Assert.StartsWith("Screen reader detected: JAWS",
            ScreenReaderAnnouncer.DetectionStatusText("JAWS", "ZDSR is running but ...", true));
        Assert.Equal($"ZDSR is running but not accepting speech.{nl}Windows speech (SAPI) is used instead.",
            ScreenReaderAnnouncer.DetectionStatusText(null, "ZDSR is running but not accepting speech.", true));
        Assert.Equal($"No screen reader detected.{nl}SAPI mode recommended for speech feedback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, null, true));
        Assert.Equal($"Unable to initialize screen reader detection.{nl}SAPI mode will be used as fallback.",
            ScreenReaderAnnouncer.DetectionStatusText(null, null, false));
    }

    [Fact]
    public void A_second_Tolk_wrapper_joins_the_session_and_only_the_last_one_unloads_it()
    {
        var count = new SharedLoadCount();
        int loads = 0, unloads = 0;
        bool loaded = false;
        void Load() { loads++; loaded = true; }
        void Unload() { unloads++; loaded = false; }

        Assert.True(count.Acquire(Load, () => loaded));   // the announcer
        Assert.True(count.Acquire(Load, () => loaded));   // the Settings status check
        Assert.Equal(1, loads);

        count.Release(Unload);                            // Settings closes
        Assert.Equal(0, unloads);
        Assert.True(loaded);

        count.Release(Unload);                            // the app exits
        Assert.Equal(1, unloads);
        count.Release(Unload);                            // a release nobody acquired
        Assert.Equal(1, unloads);

        Assert.True(count.Acquire(Load, () => loaded));   // loads again after a full unload
        Assert.Equal(2, loads);
    }

    [Fact]
    public void A_Tolk_load_that_fails_is_not_counted_so_the_next_wrapper_tries_again()
    {
        var count = new SharedLoadCount();
        int loads = 0, unloads = 0;

        Assert.False(count.Acquire(() => loads++, () => false));
        Assert.False(count.Acquire(() => loads++, () => false));
        Assert.Equal(2, loads);

        count.Release(() => unloads++);
        Assert.Equal(0, unloads);
    }

    [Theory]
    [InlineData(true, true, "JAWS", "NVDA")]
    [InlineData(false, true, "JAWS", "ZDSR")]
    [InlineData(false, true, null, "ZDSR")]
    [InlineData(false, false, "JAWS", "JAWS")]
    [InlineData(false, false, null, null)]
    [InlineData(false, false, "", null)]
    public void The_status_names_the_reader_announcements_go_to(bool nvda, bool zdsr, string? tolk, string? expected)
        => Assert.Equal(expected, ScreenReaderAnnouncer.ActiveScreenReaderName(nvda, zdsr, tolk));
}
