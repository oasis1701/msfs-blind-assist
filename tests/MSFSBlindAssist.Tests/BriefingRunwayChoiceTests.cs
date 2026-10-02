// tests/MSFSBlindAssist.Tests/BriefingRunwayChoiceTests.cs
using MSFSBlindAssist.Navigation.Briefing;

namespace MSFSBlindAssist.Tests;

public class BriefingRunwayChoiceTests
{
    [Fact]
    public void SayIntentions_runway_wins_and_the_note_names_both()
    {
        // Live KMEM (2026-09-26): SayIntentions assigned 36L; SimBrief planned 18R, a runway it was not using.
        var r = BriefingRunwayChoice.Choose("18R", "36L", siIsThisFlight: true);
        Assert.Equal("36L", r.Runway);
        Assert.Equal("runway 36L is the runway SayIntentions assigned; the flight plan names 18R", r.Note);
    }

    [Fact]
    public void The_same_runway_keeps_the_flight_plan_spelling_and_says_SayIntentions_agrees()
    {
        var r = BriefingRunwayChoice.Choose("08L", "8L", siIsThisFlight: true);
        Assert.Equal("08L", r.Runway);
        Assert.Equal("SayIntentions has assigned this runway too", r.Note);
        Assert.Equal(BriefingRunwayChoice.AgreesNote, r.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void With_no_SayIntentions_runway_the_flight_plan_runway_stands_with_no_note(string? si)
    {
        var r = BriefingRunwayChoice.Choose("18R", si, siIsThisFlight: true);
        Assert.Equal("18R", r.Runway);
        Assert.Null(r.Note);
    }

    [Fact]
    public void Another_flight_s_runway_is_ignored()
    {
        var r = BriefingRunwayChoice.Choose("18R", "36L", siIsThisFlight: false);
        Assert.Equal("18R", r.Runway);
        Assert.Null(r.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void With_no_flight_plan_runway_SayIntentions_supplies_it(string? plan)
    {
        var r = BriefingRunwayChoice.Choose(plan, "36L", siIsThisFlight: true);
        Assert.Equal("36L", r.Runway);
        Assert.Equal("runway 36L is the runway SayIntentions assigned; the flight plan names no runway", r.Note);
    }

    [Fact]
    public void Nothing_anywhere_is_an_empty_runway_with_no_note()
    {
        var r = BriefingRunwayChoice.Choose(null, null, siIsThisFlight: false);
        Assert.Equal("", r.Runway);
        Assert.Null(r.Note);
    }

    [Fact]
    public void A_compass_point_runway_is_matched_as_a_runway()
    {
        // 204 fs2024 runway ends are compass points (N/S/E/W/NE/…); the match must read them, not only numbers.
        Assert.Equal(BriefingRunwayChoice.AgreesNote, BriefingRunwayChoice.Choose("N", "N", siIsThisFlight: true).Note);
        var r = BriefingRunwayChoice.Choose("N", "S", siIsThisFlight: true);
        Assert.Equal("S", r.Runway);
        Assert.Equal("runway S is the runway SayIntentions assigned; the flight plan names N", r.Note);
    }
}
