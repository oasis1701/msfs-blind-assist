using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

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
        // v18 (FS2024): switched FS2024 from override-package approach to in-place patching of
        // horizonsim-aircraft-787-9. FS2024 VFS does NOT honor community-on-community overrides
        // of files under html_ui/Pages/VCockpit/Instruments/Airliners/ (protected namespace),
        // and Coherent GT 2.x won't execute <script src> inside template-loaded HTML — so we
        // edit the original HTML in place, inject via import-script (matching the original
        // instrument's style), and drop bridge JS next to MFD789.RR.js. FS2020 path unchanged.
        private const int BridgeVersion = 18;
        private const string VersionFileName = "bridge-version.txt";

        private const string PackageFolderName = "zzz-hs787-accessibility";

        private const string MfdBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/MFD";
        private const string EfbBasePath = "html_ui/Pages/VCockpit/Instruments/Airliners/HSB787_9/EFB";

        private const string MfdBridgeJsFileName = "hs787-mfd-bridge.js";
        private const string EfbBridgeJsFileName = "hs787-efb-bridge.js";

        // Both engine variants share the same folder for MFD and EFB.
        private static readonly string[] MfdHtmlFileNames = { "HSB789_MFD.GE.html", "HSB789_MFD.RR.html" };
        private static readonly string[] EfbHtmlFileNames = { "HSB789_EFB.GE.html", "HSB789_EFB.RR.html" };

        // <script src> is processed by Coherent GT (the browser engine inside MSFS) and reliably
        // loads the bridge into the VCockpit page context where DOM access and fetch both work.
        // import-script (MSFS framework-level loading) was tested and broke FS2020: it runs the
        // script in a different JS context from Coherent GT, causing the double-load guard to fire
        // before the <script src> version can initialize — leaving the bridge unable to read the DOM.
        private const string MfdBridgeScriptTag =
            "\n<script src=\"hs787-mfd-bridge.js\"></script>";

        private const string EfbBridgeScriptTag =
            "\n<script src=\"hs787-efb-bridge.js\"></script>";

        // Keep the old name for backwards compat with IsInstalled check
        private const string HtmlBasePath = MfdBasePath;
        private static readonly string[] HtmlFileNames = MfdHtmlFileNames;

        private const string Fs2020ManifestJson = @"{
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

        // FS2024 needs explicit override declarations for html_ui files; without them, FS2024's VFS
        // silently ignores our patched HTML and loads the originals from horizonsim-aircraft-787-9.
        // Field name and metadata (note Asobo's typo: 'overriden' single-d) cribbed from panel-raas,
        // a working FS2024 html_ui override mod confirmed to inject scripts into VCockpit pages.
        private const string Fs2024ManifestJson = @"{
  ""dependencies"": [],
  ""content_type"": ""MISC"",
  ""title"": ""MSFS Blind Assist - HorizonSim 787-9 FMC Bridge"",
  ""manufacturer"": """",
  ""creator"": ""MSFS Blind Assist"",
  ""package_version"": ""0.1.0"",
  ""minimum_game_version"": ""1.6.34"",
  ""minimum_compatibility_version"": ""6.34.0.169"",
  ""export_type"": ""Community"",
  ""builder"": ""Microsoft Flight Simulator 2024"",
  ""package_order_hint"": ""PANEL_PATCH"",
  ""globally_overriden_base_sim_files"": [
    ""\\html_ui\\Pages\\VCockpit\\Instruments\\Airliners\\HSB787_9\\MFD\\HSB789_MFD.GE.html"",
    ""\\html_ui\\Pages\\VCockpit\\Instruments\\Airliners\\HSB787_9\\MFD\\HSB789_MFD.RR.html"",
    ""\\html_ui\\Pages\\VCockpit\\Instruments\\Airliners\\HSB787_9\\EFB\\HSB789_EFB.GE.html"",
    ""\\html_ui\\Pages\\VCockpit\\Instruments\\Airliners\\HSB787_9\\EFB\\HSB789_EFB.RR.html""
  ],
  ""release_notes"": {
    ""neutral"": {
      ""LastUpdate"": """",
      ""OlderHistory"": """"
    }
  },
  ""total_package_size"": ""0000000000000001000""
}";

        private static string GetManifestJson(string communityFolderPath)
        {
            return communityFolderPath.Contains("Limitless", StringComparison.OrdinalIgnoreCase)
                ? Fs2024ManifestJson
                : Fs2020ManifestJson;
        }

        // FS2024 detection: the MS Store package name for FS2024 is Microsoft.Limitless_8wekyb3d8bbwe.
        // Every FS2024 community folder path contains "Limitless"; FS2020 paths contain "FlightSimulator".
        private static bool IsFs2024(string communityFolderPath) =>
            communityFolderPath.Contains("Limitless", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ //
        //  FS2024 in-place patching                                            //
        //                                                                      //
        //  FS2024 silently refuses community-on-community html_ui overrides     //
        //  under the Airliners namespace, AND its Coherent GT 2.x doesn't run   //
        //  <script src> tags inside template-loaded HTML. So instead of a       //
        //  separate override package we modify horizonsim-aircraft-787-9        //
        //  directly: append an import-script line to each instrument HTML and   //
        //  drop bridge JS next to MFD789.RR.js. Backups land beside originals   //
        //  for clean uninstall. FS2020 path is unchanged.                       //
        // ------------------------------------------------------------------ //

        private const string BackupSuffix = ".msfsba_backup";

        // import-script is the MSFS template loader. The original HSB789_MFD.RR.html uses
        // it for every script reference (SimPlane.js, NetBingMap.js, MFD789.RR.js, etc.).
        // Path must be absolute from html_ui root (leading slash), matching original style.
        private const string MfdBridgeImportScriptTag =
            "\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/Airliners/HSB787_9/MFD/hs787-mfd-bridge.js\"></script>";

        private const string EfbBridgeImportScriptTag =
            "\n<script type=\"text/html\" import-script=\"/Pages/VCockpit/Instruments/Airliners/HSB787_9/EFB/hs787-efb-bridge.js\"></script>";

        // Unique marker string that lives inside both bridge import-script tags. Cheap detection.
        private const string PatchMarker = "hs787-mfd-bridge.js";

        /// <summary>
        /// Locates the horizonsim-aircraft-787-9 package within a community folder by looking for
        /// the canonical MFD subpath. Returns the package root directory, or null if not found.
        /// </summary>
        private static string? FindHorizonsimPackagePath(string communityFolderPath)
        {
            if (!Directory.Exists(communityFolderPath))
                return null;

            foreach (string packageDir in Directory.GetDirectories(communityFolderPath))
            {
                if (string.Equals(Path.GetFileName(packageDir), PackageFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Directory.Exists(Path.Combine(packageDir, MfdBasePath)))
                    return packageDir;
            }
            return null;
        }

        private static bool IsInstalledFs2024(string communityFolderPath)
        {
            string? horizonsim = FindHorizonsimPackagePath(communityFolderPath);
            if (horizonsim == null)
                return false;
            // Patch is detectable on any of the four HTML files — checking one suffices.
            string mfdRR = Path.Combine(horizonsim, MfdBasePath, "HSB789_MFD.RR.html");
            return File.Exists(mfdRR) && File.ReadAllText(mfdRR).Contains(PatchMarker);
        }

        private static ModPackageResult InstallFs2024(string communityFolderPath, string resourcesDir, bool isUpdate)
        {
            if (!Directory.Exists(communityFolderPath))
                return ModPackageResult.CommunityFolderNotFound;

            string? horizonsim = FindHorizonsimPackagePath(communityFolderPath);
            if (horizonsim == null)
                return ModPackageResult.HS787PackageNotFound;

            string mfdJsSrc = Path.Combine(resourcesDir, MfdBridgeJsFileName);
            string efbJsSrc = Path.Combine(resourcesDir, EfbBridgeJsFileName);
            if (!File.Exists(mfdJsSrc) || !File.Exists(efbJsSrc))
                return ModPackageResult.BridgeJsSourceNotFound;

            // Idempotent: if Install is called against an already-patched install, bail out.
            // Update path (re-patch) skips this guard so a bumped BridgeVersion can re-apply.
            if (!isUpdate && IsInstalledFs2024(communityFolderPath))
                return ModPackageResult.AlreadyInstalled;

            try
            {
                string mfdDir = Path.Combine(horizonsim, MfdBasePath);
                string efbDir = Path.Combine(horizonsim, EfbBasePath);

                // --- MFD HTMLs: backup originals, append import-script ---
                foreach (string htmlFile in MfdHtmlFileNames)
                {
                    string htmlPath = Path.Combine(mfdDir, htmlFile);
                    if (!File.Exists(htmlPath)) continue;
                    PatchHtmlInPlace(htmlPath, MfdBridgeImportScriptTag);
                }

                // --- EFB HTMLs: same treatment ---
                if (Directory.Exists(efbDir))
                {
                    foreach (string htmlFile in EfbHtmlFileNames)
                    {
                        string htmlPath = Path.Combine(efbDir, htmlFile);
                        if (!File.Exists(htmlPath)) continue;
                        PatchHtmlInPlace(htmlPath, EfbBridgeImportScriptTag);
                    }
                }

                // --- Drop bridge JS files next to the original instrument JS ---
                File.Copy(mfdJsSrc, Path.Combine(mfdDir, MfdBridgeJsFileName), overwrite: true);
                if (Directory.Exists(efbDir))
                    File.Copy(efbJsSrc, Path.Combine(efbDir, EfbBridgeJsFileName), overwrite: true);

                // --- Update horizonsim's layout.json so MSFS sees the new JS files and the
                //     updated HTML sizes. MSFS validates these. ---
                UpdateHorizonsimLayoutJson(horizonsim);

                // --- Write our version marker into horizonsim (so we can detect installed-version
                //     across app restarts without touching horizonsim's manifest). ---
                File.WriteAllText(Path.Combine(horizonsim, "msfsba-bridge-version.txt"), BridgeVersion.ToString());

                // --- Clean up the now-useless override package if it was left behind from older
                //     versions of MSFSBA. On FS2024 it does nothing functional. ---
                string overridePackage = Path.Combine(communityFolderPath, PackageFolderName);
                if (Directory.Exists(overridePackage))
                {
                    try { Directory.Delete(overridePackage, recursive: true); } catch { }
                }

                return isUpdate ? ModPackageResult.Updated : ModPackageResult.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HS787ModPackageManager: FS2024 Install failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        /// <summary>
        /// Patches one instrument HTML: back up the original (only if no backup exists yet so we
        /// preserve the first known-good state), then strip any prior patch marker and append
        /// the new import-script line. Idempotent: re-running yields the same final content.
        /// </summary>
        private static void PatchHtmlInPlace(string htmlPath, string importScriptTag)
        {
            string backupPath = htmlPath + BackupSuffix;
            if (!File.Exists(backupPath))
                File.Copy(htmlPath, backupPath, overwrite: false);

            // Always re-derive from the backup so re-patches are clean (no stacking of script tags
            // if BridgeVersion bumps add or remove text).
            string original = File.ReadAllText(backupPath);
            File.WriteAllText(htmlPath, original.TrimEnd() + importScriptTag + "\n");
        }

        /// <summary>
        /// Adds the two bridge JS files to horizonsim's layout.json (idempotent: replaces existing
        /// entries) and updates the four patched HTML files' sizes. Backs up layout.json once.
        /// </summary>
        private static void UpdateHorizonsimLayoutJson(string horizonsimPackagePath)
        {
            string layoutPath = Path.Combine(horizonsimPackagePath, "layout.json");
            if (!File.Exists(layoutPath))
                return; // No layout.json — let MSFS deal with it; better than crashing the install.

            string backupPath = layoutPath + BackupSuffix;
            if (!File.Exists(backupPath))
                File.Copy(layoutPath, backupPath, overwrite: false);

            var layout = JObject.Parse(File.ReadAllText(layoutPath));
            var content = (JArray?)layout["content"];
            if (content == null)
                return;

            string mfdRelativeBase = MfdBasePath.Replace("/", "\\");
            string efbRelativeBase = EfbBasePath.Replace("/", "\\");

            string mfdJsRel = $"{mfdRelativeBase}\\{MfdBridgeJsFileName}";
            string efbJsRel = $"{efbRelativeBase}\\{EfbBridgeJsFileName}";

            // Paths in layout.json can be either forward- or backslash. Match horizonsim's style.
            // Probe ALL entries (not just the first — top-level files like locPaks have no slash
            // and would falsely default us to backslash); pick the convention seen in the first
            // entry that actually has any slash.
            bool useForwardSlash = true; // forward is the safe default; horizonsim's layout uses it
            foreach (var probeEntry in content)
            {
                string probe = probeEntry["path"]?.ToString() ?? "";
                if (probe.Contains('/')) { useForwardSlash = true; break; }
                if (probe.Contains('\\')) { useForwardSlash = false; break; }
            }

            string Normalize(string p) => useForwardSlash ? p.Replace('\\', '/') : p.Replace('/', '\\');
            // Slash-agnostic canonical form for matching existing entries (regardless of how the
            // package authored them). Lowercase + forward slash, both sides converted.
            string Canonical(string p) => p.Replace('\\', '/').ToLowerInvariant();

            // Update sizes for patched HTMLs.
            void UpdateHtmlSize(string relativePath)
            {
                string absPath = Path.Combine(horizonsimPackagePath, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(absPath)) return;
                long newSize = new FileInfo(absPath).Length;
                string targetCanonical = Canonical(relativePath);
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

            foreach (string h in MfdHtmlFileNames) UpdateHtmlSize($"{mfdRelativeBase}\\{h}");
            foreach (string h in EfbHtmlFileNames) UpdateHtmlSize($"{efbRelativeBase}\\{h}");

            // Add (or refresh) bridge JS entries.
            void UpsertJsEntry(string relativePath)
            {
                string absPath = Path.Combine(horizonsimPackagePath, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(absPath)) return;
                long size = new FileInfo(absPath).Length;
                string normalized = Normalize(relativePath);
                string targetCanonical = Canonical(normalized);

                // Strip any previous entry pointing at the same file (slash-agnostic) so we don't
                // duplicate from prior installs that may have used the other convention.
                for (int i = content.Count - 1; i >= 0; i--)
                {
                    string? p = content[i]["path"]?.ToString();
                    if (p != null && Canonical(p) == targetCanonical)
                        content.RemoveAt(i);
                }

                var newEntry = new JObject
                {
                    ["path"] = normalized,
                    ["size"] = size,
                    ["date"] = 133888888888888888L
                };
                content.Add(newEntry);
            }

            UpsertJsEntry(mfdJsRel);
            UpsertJsEntry(efbJsRel);

            File.WriteAllText(layoutPath, layout.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        private static ModPackageResult RemoveFs2024(string communityFolderPath)
        {
            string? horizonsim = FindHorizonsimPackagePath(communityFolderPath);
            if (horizonsim == null)
                return ModPackageResult.CommunityFolderNotFound;

            try
            {
                // Restore HTMLs from backups.
                void RestoreOne(string htmlPath)
                {
                    string backup = htmlPath + BackupSuffix;
                    if (File.Exists(backup))
                    {
                        File.Copy(backup, htmlPath, overwrite: true);
                        File.Delete(backup);
                    }
                }

                string mfdDir = Path.Combine(horizonsim, MfdBasePath);
                foreach (string h in MfdHtmlFileNames) RestoreOne(Path.Combine(mfdDir, h));
                string efbDir = Path.Combine(horizonsim, EfbBasePath);
                if (Directory.Exists(efbDir))
                    foreach (string h in EfbHtmlFileNames) RestoreOne(Path.Combine(efbDir, h));

                // Delete bridge JS we dropped in.
                string mfdJs = Path.Combine(mfdDir, MfdBridgeJsFileName);
                if (File.Exists(mfdJs)) File.Delete(mfdJs);
                string efbJs = Path.Combine(efbDir, EfbBridgeJsFileName);
                if (File.Exists(efbJs)) File.Delete(efbJs);

                // Restore layout.json from backup if present.
                string layoutPath = Path.Combine(horizonsim, "layout.json");
                string layoutBackup = layoutPath + BackupSuffix;
                if (File.Exists(layoutBackup))
                {
                    File.Copy(layoutBackup, layoutPath, overwrite: true);
                    File.Delete(layoutBackup);
                }

                // Clean our version marker.
                string versionFile = Path.Combine(horizonsim, "msfsba-bridge-version.txt");
                if (File.Exists(versionFile)) File.Delete(versionFile);

                return ModPackageResult.Removed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HS787ModPackageManager: FS2024 Remove failed: {ex.Message}");
                return ModPackageResult.InstallFailed;
            }
        }

        private static int GetInstalledVersionFs2024(string communityFolderPath)
        {
            string? horizonsim = FindHorizonsimPackagePath(communityFolderPath);
            if (horizonsim == null) return 0;
            string versionFile = Path.Combine(horizonsim, "msfsba-bridge-version.txt");
            if (File.Exists(versionFile) && int.TryParse(File.ReadAllText(versionFile).Trim(), out int v))
                return v;
            return IsInstalledFs2024(communityFolderPath) ? 1 : 0;
        }

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        public static bool IsInstalled(string communityFolderPath)
        {
            if (IsFs2024(communityFolderPath))
                return IsInstalledFs2024(communityFolderPath);

            string mfdDir = Path.Combine(communityFolderPath, PackageFolderName, HtmlBasePath);
            // Enough to check one of the HTML files — if the folder and one file exist, the package is installed.
            return File.Exists(Path.Combine(mfdDir, HtmlFileNames[0])) ||
                   File.Exists(Path.Combine(mfdDir, HtmlFileNames[1]));
        }

        public static int GetInstalledVersion(string communityFolderPath)
        {
            if (IsFs2024(communityFolderPath))
                return GetInstalledVersionFs2024(communityFolderPath);

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
            if (IsFs2024(communityFolderPath))
                return InstallFs2024(communityFolderPath, resourcesDir, isUpdate: false);

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
                    string modifiedHtml = originalHtml.Contains(MfdBridgeJsFileName)
                        ? originalHtml  // Already patched — don't double-patch
                        : originalHtml.TrimEnd() + MfdBridgeScriptTag;
                    File.WriteAllText(htmlPath, modifiedHtml);
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
                        string modifiedHtml = originalHtml.Contains(EfbBridgeJsFileName)
                            ? originalHtml  // Already patched — don't double-patch
                            : originalHtml.TrimEnd() + EfbBridgeScriptTag;
                        File.WriteAllText(htmlPath, modifiedHtml);
                        layoutEntries.Add(($"{EfbBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                    }

                    string efbJsDest = Path.Combine(efbDir, EfbBridgeJsFileName);
                    File.Copy(efbJsSrc, efbJsDest, overwrite: true);
                    layoutEntries.Add(($"{EfbBasePath}/{EfbBridgeJsFileName}", new FileInfo(efbJsDest).Length));
                }

                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), GetManifestJson(communityFolderPath));
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
            if (IsFs2024(communityFolderPath))
            {
                // On FS2024, "update" means re-run InstallFs2024 (which is idempotent on its backups
                // and is a no-op if already at-version, since it'll have just re-applied the same
                // patches). Caller controls cadence via GetInstalledVersion check below.
                if (GetInstalledVersion(communityFolderPath) >= BridgeVersion)
                    return ModPackageResult.AlreadyUpToDate;
                // If the FS2020-style override package is somehow installed on FS2024 (legacy v17),
                // IsInstalledFs2024 returns false and we'd otherwise refuse to update. Force the
                // in-place install path; it will also clean up the obsolete override package.
                return InstallFs2024(communityFolderPath, resourcesDir, isUpdate: true);
            }

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
                    string modifiedHtml = originalHtml.Contains(MfdBridgeJsFileName)
                        ? originalHtml  // Already patched — don't double-patch
                        : originalHtml.TrimEnd() + MfdBridgeScriptTag;
                    File.WriteAllText(htmlPath, modifiedHtml);
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
                        string modifiedHtml = originalHtml.Contains(EfbBridgeJsFileName)
                            ? originalHtml  // Already patched — don't double-patch
                            : originalHtml.TrimEnd() + EfbBridgeScriptTag;
                        File.WriteAllText(htmlPath, modifiedHtml);
                        layoutEntries.Add(($"{EfbBasePath}/{fileName}", new FileInfo(htmlPath).Length));
                    }

                    string efbJsDest = Path.Combine(efbDir, EfbBridgeJsFileName);
                    File.Copy(efbJsSrc, efbJsDest, overwrite: true);
                    layoutEntries.Add(($"{EfbBasePath}/{EfbBridgeJsFileName}", new FileInfo(efbJsDest).Length));
                }

                File.WriteAllText(Path.Combine(packagePath, "layout.json"), GenerateLayoutJson(layoutEntries));
                File.WriteAllText(Path.Combine(packagePath, "manifest.json"), GetManifestJson(communityFolderPath));
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
            if (IsFs2024(communityFolderPath))
            {
                // Also wipe any legacy zzz-hs787-accessibility package left over from previous versions.
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
