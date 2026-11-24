// Characterization tests for FenixChannelSdPages, what every page of the Fenix SD shares: the name of each page, the
// one word for a valve state, and the dispatch from a page number to the composer that gives the page its rows. Pure
// logic. Plans 05 and 06 implement the page seams, in parallel (plan 06 once their in-sim checks have passed, plan 05
// with its pages, inert until its window's key is switched on), so the dispatch tests follow the seams as they land: a
// page whose seam exists is its plan's to test, and every other page must give no rows. Every number is written out
// here, never read from FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: the SD's pages are numbered as the frame's FwcPage carries them, 1 APU to 12 WHEEL and 14 STATUS;
// pages 0 and 13 are not known to be drawn. A page is named as the SD draws it, and a number that is none of those is
// "page <n>", written in the invariant culture. A valve state has one word, "XX" for the data invalid that the SD draws
// as crosses, and "not available" for a number that is no state. A page whose composer has not landed gives no rows at
// all (null, not an empty list), and the SD window then shows the page's name and the bottom line alone.

using System.Reflection;
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

    // The page seams Compose calls: the number of each listed page and the name of its partial void. Plan 06 implements
    // the seams of pages 1 to 6, 9 and 12, and plan 05 those of pages 7, 8, 10, 11 and 14.
    private static readonly (int Page, string Seam)[] Seams =
    [
        (1, "ComposeApu"),      // plan 06
        (2, "ComposeBleed"),    // plan 06
        (3, "ComposePress"),    // plan 06
        (4, "ComposeCond"),     // plan 06
        (5, "ComposeCruise"),   // plan 06
        (6, "ComposeDoor"),     // plan 06
        (7, "ComposeElec"),     // plan 05
        (8, "ComposeEng"),      // plan 05
        (9, "ComposeFctl"),     // plan 06
        (10, "ComposeFuel"),    // plan 05
        (11, "ComposeHyd"),     // plan 05
        (12, "ComposeWheel"),   // plan 06
        (14, "ComposeStatus"),  // plan 05
    ];

    // The cases of A_page_with_no_plan_yet_gives_null. They follow the seams as they land, so that plans 05 and 06,
    // which run in parallel and land a seam at a time, never have to edit this test. A partial void with no
    // implementation is removed by the compiler and leaves no method in the assembly, so a seam that reflection finds
    // is a seam whose plan has landed: its page is left out here, because that plan tests the page's rows. Every other
    // listed page must still give null, and so must every number that is no page, always. A seam renamed by mistake
    // shows up as a page that gives rows while the table above says its plan has not landed.
    public static IEnumerable<object[]> PagesWithNoPlanYet()
    {
        foreach ((int page, string seam) in Seams)
        {
            if (FindSeam(seam).Length == 0) yield return [page];
        }

        // Pages 0 and 13 are not known to be drawn; the rest are beyond the pages.
        foreach (int page in new[] { 0, 13, 42, -1, int.MaxValue, int.MinValue })
        {
            yield return [page];
        }
    }

    [Theory]
    [MemberData(nameof(PagesWithNoPlanYet))]
    public void A_page_with_no_plan_yet_gives_null(int page)
    {
        FenixChannelRows? rows = FenixChannelSdPages.Compose(page, Context());

        Assert.Null(rows);
    }

    [Fact]
    public void Each_page_seam_is_absent_or_has_the_exact_signature()
    {
        // A seam a plan has implemented has the shape Compose calls it with, and is the only method of that name:
        // static void ComposeX(FenixChannelSdPageContext ctx, ref FenixChannelRows? rows). Another shape under the same
        // name (an overload) is not the seam, yet the cases above would take it for one and leave its page out.
        var wrong = new List<string>();
        foreach (string seam in Seams.Select(entry => entry.Seam))
        {
            MethodInfo[] found = FindSeam(seam);
            if (found.Length == 0) continue;

            if (found.Length != 1 || !HasSeamSignature(found[0]))
            {
                string shapes = string.Join(" | ", found.Select(method => method.ToString()));
                wrong.Add($"{seam} ({found.Length} found: {shapes})");
            }
        }

        Assert.True(wrong.Count == 0, "A page seam has another shape than Compose calls: " + string.Join("; ", wrong));
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

    // The methods of FenixChannelSdPages with this name: none for a seam whose plan has not landed.
    private static MethodInfo[] FindSeam(string name) => typeof(FenixChannelSdPages)
        .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Where(method => method.Name == name)
        .ToArray();

    // Whether a method has the shape Compose calls a seam with: static void, taking the context and a ref to the
    // nullable rows.
    private static bool HasSeamSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();

        return method.IsStatic
            && method.ReturnType == typeof(void)
            && parameters.Length == 2
            && parameters[0].ParameterType == typeof(FenixChannelSdPageContext)
            && parameters[1].ParameterType == typeof(FenixChannelRows?).MakeByRefType()
            && !parameters[1].IsOut;
    }
}
