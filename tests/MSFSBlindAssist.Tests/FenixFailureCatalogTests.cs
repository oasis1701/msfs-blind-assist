// Characterization tests for FenixFailureCatalog: parsing of the Fenix v3 manual-failures catalog,
// its states and the EFB-style search. The fixture is the first chapter (ATA 21) only.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixFailureCatalogTests
{
    internal static string Load()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-failures-manual-ch21.json"));

    private const string Gear = "{\"id\":32,\"title\":\"Landing gear\",\"shortTitle\":\"Gear\",\"groups\":[{\"groupName\":\"Brake fan\",\"failures\":[{\"id\":\"F_TEST_BRAKE_FAN\",\"title\":\"Brake fan system\",\"failureCondition\":null,\"failed\":false}]}]}";
    private const string Comms = "{\"id\":23,\"title\":\"Communications\",\"shortTitle\":\"Communications\",\"groups\":[{\"groupName\":\"Test\",\"failures\":[{\"id\":\"F_TEST_A\",\"title\":\"Alpha\",\"failureCondition\":null,\"failed\":false},{\"id\":\"F_TEST_B\",\"title\":\"Bravo\",\"failureCondition\":null,\"failed\":false}]}]}";

    /// <summary>The fixture's JSON with two inline chapters (32, 23) appended to "atas".</summary>
    internal static string MultiJson()
    {
        string json = Load().TrimEnd();
        int close = json.LastIndexOf(']');
        return json.Substring(0, close).TrimEnd() + "," + Gear + "," + Comms + json.Substring(close);
    }

    internal static FenixFailureCatalog Multi() => FenixFailureCatalog.Parse(MultiJson())!;

    private static FenixFailureCatalog Inline(string failuresJson)
        => FenixFailureCatalog.Parse("{\"atas\":[{\"id\":1,\"title\":\"T\",\"shortTitle\":\"T\",\"groups\":[{\"groupName\":\"G\",\"failures\":[" + failuresJson + "]}]}]}")!;

    [Fact]
    public void Parse_reads_the_first_chapter()
    {
        var cat = FenixFailureCatalog.Parse(Load())!;
        Assert.Single(cat.Chapters);
        Assert.Equal(57, cat.FailureCount);
        var ch = cat.Chapters[0];
        Assert.Equal(21, ch.Ata);
        Assert.Equal("Air Conditioning", ch.Title);
        Assert.Equal("Air", ch.ShortTitle);
        Assert.Equal(14, ch.Groups.Count);
        Assert.Equal(4, ch.Groups.Count(g => g.Failures.Count == 1));

        var first = ch.Groups[0].Failures[0];
        Assert.Equal("F_PNEUMATIC_LAV_GAL_FAN", first.Id);
        Assert.Equal("Lavatory/Galley fan", first.Title);
        Assert.Equal(FenixFailureState.Normal, first.State);
        Assert.Null(first.Condition);
        Assert.False(ch.Groups[0].ShowsHeader);
        Assert.Equal("Lavatory/Galley Ventilation", ch.Groups[0].Name);
        Assert.Equal("CPC", ch.Groups[1].Name);
        Assert.True(ch.Groups[1].ShowsHeader);

        var last = ch.AllFailures.Last();
        Assert.Equal("F_PNEUMATIC_AVNCS_COND_AIR_INLET_VALVE_MECH_SECURED_OPEN", last.Id);
        Assert.Equal("Conditioning air inlet valve secured open", last.Title);
        Assert.Equal("Ventilation valves", ch.Groups[^1].Name);
        Assert.Equal((0, 0), cat.Counts());
    }

    [Fact]
    public void Parse_keeps_the_Fenix_chapter_order()
    {
        var cat = Multi();
        Assert.Equal(new[] { 21, 32, 23 }, cat.Chapters.Select(c => c.Ata));
        Assert.Equal(60, cat.FailureCount);
    }

    [Fact]
    public void Parse_reads_states()
    {
        var cat = Inline(
            "{\"id\":\"A\",\"title\":\"a\",\"failed\":false,\"failureCondition\":{\"ias\":250,\"afterEvent\":\"Takeoff\",\"afterEventSeconds\":30}}," +
            "{\"id\":\"B\",\"title\":\"b\",\"failed\":true,\"failureCondition\":null}," +
            "{\"id\":\"C\",\"title\":\"c\",\"failed\":false,\"failureCondition\":{}}," +
            "{\"id\":\"D\",\"title\":\"d\",\"failed\":true,\"failureCondition\":{\"ias\":1}}," +
            "{\"id\":\"E\",\"title\":\"e\",\"failed\":false,\"failureCondition\":{\"alt\":10000.0,\"time\":\"2026-10-21T14:32:05Z\"}}," +
            "{\"id\":\"F\",\"title\":\"f\",\"failed\":false,\"failureCondition\":{\"time\":1792593125000}}");

        var a = cat.Find("A")!;
        Assert.Equal(FenixFailureState.Armed, a.State);
        Assert.Equal(250, a.Condition!.Ias);
        Assert.Equal("Takeoff", a.Condition.AfterEvent);
        Assert.Equal(30, a.Condition.AfterEventSeconds);
        Assert.False(a.Condition.IsEmpty);

        Assert.Equal(FenixFailureState.Failed, cat.Find("B")!.State);

        var c = cat.Find("C")!;
        Assert.Equal(FenixFailureState.Armed, c.State);
        Assert.True(c.Condition!.IsEmpty);

        Assert.Equal(FenixFailureState.Failed, cat.Find("D")!.State);

        var e = cat.Find("E")!.Condition!;
        Assert.Equal(10000, e.AltAbove);
        Assert.Equal("2026-10-21T14:32:05Z", e.Time);
        Assert.Equal("1792593125000", cat.Find("F")!.Condition!.Time);
    }

    [Fact]
    public void Parse_reads_the_real_countdown_reply()
    {
        var cat = Inline(
            "{\"id\":\"T\",\"title\":\"t\",\"failed\":false,\"failureCondition\":{\"id\":4,\"ias\":null,\"alt\":null,\"altb\":null,\"time\":30,\"afterEvent\":null,\"afterEventSeconds\":null}}");
        var t = cat.Find("T")!;
        Assert.Equal(FenixFailureState.Armed, t.State);
        Assert.Equal("30", t.Condition!.Time);
        Assert.Null(t.Condition.Ias);
        Assert.Null(t.Condition.AfterEvent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"atas\":\"x\"}")]
    public void Parse_rejects_what_is_not_the_catalog(string? json)
        => Assert.Null(FenixFailureCatalog.Parse(json));

    [Fact]
    public void Parse_skips_a_failure_without_an_id()
    {
        var cat = Inline("{\"title\":\"no id\",\"failed\":false},{\"id\":\"X\",\"title\":\"x\",\"failed\":false}");
        Assert.Equal(1, cat.FailureCount);
    }

    private static string[] Titles(IEnumerable<FenixFailureChapter> chapters)
        => chapters.SelectMany(c => c.AllFailures).Select(f => f.Title).ToArray();

    [Fact]
    public void Filter_matches_like_the_EFB()
    {
        var cat = FenixFailureCatalog.Parse(Load())!;
        var all = cat.Filter("", FenixFailureFilter.All);
        Assert.Single(all);
        Assert.Equal(57, all[0].AllFailures.Count());

        foreach (string q in new[] { "fan", "FAN" })
        {
            var r = cat.Filter(q, FenixFailureFilter.All);
            Assert.Single(r);
            Assert.Equal(new[] { "Lavatory/Galley fan", "Recirculation fans" }.OrderBy(x => x), Titles(r).OrderBy(x => x));
        }

        Assert.Equal(new[] { "CPC 1", "CPC 2" }, Titles(cat.Filter("cpc", FenixFailureFilter.All)));
        Assert.Equal(57, cat.Filter("ata 21", FenixFailureFilter.All)[0].AllFailures.Count());
        Assert.Empty(cat.Filter("zzz", FenixFailureFilter.All));

        var multi = Multi();
        var fan = multi.Filter("fan", FenixFailureFilter.All);
        Assert.Equal(new[] { 21, 32 }, fan.Select(c => c.Ata));
        Assert.Equal(
            new[] { "Lavatory/Galley fan", "Recirculation fans", "Brake fan system" }.OrderBy(x => x),
            Titles(fan).OrderBy(x => x));

        var ata23 = multi.Filter("ata 23", FenixFailureFilter.All);
        Assert.Single(ata23);
        Assert.Equal(new[] { "Alpha", "Bravo" }, Titles(ata23));
    }

    [Fact]
    public void Filter_by_state()
    {
        var cat = FenixFailureCatalog.Parse(Load())!;
        var first = cat.Chapters[0].AllFailures.First();
        var failed = cat.With(first with { Failed = true });

        Assert.Equal(new[] { first.Title }, Titles(failed.Filter("", FenixFailureFilter.Failed)));
        Assert.Empty(failed.Filter("", FenixFailureFilter.Armed));
        Assert.Single(Titles(failed.Filter("", FenixFailureFilter.ArmedOrFailed)));
        Assert.Equal((0, 1), failed.Counts());

        // CPC has two failures; fail one and the filtered group has one, so no header.
        var cpc2 = cat.Find("F_PNEUMATIC_CPC_2")!;
        var cut = cat.With(cpc2 with { Failed = true }).Filter("", FenixFailureFilter.Failed);
        var group = cut[0].Groups.Single();
        Assert.Equal("CPC", group.Name);
        Assert.False(group.ShowsHeader);
    }

    [Fact]
    public void With_replaces_by_id()
    {
        var cat = FenixFailureCatalog.Parse(Load())!;
        var original = cat.Find("F_PNEUMATIC_LAV_GAL_FAN")!;
        var replacement = original with { Failed = true };
        var next = cat.With(replacement);

        Assert.Same(replacement, next.Find("F_PNEUMATIC_LAV_GAL_FAN"));
        Assert.Equal(cat.FailureCount, next.FailureCount);
        Assert.Same(original, cat.Find("F_PNEUMATIC_LAV_GAL_FAN"));
    }
}
