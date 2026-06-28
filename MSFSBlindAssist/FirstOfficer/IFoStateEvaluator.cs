namespace MSFSBlindAssist.FirstOfficer;

public interface IFoStateEvaluator
{
    bool IsAvailable { get; }
    double GetValue(string field);
    bool IsOn(string field);
    bool IsPosition(string field, int position);
}
