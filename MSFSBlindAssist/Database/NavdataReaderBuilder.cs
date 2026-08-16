using System.Threading;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Database;
/// <summary>
/// Builds airport databases using the navdatareader command-line tool.
/// Supports both FS2020 (MSFS) and FS2024 (MSFS24) simulator databases.
/// </summary>
public class NavdataReaderBuilder
{
    /// <summary>
    /// Event fired when progress is updated during database building
    /// </summary>
    public event EventHandler<BuildProgressEventArgs>? ProgressUpdated;

    /// <summary>
    /// Event fired when the build process completes (success or failure)
    /// </summary>
    public event EventHandler<BuildCompletedEventArgs>? BuildCompleted;

    private Process? _process;
    private bool _isCancelled;
    private readonly NavdataReaderProgressMapper _progressMapper = new();

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
            string? navdataSimFlag = GetNavdataReaderSimulatorFlag(simulatorVersion);
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

            // The shipped navdatareader config. It REPLACES navdatareader's built-in config
            // rather than merging with it, so a missing or truncated copy does not mean
            // "fall back to defaults" — it means a build with every filter disabled, which
            // is worse than not building at all. Refuse here, BEFORE the existing database
            // is touched, so a broken deploy costs the pilot nothing.
            string configPath = GetNavdataReaderConfigPath();
            if (!NavdataReaderConfig.IsUsableFile(configPath))
            {
                OnBuildCompleted(false,
                    "The navdatareader configuration that ships with MSFS Blind Assist is missing or damaged.\n\n" +
                    $"Expected at: {configPath}\n\n" +
                    "Without it the database would be built with add-on airport parking replaced by " +
                    "default parking, so the build has been stopped and your existing database left " +
                    "untouched.\n\n" +
                    "Reinstalling or updating MSFS Blind Assist should restore the file.");
                return false;
            }
            Log.Debug("Database", $"Using navdatareader config: {configPath}");

            // Ensure output directory exists
            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Prove the existing database is not locked WITHOUT deleting it. The old code
            // deleted here, before navdatareader had produced anything, so every later
            // failure — a sim not yet at the main menu, a crash, a cancel — left the pilot
            // with no navdata at all.
            if (File.Exists(outputPath))
            {
                try
                {
                    using var probe = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ioEx)
                {
                    OnBuildCompleted(false,
                        $"Cannot replace the existing database file - it is currently in use.\n\n" +
                        $"Error: {ioEx.Message}\n\n" +
                        $"This usually means MSFS Blind Assist or another application has the database file open.\n" +
                        $"Please try closing and reopening MSFS Blind Assist, or close any other applications that might be accessing the database.");
                    return false;
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    OnBuildCompleted(false,
                        $"Cannot replace the existing database file - permission denied.\n\n" +
                        $"Error: {uaEx.Message}\n\n" +
                        $"Please check file permissions or run as administrator.");
                    return false;
                }
            }

            OnProgressUpdated(0, $"Starting {simulatorVersion} database build...");

            // Build to a temp file and rename on success, so a failed or cancelled build
            // never destroys the database the pilot already has.
            string buildPath = outputPath + ".building";
            TryDeleteQuietly(buildPath);

            string? basePath = null;
            if (simulatorVersion == "FS2024" || simulatorVersion == "FS2020")
            {
                basePath = GetMSFSBasePath(simulatorVersion);
                if (string.IsNullOrEmpty(basePath))
                    Log.Debug("Database", $"Could not detect {simulatorVersion} base path, relying on auto-detection");
            }

            string arguments = NavdataReaderArguments.Build(navdataSimFlag, buildPath, configPath, basePath);
            Log.Debug("Database", $"navdatareader arguments: {arguments}");

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
            var errorBuilder = new StringBuilder();
            bool hasSimConnectError = false;

            _process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ParseProgressOutput(e.Data);
                }
            };

            _process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    Log.Debug("Database", $"{e.Data}");

                    // Only a genuine connection failure, not the word "SimConnect" appearing
                    // in routine diagnostic output.
                    if (e.Data.IndexOf("Cannot connect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.Data.IndexOf("SimConnect_Open", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        e.Data.IndexOf("Dir is empty", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasSimConnectError = true;
                    }
                }
            };

            // Start process
            Log.Debug("Database", "Starting navdatareader process...");
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Log.Debug("Database", "Navdatareader process started, monitoring output...");

            try
            {
                await _process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess();
                TryDeleteQuietly(buildPath);
                OnBuildCompleted(false, "Build cancelled by user");
                return false;
            }

            if (_isCancelled)
            {
                TryKillProcess();
                TryDeleteQuietly(buildPath);
                OnBuildCompleted(false, "Build cancelled by user");
                return false;
            }

            // Barrier: HasExited/WaitForExitAsync return as soon as the process object dies,
            // but the redirected stream readers may still be delivering. The argument-less
            // WaitForExit is the documented flush point, and without it the tail of stderr —
            // the actual reason a build failed — can be missing from errorBuilder below.
            _process.WaitForExit();

            int exitCode = _process.ExitCode;

            if (exitCode == 0 && File.Exists(buildPath))
            {
                try
                {
                    File.Move(buildPath, outputPath, overwrite: true);
                }
                catch (Exception moveEx)
                {
                    TryDeleteQuietly(buildPath);
                    OnBuildCompleted(false,
                        $"The database was built but could not replace the existing file.\n\n" +
                        $"Error: {moveEx.Message}\n\n" +
                        $"Your previous database has been left in place.");
                    return false;
                }

                OnProgressUpdated(100, "Database build completed successfully");
                // TODO(Task 7): VerifyExclusionApplied(simulatorVersion, outputPath) replaces
                // this literal once it exists — see task-6-report.md for the handoff note.
                OnBuildCompleted(true, "Database built successfully");
                return true;
            }
            else
            {
                TryDeleteQuietly(buildPath);

                // navdatareader's own message first. The previous code decided between two
                // canned SimConnect texts on a substring match for "SimConnect", which its
                // routine options dump always contains — so every failure, including an
                // FS2020 disk build that uses no SimConnect at all, was reported as a
                // connection problem and the real error was never shown.
                string errorMessage = errorBuilder.Length > 0
                    ? errorBuilder.ToString().Trim()
                    : $"navdatareader exited with code {exitCode}";

                if (hasSimConnectError)
                {
                    errorMessage += simulatorVersion == "FS2024"
                        ? "\n\nIf this looks like a connection problem: FS2024 must be running and loaded " +
                          "to the main menu, because navdatareader reads its scenery data over SimConnect."
                        : "\n\nIf this looks like a scenery access problem: FS2020 must be closed, because " +
                          "navdatareader reads its scenery files directly from disk.";
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
                Log.Debug("Database", $"Error killing navdatareader process: {ex.Message}");
            }
        }
    }

    private void TryKillProcess()
    {
        try
        {
            if (_process != null && !_process.HasExited)
                _process.Kill();
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error killing navdatareader process: {ex.Message}");
        }
    }

    private static void TryDeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Could not delete {path}: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the simulator flag for navdatareader command line
    /// </summary>
    private string? GetNavdataReaderSimulatorFlag(string simulatorVersion)
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
        string parentNavdataPath = Path.Combine(appDir, "..", "Navdatareader-win-1.2.4", "navdatareader.exe");
        if (File.Exists(parentNavdataPath))
            return Path.GetFullPath(parentNavdataPath);

        return navdataReaderPath; // Return default path even if not found (error will be handled by caller)
    }

    /// <summary>
    /// Gets the path to the navdatareader config shipped in Resources.
    /// </summary>
    private static string GetNavdataReaderConfigPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "navdatareader.cfg");
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
            var update = _progressMapper.Map(line);
            if (update != null)
                OnProgressUpdated(update.Percent, update.Status, update.Details);
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error parsing progress: {ex.Message}");
        }
    }

    /// <summary>
    /// Fires the ProgressUpdated event
    /// </summary>
    private void OnProgressUpdated(int percentage, string? status, string? details = null)
    {
        ProgressUpdated?.Invoke(this, new BuildProgressEventArgs
        {
            PercentComplete = percentage,
            StatusMessage = status,
            DetailMessage = details
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
    /// at *either* the canonical (MSFSBlindAssist) or legacy (FBWBA) location.
    /// </summary>
    /// <param name="simulatorVersion">FS2020 or FS2024</param>
    /// <returns>True if database file exists</returns>
    public static bool DatabaseExists(string simulatorVersion)
    {
        return DatabasePathResolver.ExistsAnywhere(simulatorVersion);
    }

    /// <summary>
    /// Gets the default database path for a simulator version.
    ///
    /// Returns the existing file location — canonical (MSFSBlindAssist) is preferred,
    /// with fallback to the legacy FBWBA location for users who still have the DB
    /// in the old folder. If the file doesn't exist at either location, returns the
    /// canonical path (so error messages reference the location the user should build into).
    ///
    /// Note: build targets should call <see cref="DatabasePathResolver.GetCanonicalDatabasePath"/>
    /// directly so newly-built databases always go to the canonical folder.
    /// </summary>
    /// <param name="simulatorVersion">FS2020 or FS2024</param>
    /// <returns>Full path to the database file</returns>
    public static string GetDefaultDatabasePath(string simulatorVersion)
    {
        return DatabasePathResolver.ResolveExistingDatabasePath(simulatorVersion);
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
            try
            {
                return processes != null && processes.Length > 0;
            }
            finally
            {
                if (processes != null)
                {
                    foreach (var process in processes)
                        process?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error checking if simulator is running: {ex.Message}");
            return false; // Assume not running if we can't check
        }
    }

    /// <summary>
    /// Gets the base path for MSFS/MSFS24 from UserCfg.opt file
    /// </summary>
    /// <param name="simulatorVersion">FS2020 or FS2024</param>
    /// <returns>Base path if found, null otherwise</returns>
    private string? GetMSFSBasePath(string simulatorVersion)
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

            string? basePath = TryParseUserCfgForBasePath(roamingPath);
            if (basePath != null)
            {
                Log.Debug("Database", $"Found {simulatorVersion} base path from UserCfg.opt: {basePath}");
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
                    Log.Debug("Database", $"Found {simulatorVersion} base path from Store UserCfg.opt: {basePath}");
                    return basePath;
                }
            }

            // For FS2024, also check LocalCache location (Store version)
            if (simulatorVersion == "FS2024")
            {
                string localCachePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalCache",
                    "UserCfg.opt");

                basePath = TryParseUserCfgForBasePath(localCachePath);
                if (basePath != null)
                {
                    Log.Debug("Database", $"Found {simulatorVersion} base path from Store UserCfg.opt: {basePath}");
                    return basePath;
                }
            }

            Log.Debug("Database", $"Could not find base path for {simulatorVersion}");
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error getting MSFS base path: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses UserCfg.opt file to extract InstalledPackagesPath
    /// </summary>
    private string? TryParseUserCfgForBasePath(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                Log.Debug("Database", $"UserCfg.opt not found at: {configPath}");
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
                            Log.Debug("Database", $"InstalledPackagesPath found but directory doesn't exist: {path}");
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Database", $"Error parsing UserCfg.opt: {ex.Message}");
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
    public string? StatusMessage { get; set; }
    public string? DetailMessage { get; set; }
}

/// <summary>
/// Event arguments for build completion
/// </summary>
public class BuildCompletedEventArgs : EventArgs
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
