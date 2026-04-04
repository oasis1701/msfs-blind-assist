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
        InstallFailed,
        Removed
    }

    public static class EFBModPackageManager
    {
        private const string PackageFolderName = "zzz-pmdg-efb-accessibility";
        private const string HtmlRelativePath = "html_ui/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER";
        private const string HtmlFileName = "PMDGTabletCA.html";
        private const string BridgeJsFileName = "pmdg-efb-accessibility-bridge.js";

        // The modified HTML that loads the bridge script alongside the original EFB
        private const string ModifiedHtml = @"<script>
  // Create a new link element
  var link = document.createElement('link');

  // Set the attributes of the link element
  link.href = ""/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/Assets/fontawesome/css/all.css"";
  link.rel = ""stylesheet"";

  // Append the link element to the head of the document
  document.head.appendChild(link);
</script>

<script type=""text/html"" id=""PMDGTablet"">
    <div class=""container-flex"" id=""FullScreen"" data-tablet-side=""ca"">
        <div id=""StatusBar"">
        </div>
        <div id=""AppContent"">
        </div>
    </div>
</script>

<script type=""text/html"" import-script=""/JS/dataStorage.js""></script>
<script type=""text/html"" import-script=""/JS/Services/CommBus.js""></script>

<link rel=""stylesheet"" href=""PMDGTablet.css"" />
<script>
  window.pmdg_tablet_path = ""/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER"";
</script>
<script type=""text/html"" import-script=""/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/PMDGTablet.js""></script>
<script type=""text/html"" import-script=""/Pages/VCockpit/Instruments/PMDGTablet/pmdg-777-200ER/pmdg-efb-accessibility-bridge.js""></script>";

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
            string htmlPath = Path.Combine(packagePath, HtmlRelativePath, HtmlFileName);
            return File.Exists(htmlPath);
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

            // Fallback to common manual install paths
            string[] commonPaths = new[]
            {
                @"D:\MSFS\Community",
                @"C:\MSFS\Community",
                @"E:\MSFS\Community",
            };

            foreach (string path in commonPaths)
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
            string htmlDir = Path.Combine(packagePath, HtmlRelativePath);

            if (IsInstalled(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            try
            {
                // Create directory structure
                Directory.CreateDirectory(htmlDir);

                // Write the modified HTML
                string htmlPath = Path.Combine(htmlDir, HtmlFileName);
                File.WriteAllText(htmlPath, ModifiedHtml);

                // Copy the bridge JS
                string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                // Generate layout.json with correct file sizes
                long htmlSize = new FileInfo(htmlPath).Length;
                long jsSize = new FileInfo(bridgeJsDest).Length;
                string layoutJson = GenerateLayoutJson(htmlSize, jsSize);
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
                string htmlDir = Path.Combine(packagePath, HtmlRelativePath);

                // Copy updated bridge JS
                string bridgeJsDest = Path.Combine(htmlDir, BridgeJsFileName);
                File.Copy(bridgeJsSourcePath, bridgeJsDest, overwrite: true);

                // Regenerate layout.json
                string htmlPath = Path.Combine(htmlDir, HtmlFileName);
                long htmlSize = new FileInfo(htmlPath).Length;
                long jsSize = new FileInfo(bridgeJsDest).Length;
                string layoutJson = GenerateLayoutJson(htmlSize, jsSize);
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

        private static string GenerateLayoutJson(long htmlSize, long jsSize)
        {
            return $@"{{
  ""content"": [
    {{
      ""path"": ""{HtmlRelativePath.Replace("\\", "/")}/{HtmlFileName}"",
      ""size"": {htmlSize},
      ""date"": 133888888888888888
    }},
    {{
      ""path"": ""{HtmlRelativePath.Replace("\\", "/")}/{BridgeJsFileName}"",
      ""size"": {jsSize},
      ""date"": 133888888888888888
    }}
  ]
}}";
        }

        private static string? TryParseInstalledPackagesPath(string configPath)
        {
            if (!File.Exists(configPath)) return null;

            try
            {
                foreach (string line in File.ReadLines(configPath))
                {
                    if (line.TrimStart().StartsWith("InstalledPackagesPath"))
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
