using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// PMDG 777 flows tick their checklist, made honest (2026-10-09). A finished flow ticks and
/// latches every line of its groups (FO-7), so a line the First Officer can read or act on needs a
/// step behind it, or it is latched over whatever the aircraft reads. These lines had none; each now
/// gets a write of its own (the seat-belt sign ON, autobrake RTO, a RECALL press, the six fuel pumps
/// OFF; "Already set" when the aircraft is there), linked to the lines it delivers (FO-6), so a
/// failed write is heard skipped and its lines stay open.
/// </summary>
public class Pmdg777HonestTickingTests
{
    private static FlowStep<AircraftStateEvaluator> Step(string flowId, string stepId) =>
        PMDG777FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId);

    [Fact]
    public void Before_start_sets_the_passenger_signs_and_delivers_both_signs_lines()
    {
        // Where the sign goes ON: Cockpit Prep leaves it OFF, as PMDG's Preflight reads. Verified
        // like Shutdown's seat-belt write.
        var step = Step("BEFORE_START", "BS_SEAT_BELTS");
        Assert.Equal("Passenger signs: ON", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal("EVT_OH_FASTEN_BELTS_LIGHT_SWITCH", step.EventName);
        Assert.Equal(2, step.TargetValue);                 // ON, not the vendor's AUTO (FOB-4)
        Assert.Equal("SIGNS_SeatBeltsSelector", step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(2));
        Assert.False(step.VerifyCondition!(1));
        Assert.Equal(new[] { "BS_SEAT_BELTS", "BSCL_SIGNS" }, step.LinkedChecklistItemIds.ToArray());
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    [Fact]
    public void Before_taxi_sets_autobrake_RTO_for_its_readback_line()
    {
        // The Cockpit Prep write (CP_AUTOBRAKE); the Before Taxi group has no autobrake line, so
        // the write completes the read-back's "Auto Brake: RTO".
        var step = Step("BEFORE_TAXI", "BT_AUTOBRAKE_RTO");
        Assert.Equal("Autobrake: RTO", step.Label);
        Assert.Equal("EVT_ABS_AUTOBRAKE_SELECTOR", step.EventName);
        Assert.Equal(0, step.TargetValue);                 // 0 = RTO (P777-11)
        Assert.Equal("BRAKES_AutobrakeSelector", step.VerifyFieldName);
        Assert.True(step.VerifyCondition!(0));
        Assert.False(step.VerifyCondition!(1));
        Assert.Equal(new[] { "BTCL_AUTOBRAKE" }, step.LinkedChecklistItemIds.ToArray());
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    // An unread selector is not RTO. (int)Math.Round(NaN) is 0 under .NET's saturating
    // conversion, so "AutobrakeSelector() == 0" read RTO before the first CDA snapshot, and
    // Cockpit Prep's autobrake step said "Already set" over data that had not arrived (the
    // AreFlapsUp lesson, AircraftStateEvaluator).
    [Fact]
    public void An_unread_autobrake_selector_is_not_RTO()
    {
        Assert.False(new AircraftStateEvaluator().IsAutobrakRTO());
        Assert.False(Step("COCKPIT_PREP", "CP_AUTOBRAKE").SkipCondition!(new AircraftStateEvaluator()));
    }

    [Fact]
    public void Cockpit_prep_turns_the_fuel_pumps_off_wing_pumps_first()
    {
        // The Shutdown flow's six-pump write, wing pumps before centre pumps (FOB-12: the centre
        // falling edge must see the wing pumps already off, or a manual-off latch is set).
        var step = Step("COCKPIT_PREP", "CP_FUEL_PUMPS_OFF");
        Assert.Equal("Fuel pumps: OFF", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitchMultiple, step.ActionType);
        Assert.Equal(new (string, int?)[]
        {
            ("EVT_OH_FUEL_PUMP_1_FORWARD", 0), ("EVT_OH_FUEL_PUMP_2_FORWARD", 0),
            ("EVT_OH_FUEL_PUMP_1_AFT", 0), ("EVT_OH_FUEL_PUMP_2_AFT", 0),
            ("EVT_OH_FUEL_PUMP_L_CENTER", 0), ("EVT_OH_FUEL_PUMP_R_CENTER", 0),
        }, step.MultiActions.ToArray());
        Assert.Equal(new[] { "PF_FUEL_PUMPS_OFF" }, step.LinkedChecklistItemIds.ToArray());
        Assert.False(step.SkipCondition!(new AircraftStateEvaluator()));
    }

    // An unread pump is not OFF: before the first CDA snapshot the step must not say "Already set".
    [Fact]
    public void Unread_fuel_pumps_are_not_all_off() =>
        Assert.False(new AircraftStateEvaluator().AreAllFuelPumpsOff());

    // The same press as each line's own action (AircraftActionExecutor.PushCancelRecall: a
    // momentary EVT_DSP_CANC_RCL_SWITCH); the pilot reads the messages it brings up.
    [Theory]
    [InlineData("BEFORE_TAXI", "BT_RECALL", "Recall: push", "BT_RECALL")]
    [InlineData("DESCENT_SETUP", "DSC_RECALL", "Recall: Check no unexpected messages", "DSCA_RECALL")]
    public void Recall_is_pushed_with_the_press_its_line_fires(string flowId, string stepId, string label, string lineId)
    {
        var step = Step(flowId, stepId);
        Assert.Equal(label, step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal("EVT_DSP_CANC_RCL_SWITCH", step.EventName);
        Assert.True(step.IsMomentary);
        Assert.Null(step.TargetValue);
        Assert.Equal(new[] { lineId }, step.LinkedChecklistItemIds.ToArray());
    }
}
