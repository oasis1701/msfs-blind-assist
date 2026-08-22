# Route-Advisory Location Context Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each en-route advisory gains a derived "Location:" line (box) and spoken suffix (announce) giving distance from the aircraft, ahead/behind, and at-your-position — per the approved spec `docs/design/2026-07-13-route-advisory-location-design.md`.

**Architecture:** Pure geometry (`AdvisoryGeometry`) + pure phrasing (`ActiveSkyFormatting.BuildLocationPhrase`) + a thin orchestrator (`RouteAdvisoryLocator`) that combines three inputs: the advisory's own `WI` polygon (tier 1), an identity match into the already-cached aviationweather.gov GeoJSON (tier 2, `WeatherService`), and one authoritative ActiveSky positional probe at the aircraft's position. Both call sites (Weather Radar box, MainForm announcer) consume one `key → phrase` dictionary.

**Tech Stack:** .NET 10 / C# 13, xUnit, existing `NavigationCalculator` geodesics, `System.Text.Json`.

## Global Constraints

- Build via `dotnet build MSFSBlindAssist.sln -c Debug` (NEVER the bare csproj); tests via `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- All new pure code is `internal` (the `InternalsVisibleTo("MSFSBlindAssist.Tests")` seam exists).
- Additive-only: when anything is unresolvable, the advisory renders EXACTLY as today (no Location line, no announce suffix). Never throw, never block the UI, never drop an announcement — a hung probe adds at most its bounded 5 s timeout before the announcement fires (spec §8.2), and every failure path yields "no suffix".
- Distances in whole nm; `< 1` renders "less than one nautical mile". Box abbreviates ("nm"); spoken form spells "nautical miles". Inside wins over distance.
- Only the FIRST parsed advisory of a positional-probe response is position-matched (2026-07-13 bundling finding, weather.md §13).
- Invariant-culture formatting for every lat/lon in a URL (AS API doc requirement).
- True heading = `HeadingMagnetic + MagneticVariation` (East-positive; the inverse of `NavigationCalculator.CalculateMagneticBearing`'s documented `Magnetic = True − Variation`).
- Commit after every green task; conventional-commit messages ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: `AdvisoryGeometry.ParseWiPolygon`

**Files:**
- Create: `MSFSBlindAssist/Services/AdvisoryGeometry.cs`
- Test: `tests/MSFSBlindAssist.Tests/AdvisoryGeometryTests.cs` (create)

**Interfaces:**
- Produces: `internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body)` — null when no `WI` token or fewer than 3 distinct vertices; drops a duplicated closing vertex.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/AdvisoryGeometryTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AdvisoryGeometryTests
{
    // Live captures 2026-07-12 (weather.md §12) — spacing quirks are verbatim. MhtgBody is
    // byte-identical to RouteAdvisoriesTests.LiveMhtgBody (7 coordinate pairs); keep them in sync.
    private const string MhtgBody =
        "MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=";
    private const string YmmmBody =
        "YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=";

    [Fact]
    public void ParseWiPolygon_parses_the_live_mhtg_body()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(MhtgBody)!;
        Assert.Equal(7, v.Count);
        Assert.Equal(11 + 21 / 60.0, v[0].Lat, 6);          // N1121
        Assert.Equal(-(100 + 27 / 60.0), v[0].Lon, 6);      // W10027
        Assert.Equal(9 + 4 / 60.0, v[6].Lat, 6);            // N0904
        Assert.Equal(-(99 + 40 / 60.0), v[6].Lon, 6);       // W09940
    }

    [Fact]
    public void ParseWiPolygon_parses_southern_and_eastern_hemispheres()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(YmmmBody)!;
        Assert.Equal(6, v.Count);
        Assert.Equal(-(36 + 40 / 60.0), v[0].Lat, 6);       // S3640
        Assert.Equal(148.0, v[0].Lon, 6);                   // E14800
    }

    [Fact]
    public void ParseWiPolygon_drops_a_duplicated_closing_vertex()
    {
        // The 2026-07-13 oceanic ECHO 5 capture repeats its first vertex to close.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "FRQ TS OBS WI N3454 W07549 - N3231 W06558 - N2956 W06558 - N3454 W07549 TOP FL490")!;
        Assert.Equal(3, v.Count);
    }

    [Fact]
    public void ParseWiPolygon_ignores_coordinates_before_the_wi_token()
    {
        // VA SIGMETs carry a PSN pair before WI — only the WI polygon counts.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "VA ERUPTION MT LEWOTOLOK PSN S0816 E12330 VA CLD OBS AT 1150Z WI S0820 E12333 - S0820 E12313 - S0804 E12318")!;
        Assert.Equal(3, v.Count);
        Assert.Equal(-(8 + 20 / 60.0), v[0].Lat, 6);
    }

    [Theory]
    [InlineData("EMBD TS OBS AT 1830Z TOP FL520 MOV W 05KT NC=")]          // no WI
    [InlineData("SEV TURB FCST WI S3640 E14800 - S3340 E15000 SFC/8000FT")] // only 2 vertices
    [InlineData("")]
    public void ParseWiPolygon_returns_null_when_unusable(string body)
        => Assert.Null(AdvisoryGeometry.ParseWiPolygon(body));
}
```

- [ ] **Step 2: Run to verify RED**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AdvisoryGeometryTests"`
Expected: build error — `AdvisoryGeometry` does not exist.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Services/AdvisoryGeometry.cs
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure geometry for route-advisory location context (spec 2026-07-13 §5-6).
/// No I/O; fully characterization-tested (AdvisoryGeometryTests).
/// </summary>
internal static class AdvisoryGeometry
{
    /// <summary>[NS]ddmm [EW]dddmm degrees+minutes pair, tolerant of the live
    /// captures' spacing quirks ("W09304- N1127", double spaces).</summary>
    private static readonly Regex CoordPair = new(
        @"\b([NS])(\d{2})(\d{2})\s+([EW])(\d{3})(\d{2})\b", RegexOptions.Compiled);

    /// <summary>Extracts the "WI lat lon - lat lon - …" polygon from an ICAO-style
    /// advisory body. Coordinates BEFORE the WI token (e.g. a VA SIGMET's PSN) are
    /// ignored. Null when there is no WI token or fewer than 3 distinct vertices;
    /// a duplicated closing vertex (polygon closure) is dropped.</summary>
    internal static List<(double Lat, double Lon)>? ParseWiPolygon(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var wi = Regex.Match(body, @"\bWI\b");
        if (!wi.Success) return null;

        var verts = new List<(double Lat, double Lon)>();
        foreach (Match m in CoordPair.Matches(body, wi.Index))
        {
            double lat = int.Parse(m.Groups[2].Value) + int.Parse(m.Groups[3].Value) / 60.0;
            if (m.Groups[1].Value == "S") lat = -lat;
            double lon = int.Parse(m.Groups[5].Value) + int.Parse(m.Groups[6].Value) / 60.0;
            if (m.Groups[4].Value == "W") lon = -lon;
            verts.Add((lat, lon));
        }
        if (verts.Count > 1 && verts[0] == verts[^1]) verts.RemoveAt(verts.Count - 1);
        return verts.Count >= 3 ? verts : null;
    }
}
```

(`int.Parse` on `\d{2,3}` regex captures cannot throw; culture is irrelevant for bare digits — matches the repo's use elsewhere.)

- [ ] **Step 4: Run to verify GREEN** (same command). Expected: all `AdvisoryGeometryTests` pass.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/AdvisoryGeometry.cs tests/MSFSBlindAssist.Tests/AdvisoryGeometryTests.cs
git commit -m "feat(weather): parse WI polygons from ICAO advisory bodies

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `AdvisoryGeometry` — inside / nearest-vertex / behind

**Files:**
- Modify: `MSFSBlindAssist/Services/AdvisoryGeometry.cs`
- Test: `tests/MSFSBlindAssist.Tests/AdvisoryGeometryTests.cs` (extend)

**Interfaces:**
- Produces:
  `internal static bool IsInside(IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)` (false for < 3 vertices);
  `internal static (double DistanceNm, double BearingTrueDeg) NearestVertex(IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)`;
  `internal static bool IsBehind(double bearingToDeg, double trueHeadingDeg)` (|relative bearing| > 90).
- Consumes: `Navigation.NavigationCalculator.CalculateDistance/CalculateBearing` (nm / true degrees).

- [ ] **Step 1: Write the failing tests** (append to `AdvisoryGeometryTests`)

```csharp
    // 1°×1° square centred on (10.5, 20.5).
    private static readonly (double Lat, double Lon)[] Square =
        { (10, 20), (10, 21), (11, 21), (11, 20) };

    [Fact]
    public void IsInside_detects_containment()
    {
        Assert.True(AdvisoryGeometry.IsInside(Square, 10.5, 20.5));
        Assert.False(AdvisoryGeometry.IsInside(Square, 12.0, 20.5));
        Assert.False(AdvisoryGeometry.IsInside(new[] { (10.0, 20.0), (11.0, 21.0) }, 10.5, 20.5));
    }

    [Fact]
    public void NearestVertex_returns_distance_and_true_bearing()
    {
        // From 1° due south of the (10,20) corner: that corner is nearest,
        // ~60 nm away, bearing ~000.
        var (dist, brg) = AdvisoryGeometry.NearestVertex(Square, 9.0, 20.0);
        Assert.InRange(dist, 59, 61);
        Assert.True(brg < 1 || brg > 359);
    }

    [Theory]
    [InlineData(0, 0, false)]      // dead ahead
    [InlineData(89, 0, false)]     // just forward of abeam
    [InlineData(90, 0, false)]     // exactly abeam: strict > 90 rule (spec §5) → ahead
    [InlineData(91, 0, true)]      // just aft of abeam
    [InlineData(180, 0, true)]     // dead astern
    [InlineData(10, 350, false)]   // wrap: 20° relative
    [InlineData(170, 350, true)]   // wrap: 180° relative
    public void IsBehind_uses_relative_bearing_with_wraparound(
        double bearingTo, double heading, bool behind)
        => Assert.Equal(behind, AdvisoryGeometry.IsBehind(bearingTo, heading));
```

- [ ] **Step 2: Verify RED** (same filter). Expected: compile error — methods missing.

- [ ] **Step 3: Implement** (append to `AdvisoryGeometry`)

```csharp
    /// <summary>Ray-cast point-in-polygon on plain lat/lon (adequate at SIGMET
    /// scales; antimeridian-spanning polygons are a documented non-goal).</summary>
    internal static bool IsInside(IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)
    {
        if (vertices.Count < 3) return false;
        bool inside = false;
        for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
        {
            (double yi, double xi) = (vertices[i].Lat, vertices[i].Lon);
            (double yj, double xj) = (vertices[j].Lat, vertices[j].Lon);
            if ((yi > lat) != (yj > lat)
                && lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Distance/bearing to the nearest VERTEX — deliberately the same
    /// approximation the Nearby Advisories box uses (WeatherService.ClosestPoint),
    /// so the two boxes never disagree about one advisory's distance.</summary>
    internal static (double DistanceNm, double BearingTrueDeg) NearestVertex(
        IReadOnlyList<(double Lat, double Lon)> vertices, double lat, double lon)
    {
        double bestDist = double.MaxValue, bestBrg = 0;
        foreach (var (vLat, vLon) in vertices)
        {
            double d = Navigation.NavigationCalculator.CalculateDistance(lat, lon, vLat, vLon);
            if (d < bestDist)
            {
                bestDist = d;
                bestBrg = Navigation.NavigationCalculator.CalculateBearing(lat, lon, vLat, vLon);
            }
        }
        return (bestDist, bestBrg);
    }

    /// <summary>|relative bearing| &gt; 90° = behind. Binary by design (spec §5).</summary>
    internal static bool IsBehind(double bearingToDeg, double trueHeadingDeg)
    {
        double rel = ((bearingToDeg - trueHeadingDeg) % 360 + 540) % 360 - 180;
        return Math.Abs(rel) > 90;
    }
```

- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** (`feat(weather): advisory-geometry containment, nearest-vertex and behind tests`, same trailer).

---

### Task 3: `ActiveSkyFormatting.BuildLocationPhrase`

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs`
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyFormattingTests.cs` (extend)

**Interfaces:**
- Produces: `internal static string? BuildLocationPhrase(double? distanceNm, bool inside, bool behind, bool spoken)` — the spec §3 wordings; null when `!inside && distanceNm == null`.

- [ ] **Step 1: Write the failing tests** (append to `ActiveSkyFormattingTests`)

```csharp
    // --- BuildLocationPhrase -------------------------------------------------------------

    [Theory]
    [InlineData(123.4, false, false, false, "123 nm ahead")]
    [InlineData(123.4, false, false, true,  "123 nautical miles ahead")]
    [InlineData(95.0,  false, true,  false, "95 nm behind you")]
    [InlineData(95.0,  false, true,  true,  "95 nautical miles behind you")]
    [InlineData(0.4,   false, false, false, "less than one nautical mile ahead")]
    [InlineData(0.4,   false, true,  true,  "less than one nautical mile behind you")]
    public void Location_phrase_distances(double d, bool inside, bool behind, bool spoken, string expected)
        => Assert.Equal(expected,
            ActiveSkyFormatting.BuildLocationPhrase(d, inside, behind, spoken));

    [Fact]
    public void Location_phrase_inside_wins_and_differs_by_surface()
    {
        Assert.Equal("at your position (inside the area)",
            ActiveSkyFormatting.BuildLocationPhrase(50, inside: true, behind: false, spoken: false));
        Assert.Equal("at your position",
            ActiveSkyFormatting.BuildLocationPhrase(null, inside: true, behind: true, spoken: true));
    }

    [Fact]
    public void Location_phrase_null_without_geometry()
        => Assert.Null(ActiveSkyFormatting.BuildLocationPhrase(null, inside: false, behind: false, spoken: false));
```

- [ ] **Step 2: Verify RED** (filter `FullyQualifiedName~Location_phrase`). Expected: compile error.

- [ ] **Step 3: Implement** (add to `ActiveSkyFormatting`, near `BuildRouteAdvisoryAnnouncement`)

```csharp
    /// <summary>Spec 2026-07-13 §3 wording. Inside wins over distance. Box form
    /// abbreviates ("123 nm ahead"); spoken form spells the unit and drops the
    /// parenthetical. Null = no location resolved (caller renders nothing).</summary>
    internal static string? BuildLocationPhrase(double? distanceNm, bool inside, bool behind, bool spoken)
    {
        if (inside) return spoken ? "at your position" : "at your position (inside the area)";
        if (distanceNm is not double d) return null;
        string dir = behind ? "behind you" : "ahead";
        int whole = (int)Math.Round(d);
        if (whole < 1) return $"less than one nautical mile {dir}";
        return spoken ? $"{whole} nautical miles {dir}" : $"{whole} nm {dir}";
    }
```

- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** (`feat(weather): location phrase builder for route advisories`).

---

### Task 4: `WeatherService` tier-2 polygon lookup

**Files:**
- Modify: `MSFSBlindAssist/Services/WeatherService.cs`
- Test: `tests/MSFSBlindAssist.Tests/AdvisoryPolygonLookupTests.cs` (create)

**Interfaces:**
- Produces:
  `internal static List<(double Lat, double Lon)>? FindAdvisoryPolygonInGeoJson(string geojson, string identityPhrase)` (pure);
  `internal static async Task<List<(double Lat, double Lon)>?> TryGetAdvisoryPolygonAsync(string identityPhrase)` (async shell over the TTL caches — thin, not unit-tested).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/AdvisoryPolygonLookupTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>Pure tier-2 lookup (spec 2026-07-13 §4): identity phrase → polygon ring
/// from an aviationweather.gov GeoJSON body. The fixture is a trimmed real feature
/// from the 2026-07-13 live session (CONVECTIVE SIGMET 54E).</summary>
public class AdvisoryPolygonLookupTests
{
    private const string Fixture = """
    {"type":"FeatureCollection","features":[
      {"type":"Feature","properties":{"hazard":"CONVECTIVE","rawAirSigmet":"WSUS31 KKCI 131455 SIGE\nCONVECTIVE SIGMET 54E\nVALID UNTIL 1700Z\nFL CSTL WTRS\nAREA TS MOV FROM 27010KT. TOPS ABV FL450."},
       "geometry":{"type":"Polygon","coordinates":[[[-79.0,28.0],[-78.0,28.0],[-78.0,30.0],[-79.0,30.0],[-79.0,28.0]]]}},
      {"type":"Feature","properties":{"rawAirSigmet":"WSUS31 KKCI 131455 SIGE\nCONVECTIVE\nSIGMET 48E\nFL GA AND FL CSTL WTRS"},
       "geometry":{"type":"MultiPolygon","coordinates":[[[[-85.0,29.0],[-84.0,29.0],[-84.5,31.0],[-85.0,29.0]]]]}},
      {"type":"Feature","properties":{"rawAirSigmet":"GEOMLESS FIRST"},"geometry":null},
      {"type":"Feature","properties":{"rawAirSigmet":"GEOMLESS FIRST (reissued)"},
       "geometry":{"type":"Polygon","coordinates":[[[-70.0,40.0],[-69.0,40.0],[-69.5,41.0],[-70.0,40.0]]]}},
      {"type":"Feature","properties":{"rawAirSigmet":"no geometry here"},"geometry":null}
    ]}
    """;

    [Fact]
    public void Finds_polygon_by_identity_and_flips_geojson_order()
    {
        var v = WeatherService.FindAdvisoryPolygonInGeoJson(Fixture, "CONVECTIVE SIGMET 54E")!;
        Assert.Equal(5, v.Count);
        Assert.Equal(28.0, v[0].Lat);       // GeoJSON is [lon,lat] — order must flip
        Assert.Equal(-79.0, v[0].Lon);
    }

    [Fact]
    public void Whitespace_normalization_matches_an_identity_split_across_lines()
    {
        // Feature 2's raw splits "CONVECTIVE\nSIGMET 48E" across a newline — a naive
        // raw.Contains() fails here; only the normalized form matches. Also pins the
        // MultiPolygon first-ring path.
        var v = WeatherService.FindAdvisoryPolygonInGeoJson(Fixture, "CONVECTIVE SIGMET 48E")!;
        Assert.Equal(4, v.Count);
    }

    [Fact]
    public void Matched_feature_without_geometry_is_skipped_not_terminal()
    {
        // "GEOMLESS FIRST" matches feature 3 (geometry:null) AND feature 4 (polygon):
        // the scan must CONTINUE past the unusable match and return feature 4's ring.
        var v = WeatherService.FindAdvisoryPolygonInGeoJson(Fixture, "GEOMLESS FIRST")!;
        Assert.Equal(4, v.Count);
        Assert.Equal(40.0, v[0].Lat);
    }

    [Theory]
    [InlineData("CONVECTIVE SIGMET 99E")]   // no such advisory
    [InlineData("no geometry here")]        // matches, but geometry null and no later match
    public void Returns_null_when_unresolvable(string phrase)
        => Assert.Null(WeatherService.FindAdvisoryPolygonInGeoJson(Fixture, phrase));

    [Fact]
    public void Returns_null_on_malformed_json()
        => Assert.Null(WeatherService.FindAdvisoryPolygonInGeoJson("{not json", "X"));
}
```

- [ ] **Step 2: Verify RED** (filter `FullyQualifiedName~AdvisoryPolygonLookup`). Expected: compile error.

- [ ] **Step 3: Implement** (add to `WeatherService`, near the existing parsers)

```csharp
    /// <summary>Tier-2 geometry for route-advisory location (spec 2026-07-13 §4):
    /// finds the cached-feed feature whose raw text contains the advisory's identity
    /// phrase and returns its first polygon ring as (lat,lon). Raw text is
    /// whitespace-normalized before matching (feed raws contain newlines). Pure;
    /// null on no match / no usable geometry / malformed JSON.</summary>
    internal static List<(double Lat, double Lon)>? FindAdvisoryPolygonInGeoJson(
        string geojson, string identityPhrase)
    {
        try
        {
            using var doc = JsonDocument.Parse(geojson);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return null;
            foreach (var f in features.EnumerateArray())
            {
                if (!f.TryGetProperty("properties", out var props)) continue;
                string raw = GetString(props, "rawAirSigmet");
                if (raw.Length == 0) raw = GetString(props, "rawSigmet");
                string normalized = string.Join(" ",
                    raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (!normalized.Contains(identityPhrase, StringComparison.OrdinalIgnoreCase))
                    continue;

                // A matched feature with unusable geometry is SKIPPED, not terminal —
                // a later feature may match the same identity with a real polygon
                // (pinned by Matched_feature_without_geometry_is_skipped_not_terminal).
                if (!f.TryGetProperty("geometry", out var geom)
                    || geom.ValueKind != JsonValueKind.Object
                    || !geom.TryGetProperty("type", out var gt)) continue;
                JsonElement ring;
                switch (gt.GetString())
                {
                    case "Polygon":      ring = geom.GetProperty("coordinates")[0]; break;
                    case "MultiPolygon": ring = geom.GetProperty("coordinates")[0][0]; break;
                    default: continue;
                }
                var verts = new List<(double Lat, double Lon)>();
                foreach (var p in ring.EnumerateArray())
                    verts.Add((p[1].GetDouble(), p[0].GetDouble()));   // GeoJSON is [lon,lat]
                if (verts.Count >= 3) return verts;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>Async shell: refreshes the two SIGMET feeds through their existing
    /// TTL caches (no extra HTTP within the TTL) and searches airsigmet first
    /// (US convective lives there), then isigmet. Thin by design — the pure core
    /// above carries the tests.</summary>
    internal static async Task<List<(double Lat, double Lon)>?> TryGetAdvisoryPolygonAsync(
        string identityPhrase)
    {
        try
        {
            await Task.WhenAll(
                RefreshCacheAsync(ISIGMET_URL,   false, SIGMET_CACHE_MINUTES,
                    s => { lock (_cacheLock) _isigmetJson = s; },
                    () => { lock (_cacheLock) return _isigmetCacheTime; },
                    t => { lock (_cacheLock) _isigmetCacheTime = t; }),
                RefreshCacheAsync(AIRSIGMET_URL, false, SIGMET_CACHE_MINUTES,
                    s => { lock (_cacheLock) _airsigmetJson = s; },
                    () => { lock (_cacheLock) return _airsigmetCacheTime; },
                    t => { lock (_cacheLock) _airsigmetCacheTime = t; })
            );
            string isigmet, airsigmet;
            lock (_cacheLock) { isigmet = _isigmetJson; airsigmet = _airsigmetJson; }
            return (airsigmet.Length > 0 ? FindAdvisoryPolygonInGeoJson(airsigmet, identityPhrase) : null)
                ?? (isigmet.Length  > 0 ? FindAdvisoryPolygonInGeoJson(isigmet,  identityPhrase) : null);
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"Advisory polygon lookup error: {ex.Message}");
            return null;
        }
    }
```

(The `RefreshCacheAsync` call shape is copied verbatim from `GetNearbyAdvisoriesAsync` a few lines above — same locks, same TTL constants. If field/constant names differ when you read the file, match the file, not this plan.)

- [ ] **Step 4: Verify GREEN.**
- [ ] **Step 5: Commit** (`feat(weather): tier-2 advisory polygon lookup from cached aviationweather feeds`).

---

### Task 5: `ActiveSkyClient.GetPositionalAdvisoriesTextAsync`

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs`

**Interfaces:**
- Produces: `public async Task<string?> GetPositionalAdvisoriesTextAsync(double lat, double lon)` — same gate/port/timeout/null-on-error contract as `GetRouteAdvisoriesTextAsync`.

No unit test — pure I/O mirror of an existing method (sim-facing; covered by the in-sim plan).

- [ ] **Step 1: Implement** (directly below `GetRouteAdvisoriesTextAsync`)

```csharp
    /// <summary>
    /// /GetActiveSigmetsAt?lat=&lon= — advisories ACTIVE AT the passed position
    /// (strict containment; weather.md §13). Same contract as the route variant:
    /// raw response text, or null on error / AS off / unreachable. Per the
    /// 2026-07-13 bundling finding, callers must treat only the FIRST parsed
    /// advisory of a hit as position-matched.
    /// </summary>
    public async Task<string?> GetPositionalAdvisoriesTextAsync(double lat, double lon)
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            string url = $"{BaseUrl(port)}/GetActiveSigmetsAt"
                + $"?lat={lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&lon={lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}";
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            Log.Debug("ActiveSky", "GetPositionalAdvisories failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 2: Build** (`dotnet build MSFSBlindAssist.sln -c Debug` → 0 warnings) and run the full suite (no regressions).
- [ ] **Step 3: Commit** (`feat(weather): positional GetActiveSigmetsAt client method`).

---

### Task 6: `RouteAdvisoryLocator` (compose + orchestrate)

**Files:**
- Create: `MSFSBlindAssist/Services/RouteAdvisoryLocator.cs`
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoryLocatorTests.cs` (create)

**Interfaces:**
- Consumes: everything from Tasks 1–5; `ActiveSkyFormatting.RouteAdvisory` (`.Key`, `.Identity`, `.Lines`), `ActiveSkyFormatting.ParseRouteAdvisories`, `SimConnectManager.AircraftPosition` (`.Latitude`, `.Longitude`, `.HeadingMagnetic`, `.MagneticVariation`).
- Produces:
  `internal static string? Compose(IReadOnlyList<(double Lat, double Lon)>? vertices, bool probeMatched, double lat, double lon, double trueHeadingDeg, bool spoken)` (pure);
  `internal static async Task<Dictionary<string, string>> ComputeLocationsAsync(ActiveSkyClient client, IReadOnlyList<ActiveSkyFormatting.RouteAdvisory> advisories, SimConnectManager.AircraftPosition pos, bool spoken)` (thin shell).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/MSFSBlindAssist.Tests/RouteAdvisoryLocatorTests.cs
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoryLocatorTests
{
    private static readonly (double Lat, double Lon)[] Square =
        { (10, 20), (10, 21), (11, 21), (11, 20) };

    [Fact]
    public void Probe_match_wins_even_without_geometry()
        => Assert.Equal("at your position",
            RouteAdvisoryLocator.Compose(null, probeMatched: true, 0, 0, 0, spoken: true));

    [Fact]
    public void Polygon_containment_reads_inside()
        => Assert.Equal("at your position (inside the area)",
            RouteAdvisoryLocator.Compose(Square, probeMatched: false, 10.5, 20.5, 0, spoken: false));

    [Fact]
    public void Outside_polygon_reads_distance_and_direction()
    {
        // 1° south of the square, heading north: nearest corner ~60 nm dead ahead.
        Assert.Equal("60 nm ahead",
            RouteAdvisoryLocator.Compose(Square, false, 9.0, 20.0, 0, spoken: false));
        // Same geometry, heading south: it's behind.
        Assert.Equal("60 nautical miles behind you",
            RouteAdvisoryLocator.Compose(Square, false, 9.0, 20.0, 180, spoken: true));
    }

    [Fact]
    public void No_geometry_and_no_probe_is_null()
        => Assert.Null(RouteAdvisoryLocator.Compose(null, false, 9.0, 20.0, 0, spoken: false));
}
```

- [ ] **Step 2: Verify RED** (filter `FullyQualifiedName~RouteAdvisoryLocator`). Expected: compile error.

- [ ] **Step 3: Implement**

```csharp
// MSFSBlindAssist/Services/RouteAdvisoryLocator.cs
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
```

- [ ] **Step 4: Verify GREEN** + full suite.
- [ ] **Step 5: Commit** (`feat(weather): route-advisory location orchestrator`).

---

### Task 7: Location lines in `BuildRouteAdvisoriesText`

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (`BuildRouteAdvisoriesText`)
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs` (extend)

**Interfaces:**
- Produces: `internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories, bool decode, IReadOnlyDictionary<string, string>? locations = null)` — appends `Location: {phrase}` as the block's last line in BOTH modes; existing callers/tests unaffected (optional parameter).

- [ ] **Step 1: Write the failing tests** (append to `RouteAdvisoriesTests`)

```csharp
    [Fact]
    public void Location_line_renders_in_raw_and_decoded_modes()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        var locations = new Dictionary<string, string> { ["CONVECTIVE SIGMET 18E"] = "123 nm ahead" };

        string raw = ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false, locations);
        Assert.EndsWith("Location: 123 nm ahead", raw);
        Assert.StartsWith("CONVECTIVE SIGMET 18E", raw);          // verbatim block untouched above

        string decoded = ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: true, locations);
        Assert.EndsWith("Location: 123 nm ahead", decoded);
    }

    [Fact]
    public void Missing_location_key_renders_exactly_as_today()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        Assert.Equal(
            ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false),
            ActiveSkyFormatting.BuildRouteAdvisoriesText(advisories, decode: false,
                new Dictionary<string, string>()));
    }
```

- [ ] **Step 2: Verify RED.** Expected: compile error (no 3-arg overload).

- [ ] **Step 3: Implement** — change the signature to
`internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories, bool decode, IReadOnlyDictionary<string, string>? locations = null)` and, inside the per-advisory loop, append after BOTH the decoded branch and the raw branch (i.e. as the block's final line, before the blank separator):

```csharp
            if (locations != null && locations.TryGetValue(a.Key, out string? loc))
                sb.AppendLine($"Location: {loc}");
```

- [ ] **Step 4: Verify GREEN** + the whole `RouteAdvisoriesTests` class (existing render tests must be untouched).
- [ ] **Step 5: Commit** (`feat(weather): route-advisories box renders location lines`).

---

### Task 8: Announcement suffix

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (`BuildRouteAdvisoryAnnouncement`)
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs` (extend)

**Interfaces:**
- Produces: `internal static string BuildRouteAdvisoryAnnouncement(RouteAdvisory a, string? locationPhrase = null)` — appends `, {locationPhrase}` when non-null, in BOTH the decoded and raw-key-fallback paths.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Announcement_appends_the_location_phrase()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock)[0];
        Assert.Equal("CONVECTIVE SIGMET 18E, thunderstorms, 123 nautical miles ahead",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a, "123 nautical miles ahead"));
        Assert.Equal("CONVECTIVE SIGMET 18E, thunderstorms",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a));                 // unchanged default
    }

    [Fact]
    public void Raw_key_fallback_also_carries_the_location()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded")[0];
        Assert.Equal("No flight plan is currently loaded, at your position",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(a, "at your position"));
    }
```

- [ ] **Step 2: Verify RED.**

- [ ] **Step 3: Implement** — change the method to:

```csharp
    internal static string BuildRouteAdvisoryAnnouncement(RouteAdvisory a, string? locationPhrase = null)
    {
        string core;
        if (a.Identity == null || a.Hazard == null) core = a.Key;
        else
        {
            var parts = new List<string> { a.Identity };
            if (a.FirName != null) parts.Add(a.FirName);
            parts.Add(a.Hazard);
            if (a.VerticalExtent != null) parts.Add(a.VerticalExtent);
            core = string.Join(", ", parts);
        }
        return locationPhrase == null ? core : $"{core}, {locationPhrase}";
    }
```

(Keep the existing doc comment; extend it with one line: location is appended last, on both paths.)

- [ ] **Step 4: Verify GREEN** + whole class.
- [ ] **Step 5: Commit** (`feat(weather): route-advisory announcement carries the location suffix`).

---

### Task 9: Wire both call sites

**Files:**
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (`FetchRouteAdvisoriesAsync`)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`CheckRouteAdvisoriesAsync`)

No unit tests — sim-facing wiring; the in-sim plan covers it. Build + full suite must stay green.

- [ ] **Step 1: Weather Radar box.** Replace the body of `FetchRouteAdvisoriesAsync` with:

```csharp
    private async Task<string> FetchRouteAdvisoriesAsync()
    {
        if (_activeSkyAvailable != true) return "unavailable";
        string? raw = await _activeSky.GetRouteAdvisoriesTextAsync();
        if (raw == null) return "unavailable";
        var advisories = MSFSBlindAssist.Services.ActiveSkyFormatting.ParseRouteAdvisories(raw);

        // Location context (spec 2026-07-13): additive-only — an empty dictionary
        // renders the box exactly as before. Recomputed per pass (the aircraft moves);
        // the DisplayListBox reconcile keeps an updating row from stealing the cursor.
        Dictionary<string, string> locations = new();
        if (_simConnect.LastKnownPosition is { } pos)
            locations = await MSFSBlindAssist.Services.RouteAdvisoryLocator.ComputeLocationsAsync(
                _activeSky, advisories, pos, spoken: false);

        // Decode gating rides the same checkbox as the Nearby Advisories box; the
        // CheckedChanged handler saves the setting and the next refresh (≤30 s auto
        // or F5) picks it up — same latency contract as the sibling box.
        return MSFSBlindAssist.Services.ActiveSkyFormatting.BuildRouteAdvisoriesText(
            advisories, SettingsManager.Current.DecodeWeatherAdvisories, locations);
    }
```

(Match the file's actual field names when editing — `_simConnect` is the form's `SimConnectManager` field; keep the existing comments that still apply.)

- [ ] **Step 2: Announcer.** In `CheckRouteAdvisoriesAsync`, after `newKeys` is computed and found non-empty, compute the spoken locations ONCE, then use them in the loop:

```csharp
            if (!IsHandleCreated || IsDisposed) return;

            // Spoken location context for the new keys (spec 2026-07-13) — one probe +
            // cached-feed lookups. Bounded by the probe's 5 s timeout (spec §8.2); every
            // failure path yields "no suffix" — the announcement is never dropped.
            Dictionary<string, string> locations = new();
            if (newKeys.Count > 0 && simConnectManager.LastKnownPosition is { } locPos)
                locations = await MSFSBlindAssist.Services.RouteAdvisoryLocator.ComputeLocationsAsync(
                    weatherActiveSky, advisories, locPos, spoken: true);

            foreach (string key in newKeys)
            {
                // … existing adv lookup unchanged …
                string phrase = adv != null
                    ? MSFSBlindAssist.Services.ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(
                        adv, locations.TryGetValue(key, out string? loc) ? loc : null)
                    : key;
                // … existing Log.Debug + announcer.Announce unchanged …
            }
```

(`newKeys` is `IReadOnlyList<string>` — use `.Count`. The post-await `IsHandleCreated/IsDisposed` guard already precedes this block; keep it first.)

- [ ] **Step 3: Build the solution (0 warnings) + run the full suite (green).**
- [ ] **Step 4: Commit** (`feat(weather): route advisories speak and display their location`).

---

### Task 10: Documentation

**Files:**
- Modify: `docs/weather.md` (§12 — new sub-heading after the decode section)
- Modify: `CLAUDE.md` (one bullet in the weather invariants block)

- [ ] **Step 1: `docs/weather.md`.** Add a "Location context (2026-07)" block to §12 covering, with file/method names: the §3 output contract (box line in both modes; spoken suffix; additive-only); the two geometry tiers + the single positional probe (first-advisory-only rule, cross-referencing §13); the pure/pinned components (`AdvisoryGeometry`, `BuildLocationPhrase`, `RouteAdvisoryLocator.Compose`, `FindAdvisoryPolygonInGeoJson`); the §5 approximations (nearest-vertex distance shared with the Nearby box, heading-not-track ahead/behind); and the degradation ladder.

- [ ] **Step 2: `CLAUDE.md`.** Add to the weather invariants block:

```markdown
- Route-advisory location context is ADDITIVE-ONLY (no geometry → the advisory renders exactly as before, never dropped/blocked); only the FIRST advisory of a positional GetActiveSigmetsAt response is position-matched (bundling); tier-2 borrows aviationweather.gov geometry by EXACT identity match only — never attach geometry to an advisory whose identity didn't match. → [weather.md](docs/weather.md)
```

- [ ] **Step 3: Build + full suite one last time; commit** (`docs(weather): document route-advisory location context`).

---

## In-sim test plan (goes in the PR description; Robin runs it)

1. **Route advisory with an ICAO body (tier 1):** with a FIR-crossing plan loaded in AS, Shift+R → the advisory block ends with `Location: <N> nm ahead` (or behind), plausible against the known position; the distance changes across refreshes while flying toward it.
2. **US convective advisory (tier 2, Live mode):** with a US plan (e.g. KMIA→KJFK) and an active convective SIGMET on route, the block carries a Location line whose distance roughly matches the same SIGMET's distance in the Nearby Advisories box.
3. **Inside:** fly (or slew) into the advisory area → `Location: at your position (inside the area)` within one refresh; the Nearby box's same advisory should read ~0 nm.
4. **Behind:** pass the area and fly away → the line flips to `behind you`.
5. **Announce:** a NEW advisory appearing mid-session speaks "…, N nautical miles ahead." exactly once; an advisory with no resolvable geometry announces exactly as today (no suffix, no delay).
6. **Degradation:** disconnect the sim (no position) or switch AS to Historic with a convective advisory → affected advisories render with no Location line and nothing else changes.
7. **ENTIRE-FIR-style advisory (tier 1 fails by design):** with an advisory whose body has no `WI` polygon (e.g. "ENTIRE FIR") on route in Live mode, sim connected → its block renders with NO Location line while other advisories in the same box keep theirs.
8. **Probe-only inside (non-Live mode):** in Historic/Custom AS mode, slew inside a route convective advisory → its block shows `Location: at your position (inside the area)` from the positional probe alone (no geometry resolvable in non-Live modes).
9. *(Optional)* Internet blocked but AS reachable: advisories still render and announcements still fire; the only added delay is the single bounded feed-refresh timeout on the first tier-2 attempt.
