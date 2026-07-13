using MSFSBlindAssist.SimConnect;   // SimConnectManager.AircraftPosition (namespace is NOT
                                    // in GlobalUsings; precedent: ActiveSkyClient.cs)

namespace MSFSBlindAssist.Services;

/// <summary>
/// Location context for en-route advisories (spec 2026-07-13). Compose() is the
/// pure core; ComputeLocationsAsync() is the thin I/O shell shared by the Weather
/// Radar box and the MainForm announcer. Additive-only: every failure path yields
/// "no phrase", never an exception, and never blocks an announcement.
/// </summary>
internal static class RouteAdvisoryLocator
{
    /// <summary>Pure: geometry + probe verdict → §3 phrase (null = no line).</summary>
    internal static string? Compose(IReadOnlyList<(double Lat, double Lon)>? vertices,
        bool probeMatched, double lat, double lon, double trueHeadingDeg, bool spoken)
    {
        if (probeMatched)
            return ActiveSkyFormatting.BuildLocationPhrase(null, inside: true, behind: false, spoken);
        if (vertices == null) return null;
        if (AdvisoryGeometry.IsInside(vertices, lat, lon))
            return ActiveSkyFormatting.BuildLocationPhrase(null, inside: true, behind: false, spoken);
        var (dist, brg) = AdvisoryGeometry.NearestVertex(vertices, lat, lon);
        return ActiveSkyFormatting.BuildLocationPhrase(
            dist, inside: false, AdvisoryGeometry.IsBehind(brg, trueHeadingDeg), spoken);
    }

    /// <summary>One positional probe + per-advisory tier-1/tier-2 geometry →
    /// key → phrase (OrdinalIgnoreCase). Empty dictionary when the position is
    /// unusable. Recomputed per pass — the aircraft moves (spec §7).</summary>
    internal static async Task<Dictionary<string, string>> ComputeLocationsAsync(
        ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos, bool spoken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (advisories.Count == 0) return result;
        if (pos.Latitude == 0 && pos.Longitude == 0) return result;   // spec §8.4

        double trueHeading = ((pos.HeadingMagnetic + pos.MagneticVariation) % 360 + 360) % 360;

        // One authoritative containment probe; FIRST advisory only (bundling, §13).
        string? probeKey = null;
        string? probeRaw = await client.GetPositionalAdvisoriesTextAsync(pos.Latitude, pos.Longitude);
        if (probeRaw != null)
        {
            var hits = ActiveSkyFormatting.ParseRouteAdvisories(probeRaw);
            if (hits.Count > 0) probeKey = hits[0].Key;
        }

        foreach (var a in advisories)
        {
            bool probeMatched = probeKey != null
                && string.Equals(a.Key, probeKey, StringComparison.OrdinalIgnoreCase);

            // Tier 1: the advisory's own WI polygon (body = all lines after the header).
            var vertices = AdvisoryGeometry.ParseWiPolygon(string.Join(" ", a.Lines.Skip(1)));
            // Tier 2: cached aviationweather.gov geometry by identity (US convective).
            if (vertices == null && !probeMatched && a.Identity != null)
                vertices = await WeatherService.TryGetAdvisoryPolygonAsync(a.Identity);

            string? phrase = Compose(vertices, probeMatched,
                pos.Latitude, pos.Longitude, trueHeading, spoken);
            if (phrase != null) result[a.Key] = phrase;
        }
        return result;
    }
}
