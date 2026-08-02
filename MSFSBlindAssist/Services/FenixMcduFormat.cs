using System.Collections.Generic;
using System.Text;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Decodes Fenix A320 MCDU display markup into accessible plain text.
///
/// The Fenix `aircraft.mcduN.display` dataref emits each line with INLINE single-letter
/// codes: colors `a c g m w y` and font sizes `s` (small) / `l` (large). Both are
/// case-sensitive and lowercase, so uppercase display text ("MINOR", "ALL") is never
/// mistaken for a code.
///
/// Plain text cannot carry color or font size, so the two idioms the unit uses to mark a
/// SELECTED item are re-encoded as a leading '*' on the selected token:
///
///   1. Mixed-color lines mark the GREEN segment (the original rule, unchanged).
///   2. Slash-separated option groups mark the LARGE option among small siblings — the
///      standard Airbus "selected option is large, the others are small" convention.
///
/// Rule 2 exists because the Fenix marks toggle selections with cyan + large font, NOT
/// green, so rule 1 alone left pages like CONFIG > FAILURES with no accessible indication
/// of the current setting at all. Broadening rule 1 to "green or cyan" is NOT a valid fix:
/// cyan is used throughout the MCDU for entry fields, brackets and the leading cycle arrow,
/// so it would emit an asterisk on nearly every line.
/// </summary>
public static class FenixMcduFormat
{
    /// <summary>
    /// Display glyph -> accessible glyph. Written as \uXXXX escapes ON PURPOSE, not as
    /// literals: the KEYS are Latin-1 supplement characters matched against live dataref
    /// text, so if this file were ever re-saved as CP1252 the literals would mangle and
    /// arrow decoding would break silently at RUNTIME, with no compile error. Escapes are
    /// encoding-proof. (The file is UTF-8 without a BOM, matching the rest of the repo.)
    /// </summary>
    private static readonly Dictionary<char, char> SpecialChars = new()
    {
        { '#', '-' },  // box -> hyphen (better for Braille displays)
        { '&', '\u0394' },  // delta
        { '\u00A4', '\u2191' }, // ¤ -> up arrow
        { '\u00A5', '\u2193' }, // ¥ -> down arrow
        { '\u00A2', '\u2192' }, // ¢ -> right arrow
        { '\u00A3', '\u2190' }, // £ -> left arrow
    };

    private static readonly HashSet<char> ColorCodeSet = new() { 'a', 'c', 'g', 'm', 'w', 'y' };
    private static readonly HashSet<char> SizeCodeSet = new() { 's', 'l' };

    private readonly record struct Segment(char Color, string Text);

    /// <summary>
    /// One decode pass: the plain text with all codes removed and specials mapped, the
    /// color segmentation (split on every color code, exactly as the original did), and a
    /// per-character "is this glyph large?" flag aligned index-for-index with the text.
    /// </summary>
    private sealed class Decoded
    {
        public string Text = "";
        public List<Segment> Segments = new();
        public bool[] Large = Array.Empty<bool>();
    }

    private static Decoded Decode(string text)
    {
        var result = new Decoded();
        var segments = new List<Segment>();
        var all = new StringBuilder();
        var large = new List<bool>();

        char currentColor = 'w'; // default white

        // LOAD-BEARING, not a mere fallback: a line starts in large font and only an
        // explicit 's' makes it small. The live "NONE selected" capture
        // (c£wcNONEw/sMINORl/sALLl) carries NO size code anywhere before NONE — NONE reads
        // as large purely from this initial value, which is the only reason that capture
        // decodes to the correct selection. Flip this to false and the NONE case silently
        // stops marking. The cost of the default is that an unsized-but-meant-small first
        // option would be marked as selected ("ABC/sDEF/sGHI" -> "*ABC/DEF/GHI"); the Fenix
        // always emits 's' explicitly to go small, so that shape does not occur in practice.
        bool currentLarge = true;
        var currentText = new StringBuilder();

        foreach (char c in text)
        {
            if (ColorCodeSet.Contains(c))
            {
                // Flush current segment. Note this splits on EVERY color code, even one
                // that re-states the current color — preserved from the original so the
                // green-marker output is byte-identical.
                if (currentText.Length > 0)
                {
                    segments.Add(new Segment(currentColor, currentText.ToString()));
                    currentText.Clear();
                }
                currentColor = c;
                continue;
            }

            if (SizeCodeSet.Contains(c))
            {
                // Size codes are dropped from the text but their effect is retained: the
                // large/small distinction is what identifies a selected option (rule 2).
                currentLarge = c == 'l';
                continue;
            }

            char glyph = SpecialChars.TryGetValue(c, out char replacement) ? replacement : c;
            currentText.Append(glyph);
            all.Append(glyph);
            large.Add(currentLarge);
        }

        if (currentText.Length > 0)
        {
            segments.Add(new Segment(currentColor, currentText.ToString()));
        }

        result.Text = all.ToString();
        result.Segments = segments;
        result.Large = large.ToArray();
        return result;
    }

    /// <summary>
    /// Strips the inline format codes, re-encoding the selected-item highlight as a leading
    /// '*' on the selected token. Returns plain text safe for a screen reader / Braille row.
    /// </summary>
    public static string StripFormatCodes(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var decoded = Decode(text);

        // Marker insertion points, as indices into decoded.Text.
        //
        // ORDER MATTERS: rule 1 runs FIRST and rule 2 then skips any option token rule 1
        // already claimed. The HashSet alone is NOT enough — it only collapses an IDENTICAL
        // index, and the two rules deliberately pick different anchors (rule 1 the green
        // segment's first character, rule 2 the option's first letter/digit), so on e.g.
        // "wsAl/g(B)" they would otherwise emit "A/*(*B)".
        var markers = new HashSet<int>();
        AddGreenMarkers(decoded, markers);
        AddSelectedOptionMarkers(decoded, markers);

        if (markers.Count == 0) return decoded.Text;

        var sb = new StringBuilder(decoded.Text.Length + markers.Count);
        for (int i = 0; i < decoded.Text.Length; i++)
        {
            if (markers.Contains(i)) sb.Append('*');
            sb.Append(decoded.Text[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rule 1 (original): on a line that mixes green with another color, mark each green
    /// segment. Whitespace-only segments are ignored when deciding whether colors are mixed
    /// and are never themselves marked.
    /// </summary>
    private static void AddGreenMarkers(Decoded decoded, HashSet<int> markers)
    {
        var distinctColors = new HashSet<char>();
        foreach (var seg in decoded.Segments)
        {
            if (!string.IsNullOrWhiteSpace(seg.Text)) distinctColors.Add(seg.Color);
        }

        if (distinctColors.Count <= 1 || !distinctColors.Contains('g')) return;

        int offset = 0;
        foreach (var seg in decoded.Segments)
        {
            if (seg.Color == 'g' && !string.IsNullOrWhiteSpace(seg.Text))
            {
                markers.Add(offset);
            }
            offset += seg.Text.Length;
        }
    }

    /// <summary>
    /// Rule 2: within a whitespace-delimited field holding a slash-separated option group,
    /// mark the one option rendered in LARGE font while every sibling is small.
    ///
    /// Deliberately conservative — the group is skipped unless there are at least two
    /// options, each option's letters/digits are uniformly one size, and EXACTLY one option
    /// is large. That leaves ordinary same-size data (page counters "1/1", "FROM/TO"
    /// labels, "250/.78" values) untouched.
    /// </summary>
    private static void AddSelectedOptionMarkers(Decoded decoded, HashSet<int> markers)
    {
        string text = decoded.Text;
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }

            int fieldStart = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            MarkFieldIfOptionGroup(decoded, fieldStart, i, markers);
        }
    }

    private static void MarkFieldIfOptionGroup(Decoded decoded, int start, int end, HashSet<int> markers)
    {
        string text = decoded.Text;

        bool hasSlash = false;
        for (int i = start; i < end; i++)
        {
            if (text[i] == '/') { hasSlash = true; break; }
        }
        if (!hasSlash) return;

        // Split the field into '/'-separated option tokens.
        var tokenStarts = new List<int>();
        var tokenEnds = new List<int>();
        int tokenStart = start;
        for (int i = start; i <= end; i++)
        {
            if (i == end || text[i] == '/')
            {
                tokenStarts.Add(tokenStart);
                tokenEnds.Add(i);
                tokenStart = i + 1;
            }
        }
        if (tokenStarts.Count < 2) return;

        int largeCount = 0;
        int largeMarkerIndex = -1;
        int largeTokenStart = -1, largeTokenEnd = -1;

        for (int t = 0; t < tokenStarts.Count; t++)
        {
            // A token's size is read from its letters/digits only, so decoration such as
            // the leading '<-' cycle arrow (which stays large) can't misreport the option.
            int firstAlnum = -1;
            bool sawLarge = false, sawSmall = false;

            for (int i = tokenStarts[t]; i < tokenEnds[t]; i++)
            {
                if (!char.IsLetterOrDigit(text[i])) continue;
                if (firstAlnum < 0) firstAlnum = i;
                if (decoded.Large[i]) sawLarge = true; else sawSmall = true;
            }

            // No letters/digits, or a token that mixes sizes internally — not an option
            // group we understand; leave the whole field alone.
            if (firstAlnum < 0 || (sawLarge && sawSmall)) return;

            if (sawLarge)
            {
                largeCount++;
                largeMarkerIndex = firstAlnum;
                largeTokenStart = tokenStarts[t];
                largeTokenEnd = tokenEnds[t];
            }
        }

        if (largeCount != 1) return;

        // Rule 1 may already have marked this very token, at a DIFFERENT index: its anchor
        // is the green segment's first character, which is only the same character when the
        // segment happens to start on a letter/digit. Marking again would emit two
        // asterisks in one token ("A/*(*B)"), which reads as noise. One marker per token.
        foreach (int m in markers)
        {
            if (m >= largeTokenStart && m < largeTokenEnd) return;
        }

        markers.Add(largeMarkerIndex);
    }
}
