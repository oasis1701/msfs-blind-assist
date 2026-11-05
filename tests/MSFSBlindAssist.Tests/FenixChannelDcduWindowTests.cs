// Characterization tests for the Fenix DCDU window (Forms/Fenix/FenixChannelDcduForm.cs) and the definition's DCDU calls
// (Aircraft/FenixA320Definition.ChannelDcdu.cs, and the Variants.cs refusal for an airframe with no DCDU). The window is
// reached by Ctrl+Shift+D (the key seam) and by the "DCDU" entry of the Fenix windows list, and a new message is called by
// the announcer host's sampler; the way in, the sampler and the mute are pinned at the end of this file.
//
// The contract: the window is the base's list of rows ("DCDU display") with a keyboard on it. A key of the nine
// (FenixChannelDcduKeys.Map, the shared alternate soft keys setting picking the chords) is judged at the press on a
// snapshot taken then, never on the last refresh's, and a refusal is the only thing the window ever says: the result of a
// press is heard in the refreshed list, never the key's name ([CORE-7]); a result that is not on the row under the cursor
// (the status, the first info line: NO MORE MSG, SENDING, RECEIVD BY ATC) is read back by the sampler
// (FenixChannelDcduReadback), which every press tells first. Every press also tells the announcer, whose quiet then keeps
// the answered message, drawn again with a new first line, from being called as a new one. A press goes through the
// definition's own handler for the panel's S_DCDU1_* button, which presses and releases ([A320-3]) and carries the
// variant's refusal in front of it; the window and the definition's DCDU calls never write a variable, run a calculator
// string or open the channel themselves ([FNX-21]). A key held down presses once: the base asks the window about every
// auto-repeat too, and an answer key is pressed once for WILCO and again for SEND, so a held Ctrl+1 must not answer and
// send. Two buttons after the list, Bright and then Dim, step the brightness switch once each; when the switch has gone
// back and the value has moved the window reads N_DCDU1_BRIGHTNESS and says it ("Brightness 60 percent"), once for quick
// presses, never over a closed window; a throw in that read-back is logged and says nothing more. A DCDU turned all the way
// down still takes a message (measured 2026-11-03), so the window reads the brightness for nothing else.
//
// What is built here is never shown (the DCDU window needs a sim and a screen); the SimConnect manager is never connected,
// so a press the window sends writes nothing and the brightness is read through a stand-in. The rest, the key behaviour in
// the sim, is checked there ([CORE-5]).

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduWindowTests : IDisposable
{
    // The chords of these tests are the standard soft keys (Ctrl+1 and the rest); a pilot's own setting must not change them.
    private readonly bool _savedAlternateKeys = SettingsManager.Current.MCDUUseAlternateLSKKeys;

    public FenixChannelDcduWindowTests() => SettingsManager.Current.MCDUUseAlternateLSKKeys = false;

    public void Dispose() => SettingsManager.Current.MCDUUseAlternateLSKKeys = _savedAlternateKeys;

    // ---- An airframe with no DCDU ---------------------------------------------------------------

    [Fact]
    public void A_fenix_without_a_dcdu_refuses_and_an_unread_one_does_not()
    {
        // Only a variant that says it has none refuses; a Fenix whose DCDU field is not read yet ([FNX-11]) keeps the window.
        var speech = new SpeechCapture();
        FenixVariant none = FenixVariant.Unknown with { HasDcdu = false };
        FenixVariant fitted = FenixVariant.Unknown with { HasDcdu = true };

        Assert.True(FenixA320Definition.TryRefuseNoDcdu(none, speech));
        Assert.Equal(new[] { "This Fenix has no DCDU." }, speech.Interrupts);
        Assert.Equal("This Fenix has no DCDU.", FenixA320Definition.NoDcduMessage);

        var quiet = new SpeechCapture();
        Assert.False(FenixA320Definition.TryRefuseNoDcdu(fitted, quiet));
        Assert.False(FenixA320Definition.TryRefuseNoDcdu(FenixVariant.Unknown, quiet));
        Assert.Null(FenixVariant.Unknown.HasDcdu);
        Assert.Empty(quiet.All);
    }

    [Fact]
    public void Without_a_monitor_the_window_says_not_available()
    {
        // Before Start and after Stop there is no monitor to show: the pilot is told, and nothing opens.
        var definition = new FenixA320Definition();
        var speech = new SpeechCapture();

        definition.OpenDcduWindow(new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "DCDU display not available" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    // ---- The window -----------------------------------------------------------------------------

    private static FenixChannelDcduForm NewWindow(
        out FenixChannelMonitor monitor, out FenixA320Definition definition, out SpeechCapture speech)
    {
        monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        definition = new FenixA320Definition();
        speech = new SpeechCapture();
        return new FenixChannelDcduForm(monitor, definition, new SimConnectManager(IntPtr.Zero), speech);
    }

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const long KeyWasDown = 0x40000000;   // bit 30 of a key message's lParam: an auto-repeat

    private static readonly MethodInfo ProcessCmdKey =
        typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // What the window's ProcessCmdKey makes of a key press, as the message loop would hand it over.
    private static bool Press(Form form, Keys keyData, bool autoRepeat = false, int message = WM_KEYDOWN)
    {
        var msg = Message.Create(IntPtr.Zero, message, (IntPtr)(int)(keyData & Keys.KeyCode),
            (IntPtr)(autoRepeat ? KeyWasDown | 1 : 1));
        object?[] args = { msg, keyData };
        return (bool)ProcessCmdKey.Invoke(form, args)!;
    }

    [Fact]
    public void The_window_is_the_dcdu_list_with_bright_and_dim_buttons_after_it()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out _);
        using (monitor)
        {
            Assert.Equal("Fenix DCDU", form.Text);

            DisplayListBox list = form.Controls.OfType<DisplayListBox>().Single();
            Assert.Equal("DCDU display", list.AccessibleName);

            // No combo any more: two plain buttons, Bright then Dim in the tab order, each read by its Text alone.
            Assert.Empty(form.Controls.OfType<ComboBox>());
            Button[] buttons = form.Controls.OfType<Button>().OrderBy(button => button.TabIndex).ToArray();
            Assert.Equal(new[] { "Bright", "Dim" }, buttons.Select(button => button.Text));
            foreach (Button button in buttons)
            {
                Assert.True(string.IsNullOrEmpty(button.AccessibleName), $"{button.Text} has the AccessibleName {button.AccessibleName}");
                Assert.Equal(DockStyle.Bottom, button.Dock);
            }

            // After the list, in the layout and in the tab order.
            Assert.True(list.TabIndex < buttons[0].TabIndex, "the Bright button is not after the list in the tab order");
            Assert.True(buttons[0].TabIndex < buttons[1].TabIndex, "Dim is not after Bright in the tab order");
            Assert.Equal(DockStyle.Fill, list.Dock);
        }
    }

    [Fact]
    public void A_key_that_is_none_of_the_nine_is_left_to_the_window()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            // The keys that move through the list, the chords the standard setting does not map, and the alternate set.
            foreach (Keys key in new[]
                     {
                         Keys.Down, Keys.Up, Keys.Home, Keys.Tab, Keys.Control | Keys.D3, Keys.Alt | Keys.PageDown,
                         Keys.Shift | Keys.PageDown, Keys.F1, Keys.F2, Keys.F7, Keys.F8
                     })
            {
                Assert.False(Press(form, key), $"{key} was taken");
            }

            Assert.Empty(speech.All);
        }
    }

    [Fact]
    public void The_alternate_soft_keys_setting_picks_the_soft_key_chords()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            SettingsManager.Current.MCDUUseAlternateLSKKeys = true;

            // F1 is a soft key now, refused here because the idle monitor's DCDU cannot be read; Ctrl+1 is nothing.
            Assert.True(Press(form, Keys.F1));
            Assert.Equal(new[] { FenixChannelDcduKeys.NotAvailableMessage }, speech.Interrupts);
            Assert.False(Press(form, Keys.Control | Keys.D1));
            Assert.False(Press(form, Keys.Alt | Keys.D1, message: WM_SYSKEYDOWN));
            Assert.Single(speech.All);
        }
    }

    [Fact]
    public void A_soft_key_on_a_display_that_cannot_be_read_says_so_and_a_page_key_says_nothing()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            // The idle monitor trusts no display: a soft key presses nothing and says why, at once.
            Assert.True(Press(form, Keys.Control | Keys.D1));
            Assert.Equal(new[] { "DCDU not available" }, speech.Interrupts);
            Assert.Equal(speech.Interrupts, speech.All);

            // The page, message and print keys press whatever the display shows, and their result is heard in the list.
            foreach (Keys key in new[] { Keys.PageDown, Keys.PageUp, Keys.Control | Keys.PageDown, Keys.Control | Keys.PageUp, Keys.Control | Keys.P })
            {
                Assert.True(Press(form, key), $"{key} was not taken");
            }

            Assert.Single(speech.All);
        }
    }

    [Fact]
    public void A_held_key_is_taken_and_does_nothing_more()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            Assert.True(Press(form, Keys.Control | Keys.D1));
            Assert.Single(speech.All);

            // Each repeat has the already-down bit set: taken, so the list never sees it, and judged and pressed by nobody.
            Assert.True(Press(form, Keys.Control | Keys.D1, autoRepeat: true));
            Assert.True(Press(form, Keys.Control | Keys.D1, autoRepeat: true));
            Assert.Single(speech.All);

            // A new press after the key was let go goes ahead.
            Assert.True(Press(form, Keys.Control | Keys.D1));
            Assert.Equal(2, speech.All.Count);

            // A key that maps to nothing is no business of the window's, held or not.
            Assert.False(Press(form, Keys.Down, autoRepeat: true));
        }
    }

    // ---- The definition's calls -----------------------------------------------------------------

    private static readonly FieldInfo AnnouncerField =
        typeof(FenixA320Definition).GetField("_dcduAnnouncer", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // A DCDU that reads: the header's left half and one message line, nothing else.
    private static FenixChannelSection<FenixDcduScreen> MessageScreen(string headerLeft, string firstLine)
        => FenixChannelSection<FenixDcduScreen>.Ok(new FenixDcduScreen(
            headerLeft, "", [firstLine, "", "", "", ""], ["", "", ""], "", "", "", ""));

    [Fact]
    public void Every_key_notes_the_own_press_so_that_what_it_brings_is_never_called()
    {
        // The pilot's own paging, stepping and answering are never announced as news: each of the nine keys tells the announcer
        // first, so that a message that comes onto the screen within the quiet that follows is silent.
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        FenixChannelSection<FenixDcduScreen> before = MessageScreen("w1426Z FROM EDGG", "cCLIMB TO");
        FenixChannelSection<FenixDcduScreen> after = MessageScreen("w1431Z FROM EDGG", "cDESCEND TO");

        foreach (FenixDcduKey key in Enum.GetValues<FenixDcduKey>())
        {
            var definition = new FenixA320Definition();
            var announcer = new FenixChannelDcduAnnouncer();
            AnnouncerField.SetValue(definition, announcer);

            // The first screen is the baseline; then the key; then a new message, held for the settle, inside the quiet.
            Assert.Null(announcer.Sample(before, 1, muted: false, nowMs: 0));
            definition.PressDcdu(key, sim, speech);
            Assert.Null(announcer.Sample(after, 1, muted: false, nowMs: 1000));
            string? call = announcer.Sample(after, 1, muted: false, nowMs: 1000 + FenixChannelDcduAnnouncer.SettleMs);

            Assert.True(call is null, $"{key} is the pilot's own press, yet the screen after it was called: {call}");
        }

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_message_that_comes_with_no_press_before_it_is_called()
    {
        // The quiet is the press's: with no key pressed, the same new message is news.
        var announcer = new FenixChannelDcduAnnouncer();

        Assert.Null(announcer.Sample(MessageScreen("w1426Z FROM EDGG", "cCLIMB TO"), 1, muted: false, nowMs: 0));
        FenixChannelSection<FenixDcduScreen> after = MessageScreen("w1431Z FROM EDGG", "cDESCEND TO");
        Assert.Null(announcer.Sample(after, 1, muted: false, nowMs: 1000));
        Assert.Equal("DCDU: 1431Z FROM EDGG, DESCEND TO",
            announcer.Sample(after, 1, muted: false, nowMs: 1000 + FenixChannelDcduAnnouncer.SettleMs));
    }

    // A clearance with its answer open, as the Fenix drew it after one press of WILCO (build 3.0.0.7613, 2026-11-03): the status
    // says WILCO, the keys are CANCEL and SEND, and the message counter reads message 2 of 2.
    private static FenixChannelSection<FenixDcduScreen> AnswerOpen()
        => FenixChannelSection<FenixDcduScreen>.Ok(new FenixDcduScreen(
            "g 2124Z FROM EDDF", "ciWILCO",
            ["CLRD TO cEGSSw.".PadRight(24), "cOBOK2Ew DEPARTURE.".PadRight(24), "MNTN c4000w. SQUAWK".PadRight(24),
                "c2443w. EXPCT RWY".PadRight(24), "c07Cw.".PadRight(24)],
            ["", "MSG".PadRight(14), "2/2".PadRight(14)], "c*CANCEL", "", "", "cSEND*"));

    // The same message 1.2 s after SEND, as the Fenix sent it: re-wrapped in green with a NEW first line, SENDING on the first
    // info line, and CLOSE the only key.
    private static FenixChannelSection<FenixDcduScreen> AnswerSent()
        => FenixChannelSection<FenixDcduScreen>.Ok(new FenixDcduScreen(
            "g 2124Z FROM EDDF", "giWILCO",
            ["gCLRD TO EGSS. OBOK2E    ", "gDEPARTURE. MNTN 4000.   ", "gSQUAWK 2443. EXPCT RWY  ", "g07C.                    ", ""],
            ["SENDING", "MSG           ", "2/2           "], "", "", "", "cCLOSE*"));

    [Fact]
    public void An_answered_message_the_fenix_re_wraps_is_not_called_as_new()
    {
        // The pilot's SEND re-wraps the message with a new first line, which the announcer took for a new message and called
        // ("DCDU: 2124Z FROM EDDF, CLRD TO EGSS. OBOK2E", heard 2026-11-03). Every press of the window's keys is the pilot's own
        // reading, not only the page and message keys, so the quiet that follows it silences the re-wrap.
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        foreach (FenixDcduKey key in new[] { FenixDcduKey.RightKey2, FenixDcduKey.LeftKey1, FenixDcduKey.Print })
        {
            var definition = new FenixA320Definition();
            var announcer = new FenixChannelDcduAnnouncer();
            AnnouncerField.SetValue(definition, announcer);

            Assert.Null(announcer.Sample(AnswerOpen(), 1, muted: false, nowMs: 0));
            definition.PressDcdu(key, sim, speech);

            // The sampler ticks every 100 ms; the re-wrap is on the screen from 1.2 s and holds the settle long before 3 s.
            for (long ms = 100; ms <= 9000; ms += 100)
            {
                string? call = announcer.Sample(ms < 1200 ? AnswerOpen() : AnswerSent(), 1, muted: false, nowMs: ms);
                Assert.True(call is null, $"{key} was followed by a call at {ms} ms: {call}");
            }
        }

        Assert.Empty(speech.All);
    }

    private static readonly FieldInfo ReadbackField =
        typeof(FenixA320Definition).GetField("_dcduReadback", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // A DCDU that reads, with the given first info line.
    private static FenixChannelSection<FenixDcduScreen> InfoScreen(string info1)
        => FenixChannelSection<FenixDcduScreen>.Ok(new FenixDcduScreen(
            "g 2021Z FROM EDDF", "cOPEN", ["CLRD TO cEGSSw.", "", "", "", ""], [info1, "MSG", "2/2"], "", "", "", ""));

    [Fact]
    public void Every_press_notes_the_read_back_so_that_its_result_is_heard()
    {
        // After the pilot's own press the DCDU's first info line (a message key with nothing to step to: NO MORE MSG) may be
        // the only sign of the result, and it is not on the row under the cursor. Each of the nine keys tells the read-back
        // first, so that the first sample is the before reading.
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        foreach (FenixDcduKey key in Enum.GetValues<FenixDcduKey>())
        {
            var definition = new FenixA320Definition();
            var readback = new FenixChannelDcduReadback();
            ReadbackField.SetValue(definition, readback);

            definition.PressDcdu(key, sim, speech);

            // The first sample is the before reading (and the baseline), the next the result: with the DCDU muted it is still
            // heard, because a key is never silent.
            Assert.Null(readback.Sample(InfoScreen(""), muted: true, nowMs: 0));
            Assert.True(readback.Sample(InfoScreen("NO MORE MSG"), muted: true, nowMs: 200) == "NO MORE MSG",
                $"{key} did not note the read-back");
        }

        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_first_press_makes_the_read_back_and_the_announcer_when_the_sampler_has_not()
    {
        var definition = new FenixA320Definition();
        Assert.Null(ReadbackField.GetValue(definition));
        Assert.Null(AnnouncerField.GetValue(definition));

        definition.PressDcdu(FenixDcduKey.Print, new SimConnectManager(IntPtr.Zero), new SpeechCapture());

        Assert.NotNull(ReadbackField.GetValue(definition));
        Assert.NotNull(AnnouncerField.GetValue(definition));
    }

    // ---- Reading the source ---------------------------------------------------------------------

    private static string SourcePath(params string[] parts)
        => Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(parts).ToArray());

    private static string FormPath => SourcePath("Forms", "Fenix", "FenixChannelDcduForm.cs");

    private static string DefinitionPath => SourcePath("Aircraft", "FenixA320Definition.ChannelDcdu.cs");

    // A line comment or a block comment: a call named in a comment is no call.
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string CodeOf(string path) => Comment.Replace(File.ReadAllText(path), "");

    private static string BodyOf(string path, string method) => FoFbwUnclaimedEventKeyTests.MethodBody(path, method);

    [Fact]
    public void The_window_judges_each_soft_key_on_a_fresh_snapshot()
    {
        // The snapshot is taken at the press and is the one handed to Decide, never one kept from an earlier refresh: a message
        // that arrived after the last refresh relabels a slot, and Decide asks whether the label it finds on that snapshot is
        // blank.
        string body = BodyOf(FormPath, "HandleWindowKey");
        Match taken = Regex.Match(body, @"FenixChannelSnapshot\s+(\w+)\s*=\s*_monitor\.Snapshot\(FenixChannelDisplay\.Dcdu\)\s*;");
        int decide = body.IndexOf("FenixChannelDcduKeys.Decide(", StringComparison.Ordinal);

        Assert.True(taken.Success, "HandleWindowKey never takes a snapshot of the DCDU into a local");
        Assert.True(decide >= 0, "HandleWindowKey never decides a key");
        Assert.True(taken.Index < decide, "HandleWindowKey decides a key before it has taken its snapshot");
        Assert.Matches(@"FenixChannelDcduKeys\.Decide\(\s*key\s*,\s*" + taken.Groups[1].Value + @"\s*\)", body);

        // Read only: the window sends nothing itself and never opens the channel.
        string code = CodeOf(FormPath);
        foreach (string forbidden in new[] { "SetLVar(", "ExecuteCalculatorCode", "FenixChannelClient" })
            Assert.False(code.Contains(forbidden, StringComparison.Ordinal), $"the DCDU window uses {forbidden}");
    }

    [Fact]
    public void Every_press_goes_through_the_definition_handler()
    {
        foreach (string method in new[] { "PressDcdu", "SetDcduBrightness" })
        {
            string body = BodyOf(DefinitionPath, method);

            Assert.Contains("HandleUIVariableSet(", body);
            Assert.DoesNotContain("SetLVar(", body);
        }

        // The window presses through the definition, by key, and never the variable itself.
        string window = BodyOf(FormPath, "HandleWindowKey");
        Assert.Contains("PressDcdu(", window);
        Assert.DoesNotContain("HandleUIVariableSet(", CodeOf(FormPath));
    }

    [Fact]
    public void A_held_key_presses_once()
    {
        // The base asks a derived window about every auto-repeat, and the window reads the already-down bit of the key
        // message itself, as the base reads it for F5.
        string cmdKey = BodyOf(FormPath, "ProcessCmdKey");
        Assert.Contains("msg.LParam.ToInt64()", cmdKey);
        Assert.Contains("KeyWasDown", cmdKey);
        Assert.Contains("KeyWasDown = 0x40000000", CodeOf(FormPath));

        // In HandleWindowKey: the map first (a key that is none of the nine is left alone), then the repeat's return, and
        // only then the decision, the speech and the press.
        string body = BodyOf(FormPath, "HandleWindowKey");
        int map = body.IndexOf("FenixChannelDcduKeys.Map(", StringComparison.Ordinal);
        int repeat = body.IndexOf("if (_autoRepeat) return true;", StringComparison.Ordinal);
        int decide = body.IndexOf("FenixChannelDcduKeys.Decide(", StringComparison.Ordinal);

        Assert.True(map >= 0, "HandleWindowKey never maps the key");
        Assert.True(repeat >= 0, "HandleWindowKey never returns for an auto-repeat");
        Assert.True(decide >= 0, "HandleWindowKey never decides a key");
        Assert.True(map < repeat, "HandleWindowKey takes an auto-repeat before it has mapped the key");
        Assert.True(repeat < decide, "HandleWindowKey decides a key before it has returned for an auto-repeat");
    }

    [Fact]
    public void A_refusal_is_spoken_and_only_a_decision_without_one_presses()
    {
        // A decision with a refusal says it and stops; the press is the other arm of the same branch, so a refused key can
        // never also press. Without the else the refused soft key would be announced and pressed.
        string body = BodyOf(FormPath, "HandleWindowKey");
        Match refusal = Regex.Match(body, @"if\s*\(\s*decision\.Refusal\s+is\s*\{\s*\}\s+(\w+)\s*\)");
        Assert.True(refusal.Success, "HandleWindowKey never tests the decision's refusal");

        int speak = body.IndexOf("AnnounceImmediate(" + refusal.Groups[1].Value + ")", refusal.Index, StringComparison.Ordinal);
        Assert.True(speak >= 0, "HandleWindowKey does not speak the refusal it found");

        Match otherwise = Regex.Match(body[speak..], @"\belse\s+if\s*\(\s*decision\.VariableKey\s+is\s+not\s+null\s*\)");
        Assert.True(otherwise.Success, "the press is not the else of the refusal: a refused key could press too");

        int press = body.IndexOf("PressDcdu(", StringComparison.Ordinal);
        Assert.True(press > speak + otherwise.Index, "HandleWindowKey presses before the else that has no refusal");
        Assert.Equal(press, body.LastIndexOf("PressDcdu(", StringComparison.Ordinal));
    }

    // ---- Bright and Dim ---------------------------------------------------------------------

    [Fact]
    public void Bright_steps_the_switch_to_its_bright_end_and_Dim_to_its_dim_end()
    {
        // Position 2 is Bright and 0 is Dim (the switch rests on 1 and springs back to it); the click goes to one method that
        // presses and then waits for the read-back.
        string code = CodeOf(FormPath);
        Assert.Contains("BrightPosition = 2;", code);
        Assert.Contains("DimPosition = 0;", code);
        Assert.Matches(@"_bright\.Click\s*\+=\s*\(\s*_\s*,\s*_\s*\)\s*=>\s*PressBrightness\(\s*BrightPosition\s*\)\s*;", code);
        Assert.Matches(@"_dim\.Click\s*\+=\s*\(\s*_\s*,\s*_\s*\)\s*=>\s*PressBrightness\(\s*DimPosition\s*\)\s*;", code);

        // The press goes through the definition, and the read-back's wait is the time the switch takes to come back.
        string press = BodyOf(FormPath, "PressBrightness");
        Assert.Contains("_definition.SetDcduBrightness(position,", press);
        Assert.DoesNotContain("SetLVar(", press);
        Assert.Contains("FenixA320Definition.DcduSpringBackMs", code);

        // No combo, and no timer that shows Mid again.
        Assert.DoesNotContain("ComboBox", code);
        Assert.DoesNotContain("_returnTimer", code);
        Assert.DoesNotContain("SelectionChangeCommitted", code);
    }

    // N_DCDU1_BRIGHTNESS as the sim sends it for a count of the Fenix's own value (0 to 510): the count over 1023.
    private static double Level(double count) => count / 1023.0;

    private static readonly FieldInfo ReadBackTimerField =
        typeof(FenixChannelDcduForm).GetField("_readBackTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void A_press_starts_the_wait_for_the_switch_to_come_back_and_says_nothing_itself()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            var timer = (System.Windows.Forms.Timer)ReadBackTimerField.GetValue(form)!;
            Assert.False(timer.Enabled);
            Assert.Equal(FenixA320Definition.DcduSpringBackMs, timer.Interval);

            form.PressBrightness(2);

            Assert.True(timer.Enabled, "the press did not start the wait for the read-back");
            Assert.Empty(speech.All);
        }
    }

    [Fact]
    public async Task The_value_is_read_back_and_said_once_the_switch_is_back()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            var reads = new List<string>();
            form.ReadFresh = (key, _) =>
            {
                reads.Add(key);
                return Task.FromResult<double?>(Level(310));
            };

            form.PressBrightness(2);
            Assert.Empty(reads);

            await form.ReadBackDueAsync();

            // The spoken read-back is the press's confirmation ([CORE-7]): the value, as the interrupting call a refusal uses.
            Assert.Equal(new[] { FenixChannelDcduBrightness.LevelKey }, reads);
            Assert.Equal(new[] { "Brightness 60 percent" }, speech.Interrupts);
            Assert.Equal(speech.Interrupts, speech.All);
        }
    }

    [Fact]
    public async Task A_read_that_fails_or_times_out_says_the_brightness_is_not_available()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            // ReadFreshAsync gives null on a timeout, a disconnect or a key it cannot deliver; a throw is made null too.
            form.ReadFresh = (_, _) => Task.FromResult<double?>(null);
            form.PressBrightness(0);
            await form.ReadBackDueAsync();

            form.ReadFresh = (_, _) => throw new InvalidOperationException("the sim went away");
            form.PressBrightness(0);
            await form.ReadBackDueAsync();

            form.ReadFresh = (_, _) => Task.FromException<double?>(new TimeoutException());
            form.PressBrightness(0);
            await form.ReadBackDueAsync();

            Assert.Equal(Enumerable.Repeat("Brightness not available", 3), speech.Interrupts);
        }
    }

    [Fact]
    public async Task Without_a_sim_the_default_read_says_the_brightness_is_not_available()
    {
        // The read goes through the manager's ReadFreshAsync, which gives null when it is not connected.
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            form.PressBrightness(2);
            await form.ReadBackDueAsync();

            Assert.Equal(new[] { "Brightness not available" }, speech.Interrupts);
        }
    }

    [Fact]
    public async Task Only_the_last_of_quick_presses_is_read_back()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            var firstRead = new TaskCompletionSource<double?>();
            form.ReadFresh = (_, _) => firstRead.Task;
            form.PressBrightness(2);
            Task first = form.ReadBackDueAsync();

            // A second press while the first read is out: its wait restarts, and the first read's answer is not the last press's.
            form.PressBrightness(2);
            firstRead.SetResult(Level(110));
            await first;
            Assert.Empty(speech.All);

            form.ReadFresh = (_, _) => Task.FromResult<double?>(Level(160));
            await form.ReadBackDueAsync();
            Assert.Equal(new[] { "Brightness 30 percent" }, speech.Interrupts);
        }
    }

    [Fact]
    public async Task Nothing_is_said_after_the_window_is_disposed()
    {
        FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            var read = new TaskCompletionSource<double?>();
            form.ReadFresh = (_, _) => read.Task;
            form.PressBrightness(2);
            Task pending = form.ReadBackDueAsync();

            // The window is closed and disposed while the read is out: its answer comes to nobody.
            form.Dispose();
            read.SetResult(Level(310));
            await pending;
            Assert.Empty(speech.All);

            // A read-back that is only due after the dispose says nothing either.
            form.ReadFresh = (_, _) => Task.FromResult<double?>(Level(310));
            await form.ReadBackDueAsync();
            Assert.Empty(speech.All);
        }
    }

    [Fact]
    public void A_key_with_a_button_focused_is_the_windows_as_it_is_with_the_list()
    {
        // The keys are the form's (ProcessCmdKey), so which control has focus decides nothing: a soft key on a display that
        // cannot be read says so with Bright focused as it does with the list, and a key that is none of the nine is left.
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out SpeechCapture speech);
        using (monitor)
        {
            form.ActiveControl = form.Controls.OfType<Button>().Single(button => button.Text == "Bright");

            Assert.True(Press(form, Keys.Control | Keys.D1));
            Assert.Equal(new[] { FenixChannelDcduKeys.NotAvailableMessage }, speech.Interrupts);
            Assert.True(Press(form, Keys.PageDown));
            Assert.False(Press(form, Keys.Down));
            Assert.Single(speech.All);
        }
    }

    // ---- A read-back that goes wrong -----------------------------------------------------------

    // An announcer whose first interrupting call throws, as an announcer can after the read-back's await.
    private sealed class ThrowOnceAnnouncer : ScreenReaderAnnouncer
    {
        public ThrowOnceAnnouncer() : base(IntPtr.Zero) { }

        public int Attempts { get; private set; }

        public List<string> Spoken { get; } = new();

        public override void AnnounceImmediate(string message)
        {
            Attempts++;
            if (Attempts == 1) throw new InvalidOperationException("the announcer went away");

            Spoken.Add(message);
        }
    }

    [Fact]
    public async Task A_throw_after_the_read_is_caught_and_leaves_the_window_working()
    {
        // The timer starts the read-back and forgets it, so a throw after its await would be an unobserved task exception and
        // nothing else: it is caught inside, logged, and the window reads back the next press as before.
        var announcer = new ThrowOnceAnnouncer();
        using FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using var form = new FenixChannelDcduForm(monitor, new FenixA320Definition(), new SimConnectManager(IntPtr.Zero), announcer);
        form.ReadFresh = (_, _) => Task.FromResult<double?>(Level(310));

        form.PressBrightness(2);
        await form.ReadBackDueAsync();
        Assert.Equal(1, announcer.Attempts);
        Assert.Empty(announcer.Spoken);

        form.PressBrightness(2);
        await form.ReadBackDueAsync();
        Assert.Equal(new[] { "Brightness 60 percent" }, announcer.Spoken);
    }

    [Fact]
    public void The_window_reads_the_brightness_only_for_a_read_back_and_logs_what_the_read_back_cannot_say()
    {
        string code = CodeOf(FormPath);

        // The read-back's read goes through the one seam, which is the manager's ReadFreshAsync, and its answer is the speech.
        Assert.Contains("simConnect.ReadFreshAsync(", code);
        Assert.Contains("AnnounceImmediate(FenixChannelDcduBrightness.Spoken(", code);

        // A DCDU at brightness 0 still takes a message (measured 2026-11-03): nothing reads the level for the rows.
        Assert.DoesNotContain("_levelTimer", code);
        Assert.DoesNotContain("ReadLevelAsync", code);
        Assert.DoesNotContain("BrightnessLevel", code);
        Assert.DoesNotContain("DarkRow", code);

        // The read-back is started and forgotten by the timer, so a throw in it is caught and logged, not left unobserved.
        string readBack = BodyOf(FormPath, "ReadBackDueAsync");
        Assert.Contains("catch (Exception", readBack);
        Assert.Contains("Log.Debug(", readBack);
    }

    [Fact]
    public void Opening_the_window_asks_no_verdict()
    {
        // The window shows its own status row while the DCDU is not trusted, so the opening asks for no verdict.
        string body = BodyOf(DefinitionPath, "OpenDcduWindow");

        Assert.Contains("ShowChannelWindow(", body);
        Assert.Contains("FenixChannelDcdu.DisplayName", body);
        Assert.Contains("FenixChannelDcduForm(", body);
        Assert.DoesNotContain("Verdict", body);
        Assert.DoesNotContain("Snapshot(", body);
    }

    // ---- The way in -----------------------------------------------------------------------------

    [Fact]
    public void The_dcdu_entry_is_listed_once()
    {
        // The Fenix windows list speaks "Name. Description": one DCDU entry, named for the display as the others are.
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "DCDU");

        Assert.Equal("Datalink messages on the DCDU screen, with its soft keys, print and brightness", entry.Description);
        Assert.NotNull(entry.Open);
    }

    [Fact]
    public void The_list_entry_refuses_in_the_same_words_as_the_key()
    {
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "DCDU");
        var sim = new SimConnectManager(IntPtr.Zero);

        // An airframe that says it has no DCDU: the refusal, and no window. Only the definition and the announcer are used.
        var none = new FenixA320Definition();
        Assert.True(none.TryApplyConfigValue(FenixConfigVars.AircraftType, 3));
        none.TryApplyConfigValue(FenixConfigVars.Dcdu, 0);
        var refused = new SpeechCapture();
        entry.Open(new FenixWindowContext(none, null!, sim, refused, null!));
        Assert.Equal(new[] { FenixA320Definition.NoDcduMessage }, refused.Interrupts);
        Assert.Equal(refused.Interrupts, refused.All);

        // A Fenix whose DCDU field is not read yet is not refused: it goes on to the window, which without a monitor says so.
        var unread = new SpeechCapture();
        entry.Open(new FenixWindowContext(new FenixA320Definition(), null!, sim, unread, null!));
        Assert.Equal(new[] { "DCDU display not available" }, unread.Interrupts);
    }

    [Fact]
    public void Ctrl_Shift_D_on_a_fenix_without_a_dcdu_is_answered_here()
    {
        // The key is always answered by the Fenix, so MainForm's "only available on the A32NX, the Headwind A330 and the
        // Fenix" sentence never reaches its pilot. The refusal touches neither the sim, the form nor the hotkey manager.
        var definition = new FenixA320Definition();
        Assert.True(definition.TryApplyConfigValue(FenixConfigVars.AircraftType, 3));
        definition.TryApplyConfigValue(FenixConfigVars.Dcdu, 0);
        Assert.False(definition.Variant.HasDcdu);
        var speech = new SpeechCapture();

        bool handled = definition.HandleHotkeyAction(HotkeyAction.ShowDCDU, null!, speech, null!, null!);

        Assert.True(handled);
        Assert.Equal(new[] { "This Fenix has no DCDU." }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, speech.All);
    }

    [Fact]
    public void The_key_refuses_before_it_leaves_input_mode()
    {
        // The key is always answered (handled), and a refusal comes first and ends it: input mode stays on, as after
        // MainForm's own refusals. Only a window about to open leaves both hotkey modes, so that the chords and the page
        // keys reach it (ShowA32NXDcduDialog does the same), and then opens it.
        string body = BodyOf(DefinitionPath, "ChannelDcduKey");

        string[] inOrder =
        [
            "handled = true", "TryRefuseNoDcdu(", "ExitInputHotkeyMode()", "ExitOutputHotkeyMode()", "OpenDcduWindow("
        ];
        int at = -1;
        foreach (string step in inOrder)
        {
            int found = body.IndexOf(step, at + 1, StringComparison.Ordinal);
            Assert.True(found > at, $"ChannelDcduKey does not have `{step}` after `{(at < 0 ? "its start" : "the step before it")}`");
            at = found;
        }

        Assert.Matches(@"if\s*\(\s*TryRefuseNoDcdu\(Variant,\s*ctx\.Announcer\)\s*\)\s*return;", body);
        Assert.Contains("OpenDcduWindow(ctx.SimConnect, ctx.Announcer)", body);
    }

    [Fact]
    public void The_sampler_reads_one_snapshot_checks_its_row_and_queues_the_call()
    {
        string body = BodyOf(DefinitionPath, "SampleChannelDcdu");

        // One snapshot of the DCDU, from which the verdict, the screen and the connection are all taken.
        Assert.Single(Regex.Matches(body, @"Snapshot\("));
        Assert.Contains("Snapshot(Services.FenixChannelDisplay.Dcdu)", body);
        Assert.Contains("Services.FenixChannelDcdu.Read(s)", body);
        Assert.Contains("s.Connection", body);

        // A problem on a display the monitor trusts is the decoder's, and is reported under the window's name.
        Assert.Contains("s.Verdict.Trusted", body);
        Assert.Contains("ReportFeatureProblem(Services.FenixChannelDcdu.DisplayName", body);

        // The row is asked every tick, and the airframe's own answer is added to it.
        Assert.Contains("IsMuted(Services.FenixChannelAnnouncementRows.DcduKey)", body);
        Assert.Contains("DcduCallMuted(Variant,", body);

        // A call is a background change the pilot did not trigger ([CORE-7]): queued, never interrupting.
        Assert.Contains("ctx.Announcer.Announce(", body);
        Assert.DoesNotContain("AnnounceImmediate", body);
    }

    [Fact]
    public void The_sampler_gives_the_read_back_the_same_snapshot_and_the_same_mute_as_the_announcer_and_queues_what_it_says()
    {
        string body = BodyOf(DefinitionPath, "SampleChannelDcdu");

        // The mute is worked out once, from the Ctrl+M row and the airframe, and both the read-back and the announcer get it.
        Assert.Matches(@"bool\s+muted\s*=\s*DcduCallMuted\(\s*Variant\s*,\s*rowMuted\s*\)\s*;", body);
        Assert.Single(Regex.Matches(body, @"DcduCallMuted\("));

        // Every tick the read-back (made by the sampler if no press made it) is given the screen of the tick's one snapshot,
        // the mute and the clock, and what it says is queued: a background change the pilot did not trigger ([CORE-7]).
        Assert.Matches(
            @"\(\s*_dcduReadback\s*\?\?=\s*new\s+Services\.FenixChannelDcduReadback\(\)\s*\)\s*\.\s*Sample\(\s*section\s*,\s*muted\s*,\s*ctx\.NowMs\s*\)",
            body);
        Assert.Matches(@"Sample\(\s*section\s*,\s*s\.Connection\s*,\s*muted\s*,\s*ctx\.NowMs\s*\)", body);
        Assert.Matches(@"Announce\(\s*readBack\s*\)", body);
        Assert.DoesNotContain("AnnounceImmediate", body);

        // And still the one snapshot a tick.
        Assert.Single(Regex.Matches(body, @"Snapshot\("));
    }

    [Fact]
    public void The_call_stays_silent_on_an_airframe_with_no_dcdu()
    {
        // The channel reads the DCDU trusted on an airframe that has none, so the variant mutes the call as well as the
        // row. An unread field mutes nothing ([FNX-11]). The announcer keeps tracking while muted, so nothing replays.
        FenixVariant none = FenixVariant.Unknown with { HasDcdu = false };
        FenixVariant fitted = FenixVariant.Unknown with { HasDcdu = true };

        Assert.True(FenixA320Definition.DcduCallMuted(none, rowMuted: false));
        Assert.False(FenixA320Definition.DcduCallMuted(fitted, rowMuted: false));
        Assert.False(FenixA320Definition.DcduCallMuted(FenixVariant.Unknown, rowMuted: false));

        // An unticked row mutes it whatever the airframe says.
        Assert.True(FenixA320Definition.DcduCallMuted(none, rowMuted: true));
        Assert.True(FenixA320Definition.DcduCallMuted(fitted, rowMuted: true));
        Assert.True(FenixA320Definition.DcduCallMuted(FenixVariant.Unknown, rowMuted: true));
    }
}
