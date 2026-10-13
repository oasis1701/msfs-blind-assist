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

    // External: live ActiveSky values at ESGG 2026-10-12 under Swedish Windows, plus grouping forms.
    [Theory]
    [InlineData("1014.999", 1014.999)]
    [InlineData("1014,999", 1014.999)]
    [InlineData("7,992481", 7.992481)]
    [InlineData("-12,5", -12.5)]
    [InlineData("8", 8.0)]
    [InlineData("6449", 6449.0)]
    [InlineData("1.014,999", 1014.999)]
    [InlineData("1,014.999", 1014.999)]
    [InlineData("1 014,999", 1014.999)]
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
    public void TryParseExternal_rejects_non_numbers(string? raw)
        => Assert.False(NumberParsing.TryParseExternal(raw, out _));

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
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    public void TryParseTyped_refuses_grouping_and_garbage(string? raw)
    {
        foreach (var culture in new[] { "sv-SE", "de-DE", "en-US" })
            Under(culture, () => Assert.False(NumberParsing.TryParseTyped(raw, out _), $"{raw} under {culture}"));
    }
}
