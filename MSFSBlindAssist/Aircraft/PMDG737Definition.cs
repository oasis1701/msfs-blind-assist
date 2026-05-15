using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for the PMDG 737-800 (NG3). Variables, panels, hotkey
/// routing, MCP direct-set dialogs, and announcement logic. Tasks C2–C13 fill
/// in the data dictionaries and behavior methods.
/// </summary>
public class PMDG737Definition : BaseAircraftDefinition, IPMDGAircraft
{
    public override string AircraftName => "PMDG 737-800";
    public override string AircraftCode => "PMDG_737";

    // Cached merged variables dictionary — built once on first access.
    // All callers are read-only so sharing a single instance is safe.
    private Dictionary<string, SimConnect.SimVarDefinition>? _cachedVariables;

    // Cached set of RenderAsButton keys that are NOT annunciators.
    // Used in ProcessSimVarUpdate to suppress raw value announcements
    // without re-allocating GetVariables() on every call.
    private HashSet<string>? _suppressedButtonKeys;

    private HashSet<string> SuppressedButtonKeys =>
        _suppressedButtonKeys ??= BuildSuppressedButtonKeys();

    private HashSet<string> BuildSuppressedButtonKeys()
    {
        var set = new HashSet<string>();
        foreach (var kvp in GetVariables())
        {
            if (kvp.Value.RenderAsButton && !kvp.Value.Name.Contains("_annun"))
                set.Add(kvp.Key);
        }
        return set;
    }

    // PMDG 737 MCP supports direct SetValue events for SPD/HDG/ALT/VS (NG3 SDK
    // exposes EVT_*_SET style events just like the 777).
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // =========================================================================
    // Panel Structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "ADIRU", "Service Interphone", "Engine (Aft)", "Oxygen", "Flight Recorder",
                "Flight Controls", "NAVDIS", "Fuel", "Electrical", "APU", "Wipers",
                "Center Overhead", "Anti-Ice", "Hydraulics", "Air Systems", "Doors",
                "Bottom Overhead"
            },
            ["Glareshield"] = new List<string>
            {
                "Warnings", "EFIS Captain", "EFIS First Officer", "MCP"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Autobrake", "Display Select", "GPWS", "Speed Reference",
                "Brightness"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Fire Protection", "Cargo Fire", "Transponder",
                "Pedestal Lights", "FltDk Door", "Trim", "Communication"
            },
        };
    }

    // =========================================================================
    // Variables — scaffold (populated in Tasks C5–C8)
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        if (_cachedVariables != null)
            return _cachedVariables;

        var variables = GetBaseVariables();
        var pmdgVars = GetPMDGVariables();
        foreach (var kvp in pmdgVars)
            variables[kvp.Key] = kvp.Value;
        _cachedVariables = variables;
        return variables;
    }

    private Dictionary<string, SimConnect.SimVarDefinition> GetPMDGVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>();
    }

    // =========================================================================
    // Panel Controls — scaffold (populated in Task C9)
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>();
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =========================================================================
    // Event ID dictionary — scaffold (populated in Task C2)
    // PMDG 737 NG3 SDK third-party event range starts at THIRD_PARTY_EVENT_ID_MIN.
    // =========================================================================
    private const int event_base = 0x00011000;  // THIRD_PARTY_EVENT_ID_MIN

    /// <summary>
    /// Maps PMDG 737 event names to their numeric event IDs.
    /// Used when sending events via the PMDG SDK control area.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EventIds =
        new Dictionary<string, int>
        {
            // Populated in Task C2
        };

    // =========================================================================
    // Variable → event name mapping (simple toggle and momentary controls)
    // Populated in Task C3
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _simpleEventMap =
        new Dictionary<string, string>
        {
            // Populated in Task C3
        };

    // =========================================================================
    // Guarded switch table: varKey → (guardEvent, switchEvent)
    // Populated in Task C4
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, (string Guard, string Switch)> _guardedMap =
        new Dictionary<string, (string, string)>
        {
            // Populated in Task C4
        };

    // =========================================================================
    // Behavior overrides — scaffold (populated in Tasks C10–C12)
    // =========================================================================

    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        // Populated in Task C10
        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Populated in Task C11
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        // Populated in Task C12
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // RequestFCUHeading / RequestFCUSpeed / RequestFCUAltitude /
    // RequestFCUVerticalSpeed overrides are added in Task C13.
}
