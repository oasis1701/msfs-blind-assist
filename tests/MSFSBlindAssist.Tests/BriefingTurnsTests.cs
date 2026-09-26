// tests/MSFSBlindAssist.Tests/BriefingTurnsTests.cs
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Navigation.Briefing;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

public class BriefingTurnsTests
{
    /// <summary>A route from <paramref name="start"/> through each leg's end point; the leg's name is the taxiway of the
    /// segment that ends there ("" = unnamed). Metres east/north of the fixture base.</summary>
    private static List<TaxiRouteSegment> Route((double E, double N) start, params (string Name, double E, double N)[] legs)
    {
        var segments = new List<TaxiRouteSegment>();
        var from = new TaxiNode { NodeId = 0, Latitude = Lat(start.N), Longitude = Lon(start.E) };
        for (int i = 0; i < legs.Length; i++)
        {
            var to = new TaxiNode { NodeId = i + 1, Latitude = Lat(legs[i].N), Longitude = Lon(legs[i].E) };
            segments.Add(new TaxiRouteSegment
            {
                FromNode = from, ToNode = to, TaxiwayName = legs[i].Name,
                DistanceMeters = TaxiGraph.FastDistanceMeters(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
                BearingDegrees = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude),
            });
            from = to;
        }
        return segments;
    }

    [Fact]
    public void A_right_angle_left_onto_the_next_taxiway()
        => Assert.Equal(new string?[] { null, "left" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", -300, 300))));

    [Fact]
    public void Small_bends_that_add_up_to_a_right_angle_are_one_right_turn()
    {
        // Navdata splits real turns into small bends: here 30° + 30° + 30° over 45 m. The junction where the name changes
        // bends only 30° ("slight right" by itself); the stretch sums all three.
        var route = Route((0, 0), ("A", 0, 300), ("B", 7.5, 312.99), ("B", 20.49, 320.49), ("B", 35.49, 320.49), ("B", 235.49, 320.49));
        Assert.Equal(new string?[] { null, "right" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void A_gentle_bend_is_slight()
        => Assert.Equal(new string?[] { null, "slight right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 150, 559.81))));

    [Fact]
    public void Two_turns_close_together_do_not_blend()
    {
        // B is 40 m long: each change reaches only halfway into it, so right-then-left is not read as straight ahead.
        var route = Route((0, 0), ("A", 0, 300), ("B", 40, 300), ("C", 40, 600));
        Assert.Equal(new string?[] { null, "right", "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void A_straight_continuation_says_straight_ahead()
        => Assert.Equal(new string?[] { null, "straight ahead" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 0, 600))));

    [Fact]
    public void An_unnamed_connector_between_two_taxiways_is_part_of_the_turn()
        => Assert.Equal(new string?[] { null, "right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("", 30, 300), ("B", 330, 300))));

    [Fact]
    public void A_turn_back_on_itself_is_sharp()
        => Assert.Equal(new string?[] { null, "sharp right" },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("A", 0, 300), ("B", 150, 40.19))));

    [Fact]
    public void The_first_taxiway_never_has_a_turn()
        => Assert.Equal(new string?[] { null },
            BriefingTurns.TaxiwayTurns(Route((0, 0), ("", 0, 50), ("A", 300, 50))));

    [Fact]
    public void A_sub_metre_segment_is_skipped()
    {
        // A 0.3 m stub pointing east between north-bound A and west-bound B is a point, not a direction.
        var route = Route((0, 0), ("A", 0, 300), ("B", 0.3, 300), ("B", -300, 300));
        Assert.Equal(new string?[] { null, "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void Turns_align_with_the_taxiway_list_on_a_route_that_returns_to_a_taxiway()
    {
        var route = Route((0, 0), ("A", 0, 300), ("B", 300, 300), ("A", 300, 600));
        Assert.Equal(new[] { "A", "B", "A" }, RouteTaxiwaySequence.DistinctConsecutive(route));
        Assert.Equal(new string?[] { null, "right", "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void An_unnamed_gap_inside_one_taxiway_does_not_split_it()
    {
        var route = Route((0, 0), ("A", 0, 300), ("", 0, 310), ("A", 0, 600), ("B", -300, 600));
        Assert.Equal(new[] { "A", "B" }, RouteTaxiwaySequence.DistinctConsecutive(route));
        Assert.Equal(new string?[] { null, "left" }, BriefingTurns.TaxiwayTurns(route));
    }

    [Fact]
    public void The_turn_into_the_stand_is_measured_from_the_last_taxiway()
    {
        Assert.Equal("right", BriefingTurns.StandTurn(Route((300, 0), ("A", 0, 0), ("", 0, 150))));     // west, then north
        Assert.Equal("left", BriefingTurns.StandTurn(Route((0, 0), ("A", 300, 0), ("", 300, 150))));    // east, then north
    }

    [Fact]
    public void No_stand_turn_when_the_route_ends_on_a_taxiway()
        => Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("A", 0, 300), ("B", -300, 300))));

    [Fact]
    public void A_straight_run_into_the_stand_gives_no_stand_turn()
        => Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("A", 0, 300), ("", 0, 400))));

    [Fact]
    public void No_route_means_no_turns()
    {
        Assert.Empty(BriefingTurns.TaxiwayTurns(null));
        Assert.Empty(BriefingTurns.TaxiwayTurns(new List<TaxiRouteSegment>()));
        Assert.Empty(BriefingTurns.TaxiwayTurns(Route((0, 0), ("", 0, 300))));
        Assert.Null(BriefingTurns.StandTurn(null));
        Assert.Null(BriefingTurns.StandTurn(Route((0, 0), ("", 0, 300))));
    }

    [Theory]
    [InlineData(0, "straight ahead")]
    [InlineData(19.9, "straight ahead")]
    [InlineData(-19.9, "straight ahead")]
    [InlineData(20, "slight right")]
    [InlineData(-59.9, "slight left")]
    [InlineData(60, "right")]
    [InlineData(-60, "left")]
    [InlineData(119.9, "right")]
    [InlineData(120, "sharp right")]
    [InlineData(-179, "sharp left")]
    public void Words_follow_live_guidance_s_lines(double degrees, string words)
        => Assert.Equal(words, BriefingTurns.Words(degrees));
}
