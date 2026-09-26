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
    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static readonly AirportFacilities KtiwBox = new()
        { Icao = "KTIW", LeftLon = -122.579353, RightLon = -122.573303, TopLat = 47.274742, BottomLat = 47.260826 };

    [Fact]
    public void The_box_query_covers_the_airport_box_plus_margin_in_invariant_numbers_and_asks_for_full_geometry()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A comma-decimal locale must not reach the query: every mirror answers 400 to "47,26".
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var g = KtiwBox.Grown(OsmFeatureSource.BoxMarginMetres);
            string q = OsmFeatureSource.BuildBoxQuery(KtiwBox);
            string bbox = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "({0:0.######},{1:0.######},{2:0.######},{3:0.######})", g.Bottom, g.Left, g.Top, g.Right);
            Assert.Contains("[\"aeroway\"~\"^(terminal|hangar|apron|tower|control_tower|fuel|helipad)$\"]" + bbox + ";", q);
            Assert.Contains("[\"building\"][\"name\"]" + bbox + ";", q);   // bounded by the box, so named buildings are asked for
            Assert.DoesNotContain("area", q);                                  // no area database needed: every planet mirror answers it
            Assert.DoesNotContain("around", q);
            Assert.EndsWith(");out body geom;", q);  // `body`: a relation's members, each with its geometry
            Assert.Equal(1, CountOf(q, ";out "));    // ONE output statement
            Assert.DoesNotContain("center", q);      // Overpass honours only the LAST geometry modifier
            Assert.DoesNotContain(" bb", q);
            Assert.DoesNotContain("\"amenity\"~\"^(fuel", q);   // road fuel is not asked for
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
    public void Box_results_are_kept_only_inside_the_airport_box_plus_margin()
    {
        var box = new AirportFacilities { Icao = "KTIW", LeftLon = -122.579353, RightLon = -122.573303, TopLat = 47.274742, BottomLat = 47.260826 };
        var inside = At(47.2712, -122.5731);      // the tower, 15 m outside the bare box
        var chevron = At(47.2712, -122.5500);     // a road filling station ~1.7 km east
        var kept = OsmFeatureSource.KeepInsideBox(new[] { inside, chevron }, box);
        Assert.Same(inside, Assert.Single(kept));
    }

    // ---- Which query is asked -------------------------------------------------------------
    //
    // Driven through a fake HttpMessageHandler rather than a network: the area query is told apart
    // by the posted `data` (only it names an aerodrome). Each source gets a cooldown map of its
    // OWN (OverpassClient's internal constructor), so a mirror these tests fail is never cooled
    // for any other test — and the class runs in the OverpassMirrorState collection besides.

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
    public async Task With_a_box_only_the_box_query_is_asked_never_the_area_query()
    {
        // overpass.openstreetmap.fr has no area database (measured 2026-09-25: "runtime error …
        // area_tags_local.bin") and was the one mirror answering; the area query failed there.
        int areaAsked = 0;
        var source = SourceOver(isArea => { if (isArea) areaAsked++; return Hangars((47.2700, -122.5760)); });
        var features = await source.FetchAsync("KTIW", KtiwBox, CancellationToken.None);
        Assert.Single(features!);
        Assert.Equal(0, areaAsked);
    }

    [Fact]
    public async Task A_box_query_that_never_reached_a_mirror_is_a_failure_not_an_airport_without_buildings()
    {
        // null, so the store remembers a failure and retries — an empty list would cache
        // "no buildings here" for the whole session.
        var source = SourceOver(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.Null(await source.FetchAsync("KTIW", KtiwBox, CancellationToken.None));
    }

    [Fact]
    public async Task A_box_query_that_answers_with_nothing_really_is_an_airport_without_buildings()
    {
        var source = SourceOver(_ => NoElements());
        var features = await source.FetchAsync("KTIW", KtiwBox, CancellationToken.None);
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
        Assert.Null(await source.FetchAsync("KTIW", KtiwBox, CancellationToken.None));
    }

    private static HttpResponseMessage Hangars(params (double Lat, double Lon)[] at) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{\"elements\":[" + string.Join(",", at.Select((p, i) =>
            $"{{\"type\":\"node\",\"id\":{i + 1},\"lat\":{p.Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"lon\":{p.Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"tags\":{{\"aeroway\":\"hangar\",\"name\":\"H{i + 1}\"}}}}")) + "]}"),
    };

    [Fact]
    public async Task A_box_answer_keeps_only_what_lies_inside_the_box_plus_margin()
    {
        var source = SourceOver(_ => Hangars((47.2700, -122.5760), (47.2712, -122.5500)));   // a filling station 1.7 km east
        var features = await source.FetchAsync("KTIW", KtiwBox, CancellationToken.None);
        Assert.Equal("H1", Assert.Single(features!).Name);
    }

    [Fact]
    public async Task Without_a_box_nothing_is_asked_and_nothing_is_returned()
    {
        // Fail closed: an icao= area query, the only way to ask without a box, landed on the wrong
        // aerodrome (live UKRB/UKRK).
        int asked = 0;
        var source = SourceOver(_ => { asked++; return Hangars((47.2700, -122.5760)); });
        var features = await source.FetchAsync("KTIW", null, CancellationToken.None);
        Assert.NotNull(features);
        Assert.Empty(features);
        Assert.Equal(0, asked);
    }

    // The box query answers in 1.3-3.3 s even at EGLL, KDEN and KATL (overpass.openstreetmap.fr,
    // measured 2026-09-25); the area query it replaced took 17-23 s. The per-mirror timeout clears
    // that with a wide margin and leaves the store's FetchBudget room to try THREE mirrors, because
    // on that day two public mirrors timed out on every request before the working one was reached.
    [Fact]
    public void The_per_mirror_timeout_clears_a_large_airport_and_leaves_room_for_three_mirrors()
    {
        Assert.True(OsmFeatureSource.PerMirrorTimeout >= TimeSpan.FromSeconds(15));
        Assert.True(OsmFeatureSource.PerMirrorTimeout * 3 <= OnlineFeatureStore.FetchBudget);
    }

    // overpass-api.de refused TCP from this machine (2026-09-25) and Windows took 21 s per host to
    // say so — three hosts of it spent the whole fetch budget before a working mirror was asked.
    [Fact]
    public void A_mirror_that_refuses_the_connection_costs_at_most_five_seconds()
        => Assert.True(OverpassClient.ConnectTimeout <= TimeSpan.FromSeconds(5));
}
