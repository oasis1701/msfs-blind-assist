using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// The crew alerting system: a background monitor over the pilot PFD's CAS list (Ctrl+E toggles
/// its call-outs; four Ctrl+M rows mute a severity each), and the Alt+E window that lists the
/// messages above the MFD's engine strip. The MFD has ONE inspector socket, so the definition
/// owns the one MFD client every reader shares (the engine strip here, the synoptic reader later).
/// </summary>
public partial class SkywardC680Definition
{
    private C680CasMonitor? _casMonitor;
    private CoherentDisplayClient? _mfdClient;

    /// <summary>The four Ctrl+M rows. Never fed by the sim (their Name is their own key); they exist to carry a mute each.</summary>
    private static Dictionary<string, SimVarDefinition> BuildCasVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        foreach (var (key, display) in new[] { ("C680_CAS_WARNING", "CAS Warnings"), ("C680_CAS_CAUTION", "CAS Cautions"), ("C680_CAS_ADVISORY", "CAS Advisories"), ("C680_CAS_STATUS", "CAS Status Messages") })
            v[key] = new SimVarDefinition
            {
                Name = key, DisplayName = display, Type = SimVarType.LVar, Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true, ExcludeFromBatch = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Quiet", [1] = "Posted" }
            };
        return v;
    }

    private static bool IsCasPseudoVariable(string key) => key.StartsWith("C680_CAS_", StringComparison.Ordinal);

    public bool IsCasClassMuted(string cls)
        => Settings.SettingsManager.Current.C680DisabledMonitorVariablesSet.Contains("C680_CAS_" + cls.ToUpperInvariant());

    /// <summary>MainForm starts the monitor when the Sovereign+ is selected; it runs for as long as the aircraft is current.</summary>
    public void StartCasMonitor(ScreenReaderAnnouncer announcer)
    {
        if (_casMonitor != null) return;
        _casMonitor = new C680CasMonitor(announcer, IsCasClassMuted);
        _casMonitor.Start();
    }

    public void StopCasMonitor()
    {
        _casMonitor?.Dispose(); _casMonitor = null;
        _mfdClient?.Dispose(); _mfdClient = null;
    }

    /// <summary>The one MFD reader; the engine strip today, the synoptic pages beside it.</summary>
    private CoherentDisplayClient MfdClient
    {
        get
        {
            if (_mfdClient == null)
            {
                _mfdClient = new CoherentDisplayClient("WTG3000_MFD", 1000, "coherent-c680-mfd-agent.js");
                _mfdClient.SetActive(false);   // read on demand only; the poll loop keeps the socket alive
                _mfdClient.Start();
            }
            return _mfdClient;
        }
    }

    public Task<List<string>> ScrapeEngineStripAsync() => MfdClient.ScrapeNowAsync();

    private bool HandleCasHotkey(HotkeyAction action, ScreenReaderAnnouncer ann, HotkeyManager hk)
    {
        switch (action)
        {
            case HotkeyAction.ReadDisplayUpperECAM:   // Alt+E: the CAS and engine window
                hk.ExitOutputHotkeyMode();
                if (_casMonitor == null) { ann.AnnounceImmediate("CAS not available."); return true; }
                ShowWindow("cas", () => new Forms.Citation680.C680CasForm(_casMonitor, ScrapeEngineStripAsync));
                return true;
            case HotkeyAction.ToggleECAMMonitoring:   // Ctrl+E: the call-outs
                hk.ExitOutputHotkeyMode();
                if (_casMonitor == null) { ann.AnnounceImmediate("CAS not available."); return true; }
                _casMonitor.Enabled = !_casMonitor.Enabled;
                ann.AnnounceImmediate(_casMonitor.Enabled ? "CAS call-outs on" : "CAS call-outs off");
                return true;
        }
        return false;
    }
}
