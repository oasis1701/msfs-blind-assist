using System.IO;

namespace MSFSBlindAssist.Patching
{
    public enum ModPackageResult
    {
        Removed,
        CommunityFolderNotFound,
        InstallFailed
    }

    /// <summary>
    /// Locates the MSFS Community folder(s) and removes a named package.
    /// EFB accessibility moved to the Coherent debugger transport, so there is no longer any
    /// install / HTML-patch path here — the only remaining consumer is <see cref="LegacyEfbBridgeCleanup"/>,
    /// which deletes the retired EFB-bridge injection packages from every sim's Community folder.
    /// </summary>
    public static class EFBModPackageManager
    {
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

        // Delete a named override package from the Community folder (idempotent; no-op if absent).
        public static ModPackageResult RemoveNamed(string communityFolderPath, string packageFolderName)
        {
            string packagePath = Path.Combine(communityFolderPath, packageFolderName);
            if (!Directory.Exists(packagePath)) return ModPackageResult.CommunityFolderNotFound;
            try { Directory.Delete(packagePath, recursive: true); return ModPackageResult.Removed; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RemoveNamed failed: {ex.Message}"); return ModPackageResult.InstallFailed; }
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
