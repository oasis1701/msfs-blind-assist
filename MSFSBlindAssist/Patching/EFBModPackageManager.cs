using System.IO;

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
        private const string PackageFolderName = "zzz-pmdg-efb-accessibility";

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
