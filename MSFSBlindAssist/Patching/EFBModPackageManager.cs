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
        Removed
    }

    public static class EFBModPackageManager
    {
        // Bump this version when the mod package structure or bridge JS changes.
        // On app startup, if the installed version is older, UpdateModPackage will
        // re-patch HTML for all variants, copy the latest bridge JS, and regenerate layout.json.
        // Bump on every meaningful bridge.js change so users on older versions trigger an
        // "Updated" report rather than silent "AlreadyUpToDate". The new always-re-patch
        // logic in UpdateModPackage doesn't need this signal, but it's still useful for
        // telemetry and for the "version bump → forced refresh" semantic some users expect.
        private const int BridgeVersion = 5;
        private const string VersionFileName = "bridge-version.txt";

        private const string PackageFolderName = "zzz-pmdg-efb-accessibility";
        private const string HtmlBasePath = "html_ui/Pages/VCockpit/Instruments/PMDGTablet";
        private const string HtmlFileName = "PMDGTabletCA.html";
        private const string BridgeJsFileName = "pmdg-efb-accessibility-bridge.js";

        // Each PMDG 777 variant has its own tablet subfolder that needs an override
        private static readonly (string PackageFolder, string VariantSubfolder)[] Variants = new[]
        {
            ("pmdg-aircraft-77er", "pmdg-777-200ER"),
            ("pmdg-aircraft-77w", "pmdg-777-300ER"),
            ("pmdg-aircraft-77l", "pmdg-777-200LR"),
            ("pmdg-aircraft-77f", "pmdg-777F"),
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
  ""title"": ""MSFS Blind Assist - PMDG 777 EFB Bridge"",
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
        /// Byte-by-byte equality for small files. Used by <see cref="UpdateModPackage"/> to
        /// decide whether the bridge JS on disk already matches the resource bytes — so we
        /// can skip the write (and avoid bumping the file's modification time) when nothing
        /// has changed. Returns true if both arrays are the same length and contain identical
        /// bytes; false otherwise.
        /// </summary>
        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
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
        /// Refreshes the mod package on every app startup. Previously this was gated on
        /// <see cref="BridgeVersion"/> being newer than the installed version — but that meant
        /// when PMDG shipped an EFB update (new HTML, new button DOM IDs), our cached patched
        /// HTML stayed at whatever PMDG's HTML looked like the first time we patched it. The
        /// user would load MSFS, see our (now-stale) HTML override the real PMDG HTML, and
        /// half the EFB features would break silently. Now we always re-read PMDG's current
        /// HTML, re-patch it with our bridge script tag, and overwrite our cached copy. The
        /// version check is retained only to decide whether to report "Updated" vs "Refreshed"
        /// in telemetry — the work happens unconditionally. File I/O on a handful of files at
        /// startup is negligible (~milliseconds).
        /// </summary>
        public static ModPackageResult UpdateModPackage(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            int installedVersion = GetInstalledVersion(communityFolderPath);
            bool versionBumped = installedVersion < BridgeVersion;

            try
            {
                string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
                var layoutEntries = new List<(string relativePath, long size)>();

                // Always re-read PMDG's current HTML — this catches the case where PMDG
                // shipped an update that changed their EFB markup. Our patched copy needs
                // to stay in sync with PMDG's, otherwise we'd be overriding their new HTML
                // with our stale snapshot of their old HTML.
                var foundVariants = FindOriginalPmdgHtmlPerVariant(communityFolderPath);

                // Every write below is content-conditional: we only touch the filesystem when
                // the destination is actually different from what's there. Steady-state (PMDG
                // hasn't updated, our bridge hasn't updated) hits zero writes per app startup.
                // This avoids triggering any file-watcher MSFS might have on community packages,
                // and avoids churning the package's modification time for no reason.
                bool anythingChanged = false;
                byte[] sourceBridgeJsBytes = File.ReadAllBytes(bridgeJsSourcePath);

                foreach (var (variantSubfolder, originalHtml) in foundVariants)
                {
                    string htmlRelPath = GetHtmlRelativePath(variantSubfolder);
                    string htmlDir = Path.Combine(packagePath, htmlRelPath);
                    Directory.CreateDirectory(htmlDir);

                    // HTML — write only if PMDG's content changed or our cache is missing.
                    string htmlPath = Path.Combine(htmlDir, HtmlFileName);
                    string newPatchedHtml = originalHtml.Contains(BridgeJsFileName)
                        ? originalHtml  // PMDG's own copy already has our script tag (defensive)
                        : originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
                    string? existingPatchedHtml = File.Exists(htmlPath) ? File.ReadAllText(htmlPath) : null;
                    if (existingPatchedHtml != newPatchedHtml)
                    {
                        File.WriteAllText(htmlPath, newPatchedHtml);
                        anythingChanged = true;
                        System.Diagnostics.Debug.WriteLine($"EFBModPackageManager: HTML refreshed for variant {variantSubfolder}");
                    }

                    // Bridge JS — content-compare before writing. The previous version of this
                    // method called File.Copy unconditionally; that always rewrites the file
                    // (overwriting modification time) even when bytes are identical. Now we
                    // hash-compare via raw bytes and only write on a real change.
                    string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                    if (!File.Exists(bridgeJsDest) || !BytesEqual(sourceBridgeJsBytes, File.ReadAllBytes(bridgeJsDest)))
                    {
                        File.WriteAllBytes(bridgeJsDest, sourceBridgeJsBytes);
                        anythingChanged = true;
                        System.Diagnostics.Debug.WriteLine($"EFBModPackageManager: bridge JS refreshed for variant {variantSubfolder}");
                    }

                    layoutEntries.Add(($"{htmlRelPath}/{HtmlFileName}", new FileInfo(htmlPath).Length));
                    layoutEntries.Add(($"{htmlRelPath}/{BridgeJsFileName}", new FileInfo(bridgeJsDest).Length));
                }

                // layout.json — content-compare. MSFS reads file sizes from here, so it MUST be
                // current with the actual file sizes; but rewriting an identical layout.json on
                // every startup is wasteful.
                string layoutJson = GenerateLayoutJson(layoutEntries);
                string layoutPath = Path.Combine(packagePath, "layout.json");
                string? existingLayout = File.Exists(layoutPath) ? File.ReadAllText(layoutPath) : null;
                if (existingLayout != layoutJson)
                {
                    File.WriteAllText(layoutPath, layoutJson);
                    anythingChanged = true;
                }

                // Version file — content-compare. Only touches disk on actual version bump.
                string versionPath = Path.Combine(packagePath, VersionFileName);
                string newVersion = BridgeVersion.ToString();
                string? existingVersion = File.Exists(versionPath) ? File.ReadAllText(versionPath) : null;
                if (existingVersion != newVersion)
                {
                    File.WriteAllText(versionPath, newVersion);
                    anythingChanged = true;
                }

                // Report Updated if any file changed; AlreadyUpToDate when steady state.
                return (versionBumped || anythingChanged)
                    ? ModPackageResult.Updated
                    : ModPackageResult.AlreadyUpToDate;
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
    }
}
