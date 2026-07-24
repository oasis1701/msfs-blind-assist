namespace MSFSBlindAssist.FirstOfficer;

public interface IFoStateEvaluator
{
    bool IsAvailable { get; }
    double GetValue(string field);
    bool IsOn(string field);
    bool IsPosition(string field, int position);

    /// <summary>Store the SimBrief planned takeoff flap setting (used by the Before Taxi flow).</summary>
    void SetTakeoffFlaps(int flaps);

    /// <summary>
    /// Store both engines' latest N2 (percent), pushed from the FO background timer. The PMDG
    /// data struct has no N1/N2, so the engine-start checklist/flow reads these via the synthetic
    /// GetValue keys "FO_ENG1_N2" / "FO_ENG2_N2" to reliably detect a running engine.
    /// </summary>
    void SetEngineN2(double eng1N2, double eng2N2);

    /// <summary>
    /// Store the SimBrief pressurization plan — cruise (flight) altitude and destination
    /// field elevation, feet; null = that value unavailable in the OFP. The 737 evaluator
    /// stores it (rounded to the panel knob steps) and serves the synthetic GetValue keys
    /// "FO_PRESS_ALTS_MATCH" / "FO_PRESS_LAND_ALT_MATCH" for checklist auto-detect; the
    /// 777's pressurization is automatic (FMC landing altitude), so its evaluator no-ops.
    /// </summary>
    void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt);
}
