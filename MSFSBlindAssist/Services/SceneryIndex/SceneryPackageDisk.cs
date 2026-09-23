using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// The disk rules <see cref="SceneryPackageCensus"/> and <see cref="SceneryPackageIndexer"/> share,
/// kept ONCE. The indexer is handed the very packages the census walked, so a rule that differed
/// between them — which files a package holds, how one is opened while the simulator may hold it,
/// what stamps a cache, how a cache file is written — would only move a problem from one to the
/// other. Each of these was written twice, and the two copies were kept in step by comment (review
/// CL-4).
/// </summary>
internal static class SceneryPackageDisk
{
    /// <summary>How deep a package's own folders are walked. Reparse points must be FOLLOWED — an
    /// add-on linker puts every package behind one, and a walk that skipped them would find nothing
    /// for exactly the pilots with the most scenery — so this bound is what ends a link cycle.
    /// Packages are shallow (the deepest real BGL measured sits 4 levels down), so 12 loses nothing.</summary>
    internal const int MaxBglRecursionDepth = 12;

    /// <summary>Every *.bgl under one package: IgnoreInaccessible because the enumerator itself throws
    /// on a folder the user cannot read; CaseInsensitive because packages ship both "modelLib.BGL" and
    /// "objects.bgl"; AttributesToSkip 0 so hidden and system files are read and reparse points
    /// followed (the default skips them); and <see cref="MaxBglRecursionDepth"/>.</summary>
    internal static readonly EnumerationOptions BglFiles = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0, MaxRecursionDepth = MaxBglRecursionDepth,
    };

    /// <summary>layout.json's length and last-write time: what both caches are stamped with, because a
    /// package update rewrites layout.json. (0, 0) when there is none.</summary>
    internal readonly record struct LayoutStamp(long Length, long Ticks)
    {
        internal static LayoutStamp Of(string packageDir) => TryOf(packageDir, out var stamp) ? stamp : default;

        /// <summary>False when the folder has no layout.json — i.e. is not a package.</summary>
        internal static bool TryOf(string packageDir, out LayoutStamp stamp)
        {
            var info = new FileInfo(Path.Combine(packageDir, "layout.json"));
            stamp = info.Exists ? new LayoutStamp(info.Length, info.LastWriteTimeUtc.Ticks) : default;
            return info.Exists;
        }
    }

    /// <summary>Opens a package file for reading SHARED for write and delete — the simulator may hold
    /// that very file open, and an installer may still be writing it — sequentially, through a 64 KB
    /// buffer. The ONE shared-read open for a package's own files: the census and the indexer each
    /// carried a copy of it (review CL-4), and a reader that permits no writer can make the
    /// simulator's own write to its file fail.</summary>
    internal static FileStream OpenShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    /// <summary>What one walk of a package's BGLs saw.</summary>
    internal sealed class BglWalk
    {
        /// <summary>Every *.bgl the walk found, keyed on its path relative to the package with '/'
        /// separators — layout.json's own spelling — and compared IGNORING case (a layout lists
        /// "scenery/global/scenery/modellib.bgl" where the disk holds "modelLib.BGL"). The value is the
        /// length the file had when it was opened, or null when it was not read to the end, which
        /// <see cref="Unreadable"/> has already counted.</summary>
        internal Dictionary<string, long?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Files that could not be opened, or whose read did not finish.</summary>
        internal int Unreadable { get; set; }
    }

    /// <summary>
    /// Opens every *.bgl under <paramref name="packageDir"/> through <see cref="OpenShared"/> — the
    /// simulator may hold that very file open — and hands it to <paramref name="readOne"/>, which reads
    /// what it needs and returns whether that read FINISHED (the flag
    /// <see cref="BglPlacementReader.Read(Stream, out bool)"/> reports). One try/catch per file: one bad
    /// BGL costs its own contents, never the package's, but it does cost the walk its completeness,
    /// which decides whether a scan may be cached. A failure of the ENUMERATOR itself is NOT caught:
    /// whole files were never looked at, and each caller decides what that means for it.
    /// <paramref name="logTag"/> opens each warning ("census: {leaf}", or the leaf).
    /// </summary>
    internal static BglWalk WalkBgls(string packageDir, string logTag, Func<Stream, bool> readOne)
    {
        var walk = new BglWalk();
        foreach (string bgl in Directory.EnumerateFiles(packageDir, "*.bgl", BglFiles))
        {
            string key = RelativeKey(packageDir, bgl);
            walk.Files[key] = null;
            try
            {
                using var stream = OpenShared(bgl);
                long length = stream.Length;
                if (readOne(stream))
                {
                    walk.Files[key] = length;
                }
                else
                {
                    walk.Unreadable++;
                    Log.Warn("SceneryIndex", $"{logTag}: {Path.GetFileName(bgl)}: read did not finish");
                }
            }
            catch (Exception ex)
            {
                walk.Unreadable++;
                Log.Warn("SceneryIndex", $"{logTag}: {Path.GetFileName(bgl)}: {ex.Message}");
            }
        }
        return walk;
    }

    /// <summary>A file's path relative to its package, spelled the way layout.json spells it.</summary>
    internal static string RelativeKey(string packageDir, string file)
        => Path.GetRelativePath(packageDir, file).Replace('\\', '/');

    /// <summary>
    /// The *.bgl rows of the package's OWN layout.json content list — relative path (compared ignoring
    /// case) → the size it lists, or null for a row that gives none — or null when there is no list
    /// to hold a walk to: no layout.json, one that does not parse (it may be being written right now;
    /// its stamp then changes when it is done, which re-scans the package anyway), or one without a
    /// "content" array. The schema, measured on flightbeam-airport-kpdx-portland (2026-09-22):
    /// {"content":[{"path":"scenery/global/scenery/modellib.bgl","size":423623764,"date":133986151050000006}, …]}
    /// — paths relative to the package, lower case, '/'-separated. A row naming a place outside the
    /// package (rooted, or through "..") is not the package's, and is skipped. Opened through
    /// <see cref="OpenShared"/>: an installer may be holding layout.json while it works.
    /// </summary>
    internal static Dictionary<string, long?>? ListedBgls(string packageDir)
    {
        string path = Path.Combine(packageDir, "layout.json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = OpenShared(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                return null;

            var listed = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in content.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String) continue;
                string rel = p.GetString()!.Replace('\\', '/').TrimStart('/');
                if (!rel.EndsWith(".bgl", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(rel)
                    || rel.Split('/').Contains("..")) continue;
                listed[rel] = entry.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                              && s.TryGetInt64(out long size) ? (long?)size : null;
            }
            return listed;
        }
        catch (Exception ex)
        {
            Log.Debug("SceneryIndex", $"{Path.GetFileName(packageDir.TrimEnd('\\', '/'))}: layout.json not read ({ex.Message}); nothing to hold the scan to");
            return null;
        }
    }

    /// <summary>
    /// How many BGLs the package's own layout.json lists that <paramref name="walk"/> did not see
    /// whole: not found at all, or read at another length than listed. An installer writes layout.json
    /// FIRST, with its final stamp, and the BGLs after it (33 of 35 real packages, measured), so every
    /// file that IS there reads fine and the scan looks complete — cached, its short answer was frozen
    /// under that final stamp until the package next changed (review SI-1). 0 when there is no list to
    /// hold the walk to (see <see cref="ListedBgls"/>).
    ///
    /// Not held against a package: a listed BGL the walk found but could not read (already counted by
    /// <see cref="BglWalk.Unreadable"/>); one deeper than <see cref="MaxBglRecursionDepth"/> (no walk
    /// goes there, by design); a row with no size (held to being present); and a listed BGL absent
    /// BESIDE a copy of itself carrying one of the three <see cref="SwitchedOffSuffixes"/> —
    /// "X.bgl.disabled", "X.bgl.off", "X.off" — which is an option the vendor's configurator switched
    /// OFF, not a missing file. Measured on a real Community folder (2026-09-22): 24 listed BGLs in 6
    /// of 46 healthy packages (Aerosoft EDDF and ENGM, iniBuilds EGKK, EGLL and PHNL, Orbx KATL) are
    /// exactly that, and none of the 2,451 present BGLs has a sibling of any "&lt;stem&gt;.*" shape.
    /// Nothing broader than those three counts (see <see cref="IsSwitchedOffOption"/>). The listed
    /// "date" is never compared: it equals the installed file's mtime for 0 of 2,451.
    /// </summary>
    internal static int UnfinishedLayoutFiles(string packageDir, BglWalk walk)
    {
        var listed = ListedBgls(packageDir);
        if (listed == null) return 0;
        int unfinished = 0;
        foreach (var (rel, size) in listed)
        {
            if (walk.Files.TryGetValue(rel, out long? length))
            {
                if (length.HasValue && size.HasValue && length.Value != size.Value) unfinished++;
                continue;
            }
            if (rel.Count(c => c == '/') > MaxBglRecursionDepth) continue;
            if (IsSwitchedOffOption(packageDir, rel)) continue;
            unfinished++;
        }
        return unfinished;
    }

    /// <summary>The suffixes a vendor's options configurator was MEASURED (2026-09-22) to put on a
    /// listed "x.bgl" it switched off, in place of its ".bgl": ".bgl.disabled" (iniBuilds EGKK, EGLL,
    /// PHNL), ".bgl.off" (Orbx KATL) and ".off" (Aerosoft EDDF, ENGM — the extension replaced). ONLY
    /// these three: see <see cref="IsSwitchedOffOption"/>.</summary>
    private static readonly string[] SwitchedOffSuffixes = { ".bgl.disabled", ".bgl.off", ".off" };

    /// <summary>Whether the folder the listed BGL belongs in holds it renamed with one of the
    /// <see cref="SwitchedOffSuffixes"/> — "x.bgl" as "x.bgl.disabled", "x.bgl.off" or "x.off" —
    /// compared ignoring case (OrdinalIgnoreCase: a configurator may write "X.BGL.OFF"). Nothing
    /// broader counts, never "any file named x.&lt;anything&gt;": "x.bgl.part", "x.bgl.tmp", a backup or
    /// a same-stem "x.xml" is not an option switched off, and an installer that stages its files under
    /// such names must never have its half-written package cached as complete (review SI-1,
    /// pre-flight I13). A longer name ("x-2.bgl") is another file.</summary>
    private static bool IsSwitchedOffOption(string packageDir, string rel)
    {
        try
        {
            string full = Path.Combine(packageDir, rel.Replace('/', Path.DirectorySeparatorChar));
            string? folder = Path.GetDirectoryName(full);
            if (folder == null || !Directory.Exists(folder)) return false;
            string stem = Path.GetFileNameWithoutExtension(full);                  // "x" of the listed "x.bgl"
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(file);
                foreach (string suffix in SwitchedOffSuffixes)
                    if (name.Equals(stem + suffix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Whole file or nothing: written to a .tmp and moved into place, so a crash never leaves a
    /// truncated cache to be read as a package with fewer buildings — which nothing downstream could
    /// tell from a package that really models fewer. A cache that cannot be written costs the NEXT
    /// call its shortcut, never this one its answer.
    /// </summary>
    internal static void PersistJson(string cacheDir, string cachePath, string json)
    {
        string tmp = cachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(tmp, json);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Warn("SceneryIndex", $"could not write {Path.GetFileName(cachePath)}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
