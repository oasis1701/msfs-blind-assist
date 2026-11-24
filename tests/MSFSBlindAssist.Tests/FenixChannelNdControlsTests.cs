// Characterization tests for the EFIS controls the ND window carries under its list (FenixChannelNdControls, and the
// controls of FenixChannelNdForm): the Mode and Range combos and the CSTR, WPT, VOR.D, NDB, ARPT and LS buttons.
// The controls follow every refresh from the snapshot the rows are read from; a pick or a press calls the writer the
// window is given with the panel's own key and number, and a refresh never does. The window says nothing itself.
//
// The window is built, never shown. A control on a form that was never shown is not Visible, so Button.PerformClick
// does nothing for it: a test raises the click, and a pick's SelectionChangeCommitted, through the control's protected
// OnClick / OnSelectionChangeCommitted (reflection), which is what the user's click and pick raise.

using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelNdControlsTests
{
    private static readonly FenixDisplayVerdict Trust = new(true, "trusted");

    private static FenixChannelSnapshot Snapshot(
        FenixDisplayVerdict verdict, ulong mode = 3, ulong range = 20, ulong? option = null, bool ls = false)
    {
        var efis = new FenixChannelProtoWriter().Varint(1, mode).Varint(3, range);
        if (option is not null) efis.Varint(2, option.Value);
        if (ls) efis.Bool(6, true);
        var remote = new FenixChannelProtoWriter().Message(1, efis).Message(1, new FenixChannelProtoWriter().Varint(1, 1))
            .Message(68, new FenixChannelProtoWriter().String(1, "VAKSU g318c").String(2, "g84 csNM").String(3, "g17:29"))
            .Message(163, new FenixChannelProtoWriter().String(1, "wsGSlgd456wsdTASlgd448").String(2, "g213w / g37"))
            .Message(194, new FenixChannelProtoWriter().Double(1, 318.4).Double(2, 322.6).Double(3, 4.5));
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, remote).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        return new FenixChannelSnapshot(verdict, null, data, null, 0, 1);
    }

    private static FenixChannelSnapshot Trusted(ulong mode = 3, ulong range = 20, ulong? option = null, bool ls = false)
        => Snapshot(Trust, mode, range, option, ls);

    private static FenixChannelSnapshot Untrusted()
        => new(new FenixDisplayVerdict(false, "display off"), null, null, null, 0, 1);

    private static FenixNdData Header(FenixChannelSnapshot s)
    {
        FenixChannelSection<FenixNdData> section = FenixChannelNd.Read(s);
        Assert.Null(section.Problem);
        return section.Value!;
    }

    private sealed class Rig : IDisposable
    {
        public readonly List<(string Key, double Value)> Writes = [];
        public readonly FenixChannelMonitor Monitor = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        public readonly FenixChannelNdForm Form;

        public Rig(bool withWriter = true)
        {
            Form = new FenixChannelNdForm(Monitor, withWriter ? (key, value) => Writes.Add((key, value)) : null);
        }

        public ComboBox Mode => Form.ModeCombo!;
        public ComboBox Range => Form.RangeCombo!;
        public IReadOnlyList<Button> Buttons => Form.EfisButtons;

        public void Apply(FenixChannelSnapshot s) => Form.ApplyControls(s);

        public void Pick(ComboBox combo, int index)
        {
            combo.SelectedIndex = index;
            typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(combo, [EventArgs.Empty]);
        }

        public static void Press(Button button)
            => typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(button, [EventArgs.Empty]);

        public void Dispose()
        {
            Form.Dispose();
            Monitor.Dispose();
        }
    }

    [Fact]
    public void Each_control_writes_its_panel_key_and_nothing_else()
    {
        using var rig = new Rig();

        // A refresh in between never writes.
        rig.Apply(Trusted(mode: 3, range: 20));
        rig.Form.RefreshNow();
        Assert.Empty(rig.Writes);

        for (int mode = 0; mode < 5; mode++)
        {
            rig.Writes.Clear();
            rig.Pick(rig.Mode, mode);
            Assert.Equal([("S_FCU_EFIS1_ND_MODE", (double)mode)], rig.Writes);
            rig.Apply(Trusted());
            rig.Form.RefreshNow();
            Assert.Single(rig.Writes);
        }

        for (int range = 0; range < 6; range++)
        {
            rig.Writes.Clear();
            rig.Pick(rig.Range, range);
            Assert.Equal([("S_FCU_EFIS1_ND_ZOOM", (double)range)], rig.Writes);
        }

        string[] keys =
        [
            "S_FCU_EFIS1_CSTR", "S_FCU_EFIS1_WPT", "S_FCU_EFIS1_VORD", "S_FCU_EFIS1_NDB", "S_FCU_EFIS1_ARPT", "S_FCU_EFIS1_LS_PRESS"
        ];
        Assert.Equal(6, rig.Buttons.Count);
        for (int i = 0; i < keys.Length; i++)
        {
            rig.Writes.Clear();
            Rig.Press(rig.Buttons[i]);
            Assert.Equal([(keys[i], 1.0)], rig.Writes);
            rig.Apply(Trusted());
            Assert.Single(rig.Writes);
        }
    }

    [Theory]
    [InlineData(false, false, 0, true)]                                       // not focused: always follows the ND
    [InlineData(true, false, 0, false)]                                       // just picked: the Fenix is still taking it
    [InlineData(true, false, FenixChannelNdForm.PickSettleMs - 1, false)]
    [InlineData(true, false, FenixChannelNdForm.PickSettleMs, true)]          // a cockpit change, or a pick not taken
    [InlineData(true, true, FenixChannelNdForm.PickSettleMs, false)]          // an open list stays as the pilot has it
    [InlineData(false, true, FenixChannelNdForm.PickSettleMs, false)]
    public void A_combo_follows_the_nd_unless_the_pilot_is_using_it(bool focused, bool droppedDown, long msSincePick, bool follows)
    {
        Assert.Equal(follows, FenixChannelNdForm.FollowsTheNd(focused, droppedDown, msSincePick));
    }

    [Fact]
    public void Without_a_writer_the_window_adds_no_controls()
    {
        using var rig = new Rig(withWriter: false);

        Assert.Null(rig.Form.ModeCombo);
        Assert.Empty(rig.Buttons);
        Assert.Single(rig.Form.Controls);
        rig.Apply(Trusted());
    }

    [Fact]
    public void The_controls_are_named_for_the_reader()
    {
        using var rig = new Rig();

        Assert.Equal("Mode", rig.Mode.AccessibleName);
        Assert.Equal("Range", rig.Range.AccessibleName);
        Assert.Equal(ComboBoxStyle.DropDownList, rig.Mode.DropDownStyle);
        Assert.Equal(ComboBoxStyle.DropDownList, rig.Range.DropDownStyle);
        Assert.Equal(new[] { "LS", "VOR", "NAV", "ARC", "PLAN" }, rig.Mode.Items.Cast<string>());
        Assert.Equal(new[] { "10", "20", "40", "80", "160", "320" }, rig.Range.Items.Cast<string>());
    }

    [Fact]
    public void Button_text_follows_the_snapshot()
    {
        using var rig = new Rig();
        string[] names = ["CSTR", "WPT", "VOR.D", "NDB", "ARPT"];

        // Each option on, the others off, LS off.
        for (ulong option = 0; option < 5; option++)
        {
            FenixChannelSnapshot s = Trusted(option: option);
            Assert.Equal(names[option], Header(s).MapOption);
            rig.Apply(s);
            for (int i = 0; i < 5; i++)
            {
                Assert.Equal(names[i] + (i == (int)option ? ": on" : ": off"), rig.Buttons[i].Text);
            }

            Assert.Equal("LS: off", rig.Buttons[5].Text);
        }

        rig.Apply(Trusted(ls: true));
        Assert.Equal("LS: on", rig.Buttons[5].Text);
        Assert.Equal("CSTR: off", rig.Buttons[0].Text);

        rig.Apply(Trusted(ls: false));
        Assert.Equal("LS: off", rig.Buttons[5].Text);

        // Not trusted: the bare names.
        rig.Apply(Untrusted());
        Assert.Equal(new[] { "CSTR", "WPT", "VOR.D", "NDB", "ARPT", "LS" }, rig.Buttons.Select(b => b.Text));

        // A header problem (the efis part in the wrong shape): the bare names too.
        var wrong = new FenixChannelProtoWriter().Varint(1, 3);
        var push = FenixChannelPush.From(new FenixChannelProtoWriter().Message(1, wrong).ToArray())!;
        FenixChannelPush data = push.TryGet(1, out FenixChannelField field) ? field.AsMessage()! : push;
        rig.Apply(new FenixChannelSnapshot(Trust, null, data, null, 0, 1));
        Assert.Equal("CSTR", rig.Buttons[0].Text);

        Assert.Equal("CSTR", FenixChannelNdControls.ButtonText("CSTR", null));
        Assert.Equal("LS: off", FenixChannelNdControls.ButtonText("LS", Header(Trusted())));
    }

    [Fact]
    public void The_combos_follow_mode_and_range_without_writing()
    {
        using var rig = new Rig();

        rig.Apply(Trusted(mode: 3, range: 80));
        Assert.Equal("ARC", rig.Mode.SelectedItem);
        Assert.Equal("80", rig.Range.SelectedItem);

        rig.Apply(Trusted(mode: 4, range: 320));
        Assert.Equal("PLAN", rig.Mode.SelectedItem);
        Assert.Equal("320", rig.Range.SelectedItem);

        // A range that is not in the list: no selection.
        rig.Apply(Trusted(mode: 4, range: 25));
        Assert.Equal(-1, rig.Range.SelectedIndex);

        rig.Apply(Untrusted());
        Assert.Equal(-1, rig.Mode.SelectedIndex);
        Assert.Equal(-1, rig.Range.SelectedIndex);

        Assert.Empty(rig.Writes);
    }

    [Fact]
    public void The_refresh_hook_drives_the_controls_from_the_monitors_snapshot()
    {
        using var rig = new Rig();
        rig.Monitor.OnDataMessage(FenixChannelProtoWriter.GoodPush());
        rig.Mode.SelectedIndex = 2;
        rig.Buttons[0].Text = "CSTR: off";

        rig.Form.StartLive();

        // The monitor's snapshot reaches the hook: the good push carries no ND header (no ground speed line), so the
        // controls drop to their not-trusted state, whatever the verdict says.
        Assert.Equal(-1, rig.Mode.SelectedIndex);
        Assert.Equal("CSTR", rig.Buttons[0].Text);
        Assert.Empty(rig.Writes);
    }

    [Fact]
    public void A_refresh_that_changes_nothing_leaves_the_buttons_untouched()
    {
        using var rig = new Rig();
        rig.Apply(Trusted(option: 1, ls: true));
        int changes = 0;
        foreach (Button button in rig.Buttons) button.TextChanged += (_, _) => changes++;

        rig.Apply(Trusted(option: 1, ls: true));

        Assert.Equal(0, changes);
        Assert.Equal("WPT: on", rig.Buttons[1].Text);
        // The reader takes the text: no accessible name is set that could be re-announced.
        Assert.All(rig.Buttons, b => Assert.True(string.IsNullOrEmpty(b.AccessibleName)));
    }

    [Fact]
    public void The_list_comes_first_in_the_tab_order()
    {
        using var rig = new Rig();
        DisplayListBox list = rig.Form.Controls.OfType<DisplayListBox>().Single();

        Assert.Equal(0, list.TabIndex);
        Control panel = rig.Form.Controls.Cast<Control>().Single(c => c != list);
        Assert.True(panel.TabIndex > list.TabIndex);

        // Inside the panel: Mode, Range, then the six buttons, left to right.
        Control[] inOrder = [.. panel.Controls.Cast<Control>().OrderBy(c => c.TabIndex)];
        Assert.Equal(
            [rig.Mode, rig.Range, .. rig.Buttons],
            inOrder);
        Assert.All(inOrder, c => Assert.True(c.TabStop));
    }
}
