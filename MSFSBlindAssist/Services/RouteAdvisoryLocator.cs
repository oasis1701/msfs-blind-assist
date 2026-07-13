using MSFSBlindAssist.SimConnect;   // SimConnectManager.AircraftPosition (namespace is NOT
                                    // in GlobalUsings; precedent: ActiveSkyClient.cs)
using MSFSBlindAssist.Utils.Logging;

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
    /// unusable. Recomputed per pass — the aircraft moves (spec §7). The tier-2
    /// SIGMET feeds are refreshed LAZILY, at most ONCE per pass (only when some
    /// advisory actually needs tier-2 geometry), never once per advisory — a
    /// stalled feed refresh costs at most one bounded HTTP timeout per pass, not
    /// one per advisory (final-review Fix 2). Never throws: every failure path
    /// (including an unexpected exception) yields whatever partial results were
    /// already computed, so an escaping exception can never fault a caller's
    /// Task.WhenAll or silently drop announcements.</summary>
    internal static async Task<Dictionary<string, string>> ComputeLocationsAsync(
        ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos, bool spoken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
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

            // Lazy, once-per-pass snapshot of the tier-2 feeds — populated on first use.
            (string Airsigmet, string Isigmet)? feeds = null;

            foreach (var a in advisories)
            {
                bool probeMatched = probeKey != null
                    && string.Equals(a.Key, probeKey, StringComparison.OrdinalIgnoreCase);

                // Tier 1: the advisory's own WI polygon (body = all lines after the header).
                var vertices = AdvisoryGeometry.ParseWiPolygon(string.Join(" ", a.Lines.Skip(1)));
                // Tier 2: cached aviationweather.gov geometry by identity (US convective).
                if (vertices == null && !probeMatched && a.Identity != null)
                {
                    feeds ??= await WeatherService.RefreshAndGetSigmetFeedsAsync();
                    vertices = (feeds.Value.Airsigmet.Length > 0
                            ? WeatherService.FindAdvisoryPolygonInGeoJson(feeds.Value.Airsigmet, a.Identity)
                            : null)
                        ?? (feeds.Value.Isigmet.Length > 0
                            ? WeatherService.FindAdvisoryPolygonInGeoJson(feeds.Value.Isigmet, a.Identity)
                            : null);
                }

                string? phrase = Compose(vertices, probeMatched,
                    pos.Latitude, pos.Longitude, trueHeading, spoken);
                if (phrase != null) result[a.Key] = phrase;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Route-advisory location pass error: {ex.Message}");
        }
        return result;
    }
}
