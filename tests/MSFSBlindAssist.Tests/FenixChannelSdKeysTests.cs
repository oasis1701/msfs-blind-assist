// Characterization tests for the SD window's page keys: the thirteen buttons after the list that press the ECAM control
// panel's own page keys (FenixChannelSdForm.PageKeys, and the press method of FenixA320Definition.ChannelSd.cs). The
// window is built, never shown. A control on a form that was never shown is not Visible, so Button.PerformClick does
// nothing for it: a test raises the click through the button's protected OnClick (reflection), which is what the
// pilot's click, or Space or Enter on the focused button, raises. Every key and label is written out here, never read
// from the app, so that a wrong one in the app fails a test.
//
// The contract: after the list, in this order, one button for each of the panel's page keys: ENG (S_ECAM_ENGINE), BLEED
// (S_ECAM_BLEED), PRESS (S_ECAM_CAB_PRESS), ELEC (S_ECAM_ELEC), HYD (S_ECAM_HYD), FUEL (S_ECAM_FUEL), APU (S_ECAM_APU),
// COND (S_ECAM_COND), DOOR (S_ECAM_DOOR), WHEEL (S_ECAM_WHEEL), F/CTL (S_ECAM_FCTL), ALL (S_ECAM_ALL) and STS
// (S_ECAM_STATUS); the panel's T.O CONFIG, CLR, RCL and EMER CANC keys act on the E/WD and are not here. Each button's
// text and accessible name is its label; there is no combo box, which would press a key at every page the reader arrows
// past. A press calls the window's press delegate once with the key's variable name and nothing else; a refresh, F5 and
// opening the window never press. The definition's press method is the panel's own code for that key
// (HandleUIVariableSet with the value 1, whose S_ECAM_* cases run the full press-release), and the window itself says
// nothing on a press: it has no announcer.

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdKeysTests
{
    // The panel's page keys, in the order of its ECAM panel.
    private static readonly (string Label, string Key)[] Expected =
    [
        ("ENG", "S_ECAM_ENGINE"),
        ("BLEED", "S_ECAM_BLEED"),
        ("PRESS", "S_ECAM_CAB_PRESS"),
        ("ELEC", "S_ECAM_ELEC"),
        ("HYD", "S_ECAM_HYD"),
        ("FUEL", "S_ECAM_FUEL"),
        ("APU", "S_ECAM_APU"),
        ("COND", "S_ECAM_COND"),
        ("DOOR", "S_ECAM_DOOR"),
        ("WHEEL", "S_ECAM_WHEEL"),
        ("F/CTL", "S_ECAM_FCTL"),
        ("ALL", "S_ECAM_ALL"),
        ("STS", "S_ECAM_STATUS")
    ];

    // A window on an idle monitor (never started: the keys do not read the channel), pressing into a list.
    private static FenixChannelSdForm NewForm(List<string> pressed)
        => new(MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines()), () => SdPush.A320, pressed.Add);

    // What the pilot's click raises. PerformClick needs a visible button, and a window that was never shown has none.
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

    // The window's top-level controls in tab order: the list, then the buttons' container.
    private static Control[] InTabOrder(Control parent) => parent.Controls.Cast<Control>().OrderBy(c => c.TabIndex).ToArray();

    // ---- The table ------------------------------------------------------------------------------

    [Fact]
    public void The_table_is_the_panels_thirteen_page_keys_in_order()
    {
        Assert.Equal(Expected, FenixChannelSdForm.PageKeys.ToArray());
    }

    [Fact]
    public void Every_key_is_a_variable_and_a_control_of_the_ecam_panel_in_its_order()
    {
        // So that the window and the panel press the same thing, and the window follows the panel's order.
        var definition = new FenixA320Definition();
        var variables = definition.GetVariables();
        List<string> ecam = definition.GetPanelControls()["ECAM"];

        int previous = -1;
        foreach ((string label, string key) in FenixChannelSdForm.PageKeys)
        {
            Assert.True(variables.ContainsKey(key), $"{label}: {key} is not a Fenix variable");
            int at = ecam.IndexOf(key);
            Assert.True(at >= 0, $"{label}: {key} is not in the ECAM panel");
            Assert.True(at > previous, $"{label}: {key} is out of the ECAM panel's order");
            previous = at;
        }
    }

    [Fact]
    public void The_keys_that_act_on_the_ewd_are_not_in_the_window()
    {
        // T.O CONFIG, CLR, RCL and EMER CANC act on the E/WD, whose window is plan 04's.
        string[] keys = FenixChannelSdForm.PageKeys.Select(k => k.Key).ToArray();

        Assert.DoesNotContain("S_ECAM_TO", keys);
        Assert.DoesNotContain("S_ECAM_CLR_LEFT", keys);
        Assert.DoesNotContain("S_ECAM_CLR_RIGHT", keys);
        Assert.DoesNotContain("S_ECAM_RCL", keys);
        Assert.DoesNotContain("S_ECAM_EMER_CANCEL", keys);
    }

    // ---- The window -----------------------------------------------------------------------------

    [Fact]
    public void The_window_shows_the_thirteen_buttons_after_the_list_in_tab_order()
    {
        using FenixChannelSdForm form = NewForm([]);

        Control[] top = InTabOrder(form);
        Assert.Equal(2, top.Length);
        Assert.IsType<DisplayListBox>(top[0]);
        FlowLayoutPanel panel = Assert.IsType<FlowLayoutPanel>(top[1]);
        Assert.Equal(DockStyle.Bottom, panel.Dock);

        // The panel lays its buttons out in the order they were added, and Tab visits them in the same order.
        Button[] buttons = panel.Controls.Cast<Button>().ToArray();
        Assert.Equal(Expected.Select(k => k.Label), buttons.Select(b => b.Text));
        Assert.Equal(Expected.Select(k => k.Label), buttons.Select(b => b.AccessibleName));
        Assert.Equal(Enumerable.Range(0, buttons.Length), buttons.Select(b => b.TabIndex));
        Assert.All(buttons, button => Assert.True(button.TabStop && button.Enabled));
    }

    [Fact]
    public void The_window_has_buttons_and_no_combo_box()
    {
        // A combo changes its selection, so it would press a key at every page the reader arrows past.
        using FenixChannelSdForm form = NewForm([]);

        Assert.Equal(13, All<Button>(form).Count());
        Assert.Empty(All<ComboBox>(form));
    }

    [Fact]
    public void Clicking_a_button_presses_its_key_once_and_nothing_else()
    {
        var pressed = new List<string>();
        using FenixChannelSdForm form = NewForm(pressed);
        Button[] buttons = All<Button>(form).ToArray();

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
        using FenixChannelSdForm form = NewForm(pressed);

        form.StartLive();
        form.RefreshNow();
        form.RefreshNow();

        Assert.Empty(pressed);
    }

    [Fact]
    public void The_window_holds_no_announcer_and_no_simconnect()
    {
        // CORE-7: the panel's code and the key's light are the one confirmation. The window is built from a monitor, the
        // variant and the press, and holds no announcer or SimConnect to speak or write with.
        ConstructorInfo constructor = Assert.Single(typeof(FenixChannelSdForm).GetConstructors());

        Assert.Equal(
            new[] { typeof(FenixChannelMonitor), typeof(Func<FenixVariant>), typeof(Action<string>) },
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    // ---- The definition's side ------------------------------------------------------------------

    [Fact]
    public void The_definition_makes_the_window_with_the_keys()
    {
        using var monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using FenixChannelSdForm form = new FenixA320Definition().CreateChannelSdForm(monitor, null!, null!);

        Assert.Equal(13, All<Button>(form).Count());
    }

    [Fact]
    public void An_unknown_key_is_not_pressed()
    {
        // The press method knows the definition's variables only; a name it has none for is refused, never thrown.
        Assert.False(new FenixA320Definition().PressEcamKey("NOT_A_FENIX_KEY", null!, null!));
    }

    [Fact]
    public void The_press_is_the_panels_own_code_for_the_key()
    {
        // PressEcamKey runs HandleUIVariableSet with the value 1, whose S_ECAM_* cases run ExecuteButtonTransition,
        // the full press-release ([FNX-6], [A320-3]). It never writes or presses itself, so that the window and the
        // panel cannot drift apart. The source is read because a SimConnectManager cannot be built in a test.
        string body = BodyOf("internal bool PressEcamKey(");

        Assert.Matches(new Regex(@"HandleUIVariableSet\(\s*varKey\s*,\s*1\s*,", RegexOptions.CultureInvariant), body);
        Assert.DoesNotContain("SetLVar", body);
        Assert.DoesNotContain("ExecuteButtonTransition", body);
        Assert.DoesNotContain("ExecuteCalculatorCode", body);
    }

    [Fact]
    public void The_factory_wires_the_window_to_the_press_method_only()
    {
        string body = BodyOf("internal Forms.Fenix.FenixChannelSdForm CreateChannelSdForm(");

        Assert.Contains("PressEcamKey(", body);
        Assert.DoesNotContain("SetLVar", body);
        Assert.DoesNotContain("HandleUIVariableSet", body);
    }

    // ---- Reading the source ---------------------------------------------------------------------

    // A line comment or a block comment: a call named in a comment is no call.
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // The code of the member declared by <declaration> in FenixA320Definition.ChannelSd.cs, without comments: from its
    // declaration to the end of its block, or of its expression body at the semicolon.
    private static string BodyOf(string declaration)
    {
        string code = Comment.Replace(
            File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelSd.cs")),
            "");
        int start = code.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"FenixA320Definition.ChannelSd.cs does not declare {declaration}");

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
