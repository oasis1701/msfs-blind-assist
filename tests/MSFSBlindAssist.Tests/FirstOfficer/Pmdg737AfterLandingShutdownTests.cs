// PMDG 737 First Officer against PMDG's own B738_Checklist.xml (the 700, 800 and 900 ship the same
// file), audited 2026-10-19 (owner request). Its After Landing Procedure opens with "Speed Brake
// Lever: Down" (18.1) and its Shutdown Procedure turns the "ANTI COLLISION light switch: OFF" (19.8)
// and the "FLIGHT DIRECTOR switches: OFF" (19.21); the First Officer did none of the three, so the
// speed brake stayed wherever the landing left it and the beacon and flight directors stayed on into
// the next departure. Before Taxi's "Lower display unit: SYS" only ever set the lower display unit
// selector to NORM (the SYS page is a different button), and the After Landing description promised
// "autothrottle/FD off" that no step does.

using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

using Pmdg737Eval = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftStateEvaluator;
using Pmdg737Exec = MSFSBlindAssist.FirstOfficer.PMDG737.AircraftActionExecutor;
using Pmdg737Flows = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737FlowDefinitions;
using Pmdg737Checklist = MSFSBlindAssist.FirstOfficer.PMDG737.PMDG737ChecklistDefinitions;

namespace MSFSBlindAssist.Tests;

public class Pmdg737AfterLandingShutdownTests
{
    private static System.Collections.Generic.List<string> FlowStepIds(string flowId) =>
        Pmdg737Flows.Build().Single(f => f.Id == flowId).Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds(string groupId) =>
        Pmdg737Checklist.Build().Single(g => g.Id == groupId).Items.Select(i => i.Id).ToList();

    private static FlowStep<Pmdg737Eval> Step(string flowId, string stepId) =>
        Pmdg737Flows.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static ChecklistItem<Pmdg737Exec, Pmdg737Eval> Item(string groupId, string itemId) =>
        Pmdg737Checklist.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    private static Pmdg737Eval LeverReading(double? lever)
    {
        var eval = new Pmdg737Eval();
        eval.SetCachedValueSource(key => key == SpeedbrakeLeverState.Pmdg737.LeverKey ? lever : null);
        return eval;
    }

    // --- After Landing: speed brake lever DOWN, first, as PMDG's page opens ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, false)]   // ARM
    [InlineData(400, false)]   // fully deployed after an auto-speedbrake landing
    [InlineData(double.NaN, false)]
    public void After_landing_line_reads_the_lever_down(double lever, bool expected)
    {
        var item = Item("AFTER_LANDING", "AL_SPDBRK");
        Assert.Equal("Speed brake lever: DOWN", item.Label);
        Assert.Equal(SpeedbrakeLeverState.LeverField, item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(lever));
    }

    [Fact]
    public void After_landing_stows_the_speed_brake_first_and_verifies_it()
    {
        Assert.Equal("AL_SPDBRK_DOWN", FlowStepIds("AFTER_LANDING")[0]);
        Assert.Equal("AL_SPDBRK", ItemIds("AFTER_LANDING")[0]);

        var step = Step("AFTER_LANDING", "AL_SPDBRK_DOWN");
        Assert.Equal("EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN", step.EventName);
        Assert.Equal(new[] { "AL_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(SpeedbrakeLeverState.LeverField, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(400));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    // "Already set" only over a lever READ down; a lever nobody can read is never clicked.
    [Fact]
    public void After_landing_never_clicks_an_unread_lever()
    {
        var step = Step("AFTER_LANDING", "AL_SPDBRK_DOWN");
        Assert.True(step.SkipCondition!(LeverReading(0)));
        Assert.False(step.SkipCondition!(LeverReading(400)));
        Assert.False(step.SkipCondition!(LeverReading(null)));
        Assert.True(step.LeaveAloneWhen!(LeverReading(null)));
        Assert.False(step.LeaveAloneWhen!(LeverReading(400)));
    }

    // --- Shutdown: anti-collision OFF right after the seat belts (19.7, 19.8) ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(double.NaN, false)]
    public void Shutdown_line_reads_the_anti_collision_light_off(double value, bool expected)
    {
        var item = Item("SHUTDOWN", "SD_ANTICOL");
        Assert.Equal("Anti-collision light: OFF", item.Label);
        Assert.Equal("LTS_AntiCollisionSw", item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Shutdown_turns_the_anti_collision_light_off_after_the_seat_belts()
    {
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(sd.IndexOf("SD_BELTS") + 1, sd.IndexOf("SD_ANTICOL_OFF"));
        var items = ItemIds("SHUTDOWN");
        Assert.Equal(items.IndexOf("SD_BELTS") + 1, items.IndexOf("SD_ANTICOL"));

        var step = Step("SHUTDOWN", "SD_ANTICOL_OFF");
        Assert.Equal("EVT_OH_LIGHTS_ANT_COL", step.EventName);
        Assert.Equal(0, step.TargetValue);
        Assert.Equal(new[] { "SD_ANTICOL" }, step.LinkedChecklistItemIds.ToArray());
    }

    // --- Shutdown: flight directors OFF, just before the transponder (19.21, 19.22) ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(double.NaN, false)]
    public void Shutdown_line_reads_both_flight_directors_off(double value, bool expected)
    {
        var item = Item("SHUTDOWN", "SD_FD");
        Assert.Equal("Flight directors: OFF", item.Label);
        Assert.Equal("MCP_FDSw_0", item.StateFieldName);
        Assert.Contains("MCP_FDSw_1", item.AdditionalStateFields);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Shutdown_presses_each_flight_director_off_before_the_transponder()
    {
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(new[] { "SD_FD1", "SD_FD2", "SD_XPDR" }, sd.Skip(sd.IndexOf("SD_FD1")).Take(3).ToArray());
        var items = ItemIds("SHUTDOWN");
        Assert.Equal(items.IndexOf("SD_FD") + 1, items.IndexOf("SD_XPDR"));

        foreach (var (id, ev) in new[] { ("SD_FD1", "EVT_MCP_FD_SWITCH_L"), ("SD_FD2", "EVT_MCP_FD_SWITCH_R") })
        {
            var step = Step("SHUTDOWN", id);
            Assert.Equal(ev, step.EventName);
            Assert.True(step.UsesMouseFlag);
            Assert.Equal(new[] { "SD_FD" }, step.LinkedChecklistItemIds.ToArray());
            // A TOGGLE: never pressed over a switch nobody can read, never "Already set" there.
            var unread = new Pmdg737Eval();
            Assert.True(step.LeaveAloneWhen!(unread));
            Assert.False(step.SkipCondition!(unread));
        }
    }

    // --- Shutdown: the hydraulic panel as PMDG sets it (19.14 engine pumps ON, 19.15 electric OFF) ---

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(double.NaN, false)]
    public void Shutdown_line_reads_the_engine_hydraulic_pumps_on(double value, bool expected)
    {
        var item = Item("SHUTDOWN", "SD_HYDENG");
        Assert.Equal("Engine hydraulic pumps: ON", item.Label);
        Assert.Equal("HYD_PumpSw_eng_0", item.StateFieldName);
        Assert.Contains("HYD_PumpSw_eng_1", item.AdditionalStateFields);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Shutdown_leaves_the_engine_pumps_on_then_turns_the_electric_pumps_off()
    {
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(sd.IndexOf("SD_HYDENG_ON") + 1, sd.IndexOf("SD_HYDELEC_OFF"));
        Assert.DoesNotContain("SD_HYDENG_OFF", sd);
        var items = ItemIds("SHUTDOWN");
        Assert.Equal(items.IndexOf("SD_HYDENG") + 1, items.IndexOf("SD_HYDELEC"));

        var step = Step("SHUTDOWN", "SD_HYDENG_ON");
        Assert.Equal(new[] { ("EVT_OH_HYD_ENG1", (int?)1), ("EVT_OH_HYD_ENG2", (int?)1) },
            step.MultiActions.Select(a => (a.EventName, a.TargetValue)).ToArray());
        Assert.Equal(new[] { "SD_HYDENG" }, step.LinkedChecklistItemIds.ToArray());
    }

    // --- The two descriptions that said what nothing does ---

    [Fact]
    public void Before_taxi_names_the_lower_display_unit_selector_it_sets()
    {
        Assert.Equal("Lower display unit selector: NORM", Item("BEFORE_TAXI", "BT_LOWERDU").Label);
        var step = Step("BEFORE_TAXI", "BT_LOWERDU");
        Assert.Equal("Lower display unit selector: NORM", step.Label);
        Assert.Equal("EVT_DSP_CPT_LOWER_DU_SELECTOR", step.EventName);
        Assert.Equal(1, step.TargetValue);   // ENG PRI 0 / NORM 1 / ND 2
    }

    [Fact]
    public void After_landing_description_promises_only_what_its_steps_do()
    {
        string d = Pmdg737Flows.Build().Single(f => f.Id == "AFTER_LANDING").Description;
        Assert.DoesNotContain("autothrottle", d, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FD", d, System.StringComparison.Ordinal);
        Assert.Contains("speed brake", d, System.StringComparison.OrdinalIgnoreCase);
    }
}
