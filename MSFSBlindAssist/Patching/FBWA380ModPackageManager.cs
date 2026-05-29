using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

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
        //     we now back up the original mfd.html/efb.html, inline the bridge
        //     into the originals, and update the FBW package's own layout.json
        //     so MSFS's file-size validation passes. FS2020 keeps the overlay.
        private const int BridgeVersion = 6;
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

        // Marker we look for when deciding whether the HTML is already
        // patched (avoids appending the script tag twice on re-install).
        // The marker is the inline-bridge sentinel so we don't get a
        // stale match on a previously-import-script-patched HTML.
        private const string PatchMarker = "/* MSFSBA-A380-BRIDGE-INLINE */";

        // HTML-loaded marker — fires the instant the patched HTML is
        // parsed, regardless of whether the inlined bridge JS later
        // throws or succeeds. Lets the form distinguish "MSFS never
        // loaded my override HTML" (Stage 0 + HtmlLoaded=0) from
        // "MSFS loaded the HTML but the bridge JS broke" (Stage 0 +
        // HtmlLoaded=1).
        private const string HtmlLoadedMarkerScript =
            "<script>try{if(typeof SimVar!=='undefined'&&SimVar.SetSimVarValue){SimVar.SetSimVarValue('L:MSFSBA_FBWA380_HTML_LOADED','number',1);}}catch(e){}</script>";

        /// <summary>
        /// Builds the patched HTML by inlining the marker + the full bridge
        /// JS into the original HTML. This bypasses MSFS's import-script
        /// loader entirely — if the override HTML is being scanned at all,
        /// the inlined code runs in plain Coherent GT script execution.
        /// </summary>
        private static string BuildInlinedHtml(string originalHtml, string bridgeJsContent)
        {
            // Strip any old import-script reference we might have written
            // in a previous version so we don't end up trying to load the
            // file twice.
            string cleaned = StripOldImportScriptTag(originalHtml);
            return cleaned.TrimEnd()
                + "\n" + HtmlLoadedMarkerScript
                + "\n<script>" + PatchMarker + "\n"
                + bridgeJsContent
                + "\n</script>\n";
        }

        /// <summary>
        /// Removes a previous-style import-script tag pointing at our
        /// bridge JS file, so a re-install switches cleanly from the
        /// external-file approach to the inlined approach.
        /// </summary>
        private static string StripOldImportScriptTag(string html)
        {
            int i = html.IndexOf(BridgeJsFileName, StringComparison.Ordinal);
            if (i < 0) return html;
            int lineStart = html.LastIndexOf('\n', i);
            int lineEnd = html.IndexOf('\n', i);
            if (lineStart < 0) lineStart = 0;
            if (lineEnd < 0) lineEnd = html.Length;
            return html.Substring(0, lineStart) + html.Substring(lineEnd);
        }

        // ------- FS2024 detection ------------------------------------------

        /// <summary>
        /// True when the given Community folder belongs to MSFS 2024. The
        /// path is sim-specific: the MS Store FS2024 cache lives under
        /// <c>Microsoft.Limitless_8wekyb3d8bbwe</c> and the Steam FS2024
        /// install path contains "Microsoft Flight Simulator 2024", whereas
        /// FS2020 uses <c>Microsoft.FlightSimulator</c> / "Microsoft Flight
        /// Simulator" (no 2024). Ambiguous fallback paths (e.g. D:\MSFS\
        /// Community) resolve to FS2020 — the overlay path, which is the
        /// historical default and harmless if wrong on a non-FS2024 install.
        /// </summary>
        private static bool IsFs2024(string communityFolderPath)
        {
            if (string.IsNullOrEmpty(communityFolderPath)) return false;
            if (communityFolderPath.IndexOf("Limitless", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (communityFolderPath.IndexOf("Flight Simulator 2024", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ------- public API ------------------------------------------------

        /// <summary>
        /// True if the bridge is installed in the given Community folder. On
        /// FS2020 this means the overlay package exists with a patched HTML;
        /// on FS2024 it means the FBW A380X package's own HTML has been
        /// patched in place (overlay packages are silently ignored there).
        /// </summary>
        public static bool IsInstalled(string communityFolderPath)
        {
            if (IsFs2024(communityFolderPath))
                return IsInstalledFs2024(communityFolderPath);

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            return File.Exists(Path.Combine(packagePath, MfdRelPath, MfdHtmlFileName))
                || File.Exists(Path.Combine(packagePath, EfbRelPath, EfbHtmlFileName));
        }

        public static int GetInstalledVersion(string communityFolderPath)
        {
            if (IsFs2024(communityFolderPath))
                return GetInstalledVersionFs2024(communityFolderPath);

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
            if (IsFs2024(communityFolderPath))
                return InstallFs2024(communityFolderPath, bridgeJsSourcePath, isUpdate: false);

            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;
            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null)
                return ModPackageResult.PmdgPackageNotFound;  // shared enum — "base package missing"

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
            if (IsFs2024(communityFolderPath))
            {
                if (!File.Exists(bridgeJsSourcePath))
                    return ModPackageResult.BridgeJsSourceNotFound;
                if (GetInstalledVersion(communityFolderPath) >= BridgeVersion)
                    return ModPackageResult.AlreadyUpToDate;
                // Force the in-place install path: it re-derives from the
                // pristine backups, so re-patching is clean, and it also cleans
                // up any obsolete overlay package left by pre-v6 installs.
                return InstallFs2024(communityFolderPath, bridgeJsSourcePath, isUpdate: true);
            }

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
            if (IsFs2024(communityFolderPath))
            {
                // Also wipe any legacy overlay package left from pre-v6 installs.
                string legacyPackage = Path.Combine(communityFolderPath, PackageFolderName);
                if (Directory.Exists(legacyPackage))
                {
                    try { Directory.Delete(legacyPackage, recursive: true); } catch { }
                }
                return RemoveFs2024(communityFolderPath);
            }

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            if (!Directory.Exists(packagePath))
                return ModPackageResult.CommunityFolderNotFound;
            try
            {
                Directory.Delete(packagePath, recursive: true);
                return ModPackageResult.Removed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        // ------- FS2024 in-place patching ----------------------------------
        //
        //  MSFS 2024 silently refuses community-on-community html_ui overrides
        //  under the protected VCockpit/Instruments namespace, so instead of a
        //  separate overlay package we modify the FlyByWire A380X package
        //  directly: back up the pristine mfd.html/efb.html, inline the bridge
        //  into the originals, and update the FBW package's own layout.json so
        //  MSFS's file-size validation accepts the grown files. Backups land
        //  beside the originals for a clean uninstall. (Mirrors HS787 v18.)

        private static bool IsInstalledFs2024(string communityFolderPath)
        {
            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null) return false;
            // The patch marker is detectable on the MFD HTML — checking it suffices.
            string mfd = Path.Combine(fbwRoot, MfdRelPath, MfdHtmlFileName);
            return File.Exists(mfd) && File.ReadAllText(mfd).Contains(PatchMarker);
        }

        private static int GetInstalledVersionFs2024(string communityFolderPath)
        {
            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null) return 0;
            string marker = Path.Combine(fbwRoot, FbwVersionMarkerFileName);
            if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker).Trim(), out int v))
                return v;
            return IsInstalledFs2024(communityFolderPath) ? 1 : 0;
        }

        private static ModPackageResult InstallFs2024(
            string communityFolderPath, string bridgeJsSourcePath, bool isUpdate)
        {
            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;
            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null)
                return ModPackageResult.PmdgPackageNotFound;  // shared enum — "base package missing"

            // Idempotent: a plain Install against an already-patched package
            // bails out. Update re-patches (so a bumped BridgeVersion re-applies).
            if (!isUpdate && IsInstalledFs2024(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            try
            {
                string bridgeJsContent = File.ReadAllText(bridgeJsSourcePath);

                PatchInstrumentInPlace(fbwRoot, MfdRelPath, MfdHtmlFileName, bridgeJsContent);
                PatchInstrumentInPlace(fbwRoot, EfbRelPath, EfbHtmlFileName, bridgeJsContent);

                // MSFS validates each file's size against layout.json — update
                // the FBW package's layout so the grown HTML files load.
                UpdateFbwLayoutJson(fbwRoot);

                // Version marker (we can't touch FBW's manifest).
                File.WriteAllText(Path.Combine(fbwRoot, FbwVersionMarkerFileName), BridgeVersion.ToString());

                // Remove any obsolete overlay package from pre-v6 installs — on
                // FS2024 it does nothing functional and only adds confusion.
                string overlayPackage = Path.Combine(communityFolderPath, PackageFolderName);
                if (Directory.Exists(overlayPackage))
                {
                    try { Directory.Delete(overlayPackage, recursive: true); } catch { }
                }

                return isUpdate ? ModPackageResult.Updated : ModPackageResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: FS2024 Install failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// In-place patches one FBW instrument HTML: back up the pristine file
        /// once (preserving the first known-good state), then always re-derive
        /// the patched content from that backup so re-patches don't stack. No-op
        /// if the instrument HTML isn't present in this build.
        /// </summary>
        private static void PatchInstrumentInPlace(
            string fbwRoot, string relPath, string htmlFile, string bridgeJsContent)
        {
            string htmlPath = Path.Combine(fbwRoot, relPath, htmlFile);
            if (!File.Exists(htmlPath)) return;

            string backupPath = htmlPath + BackupSuffix;
            if (!File.Exists(backupPath))
                File.Copy(htmlPath, backupPath, overwrite: false);

            string original = File.ReadAllText(backupPath);
            File.WriteAllText(htmlPath, BuildInlinedHtml(original, bridgeJsContent));
        }

        /// <summary>
        /// Updates the FBW A380X package's layout.json so the patched (grown)
        /// mfd.html / efb.html sizes match what MSFS validates on load. Backs
        /// the layout up once. Idempotent — only sizes change.
        /// </summary>
        private static void UpdateFbwLayoutJson(string fbwRoot)
        {
            string layoutPath = Path.Combine(fbwRoot, "layout.json");
            if (!File.Exists(layoutPath)) return;

            string backupPath = layoutPath + BackupSuffix;
            if (!File.Exists(backupPath))
                File.Copy(layoutPath, backupPath, overwrite: false);

            var layout = JObject.Parse(File.ReadAllText(layoutPath));
            var content = (JArray?)layout["content"];
            if (content == null) return;

            // Slash-agnostic, case-insensitive canonical form for matching.
            string Canonical(string p) => p.Replace('\\', '/').ToLowerInvariant();

            void UpdateHtmlSize(string relPath, string htmlFile)
            {
                string absPath = Path.Combine(fbwRoot, relPath, htmlFile);
                if (!File.Exists(absPath)) return;
                long newSize = new FileInfo(absPath).Length;
                string targetCanonical = Canonical($"{relPath}/{htmlFile}");
                foreach (var entry in content)
                {
                    string? p = entry["path"]?.ToString();
                    if (p != null && Canonical(p) == targetCanonical)
                    {
                        entry["size"] = newSize;
                        break;
                    }
                }
            }

            UpdateHtmlSize(MfdRelPath, MfdHtmlFileName);
            UpdateHtmlSize(EfbRelPath, EfbHtmlFileName);

            File.WriteAllText(layoutPath, layout.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static ModPackageResult RemoveFs2024(string communityFolderPath)
        {
            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null)
                return ModPackageResult.CommunityFolderNotFound;

            try
            {
                void RestoreOne(string relPath, string htmlFile)
                {
                    string htmlPath = Path.Combine(fbwRoot, relPath, htmlFile);
                    string backup = htmlPath + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, htmlPath, overwrite: true);
                        File.Delete(backup);
                    }
                }

                RestoreOne(MfdRelPath, MfdHtmlFileName);
                RestoreOne(EfbRelPath, EfbHtmlFileName);

                // Restore layout.json from backup if present.
                string layoutPath = Path.Combine(fbwRoot, "layout.json");
                string layoutBackup = layoutPath + BackupSuffix;
                if (File.Exists(layoutBackup))
                {
                    File.Copy(layoutBackup, layoutPath, overwrite: true);
                    File.Delete(layoutBackup);
                }

                string marker = Path.Combine(fbwRoot, FbwVersionMarkerFileName);
                if (File.Exists(marker)) File.Delete(marker);

                return ModPackageResult.Removed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FBWA380ModPackageManager: FS2024 Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
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

            // Inline the bridge JS directly into the HTML rather than
            // referencing it via import-script. This bypasses MSFS's
            // instrument-loader script-import path so the bridge runs
            // off plain Coherent GT HTML parsing — which (a) is harder
            // for MSFS to silently skip, and (b) lets us prove via the
            // HtmlLoadedMarkerScript whether the override HTML is being
            // picked up at all.
            string originalHtml = File.ReadAllText(sourceHtmlPath);
            string patchedHtml = originalHtml.Contains(PatchMarker)
                ? originalHtml
                : BuildInlinedHtml(originalHtml, bridgeJsContent);
            string overlayHtmlPath = Path.Combine(overlayDir, htmlFile);
            File.WriteAllText(overlayHtmlPath, patchedHtml);

            entries.Add(($"{relPath}/{htmlFile}", new FileInfo(overlayHtmlPath).Length));
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
