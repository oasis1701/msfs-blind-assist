using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Readies an airport's online and scenery data BEFORE the pilot asks for it: the online taxiway
/// names (OSM and apt.dat) and the surroundings catalog, whose build starts the OSM buildings fetch
/// and the scenery scan. Two moments owe it:
/// <list type="bullet">
/// <item><see cref="AtCurrentAirport"/> — on the ground at an airport (asked on connect and after a
/// flight or aircraft load). In the air the airport below is not the pilot's.</item>
/// <item><see cref="AtDestination"/> — a destination runway was chosen (Shift+D), on the ground or
/// in the air.</item>
/// </list>
/// Names are fetched once per airport, claimed in the SAME set the other name prefetches use (taxi
/// form, ILS and visual guidance, landing-exit planner), and only while online taxi data is switched
/// on. The surroundings build is asked every time: the catalog cache answers a fresh catalog at
/// once, joins a running build and rebuilds a stale one, and each tier obeys its own setting.
/// Everything it starts runs off the UI thread; nothing it calls may throw out of it.
/// </summary>
public sealed class AirportWarmUp
{
    private readonly Func<string, bool> _claimNames;
    private readonly Func<bool> _onlineNamesEnabled;
    private readonly Action<string> _prefetchNames;
    private readonly Action<string> _buildSurroundings;

    public AirportWarmUp(Func<string, bool> claimNames, Func<bool> onlineNamesEnabled,
                         Action<string> prefetchNames, Action<string> buildSurroundings)
    {
        _claimNames = claimNames; _onlineNamesEnabled = onlineNamesEnabled;
        _prefetchNames = prefetchNames; _buildSurroundings = buildSurroundings;
    }

    /// <summary>The airport the aircraft is at — warmed only on the ground.</summary>
    public void AtCurrentAirport(string? icao, bool onGround)
    {
        if (onGround) Warm(icao, "current airport");
    }

    /// <summary>The destination airport — warmed whatever the aircraft is doing.</summary>
    public void AtDestination(string? icao) => Warm(icao, "destination");

    private void Warm(string? icao, string why)
    {
        if (string.IsNullOrWhiteSpace(icao)) return;
        try
        {
            // Claimed only when a fetch is actually made, so switching online data on later still
            // owes this airport its names.
            if (_onlineNamesEnabled() && _claimNames(icao)) _prefetchNames(icao);
        }
        catch (Exception ex) { Log.Warn("Surroundings", $"warm-up of {icao} taxiway names failed: {ex.Message}"); }
        try { _buildSurroundings(icao); }
        catch (Exception ex) { Log.Warn("Surroundings", $"warm-up of {icao} surroundings failed: {ex.Message}"); }
        Log.Debug("Surroundings", $"warm-up: {icao} ({why})");
    }
}
