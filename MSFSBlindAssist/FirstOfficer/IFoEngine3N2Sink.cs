namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Optional: a First Officer state evaluator for a THREE-engine aircraft (the TFDi MD-11) receives
/// engine 3's N2 through this. The two-engine <see cref="IFoStateEvaluator.SetEngineN2"/> is
/// unchanged, so the seven two-engine evaluators need no edit.
/// </summary>
public interface IFoEngine3N2Sink
{
    void SetEngine3N2(double eng3N2);
}
