namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, best-effort removal of the RETIRED PMDG EFB Community-folder bridge
    /// (<c>zzz-pmdg-efb-accessibility</c>). The PMDG EFB is now driven entirely over the
    /// Coherent debugger (<see cref="SimConnect.CoherentPmdgEfbClient"/>), so the old
    /// HTML-override package is dead weight: a user who ran an EFB-injecting build still
    /// has it shadowing the real <c>PMDGTabletCA.html</c>. Removing the override folder
    /// cleanly reverts the tablet (the package only ever shadowed, never edited, the
    /// original files).
    ///
    /// Scope is deliberately PMDG-ONLY. The HorizonSim 787 (<c>zzz-hs787-accessibility</c>)
    /// still uses its Community-folder bridge on this build (see
    /// <see cref="HS787ModPackageManager"/> + <c>CheckAndOfferHS787ModPackage</c>), so it
    /// must NOT be touched here — removing an actively-installed package would break the 787.
    ///
    /// Never throws; any per-folder failure is logged and skipped. Safe to call on every
    /// startup — it is a no-op once the package is gone (a single Directory.Exists check
    /// per Community folder).
    /// </summary>
    public static class LegacyEfbBridgeCleanup
    {
        /// <summary>
        /// Removes <c>zzz-pmdg-efb-accessibility</c> from every detected Community folder.
        /// Returns the number of folders from which the package was actually removed.
        /// </summary>
        public static int RemoveRetiredPmdgBridge()
        {
            int removed = 0;
            try
            {
                foreach (var (simLabel, communityPath) in EFBModPackageManager.FindAllCommunityFolders())
                {
                    try
                    {
                        if (!EFBModPackageManager.IsInstalled(communityPath)) continue;
                        var result = EFBModPackageManager.Remove(communityPath);
                        if (result == ModPackageResult.Removed)
                        {
                            removed++;
                            System.Diagnostics.Debug.WriteLine(
                                $"[LegacyEfbBridgeCleanup] Removed retired PMDG EFB package from {simLabel}: {communityPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[LegacyEfbBridgeCleanup] Failed to remove from {communityPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // FindAllCommunityFolders itself touches the filesystem / UserCfg.opt — never let
                // a cleanup sweep abort app startup.
                System.Diagnostics.Debug.WriteLine($"[LegacyEfbBridgeCleanup] Sweep failed: {ex.Message}");
            }
            return removed;
        }
    }
}
