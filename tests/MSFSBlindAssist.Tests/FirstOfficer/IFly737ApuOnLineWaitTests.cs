using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.SimConnect.IFly;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// iFly 737 MAX8 — "Unable to complete: APU: ON line" over a healthy APU start (2026-10-20, After
/// Landing, ticked by hand). Since the line reads the APU on line (FO_APU_ON) rather than the
/// switch, it turns true only when the blue APU GEN OFF BUS light comes on, measured 60 s after
/// START on the ground, while StartApuAsync returned at START and ChecklistManager holds a
/// hand-tick's revert only while its action runs plus 10 s (FO-11). The Fenix had the same bug.
/// </summary>
public class IFly737ApuOnLineWaitTests
{
    /// <summary>The executor waits on the field and the test the two lines judge by. If they ever
    /// diverged, the wait would end on one condition while the line was judged by another.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU")]
    [InlineData("AFTER_LANDING", "AL_APU")]
    public void Apu_lines_detect_on_what_the_executor_waits_for(string groupId, string itemId)
    {
        var item = IFly737ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

        Assert.Equal(IFly737StateEvaluator.ApuOnField, item.StateFieldName);
        foreach (double v in new[] { 0, 1, 2, double.NaN })
            Assert.Equal(item.EvaluateState(v), IFly737ActionExecutor.IsApuOnLine(v));
        Assert.True(IFly737ActionExecutor.IsApuOnLine(1));
        Assert.False(IFly737ActionExecutor.IsApuOnLine(0));
        Assert.False(IFly737ActionExecutor.IsApuOnLine(double.NaN));
    }

    /// <summary>The tick waits as long as the flows' own waits, so both paths give up together,
    /// and far longer than the 60 s the APU took or the 10 s revert grace.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU_WAIT")]
    [InlineData("AFTER_LANDING", "AL_APU_ON_LINE")]
    public void Apu_wait_budget_matches_the_flow_waits(string flowId, string stepId)
    {
        Assert.Equal(120_000, IFly737ActionExecutor.ApuOnLineTimeoutMs);

        var wait = IFly737FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);
        Assert.Equal(IFly737StateEvaluator.ApuOnField, wait.ConditionFieldName);
        Assert.Equal(IFly737ActionExecutor.ApuOnLineTimeoutMs / 1000, wait.TimeoutSeconds);
    }

    /// <summary>Why the line reverted: after landing, the engine generators carry the buses and no
    /// ground power is connected, so while the APU spools up the line reads a definite 0 (not
    /// unread), and RevertToState un-ticks it once the grace is over. It reads 1 once the APU GEN
    /// OFF BUS light comes on.</summary>
    [Fact]
    public void Spooling_apu_after_landing_reads_not_on_line_until_the_generator_light()
    {
        var sdk = IFly737SecondReviewTests.Sdk();
        sdk[IFlySdkOffsets.APU_Switch_Status] = 1;   // START has sprung back to ON
        var state = IFly737SecondReviewTests.State(sdk);
        Assert.Equal(0, state.GetValue(IFly737StateEvaluator.ApuOnField));

        sdk[IFlySdkOffsets.APU_GEN_OFF_BUS_Light_Status] = 2;
        state = IFly737SecondReviewTests.State(sdk);
        Assert.Equal(1, state.GetValue(IFly737StateEvaluator.ApuOnField));
    }

    [Fact]
    public async Task Wait_holds_through_not_on_line_and_unread_and_ends_on_line()
    {
        double[] reads = { 0, double.NaN, 0, 1 };
        int n = 0;
        Assert.True(await IFly737ActionExecutor.WaitForApuOnLineAsync(() => reads[Math.Min(n++, reads.Length - 1)], 10_000, 1));
        Assert.Equal(4, n);
    }

    [Fact]
    public async Task Wait_gives_up_at_its_budget()
    {
        int n = 0;
        Assert.False(await IFly737ActionExecutor.WaitForApuOnLineAsync(() => { n++; return 0; }, 5, 1));
        Assert.Equal(6, n);   // a read per poll, and one last read at the budget
    }

    /// <summary>A start that was never sent holds nothing open: the unavailable executor refuses
    /// at once rather than waiting out the budget.</summary>
    [Fact]
    public async Task Refused_start_does_not_wait()
    {
        var clock = Stopwatch.StartNew();
        Assert.False(await new IFly737ActionExecutor().StartApuAsync());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }
}
