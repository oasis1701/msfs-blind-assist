using MSFSBlindAssist.Aircraft.A220;
using Xunit;
using static MSFSBlindAssist.Aircraft.A220.A220FmsScreenParsing;

namespace MSFSBlindAssist.Tests;

/// <summary>LGAV ARRIVALS dialog (live 2026-09-24): 32 STARs, 5 transitions, 15 approaches.</summary>
public class A220DialogListColumnTests
{
    private static (List<WinToken> Tokens, List<FmsButton> Buttons) Lgav()
    {
        var t = new List<WinToken>
        {
            new("ARRIVALS - LGAV", 64, 469, "gray"),
            new("TRANS", 65, 513, "gray"),
            new("STARS(32)", 219, 513, "gray"),
            new("TRANS(5)", 374, 513, "gray"),
            new("APPR(15)", 538, 513, "gray"),
            new("COPY TO SEC", 548, 994, "gray"),
        };
        var b = new List<FmsButton>();
        for (int i = 0; i < 32; i++) b.Add(new($"STAR{i}", 593 + 48.5 * i, 231, i == 8 ? "cyan" : "white"));
        for (int i = 0; i < 5; i++) b.Add(new($"TR{i}", 593 + 48.5 * i, 386));
        for (int i = 0; i < 15; i++) b.Add(new($"APP{i}", 593 + 48.5 * i, 550));
        b.Add(new("DEPARTURES.", 933, 80));
        b.Add(new("DONE", 988, 646));
        return (t, b);
    }

    [Fact]
    public void EveryEntryOfALongListKeepsItsColumnName()
    {
        var (t, b) = Lgav();
        foreach (var btn in b.Where(x => x.Text.StartsWith("STAR")))
            Assert.Equal("STARS", DialogColumnOf(t, b, btn, 460));
        // The approaches below the dialog's gray "COPY TO SEC" label are still approaches.
        foreach (var btn in b.Where(x => x.Text.StartsWith("APP")))
            Assert.Equal("APPR", DialogColumnOf(t, b, btn, 460));
        Assert.Equal("", DialogColumnOf(t, b, b.Single(x => x.Text == "DONE"), 460));
    }

    [Fact]
    public void ColumnMembershipGivesThePosition()
    {
        var (_, b) = Lgav();
        var col = DialogListColumn(b, b.Single(x => x.Text == "STAR8"));
        Assert.Equal(32, col.Count);
        Assert.Equal(8, col.IndexOf(b.Single(x => x.Text == "STAR8")));
        Assert.Single(DialogListColumn(b, b.Single(x => x.Text == "DONE")));
    }

    [Fact]
    public void ListHeadingCountSplits()
    {
        Assert.Equal(("STARS", 32), SplitListHeading("STARS(32)"));
        Assert.Equal(("APPR", 0), SplitListHeading("APPR"));
    }

    [Fact]
    public void BothArrivalTransitionListsAreNamedForTheirProcedure()
    {
        var (t, b) = Lgav();
        foreach (var btn in b.Where(x => x.Text.StartsWith("TR")))
            Assert.Equal("approach transition", DialogColumnOf(t, b, btn, 460));
        Assert.Equal("STAR transition", QualifyListHeading(t, t.Single(x => x.X == 65)));
    }

    [Fact]
    public void DepartureTransitionIsTheSids()
    {
        var t = new List<WinToken>
        {
            new("RWYS(2)", 60, 513, "gray"), new("SIDS(6)", 219, 513, "gray"), new("TRANS(3)", 374, 513, "gray"),
        };
        Assert.Equal("SID transition", QualifyListHeading(t, t[2]));
        Assert.Equal("SIDS", QualifyListHeading(t, t[1]));
    }

    [Fact]
    public void EveryArrivalEntryStaysAButton()
    {
        // Nothing may be lost to the column naming: every STAR, transition and approach is still a row.
        var (t, b) = Lgav();
        var tokens = new List<WinToken>(t);
        foreach (var btn in b) tokens.Add(new(btn.Text, btn.X, btn.Y, btn.Color, Clickable: true));
        var model = ParseFms(tokens);
        foreach (var btn in b)
            Assert.Contains(model.ButtonRows, r => r.Text == btn.Text);
    }
}
