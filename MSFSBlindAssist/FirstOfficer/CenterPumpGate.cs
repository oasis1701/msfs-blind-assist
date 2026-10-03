namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Pure RULING-A gate for center-pump ON writes. The ONE all-paths chokepoint is each
/// executor's DispatchCoreAsync (single writes, flow Multi tuples, and checklist actions all
/// funnel through it — §6 corrected placement). An ON write to a center pump on a tank at or
/// below OffThresholdLbs is a dry-run: suppress it. Param 0 (OFF) is NEVER gated — a dry pump
/// must always be switchable off.
/// </summary>
public static class CenterPumpGate
{
    /// <summary>The centre-pump keys of every aircraft that shares this gate. The first two
    /// are the PMDG 737/777 SDK event names; the second two are the iFly 737 MAX8 SDK field
    /// names its executor dispatches by (the iFly write path is keyed on the definition's
    /// varKey, not on an event id). One list, because ONE gate covers all three aircraft.</summary>
    public static bool IsCenterOnEvent(string eventName) =>
        eventName == "EVT_OH_FUEL_PUMP_L_CENTER" || eventName == "EVT_OH_FUEL_PUMP_R_CENTER" ||
        eventName == "Fuel_CENTER_L_Switch_Status" || eventName == "Fuel_CENTER_R_Switch_Status";

    /// <param name="isOn">The resolved ON test for this executor (777: targetValue == 1;
    /// 737: (target ?? 1) == 1).</param>
    /// <param name="centerQty">Live center quantity (lbs); a pre-snapshot read of 0.0 correctly
    /// blocks ON — the safe direction — and self-heals on the next actuation.</param>
    public static bool ShouldSuppressCenterOn(string eventName, bool isOn, double centerQty) =>
        IsCenterOnEvent(eventName) && isOn && centerQty <= CenterFuelPumpAutomation.OffThresholdLbs;
}
