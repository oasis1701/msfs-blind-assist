using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

public class SurroundingsTierTests
{
    private static AirportFeature Feature(string name)
        => new() { Kind = FeatureKind.Hangar, Name = name, Lat = 1, Lon = 2, Source = FeatureSource.Scenery };

    [Fact]
    public void A_working_tier_passes_its_features_through_unchanged()
    {
        var one = Feature("Hangar 7");
        var two = Feature("Fire Station");
        var read = SurroundingsTier.Read("scenery", "KTIW", () => new[] { one, two });
        Assert.Equal(new[] { one, two }, read);
    }

    [Fact]
    public void A_tier_that_throws_contributes_nothing_and_never_escapes()
    {
        // The whole point: navdata and GSX features must still reach the pilot when an optional
        // tier fails. An escape here fails the WHOLE catalog build, and the cache retries it
        // every 60 s for as long as the airport is current.
        var read = SurroundingsTier.Read("scenery", "KTIW", () => throw new InvalidOperationException("disk gone"));
        Assert.Empty(read);
    }

    [Fact]
    public void A_sequence_that_throws_while_it_is_being_enumerated_is_caught_too()
    {
        // The dangerous shape: the tier returns fine and blows up later, when the caller walks it.
        // Only materialising INSIDE the try catches this.
        var read = SurroundingsTier.Read("osm", "KTIW", Lazy);
        Assert.Empty(read);

        static IEnumerable<AirportFeature> Lazy()
        {
            yield return Feature("Terminal A");
            throw new TimeoutException("mirror gave up");
        }
    }

    [Fact]
    public void A_tier_that_answers_with_nothing_at_all_is_an_empty_list_not_a_null()
    {
        Assert.Empty(SurroundingsTier.Read("gsx", "KTIW", () => Array.Empty<AirportFeature>()));
        Assert.Empty(SurroundingsTier.Read("gsx", "KTIW", () => null!));
    }
}
