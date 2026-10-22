// Characterization tests for Fenix v3 (3.0.0.7426) MCDU screens, captured verbatim from the
// data service's aircraft.mcduN.display. Each fixture is decoded the way
// FenixMCDUService.ParseDisplayXml does it: XDocument.Parse, then the title, the twelve lines and
// the scratchpad, each through FenixMcduFormat.StripFormatCodes. A capture that reads off the
// 24-column grid, or carries a glyph SpecialChars does not know, is a v3 misread (plan 04, Task 5).

using System.Xml.Linq;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixMcduV3ScreensTests
{
    // MCDU MENU, identical on both sides (2026-10-21 13:15): v3's <ATSU replaces v2's line, and
    // the active <FMGC is green.
    private const string McduMenu = """
        <root>
          <title>       MCDU MENU        </title>
          <line>s                        </line>
          <line>g&lt;FMGCw            CONFIG&gt;</line>
          <line>s                        </line>
          <line>&lt;ATSU             MAINT&gt;</line>
          <line>s                        </line>
          <line>                        </line>
          <line>s                        </line>
          <line>                        </line>
          <line>s                        </line>
          <line>                        </line>
          <line>s                        </line>
          <line>                        </line>
          <scratchpad>SELECT DESIRED SYSTEM   </scratchpad>
        </root>
        """;

    public static TheoryData<string, string> Captures => new()
    {
        { "MCDU MENU", McduMenu },
    };

    private static string[] Decode(string xml)
    {
        var root = XDocument.Parse(xml).Root!;
        var decoded = new List<string> { FenixMcduFormat.StripFormatCodes(root.Element("title")!.Value) };
        decoded.AddRange(root.Elements("line").Select(l => FenixMcduFormat.StripFormatCodes(l.Value)));
        decoded.Add(FenixMcduFormat.StripFormatCodes(root.Element("scratchpad")!.Value));
        return decoded.ToArray();
    }

    [Fact]
    public void Mcdu_menu_v3_lists_atsu_and_marks_the_active_fmgc()
    {
        var lines = Decode(McduMenu);

        Assert.Equal(14, lines.Length);
        Assert.Equal("       MCDU MENU        ", lines[0]);
        Assert.Equal("*<FMGC            CONFIG>", lines[2]);
        Assert.Equal("<ATSU             MAINT>", lines[4]);
        Assert.Equal("SELECT DESIRED SYSTEM   ", lines[13]);
        // A small-font blank keeps its 24 spaces once the 's' is stripped.
        foreach (int i in new[] { 1, 3, 5, 7, 9, 11 })
            Assert.Equal(new string(' ', 24), lines[i]);
        // An all-space line is dropped by XDocument.Parse (no PreserveWhitespace) and reads "",
        // the app's ordinary blank: the service fills missing lines with "", SplitLine pads to 24
        // and the form trims line ends, so the pilot cannot tell the two apart.
        foreach (int i in new[] { 6, 8, 10, 12 })
            Assert.Equal("", lines[i]);
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void Every_v3_capture_stays_on_the_24_column_grid(string page, string xml)
    {
        foreach (var line in Decode(xml).Where(l => l.Length > 0))
            Assert.True(line.Replace("*", "").Length == 24, $"{page}: \"{line}\" is not 24 columns");
    }

    [Theory]
    [MemberData(nameof(Captures))]
    public void Every_v3_capture_uses_only_known_glyphs(string page, string xml)
    {
        char[] known = { '¤', '¥', '¢', '£' };
        foreach (char c in xml.Where(c => c > '~'))
            Assert.True(known.Contains(c), $"{page}: unknown glyph U+{(int)c:X4}");
    }
}
