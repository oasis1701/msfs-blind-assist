using System.IO;
using System.Text;

namespace MSFSBlindAssist.Patching
{
    public enum ModPackageResult
    {
        Success,
        AlreadyInstalled,
        CommunityFolderNotFound,
        BridgeJsSourceNotFound,
        PmdgPackageNotFound,
        InstallFailed,
        Removed
    }

    public static class EFBModPackageManager
    {
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
        /// Finds the MSFS Community folder by checking known config locations.
        /// </summary>
        public static string? FindCommunityFolderPath()
        {
            string[] configPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"),
            };

            foreach (string configPath in configPaths)
            {
                string? basePath = TryParseInstalledPackagesPath(configPath);
                if (basePath != null)
                {
                    string communityPath = Path.Combine(basePath, "Community");
                    if (Directory.Exists(communityPath))
                    {
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
                    string modifiedHtml = originalHtml.TrimEnd() + GetBridgeScriptTag(variantSubfolder);
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

                // Write manifest.json
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), ManifestJson);

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
        /// Updates the bridge JS file in an existing mod package installation.
        /// Regenerates layout.json with the new file size.
        /// </summary>
        public static ModPackageResult UpdateBridgeJs(string communityFolderPath, string bridgeJsSourcePath)
        {
            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            if (!File.Exists(bridgeJsSourcePath))
                return ModPackageResult.BridgeJsSourceNotFound;

            try
            {
                string packagePath = Path.Combine(communityFolderPath, PackageFolderName);
                var layoutEntries = new List<(string relativePath, long size)>();

                // Update bridge JS in all installed variant folders
                foreach (var (_, variantSubfolder) in Variants)
                {
                    string htmlRelPath = GetHtmlRelativePath(variantSubfolder);
                    string htmlDir = Path.Combine(packagePath, htmlRelPath);
                    string htmlPath = Path.Combine(htmlDir, HtmlFileName);

                    if (!File.Exists(htmlPath))
                        continue;

                    // Copy updated bridge JS
                    string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                    File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                    layoutEntries.Add(($"{htmlRelPath}/{HtmlFileName}", new FileInfo(htmlPath).Length));
                    layoutEntries.Add(($"{htmlRelPath}/{BridgeJsFileName}", new FileInfo(bridgeJsDest).Length));
                }

                // Regenerate layout.json for all variants
                string layoutJson = GenerateLayoutJson(layoutEntries);
                File.WriteAllText(Path.Combine(packagePath, "layout.json"), layoutJson);

                return ModPackageResult.Success;
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
