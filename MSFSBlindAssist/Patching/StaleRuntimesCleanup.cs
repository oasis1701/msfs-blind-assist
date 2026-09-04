using System;
using System.IO;
using System.Linq;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, best-effort removal of the FOREIGN-PLATFORM native assets that older, PORTABLE
    /// builds shipped under <c>runtimes\</c> beside the exe.
    ///
    /// Until the build pinned <c>RuntimeIdentifier</c> to win-x64 it was a portable build, so the
    /// SDK copied EVERY platform's native assets out of each package and deferred asset selection
    /// to run time. SQLitePCLRaw ships e_sqlite3 precompiled for all 32 RIDs .NET supports, so a
    /// Windows-only app carried Android .so files, iOS .a static libs, a browser-wasm blob and
    /// fourteen Linux variants — about 67 MB the host can never load.
    ///
    /// The updater cannot clear these itself: <c>UpdaterForm.ExtractUpdate</c> is a pure OVERLAY
    /// (it writes every zip entry with overwrite:true and deletes nothing absent from the zip), so
    /// upgrading in place LEAVES the whole stale tree and actually grows the install by the two
    /// newly-flattened DLLs. Measured on a simulated upgrade: 86.2 MB / 32 RID folders before,
    /// 88.6 MB / 32 RID folders after. Sweeping at startup instead of in the updater also covers
    /// the pilot who extracts the zip over their folder by hand, and everyone who already upgraded.
    ///
    /// The leftovers are INERT, not dangerous — a RID-pinned deps.json no longer references those
    /// paths, so the flattened e_sqlite3.dll beside the exe is what loads (verified with both
    /// present). This is housekeeping; it must never be allowed to become a risk in its own right.
    ///
    /// Never throws; any per-folder failure is logged and skipped (an install under Program Files
    /// may simply not be writable). Safe to call on every startup — a no-op once the folders are
    /// gone, and a no-op on a portable build, which is what <see cref="IsRidPinnedBuild"/> guards.
    /// </summary>
    public static class StaleRuntimesCleanup
    {
        /// <summary>
        /// RID folders that are NEVER removed. The app is hard-pinned to win-x64
        /// (<c>&lt;Platforms&gt;x64&lt;/Platforms&gt;</c> plus <c>RuntimeIdentifier</c>), so no other
        /// RID folder can be loadable here — but these two are kept anyway:
        ///   • <c>win-x64</c> — the WebView2 package copies WebView2Loader.dll back into it on every
        ///     build, so it is LIVE, not stale, even in a RID-pinned output.
        ///   • <c>win</c> — the portable-Windows fallback folder (older builds put System.Speech.dll
        ///     there). Costs ~672 KB to keep and removes the whole class of "was that the only copy?"
        ///     doubt. Deliberately conservative: this sweep deletes files, so it errs toward keeping.
        /// An allowlist, not a denylist, so a RID nobody has thought of yet is still swept.
        /// </summary>
        internal static readonly string[] KeptRuntimeIds = { "win", "win-x64" };

        /// <summary>
        /// Sweeps the running application's own folder. Returns the number of RID folders removed.
        /// </summary>
        public static int RemoveForeignRuntimeAssets() =>
            RemoveForeignRuntimeAssets(AppContext.BaseDirectory);

        /// <summary>Testable overload: sweeps <paramref name="appDir"/>.</summary>
        internal static int RemoveForeignRuntimeAssets(string appDir)
        {
            int removed = 0;
            try
            {
                // THE safety gate. On a portable build the ONLY copy of e_sqlite3 lives under
                // runtimes/win-x64/native/, so sweeping there would delete a LIVE binary and break
                // SQLite outright. A flattened e_sqlite3.dll beside the exe is the signal that
                // asset selection already happened at restore time and the tree is redundant.
                if (!IsRidPinnedBuild(appDir))
                    return 0;

                string runtimesDir = Path.Combine(appDir, "runtimes");
                if (!Directory.Exists(runtimesDir))
                    return 0;

                foreach (string dir in Directory.GetDirectories(runtimesDir))
                {
                    string rid = Path.GetFileName(dir);
                    if (!ShouldRemove(rid))
                        continue;

                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        removed++;
                        Log.Debug("Patching", $"Removed stale runtime assets: runtimes/{rid}");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Patching", $"Stale runtime removal failed for {rid}: {ex.Message}");
                    }
                }

                // Leave no empty shell behind if a future build ships nothing under runtimes\.
                try
                {
                    if (Directory.Exists(runtimesDir) &&
                        Directory.GetFileSystemEntries(runtimesDir).Length == 0)
                    {
                        Directory.Delete(runtimesDir);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Patching", $"Empty runtimes folder removal failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"Stale runtimes sweep failed: {ex.Message}");
            }
            return removed;
        }

        /// <summary>
        /// True when this output had its native assets resolved at restore time (RID-pinned), which
        /// is what makes the runtimes\ tree redundant. See the guard comment above — on a portable
        /// build this MUST return false.
        /// </summary>
        internal static bool IsRidPinnedBuild(string appDir) =>
            File.Exists(Path.Combine(appDir, "e_sqlite3.dll"));

        /// <summary>
        /// True for a RID folder this build can never load. OrdinalIgnoreCase, never ToLower():
        /// tr-TR folds "I" to dotless "i" and a culture-sensitive compare would silently stop
        /// matching (the same trap MonitorVariableFilter documents).
        /// </summary>
        internal static bool ShouldRemove(string ridFolderName) =>
            !string.IsNullOrWhiteSpace(ridFolderName) &&
            !KeptRuntimeIds.Contains(ridFolderName, StringComparer.OrdinalIgnoreCase);
    }
}
