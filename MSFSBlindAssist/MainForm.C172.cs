// MSFSBlindAssist/MainForm.C172.cs
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// Stock Cessna 172 (G1000) menu entry and windows. Its own partial, like the MD-11's, so the
/// aircraft's surfaces sit together rather than being scattered through MainForm.
/// </summary>
public partial class MainForm
{
    private void Cessna172MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new Cessna172Definition());
    }

    /// <summary>
    /// The C172's monitor manager (Ctrl+M). Rebuilt from the live definition each time it is
    /// created. The aircraft guard is the MD-11's: this is public and reached from the definition,
    /// so a queued invocation running after SwitchAircraft has replaced the definition must not
    /// build a "C172 monitor manager" over whatever aircraft is loaded now.
    /// </summary>
    public void ShowC172MonitorManagerDialog()
    {
        if (currentAircraft is not Cessna172Definition c172) return;
        hotkeyManager.ExitOutputHotkeyMode();
        if (c172MonitorManagerForm == null || c172MonitorManagerForm.IsDisposed)
            c172MonitorManagerForm = new Forms.C172.Cessna172MonitorManagerForm(c172.GetVariables());
        c172MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// Disposed on swap for the stale-snapshot reason every sibling is: its rows are built from
    /// the definition's variables at construction, so a surviving instance lists the OUTGOING
    /// definition.
    /// </summary>
    private void DisposeC172Forms()
    {
        if (c172MonitorManagerForm != null && !c172MonitorManagerForm.IsDisposed) c172MonitorManagerForm.Dispose();
        c172MonitorManagerForm = null;
    }
}
