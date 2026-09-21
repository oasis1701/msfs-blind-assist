using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// One OPTIONAL surroundings tier, read so that its failure costs only itself.
///
/// The catalog is built from several independent sources — navdata stands, GSX terminals, OSM
/// buildings, the installed scenery package — and only the navdata tier is required (the airport
/// box and the facts line come from it). The others are additions. Read in a straight line, an
/// exception out of ANY of them left <c>BuildSurroundings</c> without a result at all, which
/// <c>SurroundingsCatalogCache</c> records as a failed build: the pilot then gets NO surroundings
/// at that airport — not even the navdata stands that were already in hand — and the cache retries
/// the same failing build every 60 s for as long as the airport is current. A mirror that times
/// out oddly, a scenery cache file someone edited, a package on a drive that went away: none of
/// those is a reason to stop naming the gate beside the aircraft.
///
/// The sequence is materialised INSIDE the try on purpose. A tier that returns a lazy sequence and
/// throws while the caller walks it is the dangerous shape — the return succeeds and the exception
/// lands in <c>AddRange</c>, outside any guard the tier itself could offer.
/// </summary>
public static class SurroundingsTier
{
    /// <summary>What a tier produced, and whether it FAILED rather than simply having nothing to
    /// add. Both are the same empty list and they are not the same fact: an airport with no scenery
    /// package is completely described without one, while a package that would not read leaves a
    /// catalog that must expire and be built again rather than stand as the answer.</summary>
    public readonly record struct TierRead(IReadOnlyList<AirportFeature> Features, bool Failed);

    /// <summary>What <paramref name="read"/> produced, or nothing — and which of the two.</summary>
    public static TierRead Read(string tier, string icao, Func<IEnumerable<AirportFeature>> read)
    {
        try
        {
            return new(read()?.ToList() ?? (IReadOnlyList<AirportFeature>)Array.Empty<AirportFeature>(), false);
        }
        catch (Exception ex)
        {
            // Plain Exception on purpose, and never silent: an OutOfMemoryException here is a
            // fact about one tier's workload, and the line below is how it is ever found out.
            Log.Warn("Surroundings", $"{icao}: {tier} tier failed, building without it: {ex.Message}");
            return new(Array.Empty<AirportFeature>(), true);
        }
    }
}
