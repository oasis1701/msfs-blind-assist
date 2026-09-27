using System.Windows.Forms;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins which panel controls MainForm composes a described state for (MainForm.ShowsDescribedState):
/// only a Button (its label) or a read-only TextBox (its text) can show one. RefreshDescribedState
/// runs for every dependent of an update — on the MD-11, every stateful row of the open panel when
/// the DC gate moves — so composing for any other control built a sentence nothing rendered.
/// </summary>
public class DescribedStateTargetTests
{
    [Fact]
    public void A_button_and_a_read_only_text_box_show_a_described_state()
    {
        using var button = new Button();
        using var status = new TextBox { ReadOnly = true };

        Assert.True(MainForm.ShowsDescribedState(button));
        Assert.True(MainForm.ShowsDescribedState(status));
    }

    [Fact]
    public void An_entry_box_a_combo_a_slider_and_a_checkbox_do_not()
    {
        using var entry = new TextBox();
        using var combo = new ComboBox();
        using var slider = new TrackBar();
        using var check = new CheckBox();

        Assert.False(MainForm.ShowsDescribedState(entry));
        Assert.False(MainForm.ShowsDescribedState(combo));
        Assert.False(MainForm.ShowsDescribedState(slider));
        Assert.False(MainForm.ShowsDescribedState(check));
    }
}
