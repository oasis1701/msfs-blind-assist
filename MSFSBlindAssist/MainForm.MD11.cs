using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist;

/// <summary>
/// TFDi MD-11 windows and menu entry. Kept in its own partial so the aircraft's
/// three surfaces — the MCDU text window, the Ctrl+M monitor manager and the EFB —
/// sit together rather than being scattered through MainForm.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// The MD-11's MCDU. The form polls SimConnectManager for the live client-data manager rather
    /// than being handed one, so opening this window before connecting to the sim is fine — it
    /// reports "not connected" and starts reading by itself once the data area registers.
    /// </summary>
    public void ShowMd11McduDialog()
    {
        if (currentAircraft is not TFDiMD11Definition md11) return;
        hotkeyManager.ExitInputHotkeyMode();
        if (md11McduForm == null || md11McduForm.IsDisposed)
            md11McduForm = new Forms.MD11.Md11McduForm(md11, simConnectManager, announcer);
        md11McduForm.ShowForm();
    }

    /// <summary>
    /// The MD-11's monitor manager (Ctrl+M) — mute individual auto-announced variables.
    /// Rebuilt from the live definition each time it is created, so it always lists what the
    /// aircraft actually announces.
    /// </summary>
    public void ShowMd11MonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (md11MonitorManagerForm == null || md11MonitorManagerForm.IsDisposed)
            md11MonitorManagerForm = new Forms.MD11.Md11MonitorManagerForm(currentAircraft.GetVariables());
        md11MonitorManagerForm.ShowForm();
    }

    /// <summary>
    /// The MD-11's EFB (Shift+T), through the shared FbwEfbForm over the Coherent debugger — the
    /// same transport as the PMDG tablet, just pointed at the MD-11's own view. No Community-folder
    /// bridge and no sim restart.
    /// </summary>
    public void ShowMd11EfbDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();

        if (coherentMd11Efb == null) { coherentMd11Efb = CoherentPmdgEfbClient.ForMd11(); coherentMd11Efb.Start(); }
        if (md11EfbForm == null || md11EfbForm.IsDisposed)
        {
            md11EfbForm = new Forms.FBWA380.FbwEfbForm(coherentMd11Efb, announcer, "MD-11 EFB", "EFB", "EFB");
            // Idle-gate the scrape to the window's visibility — the inspector socket and the
            // installed agent stay warm, but the DOM walk stops while the window is hidden.
            var f = md11EfbForm;
            f.VisibleChanged += (_, _) => coherentMd11Efb?.SetActive(!f.IsDisposed && f.Visible);
        }
        coherentMd11Efb.SetActive(true);   // covers a re-Show of an already-visible window
        md11EfbForm.ShowForm();
    }

    private void TFDiMD11MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new TFDiMD11Definition());
    }
}
