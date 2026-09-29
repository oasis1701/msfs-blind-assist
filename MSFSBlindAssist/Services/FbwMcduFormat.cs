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
            text.Append(ch);
            i++;
        }
        if (text.Length > 0) { segments.Add(new Segment(color, text.ToString())); }
        return segments;
    }

    /// <summary>
    /// Stand-in for the '*' green-selection marker while a line is being laid out. It takes
    /// no column: <see cref="PositionLine"/> attaches it to the character that follows and
    /// writes it out as '*' only after the columns are fixed. FBW pads a whole row to
    /// exactly 24 columns (INIT FUEL PRED sends ZFW/ZFWCG, the padding AND the BLOCK value
    /// in cell 0), so a real '*' counted as a column pushed the right-hand value past
    /// column 24 and the clip deleted it ("*3.1*/*0137" with BLOCK fuel missing).
    /// </summary>
    private const char Marker = '\u0001';

    /// <summary>
    /// Reconstruct an MCDU line positionally (24 cols): left-aligned left, right-aligned
    /// right, centred centre. Cells keep their own {sp} padding — FBW pads cells to
    /// column-align the display (e.g. F-PLN time "2053    " + speed ".78/ FL370");
    /// trimming the padding and re-centring used to run the time into the speed
    /// ("2053.78"). Spaces never overwrite, so overlapping padding can't erase a
    /// neighbouring cell's text. Trailing whitespace of the finished line is trimmed.
    /// Green-selection markers take no column (see <see cref="Marker"/>), so the finished
    /// line can be longer than <paramref name="width"/> by one '*' per marker.
    /// </summary>
    public static string PositionLine(string left, string center, string right, int width = 24)
    {
        var buf = new char[width];
        var marked = new bool[width];
        for (int i = 0; i < width; i++) { buf[i] = ' '; }
        Place(Strip(left ?? ""), 0);
        var c = Strip(center ?? "");
        if (c.Text.Trim().Length > 0) { Place(c, Math.Max(0, (width - c.Text.Length) / 2)); }
        var r = Strip(right ?? "");
        if (r.Text.Trim().Length > 0) { Place(r, Math.Max(0, width - r.Text.Length)); }

        var sb = new StringBuilder(width + 4);
        for (int i = 0; i < width; i++)
        {
            if (marked[i]) { sb.Append('*'); }
            sb.Append(buf[i]);
        }
        return sb.ToString().TrimEnd();

        void Place((string Text, bool[] Marks) cell, int start)
        {
            for (int j = 0; j < cell.Text.Length; j++)
            {
                int p = start + j;
                if (cell.Text[j] != ' ' && p >= 0 && p < width)
                {
                    buf[p] = cell.Text[j];
                    marked[p] = cell.Marks[j];
                }
            }
        }

        static (string Text, bool[] Marks) Strip(string s)
        {
            if (s.IndexOf(Marker) < 0) { return (s, new bool[s.Length]); }
            var text = new StringBuilder(s.Length);
            var marks = new List<bool>(s.Length);
            bool pending = false;
            foreach (char ch in s)
            {
                if (ch == Marker) { pending = true; continue; }
                text.Append(ch);
                marks.Add(pending);
                pending = false;
            }
            return (text.ToString(), marks.ToArray());
        }
    }

    public static string DecodeCell(string? cell) => DecodeCellMarked(cell).Replace(Marker, '*');

    /// <summary>
    /// <see cref="DecodeCell"/> with each green-selection marker left as the zero-width
    /// <see cref="Marker"/>, for text that still has to go through <see cref="PositionLine"/>.
    /// </summary>
    private static string DecodeCellMarked(string? cell)
    {
        if (string.IsNullOrEmpty(cell)) { return ""; }
        var segments = ParseSegments(cell);
        var colors = new HashSet<string>();
        foreach (var s in segments)
        {
            if (!string.IsNullOrWhiteSpace(s.Text)) { colors.Add(s.Color); }
        }
        bool mixedGreen = colors.Count > 1 && colors.Contains("green");
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            if (mixedGreen && seg.Color == "green" && !string.IsNullOrWhiteSpace(seg.Text))
            {
                string trimmed = seg.Text.TrimStart();
                string leading = seg.Text.Substring(0, seg.Text.Length - trimmed.Length);
                sb.Append(leading).Append(Marker).Append(trimmed);
            }
            else
            {
                sb.Append(seg.Text);
            }
        }
        return sb.ToString();
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

            // Kept marked (zero-width markers) so PositionLine lays out the true 24 columns.
            string labelLeft = DecodeCellMarked(Cell(label, 0));
            string labelRight = DecodeCellMarked(Cell(label, 1));
            string labelCenter = DecodeCellMarked(Cell(label, 2));
            string valueLeft = DecodeCellMarked(Cell(value, 0));
            string valueRight = DecodeCellMarked(Cell(value, 1));
            string valueCenter = DecodeCellMarked(Cell(value, 2));

            data.Lines[k] = new MCDULinePair
            {
                LeftLabel = labelLeft.Replace(Marker, '*'),
                RightLabel = labelRight.Replace(Marker, '*'),
                LeftValue = valueLeft.Replace(Marker, '*'),
                RightValue = valueRight.Replace(Marker, '*'),
            };
            data.RawLines[1 + 2 * k] = PositionLine(labelLeft, labelCenter, labelRight);
            data.RawLines[2 + 2 * k] = PositionLine(valueLeft, valueCenter, valueRight);
        }
        data.RawLines[13] = data.Scratchpad;
        return data;
    }
}
