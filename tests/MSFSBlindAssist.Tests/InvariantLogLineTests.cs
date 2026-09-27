// landing_exit.log lines are written through Utils.Logging.InvariantLogLine so a German or Turkish Windows
// writes "gs=44.1kt", never "gs=44,1kt" - one log format from every pilot's machine.

using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Tests;

public class InvariantLogLineTests
{
    private static string UnderCulture(string name, Func<string> render)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    public void Fractional_numbers_use_a_decimal_point_whatever_the_culture(string culture)
    {
        double gs = 44.1, lat = 35.123456;
        string line = UnderCulture(culture,
            () => InvariantLogLine.Format($"gs={gs:F1}kt lat={lat:F6} hdg={-3.25:+0.0;-0.0}"));
        Assert.Equal("gs=44.1kt lat=35.123456 hdg=-3.3", line);
    }

    [Fact]
    public void A_plus_chain_of_interpolated_strings_is_one_invariant_line()
    {
        // RolloutDiag's multi-line calls are "..." + "..." chains of interpolated strings; C# must bind the
        // whole chain to the handler, or the parts before the handler would be formatted in the pilot's culture.
        double a = 1.5, b = 2.25;
        string line = UnderCulture("de-DE",
            () => InvariantLogLine.Format($"a={a:F1} " + $"b={b:F2}"));
        Assert.Equal("a=1.5 b=2.25", line);
    }
}
