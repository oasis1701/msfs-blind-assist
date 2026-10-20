using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The universal auto-AP-engage contract on the C172's GFC 700 (UniversalAutomationService): what the
/// "engage the autopilot after take-off" setting presses, how it knows the press took, and the lowest
/// height it may press at. Fixed gear is the airframe's (<see cref="CessnaC172BaseDefinition.HasRetractableGear"/>).
/// </summary>
public partial class CessnaC172G1000Definition
{
    /// <summary>
    /// The GFC 700's lowest height for the autopilot: "disengaged below 200 feet AGL during approach
    /// operations and below 800 feet AGL during all other operations", as the Civil Air Patrol's GFC 700
    /// course for its G1000 Cessnas gives it (the Cessna supplement itself was not read), the same
    /// limitation as the DA40's AFM Supplement A30. The after-take-off engage is never an approach, so
    /// the universal engage never presses below it whatever the pilot's setting (FO-17).
    /// </summary>
    public const int GfcMinimumEngageHeightFt = 800;

    public override int MinimumAutopilotEngageAltitudeAgl => GfcMinimumEngageHeightFt;

    /// <summary>
    /// The panel's own AP key (C172-3: <c>AS1000_AUTOPILOT_AP_PFD</c> = 1 is one press of the bezel key,
    /// and a second press disengages), so it is a TOGGLE: pressed only on a definite OFF. A key the input
    /// event catalog does not hold yet presses nothing, and the closed loop then asks the Captain.
    /// </summary>
    public override void EngageAutopilot(SimConnectManager simConnect)
    {
        if (IsAutopilotEngaged(simConnect) != false) return;
        string key = AutopilotKeys["C172_AP_MASTER_STATE"];
        if (simConnect.HasInputEvent(key)) simConnect.TrySetInputEvent(key, 1);
    }

    /// <summary>The autopilot master from the batch (<c>C172_AP_MASTER_STATE</c>, AUTOPILOT MASTER,
    /// Continuous and announced), null until it has been read: never false for "don't know" (FO-18).</summary>
    public override bool? IsAutopilotEngaged(SimConnectManager simConnect)
        => simConnect.GetCachedVariableValue("C172_AP_MASTER_STATE") is double v && !double.IsNaN(v) ? v >= 0.5 : null;

    /// <summary>A toggle read back with the 1 Hz batch: presses 3 s apart, and none on an unread state
    /// (FO-18), as the HS787's AP_MASTER.</summary>
    public override int AutopilotEngageRetryIntervalMs => ToggleAutopilotEngageRetryIntervalMs;
    public override bool AutopilotEngageWaitsForState => true;
}
