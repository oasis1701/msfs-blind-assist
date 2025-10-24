using System.Diagnostics;
using System.Text;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// Logs startup initialization steps to help diagnose launch failures.
/// Logs are written to both Debug output and a file in the user's temp directory.
/// </summary>
public static class StartupLogger
{
    private static readonly string LogFilePath;
    private static readonly StringBuilder LogBuffer = new StringBuilder();
    private static readonly object LogLock = new object();

    static StartupLogger()
    {
        // Create log file in temp directory with timestamp
        string tempPath = Path.GetTempPath();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        LogFilePath = Path.Combine(tempPath, $"MSFSBlindAssist_Startup_{timestamp}.log");
    }

    /// <summary>
    /// Gets the path to the current log file.
    /// </summary>
    public static string GetLogFilePath() => LogFilePath;

    /// <summary>
    /// Logs a startup step with timestamp.
    /// </summary>
    /// <param name="message">The message to log</param>
    public static void Log(string message)
    {
        lock (LogLock)
        {
            string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

            // Write to debug output
            Debug.WriteLine($"[StartupLogger] {timestampedMessage}");

            // Write to console
            Console.WriteLine(timestampedMessage);

            // Buffer for file write
            LogBuffer.AppendLine(timestampedMessage);

            // Write to file immediately (in case of crash)
            try
            {
                File.AppendAllText(LogFilePath, timestampedMessage + Environment.NewLine);
            }
            catch
            {
                // Ignore file write errors - we still have debug output
            }
        }
    }

    /// <summary>
    /// Logs an error with exception details.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="ex">The exception that occurred</param>
    public static void LogError(string message, Exception ex)
    {
        Log($"ERROR: {message}");
        Log($"Exception Type: {ex.GetType().FullName}");
        Log($"Exception Message: {ex.Message}");
        Log($"Stack Trace: {ex.StackTrace}");

        if (ex.InnerException != null)
        {
            Log($"Inner Exception: {ex.InnerException.GetType().FullName}");
            Log($"Inner Exception Message: {ex.InnerException.Message}");
        }
    }

    /// <summary>
    /// Gets all logged messages as a single string.
    /// </summary>
    /// <returns>All logged messages</returns>
    public static string GetAllLogs()
    {
        lock (LogLock)
        {
            return LogBuffer.ToString();
        }
    }

    /// <summary>
    /// Logs system information useful for diagnostics.
    /// </summary>
    public static void LogSystemInfo()
    {
        Log("=== System Information ===");
        Log($"OS Version: {Environment.OSVersion}");
        Log($"OS Architecture: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");
        Log($"Process Architecture: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        Log($".NET Runtime Version: {Environment.Version}");
        Log($".NET Runtime Directory: {RuntimeEnvironment.GetRuntimeDirectory()}");
        Log($"Application Directory: {AppDomain.CurrentDomain.BaseDirectory}");
        Log($"Working Directory: {Environment.CurrentDirectory}");
        Log($"Command Line: {Environment.CommandLine}");
        Log("=========================");
    }
}
