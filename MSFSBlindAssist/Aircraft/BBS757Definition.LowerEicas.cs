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
/// Plan 25's seam (S16): the 757's lower EICAS read from BBS752.wasm's memory alone (it takes no simulator input). It
/// registers the monitor, the "EICAS status messages" Ctrl+M row, and Alt+S, which opens the one "Lower EICAS" window
/// while the reader can run and falls back to the AI read of the screen when it cannot (S15).
/// </summary>
public partial class BBS757Definition
{
    // Made on the first Alt+S that finds the reader running; owned by the registry, so a switch away closes it for good.
    private BBS757DisplayForm? _lowerEicasWindow;

    // BlueBird's ENGINE and STATUS display keys (the control map's "EICAS L Status" and "EICAS R Status"): the keys
    // beside the lower screen that change its page. A press lands as the panel's buttons do.
    internal const string EngineKeyNode = "B757_EICAS_L_STATUS_BUTTON";
    internal const string StatusKeyNode = "B757_EICAS_R_STATUS_BUTTON";

    /// <summary>
    /// The window's controls area: Engine then Status, each pressing BlueBird's key through the control map's own press.
    /// <paramref name="readerRuns"/> says whether the lower EICAS reader shows a reading right now.
    /// </summary>
    internal FlowLayoutPanel LowerEicasButtons(Func<bool> readerRuns, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, TabIndex = 1 };
        int tab = 0;
        foreach (var (caption, node) in new[] { ("Engine", EngineKeyNode), ("Status", StatusKeyNode) })
        {
            var button = new Button { Text = caption, AccessibleName = caption, AutoSize = true, TabIndex = tab++ };
            button.Click += (_, _) => _ = PressLowerEicasKeyAsync(node, readerRuns, sim, announcer);   // never throws
            panel.Controls.Add(button);
        }
        return panel;
    }

    private async Task PressLowerEicasKeyAsync(string node, Func<bool> readerRuns, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested || !_byNode.TryGetValue(node, out var control)) return;
            var io = WriteIoFor(sim);
            var press = Bbs757Write.PressFor(control);
            if (press == null || !io.CanWrite())
            {
                SayUnavailable(control, announcer);
                return;
            }
            // While the reader runs, its own "Lower EICAS: STATUS page" is the one confirmation (CORE-7); otherwise no page
            // change will be spoken, so the press says "<label> pressed" once, as the panel does.
            await PressAsync(control, press, io, announcer, speak: !readerRuns());
        }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"{node}: lower EICAS key press failed: {ex.Message}");
        }
    }

    partial void RegisterLowerEicas(Bbs757DisplayRegistry r)
    {
        var monitor = new Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>(new Bbs757LowerEicasDisplay());
        r.Add(monitor);
        r.AddMonitorRow(new MonitorRow(Bbs757LowerEicasDisplay.MuteKey, Bbs757LowerEicasDisplay.MonitorRowLabel));
        r.OnKey(HotkeyAction.ReadDisplayLowerECAM, ctx =>
        {
            if (monitor.KeyFallsBackToAi()) return ctx.AiRead();
            if (_lowerEicasWindow is not { IsDisposed: false } window)
            {
                window = _lowerEicasWindow = new BBS757DisplayForm(Bbs757LowerEicasDisplay.Name, Bbs757LowerEicasDisplay.Name,
                    monitor.View, LowerEicasButtons(() => monitor.RowsWhileRunning() != null, ctx.Sim, ctx.Announcer));
                r.OwnWindow(window);
            }
            window.ShowForm();
            return true;
        });
    }
}
