using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 26's seam (S16): the 757's Captain EHSI read from BBS752.wasm's memory. It registers the monitor (the display's two
/// position inputs come from it through the registry, so the definition streams each once, hidden where nothing carries it
/// already, [VAR-7]), the "EHSI messages" Ctrl+M row, Alt+N, which opens the one "Captain EHSI" window while the reader can
/// run and falls back to the AI read of the screen when it cannot (S15), and Ctrl+W, which says the active waypoint at once.
/// Opening the window says nothing itself: its title and list are what the screen reader says (CORE-7). Below the list
/// the window holds the Captain EFIS controls (<see cref="EhsiControls"/>), which show the cockpit and write as the
/// main panel does; the reader still speaks nothing for a mode, range or switch change.
/// </summary>
public partial class BBS757Definition
{
    private Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>? _ehsi;

    // EhsiDisplay and Ehsi are internal init so a test can supply a scripted monitor (over its own display) in place of the
    // real ones; RegisterEhsi sets FullWanted on whichever display is injected, so the injected one follows the window too.

    /// <summary>The EHSI's display; its <see cref="Bbs757EhsiDisplay.FullWanted"/> is whether the window is opening or
    /// showing.</summary>
    internal Bbs757EhsiDisplay EhsiDisplay { get; init; } = new();

    /// <summary>The EHSI's monitor, on <see cref="EhsiDisplay"/>; a test may start it on a scripted sampler.</summary>
    internal Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> Ehsi
    {
        get => _ehsi ??= new(EhsiDisplay);
        init => _ehsi = value;
    }

    // Made on the first Alt+N that finds the reader running; owned by the registry, so a switch away closes it for good.
    private BBS757DisplayForm? _ehsiWindow;

    // True from the moment Alt+N opens the window until it is shown: the window's first rows are built before it is
    // visible, and they must come from a full reading.
    private bool _ehsiOpening;

    /// <summary>The sim and announcer of the latest Alt+N; the window's EFIS controls write through them.</summary>
    private (SimConnectManager Sim, ScreenReaderAnnouncer Announcer)? _ehsiPress;

    /// <summary>Each positional EFIS control the window holds, by node: what shows a cockpit value on it.</summary>
    private Dictionary<string, Action<double>>? _ehsiFollowers;

    /// <summary>The same controls by state var, the name the cockpit watch reports a change under.</summary>
    private Dictionary<string, string>? _ehsiNodeByStateVar;

    // True while a control is set to the cockpit's value: its change handler must not write that value back.
    private bool _ehsiFollowing;

    /// <summary>The Captain EFIS drop-downs, with their labels' text: mode, then range.</summary>
    private static readonly (string Node, string Caption)[] EhsiSelectors =
    {
        ("B757_ADI_1_CTR_KNOB", "&Mode"),
        ("B757_ADI_1_TFC_KNOB", "&Range"),
    };

    /// <summary>The Captain EFIS momentary buttons.</summary>
    private static readonly (string Node, string Caption)[] EhsiButtons =
    {
        ("B757_ADI_1_CTR_BUTTON", "&CTR"),
        ("B757_ADI_1_TFC_BUTTON", "&TFC"),
    };

    /// <summary>The Captain EFIS map switches, each with a mnemonic no other control in the window takes.</summary>
    private static readonly (string Node, string Caption)[] EhsiToggles =
    {
        ("B757_ADI_1_WXR_BUTTON", "&WXR"),
        ("B757_ADI_1_TERR_BUTTON", "T&ERR"),
        ("B757_ADI_1_NAV-AID_BUTTON", "&NAV AID"),
        ("B757_ADI_1_APRT_BUTTON", "&APRT"),
        ("B757_ADI_1_DATA_BUTTON", "&DATA"),
        ("B757_ADI_1_WPT_BUTTON", "W&PT"),
    };

    /// <summary>
    /// The Captain EFIS controls that change what the EHSI shows, under the window's list: the mode and range
    /// drop-downs, CTR and TFC, and the six map switches as check boxes. Each control shows the cockpit, not a fixed
    /// caption: <see cref="ShowEhsiCockpitState"/> on every Alt+N, then every delivery of its node
    /// (<see cref="FollowEhsiControl"/>) and every change the cockpit watch reports (<see cref="FollowEhsiCockpit"/>).
    /// A pilot's change writes through the main panel's own entry, <see cref="HandleUIVariableSet"/>, so it settles,
    /// refuses aloud, marks its own write and reads back exactly as the panel does, and says nothing NVDA has not
    /// said (CORE-7). A node missing from the map leaves its control out with a warning.
    /// </summary>
    private FlowLayoutPanel EhsiControls()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            MaximumSize = new Size(520, 0),   // the list's width: ten controls wrap onto rows below it
            Name = "EHSI controls",
            AccessibleName = "EHSI controls",
        };
        var followers = new Dictionary<string, Action<double>>(StringComparer.Ordinal);
        var byStateVar = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(Control c)
        {
            c.TabIndex = panel.Controls.Count;
            panel.Controls.Add(c);
        }
        Bbs757Control? Find(string node)
        {
            if (_byNode.TryGetValue(node, out var control)) return control;
            Log.Warn("BBS757", $"EHSI window: {node} is not in the control map; its control is left out.");
            return null;
        }
        void Follows(Bbs757Control control, Action<double> show)
        {
            followers[control.Node] = show;
            if (!string.IsNullOrWhiteSpace(control.StateVar)) byStateVar[control.StateVar!] = control.Node;
        }

        foreach (var (node, caption) in EhsiSelectors)
        {
            if (Find(node) is not { } control) continue;
            string name = caption.Replace("&", "", StringComparison.Ordinal);
            Add(new Label { Text = caption, AutoSize = true, Name = node + " label", Margin = new Padding(3, 7, 0, 3) });
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = node, AccessibleName = name, Width = 90 };
            foreach (var p in control.Positions) combo.Items.Add(p.Label);
            // Every step NVDA's arrows commit is handed on, as the panel's combo does: the panel's pick settles
            // (PickSettleMs) and writes only the last.
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_ehsiFollowing || combo.SelectedIndex < 0) return;
                SetFromEhsiWindow(control, control.Positions[combo.SelectedIndex].Value);
            };
            Add(combo);
            Follows(control, value =>
            {
                int i = Bbs757Rpn.PositionIndex(control, value);
                if (i >= 0 && combo.SelectedIndex != i) combo.SelectedIndex = i;
            });
        }

        foreach (var (node, caption) in EhsiButtons)
        {
            if (Find(node) is not { } control) continue;
            var button = new Button { Text = caption, AutoSize = true, Name = node };
            button.Click += (_, _) => SetFromEhsiWindow(control, 1);
            Add(button);
        }

        foreach (var (node, caption) in EhsiToggles)
        {
            if (Find(node) is not { } control) continue;
            if (control.Positions.Count != 2)
            {
                Log.Warn("BBS757", $"EHSI window: {node} has {control.Positions.Count} positions, not off and on; its check box is left out.");
                continue;
            }
            var box = new CheckBox { Text = caption, AutoSize = true, Name = node };
            box.CheckedChanged += (_, _) =>
            {
                if (_ehsiFollowing) return;
                SetFromEhsiWindow(control, control.Positions[box.Checked ? 1 : 0].Value);
            };
            Add(box);
            Follows(control, value =>
            {
                int i = Bbs757Rpn.PositionIndex(control, value);
                if (i >= 0) box.Checked = i == 1;
            });
        }

        _ehsiFollowers = followers;
        _ehsiNodeByStateVar = byStateVar;
        return panel;
    }

    /// <summary>A window control's change, handed to the main panel's own write entry for its node (with the latest
    /// Alt+N's sim and announcer), so nothing about it differs from the same pick on the panel. When it cannot be
    /// handed on (no Alt+N context, or a node with no variable), nothing is sent, a warning is logged, and the control
    /// goes back to the cockpit's state as a refused pick's does, so it never shows a state the cockpit does not have.</summary>
    private void SetFromEhsiWindow(Bbs757Control control, double value)
    {
        if (_ehsiPress is { } now && GetVariables().TryGetValue(control.Node, out var def))
        {
            HandleUIVariableSet(control.Node, value, def, now.Sim, now.Announcer);
            return;
        }
        Log.Warn("BBS757", $"EHSI window: {control.Node} " +
            (_ehsiPress is null ? "has no Alt+N context" : "has no variable") + "; nothing sent.");
        if ((_ehsiPress?.Sim ?? Sim) is not { } sim) return;
        var io = WriteIoFor(sim);
        if (io.Cached(control.Node) is double cockpit) FollowEhsiControl(control.Node, cockpit);
        io.RequestRead(control.Node);
    }

    /// <summary>
    /// Alt+N: every control shows the value SimConnect last delivered for its node, then each node is read again, as
    /// the panel reads its controls when it opens; the answers reach the controls through
    /// <see cref="FollowEhsiControl"/>. A control with a pick in flight is left as the pilot set it.
    /// </summary>
    private void ShowEhsiCockpitState(SimConnectManager sim)
    {
        if (_ehsiFollowers is not { } followers) return;
        var io = WriteIoFor(sim);
        foreach (var node in followers.Keys.ToList())
        {
            if (IsPickPending(node)) continue;
            if (io.Cached(node) is double value) FollowEhsiControl(node, value);
            io.RequestRead(node);
        }
    }

    /// <summary>
    /// A delivery of a node the window holds (ProcessSimVarUpdate calls this after it has consumed any delivery a
    /// pick in flight owns): the control shows it, without writing it back and without moving focus. Nothing is
    /// spoken here; a focused control's new value is NVDA's to read. Never throws: it runs inside ProcessSimVarUpdate
    /// and the cockpit watch's report, so a control that fails is logged and the variable's own processing, or the
    /// report's announcements, go on.
    /// </summary>
    private void FollowEhsiControl(string node, double value)
    {
        if (_ehsiFollowers is not { } followers || !followers.TryGetValue(node, out var show)) return;
        if (_ehsiWindow is not { IsDisposed: false }) return;
        _ehsiFollowing = true;
        try { show(value); }
        catch (Exception ex) { Log.Warn("BBS757", $"EHSI window: {node} did not show the cockpit's value: {ex.Message}"); }
        finally { _ehsiFollowing = false; }
    }

    /// <summary>
    /// The cockpit watch's changes, by state var (the pilot turned the real knob, pressed a switch, or a preset moved
    /// it): each control the window holds shows its new value, unless a pick of it is in flight (its value then is
    /// the pick's to settle). Never throws: plan 13's announcements of the same report come after it.
    /// </summary>
    private void FollowEhsiCockpit(IReadOnlyDictionary<string, double> changes)
    {
        try
        {
            if (_ehsiNodeByStateVar is not { } nodes) return;
            foreach (var (name, value) in changes)
            {
                if (nodes.TryGetValue(name, out var node) && !IsPickPending(node)) FollowEhsiControl(node, value);
            }
        }
        catch (Exception ex) { Log.Warn("BBS757", $"EHSI window: the cockpit watch's report was not shown: {ex.Message}"); }
    }

    /// <summary>
    /// What Ctrl+W says: the monitor's latest reading, whether or not the window is open. While there is none the line
    /// says why: "display dark" while the screen is dark, else the monitor's own reason (its status line's words), and
    /// "no reading yet" only when it has none to give (<see cref="Bbs757DisplayMonitor{TLayout, TReading}.NoReadingReason"/>).
    /// </summary>
    internal string EhsiWaypointLine()
    {
        var latest = Ehsi.Latest;
        return EhsiDisplay.WaypointLine(latest, latest is null ? Ehsi.NoReadingReason : null);
    }

    partial void RegisterEhsi(Bbs757DisplayRegistry r)
    {
        // The route is read from the moment Alt+N is pressed and while the window shows; a window hidden (Escape, a
        // close) or disposed reads light again at once.
        EhsiDisplay.FullWanted = () => _ehsiOpening || _ehsiWindow is { IsDisposed: false, Visible: true };
        r.Add(Ehsi);
        r.AddMonitorRow(new MonitorRow(Bbs757EhsiDisplay.MuteKey, "EHSI messages"));
        r.OnKey(HotkeyAction.ReadDisplayND, context =>
        {
            if (Ehsi.KeyFallsBackToAi()) return context.AiRead();
            context.Hotkeys.ExitOutputHotkeyMode();
            _ehsiPress = (context.Sim, context.Announcer);   // a window shown again may come with a new context
            _ehsiOpening = true;
            try
            {
                // One reading now, full, so the window's first rows already list the map instead of a second later.
                Ehsi.Tick();
                if (_ehsiWindow is not { IsDisposed: false } window)
                {
                    window = _ehsiWindow = new BBS757DisplayForm(Bbs757EhsiDisplay.Name, "Captain EHSI", Ehsi.View, EhsiControls());
                    r.OwnWindow(window);
                }
                ShowEhsiCockpitState(context.Sim);
                window.ShowForm();
            }
            finally
            {
                _ehsiOpening = false;
            }
            return true;
        });
        // A key press is always answered, so no Ctrl+M row mutes it; it interrupts, as a pilot's own request does.
        r.OnKey(HotkeyAction.ReadNDWaypoint, context =>
        {
            context.Announcer.AnnounceImmediate(EhsiWaypointLine());
            return true;
        });
    }
}
