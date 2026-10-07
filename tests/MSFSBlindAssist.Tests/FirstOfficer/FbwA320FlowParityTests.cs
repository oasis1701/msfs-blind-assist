using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A32NX flows and action groups after the Airbus-card change: engine start waits for the
/// FBW engine-running state, the SimBrief takeoff flaps (with a Captain fallback), the
/// read-only gear-up confirmation, the two new action lines, and the APU lines' honest
/// "ON and available". The Fenix has the same shapes (A320FamilyParityTests keeps them equal).
/// </summary>
public class FbwA320FlowParityTests
{
    private static FlowStep<FbwA320StateEvaluator> Step(string flow, string id)
        => FbwA320FlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<FbwA320ActionExecutor, FbwA320StateEvaluator> Item(string id)
        => FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

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
        Assert.DoesNotContain(FbwA320FlowDefinitions.Build().SelectMany(f => f.Steps), s => s.Id.EndsWith("_DWELL") && s.Id.StartsWith("ES_"));
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
        var ids = FbwA320ChecklistDefinitions.Build().Single(g => g.Id == "ENGINE_START").Items.Select(i => i.Id).ToList();
        foreach (var n in new[] { 1, 2 })
            Assert.Equal(ids.IndexOf($"ES_ENG{n}") + 1, ids.IndexOf($"ES_ENG{n}_RUN"));
        Assert.True(ids.IndexOf("ES_ENG1_RUN") < ids.IndexOf("ES_ENG2"));
    }

    [Fact]
    public void Engine_running_is_unknown_with_no_data()
    {
        Assert.True(double.IsNaN(new FbwA320StateEvaluator().GetValue("FO_ENG1_RUNNING")));
        Assert.True(double.IsNaN(new FbwA320StateEvaluator().GetValue("FO_ENG2_RUNNING")));
    }

    [Theory]
    [InlineData(0, 0.0)]   // Off
    [InlineData(1, 1.0)]   // On: running
    [InlineData(2, 0.0)]   // Starting
    [InlineData(3, 0.0)]   // Shutting down
    public void Engine_running_is_true_only_for_the_FBW_On_state(double state, double expected)
        => Assert.Equal(expected, FbwA320StateEvaluator.RunningFrom(state));

    [Fact]
    public void Engine_running_from_an_unknown_state_is_unknown()
        => Assert.True(double.IsNaN(FbwA320StateEvaluator.RunningFrom(double.NaN)));

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, -1)]
    public void Takeoff_flaps_lever_index_is_the_SimBrief_setting_when_in_range(int simbrief, int expected)
    {
        var e = new FbwA320StateEvaluator();
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
        var e = new FbwA320StateEvaluator();
        Assert.Null(provider.TargetValueProvider!(e));      // no plan → silent skip, line not ticked (FO-20)
        e.SetTakeoffFlaps(2);
        Assert.Equal(2, provider.TargetValueProvider!(e));

        var fallback = Step("AFTER_START", "AS_FLAPS_CAPT");
        Assert.Equal("Flaps: set for takeoff", fallback.Label);
        Assert.Equal(FlowStepActionType.CaptainReminder, fallback.ActionType);
        Assert.False(fallback.SkipCondition!(e));            // a plan alone never skips it: lever unread
        Assert.False(fallback.SkipCondition!(new FbwA320StateEvaluator()));
    }

    /// <summary>The Captain reminder is skipped only when the flap handle READS a takeoff position.
    /// Keyed on the SimBrief plan, a write that did not take was heard as "Skipping…" then a false
    /// "Already set: Flaps: set for takeoff", and the pilot lost the reminder. A plan is loaded in
    /// every case here, so only the handle decides.</summary>
    [Theory]
    [InlineData(0.0, false)]          // handle up: the write did not take → the reminder speaks
    [InlineData(double.NaN, false)]   // unread → the reminder speaks
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]           // set for takeoff → skipped
    [InlineData(3.0, true)]
    [InlineData(4.0, false)]          // FULL is not a takeoff setting
    public void The_takeoff_flaps_reminder_is_skipped_only_when_the_handle_reads_a_takeoff_position(
        double handle, bool skipped)
    {
        Assert.Equal(skipped, FbwA320FlowDefinitions.IsTakeoffFlapsLever(handle));

        var e = new FbwA320StateEvaluator();
        e.SetTakeoffFlaps(2);
        if (!double.IsNaN(handle))
            e.SetSimConnect(SeededSimConnectCache.With(("A32NX_FLAPS_HANDLE_INDEX", handle)));
        Assert.Equal(skipped, Step("AFTER_START", "AS_FLAPS_CAPT").SkipCondition!(e));
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
        var s = FbwA320FlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF").Steps.Last();
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

    /// <summary>"APU: ON and available" is the AVAIL state, so the AVAIL wait completes it and the
    /// master write links nothing: a master that is on with an APU that never comes up must not tick
    /// (and, at flow end, latch) the line. After Landing's wait is Skip, so a timeout keeps the line
    /// out of the latch; Before Start's is Stop, so the flow ends there and latches nothing.</summary>
    [Theory]
    [InlineData("BEFORE_START", "BS_APU_MASTER", "BS_APU_AVAIL", "BS_APU", FlowStepFailurePolicy.Stop)]
    [InlineData("AFTER_LANDING", "AL_APU_MASTER", "AL_APU_AVAIL", "AL_APU", FlowStepFailurePolicy.Skip)]
    public void The_avail_wait_completes_the_apu_line_and_the_master_write_does_not(
        string flow, string master, string wait, string line, FlowStepFailurePolicy onTimeout)
    {
        var w = Step(flow, wait);
        Assert.Equal(FlowStepActionType.WaitForCondition, w.ActionType);
        Assert.Equal("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", w.ConditionFieldName);
        Assert.Equal(line, w.CompletesChecklistItemId);
        Assert.Equal(onTimeout, w.FailurePolicy);

        Assert.Equal(FlowStepActionType.SetSwitch, Step(flow, master).ActionType);
        Assert.Empty(Step(flow, master).LinkedChecklistItemIds);
        Assert.Single(FbwA320FlowDefinitions.Build().Single(f => f.Id == flow).Steps,
            s => s.LinkedChecklistItemIds.Contains(line));
    }

    [Fact]
    public void Apu_available_test_is_NaN_safe()
    {
        Assert.True(FbwA320ActionExecutor.IsApuAvailable(1));
        Assert.False(FbwA320ActionExecutor.IsApuAvailable(0));
        Assert.False(FbwA320ActionExecutor.IsApuAvailable(double.NaN));
    }

    [Fact]
    public void Apu_wait_budget_matches_the_flow_and_dwarfs_the_revert_grace()
    {
        Assert.Equal(180_000, FbwA320ActionExecutor.ApuAvailTimeoutMs);
        var wait = Step("BEFORE_START", "BS_APU_AVAIL");
        Assert.Equal("A32NX_OVHD_APU_START_PB_IS_AVAILABLE", wait.ConditionFieldName);
        Assert.Equal(FbwA320ActionExecutor.ApuAvailTimeoutMs / 1000, wait.TimeoutSeconds);
    }
}
