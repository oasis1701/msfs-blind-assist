// The PFD's FMA strip: the texts the armed roll column gives through Pmdg737PfdText, from the PFD's golden layout.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdOptionalTests
{
    /// <summary>Works out <paramref name="e"/> with the glass data fields given by offset (every other field 0).</summary>
    private static double Value(PfdExpr e, params (int Offset, double Value)[] fields) =>
        PfdEval.Value(e, (f, _) => fields.Where(x => x.Offset == f.Field.Offset).Select(x => x.Value).DefaultIfEmpty(0).First(), 0);

    [Fact]
    public void Armed_roll_column_is_derived()
    {
        // The glass data's armed roll (at 2960 in the test layout) shows LNAV armed beside another mode: PMDG draws
        // literals for 9, 10 and 14 and its roll table's word for 0 to 14, in that order.
        var strip = GoldenLayouts.Pfd.FmaStrip;
        string Armed(int v) => Pmdg737PfdText.ArmedRoll(strip.ArmedRoll, e => Value(e, (2960, v)));
        Assert.Equal("LNAV HDG SEL", Armed(9));
        Assert.Equal("LNAV VOR/LOC", Armed(10));
        Assert.Equal("LNAV ROLLOUT", Armed(14));
        Assert.Equal("LNAV", Armed(11));
        Assert.Equal("VOR/LOC", Armed(4));
        Assert.Equal("", Armed(0));
        Assert.Equal("", Armed(1));
        Assert.Equal("", Armed(15));
    }
}
