using System.IO;
using System.Text;

namespace MSFSBlindAssist.Patching
{
    /// <summary>
    /// Installs, updates, and removes the MSFS Community mod package that injects the
    /// HorizonSim 787-9 FMC and EFB accessibility bridges into the instrument HTML files.
    ///
    /// Package name: zzz-hs787-accessibility
    /// Patches: HSB789_MFD.GE.html, HSB789_MFD.RR.html, HSB789_EFB.GE.html, HSB789_EFB.RR.html
    /// Bridge JS: hs787-mfd-bridge.js, hs787-efb-bridge.js
    /// Port: 19778
    /// </summary>
    public static class HS787ModPackageManager
    {
        // Bump when bridge JS or package structure changes — triggers auto-update on app start.
        private const int BridgeVersion = 11;
        private const string VersionFileName = "bridge-version.txt";

        private const string PackageFolderName = "zzz-hs787-accessibility";

        private const string MfdBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/MFD";
        private const string EfbBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/EFB";

        private const string MfdBridgeJsFileName = "hs787-mfd-bridge.js";
        private const string EfbBridgeJsFileName = "hs787-efb-bridge.js";

        // Both engine variants share the same folder for MFD and EFB.
        private static readonly string[] MfdHtmlFileNames = { "HSB789_MFD.GE.html", "HSB789_MFD.RR.html" };
        private static readonly string[] EfbHtmlFileNames = { "HSB789_EFB.GE.html", "HSB789_EFB.RR.html" };

        // Use standard <script src> with a relative path so Coherent GT 2.x (FS2024) loads the
        // script. The import-script attribute is Coherent GT 1.x-specific and is ignored by the
        // newer Chromium base in FS2024. Relative path works in both versions because our JS file
        // lives in the same VFS directory as the HTML override.
        private const string MfdBridgeScriptTag =
            "\n<script src=\"hs787-mfd-bridge.js\"></script>";

        private const string EfbBridgeScriptTag =
            "\n<script src=\"hs787-efb-bridge.js\"></script>";

        // Keep the old name for backwards compat with IsInstalled check
        private const string HtmlBasePath = MfdBasePath;
        private static readonly string[] HtmlFileNames = MfdHtmlFileNames;

        private const string ManifestJson = @"{
  ""dependencies"": [],
  ""content_type"": ""MISC"",
  ""title"": ""MSFS Blind Assist - HorizonSim 787-9 FMC Bridge"",
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

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        public static bool IsInstalled(string communityFolderPath)
        {
            string mfdDir = Path.Combine(communityFolderPath, PackageFolderName, HtmlBasePath);
            // Enough to check one of the HTML files — if the folder and one file exist, the package is installed.
            return File.Exists(Path.Combine(mfdDir, HtmlFileNames[0])) ||
                   File.Exists(Path.Combine(mfdDir, HtmlFileNames[1]));
        }

        public static int GetInstalledVersion(string communityFolderPath)
        {
            string versionPath = Path.Combine(communityFolderPath, PackageFolderName, VersionFileName);
            if (File.Exists(versionPath) && int.TryParse(File.ReadAllText(versionPath).Trim(), out int v))
                return v;
            if (IsInstalled(communityFolderPath))
                return 1;
            return 0;
        }

        /// <summary>
        /// Installs the mod package into the given MSFS Community folder.
        /// resourcesDir should be the app's Resources output directory containing both bridge JS files.
        /// </summary>
        public static ModPackageResult Install(string communityFolderPath, string resourcesDir)
        {
            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            string mfdJsSrc = Path.Combine(resourcesDir, MfdBridgeJsFileName);
            string efbJsSrc = Path.Combine(resourcesDir, EfbBridgeJsFileName);

            if (!File.Exists(mfdJsSrc) || !File.Exists(efbJsSrc))
                return ModPackageResult.BridgeJsSourceNotFound;

            if (IsInstalled(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            var (mfdHtmls, efbHtmls) = FindOriginalHS787Htmls(communityFolderPath);
            if (mfdHtmls.Count == 0)
                return ModPackageResult.HS787PackageNotFound;

            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);

            try
            {
                var layoutEntries = new List<(string relativePath, long size)>();

                // --- MFD ---
                string mfdDir = Path.Combine(packagePath, MfdBasePath);
                Directory.CreateDirectory(mfdDir);

                foreach (var (fileName, originalHtml) in mfdHtmls)
                {
                    string htmlPath = Path.Combine(mfdDir, fileName);
                    File.WriteAllText(htmlPath, originalHtml.TrimEnd() + MfdBridgeScriptTag);
                    layoutEntries.Add(($"{MfdBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                }

                string mfdJsDest = Path.Combine(mfdDir, MfdBridgeJsFileName);
                File.Copy(mfdJsSrc, mfdJsDest, overwrite: true);
                layoutEntries.Add(($"{MfdBasePath}/{MfdBridgeJsFileName}", new FileInfo(mfdJsDest).Length));

                // --- EFB ---
                if (efbHtmls.Count > 0)
                {
                    string efbDir = Path.Combine(packagePath, EfbBasePath);
                    Directory.CreateDirectory(efbDir);

                    foreach (var (fileName, originalHtml) in efbHtmls)
                    {
                        string htmlPath = Path.Combine(efbDir, fileName);
                        File.WriteAllText(htmlPath, originalHtml.TrimEnd() + EfbBridgeScriptTag);
                        layoutEntries.Add(($"{EfbBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                    }

                    string efbJsDest = Path.Combine(efbDir, EfbBridgeJsFileName);
                    File.Copy(efbJsSrc, efbJsDest, overwrite: true);
                    layoutEntries.Add(($"{EfbBasePath}/{EfbBridgeJsFileName}", new FileInfo(efbJsDest).Length));
                }

                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), ManifestJson);
                WriteVersionFile(packagePath);

                return ModPackageResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HS787ModPackageManager: Install failed: {ex.Message}");
                try { if (Directory.Exists(packagePath)) Directory.Delete(packagePath, recursive: true); } catch { }
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Updates the mod package if the app ships a newer bridge version.
        /// </summary>
        public static ModPackageResult UpdateModPackage(string communityFolderPath, string resourcesDir)
        {
            if (!IsInstalled(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            string mfdJsSrc = Path.Combine(resourcesDir, MfdBridgeJsFileName);
            string efbJsSrc = Path.Combine(resourcesDir, EfbBridgeJsFileName);

            if (!File.Exists(mfdJsSrc))
                return ModPackageResult.BridgeJsSourceNotFound;

            if (GetInstalledVersion(communityFolderPath) >= BridgeVersion)
                return ModPackageResult.AlreadyUpToDate;

            var (mfdHtmls, efbHtmls) = FindOriginalHS787Htmls(communityFolderPath);
            string packagePath = Path.Combine(communityFolderPath, PackageFolderName);

            try
            {
                var layoutEntries = new List<(string relativePath, long size)>();

                // --- MFD ---
                string mfdDir = Path.Combine(packagePath, MfdBasePath);
                Directory.CreateDirectory(mfdDir);

                foreach (var (fileName, originalHtml) in mfdHtmls)
                {
                    string htmlPath = Path.Combine(mfdDir, fileName);
                    File.WriteAllText(htmlPath, originalHtml.TrimEnd() + MfdBridgeScriptTag);
                    layoutEntries.Add(($"{MfdBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                }

                string mfdJsDest = Path.Combine(mfdDir, MfdBridgeJsFileName);
                File.Copy(mfdJsSrc, mfdJsDest, overwrite: true);
                layoutEntries.Add(($"{MfdBasePath}/{MfdBridgeJsFileName}", new FileInfo(mfdJsDest).Length));

                // --- EFB ---
                if (efbHtmls.Count > 0 && File.Exists(efbJsSrc))
                {
                    string efbDir = Path.Combine(packagePath, EfbBasePath);
                    Directory.CreateDirectory(efbDir);

                    foreach (var (fileName, originalHtml) in efbHtmls)
                    {
                        string htmlPath = Path.Combine(efbDir, fileName);
                        File.WriteAllText(htmlPath, originalHtml.TrimEnd() + EfbBridgeScriptTag);
                        layoutEntries.Add(($"{EfbBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                    }

                    string efbJsDest = Path.Combine(efbDir, EfbBridgeJsFileName);
                    File.Copy(efbJsSrc, efbJsDest, overwrite: true);
                    layoutEntries.Add(($"{EfbBasePath}/{EfbBridgeJsFileName}", new FileInfo(efbJsDest).Length));
                }

                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                WriteVersionFile(packagePath);

                return ModPackageResult.Updated;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HS787ModPackageManager: Update failed: {ex.Message}");
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
                System.Diagnostics.Debug.WriteLine($"HS787ModPackageManager: Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        // ------------------------------------------------------------------ //
        //  Community folder discovery — delegates to EFBModPackageManager     //
        // ------------------------------------------------------------------ //

        public static string? FindCommunityFolderPath() =>
            EFBModPackageManager.FindCommunityFolderPath();

        public static List<(string SimLabel, string Path)> FindAllCommunityFolders() =>
            EFBModPackageManager.FindAllCommunityFolders();

        // ------------------------------------------------------------------ //
        //  Helpers                                                             //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Searches Community for the HS 787 package and reads its original MFD and EFB HTML files.
        /// Returns (mfdHtmls, efbHtmls) — lists of (fileName, htmlContent) per instrument folder.
        /// We scan every Community subfolder rather than hardcoding the package name so users with
        /// different install paths (store, reseller, dev builds) are all covered.
        /// </summary>
        private static (List<(string, string)> mfd, List<(string, string)> efb) FindOriginalHS787Htmls(string communityFolderPath)
        {
            var mfd = new List<(string, string)>();
            var efb = new List<(string, string)>();

            if (!Directory.Exists(communityFolderPath))
                return (mfd, efb);

            foreach (string packageDir in Directory.GetDirectories(communityFolderPath))
            {
                if (string.Equals(Path.GetFileName(packageDir), PackageFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string mfdDir = Path.Combine(packageDir, MfdBasePath);
                if (!Directory.Exists(mfdDir))
                    continue;

                // Found the HS 787 package.
                foreach (string htmlFile in MfdHtmlFileNames)
                {
                    string htmlPath = Path.Combine(mfdDir, htmlFile);
                    if (File.Exists(htmlPath))
                        mfd.Add((htmlFile, File.ReadAllText(htmlPath)));
                }

                // Also read EFB HTML from the same package.
                string efbDir = Path.Combine(packageDir, EfbBasePath);
                if (Directory.Exists(efbDir))
                {
                    foreach (string htmlFile in EfbHtmlFileNames)
                    {
                        string htmlPath = Path.Combine(efbDir, htmlFile);
                        if (File.Exists(htmlPath))
                            efb.Add((htmlFile, File.ReadAllText(htmlPath)));
                    }
                }

                break; // Stop after first matching package
            }

            return (mfd, efb);
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
    }
}
