using System;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.FirstOfficer.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.DA40;

using E = DA40FoStateEvaluator;

/// <summary>What the DA40 First Officer reads: every synthetic field NaN while an input is unread,
/// never a guessed 0.</summary>
public class DA40FoStateEvaluatorTests
{
    private static E With(DA40Variant v, params (string Key, double Value)[] values)
    {
        var e = new E { Variant = v };
        e.SetSimConnect(SeededSimConnectCache.With(values));
        e.SetBreakerKeys(new CowsDA40Definition(v).BreakerKeys);
        return e;
    }

    [Fact]
    public void Breakers_all_in_needs_every_breaker_read()
    {
        var keys = new CowsDA40Definition(DA40Variant.NG).BreakerKeys;
        Assert.Equal(34, keys.Count);
        Assert.True(double.IsNaN(With(DA40Variant.NG).GetValue(E.BreakersInField)));
        var allIn = With(DA40Variant.NG, keys.Select(k => (k, 0.0)).ToArray());
        Assert.Equal(1, allIn.GetValue(E.BreakersInField));
        var onePulled = With(DA40Variant.NG, keys.Select((k, i) => (k, i == 7 ? 1.0 : 0.0)).ToArray());
        Assert.Equal(0, onePulled.GetValue(E.BreakersInField));
    }

    [Fact]
    public void No_breaker_list_is_not_all_in()
    {
        var e = new E();
        e.SetSimConnect(SeededSimConnectCache.With());
        Assert.True(double.IsNaN(e.GetValue(E.BreakersInField)));
    }

    [Theory]
    [InlineData(DA40Variant.NG, "DA40_POWER_RPM")]
    [InlineData(DA40Variant.XLS, "DA40_XLS_RPM")]
    public void Running_and_stopped_come_from_the_airframe_s_RPM(DA40Variant v, string rpmKey)
    {
        Assert.True(double.IsNaN(With(v).GetValue(E.EngineStoppedField)));
        Assert.Equal(1, With(v, (rpmKey, 0)).GetValue(E.EngineStoppedField));
        Assert.Equal(0, With(v, (rpmKey, 0)).GetValue(E.EngineRunningField));
        Assert.Equal(1, With(v, (rpmKey, 750)).GetValue(E.EngineRunningField));
        // Between 200 and 400 (a crank, a coast-down) is neither.
        Assert.Equal(0, With(v, (rpmKey, 300)).GetValue(E.EngineRunningField));
        Assert.Equal(0, With(v, (rpmKey, 300)).GetValue(E.EngineStoppedField));
    }

    [Fact]
    public void Idle_rpm_is_710_plus_or_minus_30()
    {
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_POWER_RPM", 680)).GetValue(E.IdleRpmField));
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_POWER_RPM", 740)).GetValue(E.IdleRpmField));
        Assert.Equal(0, With(DA40Variant.NG, ("DA40_POWER_RPM", 760)).GetValue(E.IdleRpmField));
    }

    [Fact]
    public void Oil_pressure_bands_are_the_gauge_s()
    {
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_START_OIL_PRESSURE", 3.0)).GetValue(E.OilPressureGreenField));
        Assert.Equal(0, With(DA40Variant.NG, ("DA40_START_OIL_PRESSURE", 0.5)).GetValue(E.OilPressureOutOfRedField));
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_START_OIL_PRESSURE", 1.5)).GetValue(E.OilPressureOutOfRedField));
        Assert.Equal(1, With(DA40Variant.XLS, ("DA40_XLS_OIL_PRESSURE", 73.7)).GetValue(E.OilPressureGreenField));
    }

    [Fact]
    public void The_XLS_tank_choice_reads_both_tanks()
    {
        Assert.Null(With(DA40Variant.XLS, ("DA40_XLS_FUEL_LEFT", 10)).LowerTank());
        var e = With(DA40Variant.XLS, ("DA40_XLS_FUEL_LEFT", 10), ("DA40_XLS_FUEL_RIGHT", 14), ("DA40_XLS_FUEL_SELECTOR", 1));
        Assert.Equal(0, e.LowerTank());
        Assert.Equal(1, e.FullerTank());
        Assert.Equal(1, e.GetValue(E.SelectorOnFullerTankField));
        Assert.Equal(0, e.GetValue(E.SelectorOnLowerTankField));
    }

    [Fact]
    public void The_standby_follows_the_G1000_in_hundredths()
    {
        Assert.Null(With(DA40Variant.NG).G1000BaroHundredths());
        Assert.Equal(3012, With(DA40Variant.NG, ("DA40_G1000_BARO", 30.12)).G1000BaroHundredths());
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_G1000_BARO", 30.12), ("DA40_STBY_ALTIMETER_SET", 30.12))
            .GetValue(E.AltimetersAgreeField));
        Assert.Equal(0, With(DA40Variant.NG, ("DA40_G1000_BARO", 30.12), ("DA40_STBY_ALTIMETER_SET", 29.92))
            .GetValue(E.AltimetersAgreeField));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_baro_text_is_culture_proof(string culture)
    {
        FbwA380StateEvaluatorTests.InCulture(culture, () =>
            Assert.Equal("1013 hectopascals, 29.92 inches", With(DA40Variant.NG, ("DA40_G1000_BARO", 29.92)).BaroText()));
    }

    [Fact]
    public void The_landing_flap_guard_reads_the_airspeed_against_VFE()
    {
        var speeds = DA40Speeds.For(DA40Variant.NG);
        Assert.True(double.IsNaN(With(DA40Variant.NG).GetValue(E.BelowVfeLandingField)));
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_AIRSPEED", speeds.VfeLanding - 5)).GetValue(E.BelowVfeLandingField));
        Assert.Equal(0, With(DA40Variant.NG, ("DA40_AIRSPEED", speeds.VfeLanding + 5)).GetValue(E.BelowVfeLandingField));
    }

    [Fact]
    public void The_NG_start_switches_are_safe_only_master_off_pump_off_voter_auto()
    {
        Assert.Equal(1, With(DA40Variant.NG, ("DA40_START_ENGINE_MASTER", 0), ("DA40_FUEL_PUMPS", 0), ("DA40_ECU_VOTER", 1))
            .GetValue(E.StartSwitchesSafeField));
        // The voter is inverted: 2 is ECU A (DA40E-1).
        Assert.Equal(0, With(DA40Variant.NG, ("DA40_START_ENGINE_MASTER", 0), ("DA40_FUEL_PUMPS", 0), ("DA40_ECU_VOTER", 2))
            .GetValue(E.StartSwitchesSafeField));
    }

    [Fact]
    public void The_polled_fields_are_this_airframe_s()
    {
        var ng = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        var xls = new CowsDA40Definition(DA40Variant.XLS).GetVariables();
        Assert.All(new E { Variant = DA40Variant.NG }.OnRequestPollFields, k => Assert.True(ng.ContainsKey(k), k));
        Assert.All(new E { Variant = DA40Variant.XLS }.OnRequestPollFields, k => Assert.True(xls.ContainsKey(k), k));
    }
}
