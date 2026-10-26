// Characterization tests for Fenix v3 (3.0.0.7426) MCDU screens, captured verbatim from the
// data service's aircraft.mcduN.display. Each fixture is decoded the way
// FenixMCDUService.ParseDisplayXml does it: XDocument.Parse, then the title, the twelve lines and
// the scratchpad, each through FenixMcduFormat.StripFormatCodes. A capture that reads off the
// 24-column grid, or carries a glyph SpecialChars does not know, is a v3 misread.

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

    // ARRIVAL to KMCO, approach list showing (MCDU 1, 2026-10-22, CFM A320 C-FFWM).
    private const string Arrival = """
        <root>
          <title>    ARRIVAL sTOl gKMCOw   £¢</title>
          <line>s APPR      VIA     STAR </line>
          <line>------    ------  ------</line>
          <line>s                  TRANS </line>
          <line>                  ------</line>
          <line>slAPPRl   sAVAILABLEl        </line>
          <line>c£ILS36R    3659sM  CRS005lw</line>
          <line>scs        IOJP/110.70lw     </line>
          <line>c£RNV17L-Y   2744sM CRS185lw</line>
          <line>s                        </line>
          <line>c£RNV17L-Z   2744sM CRS185lw</line>
          <line>s                        </line>
          <line>&lt;RETURN                 </line>
          <scratchpad>                      ¤¥</scratchpad>
        </root>
        """;

    // PERF TAKE OFF at the gate, nothing entered (MCDU 1, 2026-10-22, CFM A320 C-FFWM).
    private const string PerfTakeOff = """
        <root>
          <title>        TAKE OFF        </title>
          <line>s V1  FLP RETR           </line>
          <line>a###     wF=---           </line>
          <line>s VR  SLT RETR  TO SHIFT </line>
          <line>a###     wS=---   [sMl]w ----w</line>
          <line>s V2     CLEAN  FLAPS/THS</line>
          <line>a###     wO=---   c[]/[   ]w</line>
          <line>sTRANS ALT   FLEX TO TEMP</line>
          <line>cs18000lw              c[  ]°w</line>
          <line>sTHR RED/ACC  ENG OUT ACC</line>
          <line>c s2050l/s2050lw          cs2050lw</line>
          <line>s                   NEXT </line>
          <line>                  PHASE&gt;</line>
          <scratchpad>                        </scratchpad>
        </root>
        """;

    // ATSU DATALINK (MCDU MENU, then LSK 2L; MCDU 1, 2026-10-22, CFM A320 C-FFWM).
    private const string Atsu = """
        <root>
          <title>     ATSU DATALINK      </title>
          <line>s                        </line>
          <line>               AOC MENU&gt;</line>
          <line>s                        </line>
          <line>                        </line>
          <line>s                        </line>
          <line>                        </line>
          <line>s                        </line>
          <line>                        </line>
          <line>s               DATALINK </line>
          <line>                 STATUS&gt;</line>
          <line>s                        </line>
          <line>              COMM MENU&gt;</line>
          <scratchpad>                        </scratchpad>
        </root>
        """;

    public static TheoryData<string, string> Captures => new()
    {
        { "MCDU MENU", McduMenu },
        { "ARRIVAL", Arrival },
        { "PERF TAKE OFF", PerfTakeOff },
        { "ATSU", Atsu },
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

    [Fact]
    public void Arrival_v3_reads_rnv_approaches()
    {
        var lines = Decode(Arrival);

        Assert.Equal(new[]
        {
            // The destination is green among white, so the green rule marks it (as on v2).
            "    ARRIVAL TO *KMCO   ←→",
            " APPR      VIA     STAR ",
            "------    ------  ------",
            "                  TRANS ",
            "                  ------",
            "APPR   AVAILABLE        ",
            "←ILS36R    3659M  CRS005",
            "        IOJP/110.70     ",
            "←RNV17L-Y   2744M CRS185",
            new string(' ', 24),
            "←RNV17L-Z   2744M CRS185",
            new string(' ', 24),
            "<RETURN                 ",
            new string(' ', 22) + "↑↓",
        }, lines);
        Assert.Contains(lines, l => l.Contains("RNV"));
        Assert.DoesNotContain(lines, l => l.Contains("RNAV"));
    }

    [Fact]
    public void Perf_takeoff_v3_decodes()
    {
        Assert.Equal(new[]
        {
            "        TAKE OFF        ",
            " V1  FLP RETR           ",
            "---     F=---           ",
            " VR  SLT RETR  TO SHIFT ",
            "---     S=---   [M] ----",
            " V2     CLEAN  FLAPS/THS",
            "---     O=---   []/[   ]",
            "TRANS ALT   FLEX TO TEMP",
            "18000              [  ]°",
            "THR RED/ACC  ENG OUT ACC",
            " 2050/2050          2050",
            "                   NEXT ",
            "                  PHASE>",
            "",
        }, Decode(PerfTakeOff));
    }

    [Fact]
    public void Atsu_v3_decodes()
    {
        string blank = new(' ', 24);
        Assert.Equal(new[]
        {
            "     ATSU DATALINK      ",
            blank,
            "               AOC MENU>",
            blank, "",
            blank, "",
            blank, "",
            "               DATALINK ",
            "                 STATUS>",
            blank,
            "              COMM MENU>",
            "",
        }, Decode(Atsu));
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
        // The four arrow keys of SpecialChars, plus the degree sign, which passes through
        // unmapped and NVDA speaks as "degree" (PERF TAKE OFF's FLEX TO TEMP).
        char[] known = { '¤', '¥', '¢', '£', '°' };
        foreach (char c in xml.Where(c => c > '~'))
            Assert.True(known.Contains(c), $"{page}: unknown glyph U+{(int)c:X4}");
    }
}
