namespace MSFSBlindAssist.Services;

/// <summary>What the startup data check offers: the navigation database when none is built for the
/// simulator the app opens, location data when there is none it can use.</summary>
internal sealed record StartupDataNeeds(bool NavigationDatabase, bool LocationData)
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

    /// <param name="enabled">The Updates tab's setting. Off, nothing is probed.</param>
    /// <param name="simulatorVersion">The simulator the app last connected to
    /// (<c>UserSettings.SimulatorVersion</c>): its database is the one every feature opens. One built
    /// for the other simulator does not count: connecting switches this setting to the running
    /// simulator whether or not its database exists.</param>
    /// <param name="locationReadyAtNextStart">A verified build waits for the startup sweep to swap
    /// it in: a download now would fetch 400 MB only to delete it. Read BEFORE
    /// <paramref name="locationAvailable"/>: the sweep may promote it meanwhile, and in the other
    /// order a promotion between the two reads would find neither.</param>
    internal static StartupDataNeeds Find(bool enabled, string? simulatorVersion, Func<string, bool> navigationDatabaseExists,
        Func<bool> locationReadyAtNextStart, Func<bool> locationAvailable)
    {
        if (!enabled) return new StartupDataNeeds(false, false);
        bool location = !locationReadyAtNextStart() && !locationAvailable();
        return new StartupDataNeeds(!navigationDatabaseExists(simulatorVersion ?? "FS2020"), location);
    }

    /// <summary>The dialog's read-only box, one fact per line. <paramref name="locationStatus"/> is the
    /// location data job's own status, as the GeoNames tab shows it (what is installed, a kept
    /// download, the disk space needed, or the running job's progress); <paramref name="locationRunning"/>
    /// adds that the job outlives the window.</summary>
    internal static string Describe(StartupDataNeeds needs, string locationStatus, bool locationRunning = false)
    {
        var lines = new List<string> { "Some data MSFS Blind Assist needs is not set up yet." };
        if (needs.NavigationDatabase)
            lines.Add("Navigation database: not built. Taxi guidance, teleport, ILS guidance and flight plans need it. "
                + "Build it in the File menu, Nav Database; it takes 2 to 5 minutes, and for MSFS 2024 the simulator must be running at the main menu.");
        if (needs.LocationData)
        {
            lines.Add("Location data: " + locationStatus);
            if (locationRunning) lines.Add("It keeps running when this window closes, and its progress is spoken.");
        }
        lines.Add($"To stop this check, turn off \"{SettingName}\" in Settings, Updates tab.");
        return string.Join("\r\n", lines);
    }
}
