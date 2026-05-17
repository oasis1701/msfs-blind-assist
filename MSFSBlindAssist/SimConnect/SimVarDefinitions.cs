
namespace MSFSBlindAssist.SimConnect;
public enum SimVarType
{
    LVar,        // Local variable (L:varname)
    Event,       // SimConnect Event
    SimVar,      // Standard SimVar
    HVar,        // H-variable (requires MobiFlight WASM)
    PMDGVar,     // PMDG SDK variable (read via Client Data Area)
    InputEvent   // B: InputEvent (write via SimConnect SetInputEvent API)
}

public enum UpdateFrequency
{
    Never = 0,          // Write-only variables, never requested
    OnRequest = 1,      // Request when needed (panels, hotkeys, etc.)
    Continuous = 2      // Monitor continuously (announcements, warnings)
}

public class SimVarDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public SimVarType Type { get; set; }
    public string Units { get; set; } = "number";
    public UpdateFrequency UpdateFrequency { get; set; } = UpdateFrequency.OnRequest;
    public bool IsAnnounced { get; set; }  // True if changes should be announced to screen reader
    public bool AnnounceValueOnly { get; set; }  // True to announce only value (e.g., "On ground") instead of "DisplayName: value" (e.g., "Ground State: On ground")
    public bool ReverseDisplayOrder { get; set; }  // True to display combo box items in reverse order
    public Dictionary<double, string> ValueDescriptions { get; set; } = new Dictionary<double, string>();
    public bool OnlyAnnounceValueDescriptionMatches { get; set; }  // True to only announce when value matches a ValueDescriptions key (within tolerance), skip intermediate values

    /// <summary>
    /// When true, exclude this variable from the batched continuous monitoring (GenericBatch1..5)
    /// and register it as its own per-second continuous subscription. Used when the batched read
    /// has been observed to deliver wrong/oscillating values due to data-definition position
    /// drift (silent AddToDataDefinition failures shifting subsequent vars' SimConnect-side
    /// struct slots out of sync with MSFSBA's continuousVariableIndexMap). Individual subscriptions
    /// use each var's own SingleValue data def — no struct alignment to worry about.
    /// Only meaningful when UpdateFrequency == Continuous.
    /// </summary>
    public bool ExcludeFromBatch { get; set; }
    public uint EventParam { get; set; }  // Parameter for events (like pump index)
    public bool IsMomentary { get; set; }  // True for momentary buttons that need auto-reset

    /// <summary>
    /// Optional B: InputEvent name for the WRITE path. When set, HandleUIVariableSet (or
    /// callers using SimConnectManager.TrySetInputEvent) can route a write through the
    /// SimConnect SetInputEvent API instead of K-events/SetLVar. Used for switches whose
    /// real subsystem state is driven by an InputEvent (WT Boeing 787 AT arm, bleed air,
    /// engine start rotaries, etc.) while the READ may still come from a separate L-var.
    /// Independent of <see cref="Type"/> — any var type can opt into InputEvent writes.
    /// </summary>
    public string? InputEventName { get; set; }

    // UI customization properties (aircraft-specific)
    public bool RenderAsButton { get; set; }  // True to render as button instead of combo box (e.g., APU Start)
    public string? StateVariable { get; set; }  // LVar name to read for actual button on/off state (e.g., I_ indicator for S_ switch buttons)
    public bool PreventTextInput { get; set; }  // True to prevent text input UI for _SET variables (e.g., autobrake)
    public string? HelpText { get; set; }  // Optional help text read by screen reader (overrides default AccessibleDescription)

    // MobiFlight WASM support properties
    public bool UseMobiFlight { get; set; }  // Flag to route through MobiFlight WASM
    public string PressEvent { get; set; } = string.Empty;   // H-variable for button press
    public string ReleaseEvent { get; set; } = string.Empty; // H-variable for button release
    public string LedVariable { get; set; } = string.Empty;  // L-variable for LED state monitoring
    public int PressReleaseDelay { get; set; } = 200; // Delay between press and release (ms)
}
