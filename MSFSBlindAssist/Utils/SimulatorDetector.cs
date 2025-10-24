
namespace MSFSBlindAssist.Utils;
/// <summary>
/// Utility class for detecting which Microsoft Flight Simulator is currently running.
/// Provides a centralized, consistent method for simulator version detection.
/// </summary>
public static class SimulatorDetector
{
    /// <summary>
    /// Detects which Microsoft Flight Simulator is currently running by checking for known process names.
    /// </summary>
    /// <returns>
    /// "FS2024" if Flight Simulator 2024 is running,
    /// "FS2020" if Flight Simulator 2020 is running,
    /// "Unknown" if no recognized simulator process is found
    /// </returns>
    public static string DetectRunningSimulator()
    {
        try
        {
            // Check for FS2024 process first (newer version takes priority)
            var fs2024Processes = Process.GetProcessesByName("FlightSimulator2024");
            if (fs2024Processes != null && fs2024Processes.Length > 0)
            {
                Debug.WriteLine("[SimulatorDetector] Detected FS2024 (FlightSimulator2024.exe)");
                return "FS2024";
            }

            // Check for FS2020 process
            var fs2020Processes = Process.GetProcessesByName("FlightSimulator");
            if (fs2020Processes != null && fs2020Processes.Length > 0)
            {
                Debug.WriteLine("[SimulatorDetector] Detected FS2020 (FlightSimulator.exe)");
                return "FS2020";
            }

            Debug.WriteLine("[SimulatorDetector] No recognized simulator process found");
            return "Unknown";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SimulatorDetector] Error detecting simulator: {ex.Message}");
            Console.WriteLine($"[SimulatorDetector] Error detecting simulator: {ex.Message}");
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets the executable name for a given simulator version.
    /// </summary>
    /// <param name="simulatorVersion">The simulator version ("FS2024", "FS2020", or "Unknown")</param>
    /// <returns>The process executable name without extension, or null if unknown</returns>
    public static string? GetProcessName(string simulatorVersion)
    {
        switch (simulatorVersion)
        {
            case "FS2024":
                return "FlightSimulator2024";
            case "FS2020":
                return "FlightSimulator";
            default:
                return null;
        }
    }

    /// <summary>
    /// Gets the SimConnect DLL filename for a given simulator version.
    /// </summary>
    /// <param name="simulatorVersion">The simulator version ("FS2024", "FS2020", or "Unknown")</param>
    /// <returns>The SimConnect DLL filename</returns>
    public static string GetSimConnectDllName(string simulatorVersion)
    {
        switch (simulatorVersion)
        {
            case "FS2024":
                return "SimConnect_msfs_2024.dll";
            case "FS2020":
                return "SimConnect_msfs_2020.dll";
            default:
                return "SimConnect_msfs_2020.dll"; // Default to FS2020 for compatibility
        }
    }
}
