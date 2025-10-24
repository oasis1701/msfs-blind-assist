
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
    public bool ReverseDisplayOrder { get; set; }  // True to display combo box items in reverse order
    public Dictionary<double, string> ValueDescriptions { get; set; } = new Dictionary<double, string>();
    public uint EventParam { get; set; }  // Parameter for events (like pump index)
    public bool IsMomentary { get; set; }  // True for momentary buttons that need auto-reset

    // MobiFlight WASM support properties
    public bool UseMobiFlight { get; set; }  // Flag to route through MobiFlight WASM
    public string PressEvent { get; set; } = string.Empty;   // H-variable for button press
    public string ReleaseEvent { get; set; } = string.Empty; // H-variable for button release
    public string LedVariable { get; set; } = string.Empty;  // L-variable for LED state monitoring
    public int PressReleaseDelay { get; set; } = 200; // Delay between press and release (ms)
}
