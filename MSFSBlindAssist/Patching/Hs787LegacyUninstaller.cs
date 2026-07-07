using System.IO;
using System.Text;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// Removal-only uninstaller for the RETIRED HorizonSim 787 Community-folder bridge. The 787 is
    /// now driven over the Coherent debugger, and <c>HS787ModPackageManager</c> (which installed AND
    /// uninstalled the bridge) was deleted with that migration — but a user who ran an older,
    /// injecting build may still have the bridge on disk. This class is the uninstall HALF of that
    /// deleted manager, ported verbatim, with NO install/patch logic (the installer is intentionally
    /// gone). Called once at startup by <see cref="LegacyEfbBridgeCleanup"/>.
    ///
    /// Two install shapes are reverted:
    ///   • FS2020 — a <c>zzz-hs787-accessibility</c> OVERRIDE package folder → deleted.
    ///   • FS2024 — an IN-PLACE patch of horizonsim-aircraft-787-9 (FS2024 ignores community-on-
    ///     community html_ui overrides, so the old build modified the aircraft package directly:
    ///     appended an import-script line to each instrument HTML and dropped bridge JS, with a
    ///     <c>.msfsba_backup</c> beside each original) → restored from those backups.
    ///
    /// Every operation is best-effort (per-file try/catch) and self-contained, so a single locked or
    /// missing file can't abort the rest. It only restores from OUR backups, strips OUR tags, and
    /// deletes files WE dropped — it never touches a pristine aircraft (no backup + no marker = no-op).
    /// </summary>
    public static class Hs787LegacyUninstaller
    {
        private const string PackageFolderName = "zzz-hs787-accessibility";
        private const string MfdBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/MFD";
        private const string EfbBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/EFB";
        private const string MfdBridgeJsFileName = "hs787-mfd-bridge.js";
        private const string EfbBridgeJsFileName = "hs787-efb-bridge.js";
        private const string VersionFileName = "msfsba-bridge-version.txt";
        private const string BackupSuffix = ".msfsba_backup";

        // A patched HTML carries the name of the bridge JS it imports; these double as the patch markers.
        private const string PatchMarker = MfdBridgeJsFileName;
        private const string EfbPatchMarker = EfbBridgeJsFileName;

        private static readonly string[] MfdHtmlFileNames = { "HSB789_MFD.GE.html", "HSB789_MFD.RR.html" };
        private static readonly string[] EfbHtmlFileNames = { "HSB789_EFB.GE.html", "HSB789_EFB.RR.html" };

        /// <summary>
        /// Reverts BOTH the FS2020 override package and the FS2024 in-place patch in one Community
        /// folder. Returns true if anything was actually removed/restored. Never throws.
        /// </summary>
        public static bool Remove(string communityFolderPath)
        {
            bool acted = false;

            // FS2020: delete the override package folder (it only ever shadowed the originals).
            try
            {
                string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
                if (Directory.Exists(packagePath))
                {
                    Directory.Delete(packagePath, recursive: true);
                    acted = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"[Hs787LegacyUninstaller] FS2020 folder delete failed for {communityFolderPath}: {ex.Message}");
            }

            // FS2024: restore the in-place patch from its .msfsba_backup copies (no-op if unpatched).
            try
            {
                string? horizonsim = FindHorizonsimPackagePath(communityFolderPath);
                if (horizonsim != null && IsPatchedFs2024(horizonsim))
                {
                    RestoreFs2024FromBackups(horizonsim);
                    acted = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Patching", $"[Hs787LegacyUninstaller] FS2024 restore failed for {communityFolderPath}: {ex.Message}");
            }

            return acted;
        }

        // Locates the horizonsim-aircraft-787-9 package within a community folder by the presence of
        // the MFD instrument path (skips our own override package).
        private static string? FindHorizonsimPackagePath(string communityFolderPath)
        {
            if (!Directory.Exists(communityFolderPath)) return null;
            foreach (string packageDir in Directory.GetDirectories(communityFolderPath))
            {
                if (string.Equals(Path.GetFileName(packageDir), PackageFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Directory.Exists(Path.Combine(packageDir, MfdBasePath)))
                    return packageDir;
            }
            return null;
        }

        private static bool IsPatchedFs2024(string horizonsim)
        {
            // Detectable on any of the four HTML files — checking one suffices.
            string mfdRR = Path.Combine(horizonsim, MfdBasePath, "HSB789_MFD.RR.html");
            return File.Exists(mfdRR) && File.ReadAllText(mfdRR).Contains(PatchMarker);
        }

        private static bool ContainsBridgeMarker(string content) =>
            content.Contains(PatchMarker) || content.Contains(EfbPatchMarker);

        // Reconstructs a pristine original by dropping every line that references one of our bridge
        // JS files (our import-script tags are whole lines we appended). Last resort when the live
        // file is patched but its .msfsba_backup is missing.
        private static string StripBridgeTags(string content)
        {
            var sb = new StringBuilder();
            foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Contains(PatchMarker) || line.Contains(EfbPatchMarker)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        // Restores each instrument HTML and layout.json from its .msfsba_backup (deleting the backup),
        // removes the dropped bridge JS files, and clears the version marker. Every op is best-effort.
        private static void RestoreFs2024FromBackups(string horizonsim)
        {
            void RestoreOne(string htmlPath)
            {
                string backup = htmlPath + BackupSuffix;
                try
                {
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, htmlPath, overwrite: true);
                        File.Delete(backup);
                    }
                    else if (File.Exists(htmlPath))
                    {
                        // No backup, but the file may still carry our tag — strip it so we don't leave
                        // a dangling reference to a bridge JS we're about to delete.
                        string content = File.ReadAllText(htmlPath);
                        if (ContainsBridgeMarker(content))
                            File.WriteAllText(htmlPath, StripBridgeTags(content) + "\n");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("Patching", $"[Hs787LegacyUninstaller] restore failed for {htmlPath}: {ex.Message}");
                }
            }

            string mfdDir = Path.Combine(horizonsim, MfdBasePath);
            foreach (string h in MfdHtmlFileNames) RestoreOne(Path.Combine(mfdDir, h));
            string efbDir = Path.Combine(horizonsim, EfbBasePath);
            if (Directory.Exists(efbDir))
                foreach (string h in EfbHtmlFileNames) RestoreOne(Path.Combine(efbDir, h));

            // Delete bridge JS we dropped in.
            try { string mfdJs = Path.Combine(mfdDir, MfdBridgeJsFileName); if (File.Exists(mfdJs)) File.Delete(mfdJs); }
            catch (Exception ex) { Log.Debug("Patching", $"[Hs787LegacyUninstaller] delete MFD JS failed: {ex.Message}"); }
            try { string efbJs = Path.Combine(efbDir, EfbBridgeJsFileName); if (File.Exists(efbJs)) File.Delete(efbJs); }
            catch (Exception ex) { Log.Debug("Patching", $"[Hs787LegacyUninstaller] delete EFB JS failed: {ex.Message}"); }

            // Restore layout.json from backup if present.
            string layoutPath = Path.Combine(horizonsim, "layout.json");
            string layoutBackup = layoutPath + BackupSuffix;
            try
            {
                if (File.Exists(layoutBackup))
                {
                    File.Copy(layoutBackup, layoutPath, overwrite: true);
                    File.Delete(layoutBackup);
                }
            }
            catch (Exception ex) { Log.Debug("Patching", $"[Hs787LegacyUninstaller] restore layout.json failed: {ex.Message}"); }

            // Clear our version marker.
            try { string versionFile = Path.Combine(horizonsim, VersionFileName); if (File.Exists(versionFile)) File.Delete(versionFile); }
            catch (Exception ex) { Log.Debug("Patching", $"[Hs787LegacyUninstaller] delete version marker failed: {ex.Message}"); }
        }
    }
}
