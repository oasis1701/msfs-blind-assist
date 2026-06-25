using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Canonical parse of a stand/gate label or ref into (leading letter, number, trailing suffix).
/// Shared by the gate-alias resolver and search so they agree on identity. Pure, allocation-light.
/// </summary>
public readonly record struct StandId(string Letter, int Number, string Suffix, bool HasNumber)
{
    private static readonly Regex Shape = new(@"^([A-Z]*)([0-9]+)([A-Z]*)$", RegexOptions.Compiled);

    /// <summary>Letter+number+suffix, e.g. "A51", "55A", "N3" (no number → letter+suffix).</summary>
    public string Canonical =>
        HasNumber ? Letter + Number.ToString(CultureInfo.InvariantCulture) + Suffix : Letter + Suffix;

    public static StandId Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new StandId("", 0, "", false);

        // Drop stand-TYPE qualifier words (Ramp/Gate/Stand/Apron/Dock/Tie Down/…) token-by-token and
        // all whitespace, uppercasing: "Ramp 51" -> "51", "Tie Down 5" -> "5", "N 1" -> "N1". Shares
        // GateSearchFilter.StandTypeWords so the identity parser and the search/normalizer agree —
        // otherwise "Ramp 51" would parse to a bogus letter "RAMP" and mint a junk alias. "GA" is
        // intentionally NOT a type word (real GA-apron concourse), so "GA 5" -> "GA5" is preserved.
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var tok in raw.ToUpperInvariant()
                     .Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries))
        {
            if (GateSearchFilter.StandTypeWords.Contains(tok)) continue;
            sb.Append(tok);
        }
        string s = sb.ToString();

        // int.TryParse, NOT int.Parse: the digit run comes from untrusted online stand names
        // (OSM ref / apt.dat free text), so a 11+ digit token overflows Int32. On overflow fall
        // through to the no-number branch — a 12-digit "gate number" is not a real gate — instead
        // of throwing out of AugmentParking into the UI thread.
        var m = Shape.Match(s);
        if (m.Success && int.TryParse(m.Groups[2].Value, NumberStyles.Integer,
                                      CultureInfo.InvariantCulture, out int number))
            return new StandId(m.Groups[1].Value, number, m.Groups[3].Value, true);

        // No number (a bare letter like "N", a word like "HAWKER", or an over-long digit run).
        return new StandId(s, 0, "", false);
    }
}
