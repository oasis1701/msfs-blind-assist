using System.IO;
using System.Text;

namespace MSFSBlindAssist.Patching
{
    public enum ModPackageResult
    {
        Success,
        AlreadyInstalled,
        AlreadyUpToDate,
        Updated,
        CommunityFolderNotFound,
        BridgeJsSourceNotFound,
        PmdgPackageNotFound,
        InstallFailed,
        Removed,
        HS787PackageNotFound
    }

    public static class EFBModPackageManager
    {
        // Bump this version when the bridge JS or package STRUCTURE changes — on app
        // startup UpdateModPackage then re-patches HTML for all variants, copies the
        // latest bridge JS, and regenerates layout.json on every existing install.
        // Adding a new PMDG variant to Variants does NOT require a bump: UpdateModPackage
        // also fires when a variant installed in the sim is missing its override folder
        // (see HasMissingVariantOverride), so a variant the user installs later is picked
        // up without forcing a no-op re-patch on installs that are already complete.
        private const int BridgeVersion = 6;
        private const string VersionFileName = "bridge-version.txt";

        private const string PackageFolderName = "zzz-pmdg-efb-accessibility";
        private const string HtmlBasePath = "html_ui/Pages/VCockpit/Instruments/PMDGTablet";
        private const string HtmlFileName = "PMDGTabletCA.html";
        private const string BridgeJsFileName = "pmdg-efb-accessibility-bridge.js";

        // Each PMDG variant has its own tablet subfolder that needs an override.
        // The 737 and 777 ship the identical EFB app, so one shared bridge JS +
        // package serves both — only the per-variant tablet subfolder differs.
        private static readonly (string PackageFolder, string VariantSubfolder)[] Variants = new[]
        {
            ("pmdg-aircraft-77er", "pmdg-777-200ER"),
            ("pmdg-aircraft-77w", "pmdg-777-300ER"),
            ("pmdg-aircraft-77l", "pmdg-777-200LR"),
            ("pmdg-aircraft-77f", "pmdg-777F"),
            ("pmdg-aircraft-738", "pmdg-737-800"),
            ("pmdg-aircraft-736", "pmdg-737-600"),
            ("pmdg-aircraft-737", "pmdg-737-700"),
            ("pmdg-aircraft-739", "pmdg-737-900"),
        };

        private static string GetHtmlRelativePath(string variantSubfolder) =>
            $"{HtmlBasePath}/{variantSubfolder}";

        private static string GetBridgeScriptTag(string variantSubfolder) =>
            $"\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/PMDGTablet/{variantSubfolder}/{BridgeJsFileName}\"></script>";

        // Default MS Store community folder paths (packages stored inside app LocalCache)
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
  ""title"": ""MSFS Blind Assist - PMDG EFB Bridge"",
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

        /// <summary>
        /// Checks if the mod package is installed in the MSFS Community folder.
        /// </summary>
        public static bool IsInstalled(string communityFolderPath)
        {
            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            foreach (var (_, variantSubfolder) in Variants)
            {
                string htmlPath = Path.Combine(packagePath, GetHtmlRelativePath(variantSubfolder), HtmlFileName);
                if (File.Exists(htmlPath))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds ALL available MSFS Community folder paths, tagged by sim version.
        /// Returns a list of (simLabel, path) tuples.
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

            // Check FS2024
            foreach (string configPath in fs2024Paths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = System.IO.Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath) && !results.Any(r => r.Item2 == communityPath))
                    {
                        results.Add(("MSFS 2024", communityPath));
                        break; // Found FS2024, don't check second path
                    }
                }
            }

            // Check FS2020
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

            // Also check default MS Store paths if nothing found via config
            if (!results.Any(r => r.Item1 == "MSFS 2024"))
            {
                foreach (string path in DefaultMSStoreCommunityPaths)
                {
                    if (Directory.Exists(path) && path.Contains("Limitless"))
                    {
                        results.Add(("MSFS 2024", path));
                        break;
                    }
                }
            }

            // Steam FS2024 default: %AppData%\Microsoft Flight Simulator 2024\Packages\Community
            if (!results.Any(r => r.Item1 == "MSFS 2024"))
            {
                string steamFs2024 = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft Flight Simulator 2024", "Packages", "Community");
                if (Directory.Exists(steamFs2024))
                    results.Add(("MSFS 2024", steamFs2024));
            }

            if (!results.Any(r => r.Item1 == "MSFS 2020"))
            {
                foreach (string path in DefaultMSStoreCommunityPaths)
                {
                    if (Directory.Exists(path) && path.Contains("FlightSimulator"))
                    {
                        results.Add(("MSFS 2020", path));
                        break;
                    }
                }
            }

            // Fallback: common manual install paths
            if (results.Count == 0)
            {
                foreach (string path in FallbackCommunityPaths)
                {
                    if (Directory.Exists(path))
                    {
                        results.Add(("MSFS", path));
                        break;
                    }
                }
            }

            return results;
        }

        public static string? FindCommunityFolderPath()
        {
            // Detect which sim is running and prioritize its paths first.
            // Without this, FS2020 paths are found before FS2024 when both are installed,
            // causing the mod package to be installed in the wrong Community folder.
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

            // Check the running sim's paths first, then fall back to the other
            var configPaths = runningSimulator == "FS2020"
                ? fs2020Paths.Concat(fs2024Paths).ToArray()
                : fs2024Paths.Concat(fs2020Paths).ToArray(); // FS2024 first (also for Unknown)

            foreach (string configPath in configPaths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[EFBModPackageManager] Found Community folder: {communityPath} (sim={runningSimulator})");
                        return communityPath;
                    }
                }
            }

            // Fallback: default MS Store paths (packages inside app LocalCache)
            foreach (string path in DefaultMSStoreCommunityPaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            // Steam FS2024 default
            string steamDefault = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft Flight Simulator 2024", "Packages", "Community");
            if (Directory.Exists(steamDefault))
                return steamDefault;

            // Fallback: common manual install paths
            foreach (string path in FallbackCommunityPaths)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Returns the installed bridge version, or 0 if no version file exists
        /// (pre-versioning installation).
        /// </summary>
        public static int GetInstalledVersion(string communityFolderPath)
        {
            string versionPath = Path.Combine(communityFolderPath, PackageFolderName, VersionFileName);
            if (File.Exists(versionPath) && int.TryParse(File.ReadAllText(versionPath).Trim(), out int version))
                return version;
            // Package exists but no version file = version 1 (pre-versioning)
            if (IsInstalled(communityFolderPath))
                return 1;
            return 0;
        }

        private static void WriteVersionFile(string packagePath)
        {
            File.WriteAllText(Path.Combine(packagePath, VersionFileName), BridgeVersion.ToString());
        }

        /// <summary>
        /// Installs the mod package into the MSFS Community folder.
        /// Creates the package directory with manifest.json, layout.json,
        /// the modified HTML file, and the bridge JS file.
        /// </summary>
        public static ModPackageResult Install(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);

            if (IsInstalled(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            // Find original HTML for each installed PMDG 777 variant
            var foundVariants = FindOriginalPmdgHtmlPerVariant(communityFolderPath);
            if (foundVariants.Count == 0)
                return ModPackageResult.PmdgPackageNotFound;

            try
            {
                var layoutEntries = new List<(string relativePath, long size)>();

                foreach (var (variantSubfolder, originalHtml) in foundVariants)
                {
                    string htmlRelPath = GetHtmlRelativePath(variantSubfolder);
                    string htmlDir = Path.Combine(packagePath, htmlRelPath);
                    Directory.CreateDirectory(htmlDir);

                    // Write the modified HTML: original + variant-specific bridge script tag
                    string htmlPath = Path.Combine(htmlDir, HtmlFileName);
                    string modifiedHtml = originalHtml.Contains(BridgeJsFileName)
                        ? originalHtml  // Already patched — don't double-patch
                        : originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
                    File.WriteAllText(htmlPath, modifiedHtml);

                    // Copy the bridge JS into this variant's folder
                    string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                    File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                    layoutEntries.Add(($"{htmlRelPath}/{HtmlFileName}", new FileInfo(htmlPath).Length));
                    layoutEntries.Add(($"{htmlRelPath}/{BridgeJsFileName}", new FileInfo(bridgeJsDest).Length));
                }

                // Generate layout.json with entries for all variants
                string layoutJson = GenerateLayoutJson(layoutEntries);
                File.WriteAllText(Path.Combine(packagePath, "layout.json"), layoutJson);

                // Write manifest.json and version file
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), ManifestJson);
                WriteVersionFile(packagePath);

                return ModPackageResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBModPackageManager: Install failed: {ex.Message}");
                // Clean up partial install
                try
                {
                    if (Directory.Exists(packagePath))
                        Directory.Delete(packagePath, recursive: true);
                }
                catch { }
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Updates an existing mod package if the app ships a newer bridge version.
        /// Re-patches HTML for all installed variants, adds override folders for any
        /// newly installed variants, copies the latest bridge JS, and regenerates layout.json.
        /// Returns AlreadyUpToDate if no update is needed, or Updated if changes were made.
        /// </summary>
        public static ModPackageResult UpdateModPackage(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            // Run the update when the bridge version advanced OR when a PMDG variant that
            // is installed in this sim is missing its override folder in our package. The
            // missing-variant case covers a variant the user installed AFTER the package
            // already reached the current BridgeVersion — a version-only gate would never
            // pick it up. (This was the 738-only-works-in-2020 bug: the 2024 package's
            // version file already read the current value, so the 737 override folder was
            // never created and the version check permanently blocked the retry.)
            int installedVersion = GetInstalledVersion(communityFolderPath);
            if (installedVersion >= BridgeVersion && !HasMissingVariantOverride(communityFolderPath))
                return ModPackageResult.AlreadyUpToDate;

            try
            {
                string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
                var layoutEntries = new List<(string relativePath, long size)>();

                // Find original HTML for all installed PMDG 777 variants
                var foundVariants = FindOriginalPmdgHtmlPerVariant(communityFolderPath);

                foreach (var (variantSubfolder, originalHtml) in foundVariants)
                {
                    string htmlRelPath = GetHtmlRelativePath(variantSubfolder);
                    string htmlDir = Path.Combine(packagePath, htmlRelPath);
                    Directory.CreateDirectory(htmlDir);

                    // Re-patch HTML with variant-specific bridge script tag
                    string htmlPath = Path.Combine(htmlDir, HtmlFileName);
                    string modifiedHtml = originalHtml.Contains(BridgeJsFileName)
                        ? originalHtml  // Already patched — don't double-patch
                        : originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
                    File.WriteAllText(htmlPath, modifiedHtml);

                    // Copy latest bridge JS
                    string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                    File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                    layoutEntries.Add(($"{htmlRelPath}/{HtmlFileName}", new FileInfo(htmlPath).Length));
                    layoutEntries.Add(($"{htmlRelPath}/{BridgeJsFileName}", new FileInfo(bridgeJsDest).Length));
                }

                // Regenerate layout.json and update version
                string layoutJson = GenerateLayoutJson(layoutEntries);
                File.WriteAllText(Path.Combine(packagePath, "layout.json"), layoutJson);
                WriteVersionFile(packagePath);

                return ModPackageResult.Updated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EFBModPackageManager: Update failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Removes the mod package from the MSFS Community folder.
        /// </summary>
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
                System.Diagnostics.Debug.WriteLine($"EFBModPackageManager: Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Returns true when a PMDG variant whose original tablet HTML is present in this
        /// Community folder does NOT yet have a corresponding override folder in our package.
        /// Lets UpdateModPackage pick up a variant the user installed after the package already
        /// reached the current BridgeVersion, without forcing a re-patch on installs that are
        /// genuinely complete (which would pop a spurious "updated" dialog at every launch).
        /// </summary>
        private static bool HasMissingVariantOverride(string communityFolderPath)
        {
            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
            foreach (var (packageFolder, variantSubfolder) in Variants)
            {
                // Is this PMDG variant actually installed in this sim?
                string originalHtml = Path.Combine(communityFolderPath, packageFolder, GetHtmlRelativePath(variantSubfolder), HtmlFileName);
                if (!File.Exists(originalHtml))
                    continue; // not installed — nothing for us to override

                // Do we already have an override folder for it?
                string overrideHtml = Path.Combine(packagePath, GetHtmlRelativePath(variantSubfolder), HtmlFileName);
                if (!File.Exists(overrideHtml))
                    return true; // a present variant lacks our override
            }
            return false;
        }

        /// <summary>
        /// Finds the original PMDGTabletCA.html for each installed PMDG 777 variant.
        /// Returns a list of (variantSubfolder, htmlContent) for each variant found.
        /// </summary>
        private static List<(string VariantSubfolder, string HtmlContent)> FindOriginalPmdgHtmlPerVariant(string communityFolderPath)
        {
            var results = new List<(string, string)>();
            foreach (var (packageFolder, variantSubfolder) in Variants)
            {
                string pmdgHtmlPath = Path.Combine(communityFolderPath, packageFolder, GetHtmlRelativePath(variantSubfolder), HtmlFileName);
                if (File.Exists(pmdgHtmlPath))
                {
                    results.Add((variantSubfolder, File.ReadAllText(pmdgHtmlPath)));
                }
            }
            return results;
        }

        private static string GenerateLayoutJson(List<(string relativePath, long size)> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"content\": [");
            for (int i = 0; i < entries.Count; i++)
            {
                var (path, size) = entries[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"path\": \"{path.Replace("\\", "/")}\",");
                sb.AppendLine($"      \"size\": {size},");
                sb.AppendLine("      \"date\": 133888888888888888");
                sb.Append("    }");
                if (i < entries.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
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
                        {
                            return line.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Returns true if the given community folder path belongs to an FS2024 installation.
        /// Three-tier check: UserCfg.opt content → MS Store path substring → Steam path substring.
        /// </summary>
        internal static bool IsPathFromFs2024(string communityFolderPath)
        {
            // Primary: check both FS2024 UserCfg.opt locations (covers custom/external paths for
            // both Steam and MS Store once the sim has been run at least once).
            string[] fs2024ConfigPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };

            foreach (string configPath in fs2024ConfigPaths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath == null) continue;

                string communityFromConfig = Path.Combine(basePath, "Community");
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(communityFolderPath),
                        Path.GetFullPath(communityFromConfig),
                        StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (ArgumentException) { } // invalid path — skip
            }

            // Fallback 1: MS Store default path contains "Limitless" in the package store name.
            if (communityFolderPath.Contains("Limitless", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback 2: Steam default path contains "Microsoft Flight Simulator 2024".
            // FS2020 Steam is "Microsoft Flight Simulator" (no year) — no false-match risk.
            if (communityFolderPath.Contains("Microsoft Flight Simulator 2024",
                StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }
    }
}
