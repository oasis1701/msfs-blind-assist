// The shared 757 display window: a named live list first, an optional controls area after it, rows refreshed in
// place, Escape hiding the window.

using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757DisplayFormTests
{
    private static DisplayListBox List(Form form) => form.Controls.OfType<DisplayListBox>().Single();

    private static string[] Lines(DisplayListBox list) => list.Items.Cast<object>().Select(o => o.ToString()!).ToArray();

    [Fact]
    public void The_list_is_named_and_first()
    {
        using var form = new BBS757DisplayForm("757 EICAS", "Upper EICAS", () => new[] { "a" });
        var list = List(form);
        Assert.Equal("757 EICAS", form.Text);
        Assert.Equal("Upper EICAS", list.AccessibleName);
        Assert.Equal(0, list.TabIndex);
        Assert.True(list.TabStop);
        Assert.Equal(new[] { "a" }, Lines(list));
    }

    [Fact]
    public void Rows_refresh_in_place()
    {
        IReadOnlyList<string> rows = new[] { "a", "b" };
        using var form = new BBS757DisplayForm("t", "n", () => rows);
        var list = List(form);
        list.Items.Add("marker");
        form.RefreshRows();   // unchanged: the list is left alone
        Assert.Contains("marker", Lines(list));

        rows = new[] { "a", "c" };
        form.RefreshRows();
        Assert.Equal(new[] { "a", "c" }, Lines(list));
    }

    [Fact]
    public void Escape_hides_the_window()
    {
        using var form = new BBS757DisplayForm("t", "n", () => new[] { "a" });
        form.ShowForm();
        Assert.True(form.Visible);
        var args = new KeyEventArgs(Keys.Escape);
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { args });
        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
    }

    [Fact]
    public void A_close_hides_and_only_close_for_good_ends_the_window()
    {
        var form = new BBS757DisplayForm("t", "n", () => new[] { "a" });
        form.ShowForm();
        form.Close();   // the pilot's close (Alt+F4): hidden, kept for reuse
        Assert.False(form.IsDisposed);
        Assert.False(form.Visible);

        form.ShowForm();
        form.CloseForGood();   // the 757 definition's switch away
        Assert.True(form.IsDisposed);
        form.CloseForGood();   // again: nothing
        Assert.True(form.IsDisposed);

        // Never shown: disposed as well.
        var unseen = new BBS757DisplayForm("t", "n", () => new[] { "a" });
        unseen.CloseForGood();
        Assert.True(unseen.IsDisposed);
    }

    [Fact]
    public void The_controls_follow_the_list_in_the_tab_order()
    {
        var panel = new Panel { Name = "controls" };
        var button = new Button { Text = "Go" };
        panel.Controls.Add(button);
        using var form = new BBS757DisplayForm("t", "n", () => new[] { "a" }, panel);
        Assert.Contains(panel, form.Controls.Cast<Control>());
        Assert.True(panel.TabIndex > List(form).TabIndex);
        Assert.True(panel.Top >= List(form).Bottom);
    }
}
