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
}
