using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Surroundings;

/// <summary>
/// Reads one optional surroundings tier (GSX, OSM, scenery) so that its failure costs only itself.
/// Only navdata is required; an exception from any other tier used to fail the whole build, taking
/// the navdata stands with it and retrying every 60 s. The sequence is materialised inside the try,
/// because a lazy tier that throws while being walked would escape the guard.
/// </summary>
public static class SurroundingsTier
{
    /// <summary>What a tier produced, and whether it failed rather than simply having nothing — a
    /// failed read makes the catalog degraded so it is rebuilt.</summary>
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
            // Any exception, never silently: this line is the only trace of a failed tier.
            Log.Warn("Surroundings", $"{icao}: {tier} tier failed, building without it: {ex.Message}");
            return new(Array.Empty<AirportFeature>(), true);
        }
    }
}
