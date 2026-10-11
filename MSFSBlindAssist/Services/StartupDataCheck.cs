using MSFSBlindAssist.Services.Geography;

namespace MSFSBlindAssist.Services;

/// <summary>What the startup data check offers: the navigation database when none is built for
/// either simulator, location data when it is not downloaded or was built for an older version.</summary>
internal sealed record StartupDataNeeds(bool NavigationDatabase, bool LocationData, bool LocationDataUpdate)
{
    internal bool Any => NavigationDatabase || LocationData;
}

/// <summary>
/// Decides what the launch-time "Set up MSFS Blind Assist" dialog offers and composes its text
/// (UPD-9). A new pilot otherwise learns that taxi guidance or Output C needs data only when
/// pressing the key mid-flight. The dialog shows on EVERY launch while something is missing,
/// governed only by its setting, never by a remembered dismissal (the update check's precedent).
/// </summary>
internal static class StartupDataCheck
{
    internal const string SettingName = "Check for missing data when the app starts";

    /// <param name="enabled">The Updates tab's setting.</param>
    /// <param name="navigationDatabaseBuilt">A navigation database exists for either simulator:
    /// connecting switches between them, so one built is enough not to nag.</param>
    /// <param name="locationReadyAtNextStart">A verified build waits for the startup sweep to swap
    /// it in: a download now would fetch 400 MB only to delete it.</param>
    internal static StartupDataNeeds Find(bool enabled, bool navigationDatabaseBuilt,
        bool locationAvailable, bool locationNeedsUpdate, bool locationReadyAtNextStart)
    {
        if (!enabled) return new StartupDataNeeds(false, false, false);
        bool location = !locationAvailable && !locationReadyAtNextStart;
        return new StartupDataNeeds(!navigationDatabaseBuilt, location, location && locationNeedsUpdate);
    }

    /// <summary>The dialog's read-only box, one fact per line. <paramref name="locationDownloading"/>
    /// is set once the pilot started the download from the dialog.</summary>
    internal static string Describe(StartupDataNeeds needs, bool locationDownloading = false)
    {
        var lines = new List<string> { "Some data MSFS Blind Assist needs is not set up yet." };
        if (needs.NavigationDatabase)
            lines.Add("Navigation database: not built. Taxi guidance, teleport, ILS guidance and flight plans need it. "
                + "Build it in Database Settings; it takes 2 to 5 minutes, and for MSFS 2024 the simulator must be running at the main menu.");
        if (needs.LocationData)
        {
            string size = DiskSpaceRequirement.Spoken(
                LocationDataDownloader.GeoNamesZipEstimateBytes + LocationDataDownloader.NaturalEarthDownloadEstimateBytes);
            lines.Add(locationDownloading
                ? "Location data: downloading. Its progress is spoken, and it keeps running when this window closes."
                : (needs.LocationDataUpdate
                    ? "Location data: needs an update for this version of MSFS Blind Assist."
                    : "Location data: not downloaded.")
                  + $" Output C and Location Info need it. About {size} to download, in the background.");
        }
        lines.Add($"To stop this check, turn off \"{SettingName}\" in Settings, Updates tab.");
        return string.Join("\r\n", lines);
    }
}
