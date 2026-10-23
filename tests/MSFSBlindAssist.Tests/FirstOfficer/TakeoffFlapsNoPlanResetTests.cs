using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Generic;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// <c>IFoStateEvaluator.SetTakeoffFlaps(0)</c> means "no plan": the First Officer window calls it when a
/// flight ends or a new one loads (and for a plan without takeoff flaps), so the next flight never
/// sets the previous flight's flaps. Until 2026-10-18 nothing cleared it, and a second flight in the
/// same session set the first flight's takeoff flaps. Each evaluator goes back to its own no-plan value.
/// </summary>
public class TakeoffFlapsNoPlanResetTests
{
    public static TheoryData<IFoStateEvaluator, int> Evaluators => new()
    {
        { new AircraftStateEvaluator(), 5 },                                          // PMDG 777
        { new MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator(), 5 },      // PMDG 737
        { new MSFSBlindAssist.FirstOfficer.IFly737.IFly737StateEvaluator(), 5 },       // iFly 737
        { new FbwA320StateEvaluator(), -1 },                                           // the L:var family
    };

    [Theory]
    [MemberData(nameof(Evaluators))]
    public void Zero_forgets_the_plans_flaps(IFoStateEvaluator evaluator, int noPlan)
    {
        evaluator.SetTakeoffFlaps(15);
        Assert.Equal(15, TakeoffFlaps(evaluator));
        evaluator.SetTakeoffFlaps(0);
        Assert.Equal(noPlan, TakeoffFlaps(evaluator));
    }

    private static int TakeoffFlaps(IFoStateEvaluator e) => e switch
    {
        AircraftStateEvaluator b777 => b777.GetTakeoffFlaps(),
        MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator b737 => b737.GetTakeoffFlaps(),
        MSFSBlindAssist.FirstOfficer.IFly737.IFly737StateEvaluator ifly => ifly.GetTakeoffFlaps(),
        LVarStateEvaluator lvar => lvar.GetTakeoffFlaps(),
        _ => throw new System.InvalidOperationException(e.GetType().Name),
    };
}
