using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 Engine Start flow starts engines 3 and 4 only once engines 1 and 2 RUN (FlyByWire's
/// <c>A32NX_ENGINE_STATE:n</c> reads On, 1, on both — in both installed builds), never after a fixed
/// pause; a pair that does not run in time STOPS the flow aloud instead of starting the next pair on
/// top of a failed start, and an unread state counts as not running.
/// </summary>
public class FbwA380EngineStartWaitTests
{
    private static FlowDefinition<FbwA380StateEvaluator> Flow() =>
        FbwA380FlowDefinitions.Build().Single(f => f.Id == "ENGINE_START");

    [Fact]
    public void Each_pair_is_followed_by_a_read_only_stop_wait_on_its_engines_running()
    {
        var ids = Flow().Steps.Select(s => s.Id).ToList();
        Assert.DoesNotContain(Flow().Steps, s => s.ActionType == FlowStepActionType.WaitSeconds);
        Assert.Equal(ids.IndexOf("ES_ENG2") + 1, ids.IndexOf("ES_ENG12_RUNNING"));
        Assert.Equal(ids.IndexOf("ES_ENG12_RUNNING") + 1, ids.IndexOf("ES_ENG3"));
        Assert.Equal(ids.Count - 1, ids.IndexOf("ES_ENG34_RUNNING"));
        foreach (var (id, field) in new[] { ("ES_ENG12_RUNNING", FbwA380StateEvaluator.Engines12RunningField),
                                            ("ES_ENG34_RUNNING", FbwA380StateEvaluator.Engines34RunningField) })
        {
            var s = Flow().Steps.Single(x => x.Id == id);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy);
            Assert.Equal(FbwA380FlowDefinitions.EngineStartTimeoutSeconds, s.TimeoutSeconds);
            Assert.False(s.Condition!(double.NaN));   // unread is not running
            Assert.False(s.Condition!(0));
            Assert.True(s.Condition!(1));
            Assert.Null(s.SkipCondition);
            Assert.Empty(s.LinkedChecklistItemIds);
        }
    }

    [Theory]
    [InlineData(1, 1, 1.0)]
    [InlineData(2, 1, 0.0)]   // still starting
    [InlineData(1, 0, 0.0)]
    [InlineData(4, 1, 0.0)]   // shutting down
    public void A_pair_runs_only_when_both_engine_states_read_on(double a, double b, double expected)
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("A32NX_ENGINE_STATE:1", a), ("A32NX_ENGINE_STATE:2", b),
            ("A32NX_ENGINE_STATE:3", a), ("A32NX_ENGINE_STATE:4", b)));
        Assert.Equal(expected, e.GetValue(FbwA380StateEvaluator.Engines12RunningField));
        Assert.Equal(expected, e.GetValue(FbwA380StateEvaluator.Engines34RunningField));
    }

    [Fact]
    public void An_unread_engine_state_is_not_running()
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("A32NX_ENGINE_STATE:1", 1)));
        Assert.True(double.IsNaN(e.GetValue(FbwA380StateEvaluator.Engines12RunningField)));
        Assert.True(double.IsNaN(e.GetValue(FbwA380StateEvaluator.Engines34RunningField)));
    }

    [Fact]
    public async Task A_pair_that_does_not_run_stops_the_flow_aloud_before_the_next_pair()
    {
        // Masters 1 and 2 already at START (so those steps say already set), engine 2 still starting:
        // the wait times out (shortened here), the flow STOPS, and engines 3 and 4 are never pressed.
        var def = new RecordingA380Definition();
        var r = await FbwA380FlowRun.Run("ENGINE_START", "ENGINE_START",
            new[] { "ES_ENG1", "ES_ENG2", "ES_ENG12_RUNNING", "ES_ENG3", "ES_ENG4" },
            new[] { ("ENG_VALVE_SWITCH:1", 1.0), ("ENG_VALVE_SWITCH:2", 1.0),
                    ("A32NX_ENGINE_STATE:1", 1.0), ("A32NX_ENGINE_STATE:2", 2.0) },
            windowHasTicked: false, waitTimeoutSeconds: 1, writesLandOn: def);
        Assert.DoesNotContain(def.Writes, w => w.Key is "ENG_VALVE_SWITCH:3" or "ENG_VALVE_SWITCH:4");
        Assert.Contains(r.Speech.All, l => l.Contains("Engine Start flow stopped. Unable to complete: Engines 1 and 2 running"));
    }

    [Fact]
    public async Task Engines_already_running_pass_the_wait_and_the_next_pair_starts()
    {
        var def = new RecordingA380Definition();
        await FbwA380FlowRun.Run("ENGINE_START", "ENGINE_START",
            new[] { "ES_ENG12_RUNNING", "ES_ENG3", "ES_ENG4" },
            new[] { ("A32NX_ENGINE_STATE:1", 1.0), ("A32NX_ENGINE_STATE:2", 1.0),
                    ("ENG_VALVE_SWITCH:3", 0.0), ("ENG_VALVE_SWITCH:4", 0.0) },
            windowHasTicked: false, waitTimeoutSeconds: 5, writesLandOn: def);
        Assert.Contains(def.Writes, w => w.Key == "ENG_VALVE_SWITCH:3" && w.Value == 1);
        Assert.Contains(def.Writes, w => w.Key == "ENG_VALVE_SWITCH:4" && w.Value == 1);
    }
}
