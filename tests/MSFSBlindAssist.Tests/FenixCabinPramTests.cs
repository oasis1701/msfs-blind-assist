// Characterization tests for FenixCabinPram, the Fenix FAP's audio side (the PRAM: boarding music,
// loudspeaker volume, prerecorded announcements and their memo, the announcement variant). The
// expected GraphQL texts and variable names are the FAP bundle's own (tools/fenix-gateway/fetch_gateway_bundles.py
// fetches the bundle and lists its operation texts); the state shape is what the gateway answered on
// 2026-10-21. The HTTP side runs against a fake HttpMessageHandler, never the network.

using System.Net;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixCabinPramTests
{
    private const string AfterTakeoffId = "bc69d460-bfb0-4945-ac49-4da6d695d962";
    private const string SafetyBriefingId = "ab9c425f-4b60-483b-9679-bab553460bcb";

    // The live read of 2026-10-21 (the sim on the ground, boarding music off).
    private const string LiveState =
        "{\"fap\":{\"pram\":{\"state\":{\"bgmOn\":false,\"bgmChannels\":[{\"id\":\"f5957b3b-9ba4-4933-822f-806acf7f0cb7\",\"name\":\"Boarding Music\"}]," +
        "\"bgmSelectedChannel\":0,\"volume\":5,\"onAnnounce\":null,\"announcementsMemo\":[],\"playingAllInMemo\":false," +
        "\"prerecordedAnnouncements\":[{\"id\":\"" + AfterTakeoffId + "\",\"name\":\"After Takeoff\"},{\"id\":\"" + SafetyBriefingId + "\",\"name\":\"Safety Briefing\"}]}}," +
        "\"announcementsVariant\":{\"preferredVariant\":0,\"maxVariant\":0}}}";

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    // ---- Parse ----------------------------------------------------------------------------------

    [Fact]
    public void Parse_reads_the_live_state_of_2026_10_21()
    {
        var state = FenixCabinPram.Parse(JObject.Parse(LiveState));

        Assert.NotNull(state);
        Assert.False(state.MusicOn);
        var channel = Assert.Single(state.Channels);
        Assert.Equal("Boarding Music", channel.Name);
        Assert.Equal("f5957b3b-9ba4-4933-822f-806acf7f0cb7", channel.Id);
        Assert.Equal(0, state.SelectedChannel);
        Assert.Equal(5, state.Volume);
        Assert.Null(state.NowAnnouncing);
        Assert.Empty(state.Memo);
        Assert.False(state.PlayingAllInMemo);
        Assert.Equal(new[] { "After Takeoff", "Safety Briefing" }, state.Announcements.Select(a => a.Name));
        Assert.Equal(new[] { AfterTakeoffId, SafetyBriefingId }, state.Announcements.Select(a => a.Id));
        Assert.Equal(0, state.PreferredVariant);
        Assert.Equal(0, state.MaxVariant);
    }

    [Fact]
    public void Parse_reads_a_playing_announcement_and_the_memo()
    {
        var data = JObject.Parse(LiveState);
        var pram = (JObject)data["fap"]!["pram"]!["state"]!;
        pram["onAnnounce"] = JObject.Parse("{\"name\":\"Safety Briefing\"}");
        pram["announcementsMemo"] = JArray.Parse("[{\"id\":\"" + AfterTakeoffId + "\",\"name\":\"After Takeoff\"}]");
        pram["playingAllInMemo"] = true;
        pram["bgmOn"] = true;
        pram["bgmSelectedChannel"] = 0;
        pram["volume"] = 7;
        data["fap"]!["announcementsVariant"] = JObject.Parse("{\"preferredVariant\":1,\"maxVariant\":2}");

        var state = FenixCabinPram.Parse(data);

        Assert.NotNull(state);
        Assert.Equal("Safety Briefing", state.NowAnnouncing);
        var memo = Assert.Single(state.Memo);
        Assert.Equal(AfterTakeoffId, memo.Id);
        Assert.Equal("After Takeoff", memo.Name);
        Assert.True(state.PlayingAllInMemo);
        Assert.True(state.MusicOn);
        Assert.Equal(7, state.Volume);
        Assert.Equal(1, state.PreferredVariant);
        Assert.Equal(2, state.MaxVariant);
    }

    [Fact]
    public void Parse_is_null_without_fap_state()
    {
        Assert.Null(FenixCabinPram.Parse(null));
        Assert.Null(FenixCabinPram.Parse(JObject.Parse("{}")));
        Assert.Null(FenixCabinPram.Parse(JObject.Parse("{\"fap\":{\"pram\":{\"state\":null}}}")));
    }

    [Fact]
    public void Parse_reads_prerecordedAnnouncements_that_is_not_an_array_as_none()
    {
        var data = JObject.Parse(LiveState);
        data["fap"]!["pram"]!["state"]!["prerecordedAnnouncements"] = "x";

        var state = FenixCabinPram.Parse(data);

        Assert.NotNull(state);
        Assert.Empty(state.Announcements);
        Assert.Equal(5, state.Volume);
    }

    [Theory]
    [InlineData("{\"fap\":\"x\"}")]
    [InlineData("{\"fap\":{\"pram\":\"x\"}}")]
    [InlineData("{\"fap\":{\"pram\":{\"state\":\"x\"}}}")]
    [InlineData("{\"fap\":[1,2]}")]
    public void Parse_never_throws_on_a_shape_it_does_not_know(string json)
    {
        Assert.Null(FenixCabinPram.Parse(JObject.Parse(json)));
    }

    [Fact]
    public void Parse_reads_odd_fields_inside_a_state_as_empty_and_keeps_the_rest()
    {
        var data = JObject.Parse(LiveState);
        var state = (JObject)data["fap"]!["pram"]!["state"]!;
        state["onAnnounce"] = "x";
        state["bgmChannels"] = JObject.Parse("{}");
        state["volume"] = "loud";
        data["fap"]!["announcementsVariant"] = "x";

        var parsed = FenixCabinPram.Parse(data);

        Assert.NotNull(parsed);
        Assert.Null(parsed.NowAnnouncing);
        Assert.Empty(parsed.Channels);
        Assert.Equal(0, parsed.Volume);
        Assert.Equal(0, parsed.PreferredVariant);
        Assert.Equal(0, parsed.MaxVariant);
        Assert.Equal(2, parsed.Announcements.Count);
    }

    // ---- Mutations ------------------------------------------------------------------------------

    private sealed record Case(Func<FenixCabinMutation> Make, string Query, string? Variables, string ResultField, bool UnderPram);

    private static readonly IReadOnlyDictionary<string, Case> Cases = new Dictionary<string, Case>
    {
        ["SetMusicOn(true)"] = new(() => FenixCabinPram.SetMusicOn(true),
            "mutation SetBoardingMusicOn($on: Boolean!) { fap { pram { setBgmOn(on: $on) { success } } } }",
            "{\"on\":true}", "setBgmOn", true),
        ["SetVolume(6)"] = new(() => FenixCabinPram.SetVolume(6),
            "mutation SetVolume($volume: Int!) { fap { pram { setVolume(volume: $volume) { success } } } }",
            "{\"volume\":6}", "setVolume", true),
        ["SetChannel(0)"] = new(() => FenixCabinPram.SetChannel(0),
            "mutation SetBgmChannel($channelIdx: Int!) { fap { pram { setBgmChannel(channelIdx: $channelIdx) { success } } } }",
            "{\"channelIdx\":0}", "setBgmChannel", true),
        ["AddToMemo"] = new(() => FenixCabinPram.AddToMemo(AfterTakeoffId),
            "mutation AddToAnnouncementsMemo($announcementId: UUID!) { fap { pram { addToMemo(announcementId: $announcementId) { success } } } }",
            "{\"announcementId\":\"" + AfterTakeoffId + "\"}", "addToMemo", true),
        ["RemoveFromMemo(0)"] = new(() => FenixCabinPram.RemoveFromMemo(0),
            "mutation RemoveFromAnnouncementsMemo($index: Int!) { fap { pram { removeFromMemo(index: $index) { success } } } }",
            "{\"index\":0}", "removeFromMemo", true),
        ["ClearMemo"] = new(() => FenixCabinPram.ClearMemo(),
            "mutation ClearAnnouncementsMemo { fap { pram { clearMemo { success } } } }",
            null, "clearMemo", true),
        ["DirectPlay"] = new(() => FenixCabinPram.DirectPlay(AfterTakeoffId),
            "mutation DirectPlayAnnouncement($announcementId: UUID!) { fap { pram { directPlay(announcementId: $announcementId) { success } } } }",
            "{\"announcementId\":\"" + AfterTakeoffId + "\"}", "directPlay", true),
        ["PlayNext"] = new(() => FenixCabinPram.PlayNext(),
            "mutation PlayNextAnnouncement { fap { pram { playNext { success } } } }",
            null, "playNext", true),
        ["PlayAll"] = new(() => FenixCabinPram.PlayAll(),
            "mutation PlayAllAnnouncements { fap { pram { playAll { success } } } }",
            null, "playAll", true),
        ["Stop"] = new(() => FenixCabinPram.Stop(),
            "mutation StopAnnouncements { fap { pram { stop { success } } } }",
            null, "stop", true),
        ["SetAnnouncementsVariant(1)"] = new(() => FenixCabinPram.SetAnnouncementsVariant(1),
            "mutation SetAnnouncementsVariant($variant: Int!) { fap { setAnnouncementsVariant(variant: $variant) { success } } }",
            "{\"variant\":1}", "setAnnouncementsVariant", false),
    };

    public static IEnumerable<object[]> CaseNames => Cases.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Mutations_are_the_FAP_bundle_documents(string name)
    {
        var expected = Cases[name];

        var mutation = expected.Make();

        Assert.Equal(expected.Query, Collapse(mutation.Query));
        Assert.Equal(expected.ResultField, mutation.ResultField);
        Assert.Equal(expected.UnderPram, mutation.UnderPram);
        if (expected.Variables == null)
            Assert.Null(mutation.Variables);
        else
            Assert.Equal(expected.Variables, JObject.FromObject(mutation.Variables!).ToString(Formatting.None));
    }

    // ---- Succeeded ------------------------------------------------------------------------------

    [Fact]
    public void Succeeded_reads_success()
    {
        var music = FenixCabinPram.SetMusicOn(true);

        Assert.True(FenixCabinPram.Succeeded(music, JObject.Parse("{\"fap\":{\"pram\":{\"setBgmOn\":{\"success\":true}}}}")));
        Assert.False(FenixCabinPram.Succeeded(music, JObject.Parse("{\"fap\":{\"pram\":{\"setBgmOn\":{\"success\":false}}}}")));
        Assert.False(FenixCabinPram.Succeeded(music, null));

        var variant = FenixCabinPram.SetAnnouncementsVariant(1);
        Assert.True(FenixCabinPram.Succeeded(variant, JObject.Parse("{\"fap\":{\"setAnnouncementsVariant\":{\"success\":true}}}")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"fap\":null}")]
    [InlineData("{\"fap\":\"x\"}")]
    [InlineData("{\"fap\":{\"pram\":null}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setBgmOn\":null}}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setBgmOn\":{}}}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setBgmOn\":{\"success\":null}}}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setBgmOn\":{\"success\":\"true\"}}}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setBgmOn\":{\"success\":1}}}}")]
    [InlineData("{\"fap\":{\"pram\":{\"setVolume\":{\"success\":true}}}}")]
    [InlineData("{\"fap\":{\"setBgmOn\":{\"success\":true}}}")]
    public void Succeeded_is_false_for_anything_but_an_explicit_true_in_the_right_place(string json)
    {
        Assert.False(FenixCabinPram.Succeeded(FenixCabinPram.SetMusicOn(true), JObject.Parse(json)));
    }

    [Fact]
    public void Succeeded_looks_under_pram_only_for_the_pram_mutations()
    {
        var variant = FenixCabinPram.SetAnnouncementsVariant(1);

        Assert.False(FenixCabinPram.Succeeded(variant, JObject.Parse("{\"fap\":{\"pram\":{\"setAnnouncementsVariant\":{\"success\":true}}}}")));
    }

    // ---- StateQuery -----------------------------------------------------------------------------

    [Fact]
    public void StateQuery_is_a_query()
    {
        string query = FenixCabinPram.StateQuery;

        Assert.StartsWith("query", query);
        Assert.Contains("bgmChannels { id name }", query);
        Assert.Contains("prerecordedAnnouncements", query);
        Assert.Contains("announcementsVariant { preferredVariant maxVariant }", query);
        Assert.DoesNotContain("mutation", query);
        Assert.Equal(
            "query { fap { pram { state { bgmOn bgmChannels { id name } bgmSelectedChannel volume onAnnounce { name } announcementsMemo { id name } playingAllInMemo prerecordedAnnouncements } } announcementsVariant { preferredVariant maxVariant } } }",
            query);
    }

    // ---- SendAsync ------------------------------------------------------------------------------

    private sealed class StubGateway : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _answer;
        public readonly List<string?> Bodies = new();

        public StubGateway(Func<HttpResponseMessage> answer) { _answer = answer; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Bodies) Bodies.Add(body);
            return _answer();
        }
    }

    private static StubGateway Answering(string json)
        => new(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });

    [Fact]
    public async Task SendAsync_follows_success()
    {
        using (var accepted = new FenixDataService(Answering("{\"data\":{\"fap\":{\"pram\":{\"setVolume\":{\"success\":true}}}}}")))
            Assert.True(await accepted.SendAsync(FenixCabinPram.SetVolume(6)));

        using (var refused = new FenixDataService(Answering("{\"data\":{\"fap\":{\"pram\":{\"setVolume\":{\"success\":false}}}}}")))
            Assert.False(await refused.SendAsync(FenixCabinPram.SetVolume(6)));

        using (var down = new FenixDataService(new StubGateway(() => throw new HttpRequestException("refused"))))
            Assert.False(await down.SendAsync(FenixCabinPram.SetVolume(6)));
    }

    [Fact]
    public async Task SendAsync_is_false_when_the_service_answers_errors()
    {
        using var service = new FenixDataService(Answering("{\"data\":{\"fap\":{\"pram\":{\"setVolume\":{\"success\":true}}}},\"errors\":[{\"message\":\"no\"}]}"));

        Assert.False(await service.SendAsync(FenixCabinPram.SetVolume(6)));
    }

    [Fact]
    public async Task SendAsync_posts_the_document_and_its_variables()
    {
        var gateway = Answering("{\"data\":{\"fap\":{\"pram\":{\"directPlay\":{\"success\":true}}}}}");
        using var service = new FenixDataService(gateway);
        var mutation = FenixCabinPram.DirectPlay(AfterTakeoffId);

        Assert.True(await service.SendAsync(mutation));

        var body = JObject.Parse(Assert.Single(gateway.Bodies)!);
        Assert.Equal(mutation.Query, (string?)body["query"]);
        Assert.Equal("{\"announcementId\":\"" + AfterTakeoffId + "\"}", body["variables"]!.ToString(Formatting.None));
    }

    [Fact]
    public async Task SendAsync_sends_no_variables_for_a_mutation_without_any()
    {
        var gateway = Answering("{\"data\":{\"fap\":{\"pram\":{\"stop\":{\"success\":true}}}}}");
        using var service = new FenixDataService(gateway);

        Assert.True(await service.SendAsync(FenixCabinPram.Stop()));

        Assert.Null(JObject.Parse(Assert.Single(gateway.Bodies)!)["variables"]);
    }
}
