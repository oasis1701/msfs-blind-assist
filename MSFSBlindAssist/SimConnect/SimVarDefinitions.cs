
namespace MSFSBlindAssist.SimConnect;
public enum SimVarType
{
    LVar,      // Local variable (L:varname)
    Event,     // SimConnect Event
    SimVar,    // Standard SimVar
    HVar       // H-variable (requires MobiFlight WASM)
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
    public uint EventParam { get; set; }  // Parameter for events (like pump index)
    public bool IsMomentary { get; set; }  // True for momentary buttons that need auto-reset

    // UI customization properties (aircraft-specific)
    public bool RenderAsButton { get; set; }  // True to render as button instead of combo box (e.g., APU Start)
    public bool PreventTextInput { get; set; }  // True to prevent text input UI for _SET variables (e.g., autobrake)

    // MobiFlight WASM support properties
    public bool UseMobiFlight { get; set; }  // Flag to route through MobiFlight WASM
    public string PressEvent { get; set; } = string.Empty;   // H-variable for button press
    public string ReleaseEvent { get; set; } = string.Empty; // H-variable for button release
    public string LedVariable { get; set; } = string.Empty;  // L-variable for LED state monitoring
    public int PressReleaseDelay { get; set; } = 200; // Delay between press and release (ms)
}
