// Characterization tests for the Fenix DCDU window (Forms/Fenix/FenixChannelDcduForm.cs) and the definition's DCDU calls
// (Aircraft/FenixA320Definition.ChannelDcdu.cs, and the Variants.cs refusal for an airframe with no DCDU). Nothing here
// reaches a pilot yet: the window has no key, no list entry and no sampler.
//
// The contract: the window is the base's list of rows ("DCDU display") with a keyboard on it. A key of the nine
// (FenixChannelDcduKeys.Map, the shared alternate soft keys setting picking the chords) is judged at the press on a
// snapshot taken then, never on the last refresh's, and a refusal is the only thing it ever says: the result of a press is
// heard in the refreshed list, never the key's name ([CORE-7]). A press goes through the definition's own handler for the
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

    [Fact]
    public void Only_a_page_or_message_key_notes_the_navigation()
    {
        // The pilot's own paging and stepping are never announced as news: the page and message keys tell the announcer
        // first, and the soft keys and print do not.
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var definition = new FenixA320Definition();

        foreach (FenixDcduKey key in Enum.GetValues<FenixDcduKey>())
        {
            AnnouncerField.SetValue(definition, null);

            definition.PressDcdu(key, sim, speech);

            Assert.Equal(FenixChannelDcduKeys.IsNavigation(key), AnnouncerField.GetValue(definition) is not null);
        }

        Assert.Empty(speech.All);
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
        // The snapshot is taken at the press, then the key is decided on it: a message that arrived after the last refresh
        // relabels a slot, and a label read before the press would press the wrong answer.
        string body = BodyOf(FormPath, "HandleWindowKey");
        int snapshot = body.IndexOf("Snapshot(FenixChannelDisplay.Dcdu)", StringComparison.Ordinal);
        int decide = body.IndexOf("FenixChannelDcduKeys.Decide(", StringComparison.Ordinal);

        Assert.True(snapshot >= 0, "HandleWindowKey never takes a snapshot of the DCDU");
        Assert.True(decide >= 0, "HandleWindowKey never decides a key");
        Assert.True(snapshot < decide, "HandleWindowKey decides a key before it has taken its snapshot");

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
    public void A_brightness_pick_steps_once_and_the_return_to_mid_writes_nothing()
    {
        string commit = BodyOf(FormPath, "OnBrightnessCommitted");
        int guard = commit.IndexOf("if (_returning) return;", StringComparison.Ordinal);
        int step = commit.IndexOf("SetDcduBrightness(", StringComparison.Ordinal);
        Assert.True(guard >= 0 && step >= 0 && guard < step, "a pick is not guarded before it steps the switch");

        // The timer shows Mid again DcduSpringBackMs after a pick, and that return never reaches the switch.
        Assert.Contains("DcduSpringBackMs", CodeOf(FormPath));
        string back = BodyOf(FormPath, "ReturnToMid");
        Assert.Contains("_returning = true", back);
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
}
