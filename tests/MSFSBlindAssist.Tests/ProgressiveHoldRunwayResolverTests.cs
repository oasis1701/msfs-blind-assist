// The runway a Progressive Taxi leg holds short of. PR #247 review R13: a NAMED holding point was
// mapped to any runway whose centreline passed within 150 m — so an INTERMEDIATE hold on a parallel
// taxiway became a "runway hold", armed the runway watch and was called the departure queue. The form
// dropped the point's kind; the kind now travels with the terminator.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ProgressiveHoldRunwayResolverTests
{
    private static readonly TaxiGraph.RunwayCenterline[] Runways =
    {
        new() { Name1 = "09", Name2 = "27", Lat1 = 50.0, Lon1 = 0.0, Lat2 = 50.0, Lon2 = 0.042,
                HeadingDeg1 = 90, HalfWidthMeters = 22.5 },
    };

    private const double NearLat = 50.0 + 100 / 110540.0;   // 100 m off the centreline
    private const double FarLat = 50.0 + 500 / 110540.0;    // 500 m off
    private const double Lon = 0.01;                         // the 09 half

    private static ProgressiveTerminator Named(string kind) => new(ProgressiveTerminatorType.HoldAtNamedPoint, "A2", kind);

    private static string? R(ProgressiveTerminator? t, string? label = null, double? lat = NearLat, double? lon = Lon)
        => ProgressiveHoldRunwayResolver.Resolve(t, label, lat, lon, Runways);

    [Fact]
    public void Hold_short_of_runway_is_its_target()
    {
        Assert.Equal("09", R(new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, "09")));
        Assert.Null(R(new ProgressiveTerminator(ProgressiveTerminatorType.HoldShortRunway, "")));
    }

    [Fact]
    public void An_intermediate_hold_is_never_a_runway_hold()
    {
        Assert.Null(R(Named("intermediate")));
        Assert.Null(R(Named("Intermediate"), label: "A2, Runway 27"));
    }

    [Fact]
    public void The_nodes_own_label_names_the_runway()
    {
        Assert.Equal("27", R(Named(""), label: "A2, Runway 27"));
        Assert.Equal("27", R(Named("runway"), label: "A2, Runway 27"));
    }

    [Theory]
    [InlineData("runway")]
    [InlineData("ILS")]
    [InlineData("ils")]
    public void A_runway_or_ils_hold_without_a_label_falls_back_to_the_nearest_centreline(string kind)
        => Assert.Equal("09", R(Named(kind)));

    [Fact]
    public void An_unknown_kind_without_a_label_is_not_guessed()
        => Assert.Null(R(Named("")));

    [Fact]
    public void The_fallback_stays_within_150_m()
        => Assert.Null(R(Named("runway"), lat: FarLat));

    [Fact]
    public void Without_a_resolved_node_there_is_no_geometry_to_use()
        => Assert.Null(R(Named("runway"), lat: null, lon: null));

    [Theory]
    [InlineData(ProgressiveTerminatorType.HoldShortTaxiway)]
    [InlineData(ProgressiveTerminatorType.AfterCrossingRunway)]
    [InlineData(ProgressiveTerminatorType.EndOfTaxiway)]
    public void Other_terminators_hold_short_of_no_runway(ProgressiveTerminatorType type)
        => Assert.Null(R(new ProgressiveTerminator(type, "09"), label: "A2, Runway 27"));

    [Fact]
    public void No_terminator_no_runway()
        => Assert.Null(R(null));

    [Fact]
    public void The_kind_defaults_to_empty_and_is_trimmed()
    {
        Assert.Equal("", new ProgressiveTerminator(ProgressiveTerminatorType.HoldAtNamedPoint, "A2").HoldingPointKind);
        Assert.Equal("runway", Named(" runway ").HoldingPointKind);
    }
}
