using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Headwind A330 flows and action groups after the Airbus-card change, line for line with
/// the A32NX (FbwA320FlowParityTests): engine start waits for the FBW engine-running state, the
/// SimBrief takeoff flaps (with a Captain fallback), the read-only gear-up confirmation, the
/// two new action lines, and the APU lines' honest "ON and available". HwA330ParityTests keeps
/// the step and item ids equal; these pin the A330's own timings, wording and detection.
/// </summary>
public class HwA330FlowParityTests
{
    private static FlowStep<HwA330StateEvaluator> Step(string flow, string id)
        => HwA330FlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<HwA330ActionExecutor, HwA330StateEvaluator> Item(string id)
        => HwA330ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Fact]
    public void Engine_start_waits_for_the_FBW_engine_running_state_and_stops_on_timeout()
    {
        foreach (var n in new[] { 1, 2 })
        {
            var s = Step("ENGINE_START", $"ES_ENG{n}_N2");
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal($"FO_ENG{n}_RUNNING", s.ConditionFieldName);
            Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy);
            Assert.Equal(120, s.TimeoutSeconds);
            Assert.Equal($"Engine {n} starting — waiting for the engine to stabilize", s.Label);
            Assert.Equal($"FO_ENG{n}_RUNNING", Item($"ES_ENG{n}_RUN").StateFieldName);
        }
        Assert.DoesNotContain(HwA330FlowDefinitions.Build().SelectMany(f => f.Steps), s => s.Id.EndsWith("_DWELL") && s.Id.StartsWith("ES_"));
    }

    [Fact]
    public void Engine_running_lines_are_action_free_detect_only_items()
    {
        foreach (var n in new[] { 1, 2 })
        {
            var item = Item($"ES_ENG{n}_RUN");
            Assert.Equal($"Engine {n}: running", item.Label);
            Assert.Equal("ENGINE_START", item.GroupId);
            Assert.Null(item.CheckAction);
            Assert.True(item.EvaluateState(1));
            Assert.False(item.EvaluateState(0));
        }
    }

    [Fact]
    public void Each_engine_running_line_directly_follows_its_engine_master_line()
    {
        // The Fenix's order, and the flow's: engine 1 master, engine 1 running, engine 2 master, engine 2 running.
        var ids = HwA330ChecklistDefinitions.Build().Single(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        foreach (var n in new[] { 1, 2 })
            Assert.Equal(ids.IndexOf($"ES_ENG{n}") + 1, ids.IndexOf($"ES_ENG{n}_RUN"));
        Assert.True(ids.IndexOf("ES_ENG1_RUN") < ids.IndexOf("ES_ENG2"));
    }

    [Fact]
    public void Engine_running_is_unknown_with_no_data()
    {
        Assert.True(double.IsNaN(new HwA330StateEvaluator().GetValue("FO_ENG1_RUNNING")));
        Assert.True(double.IsNaN(new HwA330StateEvaluator().GetValue("FO_ENG2_RUNNING")));
    }

    [Theory]
    [InlineData(0, 0.0)]   // Off
    [InlineData(1, 1.0)]   // On: running
    [InlineData(2, 0.0)]   // Starting
    [InlineData(3, 0.0)]   // Shutting down
    public void Engine_running_is_true_only_for_the_FBW_On_state(double state, double expected)
        => Assert.Equal(expected, HwA330StateEvaluator.RunningFrom(state));

    [Fact]
    public void Engine_running_from_an_unknown_state_is_unknown()
        => Assert.True(double.IsNaN(HwA330StateEvaluator.RunningFrom(double.NaN)));

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, -1)]
    public void Takeoff_flaps_lever_index_is_the_SimBrief_setting_when_in_range(int simbrief, int expected)
    {
        var e = new HwA330StateEvaluator();
        e.SetTakeoffFlaps(simbrief);
        Assert.Equal(expected, e.TakeoffFlapsLeverIndex());
    }

    [Fact]
    public void After_start_sets_simbrief_takeoff_flaps_with_a_captain_fallback()
    {
        var provider = Step("AFTER_START", "AS_FLAPS");
        Assert.Equal("A32NX_FLAPS_HANDLE_INDEX", provider.EventName);
        Assert.NotNull(provider.TargetValueProvider);
        Assert.Equal("AS_FLAPS", provider.CompletesChecklistItemId);
        var e = new HwA330StateEvaluator();
        Assert.Null(provider.TargetValueProvider!(e));      // no plan → quiet skip
        e.SetTakeoffFlaps(2);
        Assert.Equal(2, provider.TargetValueProvider!(e));

        var fallback = Step("AFTER_START", "AS_FLAPS_CAPT");
        Assert.Equal("Flaps: set for takeoff", fallback.Label);
        Assert.Equal(FlowStepActionType.CaptainReminder, fallback.ActionType);
        Assert.True(fallback.SkipCondition!(e));             // plan known → no reminder
        Assert.False(fallback.SkipCondition!(new HwA330StateEvaluator()));
    }

    [Fact]
    public void The_flaps_action_line_detects_a_set_lever_and_fires_the_flows_own_write()
    {
        var item = Item("AS_FLAPS");
        Assert.Equal("Flaps: takeoff setting", item.Label);
        Assert.Equal("A32NX_FLAPS_HANDLE_INDEX", item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.False(item.EvaluateState(0));                 // flaps up
        Assert.True(item.EvaluateState(1));
        Assert.True(item.EvaluateState(3));
        Assert.False(item.EvaluateState(4));                 // full
    }

    [Fact]
    public void After_takeoff_ends_with_a_read_only_gear_up_check()
    {
        var s = HwA330FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();
        Assert.Equal("AT_GEAR_UP_CHECK", s.Id);
        Assert.Equal("Landing gear: UP", s.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
        Assert.Equal(FbwA320GearConfirmation.UpField, s.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
        Assert.Equal(20, s.TimeoutSeconds);
        Assert.Null(s.EventName);
        Assert.Empty(s.MultiActions);
        Assert.Null(s.CompletesChecklistItemId);
    }

    [Fact]
    public void Shutdown_tcas_and_secure_external_power_have_action_lines()
    {
        Assert.Equal("SD_TCAS_STBY", Step("SHUTDOWN", "SD_TCAS_STBY").CompletesChecklistItemId);
        Assert.Equal("TCAS: STANDBY", Item("SD_TCAS_STBY").Label);
        Assert.NotNull(Item("SD_TCAS_STBY").CheckAction);
        Assert.Equal("SC_EXTPWR_OFF", Step("SECURE", "SC_EXTPWR_OFF").CompletesChecklistItemId);
        Assert.Equal("External power: OFF", Item("SC_EXTPWR_OFF").Label);
        Assert.NotNull(Item("SC_EXTPWR_OFF").CheckAction);
    }

    [Fact]
    public void The_new_action_lines_detect_the_state_the_flow_writes()
    {
        var tcas = Item("SD_TCAS_STBY");
        Assert.Equal("A32NX_SWITCH_TCAS_POSITION", tcas.StateFieldName);
        Assert.True(tcas.EvaluateState(0));
        Assert.False(tcas.EvaluateState(2));

        var ext = Item("SC_EXTPWR_OFF");
        Assert.Equal("A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", ext.StateFieldName);
        Assert.True(ext.EvaluateState(0));
        Assert.False(ext.EvaluateState(1));
    }

    [Fact]
    public void Shared_wording_matches_the_Fenix()
    {
        Assert.Equal("APU: ON and available", Item("BS_APU").Label);
        Assert.Equal("APU: ON and available", Item("AL_APU").Label);
        Assert.Equal("Weather radar: SYSTEM 1", Item("BT_WXR").Label);
        Assert.Equal("Cockpit door: LOCKED", Item("BS_COCKPITDOOR").Label);
        Assert.Equal("Cockpit door: UNLOCKED", Item("SD_COCKPITDOOR").Label);
    }

    // ==================================================================
    // The APU lines say what both flows do: wait for AVAIL (FO-11)
    // ==================================================================

    [Theory]
    [InlineData("BS_APU")]
    [InlineData("AL_APU")]
    public void The_apu_lines_detect_on_the_avail_lamp_and_start_the_apu(string itemId)
    {
        var item = Item(itemId);
        Assert.Equal("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", item.StateFieldName);
        Assert.Equal("APU: ON and available", item.Label);
        Assert.NotNull(item.CheckAction);
        Assert.True(item.EvaluateState(1));
        Assert.False(item.EvaluateState(0));
    }

    [Fact]
    public void The_flows_still_complete_the_apu_lines()
    {
        Assert.Equal("BS_APU", Step("BEFORE_START", "BS_APU_MASTER").CompletesChecklistItemId);
        Assert.Equal("AL_APU", Step("AFTER_LANDING", "AL_APU_MASTER").CompletesChecklistItemId);
    }

    [Fact]
    public void Apu_available_test_is_NaN_safe()
    {
        Assert.True(HwA330ActionExecutor.IsApuAvailable(1));
        Assert.False(HwA330ActionExecutor.IsApuAvailable(0));
        Assert.False(HwA330ActionExecutor.IsApuAvailable(double.NaN));
    }

    [Fact]
    public void Apu_wait_budget_matches_the_flow_and_dwarfs_the_revert_grace()
    {
        Assert.Equal(180_000, HwA330ActionExecutor.ApuAvailTimeoutMs);
        var wait = Step("BEFORE_START", "BS_APU_AVAIL");
        Assert.Equal("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", wait.ConditionFieldName);
        Assert.Equal(HwA330ActionExecutor.ApuAvailTimeoutMs / 1000, wait.TimeoutSeconds);
    }
}
