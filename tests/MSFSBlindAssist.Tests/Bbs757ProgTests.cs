using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// D and Shift+D (plan 06, <see cref="Bbs757Prog"/>): the destination and the top of climb or descent read off
/// the first officer's CDU PROG page 1. The lines are those read at the gate on 2026-10-25 (a route to KMIA)
/// and the T/D row of a flight of 2026-10-26 ("1633Z/ 10NM").
/// </summary>
public class Bbs757ProgTests
{
    private const string Title1 = "   4RK PROGRESS     1/3";
    private const string Title2 = "   4RK PROGRESS     2/3";

    private static Bbs757CduScreen Screen(string title, params string[] rows)
    {
        var lines = new string[Bbs757CduFrame.Rows];
        lines[0] = title;
        for (int i = 1; i < lines.Length; i++) lines[i] = i - 1 < rows.Length ? rows[i - 1] : "";
        return new Bbs757CduScreen(lines);
    }

    /// <summary>Session 8's PROG page 1, the top line row 8 as given.</summary>
    private static Bbs757CduScreen PageOne(string topLabel = " ECON SPD         TO T/C", string topData = "299/.785     0218Z/ 56NM") => Screen(Title1,
        " TO      DTG  ETA  FUEL",
        "(INTC)    1  0210Z 13.4",
        " NEXT",
        "GRITZ     6  0211Z 12.8",
        " DEST",
        "KMIA    568  0332Z   6.1",
        topLabel,
        topData,
        "",
        "",
        " POS REPORT     POS REF");

    private static Bbs757CduScreen PageTwo() => Screen(Title2,
        " WIND          XTK ERROR",
        "270/ 15KT        L0.1NM",
        "", "", "", "", "", "", "", "", "");

    [Fact]
    public void Destination_from_prog()
    {
        Assert.Equal(new Bbs757ProgDest("KMIA", 568, "0332"), Bbs757Prog.Destination(PageOne()));
    }

    [Fact]
    public void Top_of_climb_from_prog()
    {
        Assert.Equal(new Bbs757ProgTop("T/C", 56, "0218"), Bbs757Prog.TopOf(PageOne()));
    }

    [Fact]
    public void Top_of_descent_variant()
    {
        // Flight 356 in cruise: "TO T/D" with "1633Z/ 10NM".
        Assert.Equal(new Bbs757ProgTop("T/D", 10, "1633"),
            Bbs757Prog.TopOf(PageOne(" ECON SPD         TO T/D", ".735         1633Z/ 10NM")));
    }

    [Fact]
    public void Step_climb_holds_the_top_line()
    {
        // 2026-10-27, DAL356 to KLAX in cruise: a planned step climb takes the line, four-digit miles.
        var page = PageOne("   ECON SPD    TO STEP CLB", ".801       1740Z/ 1219NM");

        Assert.Equal(new Bbs757ProgTop("STEP CLB", 1219, "1740"), Bbs757Prog.TopOf(page));
        Assert.Equal("Step climb in 1219 miles, 17:40 Zulu, top of descent after it",
            Bbs757Prog.TopPhrase(Bbs757Prog.TopOf(page)));
    }

    [Fact]
    public void No_top_line()
    {
        // After the top of descent the line goes: there is no top ahead.
        Assert.Null(Bbs757Prog.TopOf(PageOne(" ECON SPD", ".735")));
        Assert.Equal(new Bbs757ProgDest("KMIA", 568, "0332"), Bbs757Prog.Destination(PageOne(" ECON SPD", ".735")));
    }

    [Fact]
    public void Page_two_is_progress_but_not_page_one()
    {
        Assert.True(Bbs757Prog.IsProgress(PageOne()));
        Assert.True(Bbs757Prog.IsProgress(PageTwo()));
        Assert.False(Bbs757Prog.IsProgress(Screen("  ACT RTE 1 LEGS     1/7")));
        Assert.Null(Bbs757Prog.Destination(PageTwo()));
        Assert.Null(Bbs757Prog.TopOf(PageTwo()));
        Assert.True(Bbs757Prog.IsPageOne(PageOne()));
        Assert.False(Bbs757Prog.IsPageOne(PageTwo()));
    }

    [Fact]
    public void Phrases()
    {
        Assert.Equal("568 miles to destination KMIA, ETA 03:32 Zulu",
            Bbs757Prog.DestinationPhrase(new Bbs757ProgDest("KMIA", 568, "0332")));
        Assert.Equal("1 mile to destination KMIA", Bbs757Prog.DestinationPhrase(new Bbs757ProgDest("KMIA", 1, null)));
        Assert.Equal("No destination on the progress page", Bbs757Prog.DestinationPhrase(null));
        Assert.Equal("Top of climb in 56 miles, 02:18 Zulu", Bbs757Prog.TopPhrase(new Bbs757ProgTop("T/C", 56, "0218")));
        Assert.Equal("Top of descent in 10 miles, 16:33 Zulu", Bbs757Prog.TopPhrase(new Bbs757ProgTop("T/D", 10, "1633")));
        Assert.Equal("No top of climb or descent ahead", Bbs757Prog.TopPhrase(null));
        Assert.Equal("The first officer's CDU did not answer.", Bbs757Prog.NoCdu);
    }

    [Fact]
    public void Blank_or_odd_lines_never_throw()
    {
        var blank = Screen("");
        Assert.False(Bbs757Prog.IsProgress(blank));
        Assert.Null(Bbs757Prog.Destination(blank));
        Assert.Null(Bbs757Prog.TopOf(blank));
        // The DEST label on the last row, with no data row after it.
        var last = Screen(Title1, "", "", "", "", "", "", "", "", "", "", "", " DEST");
        Assert.Null(Bbs757Prog.Destination(last));
    }

    // ---- the page flow: PROG, then PREV until page 1 --------------------------------------------------

    /// <summary>The first officer's CDU on the PROG pages: PROG shows <see cref="OpensOn"/>, PREV steps back.</summary>
    private sealed class FakeProgCdu : IBbs757CduLink
    {
        private readonly Func<int, Bbs757CduScreen> _page;
        public FakeProgCdu(Func<int, Bbs757CduScreen> page) => _page = page;

        public int OpensOn { get; init; } = 1;
        public int? Shown { get; private set; }
        public List<string> Keys { get; } = new();
        public bool IsReadable { get; set; } = true;
        public int PendingKeyCount => 0;
        public bool IgnorePrev { get; init; }

        public void PressKey(int cdu, string key, int holdMs)
        {
            Keys.Add(key);
            if (key == Bbs757CduKeys.Prog) Shown = OpensOn;
            else if (key == Bbs757CduKeys.Prev && Shown is int p && !IgnorePrev) Shown = p == 1 ? 3 : p - 1;
        }

        public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct)
            => Task.FromResult<Bbs757CduScreen?>(Shown is int p ? _page(p) : Screen("  ACT RTE 1 LEGS     1/7"));
    }

    private static Bbs757CduScreen ProgPage(int p) => p == 1 ? PageOne() : Screen($"   4RK PROGRESS     {p}/3");

    private static Bbs757CduPages Pages(FakeProgCdu cdu) => new(cdu, (_, _) => Task.CompletedTask, () => 0);

    [Theory]
    [InlineData(1, new[] { "PROG" })]
    [InlineData(2, new[] { "PROG", "PREV" })]
    [InlineData(3, new[] { "PROG", "PREV", "PREV" })]
    public async Task Prog_opening_on_page_two_or_three_steps_back_to_page_one(int opensOn, string[] keys)
    {
        var cdu = new FakeProgCdu(ProgPage) { OpensOn = opensOn };
        var screen = await Bbs757Prog.ReadPageOneAsync(Pages(cdu), CancellationToken.None);

        Assert.NotNull(screen);
        Assert.True(Bbs757Prog.IsPageOne(screen!));
        Assert.Equal(keys, cdu.Keys);
    }

    [Fact]
    public async Task Prev_that_does_not_reach_page_one_gives_up_after_two_presses()
    {
        var cdu = new FakeProgCdu(ProgPage) { OpensOn = 3, IgnorePrev = true };
        Assert.Null(await Bbs757Prog.ReadPageOneAsync(Pages(cdu), CancellationToken.None));
        Assert.Equal(new[] { "PROG", "PREV", "PREV" }, cdu.Keys);
    }

    [Fact]
    public async Task An_unreachable_cdu_reads_nothing()
    {
        var cdu = new FakeProgCdu(ProgPage) { IsReadable = false };
        Assert.Null(await Bbs757Prog.ReadPageOneAsync(Pages(cdu), CancellationToken.None));
        Assert.Empty(cdu.Keys);
    }

    [Fact]
    public void Phrases_under_a_comma_decimal_culture()
    {
        List<string> Said() => new()
        {
            Bbs757Prog.DestinationPhrase(Bbs757Prog.Destination(PageOne())),
            Bbs757Prog.TopPhrase(Bbs757Prog.TopOf(PageOne())),
            Bbs757Prog.TopPhrase(Bbs757Prog.TopOf(PageOne(" ECON SPD         TO T/D", ".735         1633Z/ 10NM"))),
        };

        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expected = Said();
            foreach (string culture in new[] { "de-DE", "sv-SE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(expected, Said());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
