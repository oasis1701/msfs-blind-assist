// Culture-proof number parsing. Every case runs under sv-SE (comma decimal, no-break-space
// grouping) and de-DE (comma decimal, "." grouping) as well as the invariant culture: CI runs
// en-US, where the PC-culture bugs these replace cannot show.

using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

public class NumberParsingTests
{
    private static void Under(string culture, Action body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // Spelled as escapes: a screen reader cannot tell these from "-" and " ".
    private const string Minus = "\U00002212";   // U+2212 MINUS SIGN, written by .NET 5+ for sv-SE
    private const string Nbsp = "\U000000A0";    // no-break space, the sv-SE group separator
    private const string Nnbsp = "\U0000202F";   // narrow no-break space, the fr-FR group separator

    // External: live ActiveSky values at ESGG 2026-10-12 under Swedish Windows.
    [Theory]
    [InlineData("1014.999", 1014.999)]
    [InlineData("1014,999", 1014.999)]
    [InlineData("7,992481", 7.992481)]
    [InlineData("-12,5", -12.5)]
    [InlineData(Minus + "12,5", -12.5)]
    [InlineData("8", 8.0)]
    [InlineData("6449", 6449.0)]
    [InlineData(Nbsp + "7,5" + Nnbsp, 7.5)]   // surrounding spaces are trimmed
    [InlineData("1.000000", 1.0)]
    public void TryParseExternal_reads_invariant_and_comma_decimal_strings(string raw, double expected)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () =>
            {
                Assert.True(NumberParsing.TryParseExternal(raw, out double d), $"{raw} under {culture}");
                Assert.Equal(expected, d, 6);
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("n/a")]
    // Such programs never group thousands, so anything that could only be grouping is refused.
    [InlineData("1.014,999")]
    [InlineData("1,014.999")]
    [InlineData("1 014,999")]
    [InlineData("1" + Nbsp + "014,999")]
    [InlineData("1" + Nnbsp + "014,999")]
    [InlineData("12 5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void TryParseExternal_refuses_non_numbers_and_grouping(string? raw)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () => Assert.False(NumberParsing.TryParseExternal(raw, out _), $"{raw} under {culture}"));
    }

    // Typed: what a pilot enters in a speed, V/S, altitude, heading or baro box.
    [Theory]
    [InlineData("29.92", 29.92)]
    [InlineData("29,92", 29.92)]
    [InlineData("0,82", 0.82)]
    [InlineData("0.82", 0.82)]
    [InlineData(",82", 0.82)]
    [InlineData("0,820", 0.82)]
    [InlineData("-2,5", -2.5)]
    [InlineData("+1500", 1500.0)]
    [InlineData("-1500", -1500.0)]
    [InlineData(" 1013 ", 1013.0)]
    [InlineData("1013,5", 1013.5)]
    [InlineData("10000", 10000.0)]
    [InlineData("0.820", 0.82)]
    [InlineData("1013,250", 1013.25)]   // four whole digits: no group
    public void TryParseTyped_accepts_either_decimal_separator(string raw, double expected)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () =>
            {
                Assert.True(NumberParsing.TryParseTyped(raw, out double d), $"{raw} under {culture}");
                Assert.Equal(expected, d, 6);
            });
    }

    // A thousands group is refused, never guessed: "1,000" fpm must not become an FPA of 1.0.
    [Theory]
    [InlineData("1,000")]
    [InlineData("10,000")]
    [InlineData("-1,500")]
    [InlineData("1.013,5")]
    [InlineData("1,013.5")]
    [InlineData("1,2,3")]
    // de-DE and sv-SE group with ".": the PC-culture parse the Fenix windows used read "1.500" as 1500,
    // so reading it as 1.5 would set a 2 fpm V/S (or an FPA of 1.5 in the FBW windows).
    [InlineData("1.500")]
    [InlineData("10.000")]
    [InlineData("-1.500")]
    [InlineData("35.000")]
    [InlineData("29.920")]
    [InlineData("1.2.3")]
    // NaN passes every "v < min || v > max" range check.
    [InlineData("nan")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    public void TryParseTyped_refuses_grouping_and_garbage(string? raw)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () => Assert.False(NumberParsing.TryParseTyped(raw, out _), $"{raw} under {culture}"));
    }

    // The panel text boxes (MainForm.PanelBuilder) parse with TryParseTyped, COM and NAV
    // frequencies included: a three-digit whole part is never a group (it would be 100,000 or
    // more), so "118,275" reads while "10,000" is refused rather than sent as 10.
    [Theory]
    [InlineData("118,275", 118.275)]
    [InlineData("118.275", 118.275)]
    [InlineData("110,30", 110.3)]
    [InlineData("136,975", 136.975)]
    public void TryParseTyped_reads_a_typed_frequency(string raw, double expected)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () =>
            {
                Assert.True(NumberParsing.TryParseTyped(raw, out double d), $"{raw} under {culture}");
                Assert.Equal(expected, d, 6);
            });
    }
}
