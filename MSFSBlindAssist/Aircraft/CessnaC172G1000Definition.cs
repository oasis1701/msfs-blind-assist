using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna 172 Skyhawk with Garmin G1000 glass cockpit.
/// Adds standby battery system and G1000-specific monitoring.
/// </summary>
public class CessnaC172G1000Definition : CessnaC172BaseDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (G1000)";
    public override string AircraftCode => "C172_G1000";

    protected override Dictionary<string, SimConnect.SimVarDefinition> GetC172Variables()
    {
        var variables = base.GetC172Variables();

        // Standby battery system (G1000 only)
        variables["C172G_STBY_BATTERY_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "ELECTRICAL MASTER BATTERY:2",
            DisplayName = "Standby Battery",
            Type = SimConnect.SimVarType.SimVar,
            Units = "Bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
        variables["C172G_STBY_BATTERY_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "TOGGLE_MASTER_BATTERY",
            DisplayName = "Standby Battery Toggle",
            Type = SimConnect.SimVarType.Event,
            EventParam = 2, // Battery index 2
            UpdateFrequency = SimConnect.UpdateFrequency.Never
        };
        variables["C172G_STBY_BATTERY_TEST"] = new SimConnect.SimVarDefinition
        {
            Name = "XMLVAR_STBYBattery_Test",
            DisplayName = "Standby Battery Test",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Test" }
        };

        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        // Add Standby Battery panel to Electrical section
        structure["Electrical"].Add("Standby Battery");
        return structure;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = base.BuildPanelControls();
        controls["Standby Battery"] = new List<string>
        {
            "C172G_STBY_BATTERY_STATE", "C172G_STBY_BATTERY_TOGGLE",
            "C172G_STBY_BATTERY_TEST"
        };
        return controls;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        var mapping = base.GetButtonStateMapping();
        mapping["C172G_STBY_BATTERY_TOGGLE"] = "C172G_STBY_BATTERY_STATE";
        return mapping;
    }
}
