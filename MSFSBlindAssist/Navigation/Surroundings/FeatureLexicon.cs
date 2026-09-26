using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// The one name lexicon every naming tier (OSM, GSX, scenery) shares, and the one order a name is
/// read in (<see cref="NamedKind"/>). The catalog never merges across kinds, so a name read as two
/// kinds in two tiers would list one building twice.
/// </summary>
public static class FeatureLexicon
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// The concourse words. <see cref="Concourse"/> and SceneryModelNameClassifier's concourse namer
    /// are both built from this list, so they cannot drift. Must stay above <see cref="Concourse"/>:
    /// static initialisers run in textual order.
    /// </summary>
    public static readonly IReadOnlyList<string> ConcourseWords = new[] { "concourse", "pier", "satellite", "flugsteig" };

    /// <summary>
    /// FBO words, positive only (the office veto is <see cref="IsFboName"/>; a prefilter may ask this
    /// alone). The chains are brands that ARE FBO operators. No bare "atlantic" (EGLL's "Virgin
    /// Atlantic Upper Class") and no "avfuel", a fuel brand the catalog reads as Fuel.
    /// </summary>
    public static readonly Regex Fbo = new(
        @"\b(aviation|jet ?cent(er|re)|fbo|air ?cent(er|re)|flight support|signature|million ?air|sheltair|tac ?air|clay ?lacy|executive|general aviation)\b", Opt);

    /// <summary>Office, school and government words that veto an FBO reading ("Civil Aviation
    /// Authority", "City of Atlanta Department of Aviation", "Port of Seattle Aviation Maintenance").</summary>
    private static readonly Regex NotFbo = new(
        @"\b(civil aviation|authority|administration|department|ministry|agency|division|museum|school|academy|college|university|security|police|military|army|navy|air force|national guard|coast guard|port of)\b", Opt);

    public static readonly Regex Cargo = new(@"\b(cargo|freight|fedex|ups|dhl)\b", Opt);

    public static readonly Regex Concourse = new(@"\b(" + string.Join("|", ConcourseWords.Select(Regex.Escape)) + @")\b", Opt);

    /// <summary>De-icing in every spelling ("deice", "de-ice", "de ice", "deicing", "deicer",
    /// "deiced"), whole words only: never "Device", "Hangar de Icelandair" or a glued "Deicepad".</summary>
    public static readonly Regex Deice = new(@"\bde[-\s]?ic(e[ds]?|ers?|ing)\b", Opt);

    /// <summary>Landside things a lexicon word can sit inside ("Executive Car Park").</summary>
    public static readonly Regex NotAirside = new(@"\b(car ?park|parking|garage|hotel|bus|station|shaft|city hall)\b", Opt);

    /// <summary>Is this an FBO's name? An FBO word (<see cref="Fbo"/>) and no office word.</summary>
    public static bool IsFboName(string? name)
        => !string.IsNullOrWhiteSpace(name) && Fbo.IsMatch(name) && !NotFbo.IsMatch(name);

    /// <summary>
    /// The order a name is read in, in every tier: Cargo, then Fbo, then Concourse; null when it says
    /// none. Cargo first because "DHL Aviation" or a "Cargo Satellite" is cargo; Fbo before Concourse
    /// because "pier" and "satellite" are shapes any building has.
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
