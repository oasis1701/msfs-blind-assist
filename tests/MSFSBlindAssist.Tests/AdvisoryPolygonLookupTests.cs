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
