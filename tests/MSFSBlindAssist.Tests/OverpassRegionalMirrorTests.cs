using System.Net;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A REGIONAL Overpass instance — one serving a country extract rather than the planet — answers a
/// query about anywhere outside its extract with HTTP 200, <c>"elements": []</c> and NO
/// <c>remark</c>. <see cref="OverpassClient.ClassifyBody"/> cannot tell that from a genuine
/// "nothing there", because for some queries an empty result really is the right answer. The damage
/// is downstream: <c>OnlineFeatureStore</c> caches it as Served — "this airport has no buildings" —
/// for the whole session with Degraded false, so nothing ever expires it, and <c>OsmTaxiSource</c>
/// reports a successful fetch that adopted no names.
///
/// <para>Measured live, 2026-09-22, against <c>overpass.osm.ch</c> (Swiss OSM association,
/// Switzerland extract): EHAM area query 0 elements / no remark / 0.6 s; EHAM <c>around:3000</c>
/// fallback 0; KATL taxiway query 0; LSZH <c>around:3000</c> 77 — Zurich being inside its extract.
/// Because it answered in about a second while the planet-wide mirrors returned 504, the cooldown
/// map promoted it to FIRST for every later airport in the session. One process, eight airports:
/// KJFK 0, KATL 0 in 1.1 s, EGLL 0, KORD 0, OMDB 0, LIRF 0, KTIW 0 — and LSZH 80.</para>
///
/// <para>Two defences, deliberately at different levels. The list no longer carries that instance;
/// and the CLIENT no longer believes an empty answer until no other mirror contradicts it, which
/// covers regional instances nobody has identified yet. Neither touches a query string — the one
/// change to a shipped Overpass query in this feature's history (<c>out tags geom center</c>) cost
/// every taxiway name at every airport, so the query text is left exactly alone.</para>
/// </summary>
public class OverpassRegionalMirrorTests
{
    [Fact]
    public void The_mirror_list_holds_no_regional_extract()
        => Assert.DoesNotContain(OverpassClient.MirrorUrls,
            m => m.Contains("overpass.osm.ch", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Every_mirror_is_a_planet_wide_instance()
    {
        // Guards against a future addition of another regional instance. These are the hosts
        // verified to answer for airports on more than one continent.
        string[] allowed =
        {
            "overpass-api.de", "overpass.kumi.systems", "overpass.private.coffee",
            "lz4.overpass-api.de", "z.overpass-api.de", "overpass.openstreetmap.fr",
        };
        foreach (string m in OverpassClient.MirrorUrls)
            Assert.Contains(allowed, a => m.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_empty_element_list_is_recognised_as_empty()
    {
        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody("{\"version\":0.6,\"elements\":[]}"));
        Assert.Equal(OverpassClient.BodyKind.Elements, OverpassClient.ClassifyBody("{\"elements\":[{\"type\":\"node\",\"id\":1}]}"));
    }

    /// <summary>A body that is not a usable result at all is a FAILED mirror — it must never be
    /// mistaken for a believable empty answer.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("<html>nope</html>")]
    [InlineData("{\"version\":0.6}")]
    public void A_broken_body_is_not_treated_as_an_empty_answer(string body)
        => Assert.Equal(OverpassClient.BodyKind.Failed, OverpassClient.ClassifyBody(body));

    // ---------------------------------------------------------------- the client's own rule

    private sealed class PerHostMirror : HttpMessageHandler
    {
        private readonly Func<string, string> _bodyForHost;
        public int Calls;
        public PerHostMirror(Func<string, string> bodyForHost) { _bodyForHost = bodyForHost; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_bodyForHost(request.RequestUri!.Host)),
            });
        }
    }

    private const string Empty = "{\"version\":0.6,\"elements\":[]}";
    private const string Content = "{\"version\":0.6,\"elements\":[{\"type\":\"node\",\"id\":7,\"lat\":1.0,\"lon\":2.0}]}";

    [Fact]
    public async Task One_mirror_with_the_data_beats_any_number_answering_empty()
    {
        // Order-independent on purpose: the cooldown map is static and shared across the whole
        // test run, so which mirror is tried first is not something a test may assume. Whichever
        // order they are tried in, the answer that HAS elements must be the one returned.
        var handler = new PerHostMirror(host => host.Contains("kumi") ? Content : Empty);
        var client = new OverpassClient(new HttpClient(handler));

        string? body = await client.PostAsync("[out:json];node(1);out;", CancellationToken.None);

        Assert.Equal(Content, body);
    }

    [Fact]
    public async Task An_empty_answer_is_still_returned_when_every_mirror_agrees()
    {
        // The regional-mirror defence must not break the airport that genuinely has nothing:
        // a small strip with no mapped hangar, apron or tower is a real answer, and returning
        // null there would have the store retry it every five minutes for the whole session.
        var handler = new PerHostMirror(_ => Empty);
        var client = new OverpassClient(new HttpClient(handler));

        string? body = await client.PostAsync("[out:json];node(1);out;", CancellationToken.None);

        Assert.NotNull(body);
        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.True(handler.Calls >= OverpassClient.MirrorUrls.Count,
            "every mirror must be asked before an empty answer is believed");
    }

    [Fact]
    public async Task A_mirror_answering_empty_is_never_blacklisted_for_it()
    {
        // An empty answer is not evidence the mirror is ill — it may simply be the truth about
        // that airport. Marking it failed would cool a healthy planet-wide mirror for five
        // minutes on the strength of one legitimately empty query.
        var handler = new PerHostMirror(host => host.Contains("kumi") ? Content : Empty);
        var client = new OverpassClient(new HttpClient(handler));

        await client.PostAsync("[out:json];node(1);out;", CancellationToken.None);
        int firstRound = handler.Calls;
        await client.PostAsync("[out:json];node(1);out;", CancellationToken.None);

        Assert.True(handler.Calls - firstRound >= firstRound - 1,
            "the second call must still be free to try the same mirrors");
    }
}
