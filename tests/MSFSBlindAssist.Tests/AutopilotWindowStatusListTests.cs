using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.PMDG;

namespace MSFSBlindAssist.Tests;

/// <summary>The shared Ctrl+P window's optional status list (the A300's), and that the PMDG window,
/// which passes none, is built exactly as before.</summary>
public class AutopilotWindowStatusListTests
{
    private static List<ToggleButtonDef> Buttons() => new() { new("&AP", () => "On", () => { }) };

    [Fact]
    public void Without_a_status_provider_there_is_no_list_and_the_first_button_comes_first()
    {
        using var w = new PMDGAutopilotWindow("737 Autopilot", Buttons(), Array.Empty<SelectorRowDef>());
        Assert.Empty(w.Controls.OfType<ListBox>());
        Assert.IsType<Button>(w.Controls.Cast<Control>().OrderBy(c => c.TabIndex).First());
    }

    [Fact]
    public void A_status_provider_adds_a_named_list_first_in_the_tab_order_with_the_first_line_selected()
    {
        using var w = new PMDGAutopilotWindow("A300 Autopilot", Buttons(), Array.Empty<SelectorRowDef>(),
            () => new[] { "Speed 250 knots", "Autopilot: CMD 1" }, "Autopilot status");
        var list = Assert.Single(w.Controls.OfType<ListBox>());
        Assert.Equal("Autopilot status", list.AccessibleName);
        Assert.Equal(0, list.TabIndex);
        w.RefreshStates();
        Assert.Equal(new[] { "Speed 250 knots", "Autopilot: CMD 1" }, list.Items.Cast<string>());
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void A_refresh_rewrites_changed_lines_in_place_and_keeps_the_readers_line()
    {
        var lines = new List<string> { "Speed 250 knots", "Heading 270" };
        using var w = new PMDGAutopilotWindow("A300 Autopilot", Buttons(), Array.Empty<SelectorRowDef>(),
            () => lines.ToArray(), "Autopilot status");
        var list = w.Controls.OfType<ListBox>().Single();
        w.RefreshStates();
        list.SelectedIndex = 1;
        lines[0] = "Speed 260 knots";
        w.RefreshStates();
        Assert.Equal("Speed 260 knots", list.Items[0]);
        Assert.Equal(1, list.SelectedIndex);
    }
}
