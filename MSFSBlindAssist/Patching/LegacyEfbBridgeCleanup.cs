namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, best-effort removal of the RETIRED Community-folder accessibility bridges that
    /// have been superseded by the Coherent-debugger transport:
    ///   • PMDG EFB  — <c>zzz-pmdg-efb-accessibility</c> (now <see cref="SimConnect.CoherentPmdgEfbClient"/>)
    ///   • HorizonSim 787 — <c>zzz-hs787-accessibility</c> / the FS2024 in-place HTML patch
    ///     (now driven over the Coherent debugger).
    /// A user who ran an injecting build still has these packages shadowing / patching the real
    /// instrument HTML; removing them cleanly reverts the tablet/787 (the override package only ever
    /// shadowed the original files, and the FS2024 patch is reverted from the <c>.msfsba_backup</c>
    /// copies by <see cref="HS787ModPackageManager.Remove"/>).
    ///
    /// Never throws; any per-folder failure is logged and skipped. Safe to call on every startup —
    /// a no-op once the packages are gone (a single Directory.Exists / IsInstalled check per folder).
    ///
    /// NOTE: removal must run BEFORE any code that re-installs a bridge. On a tree that still wires
    /// the old HS787 installer (<c>CheckAndOfferHS787ModPackage</c>), that installer would re-detect
    /// the removed package and re-offer it — so HS787 removal is only fully coherent once the HS787
    /// Coherent migration (which drops that installer) is also present.
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
                            System.Diagnostics.Debug.WriteLine(
                                $"[LegacyEfbBridgeCleanup] Removed retired PMDG EFB package from {simLabel}: {communityPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[LegacyEfbBridgeCleanup] PMDG remove failed for {communityPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LegacyEfbBridgeCleanup] PMDG sweep failed: {ex.Message}");
            }
            return removed;
        }

        private static int RemoveHs787Bridge()
        {
            int removed = 0;
            try
            {
                foreach (var (simLabel, communityPath) in HS787ModPackageManager.FindAllCommunityFolders())
                {
                    try
                    {
                        // HS787ModPackageManager.Remove handles BOTH transports: it restores the
                        // FS2024 in-place HTML patch from its .msfsba_backup copies AND deletes the
                        // FS2020 zzz-hs787-accessibility override package. Never a blind folder delete.
                        if (!HS787ModPackageManager.IsInstalled(communityPath)) continue;
                        if (HS787ModPackageManager.Remove(communityPath) == ModPackageResult.Removed)
                        {
                            removed++;
                            System.Diagnostics.Debug.WriteLine(
                                $"[LegacyEfbBridgeCleanup] Removed retired HS787 bridge from {simLabel}: {communityPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[LegacyEfbBridgeCleanup] HS787 remove failed for {communityPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LegacyEfbBridgeCleanup] HS787 sweep failed: {ex.Message}");
            }
            return removed;
        }
    }
}
