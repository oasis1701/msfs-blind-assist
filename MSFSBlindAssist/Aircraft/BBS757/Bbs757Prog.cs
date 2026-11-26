using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>The PROG page's destination line: "KMIA", 568 miles to go, ETA "0332" (Zulu); null when not shown.</summary>
public sealed record Bbs757ProgDest(string Ident, int Miles, string? EtaZulu);

/// <summary>The PROG page's top of climb ("T/C"), step climb ("STEP CLB") or top of descent ("T/D"): miles to it and its time (Zulu); null when not shown.</summary>
public sealed record Bbs757ProgTop(string Kind, int Miles, string? EtaZulu);

/// <summary>
/// D and Shift+D (plan 06): the distance and ETA to the destination, and to the top of climb or descent, read
/// off PROG page 1 on the first officer's CDU. Measured layout (session 8, flight 356): a label row " DEST" then
/// "KMIA    568  0332Z   6.1"; a label row " ECON SPD         TO T/C" (or "TO T/D") then
/// "299/.785     0218Z/ 56NM". With a planned step climb ahead the same row says "TO STEP CLB" instead
/// (".801       1740Z/ 1219NM", 2026-10-27, a SimBrief step on a long flight), and the T/D shows there only
/// once no step is left: the DES page shows no T/D. The parse finds the label rows rather than counting them,
/// and never throws.
/// <c>L:BBS_VNAV_TOD</c> is not used: it does not count down (in cruise on 2026-10-26 it read
/// 118.39569 unchanged over several minutes while PROG's T/D distance fell).
/// </summary>
public static class Bbs757Prog
{
    public const string NoCdu = "The first officer's CDU did not answer.";

    // How many PREV presses bring PROG back to page 1 from page 3 (it has three pages).
    private const int MaxPrevPresses = 2;

    private static readonly Regex DestData = new(@"^\s*([A-Z0-9]{3,5})\s+(\d+)(?:\.\d+)?(?:\s+(\d{4})Z)?", RegexOptions.CultureInvariant);
    private static readonly Regex TopLabel = new(@"TO (T/C|T/D|STEP CLB)", RegexOptions.CultureInvariant);
    private static readonly Regex TopData = new(@"(?:(\d{4})Z)?\s*/\s*(\d+)(?:\.\d+)?\s*NM", RegexOptions.CultureInvariant);

    /// <summary>Any PROG page: its title's name ends with "PROGRESS".</summary>
    public static bool IsProgress(Bbs757CduScreen s)
        => Bbs757CduTitle.PageName(s.Lines[0]).EndsWith("PROGRESS", StringComparison.Ordinal);

    /// <summary>PROG page 1: a PROG page whose counter reads 1 (or that shows none).</summary>
    public static bool IsPageOne(Bbs757CduScreen s)
        => IsProgress(s) && (Bbs757CduPages.PageCount(s) is not { } count || count.Page == 1);

    public static Bbs757ProgDest? Destination(Bbs757CduScreen s)
    {
        if (DataAfter(s, label => label.TrimStart().StartsWith("DEST", StringComparison.Ordinal)) is not { } row) return null;
        var m = DestData.Match(row);
        if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int miles)) return null;
        return new Bbs757ProgDest(m.Groups[1].Value, miles, m.Groups[3].Success ? m.Groups[3].Value : null);
    }

    public static Bbs757ProgTop? TopOf(Bbs757CduScreen s)
    {
        for (int i = 1; i + 1 < s.Lines.Count && i < Bbs757CduFrame.ScratchpadRow; i++)
        {
            var label = TopLabel.Match(s.Lines[i] ?? "");
            if (!label.Success) continue;
            var m = TopData.Match(s.Lines[i + 1] ?? "");
            if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int miles)) return null;
            return new Bbs757ProgTop(label.Groups[1].Value, miles, m.Groups[1].Success ? m.Groups[1].Value : null);
        }
        return null;
    }

    /// <summary>"568 miles to destination KMIA, ETA 03:32 Zulu".</summary>
    public static string DestinationPhrase(Bbs757ProgDest? d)
        => d == null ? "No destination on the progress page"
            : $"{Miles(d.Miles)} to destination {d.Ident}" + (d.EtaZulu is { } eta ? $", ETA {Clock(eta)} Zulu" : "");

    /// <summary>
    /// "Top of climb in 56 miles, 02:18 Zulu", "Top of descent in …", "Step climb in …, top of descent after it",
    /// or "No top of climb or descent ahead".
    /// </summary>
    public static string TopPhrase(Bbs757ProgTop? t)
    {
        if (t == null) return "No top of climb or descent ahead";
        string at = t.EtaZulu is { } eta ? $", {Clock(eta)} Zulu" : "";
        return t.Kind == "STEP CLB"
            ? $"Step climb in {Miles(t.Miles)}{at}, top of descent after it"
            : $"Top of {(t.Kind == "T/C" ? "climb" : "descent")} in {Miles(t.Miles)}{at}";
    }

    /// <summary>
    /// PROG page 1 on the first officer's CDU: PROG (unless a PROG page is already up), then PREV until the
    /// counter reads 1, at most twice. Null when the CDU does not answer or page 1 is not reached. One call of
    /// the page service (<see cref="Bbs757CduPages.WalkAsync"/>): D and Shift+D pressed quickly, or another
    /// reader's walk, never press between its PROG and its PREVs.
    /// </summary>
    public static Task<Bbs757CduScreen?> ReadPageOneAsync(Bbs757CduPages pages, CancellationToken ct) =>
        pages.WalkAsync<Bbs757CduScreen?>("PROG page 1 walk", null, ct, async walk =>
        {
            var screen = await walk.ShowPageAsync(Bbs757CduPages.FirstOfficer, Bbs757CduKeys.Prog, IsProgress, ct);
            for (int presses = 0; screen != null && !IsPageOne(screen); presses++)
            {
                if (presses == MaxPrevPresses) return null;
                screen = await walk.PressKeysAsync(Bbs757CduPages.FirstOfficer, new[] { Bbs757CduKeys.Prev }, ct);
                if (screen != null && !IsProgress(screen)) return null;
            }
            return screen;
        });

    /// <summary>The row after the first label row (rows 1 to 11) that <paramref name="isLabel"/> accepts; null when none.</summary>
    private static string? DataAfter(Bbs757CduScreen s, Func<string, bool> isLabel)
    {
        for (int i = 1; i + 1 < s.Lines.Count && i + 1 < Bbs757CduFrame.ScratchpadRow; i++)
            if (isLabel(s.Lines[i] ?? "")) return s.Lines[i + 1] ?? "";
        return null;
    }

    private static string Miles(int n) => n == 1 ? "1 mile" : string.Create(CultureInfo.InvariantCulture, $"{n} miles");

    private static string Clock(string hhmm) => $"{hhmm[..2]}:{hhmm[2..]}";
}
