using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A220;

/// <summary>
/// Groups the A220 ROUTE ▸ LEGS page scrape into ONE row per flight-plan leg.
///
/// Why this exists: the generic <see cref="A220FmsScreenParsing.ParseFms"/> pass
/// promotes every unclaimed white token to its own "button" row, so a single leg
/// read as four meaningless fragments ("266° 29.9", "GENOS", "2.00", "/-----").
/// The A380 MCDU form's rule is one MEANINGFUL element per line; this is the A220
/// equivalent for the one page where the layout is a repeating table.
///
/// Layout (measured live 2026-07-29, LCLK→LGAV, ROUTE ▸ SEC LEGS):
///   • Each leg occupies TWO text rows, ~48 px apart:
///       y      : track+distance at x≈10 ("266° 29.9", "R015° 1.0"), and the
///                RNP label at x≈578 with its value at x≈619.
///       y + 29 : the waypoint name at x≈17, and the speed/altitude constraint
///                (green) at x≈303 (set) or x≈365 (empty "/-----").
///   • The FIRST leg after the origin, and any leg following a discontinuity, has
///     NO track row — the track/distance is optional, never assumed present.
///   • A discontinuity is the triple THEN / ▯▯▯▯▯ / DISCONTINUITY.
///   • "HOLD AT" replaces the track row for a hold; "MISSED APPROACH" is a gray
///     section header; the origin runway (cyan, e.g. "RW22") opens the list.
///   • Pinned chrome (→…, HOLD…, FIX…, THRUST…, MSG…, ACTIVATE SEC, OFFSET and
///     the UTC/DTG/ETE/ETA/FUEL column headers) is drawn OVER the scrolling list,
///     so its y values interleave with the legs — it is excluded by identity, not
///     by position, or it lands in the middle of the route.
///
/// Pinned by A220FmsLegParsingTests against the live fixture.
/// </summary>
internal static class A220FmsLegParsing
{
    /// <summary>Track+distance row, e.g. "266° 29.9" or the radial form "R015° 1.0".</summary>
    private static readonly Regex TrackRow =
        new(@"^(R?)(\d{1,3})°\s*(\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    /// <summary>Column headers and pinned soft keys — never list content. Anything
    /// ending in "…" is a soft key in this UI and is excluded generically.</summary>
    private static readonly HashSet<string> Chrome = new(StringComparer.Ordinal)
    {
        "UTC", "SPD/ALT VPA+RNP", "RNP AUTO", "DTG", "ETE", "ETA", "FUEL (LB)",
        "FUEL (KG)", "OFFSET", "RNP", "DEST", "ALTN", "ACTIVATE SEC", "THEN",
        "FMS1", "FMS2", "ACT", "MOD", "SEC", "EXEC",
    };

    /// <summary>The window header band (side, mode, page tiles, sub-tabs, column
    /// headings) sits above this. The table's first row — the origin runway — was
    /// measured at y≈264, and the column headings at y≈157, so nothing the leg
    /// grouper wants lives above it. Without this guard the "FMS1" header (x≈8,
    /// left column like a waypoint) parses as the first leg.</summary>
    private const double TableTopY = 200;

    internal enum LegKind { Leg, Discontinuity, Header, Hold, Origin }

    internal sealed class FmsLeg
    {
        public LegKind Kind = LegKind.Leg;
        /// <summary>Waypoint identifier — also the click target text.</summary>
        public string Waypoint = "";
        /// <summary>0-based occurrence of <see cref="Waypoint"/> in document order.
        /// A waypoint legitimately repeats (KEA appears four times on this route:
        /// STAR end, transition, missed approach, hold), so a click MUST carry the
        /// occurrence or it always actions the first one.</summary>
        public int Occurrence;
        public string Track = "";        // "266" / "R015" (radial) / ""
        public string Distance = "";     // "29.9" / ""
        public string Rnp = "";          // "2.00" / ""
        public string Constraint = "";   // raw, e.g. "↓/6000A" / ""
        /// <summary>The prediction column on the waypoint row (x≈170): an ETA
        /// "15:26" in UTC mode, a fuel figure in FUEL mode. Which one is the
        /// column header's choice (<see cref="LegsPage.PredictionIsFuel"/>).</summary>
        public string Prediction = "";
        public string Dtg = "";          // "6.8" — distance to go, drawn on a pseudo-waypoint's (TOC/TOD) track row
        public string Xtk = "";          // "L0.63" — cross-track error, on the ACTIVE leg's track row
        public string Epu = "";          // "0.02" — estimated position uncertainty, same row
        public double Y;
        /// <summary>0-based index among the page's discontinuities (Discontinuity
        /// rows only, else -1) — the argument the agent's deleteDiscontinuityClick needs
        /// to target the right one when a route has several.</summary>
        public int DiscoIndex = -1;
    }

    /// <summary>True when this scrape is the LEGS table (at least two track rows).</summary>
    internal static bool IsLegsPage(IReadOnlyList<A220FmsScreenParsing.WinToken> tokens)
        => tokens.Count(t => TrackRow.IsMatch(t.Text.Trim())) >= 2;

    /// <summary>True when the LEGS prediction column is showing FUEL rather than
    /// UTC (the "UTC"/"FUEL" chooser at y≈157 above the table).</summary>
    internal static bool PredictionIsFuel(IReadOnlyList<A220FmsScreenParsing.WinToken> tokens)
        => tokens.Any(t => t.Y < TableTopY && t.X is > 150 and < 260
                           && t.Text.Trim().StartsWith("FUEL", StringComparison.Ordinal));

    private const double PairDy = 29;      // waypoint row sits this far below its track row
    private const double PairTolY = 12;
    private const double RowTolY = 8;

    /// <summary>
    /// Group the scrape into legs. <paramref name="consumed"/> receives the index of
    /// every token a leg row absorbed, so the generic pass can skip them instead of
    /// re-emitting each fragment as its own button/orphan row — which is exactly the
    /// noise this class exists to remove.
    /// </summary>
    internal static List<FmsLeg> ParseLegs(
        IReadOnlyList<A220FmsScreenParsing.WinToken> tokens, out HashSet<int> consumed)
    {
        var legs = new List<FmsLeg>();
        consumed = new HashSet<int>();
        var occurrence = new Dictionary<string, int>(StringComparer.Ordinal);

        // Left-column candidates, top to bottom. x≤45 covers both the track row
        // (x≈10) and the waypoint row (x≈17); "DISCONTINUITY" (x≈315) is matched
        // separately because it shares its row with the ▯ placeholder.
        var order = Enumerable.Range(0, tokens.Count)
            .OrderBy(i => tokens[i].Y).ThenBy(i => tokens[i].X).ToList();
        int discCount = 0;

        foreach (int i in order)
        {
            var t = tokens[i];
            string text = t.Text.Trim();
            if (text.Length == 0) continue;

            if (text == "DISCONTINUITY")
            {
                // ONE row per marker for the whole THEN/▯/DISCONTINUITY triple.
                // There is exactly one DISCONTINUITY token per discontinuity (four
                // markers for the four gaps in the live LCLK→LGAV capture), so this
                // must NOT be de-duplicated against the previous row — two
                // discontinuities can legitimately be adjacent with no waypoint
                // between them, and collapsing them loses one and mis-numbers the
                // rest, which would delete the wrong gap.
                legs.Add(new FmsLeg
                {
                    Kind = LegKind.Discontinuity, Y = t.Y, DiscoIndex = discCount++
                });
                consumed.Add(i);
                for (int j = 0; j < tokens.Count; j++)
                {
                    string jt = tokens[j].Text.Trim();
                    if (jt == "THEN" && Math.Abs(tokens[j].Y - (t.Y - PairDy)) <= PairTolY) consumed.Add(j);
                    else if (jt.Length > 0 && jt.Trim('▯', '□').Length == 0
                             && Math.Abs(tokens[j].Y - t.Y) <= PairTolY) consumed.Add(j);
                }
                continue;
            }
            if (text == "MISSED APPROACH")
            {
                legs.Add(new FmsLeg { Kind = LegKind.Header, Waypoint = text, Y = t.Y });
                consumed.Add(i);
                continue;
            }
            // The cyan "(DIR)" annotation above an active direct-to leg (live
            // capture 2026-07-30). It is a label, not a waypoint: it has no
            // fix-symbol icon, so it can never open a revision menu — an
            // actionable row here would just say "no revision menu" on Enter.
            if (text == "(DIR)")
            {
                legs.Add(new FmsLeg { Kind = LegKind.Header, Waypoint = "Direct to", Y = t.Y });
                consumed.Add(i);
                continue;
            }

            if (t.X > 45) continue;                       // right-hand columns handled below
            if (t.Y < TableTopY) continue;                // window header band
            if (Chrome.Contains(text)) continue;
            if (text.EndsWith("…", StringComparison.Ordinal)) continue;
            if (t.Color == "gray") continue;              // labels, not list content
            if (TrackRow.IsMatch(text)) continue;         // consumed by its waypoint row
            if (text == "HOLD AT") continue;              // consumed by its waypoint row
            if (text.Trim('▯', '□', '-', '.').Length == 0) continue;   // placeholder / empty rows

            // A waypoint row. Pull its own constraint, and the track/RNP from the
            // row ~29 px ABOVE (absent for the first leg and after a discontinuity).
            var leg = new FmsLeg { Waypoint = text, Y = t.Y };
            int occ = occurrence.TryGetValue(text, out int o) ? o : 0;
            occurrence[text] = occ + 1;
            leg.Occurrence = occ;
            consumed.Add(i);

            if (t.Color == "cyan" && legs.Count == 0) leg.Kind = LegKind.Origin;

            // The prediction column (ETA/fuel) at x≈170, 3 px below the ident.
            // Measured live 2026-09-24 (EGLL→LGAV ACT LEGS); unclaimed, every
            // time read as a loose row under the route with no waypoint to it.
            for (int j = 0; j < tokens.Count; j++)
            {
                var g = tokens[j];
                if (Math.Abs(g.Y - t.Y) > RowTolY) continue;
                if (g.X < 140 || g.X >= 250 || g.Color == "gray") continue;
                leg.Prediction = g.Text.Trim();
                consumed.Add(j);
                break;
            }

            for (int j = 0; j < tokens.Count; j++)
            {
                var g = tokens[j];
                if (Math.Abs(g.Y - t.Y) > RowTolY) continue;
                if (g.X < 250 || g.X > 460) continue;
                if (g.Color != "green") continue;
                consumed.Add(j);
                string c = g.Text.Trim();
                if (c.Trim('/', '-', ' ').Length == 0) break;   // "/-----" = no constraint
                leg.Constraint = c;
                break;
            }

            double trackY = t.Y - PairDy;
            for (int j = 0; j < tokens.Count; j++)
            {
                var g = tokens[j];
                if (Math.Abs(g.Y - trackY) > PairTolY) continue;
                string gt = g.Text.Trim();
                if (g.X <= 45)
                {
                    var m = TrackRow.Match(gt);
                    if (m.Success)
                    {
                        leg.Track = m.Groups[1].Value + m.Groups[2].Value;
                        leg.Distance = m.Groups[3].Value;
                        consumed.Add(j);
                    }
                    else if (gt == "HOLD AT") { leg.Kind = LegKind.Hold; consumed.Add(j); }
                }
                else if (g.X < 560)
                {
                    // Labelled values between the track and RNP columns: "DTG 6.8"
                    // on a pseudo-waypoint (TOC/TOD) and "XTK L0.63  EPU 0.02" on
                    // the active leg. Each gray label owns the nearest value to its
                    // right on the same row.
                    if (g.Color == "gray")
                    {
                        if (gt is "DTG" or "XTK" or "EPU") consumed.Add(j);
                        continue;
                    }
                    string? owner = null;
                    double best = double.MaxValue;
                    for (int k = 0; k < tokens.Count; k++)
                    {
                        var lab = tokens[k];
                        if (lab.Color != "gray" || Math.Abs(lab.Y - g.Y) > RowTolY) continue;
                        string lt = lab.Text.Trim();
                        if (lt is not ("DTG" or "XTK" or "EPU")) continue;
                        double dx = g.X - lab.X;
                        if (dx <= 0 || dx > 80 || dx >= best) continue;
                        best = dx; owner = lt;
                    }
                    if (owner == "DTG") { leg.Dtg = gt; consumed.Add(j); }
                    else if (owner == "XTK") { leg.Xtk = gt; consumed.Add(j); }
                    else if (owner == "EPU") { leg.Epu = gt; consumed.Add(j); }
                }
                else
                {
                    // The per-leg "RNP" gray label and its value.
                    if (gt == "RNP") consumed.Add(j);
                    else if (g.Color != "gray") { leg.Rnp = gt; consumed.Add(j); }
                }
            }

            legs.Add(leg);
        }

        return legs;
    }

    /// <summary>One row of the agent's fiber walk (`legs()`): the aircraft's own
    /// leg index plus the row's ident text — empty for a ▯-placeholder
    /// (discontinuity) slot.</summary>
    internal readonly record struct FiberLegRow(int Idx, string Id);

    /// <summary>
    /// Pair each grouped leg row with the aircraft's own leg index by CONTENT,
    /// never by position. Returns an array parallel to <paramref name="legs"/>;
    /// -1 where no confident pairing exists (headers, and any row that could not
    /// be matched — those degrade to text addressing / an honest refusal).
    ///
    /// Why: the two sequences are built independently and do NOT line up
    /// one-for-one. The fiber walk omits rows the grouper shows (the origin
    /// airport, the runway leg) and the grouper filters tokens the walk keeps, so
    /// a positional zip shifted every leg index by two on a live plan
    /// (2026-07-30 evening: the discontinuity between the duplicate BPKs was
    /// addressed as TOTRI — WRONG_ROW refusals on every delete, and duplicate
    /// idents could act on the WRONG twin, which no ident guard can catch).
    ///
    /// The walk is greedy and monotonic: each grouped row scans a short window
    /// ahead in the fiber list (skipping fiber-only rows), matching waypoint
    /// rows by exact ident and discontinuities by the empty/placeholder ident.
    /// A grouped row with no match inside the window gets -1 and does NOT
    /// advance the cursor, so one unmatched row cannot desync the rest.
    /// Duplicate idents pair in order (first BPK ↔ first fiber BPK), which is
    /// what makes twin waypoints safe to click.
    /// </summary>
    internal static int[] AlignLegIndices(
        IReadOnlyList<FmsLeg> legs, IReadOnlyList<FiberLegRow> fiber)
    {
        // How many consecutive fiber-only rows (annotation rows whose tokens the
        // grouper filtered) a single pairing may skip. Small on purpose: a wide
        // window lets a repeated ident later in the plan capture the cursor and
        // desync everything after it.
        const int Lookahead = 3;

        var result = new int[legs.Count];
        int cursor = 0;
        for (int i = 0; i < legs.Count; i++)
        {
            result[i] = -1;
            var leg = legs[i];
            if (leg.Kind == LegKind.Header) continue;
            bool wantPlaceholder = leg.Kind == LegKind.Discontinuity;
            int limit = Math.Min(fiber.Count - 1, cursor + Lookahead);
            for (int j = cursor; j <= limit; j++)
            {
                bool match = wantPlaceholder
                    ? fiber[j].Id.Length == 0
                    : fiber[j].Id.Length > 0
                      && string.Equals(fiber[j].Id, leg.Waypoint, StringComparison.Ordinal);
                if (!match) continue;
                result[i] = fiber[j].Idx;
                cursor = j + 1;
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// Speech form of a raw Fusion constraint. "↓/6000A" → "descend, 6000 or above";
    /// "↑220/5000A" → "climb, 220 knots, 5000 or above"; "/-----" → "".
    /// The A/B suffixes are standard ARINC at-or-above / at-or-below markers; a bare
    /// number is a hard altitude. Nothing is invented — an unrecognised shape falls
    /// through as its own text rather than being dropped.
    /// </summary>
    /// <summary>"L0.63" → "0.63 left" (miles are implied by the column).</summary>
    private static string SpokenXtk(string raw)
    {
        if (raw.Length > 1 && raw[0] is 'L' or 'R')
            return $"{raw[1..]} {(raw[0] == 'L' ? "left" : "right")}";
        return raw;
    }

    internal static string FormatConstraint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string s = raw.Trim();
        var parts = new List<string>();

        if (s.StartsWith("↓", StringComparison.Ordinal)) { parts.Add("descend"); s = s[1..]; }
        else if (s.StartsWith("↑", StringComparison.Ordinal)) { parts.Add("climb"); s = s[1..]; }

        int slash = s.IndexOf('/');
        string speed = slash >= 0 ? s[..slash].Trim() : "";
        string alt = slash >= 0 ? s[(slash + 1)..].Trim() : s.Trim();

        // "~…~" = the agent saw that part in the display's SMALL type: the FMS's
        // PREDICTION, not a constraint (see the agent's markPredicted). Say so —
        // a predicted value read as a restriction is a wrong instruction.
        static (string Text, bool Predicted) Unwrap(string p)
        {
            bool pred = p.Contains('~');
            return (p.Replace("~", "").Trim(), pred);
        }
        var (sp, spPred) = Unwrap(speed);
        var (al, alPred) = Unwrap(alt);
        if (sp.Trim('-', ' ').Length > 0) parts.Add($"{(spPred ? "predicted " : "")}{sp} knots");
        if (al.Trim('-', ' ').Length > 0)
        {
            string pre = alPred ? "predicted " : "";
            if (al.EndsWith("A", StringComparison.Ordinal)) parts.Add($"{pre}{al[..^1]} or above");
            else if (al.EndsWith("B", StringComparison.Ordinal)) parts.Add($"{pre}{al[..^1]} or below");
            else parts.Add(pre + al);
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// The spoken row for a leg. Essentials first (identifier, then how far and
    /// which way), constraint next, RNP last — so arrowing down the route reads as
    /// a route, not as a table dump. Degrees are spoken as a word because a bare
    /// "266" next to "29.9" is ambiguous; no feet/mile quantity is invented.
    /// </summary>
    internal static string Describe(FmsLeg leg, bool predictionIsFuel = false)
    {
        switch (leg.Kind)
        {
            // Say what can be DONE with it — a blind pilot has no visual cue that
            // this row is actionable, and a bare "discontinuity" reads as a dead end.
            case LegKind.Discontinuity: return "Flight plan discontinuity, Delete to remove";
            case LegKind.Header: return leg.Waypoint;
        }

        var parts = new List<string>
        {
            leg.Kind == LegKind.Hold ? $"Hold at {leg.Waypoint}" : leg.Waypoint
        };
        if (leg.Kind == LegKind.Origin) parts.Add("origin");
        if (leg.Track.Length > 0)
        {
            parts.Add(leg.Track.StartsWith("R", StringComparison.Ordinal)
                ? $"radial {leg.Track[1..]} degrees"
                : $"{leg.Track} degrees");
        }
        if (leg.Distance.Length > 0) parts.Add($"{leg.Distance} miles");
        if (leg.Dtg.Length > 0) parts.Add($"{leg.Dtg} miles to go");
        if (leg.Prediction.Length > 0)
            parts.Add(predictionIsFuel ? $"fuel {leg.Prediction}" : $"ETA {leg.Prediction}");
        string c = FormatConstraint(leg.Constraint);
        if (c.Length > 0) parts.Add(c);
        if (leg.Rnp.Length > 0) parts.Add($"RNP {leg.Rnp}");
        if (leg.Xtk.Length > 0) parts.Add($"cross track {SpokenXtk(leg.Xtk)}");
        if (leg.Epu.Length > 0) parts.Add($"EPU {leg.Epu}");
        return string.Join(", ", parts);
    }
}
