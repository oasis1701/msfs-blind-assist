using System.Linq;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FenixFlowParityTests
{
    private static FlowStep<FenixStateEvaluator> Step(string flow, string id)
        => FenixFlowDefinitions.Build().Single(f => f.Id == flow).Steps.Single(s => s.Id == id);

    private static ChecklistItem<FenixActionExecutor, FenixStateEvaluator> Item(string id)
        => FenixChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == id);

    [Theory]
    [InlineData("PREFLIGHT", "PF_ECAMDOOR", "S_ECAM_DOOR", "ECAM page: door")]
    [InlineData("BEFORE_START", "BS_ECAMAPU", "S_ECAM_APU", "ECAM page: APU")]
    [InlineData("ENGINE_START", "ES_ECAMENG", "S_ECAM_ENGINE", "ECAM page: engine")]
    [InlineData("AFTER_START", "AS_ECAMSTS", "S_ECAM_STATUS", "ECAM page: status")]
    [InlineData("SHUTDOWN", "SD_ECAMDOOR", "S_ECAM_DOOR", "ECAM page: door")]
    public void Ecam_page_steps_pulse_the_page_button(string flow, string id, string key, string label)
    {
        var s = Step(flow, id);
        Assert.Equal(key, s.EventName);
        Assert.Equal(label, s.Label);
        Assert.Equal(id, s.CompletesChecklistItemId);
        Assert.NotNull(Item(id).CheckAction);
        Assert.True(FenixActionExecutor.IsMomentaryKey(key), $"{key} must be in the pulse table");
    }

    [Fact]
    public void Takeoff_flaps_have_a_captain_reminder_that_a_plan_alone_never_skips()
    {
        var fallback = Step("AFTER_START", "AS_FLAPS_CAPT");
        Assert.Equal("Flaps: set for takeoff", fallback.Label);
        Assert.Equal(FlowStepActionType.CaptainReminder, fallback.ActionType);
        var e = new FenixStateEvaluator();
        Assert.False(fallback.SkipCondition!(e));
        e.SetTakeoffFlaps(1);
        Assert.False(fallback.SkipCondition!(e));            // plan loaded, lever unread → still speaks
    }

    /// <summary>Each engine-start wait reads the Fenix's own running flag (FO_ENGn_RUNNING, the A32NX's
    /// name; N2 55 % only while the flag is unknown), stops the flow on a 180 s timeout, and completes
    /// its own "Engine n: running" line, which reads the same field (FO-6). The 180 s is assumed margin
    /// over the IAE's sourced 30 s dry crank (the Fenix offers 30 or 50 s); the in-sim check measured the
    /// whole start at about 66 s per engine.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Engine_start_waits_for_the_running_flag_and_completes_its_running_line(int n)
    {
        var s = Step("ENGINE_START", $"ES_ENG{n}_N2");
        Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
        Assert.Equal($"FO_ENG{n}_RUNNING", s.ConditionFieldName);
        Assert.True(s.Condition!(1.0));
        Assert.False(s.Condition!(0.0));
        Assert.False(s.Condition!(double.NaN));
        Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy);
        Assert.Equal(180, s.TimeoutSeconds);
        Assert.Equal(new[] { $"ES_ENG{n}_RUN" }, s.LinkedChecklistItemIds);

        var line = Item($"ES_ENG{n}_RUN");
        Assert.Equal(s.ConditionFieldName, line.StateFieldName);
        Assert.True(line.StateCondition!(1.0));
        Assert.False(line.StateCondition!(0.0));
        Assert.False(line.StateCondition!(double.NaN));
    }

    /// <summary>The Approach flow sets the seat belts (skipped when the switch already reads on, as
    /// after Descent) and completes both seat-belt lines: its own and the Approach Checklist's.</summary>
    [Fact]
    public void Approach_sets_the_seat_belts_and_completes_both_seat_belt_lines()
    {
        var s = Step("APPROACH", "AP_SEATBELTS");
        Assert.Equal("Seat belts: ON", s.Label);
        Assert.Equal("S_OH_SIGNS", s.EventName);
        Assert.Equal(1, s.TargetValue);
        Assert.Equal(new[] { "AP_SEATBELTS", "APC_SEATBELTS" }, s.LinkedChecklistItemIds);

        var e = new FenixStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(("S_OH_SIGNS", 1)));
        Assert.True(s.SkipCondition!(e));
        e.SetSimConnect(SeededSimConnectCache.With(("S_OH_SIGNS", 0)));
        Assert.False(s.SkipCondition!(e));

        var line = Item("AP_SEATBELTS");
        Assert.Equal("Seat belts: ON", line.Label);
        Assert.Equal("S_OH_SIGNS", line.StateFieldName);
        Assert.NotNull(line.CheckAction);
    }

    /// <summary>The Captain reminder is skipped only when the flap lever READS a takeoff position.
    /// Keyed on the SimBrief plan, a write that did not take was heard as "Skipping…" then a false
    /// "Already set: Flaps: set for takeoff", and the pilot lost the reminder. A plan is loaded in
    /// every case here, so only the lever decides.</summary>
    [Theory]
    [InlineData(0.0, false)]          // lever up: the write did not take → the reminder speaks
    [InlineData(double.NaN, false)]   // unread → the reminder speaks
    [InlineData(1.0, true)]
    [InlineData(2.0, true)]           // set for takeoff → skipped
    [InlineData(3.0, true)]
    [InlineData(4.0, false)]          // FULL is not a takeoff setting
    public void The_takeoff_flaps_reminder_is_skipped_only_when_the_lever_reads_a_takeoff_position(
        double lever, bool skipped)
    {
        Assert.Equal(skipped, FenixFlowDefinitions.IsTakeoffFlapsLever(lever));

        var e = new FenixStateEvaluator();
        e.SetTakeoffFlaps(2);
        if (!double.IsNaN(lever))
            e.SetSimConnect(SeededSimConnectCache.With(("S_FC_FLAPS", lever)));
        Assert.Equal(skipped, Step("AFTER_START", "AS_FLAPS_CAPT").SkipCondition!(e));
    }

    [Fact]
    public void After_takeoff_gear_check_no_longer_links_a_removed_line()
        => Assert.Null(Step("AFTER_TAKEOFF", "AT_GEAR_UP_CHECK").CompletesChecklistItemId);

    [Fact]
    public void Shutdown_tcas_and_secure_external_power_have_action_lines()
    {
        Assert.Equal("SD_TCAS_STBY", Step("SHUTDOWN", "SD_TCAS_STBY").CompletesChecklistItemId);
        Assert.Equal("TCAS: STANDBY", Item("SD_TCAS_STBY").Label);
        Assert.Equal("SC_EXTPWR_OFF", Step("SECURE", "SC_EXTPWR_OFF").CompletesChecklistItemId);
        Assert.Equal("External power: OFF", Item("SC_EXTPWR_OFF").Label);
        Assert.Equal("I_OH_ELEC_EXT_PWR_L", Item("SC_EXTPWR_OFF").StateFieldName);
    }

    [Fact]
    public void Shared_wording_matches_the_A32NX()
    {
        Assert.Equal("Weather radar: SYSTEM 1", Item("TX_WXR").Label);
        Assert.Equal("Cockpit door: LOCKED", Item("BS_COCKPITDOOR").Label);
        Assert.Equal("Cockpit door: UNLOCKED", Item("SD_COCKPITDOOR").Label);
        Assert.Equal("APU: ON and available", Item("BS_APU").Label);
        Assert.Equal("Engine 1 starting — waiting for the engine to stabilize", Step("ENGINE_START", "ES_ENG1_N2").Label);
    }
}
