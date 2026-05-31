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

    private readonly record struct Segment(string Color, string Text);

    private static List<Segment> ParseSegments(string cell)
    {
        var segments = new List<Segment>();
        string color = "white";
        var text = new StringBuilder();
        int i = 0;
        while (i < cell.Length)
        {
            if (cell[i] == '{')
            {
                int close = cell.IndexOf('}', i);
                if (close != -1)
                {
                    string tag = cell.Substring(i + 1, close - i - 1);
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
                    // small/big/left/right/unknown tags: dropped
                    i = close + 1;
                    continue;
                }
            }
            // No closing '}' (or not at a '{'): treat this char (incl. a lone '{') as literal text.
            text.Append(cell[i]);
            i++;
        }
        if (text.Length > 0) { segments.Add(new Segment(color, text.ToString())); }
        return segments;
    }

    public static string DecodeCell(string? cell)
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
                sb.Append(leading).Append('*').Append(trimmed);
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

            string labelLeft = DecodeCell(Cell(label, 0));
            string labelRight = DecodeCell(Cell(label, 1));
            string labelCenter = DecodeCell(Cell(label, 2));
            string valueLeft = DecodeCell(Cell(value, 0));
            string valueRight = DecodeCell(Cell(value, 1));
            string valueCenter = DecodeCell(Cell(value, 2));

            data.Lines[k] = new MCDULinePair
            {
                LeftLabel = labelLeft,
                RightLabel = labelRight,
                LeftValue = valueLeft,
                RightValue = valueRight,
            };
            data.RawLines[1 + 2 * k] = JoinColumns(labelLeft, labelCenter, labelRight);
            data.RawLines[2 + 2 * k] = JoinColumns(valueLeft, valueCenter, valueRight);
        }
        data.RawLines[13] = data.Scratchpad;
        return data;
    }
}
