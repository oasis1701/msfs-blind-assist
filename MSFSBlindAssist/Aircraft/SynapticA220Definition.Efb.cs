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

    private void ShowEfbForm(ScreenReaderAnnouncer announcer)
    {
        _efbClient ??= new A220.A220EfbClient();
        if (_efbForm == null || _efbForm.IsDisposed)
            _efbForm = new Forms.A220.A220EfbForm(_efbClient, announcer);
        _efbForm.ShowForm();
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
        try { _efbClient?.Dispose(); } catch { }
        _efbClient = null;
    }
}
