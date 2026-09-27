using MSFSBlindAssist.Navigation.Surroundings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The ONE name lexicon and the ONE order a name is read in (PR #230 review PC-5 / OV-5 / CL-5).
/// The precedence rows are the review's names; the "not an FBO" rows include the measured OSM
/// names that were wrongly FBOs (EGLL's "Virgin Atlantic Upper Class", the operator tag
/// "City of Atlanta Department of Aviation" on a terminal).
/// </summary>
public class FeatureLexiconTests
{
    [Theory]
    [InlineData("DHL Aviation", FeatureKind.Cargo)]              // a cargo airline whose name carries an FBO word
    [InlineData("Menzies Aviation Cargo", FeatureKind.Cargo)]
    [InlineData("Virgin Atlantic Cargo", FeatureKind.Cargo)]
    [InlineData("Cargo Satellite", FeatureKind.Cargo)]            // a cargo building, not a passenger pier
    [InlineData("Signature Flight Support", FeatureKind.Fbo)]
    [InlineData("Jet Aviation", FeatureKind.Fbo)]
    [InlineData("Concourse B", FeatureKind.Concourse)]
    [InlineData("Terminal 1 Flugsteig A", FeatureKind.Concourse)]
    public void A_name_is_read_cargo_first_then_fbo_then_concourse(string name, FeatureKind kind)
        => Assert.Equal(kind, FeatureLexicon.NamedKind(name));

    // The FBO chains — Signature, Million Air, Sheltair, TAC Air, Clay Lacy — are brand names that ARE
    // FBO operators; "Millionair" is the glued spelling a scenery model name can carry.
    [Theory]
    [InlineData("Signature Flight Support")] [InlineData("Atlantic Aviation")] [InlineData("Jet Aviation")]
    [InlineData("Million Air")] [InlineData("Millionair")] [InlineData("Sheltair")] [InlineData("Wilson Air Center")]
    [InlineData("TAC Air")] [InlineData("Clay Lacy")] [InlineData("Executive Terminal")]
    [InlineData("General Aviation Terminal")] [InlineData("Narrows Aviation")]
    public void A_real_fbo_is_an_fbo(string name)
    {
        Assert.True(FeatureLexicon.IsFboName(name), name);
        Assert.Equal(FeatureKind.Fbo, FeatureLexicon.NamedKind(name));
    }

    [Fact]
    public void A_fuel_brand_is_not_an_fbo()
    {
        // Avfuel is a fuel BRAND, not an FBO chain: the catalog reads an "Avfuel" point as Fuel
        // (AirportFeatureCatalogTests), and as an FBO word it would make a scenery "Avfuel Tank" an
        // FBO while OSM's aeroway=fuel "Avfuel" stayed Fuel — one facility as two kinds, which the
        // catalog never merges.
        Assert.False(FeatureLexicon.IsFboName("Avfuel"));
        Assert.Null(FeatureLexicon.NamedKind("Avfuel"));
    }

    [Theory]
    [InlineData("Civil Aviation Authority")]
    [InlineData("Virgin Atlantic Upper Class")]                   // EGLL, live: an airline's own check-in wing
    [InlineData("City of Atlanta Department of Aviation")]        // the operator tag of a terminal
    [InlineData("Federal Aviation Administration")]
    [InlineData("Museum of Aviation")]
    [InlineData("Army Aviation Support Facility")]
    [InlineData("Port of Seattle Aviation Maintenance")]           // KSEA, sweep: the port authority's works yard
    public void An_office_or_an_airline_facility_is_not_an_fbo(string name)
    {
        Assert.False(FeatureLexicon.IsFboName(name), name);
        Assert.Null(FeatureLexicon.NamedKind(name));
    }

    [Theory]
    [InlineData("Deice Pad 1")] [InlineData("De-Ice Apron")] [InlineData("de ice pad")] [InlineData("Deicing Pad")]
    [InlineData("De-icing pad")] [InlineData("Deicer bay")] [InlineData("Remote deicing facility")]
    public void Every_deice_spelling_is_a_deice_word(string name) => Assert.Matches(FeatureLexicon.Deice, name);

    [Theory]
    [InlineData("Device Room")] [InlineData("Deichmann")] [InlineData("Dice")] [InlineData("Apron 3")]
    public void A_word_that_only_starts_like_deice_is_not_one(string name) => Assert.DoesNotMatch(FeatureLexicon.Deice, name);

    // The pattern ends at a word boundary, and that is a trade-off pinned here on purpose: it keeps out
    // "de" followed by a word that merely CONTINUES past "ice", and with it a glued compound, which the
    // OSM tier's old `de-?ic` read as de-icing (the scenery tier never did — its tokenizer splits only a
    // camelCase "DeicePad" into two words).
    [Theory]
    [InlineData("Hangar de Icelandair")]
    [InlineData("Deicepad")]
    public void A_deice_word_ends_where_the_word_ends(string name) => Assert.DoesNotMatch(FeatureLexicon.Deice, name);

    [Fact]
    public void The_concourse_pattern_is_the_concourse_list()
    {
        Assert.Contains("flugsteig", FeatureLexicon.ConcourseWords);
        foreach (string w in FeatureLexicon.ConcourseWords)
            Assert.Equal(FeatureKind.Concourse, FeatureLexicon.NamedKind($"{w} A"));
        Assert.Null(FeatureLexicon.NamedKind("Terminal A"));           // "terminal" is a kind of its own, not a concourse word
    }

    [Fact]
    public void A_blank_name_says_nothing()
    {
        Assert.Null(FeatureLexicon.NamedKind(""));
        Assert.Null(FeatureLexicon.NamedKind(null));
        Assert.False(FeatureLexicon.IsFboName("   "));
    }
}
