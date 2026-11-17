// Plan 26 Task 7: the EHSI reader on the 757 definition: its monitor, its "EHSI messages" Ctrl+M row, Alt+N, which opens
// the "Captain EHSI" window while the reader runs and falls back to the AI read when it cannot, and Ctrl+W, which says the
// active waypoint at once, interrupting, with the window open or not. The window is a real WinForms form, as in the
// upper EICAS wiring tests; the lit-display cases drive the real monitor over a scripted sampler.

using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect;
using static MSFSBlindAssist.Tests.Bbs757EhsiTextTests;

namespace MSFSBlindAssist.Tests;

public class Bbs757EhsiWiringTests
{
    [Fact]
    public void Alt_N_opens_the_ehsi_window_while_the_reader_runs()
    {
        using var rig = new Rig(started: true);

        Assert.True(rig.Press(HotkeyAction.ReadDisplayND));

        Assert.Empty(rig.AiReads);
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.Equal("EHSI", form.Text);
        Assert.Equal("EHSI", form.AccessibleName);
        Assert.True(form.Visible);
        Assert.Empty(rig.Speech.All);   // the window's title and list are what the screen reader says (CORE-7)
    }

    [Fact]
    public void Alt_N_falls_back_to_the_ai_read_when_unavailable()
    {
        using var rig = new Rig(started: false);

        Assert.True(rig.Def.Ehsi.KeyFallsBackToAi());
        Assert.True(rig.Press(HotkeyAction.ReadDisplayND));

        Assert.Equal(new[] { HotkeyAction.ReadDisplayND }, rig.AiReads);
        Assert.Empty(rig.Def.Displays.Windows);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_second_Alt_N_shows_the_same_window_again()
    {
        using var rig = new Rig(started: true);
        rig.Press(HotkeyAction.ReadDisplayND);
        var first = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        first.Hide();

        rig.Press(HotkeyAction.ReadDisplayND);

        Assert.Same(first, Assert.Single(rig.Def.Displays.Windows));
        Assert.True(first.Visible);
    }

    [Fact]
    public void FullWanted_follows_the_windows_visibility()
    {
        using var rig = new Rig(started: true);
        Assert.False(rig.Def.EhsiDisplay.FullWanted());   // no window yet: the route is not read

        rig.Press(HotkeyAction.ReadDisplayND);
        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.True(rig.Def.EhsiDisplay.FullWanted());

        form.Hide();
        Assert.False(rig.Def.EhsiDisplay.FullWanted());

        rig.Press(HotkeyAction.ReadDisplayND);
        Assert.True(rig.Def.EhsiDisplay.FullWanted());

        // A close by the pilot (the title bar, Alt+F4) hides the window: the route is not read.
        rig.Press(HotkeyAction.ReadDisplayND);
        Assert.True(rig.Def.EhsiDisplay.FullWanted());
        form.Close();
        Assert.False(form.IsDisposed);
        Assert.False(rig.Def.EhsiDisplay.FullWanted());

        rig.Press(HotkeyAction.ReadDisplayND);
        form.CloseForGood();
        Assert.False(rig.Def.EhsiDisplay.FullWanted());
    }

    [Fact]
    public void FullWanted_is_true_when_the_windows_first_rows_are_built_and_they_list_the_map()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());
        // The sampler reads the full set only while the display wants it, as the real one does.
        f.Sampler.Script = () => f.Display.FullWanted() ? Ehsi() : Light();
        Assert.False(f.Display.FullWanted());

        rig.Press(HotkeyAction.ReadDisplayND);

        var form = Assert.Single(rig.Def.Displays.Windows.OfType<BBS757DisplayForm>());
        Assert.NotEmpty(f.Script.RowsWanted);
        Assert.All(f.Script.RowsWanted, Assert.True);
        // The first list already holds the route: a reading was taken as the key was pressed, not a second later.
        Assert.Contains("Map: KOLM, active, 37.5 NM, 86° right", f.Script.FirstRows!);
        Assert.DoesNotContain("Map: reading", f.Script.FirstRows!);
        Assert.Empty(rig.Speech.All);

        form.Hide();
        Assert.False(f.Display.FullWanted());
    }

    [Fact]
    public void Ctrl_W_says_the_top_line_interrupting()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());

        Assert.True(rig.Press(HotkeyAction.ReadNDWaypoint));

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Empty(rig.AiReads);
        GC.KeepAlive(f);
    }

    [Fact]
    public void Ctrl_W_answers_with_the_window_closed()
    {
        using var rig = new Rig(started: false);
        rig.Lit(Light());
        Assert.False(rig.Def.EhsiDisplay.FullWanted());
        Assert.Empty(rig.Def.Displays.Windows);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
        Assert.Empty(rig.Def.Displays.Windows);   // the key never opens the window
    }

    [Fact]
    public void Ctrl_W_still_answers_with_the_ctrl_m_row_muted()
    {
        using var rig = new Rig(started: false);
        rig.Lit(Light(), muted: true);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "KOLM, 37.5 NM, no ETA" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_while_the_display_is_dark_says_so()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());
        f.Show(Dark());
        f.Tick();

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: display dark" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_names_the_monitors_state_while_the_reader_waits()
    {
        using var rig = new Rig(started: true);
        Assert.Equal("WaitingForSimulator", rig.Def.Ehsi.StateName);

        rig.Press(HotkeyAction.ReadNDWaypoint);
        Assert.Equal(new[] { "EHSI waypoint unavailable: waiting for the simulator" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void Ctrl_W_names_the_monitors_own_reason_once_it_is_unavailable()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Light());
        f.Env.Analyse = () => Task.FromResult(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "no layout file"));
        f.Monitor.Stop();
        f.Monitor.Start(f.Env, startTimer: false);
        for (int i = 0; i < 40 && f.Monitor.StateName != "Unavailable"; i++) f.Tick();
        Assert.Equal("Unavailable", f.Monitor.StateName);
        string reason = Assert.IsType<string>(f.Monitor.NoReadingReason);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: " + reason }, rig.Speech.Interrupts);
        Assert.NotEqual("display dark", reason);
    }

    [Fact]
    public void Ctrl_W_before_the_reader_started_says_no_reading_yet()
    {
        using var rig = new Rig(started: false);

        rig.Press(HotkeyAction.ReadNDWaypoint);

        Assert.Equal(new[] { "EHSI waypoint unavailable: no reading yet" }, rig.Speech.Interrupts);
    }

    [Fact]
    public void The_ctrl_m_row_is_listed()
    {
        var def = new BBS757Definition();
        var rows = def.MonitorRows();

        var row = new MonitorRow(Bbs757EhsiDisplay.MuteKey, "EHSI messages");
        Assert.Contains(row, rows);
        Assert.Contains(row, def.Displays.Rows);
        Assert.Same(def.Ehsi, def.Displays.Monitors.Single(m => ReferenceEquals(m, def.Ehsi)));
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_ehsis_inputs_ride_the_batch_once_and_stay_under_the_ceiling()
    {
        // Plan 26's two position inputs and plan 28's wind and gear: five, each streamed once in its own units.
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        Assert.Equal(5, Bbs757EhsiDisplay.SimInputs.Count);
        foreach (var input in Bbs757EhsiDisplay.SimInputs)
        {
            var carrier = vars[keys[input.Key]];
            Assert.Equal((input.Name, SimVarType.SimVar), (carrier.Name, carrier.Type));
            Assert.Equal(input.Units, carrier.Units, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(UpdateFrequency.Continuous, carrier.UpdateFrequency);
            Assert.True(carrier.IsAnnounced);
            Assert.Single(vars, kv => kv.Value.Name == input.Name && ContinuousBatchLayout.RidesBatch(kv.Value));
            if (keys[input.Key].StartsWith(BBS757Definition.DisplayInputCopyPrefix, StringComparison.Ordinal))
                Assert.True(carrier.ExcludeFromMonitorManager);   // a hidden copy is never listed in Ctrl+M ([VAR-9])
        }
        Assert.True(vars.Count < 900, $"{vars.Count} variables");   // the data-definition ceiling's test
    }

    // ---- The window's EFIS controls (task 10) ------------------------------------------------

    private const string ModeNode = "B757_ADI_1_CTR_KNOB";
    private const string RangeNode = "B757_ADI_1_TFC_KNOB";
    private const string CtrNode = "B757_ADI_1_CTR_BUTTON";
    private const string TfcNode = "B757_ADI_1_TFC_BUTTON";

    private static readonly (string Node, string Caption)[] Toggles =
    {
        ("B757_ADI_1_WXR_BUTTON", "&WXR"),
        ("B757_ADI_1_TERR_BUTTON", "T&ERR"),
        ("B757_ADI_1_NAV-AID_BUTTON", "&NAV AID"),
        ("B757_ADI_1_APRT_BUTTON", "&APRT"),
        ("B757_ADI_1_DATA_BUTTON", "&DATA"),
        ("B757_ADI_1_WPT_BUTTON", "W&PT"),
    };

    private static readonly string[] Order =
        new[] { ModeNode, RangeNode, CtrNode, TfcNode }.Concat(Toggles.Select(t => t.Node)).ToArray();

    private sealed record Window(BBS757DisplayForm Form, FlowLayoutPanel Panel)
    {
        public ComboBox Combo(string node) => Panel.Controls.OfType<ComboBox>().Single(c => c.Name == node);
        public Button Button(string node) => Panel.Controls.OfType<Button>().Single(c => c.Name == node);
        public CheckBox Check(string node) => Panel.Controls.OfType<CheckBox>().Single(c => c.Name == node);
        public List<Control> Controls => Panel.Controls.Cast<Control>().Where(c => c is not Label).ToList();
    }

    private static Window OpenControls(Rig rig, SpeechCapture? speech = null)
    {
        rig.Press(HotkeyAction.ReadDisplayND, speech);
        var form = rig.Def.Displays.Windows.OfType<BBS757DisplayForm>().Single(f => !f.IsDisposed);
        return new Window(form, form.Controls.OfType<FlowLayoutPanel>().Single());
    }

    private static string Shown(ComboBox combo) => (string)combo.SelectedItem!;

    [Fact]
    public void The_window_holds_the_ten_efis_controls_after_the_list_in_the_tab_order()
    {
        using var rig = new Rig(started: true);

        var w = OpenControls(rig);

        Assert.Equal("EHSI controls", w.Panel.Name);
        Assert.Equal("EHSI controls", w.Panel.AccessibleName);
        var list = w.Form.Controls.Cast<Control>().Single(c => c is not FlowLayoutPanel);
        Assert.True(w.Panel.TabIndex > list.TabIndex);

        Assert.Equal(Order, w.Controls.Select(c => c.Name));
        Assert.Equal(new[] { typeof(ComboBox), typeof(ComboBox), typeof(Button), typeof(Button) }
            .Concat(Enumerable.Repeat(typeof(CheckBox), 6)), w.Controls.Select(c => c.GetType()));

        // Every control in the panel, labels included, in the tab order it is laid out in; each drop-down right after
        // its label, whose mnemonic reaches it.
        var all = w.Panel.Controls.Cast<Control>().ToList();
        Assert.Equal(Enumerable.Range(0, all.Count), all.Select(c => c.TabIndex));
        Assert.Equal(new[] { "&Mode", "&Range" }, all.OfType<Label>().Select(l => l.Text));
        foreach (var (node, name) in new[] { (ModeNode, "Mode"), (RangeNode, "Range") })
        {
            var combo = w.Combo(node);
            Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
            Assert.Equal(name, combo.AccessibleName);
            var label = Assert.IsType<Label>(all[all.IndexOf(combo) - 1]);
            Assert.Equal("&" + name, label.Text);
        }

        Assert.Equal("&CTR", w.Button(CtrNode).Text);
        Assert.Equal("&TFC", w.Button(TfcNode).Text);
        Assert.Equal(Toggles.Select(t => t.Caption), w.Controls.OfType<CheckBox>().Select(c => c.Text));
        // The map's labels, and the name the screen reader is given is the visible text.
        foreach (var c in w.Controls.Where(c => c is ButtonBase))
        {
            Assert.Equal(rig.Def.ControlFor(c.Name)!.Label, c.Text.Replace("&", ""));
            Assert.Equal(c.Text.Replace("&", ""), c.AccessibilityObject.Name);
        }

        // No two mnemonics in the window collide.
        var keys = all.Select(c => c.Text).Where(t => t.Contains('&'))
            .Select(t => char.ToUpperInvariant(t[t.IndexOf('&') + 1])).ToList();
        Assert.Equal(10, keys.Count);
        Assert.Equal(keys.Count, keys.Distinct().Count());

        // The controls fit in the window.
        Assert.True(w.Panel.Bottom <= w.Form.ClientSize.Height, $"{w.Panel.Bottom} > {w.Form.ClientSize.Height}");
        Assert.True(w.Panel.Right <= w.Form.ClientSize.Width, $"{w.Panel.Right} > {w.Form.ClientSize.Width}");
    }

    [Fact]
    public void Each_drop_down_lists_the_maps_positions()
    {
        using var rig = new Rig(started: true);

        var w = OpenControls(rig);

        Assert.Equal(new[] { "VOR", "APP", "MAP", "PLAN" }, w.Combo(ModeNode).Items.Cast<string>());
        Assert.Equal(new[] { "10 NM", "20 NM", "40 NM", "80 NM", "160 NM", "320 NM" }, w.Combo(RangeNode).Items.Cast<string>());
        foreach (var node in new[] { ModeNode, RangeNode })
            Assert.Equal(rig.Def.ControlFor(node)!.Positions.Select(p => p.Label), w.Combo(node).Items.Cast<string>());
    }

    [Fact]
    public void Opening_shows_the_cockpits_mode_range_and_switches()
    {
        using var rig = new Rig(started: true);
        rig.Io.Cockpit[ModeNode] = 3;
        rig.Io.Cockpit[RangeNode] = 4;
        rig.Io.Cockpit["B757_ADI_1_WXR_BUTTON"] = 1;
        rig.Io.Cockpit["B757_ADI_1_TERR_BUTTON"] = 0;

        var w = OpenControls(rig);

        Assert.Equal("PLAN", Shown(w.Combo(ModeNode)));
        Assert.Equal("160 NM", Shown(w.Combo(RangeNode)));
        Assert.Equal(new[] { true, false, false, true, false, false },
            Toggles.Select(t => w.Check(t.Node).Checked));
        Assert.Empty(rig.Io.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_cockpit_change_updates_the_controls_without_writing_back()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);

        // A delivery of the node (a re-read after the main panel's pick).
        rig.Deliver(ModeNode, 0);
        rig.Deliver(RangeNode, 5);
        rig.Deliver("B757_ADI_1_WXR_BUTTON", 1);
        rig.Deliver("B757_ADI_1_APRT_BUTTON", 0);
        Assert.Equal("VOR", Shown(w.Combo(ModeNode)));
        Assert.Equal("320 NM", Shown(w.Combo(RangeNode)));
        Assert.True(w.Check("B757_ADI_1_WXR_BUTTON").Checked);
        Assert.False(w.Check("B757_ADI_1_APRT_BUTTON").Checked);

        // The cockpit watch's report, by state var (the pilot turned the real knob).
        rig.Def.OnCockpitLvarsChanged(new Dictionary<string, double>
        {
            ["BBS_ND1_Range_Index"] = 1, ["B757_ADI_1_CTR_KNOB"] = 3, ["B757_ADI_1_NAV_AID_BUTTON"] = 1,
        });
        Assert.Equal("20 NM", Shown(w.Combo(RangeNode)));
        Assert.Equal("PLAN", Shown(w.Combo(ModeNode)));
        Assert.True(w.Check("B757_ADI_1_NAV-AID_BUTTON").Checked);

        Assert.Empty(rig.Io.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Choosing_a_range_or_mode_writes_its_code_once_through_the_panels_path_and_says_nothing()
    {
        foreach (var (node, from, to) in new[] { (RangeNode, 2, 3), (RangeNode, 2, 0), (ModeNode, 2, 0), (ModeNode, 2, 3) })
        {
            using var rig = new Rig(started: true);
            rig.Io.Cockpit[node] = from;
            var w = OpenControls(rig);
            var control = rig.Def.ControlFor(node)!;
            var expected = Bbs757Write.ToPosition(control, from, to).Calls;
            Assert.NotEmpty(expected);
            rig.Io.AfterWrite = () => rig.Io.Cockpit[node] = to;

            w.Combo(node).SelectedIndex = to;

            Assert.Equal(expected, rig.Io.Writes);
            Assert.Equal(control.Positions[to].Label, Shown(w.Combo(node)));
            Assert.Empty(rig.Speech.All);   // NVDA read the new value; the panel's pick says nothing more (CORE-7)
            Assert.Contains(node, rig.Io.ReadsAsked);   // the panel's re-read after the write
        }
    }

    [Fact]
    public void A_check_box_toggles_its_node()
    {
        foreach (var (node, _) in Toggles)
        {
            using var rig = new Rig(started: true);
            var w = OpenControls(rig);
            var box = w.Check(node);
            var control = rig.Def.ControlFor(node)!;
            double from = rig.Io.Cockpit[node], to = from > 0.5 ? 0 : 1;
            rig.Io.AfterWrite = () => rig.Io.Cockpit[node] = to;

            box.Checked = !box.Checked;

            Assert.Equal(Bbs757Write.ToPosition(control, from, to).Calls, rig.Io.Writes);
            Assert.Equal(new[] { control.Press }, rig.Io.Writes);
            Assert.Equal(to > 0.5, box.Checked);
            Assert.Empty(rig.Speech.All);
        }
    }

    [Fact]
    public void Ctr_and_tfc_press_as_the_panel_does()
    {
        foreach (var node in new[] { CtrNode, TfcNode })
        {
            using var rig = new Rig(started: true);
            var w = OpenControls(rig);
            var control = rig.Def.ControlFor(node)!;
            var press = Bbs757Write.PressFor(control)!;

            w.Button(node).PerformClick();

            Assert.Equal(new[] { press.Down, press.Up }, rig.Io.Writes);
            Assert.Equal(new[] { $"{control.Label} pressed" }, rig.Speech.All);
        }
    }

    [Fact]
    public void With_writes_unable_to_land_each_control_says_unavailable_and_shows_the_cockpit_again()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);
        rig.Io.CanWrite = false;

        w.Combo(RangeNode).SelectedIndex = 5;
        w.Combo(ModeNode).SelectedIndex = 0;
        w.Check("B757_ADI_1_WXR_BUTTON").Checked = true;
        w.Button(CtrNode).PerformClick();

        Assert.Empty(rig.Io.Writes);
        Assert.Equal(new[] { "ND Range unavailable", "ND Mode unavailable", "WXR unavailable", "CTR unavailable" }, rig.Speech.All);
        Assert.Equal("40 NM", Shown(w.Combo(RangeNode)));
        Assert.Equal("MAP", Shown(w.Combo(ModeNode)));
        Assert.False(w.Check("B757_ADI_1_WXR_BUTTON").Checked);
    }

    [Fact]
    public void A_node_missing_from_the_map_leaves_its_control_out_and_the_rest_work()
    {
        var full = Bbs757ControlMap.Load();
        var map = new Bbs757ControlMap
        {
            Controls = full.Controls.Where(c => c.Node is not (RangeNode or "B757_ADI_1_WXR_BUTTON")).ToList(),
            Status = full.Status,
        };
        using var rig = new Rig(started: true, map);

        var w = OpenControls(rig);

        Assert.Equal(Order.Where(n => n is not (RangeNode or "B757_ADI_1_WXR_BUTTON")), w.Controls.Select(c => c.Name));
        Assert.Equal(new[] { "&Mode" }, w.Panel.Controls.OfType<Label>().Select(l => l.Text));
        var all = w.Panel.Controls.Cast<Control>().ToList();
        Assert.Equal(Enumerable.Range(0, all.Count), all.Select(c => c.TabIndex));

        rig.Io.AfterWrite = () => rig.Io.Cockpit[ModeNode] = 3;
        w.Combo(ModeNode).SelectedIndex = 3;
        Assert.Equal(Bbs757Write.ToPosition(rig.Def.ControlFor(ModeNode)!, 2, 3).Calls, rig.Io.Writes);
        rig.Deliver("B757_ADI_1_TERR_BUTTON", 0);
        Assert.False(w.Check("B757_ADI_1_TERR_BUTTON").Checked);
    }

    [Fact]
    public void The_ehsi_monitor_says_nothing_for_these_changes()
    {
        using var rig = new Rig(started: false);
        var f = rig.Lit(Ehsi());
        var w = OpenControls(rig);
        rig.Io.AfterWrite = () => rig.Io.Cockpit[RangeNode] = 3;

        w.Combo(RangeNode).SelectedIndex = 3;
        w.Check("B757_ADI_1_WXR_BUTTON").Checked = true;
        f.Show(Ehsi() with { Range = 80 });
        f.Tick(4);
        rig.Deliver(RangeNode, 3);

        Assert.Empty(f.Env.Spoken);
        Assert.Empty(rig.Speech.All);
        Assert.Contains("EHSI: MAP, range 80", f.Monitor.View());   // shown, not spoken
    }

    [Fact]
    public void A_second_Alt_N_reuses_the_window_and_its_controls_act_on_the_new_context()
    {
        using var rig = new Rig(started: true);
        var first = OpenControls(rig);
        first.Form.Hide();
        var later = new SpeechCapture();

        var again = OpenControls(rig, later);

        Assert.Same(first.Form, again.Form);
        Assert.Equal(Order, again.Controls.Select(c => c.Name));   // one set of controls
        again.Button(CtrNode).PerformClick();
        rig.Io.CanWrite = false;
        again.Combo(RangeNode).SelectedIndex = 0;

        Assert.Empty(rig.Speech.All);
        Assert.Equal(new[] { "CTR pressed", "ND Range unavailable" }, later.All);
    }

    // ---- Fix round 1: the follow hooks never cost another line ----

    private static FieldInfo PrivateField(string name)
    {
        var field = typeof(BBS757Definition).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(field is not null, $"BBS757Definition has no private field {name}: this test sets it; follow its rename");
        return field;
    }

    [Fact]
    public void A_control_that_throws_while_following_leaves_the_variables_processing_intact()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);
        bool expected;
        using (var plain = new Rig(started: true)) expected = plain.Def.ProcessSimVarUpdate(RangeNode, 4, plain.Speech);
        w.Combo(RangeNode).SelectedIndexChanged += (_, _) => throw new InvalidOperationException("injected");

        bool handled = true;
        var thrown = Record.Exception(() => handled = rig.Def.ProcessSimVarUpdate(RangeNode, 4, rig.Speech));

        Assert.Null(thrown);
        Assert.Equal(expected, handled);
        // The guard was let go: the next delivery shows, writing nothing, and a pilot's change still writes.
        rig.Deliver("B757_ADI_1_WXR_BUTTON", 1);
        Assert.True(w.Check("B757_ADI_1_WXR_BUTTON").Checked);
        Assert.Empty(rig.Io.Writes);
        w.Check("B757_ADI_1_DATA_BUTTON").Checked = true;
        Assert.Single(rig.Io.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_control_that_throws_while_following_leaves_the_cockpit_watchs_announcements_intact()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);
        var (lights, switches) = rig.Def.CockpitWatched();
        var other = switches.First(s => s.Panel != "Captain EFIS" && s.Positions.Count >= 2);
        long now = 0;
        var cockpit = new Bbs757CockpitAnnouncements(lights, switches, () => now);
        cockpit.Settle();
        now = Bbs757CockpitAnnouncements.SettleQuietMs + 1;
        PrivateField("_cockpit").SetValue(rig.Def, cockpit);
        PrivateField("_cockpitAnnouncer").SetValue(rig.Def, rig.Speech);
        w.Combo(RangeNode).SelectedIndexChanged += (_, _) => throw new InvalidOperationException("injected");

        var thrown = Record.Exception(() => rig.Def.OnCockpitLvarsChanged(new Dictionary<string, double>
        {
            ["BBS_ND1_Range_Index"] = 4, [other.StateVar!] = other.Positions[1].Value,
        }));

        Assert.Null(thrown);
        Assert.Contains($"{other.Label} {other.Positions[1].Label}", rig.Speech.All);
    }

    [Fact]
    public void A_change_with_no_variable_for_its_node_sends_nothing_and_shows_the_cockpit_again()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);
        Assert.True(rig.Def.GetVariables().Remove(RangeNode));

        w.Combo(RangeNode).SelectedIndex = 5;

        Assert.Empty(rig.Io.Writes);
        Assert.Equal("40 NM", Shown(w.Combo(RangeNode)));
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_change_with_no_alt_n_context_sends_nothing_and_shows_the_cockpit_again()
    {
        using var rig = new Rig(started: true);
        var w = OpenControls(rig);
        PrivateField("_ehsiPress").SetValue(rig.Def, null);

        w.Check("B757_ADI_1_WXR_BUTTON").Checked = true;
        w.Combo(ModeNode).SelectedIndex = 0;

        Assert.Empty(rig.Io.Writes);
        Assert.False(w.Check("B757_ADI_1_WXR_BUTTON").Checked);
        Assert.Equal("MAP", Shown(w.Combo(ModeNode)));
        Assert.Empty(rig.Speech.All);
    }

    // ---- the rig ----

    /// <summary>The panel writes' simulator access: a cockpit of values the reads answer from, every write recorded, and a
    /// re-read delivered back through the definition as SimConnect delivers it.</summary>
    private sealed class FakeIo(Rig rig)
    {
        public bool CanWrite = true;
        public readonly List<string> Writes = new();
        public readonly List<string> ReadsAsked = new();
        public Action? AfterWrite;

        public readonly Dictionary<string, double> Cockpit = new(StringComparer.Ordinal)
        {
            [ModeNode] = 2, [RangeNode] = 2,
            ["B757_ADI_1_WXR_BUTTON"] = 0, ["B757_ADI_1_TERR_BUTTON"] = 1, ["B757_ADI_1_NAV-AID_BUTTON"] = 0,
            ["B757_ADI_1_APRT_BUTTON"] = 1, ["B757_ADI_1_DATA_BUTTON"] = 0, ["B757_ADI_1_WPT_BUTTON"] = 0,
        };

        private double? Value(string key) => Cockpit.TryGetValue(key, out var v) ? v : null;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, _) => Task.FromResult(Value(key)),
            Cached = Value,
            Write = rpn => { Writes.Add(rpn); AfterWrite?.Invoke(); },
            RequestRead = key =>
            {
                ReadsAsked.Add(key);
                if (Value(key) is double v) rig.Deliver(key, v);
            },
            Delay = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        };
    }

    private sealed class Rig : IDisposable
    {
        public BBS757Definition Def { get; private set; }
        public SpeechCapture Speech { get; } = new();
        public List<HotkeyAction> AiReads { get; } = new();
        public HotkeyManager Hotkeys { get; } = new();
        public FakeIo Io { get; }
        private readonly SimConnectManager _sim = new(IntPtr.Zero);
        private readonly Form _parent = new();
        private readonly bool _started;
        private readonly Bbs757ControlMap _map;

        public Rig(bool started, Bbs757ControlMap? map = null)
        {
            _started = started;
            _map = map ?? Bbs757ControlMap.Load();
            Io = new FakeIo(this);
            Def = Make(null, null);
            if (started) Def.StartDisplays(new NoFiles(), new NoLayouts(), _sim, Speech);
            else _ = Def.Displays;
        }

        /// <summary>A SimConnect delivery of <paramref name="key"/>, as MainForm hands it to the definition.</summary>
        public void Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);

        private BBS757Definition Make(Bbs757EhsiDisplay? display, Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>? monitor)
        {
            var def = display is null
                ? new BBS757Definition(_map) { StartMonitor = StartWithoutTimer, NestRunning = () => true, WriteIo = _ => Io.Io }
                : new BBS757Definition(_map)
                {
                    StartMonitor = StartWithoutTimer, NestRunning = () => true, WriteIo = _ => Io.Io,
                    EhsiDisplay = display, Ehsi = monitor!,
                };
            def.AiReadOverride = a => { AiReads.Add(a); return true; };
            return def;
        }

        /// <summary>Replaces the definition's EHSI monitor with the real one over a scripted sampler, already running on
        /// <paramref name="first"/>.</summary>
        public Flight Lit(Bbs757EhsiReading first, bool muted = false)
        {
            Def.OnSwitchedAway();
            var flight = new Flight(first, muted);
            Def = Make(flight.Display, flight.Monitor);
            _ = Def.Displays;
            flight.RunUntilRunning();
            return flight;
        }

        public bool Press(HotkeyAction action, SpeechCapture? speech = null) =>
            Def.HandleHotkeyAction(action, _sim, speech ?? Speech, _parent, Hotkeys);

        public void Dispose()
        {
            Def.OnSwitchedAway();
            _parent.Dispose();
            Hotkeys.Dispose();
        }
    }

    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        switch (monitor)
        {
            case Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma: fma.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> eicas: eicas.Start(env, startTimer: false); break;
            case Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> ehsi: ehsi.Start(env, startTimer: false); break;
            default: monitor.Start(env); break;
        }
    }

    private static readonly Bbs757EhsiLayout Layout = Bbs757EhsiLayoutTests.Sample();

    /// <summary>The real EHSI display and monitor over a scripted sampler (whatever the module), ticked every 500 ms.</summary>
    private sealed class Flight
    {
        public Flight(Bbs757EhsiReading first, bool muted)
        {
            Sampler = new Sampler(first);
            Script = new OverScript(Display, Sampler);
            Monitor = new Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>(Script);
            Env = new Bbs757DisplayMonitorTests.FakeEnvironment
            {
                Simulator = new Bbs757SimValues(new Dictionary<string, double>
                {
                    ["PLANE LATITUDE"] = 47.442946, ["PLANE LONGITUDE"] = -122.303648,
                }),
                Analyse = () => Task.FromResult(new ModuleAnalysis(new[]
                {
                    new ModuleBuild(Bbs757Module.Description, "BBS752.wasm", "BBS752.json",
                        new[] { new ReaderResult(Bbs757Module.Ehsi, Layout, null) }),
                }, null)),
            };
            if (muted) Env.Muted.Add(Bbs757EhsiDisplay.MuteKey);
            Monitor.Start(Env, startTimer: false);
        }

        public Bbs757EhsiDisplay Display { get; } = new();
        public Sampler Sampler { get; }
        public OverScript Script { get; }
        public Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> Monitor { get; }
        public Bbs757DisplayMonitorTests.FakeEnvironment Env { get; }

        public void Show(Bbs757EhsiReading reading) => Sampler.Reading = reading;

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Bbs757EhsiDisplay.TickMs;
            }
        }

        public void RunUntilRunning(int maxTicks = 20)
        {
            for (int i = 0; i < maxTicks && Monitor.StateName != "Running"; i++) Tick();
            Assert.Equal("Running", Monitor.StateName);
        }
    }

    private sealed class Sampler(Bbs757EhsiReading reading) : IBbs757DisplaySampler<Bbs757EhsiReading>
    {
        public Bbs757EhsiReading Reading { get; set; } = reading;

        /// <summary>When set, each read's reading instead of <see cref="Reading"/>.</summary>
        public Func<Bbs757EhsiReading>? Script { get; set; }

        public bool TryRead(Bbs757SimValues sim, out Bbs757EhsiReading read) => TryRead(out read);

        public bool TryRead(out Bbs757EhsiReading read)
        {
            read = Script?.Invoke() ?? Reading;
            return true;
        }

        public void Dispose()
        {
        }
    }

    private sealed class OverScript(Bbs757EhsiDisplay real, Sampler sampler) : IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading>
    {
        private readonly IBbs757DisplayReader<Bbs757EhsiLayout, Bbs757EhsiReading> _real = real;

        /// <summary>The display's <see cref="Bbs757EhsiDisplay.FullWanted"/> at each time the rows were built.</summary>
        public List<bool> RowsWanted { get; } = new();

        /// <summary>The first rows built.</summary>
        public IReadOnlyList<string>? FirstRows { get; private set; }

        public string Name => _real.Name;
        public string MuteKey => _real.MuteKey;
        public string LogChannel => _real.LogChannel;
        public int TickMs => _real.TickMs;
        public IReadOnlyList<Bbs757SimInput> SimInputs => _real.SimInputs;
        public IReadOnlyList<Bbs757EhsiLayout> LayoutsIn(ModuleAnalysis analysis) => _real.LayoutsIn(analysis);
        public IBbs757DisplaySampler<Bbs757EhsiReading>? SamplerFor(LocatedModule module, Bbs757EhsiLayout layout) => sampler;
        public bool? Lit(Bbs757EhsiReading reading) => _real.Lit(reading);

        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix) =>
            _real.Changes(heard, now, withPrefix);

        public IReadOnlyList<string> Rows(Bbs757EhsiReading reading)
        {
            RowsWanted.Add(real.FullWanted());
            var rows = _real.Rows(reading);
            FirstRows ??= rows;
            return rows;
        }
    }

    private sealed class NoFiles : IModuleHost
    {
        public long NowMs => 0;
        public string SimulatorVersion => "FS2024";
        public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log) => Array.Empty<string>();
        public byte[] ReadFile(string path) => throw new InvalidOperationException($"the session read {path}");

        public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
        {
            failure = null;
            return null;
        }

        public void Log(string line) { }

        public Task<T> RunInBackground<T>(string name, Func<T> work)
        {
            try { return Task.FromResult(work()); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }
    }

    private sealed class NoLayouts : ILayoutSource
    {
        public LayoutLookup Find(string wasmFile) => new(null, "no layout");
    }
}
