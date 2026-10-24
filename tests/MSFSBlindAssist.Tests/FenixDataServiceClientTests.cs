// Characterization tests for FenixDataService, the one client of the Fenix data service (the local
// gateway on 127.0.0.1:8083) that the Fenix aircraft definition owns. The HTTP side runs against a
// fake HttpMessageHandler, never the network. The WebSocket side cannot run without a server, so
// only its pure message helpers are tested here, and no test calls Subscribe on a live service
// (that would open a socket to whatever answers on 8083, the pilot's sim included).
//
// The contract the Fenix windows and monitors build on: with the Fenix not loaded or the gateway down, every
// read is Unknown (never 0, false or "off"), every write is false and nothing throws.

using System.Net;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Services;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Tests;

public class FenixDataServiceClientTests
{
    // ---- Fake gateway ---------------------------------------------------------------------------

    private sealed record SeenRequest(HttpMethod Method, string Uri, string? MediaType, string? CharSet, string? Body);

    private sealed class FakeGateway : HttpMessageHandler
    {
        private readonly Func<string?, HttpResponseMessage> _answer;
        public readonly List<SeenRequest> Requests = new();

        public FakeGateway(Func<string?, HttpResponseMessage> answer) { _answer = answer; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            lock (Requests)
            {
                Requests.Add(new SeenRequest(
                    request.Method,
                    request.RequestUri?.ToString() ?? "",
                    request.Content?.Headers.ContentType?.MediaType,
                    request.Content?.Headers.ContentType?.CharSet,
                    body));
            }
            return _answer(body);
        }
    }

    private static FakeGateway Answering(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status) { Content = new StringContent(json) });

    private static FakeGateway Down()
        => new(_ => throw new HttpRequestException("No connection could be made because the target machine actively refused it."));

    // ---- The service is down --------------------------------------------------------------------

    [Fact]
    public async Task ReadAsync_returns_unknown_when_the_service_is_down()
    {
        using var service = new FenixDataService(Down());

        Assert.Equal(FenixValueKind.EmptyOrUnknown, (await service.ReadAsync("x")).Kind);
        var many = await service.ReadManyAsync(["a", "b"]);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, many["a"].Kind);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, many["b"].Kind);

        Assert.False(await service.WriteIntAsync("x", 1));
        Assert.False(await service.WriteBoolAsync("x", true));
        Assert.False(await service.WriteFloatAsync("x", 1.5));
        Assert.False(await service.WriteStringAsync("x", "on"));

        Assert.Empty(await service.GetSettingsAsync());
        Assert.Null(await service.GetSoftwareVersionAsync());
        Assert.Null(await service.QueryAsync("query { x }"));
        Assert.Null(await service.GetRestAsync("/fenix/failures/manual"));
        Assert.Null(await service.PostRestAsync("/fenix/failures/saveManual", "{}"));
    }

    // ---- Reads ----------------------------------------------------------------------------------

    [Fact]
    public async Task ReadAsync_reads_the_value()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"r0\":{\"value\":59.3}}}}");
        using var service = new FenixDataService(gateway);

        var value = await service.ReadAsync("aircraft.engines.1.n2");

        Assert.Equal(FenixValueKind.Number, value.Kind);
        Assert.Equal(59.3, value.Number);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://127.0.0.1:8083/graphql", request.Uri);
        Assert.Equal(
            "{\"query\":\"query { dataRef { r0: dataRef(name: \\\"aircraft.engines.1.n2\\\") { value } } }\"}",
            request.Body);
    }

    [Fact]
    public async Task ReadAsync_keeps_a_date_like_text_exactly()
    {
        // A default JSON reader turns this into a DateTime and writes it back as "...08:30:00.5Z".
        var gateway = Answering("{\"data\":{\"dataRef\":{\"r0\":{\"value\":\"2026-10-21T08:30:00.500Z\"}}}}");
        using var service = new FenixDataService(gateway);

        var value = await service.ReadAsync("x");

        Assert.Equal(FenixValueKind.Text, value.Kind);
        Assert.Equal("2026-10-21T08:30:00.500Z", value.Text);
    }

    [Fact]
    public async Task ReadManyAsync_with_no_names_sends_nothing()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{}}}");
        using var service = new FenixDataService(gateway);

        var values = await service.ReadManyAsync([]);

        Assert.Empty(values);
        Assert.Empty(gateway.Requests);
    }

    // ---- Writes ---------------------------------------------------------------------------------

    [Fact]
    public async Task Write_returns_false_when_the_service_reports_errors()
    {
        using var service = new FenixDataService(Answering("{\"errors\":[{\"message\":\"x\"}]}"));

        Assert.False(await service.WriteIntAsync("x", 1));
    }

    [Fact]
    public async Task Write_returns_true_when_the_service_accepts_it()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeInt\":true}}}");
        using var service = new FenixDataService(gateway);

        Assert.True(await service.WriteIntAsync("x", 1));

        var request = Assert.Single(gateway.Requests);
        Assert.Equal("http://127.0.0.1:8083/graphql", request.Uri);
        Assert.Equal(
            "{\"query\":\"mutation ($name: String!, $value: Int!) { dataRef { writeInt(name: $name, value: $value) } }\",\"variables\":{\"name\":\"x\",\"value\":1}}",
            request.Body);
    }

    [Theory]
    [InlineData("{\"data\":{\"dataRef\":{\"writeInt\":false}}}", false)]
    [InlineData("{\"data\":{\"dataRef\":{\"writeInt\":true}}}", true)]
    // Only an explicit false is a refusal: anything else keeps the 200-without-errors reading.
    [InlineData("{\"data\":{\"dataRef\":{}}}", true)]
    [InlineData("{\"data\":{\"dataRef\":{\"writeInt\":null}}}", true)]
    [InlineData("{\"data\":{}}", true)]
    public async Task Write_returns_false_only_when_the_service_answers_false(string answer, bool expected)
    {
        using var service = new FenixDataService(Answering(answer));

        Assert.Equal(expected, await service.WriteIntAsync("x", 1));
    }

    [Fact]
    public async Task Each_write_kind_reads_its_own_result()
    {
        using var service = new FenixDataService(Answering(
            "{\"data\":{\"dataRef\":{\"writeInt\":true,\"writeBool\":false,\"writeFloat\":false,\"writeString\":false}}}"));

        Assert.True(await service.WriteIntAsync("x", 1));
        Assert.False(await service.WriteBoolAsync("x", true));
        Assert.False(await service.WriteFloatAsync("x", 1.5));
        Assert.False(await service.WriteStringAsync("x", "on"));
    }

    [Fact]
    public void WriteFloatAsync_sends_an_invariant_decimal_point_under_a_comma_culture()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeFloat\":true}}}");
        using var service = new FenixDataService(gateway);

        bool ok = TestCulture.Run("de-DE", () => service.WriteFloatAsync("x", 59.3).GetAwaiter().GetResult());

        Assert.True(ok);
        Assert.EndsWith("\"variables\":{\"name\":\"x\",\"value\":59.3}}", Assert.Single(gateway.Requests).Body);
    }

    [Fact]
    public async Task WriteFloatAsync_never_sends_a_number_that_is_not_finite()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"writeFloat\":true}}}");
        using var service = new FenixDataService(gateway);

        Assert.False(await service.WriteFloatAsync("x", double.NaN));
        Assert.False(await service.WriteFloatAsync("x", double.PositiveInfinity));
        Assert.Empty(gateway.Requests);
    }

    // ---- Queries and settings -------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_returns_data_only_from_a_clean_answer()
    {
        using (var clean = new FenixDataService(Answering("{\"data\":{\"fap\":{\"ok\":true}}}")))
        {
            var data = await clean.QueryAsync("query { fap { ok } }", new { id = 3 });
            Assert.NotNull(data);
            Assert.True((bool)data!["fap"]!["ok"]!);
        }

        using (var withErrors = new FenixDataService(Answering("{\"data\":{\"fap\":null},\"errors\":[{\"message\":\"no\"}]}")))
            Assert.Null(await withErrors.QueryAsync("query { fap { ok } }"));

        using (var serverError = new FenixDataService(Answering("{\"data\":{\"fap\":{\"ok\":true}}}", HttpStatusCode.InternalServerError)))
            Assert.Null(await serverError.QueryAsync("query { fap { ok } }"));
    }

    [Theory]
    [InlineData("{\"data\":{\"dataRef\":{\"r0\":{\"value\":1},\"writeInt\":true}},\"errors\":[\"x\"]}")]
    [InlineData("{\"data\":{\"dataRef\":{\"r0\":{\"value\":1},\"writeInt\":true}},\"errors\":[null]}")]
    public async Task An_errors_entry_that_is_not_an_object_is_still_an_error_and_never_throws(string answer)
    {
        // Reading "message" off a JValue throws in Newtonsoft; that must not reach the caller, and the
        // data beside the errors must not be taken as a clean answer either.
        using var service = new FenixDataService(Answering(answer));

        Assert.Null(await service.QueryAsync("query { x }"));
        Assert.False(await service.WriteIntAsync("x", 1));
        Assert.Equal(FenixValueKind.EmptyOrUnknown, (await service.ReadAsync("x")).Kind);
    }

    [Fact]
    public async Task An_empty_errors_list_is_no_error()
    {
        using var service = new FenixDataService(
            Answering("{\"data\":{\"dataRef\":{\"r0\":{\"value\":59.3},\"writeInt\":true}},\"errors\":[]}"));

        Assert.NotNull(await service.QueryAsync("query { x }"));
        Assert.True(await service.WriteIntAsync("x", 1));
        Assert.Equal(59.3, (await service.ReadAsync("x")).Number);
    }

    [Fact]
    public async Task QueryAsync_sends_the_query_and_its_variables()
    {
        var gateway = Answering("{\"data\":{}}");
        using var service = new FenixDataService(gateway);

        await service.QueryAsync("query ($id: Int!) { x(id: $id) }", new { id = 3 });
        await service.QueryAsync("query { y }");

        Assert.Equal("{\"query\":\"query ($id: Int!) { x(id: $id) }\",\"variables\":{\"id\":3}}", gateway.Requests[0].Body);
        Assert.Equal("{\"query\":\"query { y }\"}", gateway.Requests[1].Body);
    }

    [Fact]
    public async Task Settings_and_software_version_come_from_the_settings_query()
    {
        var gateway = Answering(
            "{\"data\":{\"settings\":{\"softwareVersion\":\"3.0.0.7426\",\"state\":{\"settings\":[" +
            "{\"key\":\"weight_unit\",\"category\":\"units\",\"value\":\"KG\",\"possibleValues\":[\"KG\",\"LBS\"],\"source\":\"user\",\"type\":\"enum\",\"writable\":true}]}}}}");
        using var service = new FenixDataService(gateway);

        var setting = Assert.Single(await service.GetSettingsAsync());
        Assert.Equal("weight_unit", setting.Key);
        Assert.Equal("KG", setting.Value);
        Assert.Equal("3.0.0.7426", await service.GetSoftwareVersionAsync());
        Assert.All(gateway.Requests, r => Assert.Equal(
            "{\"query\":\"query { settings { softwareVersion state { settings { key category value possibleValues source type writable } } } }\"}",
            r.Body));
    }

    // ---- REST -----------------------------------------------------------------------------------

    [Fact]
    public async Task Rest_calls_go_to_the_gateway_path_and_return_the_body()
    {
        var gateway = Answering("[{\"id\":1}]");
        using var service = new FenixDataService(gateway);

        Assert.Equal("[{\"id\":1}]", await service.GetRestAsync("/fenix/failures/manual"));
        Assert.Equal("[{\"id\":1}]", await service.PostRestAsync("/fenix/failures/saveManual", "{\"id\":7}"));

        var get = gateway.Requests[0];
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal("http://127.0.0.1:8083/fenix/failures/manual", get.Uri);

        // The EFB's own saveManual call sends a bare application/json content type.
        var post = gateway.Requests[1];
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.Equal("http://127.0.0.1:8083/fenix/failures/saveManual", post.Uri);
        Assert.Equal("application/json", post.MediaType);
        Assert.Null(post.CharSet);
        Assert.Equal("{\"id\":7}", post.Body);
    }

    [Fact]
    public async Task Rest_calls_return_null_on_a_failed_status()
    {
        using var service = new FenixDataService(Answering("not here", HttpStatusCode.NotFound));

        Assert.Null(await service.GetRestAsync("/fenix/nothing"));
        Assert.Null(await service.PostRestAsync("/fenix/nothing", "{}"));
    }

    [Fact]
    public async Task Rest_path_without_a_leading_slash_is_refused_without_a_request()
    {
        // "@host/x" after the base would make 127.0.0.1:8083 the user name and send the call elsewhere.
        var gateway = Answering("{}");
        using var service = new FenixDataService(gateway);

        Assert.Null(await service.GetRestAsync("@example.com/x"));
        Assert.Null(await service.PostRestAsync("fenix/failures/saveManual", "{}"));
        Assert.Empty(gateway.Requests);
    }

    // ---- Lifetime -------------------------------------------------------------------------------

    [Fact]
    public async Task Calls_after_dispose_fail_quietly()
    {
        var gateway = Answering("{\"data\":{\"dataRef\":{\"r0\":{\"value\":1}}}}");
        var service = new FenixDataService(gateway);
        service.Dispose();

        Assert.Equal(FenixValueKind.EmptyOrUnknown, (await service.ReadAsync("x")).Kind);
        Assert.False(await service.WriteIntAsync("x", 1));
        Assert.Null(await service.QueryAsync("query { x }"));
        Assert.Null(await service.GetRestAsync("/x"));
        var handle = service.Subscribe(["x"], (_, _) => { });
        handle.Dispose();
        handle.Dispose();
        service.Dispose();

        Assert.Empty(gateway.Requests);
    }

    // ---- Subscription messages (pure helpers of the socket loop) ---------------------------------

    [Fact]
    public void Subscribe_message_carries_its_id_and_names()
    {
        var message = JObject.Parse(FenixDataService.BuildSubscribeMessage("7", ["aircraft.engines.1.n2", "x"]));

        Assert.Equal("7", (string?)message["id"]);
        Assert.Equal("subscribe", (string?)message["type"]);
        Assert.Equal(
            "subscription ($names: [String!]!) { dataRefs(names: $names) { name value } }",
            (string?)message["payload"]!["query"]);
        Assert.Equal(new[] { "aircraft.engines.1.n2", "x" }, message["payload"]!["variables"]!["names"]!.ToObject<string[]>());
    }

    [Fact]
    public void Next_message_reads_one_data_ref()
    {
        var message = FenixDataService.ParseMessage(
            "{\"id\":\"1\",\"type\":\"next\",\"payload\":{\"data\":{\"dataRefs\":{\"name\":\"aircraft.engines.1.n2\",\"value\":59.3}}}}");

        var entry = Assert.Single(FenixDataService.ReadNextEntries(message!));
        Assert.Equal("aircraft.engines.1.n2", entry.Key);
        Assert.Equal(FenixValueKind.Number, entry.Value.Kind);
        Assert.Equal(59.3, entry.Value.Number);
    }

    [Fact]
    public void Next_message_reads_many_data_refs_and_skips_an_entry_with_no_name()
    {
        var message = FenixDataService.ParseMessage(
            "{\"id\":\"1\",\"type\":\"next\",\"payload\":{\"data\":{\"dataRefs\":[" +
            "{\"name\":\"bogus.name\",\"value\":\"\"},{\"value\":1},{\"name\":\"a\",\"value\":\"2026-10-21T08:30:00.500Z\"}]}}}");

        var entries = FenixDataService.ReadNextEntries(message!);

        Assert.Equal(2, entries.Count);
        Assert.Equal("bogus.name", entries[0].Key);
        Assert.Equal(FenixValueKind.EmptyOrUnknown, entries[0].Value.Kind);
        Assert.Equal("a", entries[1].Key);
        Assert.Equal("2026-10-21T08:30:00.500Z", entries[1].Value.Text);
    }

    [Fact]
    public void Malformed_socket_text_is_no_message()
    {
        Assert.Null(FenixDataService.ParseMessage("not json"));
        Assert.Null(FenixDataService.ParseMessage("[1,2]"));
        Assert.Empty(FenixDataService.ReadNextEntries(JObject.Parse("{\"type\":\"next\",\"payload\":{\"errors\":[]}}")));
    }

    // ---- Address ([A320-40]) --------------------------------------------------------------------

    [Fact]
    public void Never_addresses_the_service_by_name()
    {
        string source = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Services", "FenixDataService.cs"));

        Assert.DoesNotMatch(new Regex(@"(?i)(ws|http)s?://localhost"), source);
        Assert.Contains("\"http://127.0.0.1:8083\"", source);
        Assert.Contains("\"ws://127.0.0.1:8083/graphql\"", source);
    }

    // ---- No navigraph operation (the FNX-3 full text) -------------------------------------------

    // The gateway's navigraph field returns the pilot's Navigraph access token and can disconnect
    // their account; nothing in the app needs it. Comments are removed before the scan, so one may
    // still say "never calls navigraph { … }" (two do); string literals are kept, since that is where
    // a query would be written.
    private static readonly Regex CommentOrString = new(
        @"""(?:\\.|[^""\\\r\n])*""|//[^\r\n]*|/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // "navigraph" opening a selection or taking arguments, as a word of its own (or right after a
    // "\n" escape in a query string). A settings key such as "navigraph_refresh_token" is not one.
    private static readonly Regex NavigraphSelection = new(
        @"(?i)(?:(?<![a-z0-9_])|(?<=\\[nrt]))navigraph\s*[{(]", RegexOptions.CultureInvariant);

    private static bool SendsNavigraphOperation(string source)
        => NavigraphSelection.IsMatch(CommentOrString.Replace(source, m => m.Value[0] == '"' ? m.Value : ""));

    [Theory]
    [InlineData("await QueryAsync(\"query { navigraph { accessToken } }\");", true)]
    [InlineData("var q = \"mutation {\\n  navigraph(x: 1) { disconnect { success } } }\";", true)]
    [InlineData("var q = \"query {\\nnavigraph { accessToken } }\";", true)]
    [InlineData("var s = \"// navigraph { x }\";", true)]
    [InlineData("// the service never calls navigraph { … }", false)]
    [InlineData("/// <c>navigraph { ... }</c> queries", false)]
    [InlineData("/* navigraph { } */ var x = 1;", false)]
    [InlineData("var s = \"http://127.0.0.1:8083\"; // navigraph { }", false)]
    [InlineData("var key = \"navigraph_refresh_token\";", false)]
    [InlineData("var key = \"navigraph_navdata_mode\"; Foo(navigraph_navdata_mode);", false)]
    [InlineData("token = GetNavigraphToken(1);", false)]
    public void The_navigraph_scan_finds_a_selection_and_ignores_comments_and_settings_keys(string source, bool found)
        => Assert.Equal(found, SendsNavigraphOperation(source));

    [Fact]
    public void No_Fenix_code_sends_a_navigraph_operation()
    {
        string root = ClaudeContextBudgetTests.RepoRoot();
        string app = Path.Combine(root, "MSFSBlindAssist");
        var files = new List<string>();
        files.AddRange(Directory.GetFiles(Path.Combine(app, "Services"), "Fenix*.cs"));
        files.AddRange(Directory.GetFiles(Path.Combine(app, "Aircraft"), "Fenix*.cs"));
        foreach (string folder in Directory.GetDirectories(Path.Combine(app, "Forms"), "Fenix*"))
            files.AddRange(Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(Path.Combine(app, "FirstOfficer", "Fenix"), "*.cs", SearchOption.AllDirectories));

        // The scan must really have the Fenix code in front of it.
        foreach (string expected in new[] { "FenixDataService.cs", "FenixA320Definition.cs", "FenixWindowsForm.cs", "FenixStateEvaluator.cs" })
            Assert.Contains(files, f => Path.GetFileName(f) == expected);

        List<string> offenders = files.Where(f => SendsNavigraphOperation(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(offenders.Count == 0,
            "The gateway's navigraph field returns the pilot's Navigraph access token and can disconnect their account, and "
            + $"nothing in the app needs it (docs/invariants/fenix-data.md#fnx-3). Found a navigraph selection in: {string.Join(", ", offenders)}");
    }
}
