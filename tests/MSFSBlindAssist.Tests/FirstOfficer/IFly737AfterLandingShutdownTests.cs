using System;
using System.Linq;
using Xunit;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect.IFly;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// iFly 737 MAX8: the three lines the PMDG 737's First Officer gained from PMDG's own
/// B738_Checklist.xml on 2026-10-19 (owner request), built from the iFly's own switches. After
/// Landing opens with "Speed brake lever: DOWN" (PMDG 18.1), written through the panel's own lever
/// write (Spoiler_Lever_Status, 0 DOWN, the scale the verified ARM writes 34 on); Shutdown turns
/// the anti-collision light OFF right after the belts (19.8) and both flight directors OFF before
/// the transponder (19.21), absolute SETs on this airframe.
/// </summary>
public class IFly737AfterLandingShutdownTests
{
    private const string Lever = IFly737SpeedBrakeLever.FieldName;

    private static System.Collections.Generic.List<string> FlowStepIds(string flowId) =>
        IFly737FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Select(s => s.Id).ToList();

    private static System.Collections.Generic.List<string> ItemIds(string groupId) =>
        IFly737ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Select(i => i.Id).ToList();

    private static FlowStep<IFly737StateEvaluator> Step(string flowId, string stepId) =>
        IFly737FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    private static ChecklistItem<IFly737ActionExecutor, IFly737StateEvaluator> Item(string groupId, string itemId) =>
        IFly737ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    private static IFly737StateEvaluator LeverReading(int lever)
    {
        var data = new byte[IFlySdkOffsets.StructSize];
        BitConverter.GetBytes(lever).CopyTo(data, IFlySdkOffsets.Spoiler_Lever_Status);
        var snap = new IFlySdkSnapshot(data);
        var eval = new IFly737StateEvaluator();
        eval.SnapshotSource = () => snap;
        eval.ReadySource = () => true;
        return eval;
    }

    // --- After Landing: speed brake lever DOWN, first ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(34, false)]    // ARM
    [InlineData(224, false)]   // fully deployed after an auto-speedbrake landing
    [InlineData(double.NaN, false)]
    public void After_landing_line_reads_the_lever_down(double lever, bool expected)
    {
        var item = Item("AFTER_LANDING", "AL_SPDBRK");
        Assert.Equal("Speed brake lever: DOWN", item.Label);
        Assert.Equal(Lever, item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(lever));
    }

    [Fact]
    public void After_landing_stows_the_speed_brake_first_and_verifies_it()
    {
        Assert.Equal("AL_SPDBRK_DOWN", FlowStepIds("AFTER_LANDING")[0]);
        Assert.Equal("AL_SPDBRK", ItemIds("AFTER_LANDING")[0]);

        var step = Step("AFTER_LANDING", "AL_SPDBRK_DOWN");
        Assert.Equal(Lever, step.EventName);
        Assert.Equal(0, step.TargetValue);
        Assert.Equal(IFly737SpeedBrakeLever.Detents[0].Value, step.TargetValue!.Value);
        Assert.Equal(new[] { "AL_SPDBRK" }, step.LinkedChecklistItemIds.ToArray());
        Assert.Equal(Lever, step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(224));
        Assert.False(step.VerifyCondition!(double.NaN));
    }

    // "Already set" only over a lever READ down; a lever nobody can read is never written.
    [Fact]
    public void After_landing_never_writes_an_unread_lever()
    {
        var step = Step("AFTER_LANDING", "AL_SPDBRK_DOWN");
        Assert.True(step.SkipCondition!(LeverReading(0)));
        Assert.False(step.SkipCondition!(LeverReading(224)));
        var unread = new IFly737StateEvaluator();
        Assert.False(step.SkipCondition!(unread));
        Assert.True(step.LeaveAloneWhen!(unread));
        Assert.False(step.LeaveAloneWhen!(LeverReading(224)));
    }

    // --- Shutdown: anti-collision OFF right after the belts ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(double.NaN, false)]
    public void Shutdown_line_reads_the_anti_collision_light_off(double value, bool expected)
    {
        var item = Item("SHUTDOWN", "SD_ANTICOL");
        Assert.Equal("Anti-collision light: OFF", item.Label);
        Assert.Equal("Anti_Collision_Light_Switch_Status", item.StateFieldName);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Shutdown_turns_the_anti_collision_light_off_after_the_belts()
    {
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(sd.IndexOf("SD_BELTS") + 1, sd.IndexOf("SD_ANTICOL_OFF"));
        var items = ItemIds("SHUTDOWN");
        Assert.Equal(items.IndexOf("SD_BELTS") + 1, items.IndexOf("SD_ANTICOL"));

        var step = Step("SHUTDOWN", "SD_ANTICOL_OFF");
        Assert.Equal("Anti_Collision_Light_Switch_Status", step.EventName);
        Assert.Equal(0, step.TargetValue);
        Assert.Equal(new[] { "SD_ANTICOL" }, step.LinkedChecklistItemIds.ToArray());
    }

    // --- Shutdown: flight directors OFF, just before the transponder ---

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(double.NaN, false)]
    public void Shutdown_line_reads_both_flight_directors_off(double value, bool expected)
    {
        var item = Item("SHUTDOWN", "SD_FD");
        Assert.Equal("Flight directors: OFF", item.Label);
        Assert.Equal("FD_1_Switch_Status", item.StateFieldName);
        Assert.Contains("FD_2_Switch_Status", item.AdditionalStateFields);
        Assert.NotNull(item.CheckAction);
        Assert.Equal(expected, item.EvaluateState(value));
    }

    [Fact]
    public void Shutdown_sets_each_flight_director_off_before_the_transponder()
    {
        var sd = FlowStepIds("SHUTDOWN");
        Assert.Equal(new[] { "SD_FD1", "SD_FD2", "SD_XPDR" }, sd.Skip(sd.IndexOf("SD_FD1")).Take(3).ToArray());
        var items = ItemIds("SHUTDOWN");
        Assert.Equal(items.IndexOf("SD_FD") + 1, items.IndexOf("SD_XPDR"));

        foreach (var (id, field) in new[] { ("SD_FD1", "FD_1_Switch_Status"), ("SD_FD2", "FD_2_Switch_Status") })
        {
            var step = Step("SHUTDOWN", id);
            Assert.Equal(field, step.EventName);
            Assert.Equal(0, step.TargetValue);   // an absolute SET on this airframe, not a toggle
            Assert.Equal(new[] { "SD_FD" }, step.LinkedChecklistItemIds.ToArray());
        }
    }

    [Fact]
    public void After_landing_description_names_the_speed_brake()
    {
        string d = IFly737FlowDefinitions.Build().Single(f => f.Id == "AFTER_LANDING").Description;
        Assert.Contains("speed brake", d, StringComparison.OrdinalIgnoreCase);
    }
}
