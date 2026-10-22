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

    // Variant detection: which Fenix is loaded, read from the eight config L:vars (FenixConfigVars).
    // Variant starts Unknown and fills in as each var is first delivered; VariantChanged is raised, on the
    // UI thread (ProcessSimVarUpdate's), only when the record changes. Later plans share this one answer.
    public FenixVariant Variant { get; private set; } = FenixVariant.Unknown;
    public event EventHandler<FenixVariant>? VariantChanged;

    private static readonly HashSet<string> ConfigVarNames = new(FenixConfigVars.All, StringComparer.Ordinal);

    // The eight are read, never spoken or put on a panel: consumed by TryApplyConfigValue, so they take no
    // Ctrl+M row ([VAR-9]). Each is keyed by its own name, which no other Fenix var uses ([VAR-7]).
    // IsAnnounced is true although nothing is spoken: only announced Continuous vars are subscribed
    // (ContinuousBatchLayout.RidesBatch), and ProcessSimVarUpdate consumes them before any announcement.
    private void AddConfigVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)
    {
        (string Name, string DisplayName)[] configVariables =
        {
            (FenixConfigVars.AircraftType, "Fenix config: aircraft type"),
            (FenixConfigVars.EngineType, "Fenix config: engine type"),
            (FenixConfigVars.ActCount, "Fenix config: extra tank count"),
            (FenixConfigVars.Standby, "Fenix config: standby instrument"),
            (FenixConfigVars.Dcdu, "Fenix config: DCDU"),
            (FenixConfigVars.IsNeo, "Fenix config: NEO"),
            (FenixConfigVars.Adirs, "Fenix config: ADIRS type"),
            (FenixConfigVars.CargoHeat, "Fenix config: aft cargo heat"),
        };
        foreach (var (name, displayName) in configVariables)
        {
            variables[name] = new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = displayName,
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "number",
                ExcludeFromMonitorManager = true,
            };
        }
    }

    // True for the eight config vars (consumed silently, also when the reading changes nothing), false for
    // every other var, which carries on down ProcessSimVarUpdate.
    private bool TryApplyConfigValue(string varName, double value)
    {
        if (!ConfigVarNames.Contains(varName)) return false;
        var updated = Variant.With(varName, value);
        if (updated != Variant)
        {
            Variant = updated;
            VariantChanged?.Invoke(this, updated);
        }
        return true;
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

    // Opens a Fenix window as one of this definition's tracked windows: one per type, reused when it is
    // already open, and closed by DisposeTrackedWindows when the pilot switches aircraft. Every window a
    // FenixWindowRegistry entry opens goes through here, so none outlives the aircraft it controls.
    public void ShowFenixWindow<T>(Func<T> factory, Action<T> show) where T : System.Windows.Forms.Form
        => ShowTrackedWindow(factory, show);

    // Ctrl+Shift+F (input mode): the "Fenix windows" list. Input mode ends first, as for every window a
    // definition opens from a hotkey. The list is itself a tracked window, so a second press brings the open
    // one forward, and an aircraft switch closes it. The registry and the context are built only when the
    // list is created, not on that second press.
    private void ShowFenixWindows(SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        Hotkeys.HotkeyManager hotkeyManager)
    {
        hotkeyManager.ExitInputHotkeyMode();
        ShowFenixWindow(
            () => new Forms.Fenix.FenixWindowsForm(
                Forms.Fenix.FenixWindowRegistry.Build(),
                new Forms.Fenix.FenixWindowContext(this, DataService, simConnect, announcer, parentForm)),
            list => list.ShowForm());
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
