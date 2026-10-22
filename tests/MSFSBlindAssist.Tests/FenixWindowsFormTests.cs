// The "Fenix windows" list (Ctrl+Shift+F in input mode). The window is built, never shown: a
// ListBox and a TextBox construct and answer fine headless in this xUnit host
// (DisplayListUpdateInPlaceTests). Where order matters (the list closes BEFORE the chosen window
// opens) a window handle is created, which puts nothing on screen (SurroundingsWindowFocusReturnTests).

using System.Windows.Forms;
using MSFSBlindAssist.Forms.Fenix;

namespace MSFSBlindAssist.Tests;

public class FenixWindowsFormTests
{
    private static FenixWindowContext Context(SpeechCapture? speech = null)
        => new(null!, null!, null!, speech ?? new SpeechCapture(), null!);

    private static FenixWindowEntry Entry(string name, string description, Action<FenixWindowContext>? open = null)
        => new(name, description, open ?? (_ => { }));

    [Fact]
    public void The_window_is_titled_Fenix_windows()
    {
        using var form = new FenixWindowsForm(new[] { Entry("Cabin", "Doors and lights.") }, Context());
        Assert.Equal("Fenix windows", form.Text);
    }

    [Fact]
    public void Each_entry_is_one_list_row_reading_name_then_description_with_the_first_selected()
    {
        // A plain ListBox speaks its row text, so the row IS the spoken text (no custom accessibility object).
        using var form = new FenixWindowsForm(new[]
        {
            Entry("Cabin", "Doors and lights."),
            Entry("Engines", "Start-up and fuel."),
        }, Context());

        var list = Assert.Single(form.Controls.OfType<ListBox>());
        Assert.Equal(new object[] { "Cabin. Doors and lights.", "Engines. Start-up and fuel." }, list.Items.Cast<object>().ToArray());
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void An_entry_without_a_description_is_just_its_name()
        => Assert.Equal("Cabin", FenixWindowsForm.ItemText(Entry("Cabin", "")));

    [Fact]
    public void With_no_entries_a_read_only_tab_stop_text_box_replaces_the_list()
    {
        // Status text is a read-only TextBox, never a Label: a Label is not in the tab order [VAT-13].
        using var form = new FenixWindowsForm(Array.Empty<FenixWindowEntry>(), Context());

        Assert.Empty(form.Controls.OfType<ListBox>());
        var status = Assert.Single(form.Controls.OfType<TextBox>());
        Assert.Equal("No Fenix windows yet.", status.Text);
        Assert.True(status.ReadOnly);
        Assert.True(status.TabStop);
        Assert.Empty(form.Controls.OfType<Label>());
    }

    [Fact]
    public void Opening_the_selected_entry_runs_that_entry_with_the_context()
    {
        FenixWindowContext? seen = null;
        var context = Context();
        using var form = new FenixWindowsForm(new[]
        {
            Entry("Cabin", "Doors and lights."),
            Entry("Engines", "Start-up and fuel.", c => seen = c),
        }, context);
        form.Controls.OfType<ListBox>().Single().SelectedIndex = 1;

        form.OpenSelected();

        Assert.Same(context, seen);
    }

    [Fact]
    public void The_list_closes_before_the_chosen_window_opens()
    {
        var order = new List<string>();
        using var form = new FenixWindowsForm(new[] { Entry("Cabin", "Doors and lights.", _ => order.Add("open")) }, Context());
        form.FormClosed += (_, _) => order.Add("closed");
        _ = form.Handle;   // a handle-less Form.Close() raises nothing

        form.OpenSelected();

        Assert.Equal(new[] { "closed", "open" }, order);
    }

    [Fact]
    public void A_window_that_fails_to_open_is_announced_by_name_and_does_not_throw()
    {
        var speech = new SpeechCapture();
        using var form = new FenixWindowsForm(new[]
        {
            Entry("Cabin", "Doors and lights.", _ => throw new InvalidOperationException("boom")),
        }, Context(speech));

        form.OpenSelected();

        Assert.Equal(new[] { "Could not open Cabin." }, speech.All);
    }

    [Fact]
    public void Escape_closes_the_list_without_opening_anything()
    {
        var opened = false;
        using var form = new FenixWindowsForm(new[] { Entry("Cabin", "Doors and lights.", _ => opened = true) }, Context());
        var closed = false;
        form.FormClosed += (_, _) => closed = true;
        _ = form.Handle;

        // Escape reaches the form as a dialog key whichever control has focus; call that entry point directly.
        var processDialogKey = typeof(FenixWindowsForm).GetMethod(
            "ProcessDialogKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var handled = (bool)processDialogKey.Invoke(form, new object[] { Keys.Escape })!;

        Assert.True(handled);
        Assert.True(closed);
        Assert.False(opened);
    }

    [Fact]
    public void Opening_with_nothing_selected_does_nothing()
    {
        var speech = new SpeechCapture();
        using var form = new FenixWindowsForm(Array.Empty<FenixWindowEntry>(), Context(speech));

        form.OpenSelected();

        Assert.Empty(speech.All);
        Assert.False(form.IsDisposed);
    }
}
