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
    /// <summary>How far outside an advisory's own whole shape a positional-probe match is
    /// still believed (nm). Live 2026-10-15: ActiveSky's probe answered with a British Columbia
    /// SIGMET for an aircraft over Wisconsin, about 1,600 nm away, and answered no-hit on the
    /// SIGMET's own line; trusting it announced "Entering advisory area". The margin is wide
    /// on purpose: it only has to catch a gross misfire, and leaves room for ActiveSky drawing
    /// an area a little differently from its text (a corridor's caps and bends, FIR clipping).</summary>
    internal const double ProbeTrustMarginNm = 150;

    /// <summary>Hysteresis on <see cref="ProbeTrustMarginNm"/> (nm): a key already Inside keeps
    /// its probe match out to the margin plus this band, so a match hovering at the margin
    /// cannot alternate Enter and Leave (the tracker's RearmBandNm, same width).</summary>
    internal const double ProbeTrustBandNm = 10;

    /// <summary>Pure: one advisory's LocationFact from its geometry and the probe verdict.
    /// No geometry: the probe alone decides Inside. Inside the shape: Inside. Outside it: the
    /// probe still makes it Inside (WX-14), unless <paramref name="wholeShape"/> (the vertices
    /// are the advisory's own whole shape: AdvisoryGeometry.ParseWiPolygon, or a single tier-2
    /// polygon) and the aircraft is more than <see cref="ProbeTrustMarginNm"/> outside it (plus
    /// <see cref="ProbeTrustBandNm"/> when <paramref name="wasInside"/>): then the probe answer is
    /// wrong and the fact reads the edge distance. A ring across 180° never overrules the probe:
    /// the plain lat/lon math cannot measure it (AdvisoryGeometry.CrossesAntimeridian).</summary>
    internal static LocationFact BuildFact(IReadOnlyList<(double Lat, double Lon)>? vertices, bool wholeShape,
        bool probeMatched, double lat, double lon, double trueHeadingDeg, bool wasInside = false)
    {
        if (vertices == null) return new LocationFact(false, probeMatched, null, false);
        if (AdvisoryGeometry.IsInside(vertices, lat, lon)) return new LocationFact(true, true, 0, false);
        if (probeMatched && (!wholeShape || AdvisoryGeometry.CrossesAntimeridian(vertices)))
            return new LocationFact(true, true, 0, false);
        var (dist, brg) = AdvisoryGeometry.NearestEdge(vertices, lat, lon);
        if (probeMatched && dist <= ProbeTrustMarginNm + (wasInside ? ProbeTrustBandNm : 0))
            return new LocationFact(true, true, 0, false);
        return new LocationFact(true, false, dist, AdvisoryGeometry.IsBehind(brg, trueHeadingDeg));
    }

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
    /// the response's first block), except far outside a whole own shape (BuildFact,
    /// WX-14). <paramref name="wasInside"/> (the announcer passes its tracker's
    /// RouteAdvisoryProximityTracker.IsInside) widens that guard by ProbeTrustBandNm for a key
    /// already Inside. Never throws: every failure path (including an
    /// unexpected exception) yields whatever partial results were already computed,
    /// so an escaping exception can never fault a caller's Task.WhenAll or silently
    /// drop announcements.</summary>
    internal static Task<Dictionary<string, LocationFact>> ComputeFactsAsync(
        ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos, Func<string, bool>? wasInside = null)
        => ComputeFactsAsync(client.GetPositionalAdvisoriesTextAsync, advisories, pos, wasInside);

    /// <summary><see cref="ComputeFactsAsync(ActiveSkyClient, IReadOnlyList{ActiveSkyFormatting.RouteAdvisory}, SimConnectManager.AircraftPosition, Func{string, bool}?)"/>
    /// with the positional probe (lat, lon → raw answer, null on failure) as a seam, so a test can
    /// feed a recorded answer.</summary>
    internal static async Task<Dictionary<string, LocationFact>> ComputeFactsAsync(
        Func<double, double, Task<string?>> positionalProbe,
        IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories,
        SimConnectManager.AircraftPosition pos, Func<string, bool>? wasInside = null)
    {
        var result = new Dictionary<string, LocationFact>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (advisories.Count == 0) return result;
            if (pos.Latitude == 0 && pos.Longitude == 0) return result;   // spec §8.4

            double trueHeading = ((pos.HeadingMagnetic + pos.MagneticVariation) % 360 + 360) % 360;

            // One authoritative containment probe; FIRST advisory only (bundling, §13).
            string? probeKey = null;
            string? probeRaw = await positionalProbe(pos.Latitude, pos.Longitude);
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
                // Only a whole shape can overrule a probe match (BuildFact).
                var vertices = AdvisoryGeometry.ParseWiPolygon(
                    string.Join(" ", a.Lines.Skip(1)), out bool wholeShape);
                // Tier 2: cached aviationweather.gov geometry by identity (US convective), also
                // for a probe-matched key, whose single polygon can overrule a far-off match.
                if (vertices == null && a.Identity != null)
                {
                    feeds ??= await WeatherService.RefreshAndGetSigmetFeedsAsync();
                    bool tier2Whole = false;
                    vertices = (feeds.Value.Airsigmet.Length > 0
                            ? WeatherService.FindAdvisoryPolygonInGeoJson(feeds.Value.Airsigmet, a.Identity, out tier2Whole)
                            : null)
                        ?? (feeds.Value.Isigmet.Length > 0
                            ? WeatherService.FindAdvisoryPolygonInGeoJson(feeds.Value.Isigmet, a.Identity, out tier2Whole)
                            : null);
                    wholeShape = vertices != null && tier2Whole;
                }

                var fact = BuildFact(vertices, wholeShape, probeMatched, pos.Latitude, pos.Longitude, trueHeading,
                    wasInside?.Invoke(a.Key) ?? false);
                if (probeMatched && !fact.Inside)
                    Log.Debug("Services", $"Route-advisory probe match ignored: \"{a.Key}\" is "
                        + $"{(int)Math.Round(fact.DistanceNm ?? 0)} nm outside its own shape");
                result[a.Key] = fact;
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
