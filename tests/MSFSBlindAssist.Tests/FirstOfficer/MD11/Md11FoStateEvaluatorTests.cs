using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

public class Md11FoStateEvaluatorTests
{
    private static Func<string, double> Read(Dictionary<string, double> d)
        => k => d.TryGetValue(k, out var v) ? v : double.NaN;

    private static Dictionary<string, double> Powered(params (string K, double V)[] extra)
    {
        var d = new Dictionary<string, double>
        {
            [TFDiMD11Definition.DcPowerKey] = 28,
            [TFDiMD11Definition.Dc1BusOffKey] = 0,
        };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    [Fact]
    public void LampFields_AreIndeterminateUnpowered()
    {
        var d = new Dictionary<string, double> { ["MD11_OVHD_LTS_NAV_LT"] = 0 };
        Assert.True(double.IsNaN(Md11FoStateEvaluator.Compute("FO_NAV_ON", Read(d))));
    }

    [Fact]
    public void NavOn_IsTheOffLegendDark()
    {
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_NAV_ON", Read(Powered(("MD11_OVHD_LTS_NAV_LT", 0)))));
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_NAV_ON", Read(Powered(("MD11_OVHD_LTS_NAV_LT", 1)))));
    }

    [Fact]
    public void FuelSwitchesOff_NeedsAllThreeRead()
    {
        var d = Powered(("MD11_THR_L_FUEL_SW", 0), ("MD11_THR_C_FUEL_SW", 0));
        Assert.True(double.IsNaN(Md11FoStateEvaluator.Compute("FO_FUEL_SWITCHES_OFF", Read(d))));
        d["MD11_THR_R_FUEL_SW"] = 0;
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_FUEL_SWITCHES_OFF", Read(d)));
        d["MD11_THR_C_FUEL_SW"] = 1;
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_FUEL_SWITCHES_OFF", Read(d)));
    }

    [Theory]
    [InlineData(0.0, 1, 0)]
    [InlineData(46.91, 0, 1)]
    public void Flaps(double rng, double up, double daf)
    {
        var d = new Dictionary<string, double> { ["MD11_FLAP_LATCH"] = rng };
        Assert.Equal(up, Md11FoStateEvaluator.Compute("FO_FLAPS_UP", Read(d)));
        Assert.Equal(daf, Md11FoStateEvaluator.Compute("FO_FLAPS_DAF", Read(d)));
    }

    [Fact]
    public void DafTakeoff_ComparesTheWheelToTheSimBriefFlap_AndIsUnknownWithoutAPlan()
    {
        var d = new Dictionary<string, double> { ["MD11_DIALAFLAP_WHEEL_RNG"] = 33.3 };
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_DAF_TAKEOFF", Read(d), takeoffFlaps: 15));
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_DAF_TAKEOFF", Read(d), takeoffFlaps: 20));
        Assert.True(double.IsNaN(Md11FoStateEvaluator.Compute("FO_DAF_TAKEOFF", Read(d), takeoffFlaps: -1)));
        Assert.True(double.IsNaN(Md11FoStateEvaluator.Compute("FO_DAF_TAKEOFF", Read(d), takeoffFlaps: 5)));
    }

    [Fact]
    public void GearDownAndUp_UseTheLeverTravel()
    {
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_GEAR_DOWN", Read(new() { ["MD11_MIP_GEAR_SW"] = 25 })));
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_GEAR_UP", Read(new() { ["MD11_MIP_GEAR_SW"] = 25 })));
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_GEAR_UP", Read(new() { ["MD11_MIP_GEAR_SW"] = 0 })));
    }

    /// <summary>
    /// The Shutdown line "External power: OFF" must tick once the aircraft is on its battery, where
    /// every lamp field reads unknown. Connected external power powers the DC busses, so with the
    /// busses dead (battery alone: 24 V with DC1 OFF lit; cold: 0 V) it cannot be on.
    /// </summary>
    [Theory]
    [InlineData(28.0, 0.0, 1.0, 0.0)]           // powered, ON lamp lit: connected
    [InlineData(28.0, 0.0, 0.0, 1.0)]           // powered by the APU, ON lamp dark
    [InlineData(24.0, 1.0, 0.0, 1.0)]           // battery alone: DC1 OFF lit
    [InlineData(0.0, 0.0, 0.0, 1.0)]            // cold and dark
    [InlineData(28.0, 0.0, double.NaN, double.NaN)]   // powered, ON lamp unread
    [InlineData(28.0, double.NaN, 0.0, double.NaN)]   // the power gate not yet delivered
    public void ExternalPowerOff_IsTheOnLampDark_OrNothingPowered(double dcVolts, double dc1Off, double onLamp, double want)
    {
        var d = new Dictionary<string, double>
        {
            [TFDiMD11Definition.DcPowerKey] = dcVolts,
            [TFDiMD11Definition.Dc1BusOffKey] = dc1Off,
            ["MD11_OVHD_ELEC_EXT_PWR_ON_LT"] = onLamp,
        };
        Assert.Equal(want, Md11FoStateEvaluator.Compute("FO_EXT_POWER_OFF", Read(d)));
    }

    [Fact]
    public void IgnitionSelected_HandlesTheBLampReadingTwo()
        => Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_IGNITION_SELECTED",
            Read(Powered(("MD11_OVHD_ENG_A_LT", 0), ("MD11_OVHD_ENG_B_LT", 2)))));

    [Fact]
    public void StartAirReady_NeedsTheApuRunningAndItsBleedOn()
    {
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_START_AIR_READY",
            Read(new() { ["MD11_APU_STATE"] = 2, ["MD11_OVHD_PNEU_APU_BLEED_BT"] = 1 })));
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_START_AIR_READY",
            Read(new() { ["MD11_APU_STATE"] = 1, ["MD11_OVHD_PNEU_APU_BLEED_BT"] = 1 })));
    }

    /// <summary>AUTO FLIGHT engages "both ATs and one AP"; before takeoff only the autothrottle comes on.</summary>
    [Fact]
    public void AutoFlightOn_IsTheAutopilotOrTheAutothrottleEngaged()
    {
        Assert.Equal(0, Md11FoStateEvaluator.Compute("FO_AUTO_FLIGHT_ON", Read(new() { ["MD11_AP_STATE"] = 0, ["MD11_ATS_STATE"] = 0 })));
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_AUTO_FLIGHT_ON", Read(new() { ["MD11_AP_STATE"] = 0, ["MD11_ATS_STATE"] = 1 })));
        Assert.Equal(1, Md11FoStateEvaluator.Compute("FO_AUTO_FLIGHT_ON", Read(new() { ["MD11_AP_STATE"] = 2, ["MD11_ATS_STATE"] = 0 })));
        Assert.True(double.IsNaN(Md11FoStateEvaluator.Compute("FO_AUTO_FLIGHT_ON", Read(new() { ["MD11_AP_STATE"] = 0 }))));
    }

    [Fact]
    public void Engine3N2_IsNaNUntilFed()
    {
        var e = new Md11FoStateEvaluator();
        Assert.True(double.IsNaN(e.EngineN2(3)));
        Assert.True(double.IsNaN(e.GetValue("FO_ENG3_N2")));
        e.SetEngine3N2(22);
        Assert.Equal(22, e.GetValue("FO_ENG3_N2"));
        Assert.Equal(22, e.EngineN2(3));
    }

    [Fact]
    public void OnGround_IsNullWithoutAConnection()
        => Assert.Null(new Md11FoStateEvaluator().OnGround);

    [Fact]
    public void EverySyntheticInput_IsARegisteredReadableVariable()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        foreach (var k in Md11FoStateEvaluator.SyntheticInputs.Where(k => !k.StartsWith("FO_ENG", StringComparison.Ordinal)))
        {
            Assert.True(vars.TryGetValue(k, out var def), $"synthetic input {k} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
        }
    }

    [Fact]
    public void EveryOnRequestSyntheticInput_IsPolled()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        var polled = new Md11FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var k in Md11FoStateEvaluator.SyntheticInputs)
            if (vars.TryGetValue(k, out var def) && def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.Contains(k, polled);
    }

    [Fact]
    public void EveryPollField_IsRegisteredOnRequest()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        foreach (var k in new Md11FoStateEvaluator().OnRequestPollFields)
        {
            Assert.True(vars.TryGetValue(k, out var def), $"poll field {k} is not registered");
            Assert.Equal(UpdateFrequency.OnRequest, def!.UpdateFrequency);
        }
    }
}
