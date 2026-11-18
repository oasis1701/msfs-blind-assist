// Characterization tests for the E/WD window's ECAM keys: the five buttons after the list that press the ECAM control
// panel's T.O CONFIG, CLR (left and right), RCL and EMER CANC keys (FenixChannelEwdForm.EcamKeys, and the factory and key
// seam of FenixA320Definition.ChannelEwd.cs). The window is built, never shown. A control on a form that was never shown is
// not Visible, so Button.PerformClick does nothing for it: a test raises the click through the button's protected OnClick
// (reflection), which is what the pilot's click, or Space or Enter on the focused button, raises. Every key and label is
// written out here, never read from the app, so that a wrong one in the app fails a test.
//
// The contract: after the list, in this order, one button for each key: T.O CONFIG (S_ECAM_TO), CLR left
// (S_ECAM_CLR_LEFT), CLR right (S_ECAM_CLR_RIGHT), RCL (S_ECAM_RCL) and EMER CANC (S_ECAM_EMER_CANCEL), in the app's ECAM
// panel order. Each button's text and accessible name is its label. A press calls the window's press delegate once with the
// key's variable name and nothing else; a refresh, F5 and opening the window never press. The definition's factory wires
// the delegate to PressEcamKey, the panel's own code (T.O CONFIG's case runs the verdict watcher [FNX-4], the others the
// full press-release [FNX-6], [A320-3]), and the window says nothing on a press ([CORE-7]). Alt+E still opens the window
// only through OpenChannelWindow, as the trusted path ([FNX-22]) wants.

using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdKeysTests
{
    private static readonly (string Label, string Key)[] Expected =
    [
        ("T.O CONFIG", "S_ECAM_TO"),
        ("CLR left", "S_ECAM_CLR_LEFT"),
        ("CLR right", "S_ECAM_CLR_RIGHT"),
        ("RCL", "S_ECAM_RCL"),
        ("EMER CANC", "S_ECAM_EMER_CANCEL")
    ];

    private static FenixChannelEwdForm NewForm(List<string> pressed)
        => new(MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines()), () => FenixEngineFamily.IaeV2500, pressed.Add);

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

    // ---- The table ------------------------------------------------------------------------------

    [Fact]
    public void The_table_is_the_five_keys_in_order()
    {
        Assert.Equal(Expected, FenixChannelEwdForm.EcamKeys.ToArray());
    }

    [Fact]
    public void Every_key_is_a_variable_and_a_control_of_the_ecam_panel_in_its_order()
    {
        var definition = new FenixA320Definition();
        var variables = definition.GetVariables();
        List<string> ecam = definition.GetPanelControls()["ECAM"];

        int previous = -1;
        foreach ((string label, string key) in FenixChannelEwdForm.EcamKeys)
        {
            Assert.True(variables.ContainsKey(key), $"{label}: {key} is not a Fenix variable");
            int at = ecam.IndexOf(key);
            Assert.True(at >= 0, $"{label}: {key} is not in the ECAM panel");
            Assert.True(at > previous, $"{label}: {key} is out of the ECAM panel's order");
            previous = at;
        }
    }

    // ---- The window -----------------------------------------------------------------------------

    [Fact]
    public void The_window_shows_the_five_buttons_after_the_list_in_tab_order()
    {
        using FenixChannelEwdForm form = NewForm([]);

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
        using FenixChannelEwdForm form = NewForm(pressed);
        Button[] buttons = All<Button>(form).ToArray();
        Assert.Equal(5, buttons.Length);

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
        using FenixChannelEwdForm form = NewForm(pressed);

        form.StartLive();
        form.RefreshNow();
        form.RefreshNow();

        Assert.Empty(pressed);
    }

    [Fact]
    public void The_window_needs_a_press_to_call()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());

        Assert.Throws<ArgumentNullException>(
            () => new FenixChannelEwdForm(neverStarted, () => FenixEngineFamily.IaeV2500, null!));
    }

    [Fact]
    public void The_window_holds_no_announcer_and_no_simconnect()
    {
        ConstructorInfo constructor = Assert.Single(typeof(FenixChannelEwdForm).GetConstructors());

        Assert.Equal(
            new[] { typeof(FenixChannelMonitor), typeof(Func<FenixEngineFamily>), typeof(Action<string>) },
            constructor.GetParameters().Select(p => p.ParameterType));
    }

    // ---- The definition's side ------------------------------------------------------------------

    [Fact]
    public void The_definition_makes_the_window_with_the_keys()
    {
        using var monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using FenixChannelEwdForm form = new FenixA320Definition().CreateChannelEwdForm(monitor, null!, null!);

        Assert.Equal(5, All<Button>(form).Count());
    }

    [Fact]
    public void The_factory_wires_the_window_to_PressEcamKey_only()
    {
        string body = BodyOf("internal Forms.Fenix.FenixChannelEwdForm CreateChannelEwdForm(");

        Assert.Contains("PressEcamKey(", body);
        Assert.DoesNotContain("SetLVar", body);
        Assert.DoesNotContain("HandleUIVariableSet", body);
        Assert.DoesNotContain("ExecuteButtonTransition", body);
    }

    [Fact]
    public void The_key_still_opens_the_window_only_through_OpenChannelWindow()
    {
        string source = StrippedSource();
        Match key = Regex.Match(
            source,
            @"partial\s+void\s+ChannelEwdKey\s*\(\s*Hotkeys\.HotkeyAction\s+action\s*,\s*ChannelKeyContext\s+ctx\s*,"
            + @"\s*ref\s+bool\s+handled\s*\)\s*(?:=>\s*(?<body>[^;]*;)|\{\s*(?<body>[^;]*;)\s*\})");
        Assert.True(key.Success, "FenixA320Definition.ChannelEwd.cs implements no ChannelEwdKey of the seam's shape");

        Assert.Equal(
            WithoutWhitespace(
                "handled = OpenChannelWindow(ctx, Services.FenixChannelDisplay.Ewd, Services.FenixChannelEwdRows.DisplayName,"
                + " monitor => CreateChannelEwdForm(monitor, ctx.SimConnect, ctx.Announcer));"),
            WithoutWhitespace(key.Groups["body"].Value));
        Assert.DoesNotContain("ShowChannelWindow", key.Groups["body"].Value);
    }

    [Fact]
    public void The_windows_list_entry_shows_the_window_with_the_same_factory()
    {
        string source = Comment.Replace(
            File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "Fenix", "FenixWindowRegistry.ChannelEwd.cs")),
            "");

        Assert.Contains(
            WithoutWhitespace("ctx.Definition.ShowChannelWindow(ctx.Announcer, FenixChannelEwdRows.DisplayName,"
                + " monitor => ctx.Definition.CreateChannelEwdForm(monitor, ctx.SimConnect, ctx.Announcer))"),
            WithoutWhitespace(source));
    }

    // ---- Reading the source ---------------------------------------------------------------------

    private static string WithoutWhitespace(string text) => Regex.Replace(text, @"\s+", "");

    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static string StrippedSource()
        => Comment.Replace(
            File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelEwd.cs")),
            "");

    private static string BodyOf(string declaration)
    {
        string code = StrippedSource();
        int start = code.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"FenixA320Definition.ChannelEwd.cs does not declare {declaration}");

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
