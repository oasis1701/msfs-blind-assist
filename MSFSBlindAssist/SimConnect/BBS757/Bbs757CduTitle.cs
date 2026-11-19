using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// A BlueBird 757 CDU title row (line 0 of <see cref="Bbs757CduScreen"/>, e.g.
/// <c>"  ACT RTE 1 LEGS     1/7"</c>): the page's name, its "n/m" page counter at the right, and the
/// "ACT " or "MOD " a route or performance page shows in front while active or modified. The page
/// service tells pages apart by <see cref="Identity"/>; the CDU window can speak
/// <see cref="Spoken"/>. Pure, and it never throws: a null row reads as a blank one.
/// </summary>
public static class Bbs757CduTitle
{
    // A page counter ending the row, after a space or alone: "1/7", "10/12". A 0 is no counter.
    private static readonly Regex CounterPattern = new(@"(?:^|\s)([1-9][0-9]?)/([1-9][0-9]?)$", RegexOptions.CultureInvariant);
    private static readonly Regex SpaceRuns = new(@"\s+", RegexOptions.CultureInvariant);

    // The states a page title can show in front of its name.
    private static readonly string[] States = { "ACT ", "MOD " };

    /// <summary>The title trimmed, without its page counter: "ACT RTE 1 LEGS".</summary>
    public static string PageName(string titleRow)
    {
        string row = (titleRow ?? "").Trim();
        var m = CounterPattern.Match(row);
        return m.Success ? row[..m.Index].TrimEnd() : row;
    }

    /// <summary>The page counter, (1, 7) for "1/7"; null when the title has none.</summary>
    public static (int Page, int Pages)? Counter(string titleRow)
    {
        var m = CounterPattern.Match((titleRow ?? "").Trim());
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The page whatever its state: <see cref="PageName"/> with a leading "ACT " or "MOD " folded away,
    /// so "ACT RTE 1 LEGS" and "MOD RTE 1 LEGS" are both "RTE 1 LEGS".
    /// </summary>
    public static string Identity(string titleRow)
    {
        string name = PageName(titleRow);
        foreach (var state in States)
            if (name.StartsWith(state, StringComparison.Ordinal)) return name[state.Length..].TrimStart();
        return name;
    }

    /// <summary>The whole title with its runs of spaces collapsed: "ACT RTE 1 LEGS 1/7".</summary>
    public static string Spoken(string titleRow) => SpaceRuns.Replace((titleRow ?? "").Trim(), " ");
}
