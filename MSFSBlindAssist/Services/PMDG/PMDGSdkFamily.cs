namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// A PMDG aircraft family whose SDK data broadcast MSFSBA reads: which options file each installed
/// variant keeps (<see cref="OptionsFileName"/>), which package folders hold a variant
/// (<see cref="PackagePrefix"/>, e.g. <c>pmdg-aircraft-738</c>), and whether the 777's center CDU
/// line is required. Every variant of a family has its own file, and each needs the lines.
/// </summary>
public sealed record PMDGSdkFamily(string DisplayName, string FilePrefix, string PackagePrefix, bool RequiresCenterCdu)
{
    public static readonly PMDGSdkFamily B737 = new("PMDG 737", "737", "pmdg-aircraft-73", RequiresCenterCdu: false);
    public static readonly PMDGSdkFamily B777 = new("PMDG 777", "777", "pmdg-aircraft-77", RequiresCenterCdu: true);

    /// <summary>The family a profile's <c>AircraftCode</c> names, or null for any non-PMDG profile.</summary>
    public static PMDGSdkFamily? ForAircraftCode(string? aircraftCode) => aircraftCode switch
    {
        "PMDG_737" => B737,
        "PMDG_777" => B777,
        _ => null,
    };

    public string OptionsFileName => $"{FilePrefix}_Options.ini";

    private static readonly Dictionary<string, string> VariantNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pmdg-aircraft-736"] = "737-600",
        ["pmdg-aircraft-737"] = "737-700",
        ["pmdg-aircraft-738"] = "737-800",
        ["pmdg-aircraft-739"] = "737-900",
        ["pmdg-aircraft-77er"] = "777-200ER",
        ["pmdg-aircraft-77l"] = "777-200LR",
        ["pmdg-aircraft-77f"] = "777F",
        ["pmdg-aircraft-77w"] = "777-300ER",
    };

    /// <summary>The aircraft a package folder holds, as a pilot names it ("737-800"); a folder this
    /// table does not know reads as its own name.</summary>
    public static string VariantName(string packageFolder)
        => VariantNames.TryGetValue(packageFolder, out var name) ? name : packageFolder;
}
