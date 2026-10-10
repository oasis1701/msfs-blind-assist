using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 read-only checks (FO-6, FO-13, FOA-9): each waits on its lines' own field, is skipped aloud
/// on timeout, never writes, and is the one step that completes those lines. The behaviour half, on a
/// real FlowManager, is <see cref="FbwA380TaxiMemoCheckBehaviourTests"/> and
/// <see cref="FbwA380LandingChecksBehaviourTests"/>.
/// </summary>
public class FbwA380ReadOnlyCheckTests
{
    private static List<FlowStep<FbwA380StateEvaluator>> Steps(string flowId) =>
        FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId).Steps;

    public static IEnumerable<object[]> Checks() => new[]
    {
        new object[] { "COCKPIT_PREP", "CP_WAIT_SB", "Waiting for seatbelt signs on", "SEATBELT_SIGN_LIGHT", 60,
            new[] { "CPC_SEATBELT" } },
        new object[] { "TAXI", "TX_MEMO_CHECK", "Seat belts, ground spoilers and flaps for takeoff", "FO_TAXI_MEMO_OK", 5,
            new[] { "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS" } },
        new object[] { "LINEUP", "LU_PACKS_CHECK", "Packs 1 and 2: ON", "FO_PACKS_ON", 5, new[] { "LUC_PACKS" } },
        new object[] { "LANDING", "LD_MEMO_CHECK", "Seat belts and landing flaps", "FO_LDG_MEMO_OK", 5,
            new[] { "LDC_SEATBELT", "LDC_FLAPS" } },
        new object[] { "LANDING", "LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", FbwA380GearConfirmation.DownField, 20,
            new[] { "LDC_GEAR" } },
    };

    [Theory]
    [MemberData(nameof(Checks))]
    public void A_read_only_check_waits_on_its_lines_field_skips_on_timeout_and_never_writes(
        string flowId, string stepId, string label, string field, int timeout, string[] lines)
    {
        var step = Steps(flowId).Single(s => s.Id == stepId);
        Assert.Equal(label, step.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(field, step.ConditionFieldName);
        Assert.Equal(timeout, step.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.True(step.Condition!(1.0));
        Assert.False(step.Condition!(0.0));
        Assert.False(step.Condition!(double.NaN));   // unknown is not confirmed
        Assert.Null(step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Null(step.TargetValueProvider);
        Assert.Empty(step.MultiActions);
        Assert.Equal(lines, step.LinkedChecklistItemIds.ToArray());
    }

    // The four new checks, and what the aircraft shows when it already agrees (the check's field reads 1)
    // and when it does not (the field reads 0). Written out here, not read from the evaluator under test.
    private static readonly (string FlowId, string StepId, (string Key, double Value)[] Agrees, (string Key, double Value)[] Disagrees)[] NewChecks =
    {
        ("TAXI", "TX_MEMO_CHECK",
            new[] { ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_SPOILERS_ARMED", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", 2.0) },
            new[] { ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_SPOILERS_ARMED", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", 0.0) }),
        ("LINEUP", "LU_PACKS_CHECK",
            new[] { ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1.0), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 1.0) },
            new[] { ("A32NX_OVHD_COND_PACK_1_PB_IS_ON", 1.0), ("A32NX_OVHD_COND_PACK_2_PB_IS_ON", 0.0) }),
        ("LANDING", "LD_MEMO_CHECK",
            new[] { ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", 4.0), (FbwA380StateEvaluator.LandingConf3Field, 0.0) },
            new[] { ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", 3.0), (FbwA380StateEvaluator.LandingConf3Field, 0.0) }),
        ("LANDING", "LD_GEAR_DOWN_CHECK",
            new[] { (FbwA380GearConfirmation.HandleField, 1.0), ("A32NX_GEAR_CENTER_POSITION", 100.0),
                    ("A32NX_GEAR_LEFT_POSITION", 100.0), ("A32NX_GEAR_RIGHT_POSITION", 100.0) },
            new[] { (FbwA380GearConfirmation.HandleField, 1.0), ("A32NX_GEAR_CENTER_POSITION", 100.0),
                    ("A32NX_GEAR_LEFT_POSITION", 50.0), ("A32NX_GEAR_RIGHT_POSITION", 100.0) }),
    };

    private static FbwA380StateEvaluator Reading(params (string Key, double Value)[] values)
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(values));
        return e;
    }

    /// <summary>"Already set: ..." is the First Officer saying the aircraft is already there, so it must
    /// say so only then: not for an unread aircraft (an unknown field is not set), not for one that reads
    /// the other way, and for one that agrees. A SkipCondition that is merely present, or that is true for
    /// anything but agreement, would not do.</summary>
    [Fact]
    public void The_new_checks_say_already_set_when_the_aircraft_already_agrees()
    {
        foreach (var (flowId, stepId, agrees, disagrees) in NewChecks)
        {
            var skip = Steps(flowId).Single(s => s.Id == stepId).SkipCondition;
            Assert.NotNull(skip);
            Assert.False(skip!(new FbwA380StateEvaluator()), $"{stepId} says already set over an unread aircraft");
            Assert.False(skip(Reading(disagrees)), $"{stepId} says already set over an aircraft that reads the other way");
            Assert.True(skip(Reading(agrees)), $"{stepId} does not say already set over an aircraft that agrees");
        }
    }

    [Fact]
    public void The_taxi_memo_check_comes_right_before_the_takeoff_config_test_which_is_last()
    {
        var ids = Steps("TAXI").Select(s => s.Id).ToList();
        Assert.Equal("TX_CONFIG", ids[^1]);
        Assert.Equal("TX_MEMO_CHECK", ids[^2]);
    }

    [Fact]
    public void The_landing_flow_ends_with_the_memo_check_then_the_gear_down_check()
    {
        var ids = Steps("LANDING").Select(s => s.Id).ToList();
        Assert.Equal("LD_GEAR_DOWN_CHECK", ids[^1]);   // FO-13: the gear check is LAST
        Assert.Equal("LD_MEMO_CHECK", ids[^2]);
        Assert.True(ids.IndexOf("LD_SPOILERS_ARM") < ids.IndexOf("LD_MEMO_CHECK"));
    }

    [Fact]
    public void The_packs_check_replaces_the_packs_reminder_and_nothing_switches_the_packs()
    {
        var steps = Steps("LINEUP");
        var ids = steps.Select(s => s.Id).ToList();
        Assert.DoesNotContain("LU_PACKS", ids);
        Assert.Equal(ids.IndexOf("LU_TCAS") + 1, ids.IndexOf("LU_PACKS_CHECK"));
        Assert.DoesNotContain(FbwA380ChecklistDefinitions.Build().Single(g => g.Id == "LINEUP").Items,
            i => i.Id == "LU_PACKS");
        Assert.DoesNotContain(steps.Where(s => FlowChecklistLinkAudit.IsWrite(s)).SelectMany(s => FlowChecklistLinkAudit.Writes(s)),
            w => w.Event.StartsWith("A32NX_OVHD_COND_PACK_", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_checked_line_is_completed_by_its_check_alone()
    {
        var lines = new[] { "CPC_SEATBELT", "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS",
                            "LUC_PACKS", "LDC_SEATBELT", "LDC_FLAPS", "LDC_GEAR" };
        var owners = FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps)
            .SelectMany(s => s.LinkedChecklistItemIds.Where(lines.Contains).Select(l => (Line: l, Step: s.Id)))
            .GroupBy(x => x.Line)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Step).ToList());
        foreach (var line in lines)
            Assert.True(owners.TryGetValue(line, out var steps) && steps.Count == 1,
                $"{line} is completed by [{string.Join(", ", owners.GetValueOrDefault(line) ?? new List<string>())}]");
    }
}
