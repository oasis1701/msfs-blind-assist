using System.Globalization;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// Culture-proof number parsing. Two traps this replaces:
/// <list type="bullet">
/// <item>The PC's culture (a bare <c>double.TryParse(s, out v)</c>): under sv-SE "29.92" does not
/// parse, and under de-DE it reads 2992.</item>
/// <item><c>NumberStyles.Any</c> with the invariant culture: "," is then a THOUSANDS separator, so
/// a pilot's "0,82" reads 82 and ActiveSky's "1014,999" read 1014999 hPa.</item>
/// </list>
/// </summary>
public static class NumberParsing
{
    /// <summary>
    /// A number another program wrote (ActiveSky, the MobiFlight WASM). Usually invariant, but some
    /// write in the OS culture. Such programs never group thousands (ActiveSky's live ESGG capture
    /// sent CloudCeiling "6449", with no sv-SE group space), so a lone "," is the decimal separator.
    /// A three-digit-group check, as in <see cref="TryParseTyped"/>, cannot stand in for that
    /// premise: ActiveSky's QNH "1014,999" has three decimals. What could only be grouping ("," and
    /// "." together, or whitespace inside the number) is refused, so the field reads as missing,
    /// never guessed. The U+2212 minus sign .NET 5+ writes for sv-SE, nb-NO, fi-FI and other
    /// comma-decimal cultures reads as "-".
    /// </summary>
    public static bool TryParseExternal(string? s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim();
        if (t.Contains(','))
        {
            if (t.Contains('.')) return false;
            t = t.Replace(',', '.');
        }
        if (t.Contains('\x2212')) t = t.Replace('\x2212', '-');   // U+2212 MINUS SIGN
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// A number the pilot typed, on any Windows culture: "29.92" and "29,92" both read 29.92, a
    /// leading sign is allowed. Input that could be a thousands group is REFUSED rather than guessed:
    /// "10,000" or "1.013,5" fails, so the dialog says the entry is invalid instead of setting 10 or
    /// a wrong value. Exactly three digits after a lone comma counts as a group unless the whole
    /// part is 0 ("0,820" is a Mach).
    /// </summary>
    public static bool TryParseTyped(string? s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        string t = s.Trim();
        int comma = t.IndexOf(',');
        if (comma >= 0)
        {
            if (t.IndexOf('.') >= 0 || t.IndexOf(',', comma + 1) >= 0) return false;
            string whole = t[..comma].TrimStart('+', '-');
            string frac = t[(comma + 1)..];
            if (frac.Length == 3 && frac.All(char.IsDigit) && whole.Length > 0 && whole.TrimStart('0').Length > 0)
                return false;
            t = t.Replace(',', '.');
        }
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
