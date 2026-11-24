// The Ctrl+P window's FMA list: first in the tab order, showing the monitor's rows, rewritten only on change.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757AutopilotWindowFmaTests
{
    private static BBS757AutopilotWindow Window(Func<IReadOnlyList<string>> rows) =>
        new(new[]
        {
            new Bbs757AutopilotButton("CMD L", () => "On", () => { }, 'L'),
            new Bbs757AutopilotButton("LNAV", () => "", () => { }, 'N'),
        }, null, rows);

    private static DisplayListBox FmaList(Form form) =>
        form.Controls.OfType<DisplayListBox>().Single(l => l.AccessibleName == "Flight mode annunciator");

    private static string[] Lines(DisplayListBox list) => list.Items.Cast<object>().Select(o => o.ToString()!).ToArray();

    [Fact]
    public void The_fma_list_is_first_in_the_tab_order()
    {
        using var form = Window(() => new[] { "A/T: SPD" });
        var list = FmaList(form);
        Assert.Equal("Flight mode annunciator", list.Name);
        Assert.Equal(0, list.TabIndex);
        Assert.True(list.TabStop);
        Assert.All(form.Controls.OfType<Button>(), b => Assert.True(b.TabIndex > list.TabIndex));
    }

    [Fact]
    public void The_list_shows_the_monitors_rows()
    {
        var rows = new[] { "A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: VNAV PTH, armed G/S", "AFDS: CMD" };
        using var form = Window(() => rows);
        Assert.Equal(rows, Lines(FmaList(form)));
    }

    [Fact]
    public void Unchanged_rows_are_not_set_again()
    {
        IReadOnlyList<string> rows = new[] { "A/T: SPD", "AFDS: CMD" };
        using var form = Window(() => rows);
        var list = FmaList(form);
        list.Items.Add("marker");   // an unchanged refresh must leave the list alone
        form.RefreshFma();
        Assert.Contains("marker", Lines(list));

        rows = new[] { "A/T: none", "AFDS: CMD" };
        form.RefreshFma();
        Assert.Equal(rows, Lines(list));
    }

    [Fact]
    public void While_not_running_the_list_shows_one_status_row()
    {
        IReadOnlyList<string> rows = new[] { "FMA reader starting" };
        using var form = Window(() => rows);
        Assert.Equal(new[] { "FMA reader starting" }, Lines(FmaList(form)));
        rows = new[] { "A/T: SPD", "Roll: none", "Pitch: none", "AFDS: none" };
        form.RefreshFma();
        Assert.Equal(4, FmaList(form).Items.Count);
    }
}
