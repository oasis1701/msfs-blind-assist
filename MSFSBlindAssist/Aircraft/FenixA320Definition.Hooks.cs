using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

// Seams the Fenix v3 work fills in from its own partial files. Each `partial void` is optional:
// with no implementation its call compiles to nothing, so the definition behaves as it did before.
//   FenixA320Definition.Variants.cs  variant, panel, display and visibility seams, OnFenixSwitchedAway
//   FenixA320Definition.Cabin.cs     cabin background seams
// The engine background seams (StartEngineBackground, StopEngineBackground) have no implementation: the Engines
// window reads its own values while it is open.
public partial class FenixA320Definition
{
    // Variant seams (FenixA320Definition.Variants.cs)
    partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition> variables);
    partial void AddVariantPanelControls(Dictionary<string, List<string>> panels);
    // Sections to their panel lists, after the main file builds them (the variant file adds the "Clock" panel
    // and drops the panels the loaded livery leaves without a control).
    partial void AddVariantPanelStructure(Dictionary<string, List<string>> structure);
    partial void AddVariantPanelDisplayVariables(Dictionary<string, List<string>> displayVariables);
    // Sets displayText to show that text in a panel display; leaves it null otherwise.
    partial void TryVariantDisplayOverride(string varKey, double value, ref string? displayText);
    partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled);
    partial void OnVariantSimVarUpdate(string varName, double value, Accessibility.ScreenReaderAnnouncer announcer,
        ref bool handled);
    partial void IsVariantControlVisible(string varKey, ref bool visible);
    // C# allows one implementation, so the variant file owns it.
    partial void OnFenixSwitchedAway();

    // Background seams, one Start/Stop pair each (the cabin pair is implemented, the engine pair is not). Start
    // runs once per definition instance,
    // on the UI thread, from the first ProcessSimVarUpdate that gets past the take-off IAS feed (that branch
    // returns first, so Start can come up to about a second after the instance's very first update); a sim
    // reconnect keeps the instance, so Start does not run again then. Stop runs in OnSwitchedAway, on an
    // aircraft switch and on app quit. Every seam call is isolated: an exception from one is logged and the
    // others still run, so one background's failure never aborts the switch, the quit or the update.
    partial void StartCabinBackground(Accessibility.ScreenReaderAnnouncer announcer);
    partial void StopCabinBackground();
    partial void StartEngineBackground(Accessibility.ScreenReaderAnnouncer announcer);
    partial void StopEngineBackground();

    private bool _backgroundStarted;

    /// <summary>
    /// False keeps every background seam from starting. It exists for the tests, whose module initializer
    /// (FenixTestHost) turns it off: the seams subscribe and read over 127.0.0.1:8083, and a test feeding a
    /// sim update to a new definition would otherwise leave a socket loop talking to the pilot's sim. The
    /// app never changes it.
    /// </summary>
    internal static bool BackgroundSeamsEnabled { get; set; } = true;

    /// <summary>For the tests: whether this instance has run its background Start seams.</summary>
    internal bool BackgroundStarted => _backgroundStarted;

    // Runs one background seam: an exception from it is logged (CORE-15), never thrown at the caller.
    private static void RunSeam(string seam, Action call)
    {
        try
        {
            call();
        }
        catch (Exception ex)
        {
            Log.Error("Fenix", $"{seam} failed: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    // The Fenix data service client (127.0.0.1:8083), created on first use from any thread. OnSwitchedAway
    // disposes it after the background Stops, so its consumers stop before the service goes, and from then
    // on the getter returns that disposed instance.
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
    // Variant stays Unknown until the aircraft type reads 1-4, then holds every reading so far and follows
    // each new one (the gate in TryApplyConfigValue); VariantChanged is raised, on the UI thread
    // (ProcessSimVarUpdate's), only when the record changes. Other Fenix code shares this one answer.
    public FenixVariant Variant { get; private set; } = FenixVariant.Unknown;
    public event EventHandler<FenixVariant>? VariantChanged;

    private static readonly HashSet<string> ConfigVarNames = new(FenixConfigVars.All, StringComparer.Ordinal);

    // The latest finite reading of each config var, kept whether or not the config is published yet.
    private readonly Dictionary<string, double> _configReadings = new(StringComparer.Ordinal);

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
    // every other var, which carries on down ProcessSimVarUpdate. Internal for the tests.
    internal bool TryApplyConfigValue(string varName, double value)
    {
        if (!ConfigVarNames.Contains(varName)) return false;
        // Not a finite number: nothing to apply (FenixVariant.With ignores it too), and the last good
        // reading is kept.
        if (!double.IsFinite(value)) return true;
        _configReadings[varName] = value;

        // The gate: SimConnect reads an L:var the Fenix has not written yet as 0, and 0 is a
        // real value for seven of the eight (CFM, ISIS, GNADIRS, no DCDU, CEO, no cargo heat, no extra
        // tanks), so in the sim menu or while the Fenix loads they would claim all of those. An aircraft
        // type of 1-4 is the sign the Fenix has published its config: until it reads that, the readings
        // are only kept and Variant stays Unknown; when it does, every kept reading comes in at once, as
        // one change; after that each reading applies on its own. A type back at 0 or out of range makes
        // Variant Unknown again.
        FenixVariant updated;
        if (!IsPublishedType(_configReadings.GetValueOrDefault(FenixConfigVars.AircraftType)))
            updated = FenixVariant.Unknown;
        else if (Variant.Type == FenixAircraftType.Unknown)
            updated = FoldKeptConfigReadings();
        else
            updated = Variant.With(varName, value);

        if (updated != Variant)
        {
            Variant = updated;
            RaiseVariantChanged(updated);
        }
        return true;
    }

    // An aircraft type FenixVariant maps to an airframe: a whole number 1-4.
    private static bool IsPublishedType(double type)
        => FenixVariant.Unknown.With(FenixConfigVars.AircraftType, type).Type != FenixAircraftType.Unknown;

    // Every kept reading folded into one record, the aircraft type first.
    private FenixVariant FoldKeptConfigReadings()
    {
        var variant = FenixVariant.Unknown.With(
            FenixConfigVars.AircraftType, _configReadings[FenixConfigVars.AircraftType]);
        foreach (string name in FenixConfigVars.All)
        {
            if (name != FenixConfigVars.AircraftType && _configReadings.TryGetValue(name, out double reading))
                variant = variant.With(name, reading);
        }
        return variant;
    }

    // Each subscriber in turn: one that throws is logged, and the ones after it still hear the change.
    private void RaiseVariantChanged(FenixVariant variant)
    {
        Log.Debug("Fenix", $"Variant: {variant}");
        if (VariantChanged is not { } handlers) return;
        foreach (EventHandler<FenixVariant> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, variant);
            }
            catch (Exception ex)
            {
                Log.Error("Fenix", $"A VariantChanged subscriber failed: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }
    }

    private void EnsureFenixBackground(Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (_backgroundStarted || !BackgroundSeamsEnabled) return;
        // OnSwitchedAway disposed the data service for good: MainForm builds a new definition on every
        // switch, so this instance is finished. On quit a late sim update can still arrive (Disconnect pumps
        // messages for 500 ms); a background started now would poll an inert service until the app exits.
        if (DataService.IsDisposed) return;
        _backgroundStarted = true;
        RunSeam(nameof(StartCabinBackground), () => StartCabinBackground(announcer));
        RunSeam(nameof(StartEngineBackground), () => StartEngineBackground(announcer));
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

    // Set once by OnSwitchedAway: this definition is finished. The gravity gear crank stops before its next
    // write, and a panel TO CONFIG verdict still being watched is not spoken over the next aircraft.
    private volatile bool _switchedAway;

    public override void OnSwitchedAway()
    {
        try
        {
            _switchedAway = true;
            // A typed FCU set still turning a knob stops before its next write ([FNX-20]).
            _fcuSetTurns.Close();
            RunSeam(nameof(OnFenixSwitchedAway), () => OnFenixSwitchedAway());
            RunSeam(nameof(StopCabinBackground), () => StopCabinBackground());
            RunSeam(nameof(StopEngineBackground), () => StopEngineBackground());
            _backgroundStarted = false;
        }
        finally
        {
            // Disposed but kept, never nulled: a disposed service is inert (Unknown, false, no-op Subscribe),
            // so a late callback touching DataService cannot bring a live one back after the switch. One
            // nothing touched yet is built here first, for the same reason: the getter then hands out the
            // disposed one, never a fresh live service nobody would dispose.
            Services.FenixDataService dataService;
            lock (_dataServiceLock) dataService = _dataService ??= new Services.FenixDataService();
            dataService.Dispose();
        }
    }
}
