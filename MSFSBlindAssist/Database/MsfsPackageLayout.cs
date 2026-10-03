using System;
using System.Collections.Generic;
using System.IO;

namespace MSFSBlindAssist.Database;

/// <summary>
/// The folder layout under a simulator's packages root: which folders hold add-ons and which hold
/// official packages. Deliberately dependency-free (no logger, nothing else from the app), because
/// the standalone probes that link <c>AircraftCfgCatalog.cs</c> and <c>GsxAirplaneProfile.cs</c>
/// (<c>tools/GsxOffsetProbe</c>, <c>tools/GsxAirplaneProbe</c>) link this one file beside them.
/// Nothing here throws: a folder that cannot be listed holds nothing a reader can open.
///
/// <para>MSFS 2020 reads <c>Community</c> and <c>Official\*</c>. MSFS 2024 reads <c>Community</c>
/// (add-ons that work in both simulators, since both read it) AND <c>Community2024</c> (2024-only
/// add-ons: the folder the SDK tells 2024-native scenery to go in, and where TFDi's installer puts
/// the MD-11), with its official packages under <c>Official2020\*</c> and <c>Official2024\*</c> — it
/// has no plain <c>Official</c> folder at all. So a 2024 pilot's native airports, and every official
/// 2024 aircraft, live where a <c>Community</c>-and-<c>Official</c> reader never looks. Measured
/// 2026-10-02 on a shared packages root: 100 packages under Community, 63 under Community2024, among
/// them every FlyTampa, iniBuilds and Orbx 2024 airport the pilot owned; the scenery census had
/// indexed 41 and reported "no installed scenery package found" at CYYZ while the FlyTampa package
/// sat on the disk.</para>
/// </summary>
public static class MsfsPackageLayout
{
    /// <summary>Every add-on folder name either simulator reads, in the order a reader walks them.
    /// Read-only, not a bare array: it is handed out as is, and a cast must not be able to edit it.</summary>
    public static readonly IReadOnlyList<string> AllCommunityFolderNames = Array.AsReadOnly(new[] { "Community", "Community2024" });

    private static readonly IReadOnlyList<string> CommunityOnly = Array.AsReadOnly(new[] { "Community" });

    /// <summary>The folders whose CHILDREN are official packages (<c>OneStore</c>, <c>Steam</c>, …).</summary>
    private static readonly string[] OfficialFolderNames = { "Official", "Official2020", "Official2024" };

    /// <summary>The add-on folder names the given simulator loads: both for "FS2024", <c>Community</c>
    /// alone for anything else — MSFS 2020 never reads <c>Community2024</c>, even on a shared root.</summary>
    public static IReadOnlyList<string> CommunityFolderNames(string simulatorVersion)
        => simulatorVersion == "FS2024" ? AllCommunityFolderNames : CommunityOnly;

    /// <summary>The add-on folders the given simulator loads under <paramref name="packagesRoot"/>,
    /// only those on disk, in <see cref="CommunityFolderNames"/> order; empty when none.</summary>
    public static IReadOnlyList<string> CommunityFolders(string packagesRoot, string simulatorVersion)
        => Existing(packagesRoot, CommunityFolderNames(simulatorVersion));

    /// <summary>
    /// Every folder under <paramref name="packagesRoot"/> that DIRECTLY holds packages, add-on folders
    /// first, then each child of each Official folder; only those on disk, in that order. For a reader
    /// that walks every installed package of either simulator (the aircraft.cfg catalog, the GSX
    /// profile scan) and so does not care which simulator wrote the root: a 2020 root simply has no
    /// <c>Community2024</c>, <c>Official2020</c> or <c>Official2024</c> to find. The children of one
    /// Official folder are sorted by name, because both readers keep the FIRST package that names a
    /// title or a type, and the order a directory listing comes back in belongs to the file system.
    /// </summary>
    public static IReadOnlyList<string> PackageFolders(string packagesRoot)
    {
        var result = Existing(packagesRoot, AllCommunityFolderNames);
        foreach (string name in OfficialFolderNames)
        {
            try
            {
                string official = Path.Combine(packagesRoot, name);
                if (!Directory.Exists(official)) continue;
                string[] children = Directory.GetDirectories(official);
                Array.Sort(children, StringComparer.OrdinalIgnoreCase);
                result.AddRange(children);
            }
            catch (Exception) { /* cannot be listed: nothing a reader can open */ }
        }
        return result;
    }

    private static List<string> Existing(string packagesRoot, IReadOnlyList<string> names)
    {
        var found = new List<string>();
        foreach (string name in names)
        {
            try
            {
                string dir = Path.Combine(packagesRoot, name);
                if (Directory.Exists(dir)) found.Add(dir);
            }
            catch (Exception) { /* an invalid root names no folder */ }
        }
        return found;
    }
}
