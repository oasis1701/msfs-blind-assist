using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.DA40;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Every DA40 number speaks with a decimal point whatever the PC's region. On a Swedish
/// Windows (2026-10-26) the squawk was announced "Squawk: 2000,0" and the steering assist
/// "2,0"; CI runs under en-US, so only a test that sets a comma culture can see it.
/// </summary>
public class CowsDA40DecimalPointTests
{
    private static readonly Regex DecimalComma = new(@"\d,\d");

    public static TheoryData<DA40Variant, string> Variables()
    {
        var data = new TheoryData<DA40Variant, string>();
        foreach (var variant in new[] { DA40Variant.NG, DA40Variant.XLS })
        {
            foreach (var key in new CowsDA40Definition(variant).GetVariables().Keys)
            {
                data.Add(variant, key);
            }
        }
        return data;
    }

    [Theory]
    [InlineData(DA40Variant.NG, 2000, "Squawk: 2000")]
    [InlineData(DA40Variant.XLS, 700, "Squawk: 0700")]
    public void TheSquawkIsFourDigitsWithNoDecimal(DA40Variant variant, double code, string expected)
    {
        var v = new CowsDA40Definition(variant).GetVariables()["DA40_XPDR_CODE"];

        string spoken = TestCulture.Run("sv-SE",
            () => new SimConnectManager(IntPtr.Zero).FormatVariableValue("DA40_XPDR_CODE", v, code));

        Assert.Equal(expected, spoken);
    }

    [Theory]
    [MemberData(nameof(Variables))]
    public void APanelReadoutShowsADecimalPoint(DA40Variant variant, string key)
    {
        var def = new CowsDA40Definition(variant);

        string? shown = TestCulture.Run("sv-SE",
            () => def.TryGetDisplayOverride(key, 24.15, out string text) ? text : null);

        if (shown is not null) Assert.DoesNotMatch(DecimalComma, shown);
    }
}
