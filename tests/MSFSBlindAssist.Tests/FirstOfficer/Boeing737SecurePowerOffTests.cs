using System.Linq;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;
using B737 = MSFSBlindAssist.FirstOfficer.PMDG737;
using IFly737 = MSFSBlindAssist.FirstOfficer.IFly737;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The PMDG 737 and iFly 737 Secure flows end cold and dark (owner, 2026-10-14: "all power
/// including ground power gets turned off"): APU OFF, ground power OFF, the APU cool-down
/// (only after this run switched the APU off), then Battery OFF, which completes Electrical
/// Power Down's "Battery: OFF" and is left alone while an engine turns. 737 NG sequence:
/// 60 s APU cool-down, battery off once EGT &lt; 300 °C plus 40 s (spec, sources).
/// </summary>
public class Boeing737SecurePowerOffTests
{
    private static FlowDefinition<B737.AircraftStateEvaluator> PmdgSecure()
        => B737.PMDG737FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    private static FlowDefinition<IFly737.IFly737StateEvaluator> IFlySecure()
        => IFly737.IFly737FlowDefinitions.Build().Single(f => f.Id == "SECURE");

    [Fact]
    public void Pmdg_secure_ends_apu_ground_power_cooldown_battery()
    {
        var ids = PmdgSecure().Steps.Select(s => s.Id).ToList();
        Assert.Equal(new[] { "SE_APU_OFF", "SE_GND_PWR_OFF", "SE_APU_EGT_WAIT", "SE_APU_WAIT", "SE_BAT_OFF" },
            ids.Skip(ids.Count - 5));
    }

    [Fact]
    public void IFly_secure_ends_apu_ground_power_cooldown_battery()
    {
        var ids = IFlySecure().Steps.Select(s => s.Id).ToList();
        Assert.Equal(new[] { "SE_APU_OFF", "SE_GND_PWR_OFF", "SE_APU_WAIT", "SE_BAT_OFF" },
            ids.Skip(ids.Count - 4));
    }

    [Fact]
    public void Pmdg_cooldown_waits_for_egt_then_40_seconds_after_this_runs_apu_switch_off()
    {
        var steps = PmdgSecure().Steps;
        var egt = steps.Single(s => s.Id == "SE_APU_EGT_WAIT");
        Assert.Equal(FlowStepActionType.WaitForCondition, egt.ActionType);
        Assert.Equal("APU_EGTNeedle", egt.ConditionFieldName);
        Assert.True(egt.Condition!(299) && !egt.Condition(300) && !egt.Condition(double.NaN));
        Assert.Equal(180, egt.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, egt.FailurePolicy);
        var door = steps.Single(s => s.Id == "SE_APU_WAIT");
        Assert.Equal(FlowStepActionType.WaitSeconds, door.ActionType);
        Assert.Equal(40, door.WaitSeconds);
        Assert.Equal("SE_APU_OFF", door.RunOnlyIfStepSent);
    }

    [Fact]
    public void IFly_cooldown_is_120_seconds_after_this_runs_apu_switch_off()
    {
        var wait = IFlySecure().Steps.Single(s => s.Id == "SE_APU_WAIT");
        Assert.Equal(FlowStepActionType.WaitSeconds, wait.ActionType);
        Assert.Equal(120, wait.WaitSeconds);
        Assert.Equal("SE_APU_OFF", wait.RunOnlyIfStepSent);
    }

    [Fact]
    public void Battery_step_completes_electrical_power_down_and_secure_latches_it()
    {
        foreach (var (bat, related) in new[]
        {
            (PmdgSecure().Steps.Single(s => s.Id == "SE_BAT_OFF").LinkedChecklistItemIds.ToArray(), PmdgSecure().RelatedChecklistGroupIds),
            (IFlySecure().Steps.Single(s => s.Id == "SE_BAT_OFF").LinkedChecklistItemIds.ToArray(), IFlySecure().RelatedChecklistGroupIds),
        })
        {
            Assert.Equal(new[] { "EPD_BAT" }, bat);
            Assert.Contains("ELEC_POWER_DOWN", related);
            Assert.Contains("SECURE", related);
        }
    }

    [Fact]
    public void Pmdg_battery_step_writes_off_and_skips_when_already_off()
    {
        var bat = PmdgSecure().Steps.Single(s => s.Id == "SE_BAT_OFF");
        Assert.Equal("EVT_OH_ELEC_BATTERY_SWITCH", bat.EventName);
        Assert.Equal(0, bat.TargetValue);
        Assert.NotNull(bat.SkipCondition);
    }

    [Fact]
    public void IFly_battery_step_writes_off_and_skips_when_already_off()
    {
        var bat = IFlySecure().Steps.Single(s => s.Id == "SE_BAT_OFF");
        Assert.Equal("Battery_Switch_Mode", bat.EventName);
        Assert.Equal(1, bat.TargetValue);   // 1 = OFF, 2 = ON
        Assert.NotNull(bat.SkipCondition);
    }

    [Theory]
    [InlineData(0.0, 0.0, false)]
    [InlineData(9.9, 0.0, false)]
    [InlineData(25.0, 0.0, true)]
    [InlineData(0.0, 60.0, true)]
    public void Battery_left_alone_while_an_engine_runs(double n2Eng1, double n2Eng2, bool leftAlone)
    {
        var bat = PmdgSecure().Steps.Single(s => s.Id == "SE_BAT_OFF");
        var s = new B737.AircraftStateEvaluator();
        s.SetEngineN2(n2Eng1, n2Eng2);
        Assert.Equal(leftAlone, bat.LeaveAloneWhen!(s));
        Assert.Equal("Engines running, so the battery stays on.", bat.LeaveAloneText);

        var ibat = IFlySecure().Steps.Single(st => st.Id == "SE_BAT_OFF");
        var i = new IFly737.IFly737StateEvaluator();
        i.SetEngineN2(n2Eng1, n2Eng2);
        Assert.Equal(leftAlone, ibat.LeaveAloneWhen!(i));
    }

    [Fact]
    public void Battery_left_alone_when_n2_unread()
    {
        // The iFly evaluator reads NaN until the first N2 sample; unknown is not "stopped".
        var ibat = IFlySecure().Steps.Single(st => st.Id == "SE_BAT_OFF");
        Assert.True(ibat.LeaveAloneWhen!(new IFly737.IFly737StateEvaluator()));
    }
}
