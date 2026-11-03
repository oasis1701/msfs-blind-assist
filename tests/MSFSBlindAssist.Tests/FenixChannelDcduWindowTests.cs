// Characterization tests for the Fenix DCDU window (Forms/Fenix/FenixChannelDcduForm.cs) and the definition's DCDU calls
// (Aircraft/FenixA320Definition.ChannelDcdu.cs, and the Variants.cs refusal for an airframe with no DCDU). The window is
// reached by Ctrl+Shift+D (the key seam) and by the "DCDU" entry of the Fenix windows list, and a new message is called by
// the announcer host's sampler; the way in, the sampler and the mute are pinned at the end of this file.
//
// The contract: the window is the base's list of rows ("DCDU display") with a keyboard on it. A key of the nine
// (FenixChannelDcduKeys.Map, the shared alternate soft keys setting picking the chords) is judged at the press on a
// snapshot taken then, never on the last refresh's, and a refusal is the only thing the window ever says: the result of a
// press is heard in the refreshed list, never the key's name ([CORE-7]); a result that is not on the row under the cursor
// (the status, NO MORE MSG) is read back once by the sampler (FenixChannelDcduReadback), which every press tells first. A
// press goes through the definition's own handler for the
// panel's S_DCDU1_* button, which presses and releases ([A320-3]) and carries the variant's refusal in front of it; the
// window and the definition's DCDU calls never write a variable, run a calculator string or open the channel themselves
// ([FNX-21]). A key held down presses once: the base asks the window about every auto-repeat too, and an answer key is
// pressed once for WILCO and again for SEND, so a held Ctrl+1 must not answer and send. A brightness combo after the list
// steps the switch one position for a pick of Dim or Bright, and shows Mid again when the switch has sprung back, with
// nothing written for that return.
//
// What is built here is never shown (the DCDU window needs a sim and a screen); the SimConnect manager is never connected,
// so a press the window sends writes nothing. The rest, the key behaviour in the sim, is checked there ([CORE-5]).

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
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
    public void The_window_is_the_dcdu_list_with_a_brightness_combo_after_it()
    {
        using FenixChannelDcduForm form = NewWindow(out FenixChannelMonitor monitor, out _, out _);
        using (monitor)
        {
            Assert.Equal("Fenix DCDU", form.Text);

            DisplayListBox list = form.Controls.OfType<DisplayListBox>().Single();
            Assert.Equal("DCDU display", list.AccessibleName);

            // The variable's own items, on the resting middle, in a list the pilot picks from and cannot type into.
            ComboBox combo = form.Controls.OfType<ComboBox>().Single();
            Assert.Equal("DCDU Brightness", combo.AccessibleName);
            Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
            Assert.Equal(new[] { "Dim", "Mid", "Bright" }, combo.Items.Cast<string>());
            Assert.Equal("Mid", combo.SelectedItem);

            // After the list, in the layout and in the tab order.
            Assert.True(list.TabIndex < combo.TabIndex, "the combo is not after the list in the tab order");
            Assert.Equal(DockStyle.Fill, list.Dock);
            Assert.Equal(DockStyle.Bottom, combo.Dock);
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
    public void Only_a_page_or_message_key_notes_the_navigation()
    {
        // The pilot's own paging and stepping are never announced as news: the page and message keys tell the announcer
        // first, so that a message that comes onto the screen within the quiet is silent, and the soft keys and print do not,
        // so that a message that comes after one of those is called.
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

            if (FenixChannelDcduKeys.IsNavigation(key))
                Assert.True(call is null, $"{key} is the pilot's own reading, yet its screen was called: {call}");
            else
                Assert.True(call is not null, $"{key} is no navigation, yet the message after it was not called");
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

            Assert.Null(readback.Sample(InfoScreen(""), 0));
            Assert.True(readback.Sample(InfoScreen("NO MORE MSG"), 200) == "NO MORE MSG", $"{key} did not note the read-back");
        }

        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_first_press_makes_the_read_back()
    {
        var definition = new FenixA320Definition();
        Assert.Null(ReadbackField.GetValue(definition));

        definition.PressDcdu(FenixDcduKey.Print, new SimConnectManager(IntPtr.Zero), new SpeechCapture());

        Assert.NotNull(ReadbackField.GetValue(definition));
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

    [Fact]
    public void A_brightness_pick_steps_once_and_the_return_to_mid_writes_nothing()
    {
        // The combo is told apart from the switch's return by the event it listens to: SelectionChangeCommitted is raised
        // for the pilot's pick only, never for the timer assigning SelectedIndex, so the return needs no guard of its own
        // and the form has no SelectedIndexChanged handler, which would fire for both.
        string code = CodeOf(FormPath);
        Assert.Contains("_brightness.SelectionChangeCommitted += OnBrightnessCommitted", code);
        Assert.DoesNotContain("SelectedIndexChanged", code);
        Assert.DoesNotContain("_returning", code);

        string commit = BodyOf(FormPath, "OnBrightnessCommitted");
        Assert.Contains("SetDcduBrightness(", commit);

        // The timer shows Mid again DcduSpringBackMs after a pick, and that return only assigns the selected index: it never
        // reaches the switch.
        Assert.Contains("DcduSpringBackMs", code);
        string back = BodyOf(FormPath, "ReturnToMid");
        Assert.Contains("_brightness.SelectedIndex = MidIndex;", back);
        foreach (string write in new[] { "SetDcduBrightness(", "PressDcdu(", "HandleUIVariableSet(" })
            Assert.False(back.Contains(write, StringComparison.Ordinal), $"the return to Mid calls {write}");
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
        // The key is always answered by the Fenix, so MainForm's "only available on the A32NX and the Headwind A330"
        // sentence never reaches its pilot. The refusal touches neither the sim, the form nor the hotkey manager.
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
    public void The_sampler_speaks_the_read_back_of_the_same_snapshot_queued_and_not_muted()
    {
        string body = BodyOf(DefinitionPath, "SampleChannelDcdu");

        // Every tick the read-back is given the screen of the tick's one snapshot and its clock, and what it says is queued.
        // It is the pilot's own press, so neither the Ctrl+M row nor the airframe's variant mutes it.
        Match sample = Regex.Match(body, @"[^\r\n;]*_dcduReadback\s*\?\s*\.\s*Sample\(\s*section\s*,\s*ctx\.NowMs\s*\)[^;]*;");
        Assert.True(sample.Success, "the sampler never gives the read-back the tick's screen and clock");
        Assert.DoesNotContain("uted", sample.Value);
        Assert.Matches(@"Announce\(\s*readBack\s*\)", body);

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
