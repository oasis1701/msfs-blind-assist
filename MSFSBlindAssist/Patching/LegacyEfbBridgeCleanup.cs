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
                string? community = EFBModPackageManager.FindCommunityFolderPath();
                if (string.IsNullOrEmpty(community)) return;
                foreach (var pkg in LegacyPackages)
                {
                    var result = EFBModPackageManager.RemoveNamed(community, pkg);
                    if (result == ModPackageResult.Removed)
                        System.Diagnostics.Debug.WriteLine($"LegacyEfbBridgeCleanup: removed {pkg}");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LegacyEfbBridgeCleanup: {ex.Message}"); }
        }
    }
}
