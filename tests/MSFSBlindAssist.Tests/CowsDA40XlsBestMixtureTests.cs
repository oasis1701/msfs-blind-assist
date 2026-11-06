using System;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Set Best Mixture walks the lever for seconds and the pilot heard nothing of where it
/// stopped (MTL cruise, 2026-10-19: 100 to 62 percent, silent). A sighted pilot watches the
/// lever stop; the read-back is that, said once when the lever has settled.
/// </summary>
public class CowsDA40XlsBestMixtureTests
{
    private static readonly DateTime T0 = new(2026, 10, 19, 14, 34, 0, DateTimeKind.Utc);

    [Fact]
    public void TheWalkIsSpokenOnceWhereItStops()
    {
        var s = new CowsDA40Definition.BestMixtureSettle(T0, 100);
        Assert.Null(s.Step(96, T0.AddSeconds(1)));
        Assert.Null(s.Step(80, T0.AddSeconds(2)));
        Assert.Null(s.Step(62, T0.AddSeconds(3)));
        Assert.Null(s.Step(62, T0.AddSeconds(4)));
        Assert.Equal(62, s.Step(62, T0.AddSeconds(3).AddMilliseconds(CowsDA40Definition.BestMixtureSettle.SettleMs)));
        Assert.True(s.Done);
        Assert.Null(s.Step(62, T0.AddSeconds(9)));
    }

    [Fact]
    public void ALeverThatNeverMovesIsStillAnswered()
    {
        // Already at best (or the automixture snap landed before the first sample).
        var s = new CowsDA40Definition.BestMixtureSettle(T0, 72);
        Assert.Null(s.Step(72, T0.AddSeconds(1)));
        Assert.Equal(72, s.Step(72, T0.AddMilliseconds(CowsDA40Definition.BestMixtureSettle.SettleMs)));
    }

    [Fact]
    public void AWalkThatNeverSettlesIsAnsweredAtTheTimeout()
    {
        var s = new CowsDA40Definition.BestMixtureSettle(T0, 100);
        double v = 100;
        for (int ms = 500; ms < CowsDA40Definition.BestMixtureSettle.TimeoutMs; ms += 500)
            Assert.Null(s.Step(v -= 1, T0.AddMilliseconds(ms)));
        Assert.Equal(v - 1, s.Step(v - 1, T0.AddMilliseconds(CowsDA40Definition.BestMixtureSettle.TimeoutMs)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void TheReadBackIsTheLeverInWholePercent(string culture)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            Assert.Equal("Best mixture, mixture 62 percent", CowsDA40Definition.BestMixtureText(61.704));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }
}
