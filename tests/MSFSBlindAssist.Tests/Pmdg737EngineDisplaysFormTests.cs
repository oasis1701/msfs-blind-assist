// The Engine displays window's logic: the manual N1 field is usable only when N1 SET is on 1, 2 or BOTH
// (as the Fenix altimeter window disables its value field in STD), and a refresh whose provider fails
// keeps the lines the pilot was reading instead of emptying the lists.

using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplaysFormTests
{
    [Theory]
    [InlineData(0.0, true, true)]    // "2"
    [InlineData(1.0, true, true)]    // "1"
    [InlineData(3.0, true, true)]    // "BOTH"
    [InlineData(2.0, true, false)]   // "AUTO"
    [InlineData(null, true, false)]  // N1 SET not read
    [InlineData(0.0, false, false)]  // no engine display reader
    public void Manual_n1_enabled_only_on_1_2_or_both(double? position, bool available, bool expected) =>
        Assert.Equal(expected, PMDG737EngineDisplaysForm.ManualN1FieldEnabled(position, available));

    [Fact]
    public void Provider_failure_keeps_the_last_lines()
    {
        IReadOnlyList<string> upper = new[] { "Upper display: primary engine page", "TAT +19C" };
        IReadOnlyList<string> lower = new[] { "Lower display: SYS page" };

        var kept = PMDG737EngineDisplaysForm.NextLines(() => throw new InvalidOperationException("reader gone"), upper, lower);
        Assert.Same(upper, kept.Upper);
        Assert.Same(lower, kept.Lower);

        var view = new EngineDisplaysView(new[] { "Engine displays starting" }, new[] { "Engine displays starting" }, null, null);
        var next = PMDG737EngineDisplaysForm.NextLines(() => view, upper, lower);
        Assert.Same(view.Upper, next.Upper);
        Assert.Same(view.Lower, next.Lower);
    }
}
