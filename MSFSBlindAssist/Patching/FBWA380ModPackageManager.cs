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
        private const int BridgeVersion = 1;
        private const string VersionFileName = "bridge-version.txt";

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
        private const string PatchMarker = BridgeJsFileName;

        // Each instrument needs its own script tag (relative path within
        // that instrument's folder).
        private static string GetMfdBridgeScriptTag() =>
            $"\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/A380X/MFD/{BridgeJsFileName}\"></script>";

        private static string GetEfbBridgeScriptTag() =>
            $"\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/A380X/EFB/{BridgeJsFileName}\"></script>";

        // ------- public API ------------------------------------------------

        /// <summary>
        /// True if the overlay package exists and contains at least one
        /// patched HTML file. Doesn't validate the patched HTML's contents
        /// — <see cref="UpdateModPackage"/> handles version drift.
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

        // ------- write helpers --------------------------------------------

        private static List<(string relativePath, long size)> WriteOverlay(
            string fbwRoot, string packagePath, string bridgeJsSourcePath)
        {
            var entries = new List<(string, long)>();
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsSourcePath, MfdRelPath, MfdHtmlFileName, GetMfdBridgeScriptTag()));
            entries.AddRange(WriteOneInstrumentOverlay(
                fbwRoot, packagePath, bridgeJsSourcePath, EfbRelPath, EfbHtmlFileName, GetEfbBridgeScriptTag()));
            return entries;
        }

        private static List<(string relativePath, long size)> WriteOneInstrumentOverlay(
            string fbwRoot, string packagePath, string bridgeJsSourcePath,
            string relPath, string htmlFile, string scriptTag)
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

            string originalHtml = File.ReadAllText(sourceHtmlPath);
            string patchedHtml = originalHtml.Contains(PatchMarker)
                ? originalHtml
                : originalHtml.TrimEnd() + scriptTag;
            string overlayHtmlPath = Path.Combine(overlayDir, htmlFile);
            File.WriteAllText(overlayHtmlPath, patchedHtml);

            string overlayJsPath = Path.Combine(overlayDir, BridgeJsFileName);
            File.Copy(bridgeJsSourcePath, overlayJsPath, overwrite: true);

            entries.Add(($"{relPath}/{htmlFile}", new FileInfo(overlayHtmlPath).Length));
            entries.Add(($"{relPath}/{BridgeJsFileName}", new FileInfo(overlayJsPath).Length));
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
