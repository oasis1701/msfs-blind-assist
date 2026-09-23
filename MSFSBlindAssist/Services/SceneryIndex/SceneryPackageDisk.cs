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
