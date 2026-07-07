using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, best-effort removal of the RETIRED Community-folder accessibility bridges that
    /// have been superseded by the Coherent-debugger transport:
    ///   • PMDG EFB  — <c>zzz-pmdg-efb-accessibility</c> (now <see cref="SimConnect.CoherentPmdgEfbClient"/>),
    ///     removed via <see cref="EFBModPackageManager.Remove"/>.
    ///   • HorizonSim 787 — via <see cref="Hs787LegacyUninstaller.Remove"/>, which reverts BOTH the
    ///     FS2020 <c>zzz-hs787-accessibility</c> override package (folder delete) AND the FS2024
    ///     in-place HTML patch (restored from its <c>.msfsba_backup</c> copies). The 787 is now driven
    ///     over the Coherent debugger; the uninstaller is the removal half of the deleted
    ///     <c>HS787ModPackageManager</c> (no install/patch logic — that installer is intentionally gone).
    /// A user who ran an injecting build still has these bridges shadowing/patching the real instrument
    /// HTML; reverting them restores the original aircraft.
    ///
    /// Never throws; any per-folder failure is logged and skipped. Safe to call on every startup —
    /// a no-op once the packages are gone (a single Directory.Exists / IsInstalled check per folder).
    /// </summary>
    public static class LegacyEfbBridgeCleanup
    {
        /// <summary>
        /// Removes both retired bridges from every detected Community folder.
        /// Returns the total number of folders from which a package was actually removed.
        /// </summary>
        public static int RemoveRetiredBridges()
        {
            return RemovePmdgBridge() + RemoveHs787Bridge();
        }

        private static int RemovePmdgBridge()
        {
            int removed = 0;
            try
            {
                foreach (var (simLabel, communityPath) in EFBModPackageManager.FindAllCommunityFolders())
                {
                    try
                    {
                        if (!EFBModPackageManager.IsInstalled(communityPath)) continue;
                        if (EFBModPackageManager.Remove(communityPath) == ModPackageResult.Removed)
                        {
                            removed++;
                            Log.Debug("Patching",
                                $"[LegacyEfbBridgeCleanup] Removed retired PMDG EFB package from {simLabel}: {communityPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Patching",
                            $"[LegacyEfbBridgeCleanup] PMDG remove failed for {communityPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"[LegacyEfbBridgeCleanup] PMDG sweep failed: {ex.Message}");
            }
            return removed;
        }

        private static int RemoveHs787Bridge()
        {
            int removed = 0;
            try
            {
                // Reuse EFBModPackageManager's Community-folder discovery (it enumerates every
                // detected sim's Community folder — sim-agnostic, not PMDG-specific). The actual
                // removal (FS2020 override folder + FS2024 in-place patch restore) lives in
                // Hs787LegacyUninstaller — the uninstall half of the deleted HS787ModPackageManager.
                foreach (var (simLabel, communityPath) in EFBModPackageManager.FindAllCommunityFolders())
                {
                    if (Hs787LegacyUninstaller.Remove(communityPath))
                    {
                        removed++;
                        Log.Debug("Patching",
                            $"[LegacyEfbBridgeCleanup] Removed retired HS787 bridge from {simLabel}: {communityPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"[LegacyEfbBridgeCleanup] HS787 sweep failed: {ex.Message}");
            }
            return removed;
        }
    }
}
