using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FBWBA.Database
{
    /// <summary>
    /// Builds airport databases using the navdatareader command-line tool.
    /// Supports both FS2020 (MSFS) and FS2024 (MSFS24) simulator databases.
    /// </summary>
    public class NavdataReaderBuilder
    {
        /// <summary>
        /// Event fired when progress is updated during database building
        /// </summary>
        public event EventHandler<BuildProgressEventArgs> ProgressUpdated;

        /// <summary>
        /// Event fired when the build process completes (success or failure)
        /// </summary>
        public event EventHandler<BuildCompletedEventArgs> BuildCompleted;

        private Process _process;
        private bool _isCancelled;

        /// <summary>
        /// Builds a database for the specified simulator version
        /// </summary>
        /// <param name="simulatorVersion">FS2020 or FS2024</param>
        /// <param name="outputPath">Full path where the database should be created</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if build succeeded, false otherwise</returns>
        public async Task<bool> BuildDatabaseAsync(string simulatorVersion, string outputPath, CancellationToken cancellationToken = default)
        {
            _isCancelled = false;

            try
            {
                // Validate simulator version
                string navdataSimFlag = GetNavdataReaderSimulatorFlag(simulatorVersion);
                if (navdataSimFlag == null)
                {
                    OnBuildCompleted(false, $"Invalid simulator version: {simulatorVersion}");
                    return false;
                }

                // Check simulator running state requirements
                bool isSimulatorRunning = IsSimulatorRunning(simulatorVersion);

                if (simulatorVersion == "FS2024" && !isSimulatorRunning)
                {
                    OnBuildCompleted(false,
                        "Flight Simulator 2024 is not running.\n\n" +
                        "FS2024 database building requires the simulator to be running and loaded to the main menu.\n" +
                        "Navdatareader uses SimConnect to retrieve scenery data from the running simulator.");
                    return false;
                }

                if (simulatorVersion == "FS2020" && isSimulatorRunning)
                {
                    OnBuildCompleted(false,
                        "Flight Simulator 2020 is currently running.\n\n" +
                        "FS2020 database building requires the simulator to be closed.\n" +
                        "Navdatareader reads scenery files directly from disk.\n\n" +
                        "Please close the simulator and try again.");
                    return false;
                }

                // Get navdatareader.exe path
                string navdataReaderPath = GetNavdataReaderPath();
                if (!File.Exists(navdataReaderPath))
                {
                    OnBuildCompleted(false, $"navdatareader.exe not found at: {navdataReaderPath}");
                    return false;
                }

                // Ensure output directory exists
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                // Delete existing database file if it exists
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                OnProgressUpdated(0, $"Starting {simulatorVersion} database build...");

                // Build command line arguments
                string arguments = $"-f {navdataSimFlag} -o \"{outputPath}\"";

                // For MSFS/MSFS24, add base path parameter if we can detect it
                // This ensures navdatareader looks in the correct location for scenery
                if (simulatorVersion == "FS2024" || simulatorVersion == "FS2020")
                {
                    string basePath = GetMSFSBasePath(simulatorVersion);
                    if (!string.IsNullOrEmpty(basePath))
                    {
                        arguments += $" -b \"{basePath}\"";
                        Debug.WriteLine($"[NavdataReaderBuilder] Added base path parameter: -b \"{basePath}\"");
                    }
                    else
                    {
                        Debug.WriteLine($"[NavdataReaderBuilder] Warning: Could not detect {simulatorVersion} base path, relying on auto-detection");
                    }
                }

                // Configure process
                var startInfo = new ProcessStartInfo
                {
                    FileName = navdataReaderPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(navdataReaderPath)
                };

                _process = new Process { StartInfo = startInfo };

                // Capture output for progress reporting
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                bool hasSimConnectError = false;

                _process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        ParseProgressOutput(e.Data);

                        // Detect SimConnect connection errors
                        if (e.Data.IndexOf("Dir is empty", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("SimConnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("Cannot connect", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasSimConnectError = true;
                        }
                    }
                };

                _process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        Debug.WriteLine($"[NavdataReader Error] {e.Data}");

                        // Detect SimConnect errors in stderr
                        if (e.Data.IndexOf("SimConnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            e.Data.IndexOf("Dir is empty", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hasSimConnectError = true;
                        }
                    }
                };

                // Start process
                Debug.WriteLine($"[NavdataReaderBuilder] [{DateTime.Now:HH:mm:ss.fff}] Starting navdatareader process...");
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Debug.WriteLine($"[NavdataReaderBuilder] [{DateTime.Now:HH:mm:ss.fff}] Navdatareader process started, monitoring output...");

                // Wait for completion with cancellation support
                while (!_process.HasExited)
                {
                    if (cancellationToken.IsCancellationRequested || _isCancelled)
                    {
                        _process.Kill();
                        OnBuildCompleted(false, "Build cancelled by user");
                        return false;
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // Check exit code
                int exitCode = _process.ExitCode;

                if (exitCode == 0 && File.Exists(outputPath))
                {
                    OnProgressUpdated(100, "Database build completed successfully");
                    OnBuildCompleted(true, "Database built successfully");
                    return true;
                }
                else
                {
                    // Provide helpful error messages based on detected issues
                    string errorMessage;

                    if (hasSimConnectError && simulatorVersion == "FS2024")
                    {
                        errorMessage =
                            "Cannot connect to Flight Simulator 2024 via SimConnect.\n\n" +
                            "Possible causes:\n" +
                            "• Simulator is not running or not fully loaded to main menu\n" +
                            "• SimConnect service is not responding\n" +
                            "• Firewall blocking connection\n\n" +
                            "Please ensure FS2024 is running and try again.";
                    }
                    else if (hasSimConnectError && simulatorVersion == "FS2020")
                    {
                        errorMessage =
                            "Cannot access Flight Simulator 2020 scenery files.\n\n" +
                            "Possible causes:\n" +
                            "• Simulator is running (it should be closed for FS2020)\n" +
                            "• Scenery files are inaccessible\n" +
                            "• Insufficient permissions\n\n" +
                            "Please close FS2020 and try again.";
                    }
                    else if (errorBuilder.Length > 0)
                    {
                        errorMessage = errorBuilder.ToString();
                    }
                    else
                    {
                        errorMessage = $"navdatareader exited with code {exitCode}";
                    }

                    OnBuildCompleted(false, $"Build failed:\n\n{errorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnBuildCompleted(false, $"Build error: {ex.Message}");
                return false;
            }
            finally
            {
                _process?.Dispose();
                _process = null;
            }
        }

        /// <summary>
        /// Cancels the current build operation
        /// </summary>
        public void CancelBuild()
        {
            _isCancelled = true;

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing navdatareader process: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the simulator flag for navdatareader command line
        /// </summary>
        private string GetNavdataReaderSimulatorFlag(string simulatorVersion)
        {
            switch (simulatorVersion?.ToUpper())
            {
                case "FS2020":
                    return "MSFS";
                case "FS2024":
                    return "MSFS24";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets the path to navdatareader.exe
        /// </summary>
        private string GetNavdataReaderPath()
        {
            // Look in application directory
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string navdataReaderPath = Path.Combine(appDir, "navdatareader", "navdatareader.exe");

            if (File.Exists(navdataReaderPath))
                return navdataReaderPath;

            // Look in parent directory (development environment)
            string parentNavdataPath = Path.Combine(appDir, "..", "Navdatareader-win-1.2.3", "navdatareader.exe");
            if (File.Exists(parentNavdataPath))
                return Path.GetFullPath(parentNavdataPath);

            return navdataReaderPath; // Return default path even if not found (error will be handled by caller)
        }

        /// <summary>
        /// Parses output lines from navdatareader to extract progress information
        /// </summary>
        private void ParseProgressOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            try
            {
                // Log all output for debugging
                Debug.WriteLine($"[NavdataReader] {line}");

                // Look for common progress indicators (case-insensitive using IndexOf)
                if (line.IndexOf("Reading", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OnProgressUpdated(25, "Reading scenery files...");
                }
                else if (line.IndexOf("Processing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OnProgressUpdated(50, "Processing airport data...");
                }
                else if (line.IndexOf("Creating", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("Writing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OnProgressUpdated(75, "Writing database...");
                }
                else if (line.IndexOf("Done", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("Finished", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    OnProgressUpdated(95, "Finalizing database...");
                }
                else if (line.IndexOf("airports", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         Regex.IsMatch(line, @"\d+"))
                {
                    // Extract airport count if available
                    var match = Regex.Match(line, @"(\d+)\s*airports?");
                    if (match.Success)
                    {
                        OnProgressUpdated(90, $"Processed {match.Groups[1].Value} airports");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Fires the ProgressUpdated event
        /// </summary>
        private void OnProgressUpdated(int percentage, string status)
        {
            ProgressUpdated?.Invoke(this, new BuildProgressEventArgs
            {
                PercentComplete = percentage,
                StatusMessage = status
            });
        }

        /// <summary>
        /// Fires the BuildCompleted event
        /// </summary>
        private void OnBuildCompleted(bool success, string message)
        {
            BuildCompleted?.Invoke(this, new BuildCompletedEventArgs
            {
                Success = success,
                Message = message
            });
        }

        /// <summary>
        /// Checks if a navdatareader-generated database exists for the specified simulator
        /// </summary>
        /// <param name="simulatorVersion">FS2020 or FS2024</param>
        /// <returns>True if database file exists</returns>
        public static bool DatabaseExists(string simulatorVersion)
        {
            string databasePath = GetDefaultDatabasePath(simulatorVersion);
            return File.Exists(databasePath);
        }

        /// <summary>
        /// Gets the default database path for a simulator version
        /// </summary>
        /// <param name="simulatorVersion">FS2020 or FS2024</param>
        /// <returns>Full path to the database file</returns>
        public static string GetDefaultDatabasePath(string simulatorVersion)
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string databaseFolder = Path.Combine(appDataPath, "FBWBA", "databases");

            string filename = simulatorVersion?.ToUpper() == "FS2024"
                ? "fs2024.sqlite"
                : "fs2020.sqlite";

            return Path.Combine(databaseFolder, filename);
        }

        /// <summary>
        /// Checks if the specified simulator is currently running
        /// </summary>
        /// <param name="simulatorVersion">FS2020 or FS2024</param>
        /// <returns>True if simulator process is found</returns>
        private bool IsSimulatorRunning(string simulatorVersion)
        {
            try
            {
                string processName = simulatorVersion == "FS2024"
                    ? "FlightSimulator2024"
                    : "FlightSimulator";

                var processes = Process.GetProcessesByName(processName);
                return processes != null && processes.Length > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking if simulator is running: {ex.Message}");
                return false; // Assume not running if we can't check
            }
        }

        /// <summary>
        /// Gets the base path for MSFS/MSFS24 from UserCfg.opt file
        /// </summary>
        /// <param name="simulatorVersion">FS2020 or FS2024</param>
        /// <returns>Base path if found, null otherwise</returns>
        private string GetMSFSBasePath(string simulatorVersion)
        {
            try
            {
                string configFileName = simulatorVersion == "FS2024"
                    ? "Microsoft Flight Simulator 2024"
                    : "Microsoft Flight Simulator";

                // Check AppData\Roaming location first
                string roamingPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    configFileName,
                    "UserCfg.opt");

                string basePath = TryParseUserCfgForBasePath(roamingPath);
                if (basePath != null)
                {
                    Debug.WriteLine($"Found {simulatorVersion} base path from UserCfg.opt: {basePath}");
                    return basePath;
                }

                // For FS2020, also check LocalCache location (Store version)
                if (simulatorVersion == "FS2020")
                {
                    string localCachePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalCache",
                        "UserCfg.opt");

                    basePath = TryParseUserCfgForBasePath(localCachePath);
                    if (basePath != null)
                    {
                        Debug.WriteLine($"Found {simulatorVersion} base path from Store UserCfg.opt: {basePath}");
                        return basePath;
                    }
                }

                Debug.WriteLine($"Could not find base path for {simulatorVersion}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting MSFS base path: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses UserCfg.opt file to extract InstalledPackagesPath
        /// </summary>
        private string TryParseUserCfgForBasePath(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"UserCfg.opt not found at: {configPath}");
                    return null;
                }

                string[] lines = File.ReadAllLines(configPath);
                foreach (string line in lines)
                {
                    // Look for InstalledPackagesPath setting
                    if (line.IndexOf("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Format: InstalledPackagesPath "F:\msfs2024"
                        int firstQuote = line.IndexOf('"');
                        int lastQuote = line.LastIndexOf('"');

                        if (firstQuote >= 0 && lastQuote > firstQuote)
                        {
                            string path = line.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                            // Validate the path exists
                            if (Directory.Exists(path))
                            {
                                return path;
                            }
                            else
                            {
                                Debug.WriteLine($"InstalledPackagesPath found but directory doesn't exist: {path}");
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing UserCfg.opt: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Event arguments for build progress updates
    /// </summary>
    public class BuildProgressEventArgs : EventArgs
    {
        public int PercentComplete { get; set; }
        public string StatusMessage { get; set; }
    }

    /// <summary>
    /// Event arguments for build completion
    /// </summary>
    public class BuildCompletedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
