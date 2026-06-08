// GsxMenuClassifier — classifies one GSX MenuOption into a GsxMenuEntryKind.
//
// This is the ONE tunable surface for the gate-selection DFS. All literal
// pattern arrays are at the top of the class, each marked "// TUNE LIVE" or
// "// CONFIRMED LIVE" so a live GSX session can update them against real menu
// text (Task E). Patterns confirmed at OMDB/Dubai Intl are marked CONFIRMED LIVE.
//
// Classification is CONSERVATIVE by design:
//   • On a final action menu, only a positive safe-servicing match is Action.
//     A warp/teleport/follow/reposition match is also Action but is flagged
//     forbidden — callers MUST check IsForbidden before choosing any Action.
//   • Anything not positively classified on a final action menu is Unknown
//     and must never be chosen.
//   • On navigation menus, unrecognised non-gate entries become Category
//     (drillable) by default, on the assumption that an unrecognised entry
//     is a sub-group header rather than a dangerous action. This is safe
//     because the DFS will only back-track through Category entries, not
//     execute them; actual chosen leaves are always GateLeaf.
//   • IGNORE entries (select from map, search) are classified Unknown so the
//     DFS never drills them — they are special in-sim functions.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;   // MenuOption

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// The kind of a classified GSX menu entry.
/// </summary>
public enum GsxMenuEntryKind
{
    /// <summary>A parking leaf — text identifies a specific stand (letter+number+suffix).</summary>
    GateLeaf,
    /// <summary>A drillable sub-group (terminal, concourse, area, etc.).</summary>
    Category,
    /// <summary>A "next page" / "more" pagination entry.</summary>
    Pagination,
    /// <summary>A "back" / "return" / "previous" navigation entry.</summary>
    Back,
    /// <summary>
    /// A final-menu action (servicing, WARP, Follow-Me, etc.).
    /// Call <see cref="GsxMenuClassifier.IsForbiddenAction"/> before choosing;
    /// only choose if <see cref="GsxMenuClassifier.IsSafeServicingAction"/> is true.
    /// </summary>
    Action,
    /// <summary>Classification was inconclusive — must never be chosen.</summary>
    Unknown,
}

/// <summary>
/// Classifies <see cref="GsxService.MenuOption"/> entries for the gate-selection DFS.
/// Pure / stateless — no I/O, no sim interaction.
/// </summary>
public static class GsxMenuClassifier
{
    // ─────────────────────────────────────────────────────────────────────────
    // TUNABLE PATTERN SETS — review + update in a live GSX session (Task E).
    // All comparisons use OrdinalIgnoreCase unless noted.
    // Entries marked CONFIRMED LIVE were observed at OMDB/Dubai Intl (2026-06-08).
    // ─────────────────────────────────────────────────────────────────────────

    // CONFIRMED LIVE: these are special in-sim functions — NEVER drill them.
    // Observed at OMDB: "Select from Map" (choice 0), "Search parking..." (choice 1).
    // Classified as Unknown so the DFS skips them entirely.
    private static readonly string[] IgnorePatterns =
    {
        "select from map",
        "select from",   // broad: covers variants like "Select from list"
        "search parking",
        "search",        // broad: covers "Search parking..." and similar
    };

    // CONFIRMED LIVE: pagination / next-page indicators.
    // "Next Page ▶" confirmed at OMDB (separating apron groups across pages).
    private static readonly string[] PaginationPatterns =
    {
        "next page",   // CONFIRMED LIVE: "Next Page ▶"
        "next", "more", ">>", "▶", "→", "page", "forward",
    };

    // CONFIRMED LIVE: back / return indicators.
    // "↑ Back" confirmed at OMDB sub-menus.
    private static readonly string[] BackPatterns =
    {
        "↑ back",      // CONFIRMED LIVE: "↑ Back"
        "back", "previous", "return", "top", "main", "<<", "◀", "←", "cancel",
    };

    // TUNE LIVE — not yet observed: safe final-menu actions (arms marshaller/VDGS — what we want).
    // Match is substring-based so "Request servicing" and "Servicing" both hit.
    // These will be confirmed on the first real arrival run via gsx-gate-select.log.
    private static readonly string[] SafeServicingPatterns =
    {
        "servic",      // "Select for servicing", "Servicing"
        "marshall",    // "Call Marshaller", "Marshaller"
        "proceed",     // "Proceed to gate"
        "request",     // "Request service"
        "dock",        // "Dock"
        "assist",      // "Parking assist"
        "park",        // "Park here", "Parking"  — conservative but covers many airports
    };

    // CONFIRMED LIVE: forbidden actions — NEVER choose these under any circumstances.
    // "Reposition Aircraft" confirmed at OMDB departure menu.
    // Any entry containing any of these substrings is Action+Forbidden.
    private static readonly string[] ForbiddenPatterns =
    {
        "reposition",  // CONFIRMED LIVE: "Reposition Aircraft"
        "warp",
        "teleport",
        "follow",      // "Follow Me" / "Follow-Me service"
        "move to",
        "relocate",
    };

    // TUNE LIVE: known parking-selection entry labels (top-level drill-in targets).
    // Prefer "change parking" over "select parking" when both appear.
    // NOTE: at OMDB arrival (SetGateName == -1) the top menu IS the position selector
    // (apron categories appear directly) — no drill-in entry exists in that case.
    // These patterns are used only when the context is parked/departure.
    internal static readonly string[] ChangeParkingPatterns =
    {
        "change parking",
        "change gate",
        "change stand",
    };

    internal static readonly string[] SelectParkingPatterns =
    {
        "select parking",
        "select gate",
        "select stand",
        "parking",     // broad fallback
    };

    // Position-selection menu title patterns (confirmed live at OMDB).
    // "Select Position at OMDB/Dubai Intl" — title of the arrival gate-selection menu.
    private static readonly string[] PositionSelectionTitlePatterns =
    {
        "select position",
        "select parking",
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Gate-leaf identity parsing.
    // ─────────────────────────────────────────────────────────────────────────

    // Matches the text of a stand / gate leaf.
    // Accepted shapes (mirrors GsxProfileParser.TryParseSectionHeader + real menu observations):
    //   "C18"      → concourse C, number 18
    //   "C 18"     → concourse C, number 18
    //   "C 18 L"   → concourse C, number 18, suffix L
    //   "C18L"     → concourse C, number 18, suffix L (glued)
    //   "218"      → pure number (no concourse)
    //   "218L"     → number 218, suffix L
    //   "1 A"      → number 1, suffix A
    // The normalised identity is LETTERS + NUMBER + SUFFIX, all upper, no spaces.
    // e.g. "C 18 L" → "C18L"; "218" → "218"; "C18" → "C18"

    // Regex: optional letters-prefix, required digits, optional letter-suffix.
    // The letters-prefix and digits must not be empty at the same time.
    private static readonly Regex GateLeafRegex = new Regex(
        @"^(?:(?<concourse>[A-Za-z]{1,4})\s*)?(?<number>\d{1,4})(?:\s*(?<suffix>[A-Za-z]))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A concourse-only token (single letter, used in menus like "Concourse C" or just "C").
    // Only treated as a Category, never a leaf.
    private static readonly Regex PureConcourseRegex = new Regex(
        @"^[A-Za-z]{1,2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ─────────────────────────────────────────────────────────────────────────
    // Primary API.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies a single GSX <see cref="GsxService.MenuOption"/>.
    /// </summary>
    /// <param name="opt">The option to classify.</param>
    /// <param name="onFinalActionMenu">
    /// Pass <see langword="true"/> when the current menu is the post-gate-choice
    /// final action menu (servicing / WARP / Follow-Me options).  On this menu
    /// the classifier ONLY returns <see cref="GsxMenuEntryKind.Action"/> or
    /// <see cref="GsxMenuEntryKind.Unknown"/> — never GateLeaf / Category.
    /// </param>
    public static GsxMenuEntryKind Classify(GsxService.MenuOption opt, bool onFinalActionMenu)
    {
        string text = (opt.Text ?? string.Empty).Trim();

        // ── Final action menu ──────────────────────────────────────────────
        if (onFinalActionMenu)
        {
            // Forbidden check first — these are also Action but must never be chosen.
            if (IsForbiddenAction(text)) return GsxMenuEntryKind.Action;
            if (IsSafeServicingAction(text)) return GsxMenuEntryKind.Action;
            // Back/nav entries can appear even on the final menu.
            if (IsBack(text)) return GsxMenuEntryKind.Back;
            // Everything else on a final menu is Unknown — never chosen.
            return GsxMenuEntryKind.Unknown;
        }

        // ── Navigation menu ────────────────────────────────────────────────

        // CONFIRMED LIVE: ignore special in-sim functions (map picker, search).
        // These must never be drilled — classify Unknown so the DFS skips them.
        if (IsIgnored(text)) return GsxMenuEntryKind.Unknown;

        // Pagination before back (both are nav helpers).
        if (IsNext(text)) return GsxMenuEntryKind.Pagination;
        if (IsBack(text)) return GsxMenuEntryKind.Back;

        // Forbidden-action patterns win over gate-leaf parsing so we never
        // accidentally "drill" into a WARP/reposition entry.
        if (IsForbiddenAction(text)) return GsxMenuEntryKind.Action; // forbidden on nav menu too

        // Gate leaf?
        if (LooksLikeGate(text, out _)) return GsxMenuEntryKind.GateLeaf;

        // Everything else is treated as Category (drillable group).
        // This covers: "Apron A (8 suitable parkings)", "Concourse C", "Terminal 3",
        // "Gates 100-150", etc. — all unrecognised non-gate entries are drillable.
        return GsxMenuEntryKind.Category;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper predicates — public so the DFS selector can use them directly.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to parse a menu entry text as a gate/stand leaf identity.
    /// Returns <see langword="true"/> and sets <paramref name="normalizedIdentity"/>
    /// (e.g. "C18L", "218", "B36") when the text matches.
    /// </summary>
    public static bool LooksLikeGate(string text, out string normalizedIdentity)
    {
        normalizedIdentity = string.Empty;
        string t = text.Trim();
        if (string.IsNullOrEmpty(t)) return false;

        // Pure concourse-only tokens ("A", "B", "T1", "T2") are categories, not leaves.
        // Gate leaves always have a number component.
        var m = GateLeafRegex.Match(t);
        if (!m.Success) return false;

        string concourse = m.Groups["concourse"].Value.ToUpperInvariant();
        string number    = m.Groups["number"].Value;
        string suffix    = m.Groups["suffix"].Value.ToUpperInvariant();

        // A pure single-letter-without-number token caught by GateLeafRegex
        // would appear as an empty number — guard against it (regex requires \d+,
        // so this should never happen, but be explicit).
        if (string.IsNullOrEmpty(number)) return false;

        // Build normalised identity: CONCOURSE + NUMBER + SUFFIX (no spaces).
        normalizedIdentity = concourse + number + suffix;
        return true;
    }

    /// <summary>
    /// Returns the normalised identity for matching against a target
    /// <see cref="MSFSBlindAssist.Database.Models.ParkingSpot"/>:
    /// concourse-letter(s) + number + suffix, all upper, no spaces.
    /// </summary>
    public static string NormalizeTargetIdentity(
        string? name, int number, string? suffix)
    {
        // Mirror GateSearchFilter.NormalizeIdentity convention.
        string n = (name ?? string.Empty).ToUpperInvariant().Replace(" ", "");
        // Strip single leading 'G' from multi-letter all-letter concourse names
        // (navdata "GC" → "C", "GA" → "A") — same as GsxParkingNameEnum.ExtractConcourseLetter.
        if (n.Length > 1
            && n[0] == 'G'
            && IsAllLetters(n))
        {
            n = n.Substring(1);
        }
        string num = number > 0 ? number.ToString() : string.Empty;
        string suf = (suffix ?? string.Empty).ToUpperInvariant().Trim();
        return n + num + suf;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the entry text is a special in-sim
    /// function that must NEVER be drilled by the DFS
    /// (e.g. "Select from Map", "Search parking...").
    /// CONFIRMED LIVE at OMDB: choice 0 = "Select from Map", choice 1 = "Search parking...".
    /// </summary>
    public static bool IsIgnored(string text)
        => ContainsAny(text, IgnorePatterns);

    /// <summary>
    /// Returns <see langword="true"/> when a menu title indicates that this menu
    /// IS the position-selection root (arrival context, un-parked).
    /// CONFIRMED LIVE at OMDB: title = "Select Position at OMDB/Dubai Intl".
    /// </summary>
    public static bool IsPositionSelectionMenu(string title)
        => ContainsAny(title, PositionSelectionTitlePatterns);

    /// <summary>
    /// Returns <see langword="true"/> when the entry text matches a
    /// pagination / "next page" pattern.
    /// CONFIRMED LIVE: "Next Page ▶" observed at OMDB.
    /// </summary>
    public static bool IsNext(string text)
        => ContainsAny(text, PaginationPatterns);

    /// <summary>
    /// Returns <see langword="true"/> when the entry text matches a
    /// back / return / previous navigation pattern.
    /// CONFIRMED LIVE: "↑ Back" observed at OMDB sub-menus.
    /// </summary>
    public static bool IsBack(string text)
        => ContainsAny(text, BackPatterns);

    /// <summary>
    /// Returns <see langword="true"/> when the entry text matches a
    /// forbidden action pattern (WARP, Follow-Me, reposition, etc.).
    /// Entries matching this must NEVER be chosen.
    /// CONFIRMED LIVE: "Reposition Aircraft" observed at OMDB departure menu.
    /// </summary>
    public static bool IsForbiddenAction(string text)
        => ContainsAny(text, ForbiddenPatterns);

    /// <summary>
    /// Returns <see langword="true"/> when the entry text matches a
    /// positively-identified safe servicing action (marshaller, dock, etc.).
    /// On the final action menu, ONLY these may be chosen.
    /// </summary>
    public static bool IsSafeServicingAction(string text)
        => ContainsAny(text, SafeServicingPatterns);

    // ─────────────────────────────────────────────────────────────────────────
    // Parking-selection entry finders (used by GsxGateSelector to locate the
    // "Change Parking facility" / "Select Parking" drill-in entry at the top).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the entry is the "Change Parking"
    /// drill-in (preferred over SelectParking when both are present).
    /// </summary>
    public static bool IsChangeParkingEntry(string text)
        => ContainsAny(text, ChangeParkingPatterns);

    /// <summary>
    /// Returns <see langword="true"/> when the entry is a "Select Parking"
    /// drill-in (fallback if no Change Parking entry exists).
    /// </summary>
    public static bool IsSelectParkingEntry(string text)
        => ContainsAny(text, SelectParkingPatterns);

    // ─────────────────────────────────────────────────────────────────────────
    // Category relevance ranking (used by GsxGateSelector DFS to drill
    // best-first when multiple Category entries exist on a menu).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scores how relevant a Category entry is to the given target identity
    /// components.  Higher = more relevant (drill here first).
    /// </summary>
    /// <param name="categoryText">
    /// The category entry text (e.g. "Concourse C", "Apron C\t(33 suitable parkings)").
    /// </param>
    /// <param name="targetConcourse">The normalised concourse prefix from the target (may be empty).</param>
    /// <param name="targetNumber">The gate number of the target.</param>
    /// <remarks>
    /// CONFIRMED LIVE at OMDB: category labels look like "Apron C\t(33 suitable parkings)".
    /// The concourse letter ("C") appears as a standalone whitespace-delimited token.
    /// Whole-token matching (not substring) prevents "B" matching "Back" etc.
    /// </remarks>
    public static int RankCategoryRelevance(
        string categoryText,
        string targetConcourse,
        int targetNumber)
    {
        string t = categoryText.ToUpperInvariant();
        int score = 0;

        // Concourse letter match — use whole-token comparison so a single-letter
        // concourse (e.g. "C" in "Apron C\t(33 suitable parkings)") scores high
        // and doesn't accidentally match substrings inside words.
        // CONFIRMED LIVE at OMDB: apron labels tokenise to ["APRON", "C", "(33", "SUITABLE", "PARKINGS)"].
        if (!string.IsNullOrEmpty(targetConcourse))
        {
            // Split on any non-letter, non-digit character to get tokens.
            string[] tokens = Regex.Split(t, @"[^A-Z0-9]+");
            string concourseUpper = targetConcourse.ToUpperInvariant();
            foreach (string token in tokens)
            {
                if (token.Length == 0) continue;
                if (string.Equals(token, concourseUpper, StringComparison.Ordinal))
                {
                    score += 10; // exact whole-token match
                    break;
                }
                // Fallback: the token starts with the concourse prefix (handles multi-letter).
                if (token.StartsWith(concourseUpper, StringComparison.Ordinal) && token.Length <= concourseUpper.Length + 1)
                {
                    score += 6;
                    break;
                }
            }

            // Substring fallback for labels like "CONCOURSE C" where the letter
            // might be glued — only award partial score to avoid false-positives.
            if (score == 0 && t.Contains(concourseUpper, StringComparison.Ordinal))
                score += 4;
        }

        // Number-range hint (e.g. "Gates 100-150" and target is 120).
        if (targetNumber > 0)
        {
            // Try to extract a numeric range from the category label.
            var rangeMatch = Regex.Match(t, @"(\d+)\s*[-–]\s*(\d+)");
            if (rangeMatch.Success
                && int.TryParse(rangeMatch.Groups[1].Value, out int lo)
                && int.TryParse(rangeMatch.Groups[2].Value, out int hi)
                && targetNumber >= lo && targetNumber <= hi)
            {
                score += 5;
            }

            // Single number hint (e.g. "Terminal 3" and gate number starts with 3xx).
            var numMatch = Regex.Match(t, @"(\d+)");
            if (numMatch.Success
                && int.TryParse(numMatch.Groups[1].Value, out int hint))
            {
                // Heuristic: if the hint matches the hundreds digit of the target gate.
                int hundreds = targetNumber / 100;
                if (hundreds > 0 && hint == hundreds) score += 3;
                // Or if the hint matches the target number exactly.
                if (hint == targetNumber) score += 4;
            }
        }

        return score;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers.
    // ─────────────────────────────────────────────────────────────────────────

    private static bool ContainsAny(string text, string[] patterns)
    {
        foreach (string p in patterns)
            if (text.Contains(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool IsAllLetters(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (!char.IsLetter(c)) return false;
        return true;
    }
}
