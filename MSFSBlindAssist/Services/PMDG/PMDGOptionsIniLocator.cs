namespace MSFSBlindAssist.Services.PMDG;

/// <summary>A folder holding PMDG's per-package "work" storage, and the simulator it belongs to.</summary>
public sealed record PMDGStorageRoot(string Path, string Simulator);

/// <summary>One installed variant's options file: where it is, its package folder, and its simulator.</summary>
public sealed record PMDGOptionsFile(string Path, string PackageFolder, string Simulator);

/// <summary>
/// Finds every installed variant's <c>&lt;family&gt;_Options.ini</c>. PMDG keeps it in the package's
/// "work" folder, in MSFS's per-package storage, a tree separate from Community/Official that does
/// not move with <c>InstalledPackagesPath</c>, so it is never derived from <c>UserCfg.opt</c>:
/// <list type="bullet">
/// <item>FS2024 Store: <c>%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalState\WASM\MSFS2024\&lt;pkg&gt;\work\</c>
/// (measured on the owner's install, 2026-10-12: all four 737s and all four 777s)</item>
/// <item>FS2024 Steam: <c>%APPDATA%\Microsoft Flight Simulator 2024\WASM\MSFS2024\&lt;pkg&gt;\work\</c></item>
/// <item>FS2020 Store: <c>%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\&lt;pkg&gt;\work\</c></item>
/// <item>FS2020 Steam: <c>%APPDATA%\Microsoft Flight Simulator\Packages\&lt;pkg&gt;\work\</c></item>
/// </list>
/// The file appears only once the variant has been loaded in the simulator, so a variant never
/// loaded has none and is not listed.
/// </summary>
public static class PMDGOptionsIniLocator
{
    /// <summary>The four storage roots, FS2024 first, for the given LocalAppData and roaming AppData.</summary>
    public static IReadOnlyList<PMDGStorageRoot> StorageRoots(string localAppData, string roamingAppData) => new[]
    {
        new PMDGStorageRoot(Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalState", "WASM", "MSFS2024"), "MSFS 2024"),
        new PMDGStorageRoot(Path.Combine(roamingAppData, "Microsoft Flight Simulator 2024", "WASM", "MSFS2024"), "MSFS 2024"),
        new PMDGStorageRoot(Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState", "packages"), "MSFS 2020"),
        new PMDGStorageRoot(Path.Combine(roamingAppData, "Microsoft Flight Simulator", "Packages"), "MSFS 2020"),
    };

    /// <summary>The four storage roots on this machine.</summary>
    public static IReadOnlyList<PMDGStorageRoot> StorageRoots() => StorageRoots(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Every <c>&lt;root&gt;\&lt;PackagePrefix&gt;*\work\&lt;OptionsFileName&gt;</c> that exists, root by
    /// root, package folders in name order. A missing or unreadable root is skipped; never throws.
    /// </summary>
    public static IReadOnlyList<PMDGOptionsFile> FindOptionsFiles(IEnumerable<PMDGStorageRoot> roots, PMDGSdkFamily family)
    {
        var found = new List<PMDGOptionsFile>();
        foreach (var root in roots)
        {
            string[] packages;
            try
            {
                if (!Directory.Exists(root.Path)) continue;
                packages = Directory.GetDirectories(root.Path, family.PackagePrefix + "*");
            }
            catch { continue; }

            Array.Sort(packages, StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                string path = Path.Combine(package, "work", family.OptionsFileName);
                try
                {
                    if (File.Exists(path)) found.Add(new PMDGOptionsFile(path, Path.GetFileName(package), root.Simulator));
                }
                catch { /* an inaccessible package folder is the same as an absent one */ }
            }
        }
        return found;
    }
}
