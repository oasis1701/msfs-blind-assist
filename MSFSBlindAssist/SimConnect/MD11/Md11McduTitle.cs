using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>
/// What an MD-11 MCDU title says about the PAGE, as opposed to the page counter.
///
/// MD-11 titles carry their page counter in the title row itself — the live Left unit read
/// <c>      ACT F-PLN     1/2</c> (2026-09-07 16:57). A slew past the sixth waypoint turns that
/// into <c>ACT F-PLN     2/2</c>: the title TEXT changed, the page did not. The window treated
/// every title change as a page change — cursor forced to line 1, title announced — which is
/// the second half of "hitting Alt+Up more than a few times will cause the focus to jump off of
/// line 6" (the first half is <see cref="Md11McduRows"/>).
///
/// The other CDU forms never had to make this distinction: the FBW/Fenix exports carry the page
/// counter as a separate field and their forms compare the title alone, while the PMDG title
/// row carries no counter on its scrolling pages. Here it has to be split off.
/// </summary>
public static class Md11McduTitle
{
    /// <summary>
    /// A page counter at the END of the title only — "1/2", "10/12" — with at least one space
    /// before it, so a title that merely contains a slash keeps its whole text.
    /// </summary>
    private static readonly Regex TrailingCounter =
        new(@"\s+\d+/\d+\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The title without its trailing page counter, trimmed.</summary>
    public static string PageName(string? title) =>
        string.IsNullOrEmpty(title) ? string.Empty : TrailingCounter.Replace(title, string.Empty).Trim();

    /// <summary>
    /// The Honeywell FMS retitles a page while a modification is pending — ACT F-PLN becomes
    /// MOD F-PLN the moment a waypoint is entered — and it is the same page with the same lines
    /// under the cursor; SEC F-PLN is a different flight plan and stays distinct. "ACT F-PLN" is
    /// captured live on this aircraft; "MOD" is the Honeywell convention and not yet captured —
    /// folding a prefix that never appears is harmless. The one residual is prefix versus BARE:
    /// "ACT F-PLN" and a plain "F-PLN" page would read as one page and keep the cursor mid-page
    /// on different lines; no bare-named twin of a prefixed page has been captured, and the
    /// direction is a kept cursor, never a thrown one.
    /// </summary>
    private static readonly string[] PendingPrefixes = { "ACT ", "MOD " };

    /// <summary>The page name with a pending-modification prefix folded away: what the cursor follows.</summary>
    public static string Identity(string? title)
    {
        var name = PageName(title);
        foreach (var prefix in PendingPrefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return name.Substring(prefix.Length).Trim();
        return name;
    }

    /// <summary>
    /// True when the two titles name the same page — a counter ticking over is the same page,
    /// and so is a flight plan going from ACT to MOD as an entry is made. Announcing a changed
    /// title is a separate decision from moving the cursor; this answers only the second.
    /// </summary>
    public static bool SamePage(string? previousTitle, string? title) =>
        string.Equals(Identity(previousTitle), Identity(title), StringComparison.Ordinal);
}
