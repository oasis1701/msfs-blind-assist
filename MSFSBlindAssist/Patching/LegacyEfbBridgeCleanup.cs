namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// One-time, best-effort removal of the RETIRED Community-folder accessibility bridges that
    /// have been superseded by the Coherent-debugger transport:
    ///   • PMDG EFB  — <c>zzz-pmdg-efb-accessibility</c> (now <see cref="SimConnect.CoherentPmdgEfbClient"/>),
    ///     removed via <see cref="EFBModPackageManager.Remove"/>.
    ///   • HorizonSim 787 — the FS2020 <c>zzz-hs787-accessibility</c> override package, removed by a
    ///     direct folder delete (the 787 is now driven over the Coherent debugger).
    /// A user who ran an injecting build still has these packages shadowing the real instrument HTML;
    /// removing the override folder reverts cleanly (the package only ever shadowed the originals).
    ///
    /// LIMITATION: the FS2024 HS787 in-place HTML patch is NOT reverted here. The code that safely
    /// restored it (HS787ModPackageManager.RestoreFs2024FromBackups) was deleted with the 787 Coherent
    /// migration, so only the FS2020 override-folder case is cleaned up.
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

        // The FS2020 HorizonSim 787 override package folder. (HS787ModPackageManager was removed
        // when the 787 moved to the Coherent transport, so this is a direct, manager-free delete.)
        private const string Hs787PackageFolderName = "zzz-hs787-accessibility";

        private static int RemoveHs787Bridge()
        {
            int removed = 0;
            try
            {
                // Reuse EFBModPackageManager's Community-folder discovery (it enumerates every
                // detected sim's Community folder — sim-agnostic, not PMDG-specific).
                foreach (var (simLabel, communityPath) in EFBModPackageManager.FindAllCommunityFolders())
                {
                    try
                    {
                        // Delete the FS2020 zzz-hs787-accessibility OVERRIDE package — like the PMDG
                        // package it only ever shadowed the original instrument HTML, so removing the
                        // folder reverts cleanly. NOTE: the FS2024 in-place HTML patch (patched HTML +
                        // .msfsba_backup copies) is NOT handled here — the code that safely restored it
                        // (HS787ModPackageManager.RestoreFs2024FromBackups) was removed with the 787
                        // Coherent migration. Reverting that patch would need that restoration logic and
                        // is out of scope for this folder cleanup.
                        string packagePath = System.IO.Path.Combine(communityPath, Hs787PackageFolderName);
                        if (!System.IO.Directory.Exists(packagePath)) continue;
                        System.IO.Directory.Delete(packagePath, recursive: true);
                        removed++;
                        System.Diagnostics.Debug.WriteLine(
                            $"[LegacyEfbBridgeCleanup] Removed retired HS787 override package from {simLabel}: {packagePath}");
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
