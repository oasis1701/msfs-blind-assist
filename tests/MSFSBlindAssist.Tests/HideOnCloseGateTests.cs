// Wiring characterization test for MonitorManagerShared.HideOnClose — the hide-on-close
// plumbing shared by the per-aircraft Ctrl+M monitor managers and (since PR #189) the MD-11
// MCDU (Shift+M) and Flight Control Panel (Ctrl+P) windows.
//
// Why this wiring exists: Application.Exit raises FormClosing on every member of
// Application.OpenForms, hidden forms included, and ABORTS the whole exit if any handler
// cancels. MainForm's own OnFormClosing teardown has already run by then (OpenForms insertion
// order — MainForm is first), so an unconditional cancel leaves the process alive but gutted,
// and the auto-updater — which waits only 10 s for the exe to exit — then extracts over a
// still-locked file. Both MD-11 windows used to wire an unconditional
// `FormClosing += (s, e) => { e.Cancel = true; Hide(); ... }` lambda with no CloseReason gate;
// they now call this shared HideOnClose instead.
//
// This test drives the REAL handler MonitorManagerShared.HideOnClose installs, through a
// minimal Form subclass that exposes OnFormClosing (protected virtual, so it can be invoked
// directly with no window handle and no message pump — Form.Close() on a handle-less form
// never actually raises FormClosing, which is why the probe calls OnFormClosing itself rather
// than Close()). It pins two things: (1) the three process-shutdown reasons pass through
// uncancelled with no Hide/onHide side effect, so Application.Exit can complete; and (2) every
// other reason cancels, hides the form, and — in that ORDER — invokes onHide only after Hide()
// has already run, which is what lets the MD-11 windows' onHide callback safely call
// SetForegroundWindow(previousWindow) to restore focus to whatever the pilot had before.
//
// What this does NOT pin: that Md11McduForm and Md11AutopilotWindow actually call
// MonitorManagerShared.HideOnClose instead of their old unconditional lambda. A compiled call
// site inside those forms' constructors isn't reachable from outside the assembly without
// creating a real window handle and pumping messages, which this xUnit host does not do; that
// half is verified only by the in-sim updater-restart step described in the PR body.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class HideOnCloseGateTests
{
    /// <summary>Exposes the protected OnFormClosing so a test can raise FormClosing without a
    /// window handle or a message pump — both of which Form.Close() requires.</summary>
    private sealed class Probe : Form
    {
        public FormClosingEventArgs Raise(CloseReason reason)
        {
            var e = new FormClosingEventArgs(reason, false);
            OnFormClosing(e);
            return e;
        }
    }

    [Theory]
    [InlineData(CloseReason.ApplicationExitCall)]   // Application.Exit — the updater's restart
    [InlineData(CloseReason.WindowsShutDown)]
    [InlineData(CloseReason.TaskManagerClosing)]
    public void Process_shutdown_reasons_pass_through_uncancelled(CloseReason reason)
    {
        using var probe = new Probe();
        var hides = 0;
        MonitorManagerShared.HideOnClose(probe, () => hides++);

        var e = probe.Raise(reason);

        Assert.False(e.Cancel);
        Assert.Equal(0, hides);
    }

    [Theory]
    [InlineData(CloseReason.UserClosing)]   // Escape, the &Close button, Alt+F4, the title-bar X
    [InlineData(CloseReason.None)]
    public void Window_dismissals_cancel_and_hide_before_onHide_runs(CloseReason reason)
    {
        using var probe = new Probe();
        var hides = 0;
        bool? visibleWhenOnHideRan = null;
        MonitorManagerShared.HideOnClose(probe, () =>
        {
            visibleWhenOnHideRan = probe.Visible;
            hides++;
        });

        var e = probe.Raise(reason);

        Assert.True(e.Cancel);
        Assert.False(probe.Visible);
        Assert.Equal(1, hides);
        // Hide() must run before onHide — the MD-11 windows' onHide calls SetForegroundWindow
        // to restore the previous window, which only makes sense once this one is gone.
        Assert.False(visibleWhenOnHideRan);
    }
}
