using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Cessna 172 Skyhawk with traditional steam gauges (six-pack instruments).
/// Adds airspeed calibrator and enhanced vacuum monitoring (gyro instruments depend on vacuum).
/// </summary>
public class CessnaC172ClassicDefinition : CessnaC172BaseDefinition
{
    public override string AircraftName => "Cessna 172 Skyhawk (Classic)";
    public override string AircraftCode => "C172_CLASSIC";

    // Enhanced vacuum warning for classic variant (gyro instruments depend on vacuum)
    private bool _lowVacuumCautionWarned = false;

    protected override Dictionary<string, SimConnect.SimVarDefinition> GetC172Variables()
    {
        var variables = base.GetC172Variables();

        // Airspeed True Calibrator (classic steam gauge only)
        variables["C172C_AIRSPEED_CAL"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRSPEED TRUE CALIBRATE",
            DisplayName = "Airspeed Calibrator",
            Type = SimConnect.SimVarType.SimVar,
            Units = "degrees",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        };
        variables["C172C_AIRSPEED_CAL_INC"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRSPEED_TRUE_CALIBRATE_INC",
            DisplayName = "Airspeed Calibrate Increase",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            RenderAsButton = true
        };
        variables["C172C_AIRSPEED_CAL_DEC"] = new SimConnect.SimVarDefinition
        {
            Name = "AIRSPEED_TRUE_CALIBRATE_DEC",
            DisplayName = "Airspeed Calibrate Decrease",
            Type = SimConnect.SimVarType.Event,
            UpdateFrequency = SimConnect.UpdateFrequency.Never,
            RenderAsButton = true
        };

        return variables;
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = base.GetPanelStructure();
        structure["Instruments"] = new List<string> { "Calibration" };
        return structure;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = base.BuildPanelControls();
        controls["Calibration"] = new List<string>
        {
            "C172C_AIRSPEED_CAL", "C172C_AIRSPEED_CAL_INC", "C172C_AIRSPEED_CAL_DEC"
        };
        return controls;
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // Enhanced vacuum warning for classic variant — gyro instruments (attitude indicator,
        // heading indicator) depend on vacuum, so warn earlier at 4.0 inHg
        if (varName == "C172_VACUUM")
        {
            if (value > 0 && value < 4.0 && !_lowVacuumCautionWarned)
            {
                announcer.Announce($"Caution: Vacuum low, {value:F1} inches. Gyro instruments unreliable.");
                _lowVacuumCautionWarned = true;
            }
            else if (value >= 4.0)
            {
                _lowVacuumCautionWarned = false;
            }
            return true; // Handled — skip base vacuum warning at 3.5
        }

        return base.ProcessSimVarUpdate(varName, value, announcer);
    }
}
