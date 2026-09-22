using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A feature's name is SPOKEN verbatim, so a mangled byte is a mangled word in a blind pilot's ear
/// — "Hangar S?d 4" for "Hangar Süd 4". Non-ASCII names are the NORM outside English-speaking
/// countries (Zürich, Düsseldorf, Köln, Malmö, København, 東京), so this is not an edge case; it is
/// most of Europe and Asia.
///
/// <para>Overpass hands back UTF-8 and <see cref="JsonDocument"/> reads UTF-8, so the pipeline
/// should be clean end to end — these pin that it stays so. The check is worth having because the
/// failure is SILENT and cosmetic-looking in a log while being unintelligible in speech, and
/// because a future "optimisation" that reads the body as ASCII or through the system code page
/// would pass every other test in this suite.</para>
///
/// <para>The names below are real OSM names at real airports. <c>Hangar Süd 4</c> and
/// <c>Flugsportzentrum Tirol</c> are LOWI's, seen live 2026-09-22.</para>
/// </summary>
public class SpokenNameEncodingTests
{
    private static AirportFeature? Classify(string name, string tags = "\"aeroway\":\"hangar\"")
    {
        string json = "{\"type\":\"node\",\"id\":1,\"lat\":47.26,\"lon\":11.34,\"tags\":{"
                    + tags + ",\"name\":" + JsonSerializer.Serialize(name) + "}}";
        using var doc = JsonDocument.Parse(json);
        return OsmFeatureClassifier.Classify(doc.RootElement);
    }

    [Theory]
    [InlineData("Hangar Süd 4")]                 // LOWI, live
    [InlineData("Flugsportzentrum Tirol")]        // LOWI, live
    [InlineData("Hangar Nord 1 – 3")]             // en dash, as OSM writes it
    [InlineData("Køge Lufthavn Hangar")]
    [InlineData("Hangar Málaga")]
    [InlineData("Ангар 2")]                       // Cyrillic
    [InlineData("格納庫 1")]                       // Japanese
    public void A_non_ascii_name_reaches_the_feature_unchanged(string name)
    {
        var f = Classify(name);
        Assert.NotNull(f);
        Assert.Equal(name, f!.Name);
        Assert.Equal(name, f.SpokenName);
        Assert.True(f.HasProperName);
    }

    [Fact]
    public void The_json_reader_decodes_utf8_bytes_not_the_system_code_page()
    {
        // The real wire shape: UTF-8 BYTES, as HttpClient hands them over. Reading these through
        // Encoding.Default (the Windows code page) turns "ü" into two characters, which is exactly
        // how "Hangar Süd 4" becomes "Hangar SÃ¼d 4" or "Hangar S?d 4".
        const string name = "Hangar Süd 4";
        string json = "{\"elements\":[{\"type\":\"node\",\"id\":1,\"lat\":47.26,\"lon\":11.34,"
                    + "\"tags\":{\"aeroway\":\"hangar\",\"name\":" + JsonSerializer.Serialize(name) + "}}]}";
        byte[] utf8 = Encoding.UTF8.GetBytes(json);

        var parsed = OsmFeatureSource.Parse(Encoding.UTF8.GetString(utf8));

        var f = Assert.Single(parsed);
        Assert.Equal(name, f.SpokenName);
        Assert.DoesNotContain('�', f.SpokenName);   // the replacement character
        Assert.DoesNotContain('?', f.SpokenName);
    }

    [Fact]
    public void A_name_is_never_silently_emptied_by_normalisation()
    {
        // Whatever the merge does to compare names, what is SPOKEN must still be the real one: a
        // feature whose name normalises to nothing would announce as the bare kind word, and a
        // pilot would hear "Hangar" where the airport says "格納庫 1".
        var f = Classify("格納庫 1");
        Assert.NotNull(f);
        Assert.False(string.IsNullOrWhiteSpace(f!.SpokenName));
        Assert.NotEqual(FeatureKindWords.Generic(FeatureKind.Hangar), f.SpokenName);
    }
}
