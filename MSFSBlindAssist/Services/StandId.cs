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

        // Uppercase, drop ALL whitespace: "N 1" -> "N1", "P 209" -> "P209".
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        string s = sb.ToString();

        // Strip ONE leading descriptor word: "GATE11B" -> "11B", "STAND5" -> "5", "PARKING209" -> "209".
        foreach (var w in new[] { "GATE", "STAND", "PARKING" })
            if (s.StartsWith(w, StringComparison.Ordinal) && s.Length > w.Length)
            { s = s.Substring(w.Length); break; }

        var m = Shape.Match(s);
        if (m.Success)
            return new StandId(m.Groups[1].Value,
                               int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                               m.Groups[3].Value, true);

        // No number (a bare letter like "N", or a word like "HAWKER").
        return new StandId(s, 0, "", false);
    }
}
