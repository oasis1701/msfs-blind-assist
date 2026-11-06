// Characterization tests for the ISIS window's buttons: the eight buttons after the list that press the ISIS panel's
// controls (FenixChannelIsisForm.IsisKeys, and the factory of FenixA320Definition.ChannelIsis.cs), added at the owner's word
// after the flight of 2026-11-05 (spec row 32 changed). The baro knob is among them at the owner's word the same day: Ctrl+B
// sets the ISIS baro too, but on the bugs page the knob steps the selected bug's value, the only way to set one. The window
// is built, never shown. A control on a form that was never shown is not Visible, so Button.PerformClick does nothing for
// it: a test raises the click through the button's protected OnClick (reflection), which is what the pilot's click, or
// Space or Enter on the focused button, raises. Every key and label is written out here, never read from the app, so that
// a wrong one in the app fails a test.
//
// The contract: after the list, in the ISIS panel's order, one button for each of the panel's controls: BUGS
// (S_MIP_ISFD_BUGS), LS (S_MIP_ISFD_LS), Plus (S_MIP_ISFD_PLUS), Minus (S_MIP_ISFD_MINUS), RST (S_MIP_ISFD_RST), Baro Inc
// (E_MIP_ISFD_BARO_INC), Baro Dec (E_MIP_ISFD_BARO_DEC) and Baro Push (S_MIP_ISFD_BARO_BUTTON). Each button's text and
// accessible name is its label. A press calls the window's press delegate once with the key's variable name and nothing
// else; a refresh, F5 and opening the window never press. The definition's factory wires the delegate to PressIsisKey, the
// panel's own code (RST's 3 s hold and its "pressed", the baro push's "pressed", the knob's counter step, a full
// press-release for the buttons [FNX-5], [FNX-6], [A320-3]), and the window says nothing on a press ([CORE-7]). Alt+I and
// the windows list's entry both make the window with that one factory.

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelIsisKeysTests
{
    private static readonly (string Label, string Key)[] Expected =
    [
        ("BUGS", "S_MIP_ISFD_BUGS"),
        ("LS", "S_MIP_ISFD_LS"),
        ("Plus", "S_MIP_ISFD_PLUS"),
        ("Minus", "S_MIP_ISFD_MINUS"),
        ("RST", "S_MIP_ISFD_RST"),
        ("Baro Inc", "E_MIP_ISFD_BARO_INC"),
        ("Baro Dec", "E_MIP_ISFD_BARO_DEC"),
        ("Baro Push", "S_MIP_ISFD_BARO_BUTTON")
    ];

    private static FenixChannelIsisForm NewForm(List<string> pressed)
        => new(MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines()), () => FenixVariant.Unknown, pressed.Add);

    private static void Click(Button button)
        => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });

    private static IEnumerable<T> All<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (T inner in All<T>(child)) yield return inner;
        }
    }

    private static Control[] InTabOrder(Control parent) => parent.Controls.Cast<Control>().OrderBy(c => c.TabIndex).ToArray();

    // ---- The table and the panel ----------------------------------------------------------------

    [Fact]
    public void The_table_is_the_eight_keys_in_order()
    {
        Assert.Equal(Expected, FenixChannelIsisForm.IsisKeys.ToArray());
    }

    [Fact]
    public void The_window_carries_every_control_of_the_isis_panel_in_its_order()
    {
        var definition = new FenixA320Definition();

        Assert.Equal(Expected.Select(k => k.Key), definition.GetPanelControls()["ISIS"]);
        Assert.All(Expected, k => Assert.True(definition.GetVariables().ContainsKey(k.Key), $"{k.Key} is not a Fenix variable"));
    }

    // ---- The window -----------------------------------------------------------------------------

    [Fact]
    public void The_window_shows_the_eight_buttons_after_the_list_in_tab_order()
    {
        using FenixChannelIsisForm form = NewForm([]);

        Control[] top = InTabOrder(form);
        Assert.Equal(2, top.Length);
        Assert.IsType<DisplayListBox>(top[0]);
        FlowLayoutPanel panel = Assert.IsType<FlowLayoutPanel>(top[1]);

        Button[] buttons = panel.Controls.Cast<Button>().ToArray();
        Assert.Equal(Expected.Select(k => k.Label), buttons.Select(b => b.Text));
        Assert.Equal(Expected.Select(k => k.Label), buttons.Select(b => b.AccessibleName));
        Assert.Equal(Enumerable.Range(0, buttons.Length), buttons.Select(b => b.TabIndex));
        Assert.All(buttons, button => Assert.True(button.TabStop && button.Enabled));
        Assert.Empty(All<ComboBox>(form));
    }

    [Fact]
    public void Clicking_a_button_presses_its_key_once_and_nothing_else()
    {
        var pressed = new List<string>();
        using FenixChannelIsisForm form = NewForm(pressed);
        Button[] buttons = All<Button>(form).ToArray();
        Assert.Equal(8, buttons.Length);

        for (int i = 0; i < buttons.Length; i++)
        {
            pressed.Clear();

            Click(buttons[i]);

            Assert.Equal(new[] { Expected[i].Key }, pressed);
        }
    }

    [Fact]
    public void Opening_refreshing_and_f5_never_press_a_key()
    {
        var pressed = new List<string>();
        using FenixChannelIsisForm form = NewForm(pressed);

        form.StartLive();
        form.RefreshNow();
        form.RefreshNow();

        Assert.Empty(pressed);
    }

    [Fact]
    public void The_window_needs_a_press_to_call()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());

        Assert.Throws<ArgumentNullException>(() => new FenixChannelIsisForm(neverStarted, () => FenixVariant.Unknown, null!));
    }

    [Fact]
    public void The_window_holds_no_announcer_and_no_simconnect()
    {
        ConstructorInfo constructor = Assert.Single(typeof(FenixChannelIsisForm).GetConstructors());

        Assert.Equal(
            new[] { typeof(FenixChannelMonitor), typeof(Func<FenixVariant>), typeof(Action<string>) },
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    // ---- The definition's side ------------------------------------------------------------------

    [Fact]
    public void The_definition_makes_the_window_with_the_keys()
    {
        using var monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using FenixChannelIsisForm form = new FenixA320Definition().CreateChannelIsisForm(monitor, null!, null!);

        Assert.Equal(8, All<Button>(form).Count());
    }

    [Fact]
    public void A_name_the_definition_has_no_variable_for_presses_nothing()
    {
        // The sim is null: a write would throw. An unknown name returns before touching it.
        Assert.False(new FenixA320Definition().PressIsisKey("S_NOT_AN_ISIS_KEY", null!, null!));
    }

    [Fact]
    public void The_factory_wires_the_window_to_PressIsisKey_only()
    {
        string body = BodyOf("internal Forms.Fenix.FenixChannelIsisForm CreateChannelIsisForm(");

        Assert.Contains("PressIsisKey(", body);
        Assert.DoesNotContain("SetLVar", body);
        Assert.DoesNotContain("HandleUIVariableSet", body);
        Assert.DoesNotContain("ExecuteButtonTransition", body);
    }

    [Fact]
    public void Alt_i_and_the_windows_list_make_the_window_with_the_one_factory()
    {
        string key = StrippedSource("Aircraft", "FenixA320Definition.ChannelIsis.cs");
        Assert.Contains(
            WithoutWhitespace("monitor => CreateChannelIsisForm(monitor, ctx.SimConnect, ctx.Announcer)"),
            WithoutWhitespace(key));

        string entry = StrippedSource("Forms", "Fenix", "FenixWindowRegistry.ChannelIsis.cs");
        Assert.Contains(
            WithoutWhitespace("monitor => ctx.Definition.CreateChannelIsisForm(monitor, ctx.SimConnect, ctx.Announcer)"),
            WithoutWhitespace(entry));
        Assert.DoesNotContain("new FenixChannelIsisForm(", entry);
    }

    // ---- Reading the source ---------------------------------------------------------------------

    private static string WithoutWhitespace(string text) => Regex.Replace(text, @"\s+", "");

    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string StrippedSource(params string[] path)
        => Comment.Replace(
            File.ReadAllText(Path.Combine([ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", .. path])), "");

    private static string BodyOf(string declaration)
    {
        string code = StrippedSource("Aircraft", "FenixA320Definition.ChannelIsis.cs");
        int start = code.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"FenixA320Definition.ChannelIsis.cs does not declare {declaration}");

        int open = code.IndexOf('{', start);
        int arrow = code.IndexOf("=>", start, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open)) return code.Substring(start, code.IndexOf(';', arrow) - start + 1);

        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(start, i - start + 1);
        }

        throw new InvalidOperationException("the block is not closed");
    }
}
