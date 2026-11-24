using System;
using System.IO;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// Removes the screen reader libraries Prism replaced, <c>Tolk.dll</c> and <c>nvdaControllerClient.dll</c>,
    /// from beside the exe of an install that was updated in place.
    ///
    /// The updater is a pure overlay (<c>UpdaterForm.ExtractUpdate</c> writes every zip entry and deletes nothing the
    /// new zip leaves out), so an install updated past the move to Prism keeps both files. Nothing loads them any more
    /// (every screen reader goes through Prism: <c>ScreenReaderOutput</c>), so they are inert, and this is housekeeping.
    /// Sweeping at start-up also covers a pilot who extracts the zip over the folder by hand.
    ///
    /// Guarded: it runs only where <c>prism.dll</c> sits beside the exe, so a folder of an older version, whose speech
    /// still needs them, is never touched; and it removes only those two files at the top of the folder. Never throws;
    /// a file it cannot delete (in use, or an install under Program Files that is not writable) is logged and skipped,
    /// and tried again at the next start. A no-op once they are gone.
    /// </summary>
    public static class RetiredScreenReaderLibrariesCleanup
    {
        /// <summary>The libraries Prism replaced. Never add a file something still loads.</summary>
        internal static readonly string[] RetiredLibraries = { "Tolk.dll", "nvdaControllerClient.dll" };

        /// <summary>Sweeps the running application's own folder. Returns the number of files removed.</summary>
        public static int RemoveRetiredLibraries() => RemoveRetiredLibraries(AppContext.BaseDirectory);

        /// <summary>Testable overload: sweeps <paramref name="appDir"/>.</summary>
        internal static int RemoveRetiredLibraries(string appDir)
        {
            int removed = 0;
            try
            {
                // THE safety gate: only a Prism build's folder. An older version's own speech needs these files.
                if (!File.Exists(Path.Combine(appDir, "prism.dll")))
                    return 0;

                foreach (string name in RetiredLibraries)
                {
                    string path = Path.Combine(appDir, name);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        File.Delete(path);
                        removed++;
                        Log.Debug("Patching", $"Removed {name}, which Prism replaced");
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Patching", $"Could not remove {name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"Retired screen reader library sweep failed: {ex.Message}");
            }
            return removed;
        }
    }
}
