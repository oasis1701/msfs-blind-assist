using System.IO;
using System.Text;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// Installs an MSFS Community-folder overlay package that injects the
    /// accessibility bridge (<c>fbw-a380-bridge.js</c>) into the FlyByWire
    /// A380X's MFD and EFB instruments.
    ///
    /// The overlay package name (<c>zzz-fbw-a380-msfsba-bridge</c>) sorts
    /// after the base FBW package alphabetically, so MSFS loads it last and
    /// the patched HTML files replace the originals at runtime.
    ///
    /// Both reads (selectors) and writes (KCCU H:events) are documented in
    /// the bridge JS file. The base FBW A380X paths are verified against
    /// the open-source repo (master/fbw-a380x):
    ///   - MFD: <c>html_ui/Pages/VCockpit/Instruments/A380X/MFD/mfd.html</c>
    ///   - EFB: <c>html_ui/Pages/VCockpit/Instruments/A380X/EFB/efb.html</c>
    /// Base package folder is <c>flybywire-aircraft-a380-842</c> (per
    /// FlyByWire's mach.config.js + installation docs).
    /// </summary>
    public static class FBWA380ModPackageManager
    {
        // Bump on any bridge-JS or override-HTML structure change. Pre-existing
        // installs older than this trigger an automatic re-patch on app start.
        // v2: bridge JS rewritten to v0.5.0-flat — clears stale data attrs,
        //     auto-enables KCCU keyboard, adds send_to_field composite, and
        //     emits Fenix-style "N: <value>" field markers in the grid.
        // v3: bridge JS v0.6.0-stage — adds L:MSFSBA_FBWA380_STAGE diagnostic
        //     so the MCDU form's status label can show where bring-up failed
        //     (JS not running / fetch blocked / connected) without dev mode.
        // v4: bridge JS v0.7.0-xhr-fallback — defensive XHR transport when
        //     fetch() is blocked (some Coherent GT CSP combinations).
        //     Stage diagnostic now only escalates to 2 when *both* fetch
        //     and XHR fail, so a CSP-block-on-fetch alone still posts.
        // v5: bridge JS now INLINED into the patched HTML rather than
        //     loaded via import-script. The user's overlay was confirmed
        //     installed but the bridge still reported Stage 0, meaning
        //     either MSFS isn't loading the override HTML or it's
        //     loading it but rejecting the import-script reference. We
        //     also write L:MSFSBA_FBWA380_HTML_LOADED from a tiny inline
        //     marker script so the form can distinguish "HTML never
        //     loaded" from "HTML loaded but bridge JS still failed".
        // v6 (FS2024): switched FS2024 from the community-on-community overlay
        //     package to IN-PLACE patching of the FlyByWire A380X package —
        //     exactly what the HS787 bridge had to do at its v18. MSFS 2024's
        //     VFS silently ignores community overrides of files under the
        //     protected html_ui/Pages/VCockpit/Instruments/ namespace, so our
        //     overlay's mfd.html/efb.html were never loaded (diagnostic proved
        //     HTML loaded: NO, Stage: 0 even after a clean reboot). On FS2024
        //     we now back up the original mfd.html/efb.html and update the FBW
        //     package's own layout.json so MSFS's file-size validation passes.
        // v7: CRITICAL bring-up fix. v5/v6 INLINED the bridge as a plain
        //     <script> appended to the instrument HTML — but MSFS instrument
        //     templates are NOT ordinary web pages: the Coherent loader only
        //     executes scripts referenced via `import-script="…"` directives,
        //     never plain inline <script> blocks. So the inlined bridge (and
        //     the HTML-loaded marker) never ran, leaving HTML loaded: NO even
        //     though the patched file was on disk and loading fine. v7 drops
        //     the bridge JS (and a tiny HTML-loaded marker JS) as SEPARATE
        //     files in each instrument folder and appends `import-script` tags
        //     referencing them — the same mechanism FBW's own mfd.js/efb.js
        //     use, and what HS787 v18 does. The new JS files are registered in
        //     layout.json (without that, MSFS's VFS won't serve them and the
        //     import 404s). Applies to BOTH the FS2024 in-place path and the
        //     FS2020 overlay.
        // v8: CRITICAL bring-up fix #2 — abandon FS2024 in-place patching and
        //     unify on the `zzz-` overlay package for ALL sims. The v6 premise
        //     ("MSFS 2024 ignores community overrides of html_ui/Pages/VCockpit/
        //     Instruments/…") was WRONG: this app's own *working* PMDG EFB
        //     bridge (EFBModPackageManager) overrides exactly that namespace
        //     via a zzz- overlay on FS2024 and it loads fine. What actually
        //     broke the v5 overlay was the inline-<script> bug (fixed in v7) —
        //     NOT the overlay mechanism. v7 fixed the script bug but kept the
        //     pointless in-place strategy, which MSFS 2024 was apparently
        //     refusing to re-index (HTML loaded: NO, Stage: 0). v8 writes the
        //     overlay (already correct: import-script + layout.json) on FS2024
        //     too, and REPAIRS any prior in-place patch by restoring the FBW
        //     package's mfd.html/efb.html/layout.json from the .msfsba_backup
        //     files so the overlay is the single source of truth.
        private const int BridgeVersion = 8;
        private const string VersionFileName = "bridge-version.txt";

        // Suffix for the backed-up pristine HTML kept beside each in-place
        // patched file (FS2024 path only). Restored on Remove.
        private const string BackupSuffix = ".msfsba_backup";

        // Version marker we drop inside the FBW A380X package on the FS2024
        // in-place path (we can't touch FBW's manifest, and the overlay's
        // bridge-version.txt doesn't exist on this path). Detects installed
        // version across app restarts without a separate package.
        private const string FbwVersionMarkerFileName = "msfsba-bridge-version.txt";

        private const string PackageFolderName = "zzz-fbw-a380-msfsba-bridge";
        private const string BridgeJsFileName = "fbw-a380-bridge.js";

        // Tiny companion script dropped next to the bridge. It only sets
        // L:MSFSBA_FBWA380_HTML_LOADED=1, and is loaded by its OWN
        // import-script tag BEFORE the bridge. If this var goes to 1 but the
        // bridge's Stage var stays 0, we know MSFS loaded & ran our injected
        // scripts but the bridge JS itself failed to parse — a different bug
        // from "MSFS never loaded the override at all" (both vars 0).
        private const string MarkerJsFileName = "msfsba-html-marker.js";
        private const string MarkerJsContent =
            "try{if(typeof SimVar!=='undefined'&&SimVar.SetSimVarValue){" +
            "SimVar.SetSimVarValue('L:MSFSBA_FBWA380_HTML_LOADED','number',1);}}catch(e){}";

        // Per-instrument override target — relative paths inside the FBW
        // A380X package that we mirror in our overlay package.
        private const string MfdRelPath = "html_ui/Pages/VCockpit/Instruments/A380X/MFD";
        private const string MfdHtmlFileName = "mfd.html";

        private const string EfbRelPath = "html_ui/Pages/VCockpit/Instruments/A380X/EFB";
        private const string EfbHtmlFileName = "efb.html";

        // FlyByWire A380X publishes one stable package folder name. We also
        // accept a few dev-installer aliases (FBW Installer dev/PR builds).
        private static readonly string[] FbwA380PackageFolders = new[]
        {
            "flybywire-aircraft-a380-842",
            "flybywire-aircraft-a380x",
            "flybywire-aircraft-a380-development",
        };

        private static readonly string[] DefaultMSStoreCommunityPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
        };

        private static readonly string[] FallbackCommunityPaths = new[]
        {
            @"D:\MSFS\Community",
            @"C:\MSFS\Community",
            @"E:\MSFS\Community",
        };

        private const string ManifestJson = @"{
  ""dependencies"": [],
  ""content_type"": ""MISC"",
  ""title"": ""MSFS Blind Assist - FlyByWire A380X Bridge"",
  ""manufacturer"": """",
  ""creator"": ""MSFS Blind Assist"",
  ""package_version"": ""0.1.0"",
  ""minimum_game_version"": ""1.39.9"",
  ""release_notes"": {
    ""neutral"": {
      ""LastUpdate"": """",
      ""OlderHistory"": """"
    }
  },
  ""total_package_size"": ""0000000000000001000""
}";

        // HTML comment we append so we can detect an already-patched file.
        private const string PatchMarker = "<!-- MSFSBA-A380-BRIDGE -->";

        // Legacy v5/v6 inline sentinel. Still recognised by the
        // "is it patched?" check so we correctly identify an old broken
        // install and re-patch it (rather than re-prompting from scratch).
        private const string LegacyInlineMarker = "/* MSFSBA-A380-BRIDGE-INLINE */";

        /// <summary>
        /// Builds the patched instrument HTML. MSFS instrument templates only
        /// execute scripts referenced via <c>import-script="…"</c> — plain
        /// inline &lt;script&gt; blocks are ignored — so we append two
        /// import-script tags (marker first, then the bridge), matching the
        /// way FBW's own mfd.js/efb.js are loaded. The referenced JS files are
        /// dropped beside the HTML by the caller and registered in layout.json.
        /// </summary>
        private static string BuildPatchedHtml(string originalHtml, string relPath)
        {
            string cleaned = StripOldBridgeReferences(originalHtml);
            string markerImport = ImportScriptPath(relPath, MarkerJsFileName);
            string bridgeImport = ImportScriptPath(relPath, BridgeJsFileName);
            return cleaned.TrimEnd()
                + "\n" + PatchMarker
                + "\n<script type=\"text/html\" import-script=\"" + markerImport + "\" import-async=\"false\"></script>"
                + "\n<script type=\"text/html\" import-script=\"" + bridgeImport + "\" import-async=\"false\"></script>\n";
        }

        /// <summary>
        /// Converts a layout-relative instrument folder ("html_ui/Pages/…/MFD")
        /// + a file name into the leading-slash, html_ui-rooted path that MSFS
        /// import-script directives use ("/Pages/…/MFD/fbw-a380-bridge.js"),
        /// matching the existing mfd.js / efb.js references in these files.
        /// </summary>
        private static string ImportScriptPath(string relPath, string fileName)
        {
            string p = relPath.Replace('\\', '/');
            const string prefix = "html_ui/";
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                p = p.Substring(prefix.Length);
            return "/" + p.TrimEnd('/') + "/" + fileName;
        }

        /// <summary>
        /// Strips any previous bridge/marker import lines and the legacy inline
        /// &lt;script&gt; block, so re-patching is clean. (FS2024 already
        /// re-derives from the pristine backup; this is a belt-and-braces for
        /// the FS2020 overlay path and any odd state.)
        /// </summary>
        private static string StripOldBridgeReferences(string html)
        {
            var kept = new List<string>();
            bool inLegacyBlock = false;
            foreach (var line in html.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Contains(LegacyInlineMarker)) { inLegacyBlock = true; continue; }
                if (inLegacyBlock)
                {
                    if (line.Contains("</script>")) inLegacyBlock = false;
                    continue;
                }
                if (line.Contains(BridgeJsFileName) || line.Contains(MarkerJsFileName)
                    || line.Contains(PatchMarker)
                    || line.Contains("MSFSBA_FBWA380_HTML_LOADED"))
                    continue;
                kept.Add(line);
            }
            return string.Join("\n", kept);
        }

        // ------- public API ------------------------------------------------

        /// <summary>
        /// True if the bridge overlay package is installed in the given
        /// Community folder (overlay-only on both FS2020 and FS2024 as of v8).
        /// </summary>
        public static bool IsInstalled(string communityFolderPath)
        {
            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            return File.Exists(Path.Combine(packagePath, MfdRelPath, MfdHtmlFileName))
                || File.Exists(Path.Combine(packagePath, EfbRelPath, EfbHtmlFileName));
        }

        public static int GetInstalledVersion(string communityFolderPath)
        {
            string versionPath = Path.Combine(communityFolderPath, PackageFolderName, VersionFileName);
            if (File.Exists(versionPath) && int.TryParse(File.ReadAllText(versionPath).Trim(), out int v))
                return v;
            return IsInstalled(communityFolderPath) ? 1 : 0;
        }

        /// <summary>
        /// Resolves the running sim's Community folder. Mirrors the
        /// PMDG/EFB resolver in <see cref="EFBModPackageManager"/>.
        /// </summary>
        public static string? FindCommunityFolderPath()
        {
            string runningSimulator = Utils.SimulatorDetector.DetectRunningSimulator();

            var fs2024Paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };
            var fs2020Paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };
            var configPaths = runningSimulator == "FS2020"
                ? fs2020Paths.Concat(fs2024Paths).ToArray()
                : fs2024Paths.Concat(fs2020Paths).ToArray();

            foreach (string configPath in configPaths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath)) return communityPath;
                }
            }
            foreach (string path in DefaultMSStoreCommunityPaths)
                if (Directory.Exists(path)) return path;
            foreach (string path in FallbackCommunityPaths)
                if (Directory.Exists(path)) return path;
            return null;
        }

        /// <summary>
        /// Discovers every accessible Community folder (FS2020 + FS2024 +
        /// fallback). Used when offering to install the overlay across
        /// multiple sims. Returns (simLabel, communityPath) tuples.
        /// </summary>
        public static List<(string SimLabel, string Path)> FindAllCommunityFolders()
        {
            var results = new List<(string, string)>();

            var fs2024Paths = new[]
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };
            var fs2020Paths = new[]
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator", "UserCfg.opt"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };

            foreach (string configPath in fs2024Paths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = System.IO.Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath) && !results.Any(r => r.Item2 == communityPath))
                    {
                        results.Add(("MSFS 2024", communityPath));
                        break;
                    }
                }
            }
            foreach (string configPath in fs2020Paths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = System.IO.Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath) && !results.Any(r => r.Item2 == communityPath))
                    {
                        results.Add(("MSFS 2020", communityPath));
                        break;
                    }
                }
            }
            if (!results.Any(r => r.Item1 == "MSFS 2024"))
                foreach (string p in DefaultMSStoreCommunityPaths)
                    if (Directory.Exists(p) && p.Contains("Limitless")) { results.Add(("MSFS 2024", p)); break; }
            if (!results.Any(r => r.Item1 == "MSFS 2020"))
                foreach (string p in DefaultMSStoreCommunityPaths)
                    if (Directory.Exists(p) && p.Contains("FlightSimulator")) { results.Add(("MSFS 2020", p)); break; }
            if (results.Count == 0)
                foreach (string p in FallbackCommunityPaths)
                    if (Directory.Exists(p)) { results.Add(("MSFS", p)); break; }

            return results;
        }

        /// <summary>
        /// Locates the installed FBW A380X package within the given
        /// Community folder. Searches the known folder names and returns
        /// the first existing one whose <c>mfd.html</c> can be found.
        /// </summary>
        public static string? FindFbwA380PackageRoot(string communityFolderPath)
        {
            foreach (string candidate in FbwA380PackageFolders)
            {
                string root = Path.Combine(communityFolderPath, candidate);
                if (Directory.Exists(root))
                {
                    string mfd = Path.Combine(root, MfdRelPath, MfdHtmlFileName);
                    if (File.Exists(mfd)) return root;
                }
            }
            return null;
        }

        /// <summary>
        /// Creates the overlay package: manifest + layout + the patched
        /// MFD and EFB HTML + a copy of the bridge JS in each instrument's
        /// folder. Idempotent: re-installing with the same bridge version
        /// is a no-op (returns <see cref="ModPackageResult.AlreadyInstalled"/>).
        /// </summary>
        public static ModPackageResult Install(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;
            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null)
                return ModPackageResult.PmdgPackageNotFound;  // shared enum — "base package missing"

            // Undo any earlier in-place patch (v6/v7) so the FBW package is
            // pristine and the overlay is the single source of truth.
            RepairInPlacePatch(communityFolderPath);

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            if (IsInstalled(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            try
            {
                var layoutEntries = WriteOverlay(fbwRoot, packagePath, bridgeJsSourcePath);
                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), ManifestJson);
                WriteVersionFile(packagePath);
                return ModPackageResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: Install failed: {ex.Message}");
                try { if (Directory.Exists(packagePath)) Directory.Delete(packagePath, recursive: true); } catch { }
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Re-patches the overlay against the current FBW A380X package
        /// content when the bundled bridge version is newer than the
        /// installed one. Used at app startup so a user who first
        /// installed an older overlay automatically picks up improvements.
        /// </summary>
        public static ModPackageResult UpdateModPackage(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;
            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            int installed = GetInstalledVersion(communityFolderPath);
            if (installed >= BridgeVersion)
                return ModPackageResult.AlreadyUpToDate;

            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null)
                return ModPackageResult.PmdgPackageNotFound;

            // Repair any leftover in-place patch (a user updating from a
            // v6/v7 in-place install) before refreshing the overlay.
            RepairInPlacePatch(communityFolderPath);

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            try
            {
                var layoutEntries = WriteOverlay(fbwRoot, packagePath, bridgeJsSourcePath);
                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                WriteVersionFile(packagePath);
                return ModPackageResult.Updated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: Update failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        public static ModPackageResult Remove(string communityFolderPath)
        {
            // Restore any in-place patch (v6/v7) the FBW package may still carry.
            bool repaired = RepairInPlacePatch(communityFolderPath);

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            bool overlayExisted = Directory.Exists(packagePath);
            if (!overlayExisted && !repaired)
                return ModPackageResult.CommunityFolderNotFound;
            try
            {
                if (overlayExisted)
                    Directory.Delete(packagePath, recursive: true);
                return ModPackageResult.Removed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        // ------- in-place patch repair (legacy v6/v7 cleanup) --------------
        //
        //  v6/v7 modified the FlyByWire A380X package directly (in-place patch
        //  of mfd.html/efb.html + edits to FBW's own layout.json). v8 reverts
        //  to the overlay model, so before writing the overlay we restore the
        //  FBW package to pristine from the .msfsba_backup files this app made.
        //  Safe to call unconditionally: a no-op when no backups exist (a fresh
        //  install, an FS2020 install, or a package never patched in place).

        /// <summary>
        /// Reverts any earlier in-place patch of the FBW A380X package by
        /// restoring mfd.html / efb.html / layout.json from their
        /// <c>.msfsba_backup</c> siblings and deleting the dropped bridge/
        /// marker JS plus the version marker. Returns true if anything was
        /// restored. No-op (returns false) when no backups are present.
        /// </summary>
        private static bool RepairInPlacePatch(string communityFolderPath)
        {
            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null) return false;

            bool repairedAnything = false;
            try
            {
                void RestoreOne(string relPath, string htmlFile)
                {
                    string instrumentDir = Path.Combine(fbwRoot, relPath);
                    string htmlPath = Path.Combine(instrumentDir, htmlFile);
                    string backup = htmlPath + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, htmlPath, overwrite: true);
                        File.Delete(backup);
                        repairedAnything = true;
                    }
                    // Remove the dropped companion JS files left in the FBW dir.
                    string bridgeJs = Path.Combine(instrumentDir, BridgeJsFileName);
                    string markerJs = Path.Combine(instrumentDir, MarkerJsFileName);
                    if (File.Exists(bridgeJs)) { File.Delete(bridgeJs); repairedAnything = true; }
                    if (File.Exists(markerJs)) { File.Delete(markerJs); repairedAnything = true; }
                }

                RestoreOne(MfdRelPath, MfdHtmlFileName);
                RestoreOne(EfbRelPath, EfbHtmlFileName);

                // Restore FBW's layout.json from backup if we edited it.
                string layoutPath = Path.Combine(fbwRoot, "layout.json");
                string layoutBackup = layoutPath + BackupSuffix;
                if (File.Exists(layoutBackup))
                {
                    File.Copy(layoutBackup, layoutPath, overwrite: true);
                    File.Delete(layoutBackup);
                    repairedAnything = true;
                }

                string marker = Path.Combine(fbwRoot, FbwVersionMarkerFileName);
                if (File.Exists(marker)) { File.Delete(marker); repairedAnything = true; }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FBWA380ModPackageManager: in-place repair failed: {ex.Message}");
            }
            return repairedAnything;
        }

        // ------- write helpers --------------------------------------------

        private static List<(string relativePath, long size)> WriteOverlay(
            string fbwRoot, string packagePath, string bridgeJsSourcePath)
        {
            string bridgeJsContent = File.ReadAllText(bridgeJsSourcePath);
            var entries = new List<(string, long)>();
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsContent, MfdRelPath, MfdHtmlFileName));
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsContent, EfbRelPath, EfbHtmlFileName));
            return entries;
        }

        private static List<(string relativePath, long size)> WriteOneInstrumentOverlay(
            string fbwRoot, string packagePath, string bridgeJsContent,
            string relPath, string htmlFile)
        {
            var entries = new List<(string, long)>();
            string sourceHtmlPath = Path.Combine(fbwRoot, relPath, htmlFile);
            if (!File.Exists(sourceHtmlPath))
            {
                // Not every dev build ships both instruments — silently
                // skip the missing one rather than failing the whole install.
                System.Diagnostics.Debug.WriteLine(
                    $"FBWA380ModPackageManager: skipping {relPath}/{htmlFile} — not present in {fbwRoot}");
                return entries;
            }

            string overlayDir = Path.Combine(packagePath, relPath);
            Directory.CreateDirectory(overlayDir);

            // Drop the bridge + marker JS into the overlay and reference them
            // from the patched HTML via import-script — the only script form
            // MSFS instrument templates actually execute. All three files are
            // registered in the overlay's layout.json (returned entries).
            string markerJsPath = Path.Combine(overlayDir, MarkerJsFileName);
            File.WriteAllText(markerJsPath, MarkerJsContent);
            string bridgeJsPath = Path.Combine(overlayDir, BridgeJsFileName);
            File.WriteAllText(bridgeJsPath, bridgeJsContent);

            string originalHtml = File.ReadAllText(sourceHtmlPath);
            string patchedHtml = BuildPatchedHtml(originalHtml, relPath);
            string overlayHtmlPath = Path.Combine(overlayDir, htmlFile);
            File.WriteAllText(overlayHtmlPath, patchedHtml);

            entries.Add(($"{relPath}/{htmlFile}", new FileInfo(overlayHtmlPath).Length));
            entries.Add(($"{relPath}/{MarkerJsFileName}", new FileInfo(markerJsPath).Length));
            entries.Add(($"{relPath}/{BridgeJsFileName}", new FileInfo(bridgeJsPath).Length));
            return entries;
        }

        private static void WriteVersionFile(string packagePath)
        {
            File.WriteAllText(Path.Combine(packagePath, VersionFileName), BridgeVersion.ToString());
        }

        private static string GenerateLayoutJson(List<(string relativePath, long size)> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"content\": [");
            for (int i = 0; i < entries.Count; i++)
            {
                var (p, sz) = entries[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"path\": \"{p.Replace("\\", "/")}\",");
                sb.AppendLine($"      \"size\": {sz},");
                sb.AppendLine("      \"date\": 133888888888888888");
                sb.Append("    }");
                sb.AppendLine(i < entries.Count - 1 ? "," : "");
            }
            sb.AppendLine("  ]");
            sb.Append("}");
            return sb.ToString();
        }

        private static string? TryParseInstalledPackagesPath(string configPath)
        {
            if (!File.Exists(configPath)) return null;
            try
            {
                foreach (string line in File.ReadLines(configPath))
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith("InstalledPackagesPath") &&
                        !trimmed.StartsWith("InstalledPackagesPathNextBoot"))
                    {
                        int quoteStart = line.IndexOf('"');
                        int quoteEnd = line.LastIndexOf('"');
                        if (quoteStart >= 0 && quoteEnd > quoteStart)
                            return line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
