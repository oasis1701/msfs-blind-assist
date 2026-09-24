using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.SceneryIndex;

/// <summary>
/// The disk rules <see cref="SceneryPackageCensus"/> and <see cref="SceneryPackageIndexer"/> share,
/// kept once: the indexer reads the very packages the census walked, so a rule that differed between
/// them would only move a problem from one to the other.
/// </summary>
internal static class SceneryPackageDisk
{
    /// <summary>How deep a package is walked. Reparse points are followed (linker tools put whole
    /// packages behind them), so this bound is what ends a link cycle; the deepest real BGL is 4 down.</summary>
    internal const int MaxBglRecursionDepth = 12;

    /// <summary>Every *.bgl under one package, any case, hidden/system files included and reparse
    /// points followed (AttributesToSkip 0), to <see cref="MaxBglRecursionDepth"/>.</summary>
    internal static readonly EnumerationOptions BglFiles = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0, MaxRecursionDepth = MaxBglRecursionDepth,
    };

    /// <summary>layout.json's length and last-write time, which a package update rewrites; (0, 0) when absent.</summary>
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

    /// <summary>Opens a package file shared for write and delete — the simulator or an installer may
    /// hold it, and a reader that permits no writer can make their write fail — sequentially, 64 KB.</summary>
    internal static FileStream OpenShared(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    /// <summary>What one walk of a package's BGLs saw.</summary>
    internal sealed class BglWalk
    {
        /// <summary>Every *.bgl found, keyed on its package-relative '/' path (layout.json's spelling,
        /// compared ignoring case) → its length, or null when it was not read to the end.</summary>
        internal Dictionary<string, long?> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Files that could not be opened, or whose read did not finish.</summary>
        internal int Unreadable { get; set; }
    }

    /// <summary>
    /// Opens every *.bgl through <see cref="OpenShared"/> and hands it to <paramref name="readOne"/>,
    /// which returns whether its read finished. One bad file costs its own contents and the walk's
    /// completeness, never the package; an enumerator failure is not caught (the caller decides).
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
    /// The *.bgl rows of the package's layout.json content list (relative path → listed size), or null
    /// when there is none to hold a walk to (absent, unparseable — perhaps mid-write — or no "content").
    /// Shape: {"content":[{"path":"scenery/global/scenery/modellib.bgl","size":423623764,…}]}. A row
    /// naming a place outside the package is skipped.
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
    /// How many listed BGLs the walk did not see whole (absent, or another length than listed). An
    /// installer writes layout.json first with its final stamp, so a half-installed package otherwise
    /// looks complete and its short answer would be cached. Not counted: a found-but-unreadable file
    /// (already counted), one deeper than the walk goes, a row with no size (held only to presence),
    /// and a BGL a vendor configurator switched off (<see cref="IsSwitchedOffOption"/>; 24 such rows in
    /// 6 of 46 real packages). The listed "date" never matches the file's mtime, so it is not compared.
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

    /// <summary>The measured suffixes a configurator gives a listed "x.bgl" it switched off:
    /// ".bgl.disabled" (iniBuilds), ".bgl.off" (Orbx), ".off" (Aerosoft).</summary>
    private static readonly string[] SwitchedOffSuffixes = { ".bgl.disabled", ".bgl.off", ".off" };

    /// <summary>Whether the listed BGL's folder holds it renamed with one of the
    /// <see cref="SwitchedOffSuffixes"/>, ignoring case. Nothing broader: an installer's staged
    /// "x.bgl.part"/"x.bgl.tmp" must never pass for a switched-off option.</summary>
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
    /// Whole file or nothing (.tmp then move), so a crash never leaves a truncated cache that reads as
    /// a package with fewer buildings. A failed write costs only the next call its shortcut.
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
