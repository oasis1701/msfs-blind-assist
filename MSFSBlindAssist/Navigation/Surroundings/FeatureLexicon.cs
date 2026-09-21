using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// The ONE name lexicon all three naming tiers share: OsmFeatureClassifier uses every pattern
/// here, GsxTerminalFeatureSource.KindOf reads a section header with Cargo/Fbo/Concourse, and
/// SceneryModelNameClassifier's kind table takes Concourse, Fbo and Cargo. Three private copies
/// had already drifted (the scenery FBO list lacked half the OSM one, and its concourse list had
/// no "flugsteig"), so one building classified by two sources came out as two kinds — and
/// AirportFeatureCatalog never merges across kinds.
/// </summary>
public static class FeatureLexicon
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static readonly Regex Fbo = new(
        @"\b(aviation|jet ?cent(er|re)|fbo|air ?cent(er|re)|flight support|signature|atlantic|million air|executive|general aviation)\b", Opt);
    public static readonly Regex Cargo = new(@"\b(cargo|freight|fedex|ups|dhl)\b", Opt);
    public static readonly Regex Concourse = new(@"\b(concourse|pier|satellite|flugsteig)\b", Opt);
    public static readonly Regex Deice = new(@"de-?ic", Opt);
    /// <summary>Landside things a lexicon word can sit inside ("Executive Car Park").</summary>
    public static readonly Regex NotAirside = new(@"\b(car ?park|parking|garage|hotel|bus|station|shaft|city hall)\b", Opt);
}
