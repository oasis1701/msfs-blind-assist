using System;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, idempotent removal of the retired EFB Community-folder injection packages.
    /// EFB accessibility moved to the Coherent debugger transport; the old packages (which injected
    /// an HTTP bridge) are no longer used and are removed here so every user's Community folder is
    /// cleaned automatically. Safe to run every launch (no-op if absent).
    /// </summary>
    public static class LegacyEfbBridgeCleanup
    {
        private static readonly string[] LegacyPackages = { "zzz-pmdg-efb-accessibility", "zzz-hs787-accessibility" };

        public static void RemoveAll()
        {
            try
            {
                // Sweep EVERY sim's Community folder (FS2020 AND FS2024) — at startup no sim is
                // running, so a single-folder lookup would only clean the default sim and leave the
                // package behind in the other one. FindAllCommunityFolders() covers both.
                var folders = EFBModPackageManager.FindAllCommunityFolders();
                if (folders == null) return;
                foreach (var (_, folder) in folders)
                {
                    if (string.IsNullOrEmpty(folder)) continue;
                    foreach (var pkg in LegacyPackages)
                    {
                        var result = EFBModPackageManager.RemoveNamed(folder, pkg);
                        if (result == ModPackageResult.Removed)
                            System.Diagnostics.Debug.WriteLine($"LegacyEfbBridgeCleanup: removed {pkg} from {folder}");
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LegacyEfbBridgeCleanup: {ex.Message}"); }
        }
    }
}
