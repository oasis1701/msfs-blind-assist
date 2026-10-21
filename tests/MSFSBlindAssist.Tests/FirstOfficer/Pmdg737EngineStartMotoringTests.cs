using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the PMDG 737 Engine Start flow's fuel-introduction timing (2026-09-28 live
/// measurement): the NG3 writes the stock TURB ENG N2 only from light-off, so an "N2 before
/// fuel" wait reads 0 for the whole crank and stops every start. Each engine is instead
/// motored for a fixed time counted from its start valve reading open, and nothing between
/// the start switch and the start lever may read an N2 field.
/// </summary>
public class Pmdg737EngineStartMotoringTests
{
    private static System.Collections.Generic.List<FlowStep<AircraftStateEvaluator>> EngineStart() =>
        PMDG737FlowDefinitions.Build().Single(f => f.Id == "ENGINE_START").Steps;

    [Theory]
    [InlineData("ES_E2_GRD", "ES_E2_VALVE", "ES_E2_MOTOR", "ES_E2_RUN")]
    [InlineData("ES_E1_GRD", "ES_E1_VALVE", "ES_E1_MOTOR", "ES_E1_RUN")]
    public void Each_engine_motors_for_a_fixed_time_between_valve_open_and_fuel(
        string grdId, string valveId, string motorId, string runId)
    {
        var steps = EngineStart();
        int grd = steps.FindIndex(s => s.Id == grdId);
        int valve = steps.FindIndex(s => s.Id == valveId);
        int motor = steps.FindIndex(s => s.Id == motorId);
        int run = steps.FindIndex(s => s.Id == runId);
        Assert.True(grd >= 0 && grd < valve && valve < motor && motor < run,
            $"order: grd={grd} valve={valve} motor={motor} run={run}");

        // The valve wait is still what refuses a start the starter never engaged.
        Assert.Equal(FlowStepActionType.WaitForCondition, steps[valve].ActionType);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[valve].FailurePolicy);

        // A fixed motoring time, long enough for the ~25 % N2 the procedure asks for.
        Assert.Equal(FlowStepActionType.WaitSeconds, steps[motor].ActionType);
        Assert.InRange(steps[motor].WaitSeconds, 12, 30);

        // Nothing between the start switch and the lever reads N2: it is 0 while motoring.
        foreach (var s in steps.Skip(grd).Take(run - grd))
            Assert.False((s.ConditionFieldName ?? "").Contains("N2"),
                $"{s.Id} reads {s.ConditionFieldName} before fuel");
    }
}
