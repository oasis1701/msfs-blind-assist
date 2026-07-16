using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist;

// iFly 737 MAX8 support: the shared-memory SDK bridge (SDK field events → the normal
// SimVar pipeline), the CDU / EFB / monitor-manager dialogs, and the menu entry. Kept
// in its own partial so the upstream MainForm partials stay untouched and the iFly
// work is easy to find; the form field declarations live in MainForm.cs.
public partial class MainForm
{
    public void ShowIFlyMonitorManagerDialog()
    {
        hotkeyManager.ExitOutputHotkeyMode();
        if (iflyMonitorManagerForm == null || iflyMonitorManagerForm.IsDisposed)
        {
            iflyMonitorManagerForm = new Forms.IFly737.IFly737MonitorManagerForm(currentAircraft.GetVariables());
        }
        iflyMonitorManagerForm.ShowForm();
    }

    private void StartIFlySdkBridge()
    {
        if (currentAircraft is not IFly737MAXDefinition ifly) return;
        ifly.Sdk.VariableChanged -= OnIFlyVariableChanged; // no double-subscribe
        ifly.Sdk.VariableChanged += OnIFlyVariableChanged;
        ifly.Sdk.Start();
    }

    private void OnIFlyVariableChanged(object? sender, SimConnect.IFly.IFlyVariableChangedEventArgs e)
    {
        if (currentAircraft is not IFly737MAXDefinition) return;
        if (_pmdgFieldToKeyMap == null) BuildPMDGFieldMap();
        if (!_pmdgFieldToKeyMap!.TryGetValue(e.FieldName, out string? varKey))
            return;

        OnSimVarUpdated(this, new SimVarUpdateEventArgs
        {
            VarName = varKey,
            Value = e.Value,
            Description = string.Empty,
            IsInitialSnapshot = e.IsInitialSnapshot,
        });
    }

    public void ShowIFlyCDUDialog()
    {
        if (currentAircraft is not IFly737MAXDefinition ifly) return;
        hotkeyManager.ExitInputHotkeyMode();
        if (iflyCduForm == null || iflyCduForm.IsDisposed)
            iflyCduForm = new Forms.IFly737.IFly737CDUForm(ifly.Sdk, announcer);
        iflyCduForm.ShowForm();
    }

    public void ShowIFlyEfbDialog()
    {
        hotkeyManager.ExitInputHotkeyMode();
        if (iflyEfbForm == null || iflyEfbForm.IsDisposed)
            iflyEfbForm = new Forms.IFly737.IFlyEfbForm(announcer);
        iflyEfbForm.ShowForm();
    }

    private void DisposeIFlyForms()
    {
        if (iflyCduForm != null && !iflyCduForm.IsDisposed) iflyCduForm.Dispose();
        iflyCduForm = null;
        if (iflyEfbForm != null && !iflyEfbForm.IsDisposed) iflyEfbForm.Dispose();
        iflyEfbForm = null;
        if (iflyMonitorManagerForm != null && !iflyMonitorManagerForm.IsDisposed) iflyMonitorManagerForm.Dispose();
        iflyMonitorManagerForm = null;
    }

    private void IFly737MAXMenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IFly737MAXDefinition());
    }
}
