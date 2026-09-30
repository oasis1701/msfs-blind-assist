using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Decodes FlyByWire MCDU cell markup ({green}/{small}/{sp}/{end}/...) into accessible
/// plain text and builds an <see cref="MCDUDisplayData"/> from a SimBridge update side.
/// Mirrors tools/fbw-mcdu-probe/mcdu-format.js — keep the two in sync.
/// </summary>
public static class FbwMcduFormat
{
    private static readonly HashSet<string> ColorTags = new()
        { "green", "amber", "cyan", "white", "magenta", "yellow", "red", "inop" };

    private static readonly string[] AnnunciatorOrder =
        { "fail", "fmgc", "mcdu_menu", "fm1", "fm2", "ind", "rdy" };

    private static readonly Dictionary<string, string> AnnunciatorLabels = new()
    {
        { "fail", "FAIL" }, { "fmgc", "FMGC" }, { "mcdu_menu", "MENU" },
        { "fm1", "FM1" }, { "fm2", "FM2" }, { "ind", "IND" }, { "rdy", "RDY" },
    };

    // Size/align tags affect styling only; we drop them but still consume the {tag}.
    private static readonly HashSet<string> DropTags = new() { "small", "big", "left", "right" };

    private const char NoBreakSpace = '\u00A0';

    private static bool IsKnownTag(string tag)
        => tag == "sp" || tag == "end" || ColorTags.Contains(tag) || DropTags.Contains(tag);

    private readonly record struct Segment(string Color, string Text);

    private static List<Segment> ParseSegments(string cell)
    {
        var segments = new List<Segment>();
        string color = "white";
        var text = new StringBuilder();
        int i = 0;
        while (i < cell.Length)
        {
            char ch = cell[i];
            if (ch == '{')
            {
                int close = cell.IndexOf('}', i);
                if (close != -1)
                {
                    string tag = cell.Substring(i + 1, close - i - 1);
                    if (IsKnownTag(tag))
                    {
                        if (tag == "sp")
                        {
                            text.Append(' ');
                        }
                        else if (ColorTags.Contains(tag))
                        {
                            if (text.Length > 0) { segments.Add(new Segment(color, text.ToString())); text.Clear(); }
                            color = tag;
                        }
                        else if (tag == "end")
                        {
                            if (text.Length > 0) { segments.Add(new Segment(color, text.ToString())); text.Clear(); }
                            color = "white";
                        }
                        // small/big/left/right: styling only, dropped
                        i = close + 1;
                        continue;
                    }
                }
                // A lone '{' that does NOT open a known {tag} is the FBW MCDU's LSK arrow /
                // bracket glyph (e.g. "{08L" = the selectable runway prompt). Drop the glyph
                // and keep the content ("08L"); the old greedy parse ate everything up to the
                // next '}', deleting the runway designator and breaking the DEP/ARR pages.
                i++;
                continue;
            }
            if (ch == '}')
            {
                // Stray right-side arrow/bracket glyph (real {tag} closers are consumed above).
                i++;
                continue;
            }
            // FBW pads cells and draws its entry boxes ("[\xa0\xa0]") with U+00A0. It is a blank
            // like {sp}, so it becomes one here and nothing downstream has to know it exists.
            text.Append(ch == NoBreakSpace ? ' ' : ch);
            i++;
        }
        if (text.Length > 0) { segments.Add(new Segment(color, text.ToString())); }
        return segments;
    }

    /// <summary>
    /// Decoded cell text with its green-selection markers held beside it rather than in it:
    /// <c>Marks[j]</c> is true when a '*' belongs in front of <c>Text[j]</c>. <c>Marks</c> is
    /// null when the cell has none. FBW pads a whole row to exactly 24 columns (INIT FUEL
    /// PRED sends ZFW/ZFWCG, the padding AND the BLOCK value in cell 0), so a '*' counted as
    /// a column pushed the right-hand value past column 24 and the clip deleted it
    /// ("*3.1*/*0137" with BLOCK fuel missing); <see cref="PositionLine"/> lays the columns
    /// out first and places the markers after.
    /// </summary>
    private readonly record struct MarkedText(string Text, bool[]? Marks)
    {
        public bool IsMarked(int j) => Marks != null && Marks[j];

        /// <summary>The text with each marker written out as '*' in front of its character.</summary>
        public string WithStars()
        {
            if (Marks == null) { return Text; }
            var sb = new StringBuilder(Text.Length + 4);
            for (int j = 0; j < Text.Length; j++)
            {
                if (Marks[j]) { sb.Append('*'); }
                sb.Append(Text[j]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Reconstruct an MCDU line positionally (24 cols): left-aligned left, right-aligned
    /// right, centred centre. Cells keep their own {sp} padding — FBW pads cells to
    /// column-align the display (e.g. F-PLN time "2053    " + speed ".78/ FL370");
    /// trimming the padding and re-centring used to run the time into the speed
    /// ("2053.78"). Spaces never overwrite, so overlapping padding can't erase a
    /// neighbouring cell's text. The text here is not decoded (the DCDU's), so a U+00A0 is
    /// made a plain space first, as <see cref="ParseSegments"/> does for decoded cells.
    /// Trailing whitespace of the finished line is trimmed.
    /// A '*' in the text is an ordinary column (the DCDU's key stars are real content).
    /// </summary>
    public static string PositionLine(string left, string center, string right, int width = 24)
        => PositionLine(Undecoded(left), Undecoded(center), Undecoded(right), width);

    private static MarkedText Undecoded(string? text)
        => new((text ?? "").Replace(NoBreakSpace, ' '), null);

    /// <summary>
    /// <see cref="PositionLine(string, string, string, int)"/> for decoded cells. The columns
    /// are laid out without the green-selection markers, then each marker is written in:
    /// into the blank column in front of its text when the gap there has one to spare (a gap
    /// after text keeps one blank, so "ALT *FL370" keeps its space), otherwise inserted, and
    /// an inserted '*' is paid back by the next gap with a blank to spare. So everything after
    /// a gap stays in its FBW column — the right-hand values line up down the page, which a
    /// braille display depends on — and the line is wider than <paramref name="width"/> only
    /// when no gap can pay. The blanks inside an entry box ("[  ]", which FBW draws from the
    /// same U+00A0 it pads with) are the width of the field, never a gap to pay from.
    /// </summary>
    private static string PositionLine(MarkedText left, MarkedText center, MarkedText right, int width = 24)
    {
        var buf = new char[width];
        bool[]? marked = null;
        for (int i = 0; i < width; i++) { buf[i] = ' '; }
        Place(left, 0);
        if (center.Text.Trim().Length > 0) { Place(center, Math.Max(0, (width - center.Text.Length) / 2)); }
        if (right.Text.Trim().Length > 0) { Place(right, Math.Max(0, width - right.Text.Length)); }
        if (marked == null) { return new string(buf).TrimEnd(); }

        var sb = new StringBuilder(width + 4);
        int owed = 0;          // inserted stars not yet paid back by a gap
        for (int i = 0; i < width;)
        {
            if (buf[i] != ' ')
            {
                // A value that follows a gap had its '*' written by the gap.
                if (marked[i] && (i == 0 || buf[i - 1] != ' ')) { sb.Append('*'); owed++; }
                sb.Append(buf[i]);
                i++;
                continue;
            }
            int end = i;
            while (end < width && buf[end] == ' ') { end++; }
            if (end == width) { break; }
            // Blanks the gap can give up: all of a leading gap, all but one after text, and none
            // inside an entry box ("[  ]"), whose blanks are the width of the field.
            bool box = i > 0 && buf[i - 1] == '[' && buf[end] == ']';
            int spare = box ? 0 : end - i - (i == 0 ? 0 : 1);
            int taken = marked[end] && spare > 0 ? 1 : 0;   // the '*' takes a blank
            if (marked[end] && taken == 0) { owed++; }      // or is inserted
            int repay = Math.Min(owed, spare - taken);
            owed -= repay;
            sb.Append(' ', end - i - taken - repay);
            if (marked[end]) { sb.Append('*'); }
            i = end;
        }
        return sb.ToString().TrimEnd();

        void Place(MarkedText cell, int start)
        {
            for (int j = 0; j < cell.Text.Length; j++)
            {
                int p = start + j;
                if (cell.Text[j] != ' ' && p >= 0 && p < width)
                {
                    buf[p] = cell.Text[j];
                    bool mark = cell.IsMarked(j);
                    if (mark) { marked ??= new bool[width]; }
                    if (marked != null) { marked[p] = mark; }
                }
            }
        }
    }

    public static string DecodeCell(string? cell) => DecodeCellMarked(cell).WithStars();

    /// <summary>
    /// <see cref="DecodeCell"/> with the green-selection markers kept beside the text, for
    /// cells that still have to go through <see cref="PositionLine(MarkedText, MarkedText, MarkedText, int)"/>.
    /// </summary>
    private static MarkedText DecodeCellMarked(string? cell)
    {
        if (string.IsNullOrEmpty(cell)) { return new MarkedText("", null); }
        var segments = ParseSegments(cell);
        var colors = new HashSet<string>();
        foreach (var s in segments)
        {
            if (!string.IsNullOrWhiteSpace(s.Text)) { colors.Add(s.Color); }
        }
        bool mixedGreen = colors.Count > 1 && colors.Contains("green");
        var sb = new StringBuilder();
        List<int>? markAt = null;
        // FBW sends one green value as touching pieces ("10.2", "/", "0213"). A pilot sees one
        // green value, so a piece that continues a green run gets no '*' of its own.
        bool inGreenRun = false;
        foreach (var seg in segments)
        {
            bool green = mixedGreen && seg.Color == "green" && !string.IsNullOrWhiteSpace(seg.Text);
            if (green)
            {
                string trimmed = seg.Text.TrimStart();
                string leading = seg.Text.Substring(0, seg.Text.Length - trimmed.Length);
                sb.Append(leading);
                if (!(inGreenRun && leading.Length == 0)) { (markAt ??= new List<int>()).Add(sb.Length); }
                sb.Append(trimmed);
            }
            else
            {
                sb.Append(seg.Text);
            }
            inGreenRun = green && !char.IsWhiteSpace(seg.Text[^1]);
        }
        if (markAt == null) { return new MarkedText(sb.ToString(), null); }
        var marks = new bool[sb.Length];
        foreach (int j in markAt) { marks[j] = true; }
        return new MarkedText(sb.ToString(), marks);
    }

    public static List<string> LitAnnunciators(JToken? ann)
    {
        var result = new List<string>();
        if (ann == null) { return result; }
        foreach (var key in AnnunciatorOrder)
        {
            if (ann[key]?.Type == JTokenType.Boolean && ann[key]!.Value<bool>()
                && AnnunciatorLabels.TryGetValue(key, out var label))
            {
                result.Add(label);
            }
        }
        return result;
    }

    public static string JoinColumns(string left, string center, string right)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(left)) { parts.Add(left.Trim()); }
        if (!string.IsNullOrWhiteSpace(center)) { parts.Add(center.Trim()); }
        if (!string.IsNullOrWhiteSpace(right)) { parts.Add(right.Trim()); }
        return string.Join("   ", parts);
    }

    private static string? Cell(JArray? row, int idx)
        => row != null && idx < row.Count ? row[idx]?.ToString() : null;

    /// <summary>Build display data from one side ("left" or "right") of an update payload.</summary>
    public static MCDUDisplayData BuildDisplayData(JObject side)
    {
        var data = new MCDUDisplayData();
        var lines = side["lines"] as JArray;

        data.Title = DecodeCell(side["title"]?.ToString());
        data.Page = DecodeCell(side["page"]?.ToString());
        data.Scratchpad = DecodeCell(side["scratchpad"]?.ToString());
        data.Annunciators = LitAnnunciators(side["annunciators"]);

        var arrows = side["arrows"] as JArray;
        for (int a = 0; a < 4; a++)
        {
            data.Arrows[a] = arrows != null && a < arrows.Count
                && arrows[a].Type == JTokenType.Boolean && arrows[a].Value<bool>();
        }

        // RawLines[0]=title, [1..12]=12 joined rows (label/value interleaved), [13]=scratchpad.
        data.RawLines[0] = data.Title;
        for (int k = 0; k < 6; k++)
        {
            JArray? label = lines != null && 2 * k < lines.Count ? lines[2 * k] as JArray : null;
            JArray? value = lines != null && 2 * k + 1 < lines.Count ? lines[2 * k + 1] as JArray : null;

            // Kept marked so PositionLine lays out the true 24 columns.
            var labelLeft = DecodeCellMarked(Cell(label, 0));
            var labelRight = DecodeCellMarked(Cell(label, 1));
            var labelCenter = DecodeCellMarked(Cell(label, 2));
            var valueLeft = DecodeCellMarked(Cell(value, 0));
            var valueRight = DecodeCellMarked(Cell(value, 1));
            var valueCenter = DecodeCellMarked(Cell(value, 2));

            data.Lines[k] = new MCDULinePair
            {
                LeftLabel = labelLeft.WithStars(),
                RightLabel = labelRight.WithStars(),
                LeftValue = valueLeft.WithStars(),
                RightValue = valueRight.WithStars(),
            };
            data.RawLines[1 + 2 * k] = PositionLine(labelLeft, labelCenter, labelRight);
            data.RawLines[2 + 2 * k] = PositionLine(valueLeft, valueCenter, valueRight);
        }
        data.RawLines[13] = data.Scratchpad;
        return data;
    }
}
