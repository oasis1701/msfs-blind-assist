using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// GSX's per-vehicle ground-crew narration, read out of a service row's
/// <see cref="GsxServiceState.StatusText"/> — "front loader raising belt", "rear stairs in
/// position", "front train on the way".
///
/// <para>
/// The pre-Remote-API transport scraped ONE tooltip string carrying every segment and read the
/// lot aloud (its own regex comment named the shape: "rear loader leaving while 5 boarded").
/// The migration split that string in two — GSX's banner became the <c>message</c> slot, the
/// per-vehicle detail became each row's <c>statusText</c> — and only the banner got an
/// announcer. So since then this text has reached the tooltip and nowhere else. The bus is the
/// exception that hid it: GSX publishes the bus phase twice, once here and once in the
/// dedicated <c>detail.busPhase</c> that <see cref="GsxServiceAnnouncer"/> already speaks.
/// </para>
///
/// <para>
/// Two rules, both taken from the live capture
/// "rear stairs in position / front loader raising belt / rear loader raising belt / front
/// train on the way / rear train on the way, ETA 33 secs / bus idle / pax 0/93":
/// </para>
///
/// <para>
/// A QUANTITY line is not narration. <c>pax 0/93</c> and <c>bags 100%</c> are spoken by the
/// typed announcers on their own milestone schedule, and reading them from here as well would
/// say one number twice from two places.
/// </para>
///
/// <para>
/// A line that differs from one already spoken ONLY in a standalone run of digits is a tick,
/// not news — that is <see cref="GsxPhraseGate.IsDigitRunOnlyChange"/>, and it is what stops
/// the embedded "ETA 33 secs" countdown reading once a second. The comparison is against
/// EVERY previously-spoken line rather than a positional predecessor, because GSX reorders and
/// drops lines as vehicles come and go, so position carries no meaning.
/// </para>
///
/// <para>
/// A line that DISAPPEARS is deliberately silent: the vehicle's next real move is what the
/// pilot needs, and announcing departures as well would double the traffic for no new fact.
/// Pure and stateless — the caller keeps the last-spoken set. Pinned by GsxStatusNarrationTests.
/// </para>
/// </summary>
internal static class GsxStatusNarration
{
    // Anchored, so it only ever strips a line that IS a quantity — never one that merely
    // mentions a count ("rear loader leaving while 5 boarded" stays narration, which is the
    // exact case the pre-Remote-API rules called out).
    private static readonly Regex QuantityLine =
        new(@"^\s*(?:pax|passengers?|bags?|baggage)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // GSX publishes the bus TWICE — here AND in the dedicated detail.busPhase that
    // GsxServiceAnnouncer.BusPhrase already speaks ("Board bus approaching."). The captured row
    // proves the overlap: statusText "bus in position" beside busPhase "in position". Reading
    // both would double every bus callout, so the dedicated field keeps ownership and this line
    // is dropped. No other vehicle has a field of its own, which is the whole reason this
    // class exists.
    private static readonly Regex BusLine =
        new(@"^\s*bus\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // A comma NOT sitting inside a number: "fuel 4,801 lb" is ONE clause, and splitting it
    // would leave "801 lb" to re-announce on every tick. The negative lookahead is what keeps
    // a thousands separator joined, since GSX writes those with no following space.
    private static readonly Regex ClauseSeparator =
        new(@",\s*(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The narration lines of one row's status block, in GSX's own order, with the
    /// quantity lines the typed announcers own removed.</summary>
    public static IReadOnlyList<string> VehicleLines(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText)) return Array.Empty<string>();

        var lines = new List<string>();
        foreach (string line in statusText.ReplaceLineEndings("\n")
                                          .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Split on COMMAS as well as newlines. GSX publishes the vehicle block as one
            // comma-separated line, so newlines alone left it whole and any single vehicle
            // moving re-read the lot: measured on a live turnaround, 55 of 83 spoken clauses
            // (66 %) were clauses already said. The captured fixtures hid this because their
            // "bus in position" / "pax 181/186" genuinely are newline-separated, which made
            // per-line look like per-vehicle.
            foreach (string raw in ClauseSeparator.Split(line))
            {
                string clause = raw.Trim();
                if (clause.Length == 0) continue;
                if (QuantityLine.IsMatch(clause) || BusLine.IsMatch(clause)) continue;
                lines.Add(clause);
            }
        }
        return lines;
    }

    /// <summary>
    /// The lines in <paramref name="current"/> worth speaking given <paramref name="lastSpoken"/>
    /// — anything neither already said nor a digit-run variant of something already said.
    /// </summary>
    public static IReadOnlyList<string> NewSince(IReadOnlyList<string> current, IReadOnlyList<string> lastSpoken)
    {
        var fresh = new List<string>();
        foreach (string line in current)
        {
            bool known = false;
            foreach (string old in lastSpoken)
            {
                if (GsxPhraseGate.IsDigitRunOnlyChange(old, line)) { known = true; break; }
            }
            if (!known) fresh.Add(line);
        }
        return fresh;
    }
}
