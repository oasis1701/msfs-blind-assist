namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// The package folders navdatareader recorded for an airport (airport.scenery_local_path),
/// e.g. "fs-base-genericairports, C:\...\Community\orbx-airport-ktiw-tacoma-narrows" — an MSFS
/// 2020 build; an MSFS 2024 one records a path for NO airport, which is why
/// <see cref="SceneryPackageCensus"/> exists. These are the packages the INDEXER is then handed;
/// the census finds its own by scanning Community, so this is no longer the only way one is named.
/// </summary>
public static class SceneryPackageLocator
{
    public static List<string> PackageDirs(string sceneryLocalPath, Func<string, bool> dirExists)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sceneryLocalPath)) return result;
        foreach (var raw in sceneryLocalPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.Contains('\\') && !raw.Contains('/')) continue;                       // "fs-base-genericairports": no folder
            if (raw.EndsWith("navigraph-navdata", StringComparison.OrdinalIgnoreCase)) continue;
            if (result.Contains(raw, StringComparer.OrdinalIgnoreCase)) continue;
            if (!dirExists(raw)) continue;
            result.Add(raw);
        }
        return result;
    }
}
