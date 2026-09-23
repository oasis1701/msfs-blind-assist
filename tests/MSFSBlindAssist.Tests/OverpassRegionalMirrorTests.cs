using System.Collections.Concurrent;
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
/// and the CLIENT no longer believes an empty answer until ONE more FRESH mirror has been asked,
/// which covers regional instances nobody has identified yet — one, never a sweep of the list and
/// never a cooled-down mirror, because a genuinely empty small field used to cost six round-trips
/// (review OV-1). Neither touches a query string — the one change to a shipped Overpass query in
/// this feature's history (<c>out tags geom center</c>) cost every taxiway name at every airport,
/// so the shipped taxiway query text is left exactly alone.</para>
/// </summary>
[Collection("OverpassMirrorState")]
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
    //
    // Every client below gets a cooldown map of its OWN (OverpassClient's internal constructor),
    // so the order it asks the mirrors in is the list order minus whatever THIS test cooled —
    // which is what lets these tests pin the ORDER, not only the answer (review SW-2).

    /// <summary>Answers per HOST and records every host it was asked, in order.</summary>
    private sealed class PerHostMirror : HttpMessageHandler
    {
        private readonly Func<string, CancellationToken, Task<HttpResponseMessage>> _reply;
        public readonly List<string> Asked = new();

        public PerHostMirror(Func<string, HttpResponseMessage> reply)
            : this((host, _) => Task.FromResult(reply(host))) { }

        public PerHostMirror(Func<string, CancellationToken, Task<HttpResponseMessage>> reply) { _reply = reply; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string host = request.RequestUri!.Host;
            lock (Asked) Asked.Add(host);
            return _reply(host, ct);
        }
    }

    private const string Empty = "{\"version\":0.6,\"elements\":[]}";
    private const string Content = "{\"version\":0.6,\"elements\":[{\"type\":\"node\",\"id\":7,\"lat\":1.0,\"lon\":2.0}]}";
    private const string Query = "[out:json];node(1);out;";

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    private static HttpResponseMessage ServerError() => new(HttpStatusCode.InternalServerError);

    /// <summary>The mirrors' hosts in list order — the order a client with no cooldowns asks them.</summary>
    private static readonly string[] Hosts = OverpassClient.MirrorUrls.Select(u => new Uri(u).Host).ToArray();

    private static OverpassClient ClientOver(HttpMessageHandler handler, ConcurrentDictionary<string, DateTime>? cooldowns = null)
        => new(new HttpClient(handler), cooldowns ?? new ConcurrentDictionary<string, DateTime>());

    [Fact]
    public async Task A_fresh_mirror_with_the_data_beats_an_empty_answer_from_the_one_before()
    {
        // The regional-mirror defence: the first mirror answers "nothing" (as overpass.osm.ch did
        // for everywhere outside Switzerland) and the next fresh one has the airport.
        var handler = new PerHostMirror(host => host == Hosts[0] ? Ok(Empty) : Ok(Content));

        string? body = await ClientOver(handler).PostAsync(Query, CancellationToken.None);

        Assert.Equal(Content, body);
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task An_empty_answer_is_believed_once_one_more_fresh_mirror_agrees()
    {
        // A small strip with no mapped hangar, apron or tower is a real answer; returning null
        // there would have the store retry it every five minutes for the whole session. It costs
        // TWO round-trips — the answer and one confirmation — never a sweep of the list.
        var handler = new PerHostMirror(_ => Ok(Empty));

        string? body = await ClientOver(handler).PostAsync(Query, CancellationToken.None);

        Assert.NotNull(body);
        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task A_third_mirror_is_never_asked_just_to_confirm_an_empty()
    {
        // The accepted cost of the bound: data on a THIRD mirror is not looked for once two have
        // agreed on "nothing". The list holds planet-wide instances only, so two empty answers
        // from it are the truth about the airport.
        var handler = new PerHostMirror(host => host == Hosts[2] ? Ok(Content) : Ok(Empty));

        string? body = await ClientOver(handler).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task A_confirmation_that_fails_leaves_the_held_empty_answer_standing()
    {
        var cooldowns = new ConcurrentDictionary<string, DateTime>();
        var handler = new PerHostMirror(host => host == Hosts[0] ? Ok(Empty) : ServerError());

        string? body = await ClientOver(handler, cooldowns).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
        Assert.True(cooldowns.ContainsKey(OverpassClient.MirrorUrls[1]), "the mirror that failed is cooled as usual");
        Assert.False(cooldowns.ContainsKey(OverpassClient.MirrorUrls[0]), "the one that answered empty is not");
    }

    [Fact]
    public async Task A_cooled_down_mirror_is_never_asked_to_confirm_an_empty()
    {
        // Every mirror but the first is cooling: no FRESH mirror is left to confirm with, so the
        // empty answer is returned after ONE request.
        var handler = new PerHostMirror(host => host == Hosts[0] ? Ok(Empty) : Ok(Content));

        string? body = await ClientOver(handler, CoolingAllBut(0)).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(Hosts[0], Assert.Single(handler.Asked));
    }

    [Fact]
    public async Task Cooled_down_mirrors_are_still_asked_when_every_fresh_one_failed()
    {
        // The cooldown only ever REORDERS the attempts for an answer: with the one fresh mirror
        // down, the cooled-down ones get their second pass as before.
        var handler = new PerHostMirror(host => host == Hosts[0] ? ServerError() : Ok(Content));

        string? body = await ClientOver(handler, CoolingAllBut(0)).PostAsync(Query, CancellationToken.None);

        Assert.Equal(Content, body);
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task The_confirmation_skips_a_cooled_down_mirror_to_reach_the_next_fresh_one()
    {
        // The SECOND mirror in the list is cooling and sits between two fresh ones. The
        // confirmation goes to the THIRD — the next FRESH mirror — and the cooled one is never
        // asked, not even last, although it is the one with the data: order is fresh-first by
        // cooldown, never by list position.
        var cooldowns = new ConcurrentDictionary<string, DateTime>();
        cooldowns[OverpassClient.MirrorUrls[1]] = DateTime.UtcNow.AddMinutes(5);
        var handler = new PerHostMirror(host => host == Hosts[1] ? Ok(Content) : Ok(Empty));

        string? body = await ClientOver(handler, cooldowns).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(new[] { Hosts[0], Hosts[2] }, handler.Asked);
    }

    /// <summary>A cooldown map in which every mirror except the <paramref name="freshIndex"/>-th is
    /// cooling for the next five minutes. Keys are mirror URLs, as the client records them.</summary>
    private static ConcurrentDictionary<string, DateTime> CoolingAllBut(int freshIndex)
    {
        var map = new ConcurrentDictionary<string, DateTime>();
        for (int i = 0; i < OverpassClient.MirrorUrls.Count; i++)
            if (i != freshIndex) map[OverpassClient.MirrorUrls[i]] = DateTime.UtcNow.AddMinutes(5);
        return map;
    }

    [Fact]
    public async Task A_mirror_answering_empty_is_never_blacklisted_for_it()
    {
        // An empty answer is not evidence the mirror is ill — it may simply be the truth about
        // that airport. Marking it failed would cool a healthy planet-wide mirror for five
        // minutes on the strength of one legitimately empty query, and the NEXT call would ask
        // it LAST. So it must be asked FIRST again. (The old version of this test compared call
        // counts, and still passed with the empty mirror blacklisted.)
        var handler = new PerHostMirror(host => host == Hosts[0] ? Ok(Empty) : Ok(Content));
        var client = ClientOver(handler);

        await client.PostAsync(Query, CancellationToken.None);
        await client.PostAsync(Query, CancellationToken.None);

        Assert.Equal(new[] { Hosts[0], Hosts[1], Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task A_mirror_that_fails_is_asked_last_next_time()
    {
        // The contrast that gives the test above its teeth: a REAL failure does move a mirror to
        // the back of the next call, so an order assertion can tell the two apart.
        var handler = new PerHostMirror(host => host == Hosts[0] ? ServerError() : Ok(Content));
        var client = ClientOver(handler);

        await client.PostAsync(Query, CancellationToken.None);
        await client.PostAsync(Query, CancellationToken.None);

        Assert.Equal(new[] { Hosts[0], Hosts[1], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task A_caller_that_gives_up_during_the_confirmation_still_gets_the_held_empty_answer()
    {
        // The first mirror has answered "nothing"; the caller's budget runs out while the second
        // is being asked. What was learned is still an answer — a well-formed empty body from a
        // mirror that worked — so PostAsync returns it rather than null (review OV-2). For the
        // taxiway-name fetch that is its whole answer; the buildings fetch gains it only in its
        // FALLBACK query, because after an empty AREA answer OsmFeatureSource.FetchAsync runs the
        // fallback on the same cancelled token, and a PostAsync that learned nothing is still null.
        var confirming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PerHostMirror(async (host, ct) =>
        {
            if (host == Hosts[0]) return Ok(Empty);
            confirming.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);    // a mirror that never answers in time
            return Ok(Content);
        });
        using var caller = new CancellationTokenSource();

        Task<string?> post = ClientOver(handler).PostAsync(Query, caller.Token);
        await confirming.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();
        string? body = await post.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(body);
        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
    }

    [Fact]
    public async Task A_caller_that_gives_up_before_any_mirror_answered_gets_null()
    {
        // Nothing was learned, so there is nothing to hand back — and the caller's cancel is not a
        // mirror failure: no second mirror is tried and none is cooled.
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cooldowns = new ConcurrentDictionary<string, DateTime>();
        var handler = new PerHostMirror(async (host, ct) =>
        {
            asking.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Ok(Content);
        });
        using var caller = new CancellationTokenSource();

        Task<string?> post = ClientOver(handler, cooldowns).PostAsync(Query, caller.Token);
        await asking.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();

        Assert.Null(await post.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(Hosts[0], Assert.Single(handler.Asked));
        Assert.Empty(cooldowns);
    }

    [Fact]
    public async Task An_empty_from_a_cooled_down_mirror_on_the_second_pass_is_returned_unconfirmed()
    {
        // Every mirror but the first is cooling. The one fresh mirror answers 500 (a real
        // failure), so the second pass reaches the cooled-down mirrors; the first of those
        // answers empty. There is no FRESH mirror left to confirm with — cooled-down mirrors are
        // never asked to confirm — so the empty answer is returned as-is (controller addition 4a).
        var handler = new PerHostMirror(host => host == Hosts[0] ? ServerError() : host == Hosts[1] ? Ok(Empty) : Ok(Content));

        string? body = await ClientOver(handler, CoolingAllBut(0)).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.Equal(new[] { Hosts[0], Hosts[1] }, handler.Asked);
    }

    [Fact]
    public async Task The_confirmation_after_a_failed_fresh_mirror_is_the_next_fresh_one_not_list_index_1()
    {
        // No cooldowns. The first mirror fails outright (not empty), so it holds nothing and the
        // SECOND mirror's empty answer becomes the held one. The confirmation is the NEXT FRESH
        // mirror after the HELD index (list index 2, the third mirror) — never plain list index 1,
        // which here is where the empty answer itself came from (controller addition 4b).
        var handler = new PerHostMirror(host => host == Hosts[0] ? ServerError() : host == Hosts[1] ? Ok(Empty) : Ok(Content));

        string? body = await ClientOver(handler).PostAsync(Query, CancellationToken.None);

        Assert.Equal(Content, body);
        Assert.Equal(new[] { Hosts[0], Hosts[1], Hosts[2] }, handler.Asked);
    }

    [Fact]
    public async Task Two_empty_answers_in_a_row_cool_neither_mirror()
    {
        // Two mirrors both answer empty: the first is held, the second confirms it. An empty
        // answer is never evidence a mirror is ill — not the held one, and not the confirming one
        // either — so afterwards the cooldown map contains neither (controller addition 4c).
        var cooldowns = new ConcurrentDictionary<string, DateTime>();
        var handler = new PerHostMirror(_ => Ok(Empty));

        string? body = await ClientOver(handler, cooldowns).PostAsync(Query, CancellationToken.None);

        Assert.Equal(OverpassClient.BodyKind.Empty, OverpassClient.ClassifyBody(body!));
        Assert.False(cooldowns.ContainsKey(OverpassClient.MirrorUrls[0]));
        Assert.False(cooldowns.ContainsKey(OverpassClient.MirrorUrls[1]));
    }
}
