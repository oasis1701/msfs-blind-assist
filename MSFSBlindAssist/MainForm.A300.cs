using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// iniBuilds A300 windows and menu entry: the Ctrl+M monitor manager. Kept in its own partial like
/// the MD-11's.
/// </summary>
public partial class MainForm
{
    private Forms.A300.A300MonitorManagerForm? a300MonitorManagerForm;
    private Forms.A300.A300McduForm? a300McduForm;
    private SimConnect.CoherentPmdgEfbClient? coherentA300Efb;
    private Forms.FBWA380.FbwEfbForm? a300EfbForm;

    /// <summary>
    /// The A300's monitor manager (Ctrl+M). Guarded on the loaded aircraft because the definition
    /// calls it: a queued call that runs after a switch must not build an "A300" manager over
    /// another aircraft's variables.
    /// </summary>
    public void ShowA300MonitorManagerDialog()
    {
        if (currentAircraft is not IniA300Definition a300) return;
        hotkeyManager.ExitOutputHotkeyMode();
        if (a300MonitorManagerForm == null || a300MonitorManagerForm.IsDisposed)
            a300MonitorManagerForm = new Forms.A300.A300MonitorManagerForm(a300.GetVariables());
        a300MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// The surroundings window's COM 1 tune (<c>TuneCom1FromSurroundings</c>) sends the stock standby-set and swap events,
    /// which the A300 mirrors into VHF 1's windows. The window's own read-back says what COM 1 holds, so the A300's
    /// call-out for a VHF change MSFSBA did not make stays quiet for the tune ([A300-29]). Nothing on any other aircraft.
    /// </summary>
    private void NoteA300OwnCom1Tune() => (currentAircraft as IniA300Definition)?.NoteOwnCom1Tune();

    /// <summary>The A300's two MCDUs (Shift+M). Guarded on the loaded aircraft like the monitor manager.</summary>
    public void ShowA300McduDialog()
    {
        if (currentAircraft is not IniA300Definition a300) return;
        hotkeyManager.ExitOutputHotkeyMode();
        if (a300McduForm == null || a300McduForm.IsDisposed)
            a300McduForm = new Forms.A300.A300McduForm(a300, simConnectManager, announcer);
        a300McduForm.ShowForm();
    }

    /// <summary>
    /// The A300 tablet (Shift+T), read through the Coherent debugger by the shared tablet window, as
    /// the MD-11's is. The scrape runs only while the window is visible; the socket stays warm.
    /// </summary>
    public void ShowA300EfbDialog()
    {
        if (currentAircraft is not IniA300Definition) return;
        hotkeyManager.ExitInputHotkeyMode();
        if (coherentA300Efb == null) { coherentA300Efb = SimConnect.CoherentPmdgEfbClient.ForA300(); coherentA300Efb.Start(); }
        if (a300EfbForm == null || a300EfbForm.IsDisposed)
        {
            a300EfbForm = new Forms.FBWA380.FbwEfbForm(coherentA300Efb, announcer, "A300 Tablet", "Tablet", "Tablet");
            var f = a300EfbForm;
            f.VisibleChanged += (_, _) => coherentA300Efb?.SetActive(!f.IsDisposed && f.Visible);
        }
        coherentA300Efb.SetActive(true);
        a300EfbForm.ShowForm();
    }

    /// <summary>Disposes the A300 windows: their rows and keys belong to one definition, and the tablet
    /// client holds the ONE inspector socket Coherent allows for its view.</summary>
    private void DisposeA300Windows()
    {
        if (a300MonitorManagerForm != null && !a300MonitorManagerForm.IsDisposed) a300MonitorManagerForm.Dispose();
        a300MonitorManagerForm = null;
        if (a300McduForm != null && !a300McduForm.IsDisposed) a300McduForm.Dispose();
        a300McduForm = null;
        if (a300EfbForm != null && !a300EfbForm.IsDisposed) a300EfbForm.Dispose();
        a300EfbForm = null;
        if (coherentA300Efb != null)
        {
            coherentA300Efb.Stop();
            coherentA300Efb.Dispose();
            coherentA300Efb = null;
        }
    }

    private void IniA300MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA300Definition());
    }
}
