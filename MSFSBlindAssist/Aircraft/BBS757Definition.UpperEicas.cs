using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's upper EICAS (plan 24): its monitor (its simulator input comes from the reader through the registry, never
/// registered by hand), its three Ctrl+M rows, and Alt+E, which opens the "Upper EICAS" window or, when the reader cannot
/// run, falls back to the AI read (S15). The key speaks nothing itself: the window's title and list are what the screen
/// reader says (CORE-7).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The upper EICAS: spoken as its messages change, and the window Alt+E opens.</summary>
    internal Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> UpperEicas { get; } =
        new(new Bbs757UpperEicasDisplay());

    private BBS757DisplayForm? _upperEicasForm;

    /// <summary>The sim and announcer of the latest Alt+E; the window's buttons press through them.</summary>
    private (SimConnectManager Sim, ScreenReaderAnnouncer Announcer)? _upperEicasPress;

    /// <summary>
    /// The EICAS control keys that change what the upper EICAS shows, as the window's buttons: CANC and RECALL the
    /// messages, MAX IND RESET the N1/EGT max rows. A click presses as the panel does (<see cref="PressFromPanelAsync"/>:
    /// "{label} unavailable" when nothing can land, else "{label} pressed" once); nothing else is spoken (CORE-7).
    /// A node missing from the map leaves its button out with a warning.
    /// </summary>
    private FlowLayoutPanel UpperEicasButtons()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Name = "Upper EICAS controls",
            AccessibleName = "Upper EICAS controls",
        };
        foreach (var (node, caption) in new[]
        {
            ("B757_MAIN_CANCEL_BUTTON", "&Cancel"),
            ("B757_MAIN_RECALL_BUTTON", "&Recall"),
            ("B757_EICAS_MAX-IND_BUTTON", "&Max Ind"),
        })
        {
            if (!_byNode.TryGetValue(node, out var control))
            {
                Log.Warn("BBS757", $"Upper EICAS window: {node} is not in the control map; its button is left out.");
                continue;
            }
            var button = new Button { Text = caption, AutoSize = true, Name = node, TabIndex = panel.Controls.Count };
            button.Click += (_, _) =>
            {
                if (_upperEicasPress is not { } now) return;
                _ = PressFromPanelAsync(control, WriteIoFor(now.Sim), now.Announcer);   // never throws: it catches and logs
            };
            panel.Controls.Add(button);
        }
        return panel;
    }

    partial void RegisterUpperEicas(Bbs757DisplayRegistry r)
    {
        r.Add(UpperEicas);
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.MessagesKey, Bbs757UpperEicasDisplay.MessagesLabel));
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.ThrustKey, Bbs757UpperEicasDisplay.ThrustLabel));
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.ColorsKey, Bbs757UpperEicasDisplay.ColorsLabel));
        r.OnKey(HotkeyAction.ReadDisplayUpperECAM, context =>
        {
            if (UpperEicas.KeyFallsBackToAi()) return context.AiRead();
            context.Hotkeys.ExitOutputHotkeyMode();
            _upperEicasPress = (context.Sim, context.Announcer);   // a window shown again may come with a new context
            if (_upperEicasForm is not { IsDisposed: false } form)
            {
                form = _upperEicasForm = new BBS757DisplayForm("Upper EICAS", "Upper EICAS", UpperEicas.View, UpperEicasButtons());
                // The seam runs inside the first GetVariables, so the handler, not the seam, makes and owns the form.
                Displays.OwnWindow(form);
            }
            form.ShowForm();
            return true;
        });
    }
}
