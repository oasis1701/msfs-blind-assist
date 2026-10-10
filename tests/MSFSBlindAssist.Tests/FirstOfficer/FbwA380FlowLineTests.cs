using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The FBW A380 flow lines fixed with the 2026-10-09 read-back rebuild (FOA-9): each says what its step
/// does, and the one surveillance reminder that names a position the A380 does not offer is gone. The
/// other surveillance reminders stay as Captain items that name the MFD SURV page (owner decision,
/// 2026-10-09). These tests pin each reminder's wording and kind; they say nothing about whether the
/// aircraft honours the setting (see the comment above the reminder test).
/// </summary>
public class FbwA380FlowLineTests
{
    private static FlowStep<FbwA380StateEvaluator> Step(string flowId, string id) =>
        FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == id);

    private static ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator> Item(string groupId, string id) =>
        FbwA380ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == id);

    [Fact]
    public void The_taxi_autobrake_line_says_what_it_arms()
    {
        var step = Step("TAXI", "TX_AUTOBRAKE");
        Assert.Equal("Autobrake: RTO", step.Label);
        Assert.Equal("A32NX_OVHD_AUTOBRK_RTO_ARM_IS_PRESSED", step.EventName);
        Assert.Equal(1, step.TargetValue);
        var item = Item("TAXI", "TX_AUTOBRAKE");
        Assert.Equal("Autobrake: RTO", item.Label);
        Assert.Equal("A32NX_AUTOBRAKES_RTO_ARMED", item.StateFieldName);
        // The flow's one-line description names the same setting.
        Assert.StartsWith("Autobrake RTO,", FbwA380FlowDefinitions.Build().Single(f => f.Id == "TAXI").Description);
    }

    [Fact]
    public void The_after_start_cockpit_lights_line_says_what_it_sets()
    {
        var step = Step("AFTER_START", "AS_COCKPITLT");
        Assert.Equal("Cockpit lights: annunciator dim", step.Label);
        Assert.Equal(new (string, int?)[] { ("A32NX_OVHD_INTLT_ANN", 2) }, step.MultiActions.ToArray());
        var item = Item("AFTER_START", "AS_COCKPITLT");
        Assert.Equal("Cockpit lights: annunciator dim", item.Label);
        Assert.True(item.StateCondition!(2));    // 0 Test / 1 Bright / 2 Dim
        Assert.False(item.StateCondition!(1));
    }

    // FlyByWire's source at 255546cb offers NORM / ABV / BLW for the MFD SURV TCAS traffic selection, so
    // "all" names a position the A380 does not have.
    [Fact]
    public void TCAS_traffic_all_is_gone()
    {
        var steps = FbwA380FlowDefinitions.Build().SelectMany(f => f.Steps).ToList();
        var items = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).ToList();
        Assert.DoesNotContain(steps, s => s.Id == "CP_TCASTRAFFIC"
            || s.Label.Contains("TCAS traffic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i => i.Id == "CP_TCASTRAFFIC"
            || i.Label.Contains("TCAS traffic", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> SurveillanceReminders() => new[]
    {
        new object[] { "COCKPIT_PREP", "CP_ALTRPT", "Altitude reporting: on (MFD SURV page)" },
        new object[] { "COCKPIT_PREP", "CP_TCASMODE", "TCAS mode: standby (MFD SURV page)" },
        new object[] { "COCKPIT_PREP", "CP_XPDR", "Transponder: standby (MFD SURV page)" },
        new object[] { "BEFORE_START", "BS_XPDR", "Transponder: set for departure" },   // the squawk: unchanged
        new object[] { "BEFORE_START", "BS_TCAS", "TCAS mode: TA/RA (MFD SURV page)" },
        new object[] { "LINEUP", "LU_TCAS", "TCAS mode: TA/RA (MFD SURV page)" },
        new object[] { "PARKING", "PK_XPDR", "Transponder: standby (MFD SURV page)" },
        new object[] { "PARKING", "PK_TCAS", "TCAS mode: standby (MFD SURV page)" },
    };

    // What is known about these settings, and what is not (FOA-9):
    //  - FlyByWire's source at 255546cb models XPDR AUTO/STBY, ALT RPTG and TCAS TA/RA / TA ONLY / STBY on
    //    the MFD SURV CONTROLS page;
    //  - the repo's own note (FlyByWireA380Definition.cs, written after a live in-flight test on
    //    2026-06-03) records the TCAS selection on that page as a dead stub;
    //  - MSFSBA cannot set them (A380-4).
    // So they stay Captain reminders that name the page (owner decision, 2026-10-09): never a write, never
    // a check, and this test does not claim the page's TCAS selection does anything.
    [Theory]
    [MemberData(nameof(SurveillanceReminders))]
    public void The_surveillance_reminders_stay_Captain_items_with_their_wording(string flowId, string id, string label)
    {
        var step = Step(flowId, id);
        Assert.Equal(FlowStepActionType.CaptainReminder, step.ActionType);
        Assert.Equal(label, step.Label);
        Assert.Equal(label, step.ReminderText);
        var item = Item(flowId, id);
        Assert.Equal(ChecklistItemType.CaptainReminder, item.Type);
        Assert.Equal(label, item.Label);
        Assert.Equal(label, item.ReminderText);
    }
}
