// Characterization tests for FenixChannelEwdAlertLines, which finds the E/WD line of an ECAM entry's title so the entry
// can be spoken in the color of its own line. Pure logic: the columns are constructed here from lines as the channel
// sent them (measured 2026-11-04).
//
// The contract: the title and a line are equal when, on both sides, a run of two or more dots counts as one space and
// whitespace is collapsed and trimmed (a single dot is text, "T.O"). A line matches alone, or two consecutive lines
// joined with a space match a title the E/WD wraps. The first match top to bottom in the title's own column wins, then
// the other column. The color of a found title is that of the first stretch with text.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdAlertLinesTests
{
    private static readonly string[] None = [];

    [Fact]
    public void A_title_is_found_by_its_joined_text()
    {
        AssertFound("ELEC GEN 1 FAULT", ["c -GEN 1......OFF THEN ON", "uaELECu aGEN 1 FAULT"], ["uaELECu aGEN 1 FAULT"],
            FenixTextColor.Amber);
        AssertFound("ENG 1 FIRE", ["urENG 1 FIREu "], ["urENG 1 FIREu "], FenixTextColor.Red);
        AssertFound("ENG 1 SHUT DOWN", ["uaENG 1u afSHUT DOWNf"], ["uaENG 1u afSHUT DOWNf"], FenixTextColor.Amber);
        AssertFound("AUTO FLT AP OFF", ["urAUTO FLTu rAP OFF"], ["urAUTO FLTu rAP OFF"], FenixTextColor.Red);
    }

    [Fact]
    public void A_config_title_is_found_on_its_own_line()
    {
        string[] left = ["urCONFIGu ", "rSLATS NOT IN T.O CONFIG", "urCONFIGu ", "rFLAPS NOT IN T.O CONFIG"];

        AssertFound("FLAPS NOT IN T.O CONFIG", left, ["rFLAPS NOT IN T.O CONFIG"], FenixTextColor.Red);
    }

    [Fact]
    public void A_title_on_two_lines_is_found()
    {
        string[] left = ["urCONFIGu ", "rFLAPS NOT IN T.O CONFIG"];

        var found = FenixChannelEwdAlertLines.Find("CONFIG FLAPS NOT IN T.O CONFIG", left, None);

        Assert.Equal(left, found);
        Assert.Equal(FenixTextColor.Red, FenixChannelEwdAlertLines.TitleColor(found!));
        Assert.Equal("CONFIG FLAPS NOT IN T.O CONFIG, red", FenixChannelSpeech.Line(found!.Cast<string?>().ToList()));
    }

    [Fact]
    public void Only_a_whole_line_matches()
    {
        Assert.Null(FenixChannelEwdAlertLines.Find("ENG 1", ["uaENG 1u afSHUT DOWNf"], None));
        Assert.Null(FenixChannelEwdAlertLines.Find("GEN 1", ["c -GEN 1......OFF THEN ON"], None));
    }

    [Fact]
    public void The_own_column_comes_first()
    {
        var found = FenixChannelEwdAlertLines.Find("SEAT BELTS", ["gSEAT BELTS"], ["wSEAT BELTS"]);

        Assert.Equal(["gSEAT BELTS"], found);
        Assert.Equal(FenixTextColor.Green, FenixChannelEwdAlertLines.TitleColor(found!));

        var other = FenixChannelEwdAlertLines.Find("T.O AUTO BRK.....MAX", ["gSEAT BELTS"], ["guT.Ou AUTO BRKc.....MAX    "]);

        Assert.Equal(["guT.Ou AUTO BRKc.....MAX    "], other);
    }

    [Fact]
    public void A_run_of_dots_counts_as_a_space()
    {
        Assert.NotNull(FenixChannelEwdAlertLines.Find("LDG LDG GEAR......DN", ["guLDGu LDG GEAR DN"], None));
        Assert.NotNull(FenixChannelEwdAlertLines.Find("T.O AUTO BRK.....MAX", ["guT.Ou AUTO BRK MAX"], None));
        Assert.Null(FenixChannelEwdAlertLines.Find("TO CONFIG", ["rT.O CONFIG"], None));
    }

    [Fact]
    public void No_line_gives_null()
    {
        Assert.Null(FenixChannelEwdAlertLines.Find("ENG 1 FIRE", ["gSEAT BELTS"], ["wSEAT BELTS"]));
        Assert.Null(FenixChannelEwdAlertLines.Find("ENG 1 FIRE", None, None));
        Assert.Null(FenixChannelEwdAlertLines.Find("  ", ["  "], None));
    }

    [Fact]
    public void The_color_is_the_first_stretch_with_text()
    {
        Assert.Equal(FenixTextColor.Red, FenixChannelEwdAlertLines.TitleColor(["rLAND ASAP"]));
        Assert.Equal(FenixTextColor.Amber, FenixChannelEwdAlertLines.TitleColor(["aLAND ASAP"]));
        Assert.Equal(FenixTextColor.Red, FenixChannelEwdAlertLines.TitleColor([" rLAND ASAP"]));
        Assert.Null(FenixChannelEwdAlertLines.TitleColor(["   "]));
    }

    [Fact]
    public void A_memo_has_the_words_the_ewd_draws()
    {
        AssertMemo("FUEL < 3 T", "FOB BELOW 3 T", ["FOB BELOW 3 T"]);
        AssertMemo("T.CAS STBY", "TCAS STBY", ["TCAS STBY"]);
        AssertMemo("T.O. INHIBIT", "T.O INHIBIT", ["T.O INHIBIT"]);
        AssertMemo("SPEED BRAKE", "SPEED BRK", ["SPEED BRK"]);
        AssertMemo("AUTO BRK", "AUTO BRK", ["AUTO BRK LO", "AUTO BRK MED", "AUTO BRK MAX"]);
    }

    [Fact]
    public void Any_other_memo_keeps_its_name()
    {
        AssertMemo("PARK BRK", "PARK BRK", ["PARK BRK"]);
    }

    [Fact]
    public void Matching_does_not_depend_on_the_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.NotNull(FenixChannelEwdAlertLines.Find("ENG 1 FIRE", ["urENG 1 FIREu "], None));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static void AssertFound(string title, string[] own, string[] expected, FenixTextColor color)
    {
        var found = FenixChannelEwdAlertLines.Find(title, own, None);

        Assert.Equal(expected, found);
        Assert.Equal(color, FenixChannelEwdAlertLines.TitleColor(found!));
    }

    private static void AssertMemo(string name, string text, string[] lineTexts)
    {
        var words = FenixChannelEwdAlertLines.MemoWords(name);

        Assert.Equal(text, words.Text);
        Assert.Equal(lineTexts, words.LineTexts);
    }
}
