using System.Globalization;

namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// The Settings tab's status sentence when no job is running. Never empty: an empty read-only
/// box reads as a broken control. Numbers and dates are invariant ("10 October 2026",
/// "8.4 million") whatever the pilot's Windows region says.
/// </summary>
internal static class LocationDataStatusText
{
    internal static string Idle(LocationDataInfo? info, int schemaVersion, long partialBytes)
    {
        if (info is { Complete: true } && info.SchemaVersion == schemaVersion)
        {
            var parts = new List<string> { "Ready." };
            if (FormatDate(info.GeoNamesDate) is { } date) parts.Add($"GeoNames data from {date},");
            if (!string.IsNullOrEmpty(info.NaturalEarthRelease)) parts.Add($"Natural Earth {info.NaturalEarthRelease},");
            parts.Add($"{Millions(info.PlaceCount)} places.");
            return string.Join(" ", parts);
        }
        if (info is not null)
            return "This version of MSFS Blind Assist needs newer location data. Press Update location data.";
        if (partialBytes > 0)
            return $"Not downloaded yet. Partial download kept: {DiskSpaceRequirement.Spoken(partialBytes)}. Press Download location data to resume.";
        return $"Not downloaded. About {DiskSpaceRequirement.Spoken(LocationDataDownloader.GeoNamesZipEstimateBytes + LocationDataDownloader.NaturalEarthDownloadEstimateBytes)} to download; "
            + $"building needs about {DiskSpaceRequirement.Spoken(DiskSpaceRequirement.BeforeDownload(LocationDataDownloader.GeoNamesZipEstimateBytes))} of free disk space, "
            + $"and the finished data takes about {DiskSpaceRequirement.Spoken(DiskSpaceRequirement.BuiltBytes)}. "
            + "Output C and Location Info need it.";
    }

    /// <summary>"8.4 million" from 8,361,204; smaller counts in full.</summary>
    internal static string Millions(long count) => count >= 1_000_000
        ? string.Create(CultureInfo.InvariantCulture, $"{count / 1_000_000.0:0.0} million")
        : count.ToString(CultureInfo.InvariantCulture);

    /// <summary>"2026-10-10" → "10 October 2026"; null when unreadable.</summary>
    internal static string? FormatDate(string isoDate) =>
        DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : null;
}
