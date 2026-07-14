using MSFSBlindAssist.SimConnect;   // SimConnectManager.AircraftPosition (namespace is NOT
                                    // in GlobalUsings; precedent: ActiveSkyClient.cs)
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>Structured location facts for one advisory (spec 2026-07-14 §4).
/// DistanceNm is edge-true (AdvisoryGeometry.NearestEdge) and 0 when inside;
/// null when there is no geometry. Behind is meaningful only when outside.</summary>
internal readonly record struct LocationFact(bool HasGeometry, bool Inside, double? DistanceNm, bool Behind);

/// <summary>
/// Location context for en-route advisories (spec 2026-07-13, amended 2026-07-14).
/// ComputeFactsAsync() is the pure-ish core (one bounded HTTP probe, otherwise pure
/// geometry) that produces structured LocationFacts; ComposePhrase() is the pure
/// fact-to-phrase mapping; ComputeLocationsAsync() is the thin wrapper shared by the
/// Weather Radar box and the MainForm announcer. Additive-only: every failure path
/// yields "no fact"/"no phrase", never an exception, and never blocks an announcement.
/// </summary>
internal static class RouteAdvisoryLocator
{
    /// <summary>Pure: LocationFact → §3 phrase (null = no line). Inside always wins
    /// over distance; a no-geometry, not-inside fact yields no line.</summary>
    internal static string? ComposePhrase(LocationFact fact, bool spoken)
    {
        if (fact.Inside)
            return ActiveSkyFormatting.BuildLocationPhrase(null, inside: true, behind: false, spoken);
        if (!fact.HasGeometry) return null;
        return ActiveSkyFormatting.BuildLocationPhrase(fact.DistanceNm, inside: false, fact.Behind, spoken);
    }

    /// <summary>One positional probe + per-advisory tier-1/tier-2 geometry →
    /// key → LocationFact (OrdinalIgnoreCase). Empty dictionary when the position is
    /// unusable. Recomputed per pass — the aircraft moves (spec §7). The tier-2
    /// SIGMET feeds are refreshed LAZILY, at most ONCE per pass (only when some
    /// advisory actually needs tier-2 geometry), never once per advisory — a
    /// stalled feed refresh costs at most one bounded HTTP timeout per pass, not
    /// one per advisory (final-review Fix 2). A fact is returned for EVERY advisory
    /// key on a usable position — including no-geometry keys — so callers can
    /// distinguish "no facts computed this tick" from "computed, nothing to say".
    /// Probe match can only STRENGTHEN an inside verdict, never weaken one (probe-
    /// match loss ≠ outside — another overlapping advisory may simply have become
    /// the response's first block). Never throws: every failure path (including an
    /// unexpected exception) yields whatever partial results were already computed,
    /// so an escaping exception can never fault a caller's Task.WhenAll or silently
    /// drop announcements.</summary>
    internal static async Task<Dictionary<string, LocationFact>> ComputeFactsAsync(
        ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos)
    {
        var result = new Dictionary<string, LocationFact>(StringComparer.OrdinalIgnoreCase);
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

                if (vertices == null)
                {
                    result[a.Key] = new LocationFact(false, probeMatched, null, false);
                    continue;
                }
                bool inside = probeMatched || AdvisoryGeometry.IsInside(vertices, pos.Latitude, pos.Longitude);
                if (inside) { result[a.Key] = new LocationFact(true, true, 0, false); continue; }
                var (dist, brg) = AdvisoryGeometry.NearestEdge(vertices, pos.Latitude, pos.Longitude);
                result[a.Key] = new LocationFact(true, false, dist, AdvisoryGeometry.IsBehind(brg, trueHeading));
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Route-advisory location pass error: {ex.Message}");
        }
        return result;
    }

    /// <summary>Thin wrapper over ComputeFactsAsync for callers that only want
    /// phrases (the Shift+R box). No-geometry, not-inside keys are dropped here —
    /// ComputeFactsAsync itself returns a fact for every key.</summary>
    internal static async Task<Dictionary<string, string>> ComputeLocationsAsync(
        ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos, bool spoken)
    {
        var facts = await ComputeFactsAsync(client, advisories, pos);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, fact) in facts)
            if (ComposePhrase(fact, spoken) is { } phrase) result[key] = phrase;
        return result;
    }
}
