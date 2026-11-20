using System.Globalization;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Ctrl+N on the DA40: one window, a box per NAV radio, a Set button each, and Set all, the
/// Ctrl+B shape. It was two windows in a row, NAV 1 then NAV 2.
/// </summary>
public class CowsDA40NavSetTests
{
    [Fact]
    public void TheNav2SetButtonSetsNav2AloneEvenWithNav1Empty()
        => Assert.Equal(new[] { (2, 110.30) }, CowsDA40Definition.PlanNavSet(1, "", "110.30"));

    [Fact]
    public void TheNav1SetButtonSetsNav1Alone()
        => Assert.Equal(new[] { (1, 113.90) }, CowsDA40Definition.PlanNavSet(0, "113.90", "garbage"));

    [Fact]
    public void SetAllSetsEachToItsOwnValueInRadioOrder()
        => Assert.Equal(new[] { (1, 109.50), (2, 112.70) },
            CowsDA40Definition.PlanNavSet(-1, "109.50", "112.70"));

    [Theory]
    [InlineData("107.95")]
    [InlineData("118.00")]
    [InlineData("")]
    public void AnythingOutsideTheNavBandSetsNothing(string text)
        => Assert.Empty(CowsDA40Definition.PlanNavSet(0, text, "110.30"));

    // CI runs under en-US; a pilot on a Swedish or German PC types a comma.
    [Theory]
    [InlineData("110,30")]
    [InlineData("110.30")]
    public void BothDecimalMarksReadUnderACommaCulture(string text)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(new[] { (1, 110.30) }, CowsDA40Definition.PlanNavSet(0, text, ""));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
