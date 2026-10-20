using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.PMDG737;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// PMDG 737 NG3 — "Unable to complete: APU: ON line" about 10 s after a hand-tick over a healthy APU
/// start (owner, 2026-10-20). The line reads the APU running (FO_APU_ON: the selector at ON or START
/// and APU EGT at or above 100), which the APU reaches only some way into its start, while
/// StartApuAsync returned at START and ChecklistManager holds a hand-tick's revert only while its
/// action runs plus 10 s (FO-11). The iFly 737 MAX8 had the same bug the same day.
/// </summary>
public class Pmdg737ApuOnLineWaitTests
{
    /// <summary>A PMDG evaluator whose data manager holds a snapshot with the APU selector and EGT
    /// given (the NG3 manager takes a snapshot only from a SimConnect callback; see
    /// Boeing737SecureApuAlreadyOffTests).</summary>
    private static AircraftStateEvaluator State(byte apuSelector, float apuEgt)
    {
        var dm = new PMDGNG3DataManager();
        object snapshot = new PMDGNG3DataStruct { APU_Selector = apuSelector, APU_EGTNeedle = apuEgt, ELEC_BatSelector = 1 };
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(PMDGNG3DataManager).GetField("_lastDataSnapshot", Private)!.SetValue(dm, snapshot);
        typeof(PMDGNG3DataManager).GetField("_hasSnapshot", Private)!.SetValue(dm, true);
        var state = new AircraftStateEvaluator();
        state.SetDataManager(dm);
        return state;
    }

    /// <summary>The executor waits on the field and the test the two lines judge by. If they ever
    /// diverged, the wait would end on one condition while the line was judged by another.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU")]
    [InlineData("AFTER_LANDING", "AL_APU")]
    public void Apu_lines_detect_on_what_the_executor_waits_for(string groupId, string itemId)
    {
        var item = PMDG737ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

        Assert.Equal(AircraftStateEvaluator.ApuOnField, item.StateFieldName);
        foreach (double v in new[] { 0, 1, double.NaN })
            Assert.Equal(item.EvaluateState(v), AircraftActionExecutor.IsApuOnLine(v));
        Assert.True(AircraftActionExecutor.IsApuOnLine(1));
        Assert.False(AircraftActionExecutor.IsApuOnLine(0));
        Assert.False(AircraftActionExecutor.IsApuOnLine(double.NaN));
    }

    /// <summary>The tick waits as long as After Landing's own wait for the same line, so both paths
    /// give up together, and far longer than the 10 s revert grace. Before Start's wait reads the
    /// same field, after its own wait for the selector to spring back to ON.</summary>
    [Fact]
    public void Apu_wait_budget_matches_the_flow_wait()
    {
        Assert.Equal(120_000, AircraftActionExecutor.ApuOnLineTimeoutMs);

        var flows = PMDG737FlowDefinitions.Build();
        var al = flows.Single(f => f.Id == "AFTER_LANDING").Steps.Single(s => s.Id == "AL_APU_RUNNING");
        Assert.Equal(AircraftStateEvaluator.ApuOnField, al.ConditionFieldName);
        Assert.Equal(AircraftActionExecutor.ApuOnLineTimeoutMs / 1000, al.TimeoutSeconds);

        var bs = flows.Single(f => f.Id == "BEFORE_START").Steps.Single(s => s.Id == "BS_APU_RUNNING");
        Assert.Equal(AircraftStateEvaluator.ApuOnField, bs.ConditionFieldName);
    }

    /// <summary>Why the line reverted: while the APU spools up the selector reads ON or START but the
    /// EGT is still below 100, a definite 0 (not unread), so RevertToState un-ticks it once the grace
    /// is over. It reads 1 once the EGT reaches 100.</summary>
    [Theory]
    [InlineData(2)]   // START
    [InlineData(1)]   // back at ON
    public void Spooling_apu_reads_not_on_line_until_its_egt_reaches_100(byte selector)
    {
        Assert.Equal(0, State(selector, 60).GetValue(AircraftStateEvaluator.ApuOnField));
        Assert.Equal(1, State(selector, (float)AircraftStateEvaluator.ApuRunningEgt).GetValue(AircraftStateEvaluator.ApuOnField));
    }

    [Fact]
    public async Task Wait_holds_through_not_on_line_and_unread_and_ends_on_line()
    {
        double[] reads = { 0, double.NaN, 0, 1 };
        int n = 0;
        Assert.True(await AircraftActionExecutor.WaitForApuOnLineAsync(() => reads[Math.Min(n++, reads.Length - 1)], 10_000, 1));
        Assert.Equal(4, n);
    }

    [Fact]
    public async Task Wait_gives_up_at_its_budget()
    {
        int n = 0;
        Assert.False(await AircraftActionExecutor.WaitForApuOnLineAsync(() => { n++; return 0; }, 5, 1));
        Assert.Equal(6, n);   // a read per poll, and one last read at the budget
    }

    /// <summary>A start that was never sent holds nothing open: with no SimConnect the selector
    /// writes are refused, and the tick fails at once rather than waiting out the budget.</summary>
    [Fact]
    public async Task Refused_start_does_not_wait()
    {
        var clock = Stopwatch.StartNew();
        Assert.False(await new AircraftActionExecutor().StartApuAsync(State(0, 0)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }
}
