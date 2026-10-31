using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The universal auto-AP-engage and auto-gear contract on the DA40 (UniversalAutomationService):
/// what the First Officer's "engage the autopilot after take-off" setting presses, how it knows the
/// press took, the lowest height it may press at, and that there is no gear to move.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>
    /// The GFC 700's lowest height for the autopilot: AFM Supplement A30, limitation 7, "below 800 ft
    /// AGL for all other phases of flight" than an approach (200 ft there), and the after-take-off
    /// engage is never an approach. The Working Title autopilot itself engages lower, so this is the
    /// aircraft's limitation rather than an inhibit, and the universal engage never presses below it
    /// whatever the pilot's setting (FO-17).
    /// </summary>
    public const int GfcMinimumEngageHeightFt = 800;

    public override int MinimumAutopilotEngageAltitudeAgl => GfcMinimumEngageHeightFt;

    /// <summary>
    /// <c>AUTOPILOT_ON</c> through the calculator, the one way the GFC 700's state manager takes the
    /// engage (DA40A-1, DA40A-3: the panel's Autopilot row writes the same event). ON is ON, never a
    /// toggle, so a repeat over an engaged autopilot changes nothing.
    /// </summary>
    public override void EngageAutopilot(SimConnectManager simConnect)
        => simConnect.ExecuteCalculatorCodeUnique("1 (>K:AUTOPILOT_ON)");

    /// <summary>The autopilot master from the batch (<c>DA40_AP_MASTER</c>, Continuous and
    /// announced), null until it has been read: never false for "don't know" (FO-18).</summary>
    public override bool? IsAutopilotEngaged(SimConnectManager simConnect)
        => simConnect.GetCachedVariableValue("DA40_AP_MASTER") is double v ? v >= 0.5 : null;

    /// <summary>
    /// The engage's state reaches the cache with the 1 Hz batch, so a retry waits two seconds for the
    /// press before it to show (DA40S-2): retried on every position answer, five attempts ran out in
    /// under three seconds, before the first one could read back. The press is idempotent, so the
    /// spacing only stops a premature Captain call.
    /// </summary>
    public override int AutopilotEngageRetryIntervalMs => 2000;

    /// <summary>
    /// ⚠️ FIXED GEAR, WHICH THE SIM DOES NOT SAY. The COWS DA40's flight model reports
    /// <c>IS GEAR RETRACTABLE</c> 1 and its gear handle UP (measured on the NG on the ground,
    /// 2026-10-20), because COWS puts the gear key to another use: "Use your Toggle Landing gear button
    /// to switch steering assist modes" (the aircraft's own Tips page). The universal auto-gear read the
    /// handle up and, in the descent, sent GEAR_DOWN and said "Gear down.": a steering-assist mode
    /// change, announced as a gear the aeroplane does not have.
    /// </summary>
    public override bool HasRetractableGear => false;
}
