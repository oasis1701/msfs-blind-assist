namespace MSFSBlindAssist.Aircraft;

// Seams the Fenix v3 plans fill in from their own partial files. Each `partial void` is optional:
// with no implementation its call compiles to nothing, so the definition behaves as it did before.
//   Plan 06  FenixA320Definition.Variants.cs  variant, panel, display and visibility seams, OnFenixSwitchedAway
//   Plan 08  FenixA320Definition.Cabin.cs     cabin background seams
//   Plan 09  FenixA320Definition.Engines.cs   engine background seams
public partial class FenixA320Definition
{
    // Variant seams (plan 06)
    partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition> variables);
    partial void AddVariantPanelControls(Dictionary<string, List<string>> panels);
    // Sections to their panel lists, after the main file builds them (plan 06 adds the "Clock" panel).
    partial void AddVariantPanelStructure(Dictionary<string, List<string>> structure);
    partial void AddVariantPanelDisplayVariables(Dictionary<string, List<string>> displayVariables);
    // Sets displayText to show that text in a panel display; leaves it null otherwise.
    partial void TryVariantDisplayOverride(string varKey, double value, ref string? displayText);
    partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled);
    partial void OnVariantSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer,
        ref bool handled);
    partial void IsVariantControlVisible(string varKey, ref bool visible);
    // C# allows one implementation, so plan 06 owns it.
    partial void OnFenixSwitchedAway();

    // Background seams, one Start/Stop pair per plan (cabin: plan 08, engine: plan 09). Start runs once
    // per definition instance, on the UI thread, from the first ProcessSimVarUpdate after a connection;
    // Stop runs in OnSwitchedAway.
    partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer);
    partial void StopCabinBackground();
    partial void StartEngineBackground(Accessibility.ScreenReaderAnnouncer announcer);
    partial void StopEngineBackground();

    private bool _backgroundStarted;

    // The Fenix data service client (127.0.0.1:8083), created on first use from any thread. OnSwitchedAway
    // disposes it after the background Stops, so its consumers stop before the service goes.
    private readonly object _dataServiceLock = new();
    private Services.FenixDataService? _dataService;

    public Services.FenixDataService DataService
    {
        get
        {
            lock (_dataServiceLock)
                return _dataService ??= new Services.FenixDataService();
        }
    }

    private void EnsureFenixBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (_backgroundStarted) return;
        _backgroundStarted = true;
        StartCabinBackground(announcer);
        StartEngineBackground(announcer);
    }

    public override bool IsPanelControlVisible(string varKey, SimConnect.SimConnectManager? simConnect)
    {
        bool visible = true;
        IsVariantControlVisible(varKey, ref visible);
        return visible;
    }

    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        string? text = null;
        TryVariantDisplayOverride(varKey, value, ref text);
        displayText = text ?? "";
        return text != null;
    }

    public override void OnSwitchedAway()
    {
        OnFenixSwitchedAway();
        StopCabinBackground();
        StopEngineBackground();
        _backgroundStarted = false;
        // Disposed but kept, never nulled: a disposed service is inert (Unknown, false, no-op Subscribe),
        // so a late callback touching DataService cannot bring a live one back after the switch.
        Services.FenixDataService? dataService;
        lock (_dataServiceLock) dataService = _dataService;
        dataService?.Dispose();
    }
}
