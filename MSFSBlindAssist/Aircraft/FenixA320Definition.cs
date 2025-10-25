using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for Fenix A320 CEO.
/// Fenix uses increment/decrement controls for FCU instead of direct value input.
/// </summary>
public class FenixA320Definition : BaseAircraftDefinition
{
    public override string AircraftName => "Fenix A320 CEO";
    public override string AircraftCode => "FENIX_A320CEO";

    // Fenix FCU uses increment/decrement buttons, not direct value input like FlyByWire
    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // Variables will be added here using fenix-variables skill
            // Structure ready for Fenix-specific L-variables and H-variables
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical"
                // Additional panels will be added here as features are implemented
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Electrical"] = new List<string>
            {
                // Electrical panel variables will be added here
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
            // Display-only variables can be added here as needed
        };
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>
        {
            // Button-to-state mappings will be added here
        };
    }
}
