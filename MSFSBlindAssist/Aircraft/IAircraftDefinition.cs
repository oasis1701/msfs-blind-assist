namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Defines the control type for FCU (Flight Control Unit) controls.
/// Different aircraft may use different interaction patterns.
/// </summary>
public enum FCUControlType
{
    /// <summary>
    /// Direct value input (e.g., A320 FCU with SET commands)
    /// </summary>
    SetValue,

    /// <summary>
    /// Increment/Decrement buttons (e.g., Boeing MCP with INC/DEC)
    /// </summary>
    IncrementDecrement
}

/// <summary>
/// Interface for aircraft-specific definitions including variables, panels, and behavior.
/// Each supported aircraft should implement this interface to provide its configuration.
/// </summary>
public interface IAircraftDefinition
{
    /// <summary>
    /// Full display name of the aircraft (e.g., "FlyByWire Airbus A320neo")
    /// </summary>
    string AircraftName { get; }

    /// <summary>
    /// Short code for the aircraft (e.g., "A320", "B737")
    /// Used for settings persistence and internal identification
    /// </summary>
    string AircraftCode { get; }

    /// <summary>
    /// Gets all simulator variables and controls for this aircraft.
    /// Maps variable keys to their definitions.
    /// </summary>
    Dictionary<string, SimConnect.SimVarDefinition> GetVariables();

    /// <summary>
    /// Gets the panel organization structure.
    /// Maps section names to lists of panel names within that section.
    /// Example: "Overhead Forward" -> ["ELEC", "ADIRS", "APU"]
    /// </summary>
    Dictionary<string, List<string>> GetPanelStructure();

    /// <summary>
    /// Gets the mapping of panels to their control variable keys.
    /// Maps panel names to lists of variable keys that appear in that panel.
    /// Example: "FCU" -> ["A32NX.FCU_HDG_SET", "A32NX.FCU_SPD_SET"]
    /// </summary>
    Dictionary<string, List<string>> GetPanelControls();

    /// <summary>
    /// Gets the mapping of panels to display-only variables.
    /// These variables update silently without triggering announcements.
    /// Optional - return empty dictionary if not used.
    /// </summary>
    Dictionary<string, List<string>> GetPanelDisplayVariables();

    /// <summary>
    /// Gets the button-to-state variable mapping for automatic announcements.
    /// Maps button event keys to their corresponding state variable keys.
    /// Example: "A32NX.FCU_AP_1_PUSH" -> "A32NX_FCU_AP_1_LIGHT_ON"
    /// </summary>
    Dictionary<string, string> GetButtonStateMapping();

    /// <summary>
    /// Gets the control type for altitude input in the FCU.
    /// </summary>
    FCUControlType GetAltitudeControlType();

    /// <summary>
    /// Gets the control type for heading input in the FCU.
    /// </summary>
    FCUControlType GetHeadingControlType();

    /// <summary>
    /// Gets the control type for speed input in the FCU.
    /// </summary>
    FCUControlType GetSpeedControlType();

    /// <summary>
    /// Gets the control type for vertical speed input in the FCU.
    /// </summary>
    FCUControlType GetVerticalSpeedControlType();
}
