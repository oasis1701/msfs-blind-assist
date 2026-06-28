namespace MSFSBlindAssist.FirstOfficer;

public interface IFoStateEvaluator
{
    bool IsAvailable { get; }
    double GetValue(string field);
    bool IsOn(string field);
    bool IsPosition(string field, int position);

    /// <summary>Store the SimBrief planned takeoff flap setting (used by the Before Taxi flow).</summary>
    void SetTakeoffFlaps(int flaps);
}
