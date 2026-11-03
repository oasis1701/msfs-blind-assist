// Characterization tests for FenixChannelSdPages, what every page of the Fenix SD shares: the name of each page, the
// one word for a valve state, and the dispatch from a page number to the composer that gives the page its rows. Pure
// logic. No page has a composer yet (plans 05 and 06 implement them, each once its in-sim checks have passed), so the
// dispatch is tested as what it is today: nothing, for every page. Every number is written out here, never read from
// FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: the SD's pages are numbered as the frame's FwcPage carries them, 1 APU to 12 WHEEL and 14 STATUS;
// pages 0 and 13 are not known to be drawn. A page is named as the SD draws it, and a number that is none of those is
// "page <n>", written in the invariant culture. A valve state has one word, "XX" for the data invalid that the SD draws
// as crosses, and "not available" for a number that is no state. A page whose composer has not landed gives no rows at
// all (null, not an empty list), and the SD window then shows the page's name and the bottom line alone.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdPagesTests
{
    // ---- The names --------------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "APU")]
    [InlineData(2, "BLEED")]
    [InlineData(3, "PRESS")]
    [InlineData(4, "COND")]
    [InlineData(5, "CRUISE")]       // inferred: it is the page the SD showed in cruise
    [InlineData(6, "DOOR")]
    [InlineData(7, "ELEC")]
    [InlineData(8, "ENG")]
    [InlineData(9, "F/CTL")]
    [InlineData(10, "FUEL")]
    [InlineData(11, "HYD")]
    [InlineData(12, "WHEEL")]
    [InlineData(14, "STATUS")]
    public void Each_page_number_has_its_name(int page, string name)
    {
        Assert.Equal(name, FenixChannelSdPages.NameOf(page));
    }

    [Theory]
    [InlineData(42, "page 42")]
    [InlineData(100, "page 100")]
    [InlineData(-1, "page -1")]
    [InlineData(int.MaxValue, "page 2147483647")]
    [InlineData(int.MinValue, "page -2147483648")]
    public void An_unknown_page_is_named_by_its_number(int page, string name)
    {
        // Never page 0 or 13 here: nobody knows what they draw, and plan 05 may name them if its first task finds them
        // drawn. Until then they are named like any other number; if plan 05 names them, it adds the test that pins
        // their names.
        Assert.Equal(name, FenixChannelSdPages.NameOf(page));
    }

    // ---- The dispatch -----------------------------------------------------------------------------

    // The pages whose composer has not landed, each with the plan that implements it, and then numbers that are no page
    // the dispatch knows. A plan that lands a composer takes its page's line out of this list in the same commit and
    // tests the page in its own file, where the page gives rows.
    [Theory]
    [InlineData(1)]     // APU, plan 06
    [InlineData(2)]     // BLEED, plan 06
    [InlineData(3)]     // PRESS, plan 06
    [InlineData(4)]     // COND, plan 06
    [InlineData(5)]     // CRUISE, plan 06
    [InlineData(6)]     // DOOR, plan 06
    [InlineData(7)]     // ELEC, plan 05
    [InlineData(8)]     // ENG, plan 05
    [InlineData(9)]     // F/CTL, plan 06
    [InlineData(10)]    // FUEL, plan 05
    [InlineData(11)]    // HYD, plan 05
    [InlineData(12)]    // WHEEL, plan 06
    [InlineData(14)]    // STATUS, plan 05
    [InlineData(0)]     // not known to be drawn
    [InlineData(13)]    // not known to be drawn
    [InlineData(42)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_page_with_no_plan_yet_gives_null(int page)
    {
        FenixChannelRows? rows = FenixChannelSdPages.Compose(page, Context());

        Assert.Null(rows);
    }

    // ---- The valve states -------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "open")]
    [InlineData(1, "closed")]
    [InlineData(2, "in transit")]
    [InlineData(3, "failed open")]
    [InlineData(4, "failed closed")]
    [InlineData(5, "XX")]               // DataInvalid, which the SD draws as crosses
    [InlineData(9, "not available")]    // no state the contract has
    public void Each_valve_state_has_one_word(int state, string word)
    {
        Assert.Equal(word, FenixChannelSdPages.ValveWord(state));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_number_that_is_no_valve_state_is_not_available(int state)
    {
        Assert.Equal("not available", FenixChannelSdPages.ValveWord(state));
    }

    // ---- Culture ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Names_and_words_are_the_same_in_every_culture(string culture)
    {
        // A page nobody has named reads as its number, and a negative number is written with the PC's minus sign where
        // the culture has its own (sv-SE writes U+2212): the name is spoken, so it is written in the invariant culture.
        string[] spoken = TestCulture.Run(culture, () => new[]
        {
            FenixChannelSdPages.NameOf(9),
            FenixChannelSdPages.NameOf(42),
            FenixChannelSdPages.NameOf(-1),
            FenixChannelSdPages.NameOf(int.MinValue),
            FenixChannelSdPages.ValveWord(2),
            FenixChannelSdPages.ValveWord(5),
            FenixChannelSdPages.ValveWord(-1),
        });

        Assert.Equal(
            new[] { "F/CTL", "page 42", "page -1", "page -2147483648", "in transit", "XX", "not available" },
            spoken);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    // The context a page is composed from. The dispatch hands it on without reading it, so this is the least that
    // builds: an empty RemoteData, a frame with no data, an unknown Fenix, and a report that drops what it is told.
    private static FenixChannelSdPageContext Context() => new(
        FenixChannelPush.From(ReadOnlyMemory<byte>.Empty)!, FenixChannelFrame.NoData, FenixVariant.Unknown, _ => { });
}
