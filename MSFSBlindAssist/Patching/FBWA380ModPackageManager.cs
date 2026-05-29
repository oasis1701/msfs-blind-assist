using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// Installs / updates / removes the accessibility bridge
    /// (<c>fbw-a380-bridge.js</c>) for the FlyByWire A380X's MFD and EFB
    /// instruments.
    ///
    /// This is modelled byte-for-byte on the proven-working HS787 bridge
    /// (<see cref="HS787ModPackageManager"/>): on MSFS 2024 we patch the
    /// FlyByWire A380X package IN PLACE (append a single bare
    /// <c>import-script</c> tag to each instrument HTML, drop the bridge JS
    /// beside the instrument's own JS, and register it in the package's
    /// layout.json); on MSFS 2020 we ship a separate <c>zzz-</c> overlay
    /// package. Backups (.msfsba_backup) beside each touched file allow a
    /// clean uninstall.
    ///
    /// Base FBW A380X paths (verified against master/fbw-a380x and the
    /// installed package on disk):
    ///   - MFD: <c>html_ui/Pages/VCockpit/Instruments/A380X/MFD/mfd.html</c>
    ///   - EFB: <c>html_ui/Pages/VCockpit/Instruments/A380X/EFB/efb.html</c>
    /// </summary>
    public static class FBWA380ModPackageManager
    {
        // Bump on any bridge-JS or patch-structure change — triggers an
        // automatic re-patch on app start for older installs.
        //
        // v9: CRITICAL bring-up fix. Earlier versions appended TWO
        //     import-script tags (a separate msfsba-html-marker.js plus the
        //     bridge) and an HTML comment to the instrument template. On the
        //     A380X template the Coherent loader choked on that multi-tag /
        //     commented block and executed NEITHER script (HTML loaded: NO,
        //     Stage: 0) — and because the overlay and in-place paths shared
        //     the same BuildPatchedHtml, BOTH failed identically. v9 matches
        //     the verified-working HS787 bridge exactly: a SINGLE bare
        //     `<script type="text/html" import-script="…"></script>` tag,
        //     nothing else. The HTML-loaded marker moved into the bridge JS's
        //     first executable line, so we keep the diagnostic with one tag.
        //     FS2024 returns to in-place patching (what HS787 does and what
        //     the user confirmed works on their machine); FS2020 keeps the
        //     overlay package. Both share the new single-tag BuildPatchedHtml.
        private const int BridgeVersion = 9;
        private const string VersionFileName = "bridge-version.txt";

        // Backup suffix for pristine files kept beside each in-place patched
        // file (FS2024). Restored on Remove.
        private const string BackupSuffix = ".msfsba_backup";

        // Version marker dropped inside the FBW package on the FS2024 in-place
        // path (we can't touch FBW's manifest). Detects installed version
        // across app restarts.
        private const string FbwVersionMarkerFileName = "msfsba-bridge-version.txt";

        private const string PackageFolderName = "zzz-fbw-a380-msfsba-bridge";
        private const string BridgeJsFileName = "fbw-a380-bridge.js";

        private const string MfdRelPath = "html_ui/Pages/VCockpit/Instruments/A380X/MFD";
        private const string MfdHtmlFileName = "mfd.html";

        private const string EfbRelPath = "html_ui/Pages/VCockpit/Instruments/A380X/EFB";
        private const string EfbHtmlFileName = "efb.html";

        // Absolute (html_ui-rooted) import-script tags — single, bare, no
        // import-async, no comment. This is exactly the form HS787 uses and
        // the form the A380X's own mfd.js/efb.js use.
        private const string MfdBridgeImportScriptTag =
            "\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/A380X/MFD/fbw-a380-bridge.js\"></script>";
        private const string EfbBridgeImportScriptTag =
            "\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/A380X/EFB/fbw-a380-bridge.js\"></script>";

        // The bridge filename itself is the patch marker — it never appears in
        // pristine FBW HTML (which references mfd.js/efb.js, not our bridge).
        private const string PatchMarker = BridgeJsFileName;

        // FlyByWire publishes one stable folder name; accept dev-installer
        // aliases too.
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

        // ------- FS2024 detection ------------------------------------------

        private static bool IsFs2024(string communityFolderPath)
        {
            if (string.IsNullOrEmpty(communityFolderPath)) return false;
            if (communityFolderPath.IndexOf("Limitless", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (communityFolderPath.IndexOf("Flight Simulator 2024", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ------- shared HTML patch -----------------------------------------

        /// <summary>
        /// Appends a single bare import-script tag to a pristine instrument
        /// HTML. No marker JS, no HTML comment, no import-async — matches the
        /// proven HS787 patch and the A380X template's own script style.
        /// </summary>
        private static string BuildPatchedHtml(string originalHtml, string importScriptTag)
        {
            return originalHtml.TrimEnd() + importScriptTag + "\n";
        }

        // ------- package discovery -----------------------------------------

        /// <summary>
        /// Locates the installed FBW A380X package within a Community folder.
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

        // ------- public API ------------------------------------------------

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

        public static ModPackageResult UpdateModPackage(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (IsFs2024(communityFolderPath))
            {
                if (!File.Exists(bridgeJsSourcePath))
                    return ModPackageResult.BridgeJsSourceNotFound;
                if (GetInstalledVersion(communityFolderPath) >= BridgeVersion)
                    return ModPackageResult.AlreadyUpToDate;
                // In-place re-patch re-derives from the pristine backups, so
                // re-patching is always clean.
                return InstallFs2024(communityFolderPath, bridgeJsSourcePath, isUpdate: true);
            }

            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;
            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            if (GetInstalledVersion(communityFolderPath) >= BridgeVersion)
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
                // Wipe any legacy overlay package, then restore in-place patch.
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
        //  MSFS 2024 patches the FlyByWire A380X package directly: back up the
        //  pristine mfd.html/efb.html, append a single bare import-script tag,
        //  drop the bridge JS beside the instrument's own JS, and update the
        //  FBW package's layout.json (MSFS validates file sizes and won't serve
        //  a file that isn't listed). Mirrors HS787 v18+.

        private static bool IsInstalledFs2024(string communityFolderPath)
        {
            string? fbwRoot = FindFbwA380PackageRoot(communityFolderPath);
            if (fbwRoot == null) return false;
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

            if (!isUpdate && IsInstalledFs2024(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            try
            {
                string bridgeJsContent = File.ReadAllText(bridgeJsSourcePath);

                PatchInstrumentInPlace(fbwRoot, MfdRelPath, MfdHtmlFileName, MfdBridgeImportScriptTag, bridgeJsContent);
                PatchInstrumentInPlace(fbwRoot, EfbRelPath, EfbHtmlFileName, EfbBridgeImportScriptTag, bridgeJsContent);

                UpdateFbwLayoutJson(fbwRoot);

                File.WriteAllText(Path.Combine(fbwRoot, FbwVersionMarkerFileName), BridgeVersion.ToString());

                // Remove any obsolete overlay package from earlier installs —
                // on FS2024 it does nothing functional.
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
        /// In-place patches one FBW instrument: back up the pristine HTML once,
        /// drop the bridge JS beside it, then re-derive the patched HTML from
        /// the backup with a single bare import-script tag. Re-derive means
        /// re-patches never stack. No-op if the HTML isn't present.
        /// </summary>
        private static void PatchInstrumentInPlace(
            string fbwRoot, string relPath, string htmlFile, string importScriptTag, string bridgeJsContent)
        {
            string instrumentDir = Path.Combine(fbwRoot, relPath);
            string htmlPath = Path.Combine(instrumentDir, htmlFile);
            if (!File.Exists(htmlPath)) return;

            string backupPath = htmlPath + BackupSuffix;
            if (!File.Exists(backupPath))
                File.Copy(htmlPath, backupPath, overwrite: false);

            File.WriteAllText(Path.Combine(instrumentDir, BridgeJsFileName), bridgeJsContent);

            string original = File.ReadAllText(backupPath);
            File.WriteAllText(htmlPath, BuildPatchedHtml(original, importScriptTag));
        }

        /// <summary>
        /// Updates the FBW A380X package's layout.json: refresh the patched
        /// mfd.html / efb.html sizes and add (idempotently) the dropped bridge
        /// JS entries. Matches the package's existing slash convention. Backs
        /// the layout up once.
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

            // Detect the package's slash convention from the first entry that
            // has any slash (top-level files have none).
            bool useForwardSlash = true;
            foreach (var probeEntry in content)
            {
                string probe = probeEntry["path"]?.ToString() ?? "";
                if (probe.Contains('/')) { useForwardSlash = true; break; }
                if (probe.Contains('\\')) { useForwardSlash = false; break; }
            }
            string Normalize(string p) => useForwardSlash ? p.Replace('\\', '/') : p.Replace('/', '\\');
            string Canonical(string p) => p.Replace('\\', '/').ToLowerInvariant();

            void UpdateSize(string relPath, string fileName)
            {
                string absPath = Path.Combine(fbwRoot, relPath, fileName);
                if (!File.Exists(absPath)) return;
                long newSize = new FileInfo(absPath).Length;
                string targetCanonical = Canonical($"{relPath}/{fileName}");
                foreach (var entry in content)
                {
                    string? p = entry["path"]?.ToString();
                    if (p != null && Canonical(p) == targetCanonical)
                    {
                        entry["size"] = newSize;
                        return;
                    }
                }
            }

            void UpsertJs(string relPath, string fileName)
            {
                string absPath = Path.Combine(fbwRoot, relPath, fileName);
                if (!File.Exists(absPath)) return;
                long size = new FileInfo(absPath).Length;
                string normalized = Normalize($"{relPath}/{fileName}");
                string targetCanonical = Canonical(normalized);
                for (int i = content.Count - 1; i >= 0; i--)
                {
                    string? p = content[i]["path"]?.ToString();
                    if (p != null && Canonical(p) == targetCanonical)
                        content.RemoveAt(i);
                }
                content.Add(new JObject
                {
                    ["path"] = normalized,
                    ["size"] = size,
                    ["date"] = 133888888888888888L
                });
            }

            UpdateSize(MfdRelPath, MfdHtmlFileName);
            UpdateSize(EfbRelPath, EfbHtmlFileName);
            UpsertJs(MfdRelPath, BridgeJsFileName);
            UpsertJs(EfbRelPath, BridgeJsFileName);

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
                    string instrumentDir = Path.Combine(fbwRoot, relPath);
                    string htmlPath = Path.Combine(instrumentDir, htmlFile);
                    string backup = htmlPath + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, htmlPath, overwrite: true);
                        File.Delete(backup);
                    }
                    string bridgeJs = Path.Combine(instrumentDir, BridgeJsFileName);
                    if (File.Exists(bridgeJs)) File.Delete(bridgeJs);
                }

                RestoreOne(MfdRelPath, MfdHtmlFileName);
                RestoreOne(EfbRelPath, EfbHtmlFileName);

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

        // ------- FS2020 overlay package ------------------------------------

        private static List<(string relativePath, long size)> WriteOverlay(
            string fbwRoot, string packagePath, string bridgeJsSourcePath)
        {
            string bridgeJsContent = File.ReadAllText(bridgeJsSourcePath);
            var entries = new List<(string, long)>();
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsContent, MfdRelPath, MfdHtmlFileName, MfdBridgeImportScriptTag));
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsContent, EfbRelPath, EfbHtmlFileName, EfbBridgeImportScriptTag));
            return entries;
        }

        private static List<(string relativePath, long size)> WriteOneInstrumentOverlay(
            string fbwRoot, string packagePath, string bridgeJsContent,
            string relPath, string htmlFile, string importScriptTag)
        {
            var entries = new List<(string, long)>();
            string sourceHtmlPath = Path.Combine(fbwRoot, relPath, htmlFile);
            if (!File.Exists(sourceHtmlPath))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"FBWA380ModPackageManager: skipping {relPath}/{htmlFile} — not present in {fbwRoot}");
                return entries;
            }

            string overlayDir = Path.Combine(packagePath, relPath);
            Directory.CreateDirectory(overlayDir);

            string bridgeJsPath = Path.Combine(overlayDir, BridgeJsFileName);
            File.WriteAllText(bridgeJsPath, bridgeJsContent);

            string originalHtml = File.ReadAllText(sourceHtmlPath);
            string overlayHtmlPath = Path.Combine(overlayDir, htmlFile);
            File.WriteAllText(overlayHtmlPath, BuildPatchedHtml(originalHtml, importScriptTag));

            entries.Add(($"{relPath}/{htmlFile}", new FileInfo(overlayHtmlPath).Length));
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

        // ------- Community folder resolution -------------------------------

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
