using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the PMDG 737 Before Start flow's APU generator gate (2026-09-28 live report: the
/// generator wait timed out on a healthy start and the flow then dropped ground power
/// with nothing else on the buses).
///
/// FlowManager itself is not unit-testable (its ScreenReaderAnnouncer drives a real
/// screen reader), so what is pinned here is the DEFINITION the engine reads: the wait's
/// budget stays above the measured cutout-to-light interval, both steps that act on the
/// generator depend on the wait's own outcome, and the dependency points at a step that
/// really precedes them in the same flow.
/// </summary>
public class Pmdg737ApuGeneratorGateTests
{
    private static FlowDefinition<AircraftStateEvaluator> BeforeStart() =>
        PMDG737FlowDefinitions.Build().Single(f => f.Id == "BEFORE_START");

    [Fact]
    public void Generator_wait_budget_clears_the_measured_interval()
    {
        // Measured 2026-09-28, warm APU: light on 25-40 s after the selector sprang back.
        // The old 30 s budget sat on that line; a cold APU is slower.
        var wait = BeforeStart().Steps.Single(s => s.Id == "BS_APU_GEN_AVAIL");
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal("ELEC_annunAPU_GEN_OFF_BUS", wait.ConditionFieldName);
        Assert.True(wait.TimeoutSeconds >= 120, $"generator wait is {wait.TimeoutSeconds} s");
        // Skip, never Stop: the light is legitimately off on a re-run after a completed
        // transfer, and a Stop would abort every re-run (the definition's own comment).
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
    }

    [Theory]
    [InlineData("BS_APUGEN")]
    [InlineData("BS_GPU_OFF")]
    public void Generator_transfer_and_ground_power_drop_depend_on_the_wait(string stepId)
    {
        var steps = BeforeStart().Steps;
        var step = steps.Single(s => s.Id == stepId);
        Assert.Equal("BS_APU_GEN_AVAIL", step.RequiresStepId);
        Assert.False(string.IsNullOrWhiteSpace(step.RequiresStepSkipText));
        Assert.StartsWith("Skipping: ", step.RequiresStepSkipText);

        // The dependency must name a step that runs BEFORE this one in the same flow —
        // FlowManager only knows about steps it has already passed.
        int waitIndex = steps.FindIndex(s => s.Id == "BS_APU_GEN_AVAIL");
        int stepIndex = steps.FindIndex(s => s.Id == stepId);
        Assert.True(waitIndex >= 0 && waitIndex < stepIndex);
    }

    [Fact]
    public void Ground_power_skip_text_says_ground_power_stays_connected()
    {
        // A blind pilot has no other way to learn what the flow left alone.
        var gpu = BeforeStart().Steps.Single(s => s.Id == "BS_GPU_OFF");
        Assert.Contains("ground power stays connected", gpu.RequiresStepSkipText);
    }
}
