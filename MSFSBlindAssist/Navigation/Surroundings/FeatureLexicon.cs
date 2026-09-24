using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// The ONE name lexicon all three naming tiers share, and the ONE order a NAME is read in.
/// <see cref="NamedKind"/> is how a name becomes Cargo, Fbo or Concourse in every tier —
/// OsmFeatureClassifier (TerminalKind and the named-building branch),
/// GsxTerminalFeatureSource.KindOf and SceneryModelNameClassifier all call it — because
/// AirportFeatureCatalog never merges across kinds, so one building a name reads as two kinds in
/// two tiers is listed twice. Three private orders had drifted apart (GSX read Cargo first, OSM
/// and the scenery Concourse first), as had three private vocabularies (the scenery FBO list
/// lacked half the OSM one, its concourse list had no "flugsteig", its de-ice word no "deicing").
/// </summary>
public static class FeatureLexicon
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// The concourse words, as DATA. <see cref="Concourse"/> is built from this list and so is the
    /// keyword set SceneryModelNameClassifier builds a concourse's spoken NAME from, so a word the
    /// kind test accepts can never be one the namer does not know. MUST stay declared above
    /// <see cref="Concourse"/>: static initialisers run in textual order.
    /// </summary>
    public static readonly IReadOnlyList<string> ConcourseWords = new[] { "concourse", "pier", "satellite", "flugsteig" };

    /// <summary>
    /// FBO words, POSITIVE only: the FBO CHAINS — brand names that ARE FBO operators: Signature,
    /// Million Air (also glued, "Millionair"), Sheltair, TAC Air, Clay Lacy — a jet or air centre
    /// such as Wilson Air Center, the "… Aviation" operator ("Narrows Aviation", "Jet Aviation",
    /// "Atlantic Aviation") and the GA phrasings ("Executive Terminal", "General Aviation
    /// Terminal"). A name is an FBO only through <see cref="IsFboName"/>, which also applies the
    /// office veto — this pattern alone is what a PREFILTER may ask, because one word more can never
    /// make it false. No bare "atlantic": it made EGLL's "Virgin Atlantic Upper Class" an FBO. No
    /// "avfuel" either: Avfuel is a FUEL brand — the catalog reads an "Avfuel" point as Fuel — and as
    /// an FBO word it would make a scenery "Avfuel Tank" an FBO while OSM's aeroway=fuel "Avfuel"
    /// stayed Fuel: one facility as two kinds, which the catalog never merges.
    /// </summary>
    public static readonly Regex Fbo = new(
        @"\b(aviation|jet ?cent(er|re)|fbo|air ?cent(er|re)|flight support|signature|million ?air|sheltair|tac ?air|clay ?lacy|executive|general aviation)\b", Opt);

    /// <summary>What makes an "… Aviation" name an office, a school or a government body rather than
    /// an FBO: "Civil Aviation Authority", the "City of Atlanta Department of Aviation" that operates
    /// a terminal, "Federal Aviation Administration", "Museum of Aviation", "Army Aviation Support
    /// Facility".</summary>
    private static readonly Regex NotFbo = new(
        @"\b(civil aviation|authority|administration|department|ministry|agency|division|museum|school|academy|college|university|security|police|military|army|navy|air force|national guard|coast guard|port of)\b", Opt);

    public static readonly Regex Cargo = new(@"\b(cargo|freight|fedex|ups|dhl)\b", Opt);

    public static readonly Regex Concourse = new(@"\b(" + string.Join("|", ConcourseWords.Select(Regex.Escape)) + @")\b", Opt);

    /// <summary>De-icing in every spelling a name uses — "deice", "de-ice", "de ice", "deicing",
    /// "de-icing", "deicer", "deiced" — and never a word that merely looks like it ("Device",
    /// "Deichmann"). The ONE pattern the OSM and scenery tiers share: the scenery's own copy had no
    /// "deicing", the old one here no "de ice". WHOLE WORDS only, and the trailing word boundary is
    /// a trade-off: it keeps out "de" followed by a word that merely continues past "ice" ("Hangar
    /// de Icelandair") and, with it, a glued compound — "Deicepad" is not read as de-icing, which
    /// the OSM tier's old <c>de-?ic</c> did (the scenery tier never did: its tokenizer splits only a
    /// camelCase "DeicePad" into two words).</summary>
    public static readonly Regex Deice = new(@"\bde[-\s]?ic(e[ds]?|ers?|ing)\b", Opt);

    /// <summary>Landside things a lexicon word can sit inside ("Executive Car Park").</summary>
    public static readonly Regex NotAirside = new(@"\b(car ?park|parking|garage|hotel|bus|station|shaft|city hall)\b", Opt);

    /// <summary>Is this an FBO's name? An FBO word (<see cref="Fbo"/>) and no office word.</summary>
    public static bool IsFboName(string? name)
        => !string.IsNullOrWhiteSpace(name) && Fbo.IsMatch(name) && !NotFbo.IsMatch(name);

    /// <summary>
    /// THE order a NAME is read in, in every tier: Cargo, then Fbo, then Concourse; null when the
    /// name says none of them, and the tier decides from its own evidence (tags, stand types, the
    /// scenery's other kind words).
    /// <list type="bullet">
    /// <item>Cargo FIRST: its words name the trade the building serves. "DHL Aviation", "Menzies
    /// Aviation Cargo" and "Virgin Atlantic Cargo" are cargo operations whose names also carry an
    /// FBO word, and a "Cargo Satellite" is a cargo building, not a passenger pier.</item>
    /// <item>Fbo before Concourse: an FBO word is a company or a GA phrasing, while "pier" and
    /// "satellite" are shapes any building can have. "Signature Flight Support" and "Jet Aviation"
    /// are FBOs; "Concourse B" is a concourse.</item>
    /// </list>
    /// </summary>
    public static FeatureKind? NamedKind(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (Cargo.IsMatch(name)) return FeatureKind.Cargo;
        if (IsFboName(name)) return FeatureKind.Fbo;
        if (Concourse.IsMatch(name)) return FeatureKind.Concourse;
        return null;
    }
}
