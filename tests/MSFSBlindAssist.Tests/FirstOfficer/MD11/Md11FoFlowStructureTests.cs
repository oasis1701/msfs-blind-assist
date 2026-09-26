using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

public class Md11FoFlowStructureTests
{
    private static readonly List<FlowDefinition<Md11FoStateEvaluator>> Flows = Md11FoFlowDefinitions.Build();

    private static IEnumerable<(string Flow, FlowStep<Md11FoStateEvaluator> Step, string Key, int? Target)> Writes()
    {
        foreach (var f in Flows)
            foreach (var s in f.Steps)
            {
                if (s.ActionType == FlowStepActionType.SetSwitch && s.EventName != null)
                    yield return (f.Id, s, s.EventName, s.TargetValue);
                else if (s.ActionType == FlowStepActionType.SetSwitchMultiple)
                    foreach (var (ev, tv) in s.MultiActions) yield return (f.Id, s, ev, tv);
            }
    }

    [Fact]
    public void FlowIds_InFlightOrder()
        => Assert.Equal(new[] { "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START",
            "BEFORE_TAKEOFF", "AFTER_TAKEOFF", "DESCENT", "BEFORE_LANDING", "AFTER_LANDING", "PARKING", "SHUTDOWN" },
            Flows.Select(f => f.Id));

    [Fact]
    public void EveryWrittenKey_IsKnownToTheExecutor()
    {
        int n = 0;
        foreach (var (flow, step, key, _) in Writes())
        {
            n++;
            Assert.True(Md11FoActionExecutor.IsKnownKey(key), $"{flow}.{step.Id}: '{key}' would silently do nothing");
        }
        Assert.True(n > 60, $"only {n} writes enumerated");
    }

    [Fact]
    public void StepIds_AreUniqueAcrossFlows()
    {
        var ids = Flows.SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EngineStart_OrderIsThreeOneTwo()
    {
        var starts = Flows.Single(f => f.Id == "ENGINE_START").Steps
            .Where(s => s.EventName is Md11FoActionExecutor.EngineStart1 or Md11FoActionExecutor.EngineStart2 or Md11FoActionExecutor.EngineStart3)
            .Select(s => s.EventName).ToArray();
        Assert.Equal(new[] { Md11FoActionExecutor.EngineStart3, Md11FoActionExecutor.EngineStart1, Md11FoActionExecutor.EngineStart2 }, starts);
    }

    [Fact]
    public void EngineStart_ANoLightUpStopsTheFlowBeforeAnyFuel()
    {
        var steps = Flows.Single(f => f.Id == "ENGINE_START").Steps;
        foreach (var (lightUp, fuel) in new[]
        {
            (Md11FoActionExecutor.EngineLightUp3, "MD11_THR_R_FUEL_SW"),
            (Md11FoActionExecutor.EngineLightUp1, "MD11_THR_L_FUEL_SW"),
            (Md11FoActionExecutor.EngineLightUp2, "MD11_THR_C_FUEL_SW"),
        })
        {
            var watch = steps.Single(s => s.EventName == lightUp);
            Assert.Equal(FlowStepFailurePolicy.Stop, watch.FailurePolicy);
            Assert.True(steps.IndexOf(watch) < steps.FindIndex(s => s.EventName == fuel));
        }
    }

    [Fact]
    public void EngineStart_PreconditionsStopTheFlow()
    {
        var steps = Flows.Single(f => f.Id == "ENGINE_START").Steps;
        foreach (var field in new[] { "FO_START_AIR_READY", "FO_IGNITION_SELECTED" })
        {
            var s = steps.Single(x => x.ConditionFieldName == field);
            Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy);
            Assert.True(steps.IndexOf(s) < steps.FindIndex(x => x.EventName == Md11FoActionExecutor.EngineStart3));
        }
    }

    [Fact]
    public void NoAirbornePhaseMovesTheFlapHandleOrDialAFlap()
    {
        foreach (var (flow, step, key, _) in Writes().Where(w => w.Flow is "AFTER_TAKEOFF" or "DESCENT" or "BEFORE_LANDING"))
            Assert.False(key is Md11FoActionExecutor.FlapHandle or Md11FoActionExecutor.DialAFlap, $"{flow}.{step.Id}");
    }

    [Fact]
    public void AutobrakeWrites_AreOnlyTakeoffOrOff()
    {
        foreach (var (flow, step, key, target) in Writes().Where(w => w.Key == "MD11_CTR_AUTOBRAKE_SW"))
            Assert.True(target is 0 or 1, $"{flow}.{step.Id}: autobrake {target} — the landing setting is the Captain's");
    }

    [Fact]
    public void Preflight_RunsEveryTest()
    {
        var keys = Flows.Single(f => f.Id == "PREFLIGHT").Steps.Select(s => s.EventName).ToHashSet();
        foreach (var k in new[]
        {
            "MD11_AOVHD_FIRETEST_BT", Md11FoActionExecutor.AnnunciatorTest, "MD11_AOVHD_CRGSMK_TEST_BT",
            "MD11_OVHD_CVR_TEST_BT", Md11FoActionExecutor.HydraulicTest, "MD11_OVHD_FUEL_QTY_TEST_BT",
            "MD11_OVHD_LTS_EMER_TEST_BT", Md11FoActionExecutor.GpwsTest, Md11FoActionExecutor.WeatherRadarTest,
            "MD11_LSIDE_OXY_TEST_BT", "MD11_RSIDE_OXY_TEST_BT", "MD11_MIP_ISFD_TEST_BT",
            "MD11_PED_XPNDR_TEST_BT", "MD11_OVHD_CRG_DOOR_TEST_BT",
        })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void Preflight_HydraulicAutoPrecedesTheTest_AndTheFlowWaitsForIt()
    {
        var s = Flows.Single(f => f.Id == "PREFLIGHT").Steps;
        int auto = s.FindIndex(x => x.EventName == "MD11_OVHD_HYD_SYSTEM_SEL_BT");
        int test = s.FindIndex(x => x.EventName == Md11FoActionExecutor.HydraulicTest);
        int wait = s.FindIndex(x => x.ConditionFieldName == "FO_HYD_TEST_RUNNING");
        Assert.True(auto >= 0 && auto < test && test < wait);
    }

    [Fact]
    public void BeforeStart_WaitsForTheHydraulicTestBeforeTheAuxPump()
    {
        var s = Flows.Single(f => f.Id == "BEFORE_START").Steps;
        int wait = s.FindIndex(x => x.ConditionFieldName == "FO_HYD_TEST_RUNNING");
        int aux = s.FindIndex(x => x.EventName == "MD11_OVHD_HYD_AUX_PUMP_1_BT");
        Assert.True(wait >= 0 && wait < aux);
    }

    [Fact]
    public void BeforeStart_NeverDropsExternalPowerBeforeApuPowerIsOn()
    {
        var s = Flows.Single(f => f.Id == "BEFORE_START").Steps;
        var apuPower = s.Single(x => x.ConditionFieldName == "FO_APU_POWER_ON");
        Assert.Equal(FlowStepFailurePolicy.Stop, apuPower.FailurePolicy);
        Assert.True(s.IndexOf(apuPower) < s.FindIndex(x => x.EventName == Md11FoActionExecutor.ExtPower));
    }

    [Fact]
    public void GpwsTest_StopsTheFlowIfItCannotComeBackToNormal()
        => Assert.Equal(FlowStepFailurePolicy.Stop,
            Flows.Single(f => f.Id == "PREFLIGHT").Steps.Single(s => s.EventName == Md11FoActionExecutor.GpwsTest).FailurePolicy);

    [Fact]
    public void DescentReminder_NamesThePanelAndSuggestsNoSetting()
    {
        var r = Flows.Single(f => f.Id == "DESCENT").Steps.Single(s => s.Id == "DS_AUTOBRAKE");
        Assert.Equal(FlowStepActionType.CaptainReminder, r.ActionType);
        Assert.Contains("center instrument panel", r.ReminderText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MIN", r.ReminderText);
        Assert.DoesNotContain("MED", r.ReminderText);
        Assert.DoesNotContain("MAX", r.ReminderText);
    }

    [Fact]
    public void AfterLanding_TheFirstOfficerStowsTheSpoilersFirst()
    {
        var steps = Flows.Single(f => f.Id == "AFTER_LANDING").Steps;
        var first = steps.First(s => s.ActionType == FlowStepActionType.SetSwitch);
        Assert.Equal(Md11FoActionExecutor.Spoilers, first.EventName);
        Assert.Equal(0, first.TargetValue);
        Assert.DoesNotContain(steps, s => s.ActionType == FlowStepActionType.CaptainReminder
            && s.ReminderText!.Contains("spoiler", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parking_WaitsForTheAircraftToStopBeforeTheFuelSwitches()
    {
        var s = Flows.Single(f => f.Id == "PARKING").Steps;
        int stopped = s.FindIndex(x => x.ConditionFieldName == "GROUND_VELOCITY");
        int fuel = s.FindIndex(x => x.Id == "PK_FUEL");
        Assert.True(stopped >= 0 && stopped < fuel);
    }

    [Fact]
    public void Shutdown_BatteryIsLastAndWaitsForTheApu()
    {
        var s = Flows.Single(f => f.Id == "SHUTDOWN").Steps;
        Assert.Equal("SD_BATTERY", s[^1].Id);
        Assert.True(s.FindIndex(x => x.Id == "SD_APU_STOPPED") < s.Count - 1);
    }

    /// <summary>
    /// Power Up and Shutdown are cold-and-dark flows: started by mistake with the engines turning,
    /// Power Up's first step cuts all three fuel switches and Shutdown switches the IRS and the
    /// battery off (both seen live, 2026-09-26). Each must STOP before its first switch unless the
    /// engines are stopped — and Shutdown also unless the aircraft is on the ground.
    /// </summary>
    [Theory]
    [InlineData("POWER_UP")]
    [InlineData("SHUTDOWN")]
    public void ColdAndDarkFlows_StopBeforeAnySwitchUnlessTheEnginesAreStopped(string flowId)
    {
        var s = Flows.Single(f => f.Id == flowId).Steps;
        int guard = s.FindIndex(x => x.ConditionFieldName == "FO_ENGINES_STOPPED");
        int firstSwitch = s.FindIndex(x => x.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple);
        Assert.True(guard >= 0, $"{flowId} has no engines-stopped guard");
        Assert.Equal(FlowStepFailurePolicy.Stop, s[guard].FailurePolicy);
        Assert.True(guard < firstSwitch, $"{flowId}: the guard must come before any switch");
        Assert.False(s[guard].Condition!(double.NaN), "an unread engine state must not pass the guard");
    }

    [Fact]
    public void Shutdown_StopsBeforeAnySwitchUnlessOnTheGround()
    {
        var s = Flows.Single(f => f.Id == "SHUTDOWN").Steps;
        int guard = s.FindIndex(x => x.ConditionFieldName == "SIM_ON_GROUND");
        int firstSwitch = s.FindIndex(x => x.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple);
        Assert.True(guard >= 0 && guard < firstSwitch);
        Assert.Equal(FlowStepFailurePolicy.Stop, s[guard].FailurePolicy);
        Assert.False(s[guard].Condition!(0));
        Assert.True(s[guard].Condition!(1));
    }
}
