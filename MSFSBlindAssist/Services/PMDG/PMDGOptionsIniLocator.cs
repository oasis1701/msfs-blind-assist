namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Locates a PMDG variant's <c>&lt;family&gt;_Options.ini</c> on disk. This file is NOT under the
/// Community/Official package tree <see cref="AircraftCfgCatalog"/> scans — it lives in MSFS's
/// per-package WASM "work" folder, a separate persisted-storage tree keyed by the same package
/// folder name (e.g. <c>pmdg-aircraft-738</c>) PMDG uses in Community. Confirmed against PMDG's
/// own forum for both simulator generations and both storefronts:
/// <list type="bullet">
/// <item>FS2024 MS Store: <c>%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\&lt;pkg&gt;\work\</c></item>
/// <item>FS2024 Steam: <c>%APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2024\&lt;pkg&gt;\work\</c></item>
/// <item>FS2020 MS Store: <c>%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\&lt;pkg&gt;\work\</c></item>
/// <item>FS2020 Steam: <c>%APPDATA%\Microsoft Flight Simulator\Packages\&lt;pkg&gt;\work\</c></item>
/// </list>
/// The "work" folder (and the options file in it) is created by MSFS/PMDG only after the variant
/// has actually been loaded into a flight at least once — so a missing file here does not
/// necessarily mean misconfiguration; see <see cref="FindExisting"/>.
///
/// <para>
/// ⚠️ Deliberately NEVER read <c>InstalledPackagesPath</c> (from <c>UserCfg.opt</c>) here, even
/// though <c>AircraftCfgCatalog</c> and <c>NavdataReaderBuilder</c> both do for the Community/
/// Official tree. Confirmed from two independent sources (a PMDG owner's own account on the
/// official MSFS forum after relocating Community/Official to a custom drive, and a WASM-cache
/// explainer): the add-on's own package folder is a SEPARATE storage tree from Community/Official,
/// tied to the fixed OS/app-identity location — relocating Community/Official via
/// <c>InstalledPackagesPath</c> (MSFS's own "change installation drive" feature) does NOT move
/// it. So these four fixed AppData-rooted candidates are correct even when the pilot's Community
/// folder lives on a different drive; deriving a fifth candidate from
/// <c>InstalledPackagesPath</c> would be the bug, not an improvement. A relocated *Windows user
/// profile* (roaming profile, redirected AppData) is still handled correctly, because it's
/// resolved dynamically via <c>Environment.GetFolderPath</c> rather than a hardcoded path.
/// </para>
/// </summary>
public static class PMDGOptionsIniLocator
{
    /// <summary>
    /// Builds every candidate path for <paramref name="packageFolderName"/> (e.g.
    /// <c>pmdg-aircraft-738</c>) and <paramref name="familyPrefix"/> (<c>"737"</c> or
    /// <c>"777"</c>), ordered so the currently-detected running simulator's own paths (per
    /// <paramref name="runningSimulatorVersion"/>, as returned by
    /// <see cref="Utils.SimulatorDetector.DetectRunningSimulator"/> — <c>"FS2020"</c> or
    /// <c>"FS2024"</c>) are tried first. Any other value (including <c>"Unknown"</c> or null) tries
    /// FS2024 first, the newer sim. Pure — no disk access, so it is fully unit-testable.
    /// </summary>
    public static IReadOnlyList<string> BuildCandidatePaths(
        string packageFolderName, string familyPrefix, string? runningSimulatorVersion)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string iniFileName = $"{familyPrefix}_Options.ini";

        var fs2024 = new[]
        {
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalState",
                "WASM", "MSFS2024", packageFolderName, "work", iniFileName),
            Path.Combine(appData, "Microsoft Flight Simulator 2024",
                "WASM", "MSFS2024", packageFolderName, "work", iniFileName),
        };
        var fs2020 = new[]
        {
            Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState",
                "packages", packageFolderName, "work", iniFileName),
            Path.Combine(appData, "Microsoft Flight Simulator",
                "Packages", packageFolderName, "work", iniFileName),
        };

        var ordered = new List<string>(4);
        if (runningSimulatorVersion == "FS2020")
        {
            ordered.AddRange(fs2020);
            ordered.AddRange(fs2024);
        }
        else
        {
            ordered.AddRange(fs2024);
            ordered.AddRange(fs2020);
        }
        return ordered;
    }

    /// <summary>
    /// The first candidate path that exists on disk, or null when none do. Thin IO wrapper —
    /// never throws (a locked/inaccessible path is treated the same as a missing one).
    /// </summary>
    public static string? FindExisting(IReadOnlyList<string> candidatePaths)
    {
        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path)) return path;
            }
            catch { /* treat as not found */ }
        }
        return null;
    }
}
