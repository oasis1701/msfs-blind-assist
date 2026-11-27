using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE TWO AEROPLANES DO NOT SHARE ALL THIRTY-FOUR BREAKERS, AND THE FILE USED TO SAY
/// "Both variants".
///
/// Counted out of each model's OWN CircuitBreakers.xml in the installed package: 34 each,
/// 28 common, and SIX different each way. So the XLS panel offered six breakers its
/// aeroplane does not have - ECU A, ECU B, Fuel Pump A, Fuel Pump B, Power and Fuel
/// Transfer, every one an Austro/NG item - while MISSING six it does have, including the
/// ALTERNATOR and the FUEL PUMP, both of which the AFM's emergency drills tell a pilot to
/// pull. A breaker row that cannot do anything is worse than a missing one: a pilot reaches
/// for it in a checklist and finds a dead control.
///
/// The six XLS names are expanded from that aeroplane's own AFM section 1.5.6, shipped
/// inside the package - not guessed from the abbreviation.
/// </summary>
public class CowsDA40BreakerVariantTests
{
    private static string[] BreakerLvars(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();
        return def.GetPanelControls()
                  .Where(p => p.Key.StartsWith("Engine and Fuel") || p.Key.StartsWith("Flight Instruments")
                           || p.Key.StartsWith("Avionics") || p.Key.StartsWith("Bus and Power")
                           || p.Key.StartsWith("Lighting") || p.Key.StartsWith("Airframe Systems"))
                  .SelectMany(p => p.Value)
                  .Where(k => vars.ContainsKey(k) && vars[k].Name.StartsWith("CB_"))
                  .Select(k => vars[k].Name)
                  .Distinct()
                  .OrderBy(x => x, System.StringComparer.Ordinal)
                  .ToArray();
    }

    [Fact]
    public void TheXlsCarriesItsOwnSixAndNoneOfTheNgs()
    {
        var xls = BreakerLvars(DA40Variant.XLS);

        foreach (var own in new[] { "CB_ACN", "CB_ALT", "CB_APT", "CB_FAN", "CB_FUP", "CB_TAS" })
            Assert.Contains(own, xls);

        // Austro-only, and the XLS has no ECU, no transfer pump and no second electric pump.
        foreach (var ng in new[] { "CB_ECA", "CB_ECB", "CB_FPA", "CB_FPB", "CB_PWR", "CB_XFR" })
            Assert.DoesNotContain(ng, xls);
    }

    [Fact]
    public void TheNgKeepsItsOwnSixAndNoneOfTheXlss()
    {
        var ng = BreakerLvars(DA40Variant.NG);

        foreach (var own in new[] { "CB_ECA", "CB_ECB", "CB_FPA", "CB_FPB", "CB_PWR", "CB_XFR" })
            Assert.Contains(own, ng);

        foreach (var xls in new[] { "CB_ACN", "CB_ALT", "CB_APT", "CB_FAN", "CB_FUP", "CB_TAS" })
            Assert.DoesNotContain(xls, ng);
    }

    /// <summary>Each aeroplane has THIRTY-FOUR, which is what the model files count.</summary>
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EachVariantCarriesThirtyFour(DA40Variant variant)
    {
        Assert.Equal(34, BreakerLvars(variant).Length);
    }
}
