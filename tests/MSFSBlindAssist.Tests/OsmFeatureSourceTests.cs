using System.Collections.Concurrent;
using System.Net;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

[Collection("OverpassMirrorState")]
public class OsmFeatureSourceTests
{
    [Fact]
    public void The_area_query_is_scoped_to_the_icao_tagged_aerodrome_and_asks_for_full_geometry()
    {
        string q = OsmFeatureSource.BuildAreaQuery("ktiw\";x");
        Assert.Contains("area[\"aeroway\"=\"aerodrome\"][\"icao\"=\"KTIWX\"]->.ad;", q);   // sanitised, upper-cased
        Assert.Contains("(area.ad)", q);
        Assert.EndsWith(");out body geom;", q);  // `body`: a relation's members, each with its geometry
        Assert.Equal(1, CountOf(q, ";out "));    // ONE output statement
        Assert.DoesNotContain("center", q);      // Overpass honours only the LAST geometry modifier
        Assert.DoesNotContain(" bb", q);
        Assert.DoesNotContain("\"amenity\"~\"^(fuel", q);   // road fuel is not asked for
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void The_fallback_query_is_a_3_km_radius_without_the_generic_named_building_clauses()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            string q = OsmFeatureSource.BuildFallbackQuery(47.2679, -122.5781);
            Assert.Contains("(around:3000,47.2679,-122.5781)", q);
            Assert.DoesNotContain("[\"office\"][\"name\"]", q);
            Assert.DoesNotContain("[\"building\"][\"name\"]", q);
            Assert.EndsWith(");out body geom;", q);
            Assert.Equal(1, CountOf(q, ";out "));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void A_real_response_parses_into_features()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "osm-features-area-ktiw.json"));
        Assert.Equal(25, OsmFeatureSource.Parse(json).Count);
    }

    private static AirportFeature At(double lat, double lon) => new() { Kind = FeatureKind.Hangar, Lat = lat, Lon = lon, Source = FeatureSource.Osm };

    [Fact]
    public void Fallback_results_are_kept_only_inside_the_airport_box_plus_margin_and_dropped_without_a_box()
    {
        var box = new AirportFacilities { Icao = "KTIW", LeftLon = -122.579353, RightLon = -122.573303, TopLat = 47.274742, BottomLat = 47.260826 };
        var inside = At(47.2712, -122.5731);      // the tower, 15 m outside the bare box
        var chevron = At(47.2712, -122.5500);     // a road filling station ~1.7 km east
        var kept = OsmFeatureSource.KeepInsideBox(new[] { inside, chevron }, box);
        Assert.Same(inside, Assert.Single(kept));
        Assert.Empty(OsmFeatureSource.KeepInsideBox(new[] { inside, chevron }, null));   // fail closed
    }

    // ---- The two-request sequence ---------------------------------------------------------
    //
    // Driven through a fake HttpMessageHandler rather than a network: the area query and the
    // fallback are told apart by the posted `data` (only the area query names an aerodrome).
    // Each source gets a cooldown map of its OWN (OverpassClient's internal constructor), so a
    // mirror these tests fail is never cooled for any other test — and the class runs in the
    // OverpassMirrorState collection besides.

    private sealed class ScriptedMirror : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _reply;
        public ScriptedMirror(Func<string, HttpResponseMessage> reply) { _reply = reply; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _reply(await request.Content!.ReadAsStringAsync(ct));
    }

    private static OsmFeatureSource SourceOver(Func<bool, HttpResponseMessage> reply)
        => new(new OverpassClient(new HttpClient(new ScriptedMirror(
            posted => reply(posted.Contains("aerodrome", StringComparison.Ordinal)))),
            new ConcurrentDictionary<string, DateTime>()));

    private static HttpResponseMessage NoElements() => new(HttpStatusCode.OK) { Content = new StringContent("{\"elements\":[]}") };

    [Fact]
    public async Task A_fallback_that_never_reached_a_mirror_is_a_failure_not_an_airport_without_buildings()
    {
        // Every mirror down for the SECOND request: null, so the store remembers a failure and
        // retries — an empty list would cache "no buildings here" for the whole session.
        var source = SourceOver(isAreaQuery => isAreaQuery ? NoElements() : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await source.FetchAsync("KTIW", 47.2679, -122.5781, null, CancellationToken.None));
    }

    [Fact]
    public async Task A_fallback_that_answers_with_nothing_really_is_an_airport_without_buildings()
    {
        var source = SourceOver(_ => NoElements());
        var features = await source.FetchAsync("KTIW", 47.2679, -122.5781, null, CancellationToken.None);
        Assert.NotNull(features);
        Assert.Empty(features);
    }

    [Theory]
    // Both pass OverpassClient.ClassifyBody (an object with an `elements` array and no
    // "runtime error" remark) and still throw inside Parse. A throw here would escape
    // SurroundingsTier.Read's caller as a faulted task rather than a source that failed, so the
    // store must be told "failed" — which it remembers for FailureMemory and then retries.
    [InlineData("{\"elements\":[42]}")]                                        // an element that is not an object
    [InlineData("{\"elements\":[{\"type\":\"node\",\"lat\":\"x\",\"lon\":\"y\",\"tags\":{\"aeroway\":\"hangar\"}}]}")]   // a coordinate that is not a number
    public async Task A_body_that_is_shapeless_enough_to_break_the_parser_is_a_failed_fetch_not_a_throw(string body)
    {
        var source = SourceOver(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        Assert.Null(await source.FetchAsync("KTIW", 47.2679, -122.5781, null, CancellationToken.None));
    }

    private static readonly AirportFacilities UkrbBox = new()
        { Icao = "UKRB", LeftLon = 31.60, RightLon = 31.62, TopLat = 51.62, BottomLat = 51.60 };

    private static HttpResponseMessage Hangars(params (double Lat, double Lon)[] at) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"elements\":[" + string.Join(",", at.Select((p, i) =>
            $"{{\"type\":\"node\",\"id\":{i + 1},\"lat\":{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lon\":{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"tags\":{{\"aeroway\":\"hangar\",\"name\":\"H{i + 1}\"}}}}")) + "]}"),
    };

    [Fact]
    public async Task An_area_answer_keeps_only_what_lies_near_the_airport()
    {
        // Live UKRB/UKRK: the icao-tagged aerodrome OSM returned was another field 1,279 km and
        // 4,171 km away, and its hangars were announced as this airport's.
        var source = SourceOver(isArea => isArea ? Hangars((51.61, 31.61), (40.0, 20.0)) : NoElements());
        var features = await source.FetchAsync("UKRB", 51.61, 31.61, UkrbBox, CancellationToken.None);
        Assert.Equal("H1", Assert.Single(features!).Name);
    }

    [Fact]
    public async Task An_area_answer_about_another_airport_falls_through_to_the_radius_query()
    {
        var source = SourceOver(isArea => isArea ? Hangars((40.0, 20.0)) : Hangars((51.605, 31.615)));
        var features = await source.FetchAsync("UKRB", 51.61, 31.61, UkrbBox, CancellationToken.None);
        Assert.Equal("H1", Assert.Single(features!).Name);
        Assert.Equal(51.605, features![0].Lat, 6);
    }
}
