using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Synaptic A220 — EFB tablet window (Shift+T, HotkeyAction.ShowPMDGEFB, the shared
/// "show this aircraft's EFB" action). The captain tablet ("efbA220_1" Coherent
/// view) is scraped/driven by <see cref="A220.A220EfbClient"/> +
/// <c>Resources/coherent-a220-efb-agent.js</c> and presented by
/// <see cref="Forms.A220.A220EfbForm"/> (plan docs/a220-plan.md W7).
/// </summary>
public partial class SynapticA220Definition
{
    private Forms.A220.A220EfbForm? _efbForm;
    private A220.A220EfbClient? _efbClient;

    private Forms.A220.A220NavdataForm? _navdataForm;

    private void ShowEfbForm(ScreenReaderAnnouncer announcer)
    {
        _efbClient ??= new A220.A220EfbClient();
        if (_efbForm == null || _efbForm.IsDisposed)
            _efbForm = new Forms.A220.A220EfbForm(_efbClient, announcer, () => ShowNavdataForm(announcer));
        _efbForm.ShowForm();
    }

    /// <summary>
    /// FMS navigation database update (Navigraph) — the aircraft's own Maintenance →
    /// Data Load page, driven through the shared DisplayUnits socket. Reached from the
    /// EFB window's "FMS navigation database" button: the EFB's own UPDATE NAVDATA
    /// does nothing on v1.0.9 (see A220NavdataState).
    /// </summary>
    internal void ShowNavdataForm(ScreenReaderAnnouncer announcer)
    {
        if (_navdataForm != null && !_navdataForm.IsDisposed)
        {
            _navdataForm.Activate();
            return;
        }
        _navdataForm = new Forms.A220.A220NavdataForm(
            DisplaysAgentCallAsync,
            () => _sim?.GetCachedVariableValue("A22X_NAV_DATA_SOURCE") ?? -1,
            source =>
            {
                var sim = _sim;
                if (sim == null) { announcer.AnnounceImmediate("Not connected to the simulator."); return; }
                WriteLVar(sim, "A220 Nav Data Source", source);
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(2000);
                    double now = sim.GetCachedVariableValue("A22X_NAV_DATA_SOURCE") ?? -1;
                    if (now >= 0 && (now >= 0.5) != (source == 1))
                        announcer.AnnounceImmediate("The FMS did not change its database source.");
                });
            },
            announcer);
        _ = _navdataForm.OpenAsync();
    }

    /// <summary>
    /// Aircraft-swap teardown (called from StopAllMotion): the form hides on user
    /// close, so it must be Dispose()d directly here — and the Coherent inspector
    /// socket must be torn down so the page is not held for the process lifetime
    /// (Coherent GT allows only one inspector socket per page).
    /// </summary>
    private void DisposeEfb()
    {
        try { _efbForm?.Dispose(); } catch { }
        _efbForm = null;
        try { _navdataForm?.Dispose(); } catch { }
        _navdataForm = null;
        try { _efbClient?.Dispose(); } catch { }
        _efbClient = null;
    }
}
