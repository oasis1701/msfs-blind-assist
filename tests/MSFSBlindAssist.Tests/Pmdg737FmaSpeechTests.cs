// The FMA speech composes what the pilot hears when the 737 FMA changes: PMDG's own printed words,
// verbatim, in PFD order. Pure logic over the FMA fixture's golden layout.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737FmaSpeechTests
{
    private static readonly Pmdg737FmaLayout Layout = GoldenLayouts.Fma;

    private static readonly FmaState Zero = new(0, 0, 0, 0, 0, 0);

    private static string? Change(FmaState before, FmaState after) =>
        Pmdg737FmaSpeech.DescribeChange(before, after, Layout);

    private const string GroupedExpected = "A/T: N1, Pitch: G/S, AFDS: SINGLE CH";
    private const string AllExpected = "A/T: MCP SPD. Roll: HDG SEL, armed VOR/LOC. Pitch: ALT HOLD, armed G/S. AFDS: CMD.";

    private static string Grouped() =>
        Change(new FmaState(0, 0, 0, 0, 0, 2), new FmaState(5, 0, 0, 7, 0, 4))!;

    private static string All() =>
        Pmdg737FmaSpeech.DescribeAll(new FmaState(3, 3, 4, 6, 7, 3), Layout);

    [Fact]
    public void Columns_join_to_describe_all()
    {
        var s = new FmaState(3, 3, 4, 6, 7, 3);
        var columns = Pmdg737FmaSpeech.Columns(s, Layout);
        Assert.Equal(new[] { "A/T: MCP SPD", "Roll: HDG SEL, armed VOR/LOC", "Pitch: ALT HOLD, armed G/S", "AFDS: CMD" }, columns);
        Assert.Equal(AllExpected, string.Join(". ", columns) + ".");
        Assert.Equal(AllExpected, All());
    }

    [Fact]
    public void With_armed_replaces_the_armed_part()
    {
        // The PFD window's roll row takes the armed text PMDG draws (LNAV armed beside another mode).
        var roll = Pmdg737FmaSpeech.Columns(new FmaState(3, 3, 4, 6, 7, 3), Layout)[1];
        Assert.StartsWith(Pmdg737FmaSpeech.RollLabel + ": ", roll, StringComparison.Ordinal);
        Assert.Equal("Roll: HDG SEL, armed LNAV VOR/LOC", Pmdg737FmaSpeech.WithArmed(roll, "LNAV VOR/LOC"));
        Assert.Equal("Roll: HDG SEL, armed LNAV", Pmdg737FmaSpeech.WithArmed("Roll: HDG SEL", "LNAV"));
    }

    [Fact]
    public void Single_change() =>
        Assert.Equal("Roll: VOR/LOC", Change(Zero, Zero with { Roll = 4 }));

    [Fact]
    public void Grouped_in_pfd_order() => Assert.Equal(GroupedExpected, Grouped());

    [Fact]
    public void Armed_becoming_active_spoken_once() =>
        Assert.Equal("Roll: VOR/LOC", Change(new FmaState(0, 3, 4, 0, 0, 0), new FmaState(0, 4, 0, 0, 0, 0)));

    [Fact]
    public void Armed_cleared_says_none() =>
        Assert.Equal("Roll armed: none", Change(new FmaState(0, 3, 5, 0, 0, 0), new FmaState(0, 3, 0, 0, 0, 0)));

    [Fact]
    public void Blank_says_none() =>
        Assert.Equal("A/T: none", Change(new FmaState(3, 0, 0, 0, 0, 0), Zero));

    [Fact]
    public void Cws_value_one()
    {
        Assert.Equal("Pitch: CWS P", Change(Zero, Zero with { Pitch = 1 }));
        Assert.Equal("Roll: CWS R", Change(Zero, Zero with { Roll = 1 }));
    }

    // A value with no word is spoken as "unrecognised", never dropped: on a TO/GA go-around from LOC
    // roll changes to value 2, which has no word, and silence would leave the pilot believing LOC.
    [Fact]
    public void No_word_value_spoken_unrecognised() =>
        Assert.Equal("Roll: unrecognised", Change(Zero, Zero with { Roll = 2 }));

    [Fact]
    public void Armed_no_word_value_spoken_unrecognised() =>
        Assert.Equal("Roll armed: unrecognised", Change(Zero, Zero with { RollArmed = 2 }));

    [Fact]
    public void Armed_no_word_value_becoming_active_spoken_once() =>
        Assert.Equal("Roll: unrecognised", Change(new FmaState(0, 3, 2, 0, 0, 0), new FmaState(0, 2, 0, 0, 0, 0)));

    [Fact]
    public void Armed_cleared_while_active_becomes_another_value_says_both() =>
        Assert.Equal("Roll: unrecognised, Roll armed: none",
            Change(new FmaState(0, 3, 4, 0, 0, 0), new FmaState(0, 2, 0, 0, 0, 0)));

    [Fact]
    public void Corrupt_omitted()
    {
        Assert.Null(Change(Zero, Zero with { Pitch = 999 }));
        Assert.Equal(FmaWordKind.Corrupt, Pmdg737FmaSpeech.Lookup(Layout.PitchWords, 999).Kind);
    }

    [Fact]
    public void Nothing_changed_is_null() =>
        Assert.Null(Change(new FmaState(3, 3, 4, 6, 7, 3), new FmaState(3, 3, 4, 6, 7, 3)));

    [Fact]
    public void Lookup_range_rules()
    {
        var p = Layout.PitchWords;
        int end = p.FirstValue + p.Words.Count;
        Assert.Equal(FmaWordKind.Blank, Pmdg737FmaSpeech.Lookup(p, 0).Kind);
        Assert.Equal((FmaWordKind.Word, "CWS P"), Pmdg737FmaSpeech.Lookup(p, 1));
        Assert.Equal((FmaWordKind.Word, "TO/GA"), Pmdg737FmaSpeech.Lookup(p, 2));
        Assert.Equal(FmaWordKind.Word, Pmdg737FmaSpeech.Lookup(p, end - 1).Kind);
        Assert.Equal(FmaWordKind.NoWord, Pmdg737FmaSpeech.Lookup(p, end).Kind);
        Assert.Equal(FmaWordKind.NoWord, Pmdg737FmaSpeech.Lookup(p, end + 7).Kind);
        Assert.Equal(FmaWordKind.Corrupt, Pmdg737FmaSpeech.Lookup(p, end + 8).Kind);
        Assert.Equal(FmaWordKind.Corrupt, Pmdg737FmaSpeech.Lookup(p, -1).Kind);
        // Status starts at 2 and has no value-one word, so 1 is a value with no word.
        Assert.Equal(FmaWordKind.NoWord, Pmdg737FmaSpeech.Lookup(Layout.StatusWords, 1).Kind);
    }

    [Fact]
    public void Describe_all() => Assert.Equal(AllExpected, All());

    [Fact]
    public void Describe_all_blank_and_no_word()
    {
        Assert.Equal("A/T: none. Roll: none. Pitch: none. AFDS: none.", Pmdg737FmaSpeech.DescribeAll(Zero, Layout));
        Assert.Equal("A/T: none. Roll: unrecognised. Pitch: none. AFDS: none.",
            Pmdg737FmaSpeech.DescribeAll(Zero with { Roll = 2 }, Layout));
    }

    // Every change the monitor speaks starts with "FMA: " so the pilot knows where it came from among the
    // MCP lights and other callouts; DescribeChange itself stays bare (it is also the tail of "resumed, ...").
    [Fact]
    public void Change_announcement_starts_with_fma()
    {
        Assert.Equal("FMA: ", Pmdg737FmaSpeech.ChangePrefix);
        Assert.Equal("FMA: Roll: VOR/LOC", Pmdg737FmaSpeech.ChangeAnnouncement("Roll: VOR/LOC"));
        Assert.Equal("FMA: A/T: N1, Pitch: G/S, AFDS: SINGLE CH", Pmdg737FmaSpeech.ChangeAnnouncement(Grouped()));
        Assert.Equal("FMA: Roll: unrecognised",
            Pmdg737FmaSpeech.ChangeAnnouncement(Change(Zero, Zero with { Roll = 2 })!));
    }

    [Fact]
    public void Readout_never_gets_the_change_prefix() =>
        Assert.DoesNotContain(Pmdg737FmaSpeech.ChangePrefix, All()); // the whole-FMA readout: "we already know what it is"

    [Fact]
    public void Fixed_messages()
    {
        Assert.Equal("FMA reader starting", Pmdg737FmaSpeech.Starting);
        Assert.Equal("FMA reader waiting for PMDG SDK data", Pmdg737FmaSpeech.WaitingForSdk);
        Assert.Equal("FMA announcements resumed", Pmdg737FmaSpeech.Resumed);
        Assert.Equal("FMA announcements unavailable: PMDG file not recognised",
            Pmdg737FmaSpeech.Unavailable(FmaReasons.FileNotRecognised));
        Assert.Equal("FMA announcements stopped: unexpected values",
            Pmdg737FmaSpeech.Stopped(FmaReasons.UnexpectedValues));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Spoken_text_is_culture_invariant(string culture)
    {
        Assert.Equal(GroupedExpected, TestCulture.Run(culture, Grouped));
        Assert.Equal(AllExpected, TestCulture.Run(culture, All));
        Assert.Equal("Roll: unrecognised", TestCulture.Run(culture, () => Change(Zero, Zero with { Roll = 2 })));
        Assert.Equal("FMA: " + GroupedExpected,
            TestCulture.Run(culture, () => Pmdg737FmaSpeech.ChangeAnnouncement(Grouped())));
        Assert.Equal("FMA reader waiting for PMDG SDK data", TestCulture.Run(culture, () => Pmdg737FmaSpeech.WaitingForSdk));
    }
}
