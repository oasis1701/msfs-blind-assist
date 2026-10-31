using System.Globalization;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS's indicated tank quantities are the G1000's INDICATION, not the probe behind it.
/// COWS 1.2.0's Logic writes DISP_FUEL:n from the probe and zeroes it with the tank's
/// indication failure; the Engine page draws its digits from it. Reading the probe kept a perfect
/// quantity on screen off a failed gauge, which is the DISP_ rule in docs/da40.md.
/// </summary>
public class CowsDA40XlsFuelIndicationTests
{
    [Theory]
    [InlineData("DA40_XLS_FUEL_LEFT_IND", "DISP_FUEL:1")]
    [InlineData("DA40_XLS_FUEL_RIGHT_IND", "DISP_FUEL:2")]
    public void EachTankReadsTheIndication(string key, string lvar)
        => Assert.Equal(lvar, new CowsDA40Definition(DA40Variant.XLS).GetVariables()[key].Name);

    [Fact]
    public void TheRowSaysWhatTheEnginePageDraws()
    {
        var prior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var def = new CowsDA40Definition(DA40Variant.XLS);
            Assert.True(def.TryGetDisplayOverride("DA40_XLS_FUEL_LEFT_IND", 12.4, out string text));
            Assert.StartsWith("12.4", text);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void AFailedIndicationReadsZero()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        Assert.True(def.TryGetDisplayOverride("DA40_XLS_FUEL_RIGHT_IND", 0, out string text));
        Assert.StartsWith("0.0", text);
    }
}

/// <summary>
/// The package-presence test checks L:vars only, so a STOCK SimVar describing a system the
/// XLS has not got slipped through: the coolant reservoir read 0 percent live on the XLS.
/// </summary>
public class CowsDA40XlsStockScopeTests
{
    [Theory]
    [InlineData("RECIP ENG COOLANT RESERVOIR PERCENT:1")]
    [InlineData("RECIP ENG TURBOCHARGER FAILED:1")]
    [InlineData("RECIP ENG ENGINE MASTER SWITCH:1")]
    [InlineData("GENERAL ENG FUEL PUMP SWITCH EX1:1")]
    public void TheXlsBindsNoStockVariableForASystemItHasNotGot(string simVar)
    {
        Assert.DoesNotContain(new CowsDA40Definition(DA40Variant.XLS).GetVariables().Values, d => d.Name == simVar);
        Assert.Contains(new CowsDA40Definition(DA40Variant.NG).GetVariables().Values, d => d.Name == simVar);
    }
}
