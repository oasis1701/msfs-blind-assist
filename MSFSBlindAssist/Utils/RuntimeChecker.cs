using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// Checks runtime requirements for the application.
/// </summary>
public static class RuntimeChecker
{
    /// <summary>
    /// Result of a runtime check operation.
    /// </summary>
    public class CheckResult
    {
        public bool Success { get; set; }
        public List<string> Messages { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> MissingComponents { get; set; } = new List<string>();

        public string GetSummary()
        {
            var summary = string.Join("\n", Messages);
            if (Errors.Count > 0)
            {
                summary += "\n\nErrors:\n" + string.Join("\n", Errors);
            }
            if (MissingComponents.Count > 0)
            {
                summary += "\n\nMissing Components:\n" + string.Join("\n", MissingComponents);
            }
            return summary;
        }
    }

    /// <summary>
    /// Performs a comprehensive runtime check.
    /// </summary>
    /// <returns>Check result with details</returns>
    public static CheckResult PerformRuntimeCheck()
    {
        var result = new CheckResult { Success = true };

        StartupLogger.Log("=== Starting Runtime Check ===");

        // Check .NET Runtime
        CheckDotNetRuntime(result);

        // Check architecture
        CheckArchitecture(result);

        // Check required DLLs
        CheckRequiredDlls(result);

        // Check Windows version
        CheckWindowsVersion(result);

        StartupLogger.Log($"=== Runtime Check Complete: {(result.Success ? "PASSED" : "FAILED")} ===");

        return result;
    }

    private static void CheckDotNetRuntime(CheckResult result)
    {
        try
        {
            var runtimeVersion = Environment.Version;
            StartupLogger.Log($".NET Runtime Version: {runtimeVersion}");

            // Check if it's .NET 9 or higher
            if (runtimeVersion.Major >= 9)
            {
                result.Messages.Add($"✓ .NET Runtime {runtimeVersion} detected");
                StartupLogger.Log($"✓ .NET Runtime version check passed");
            }
            else
            {
                result.Success = false;
                result.Errors.Add($"✗ .NET Runtime {runtimeVersion} detected (requires .NET 9 or higher)");
                result.MissingComponents.Add(".NET 9 Runtime");
                StartupLogger.Log($"✗ .NET Runtime version check FAILED - found {runtimeVersion}, need 9.0+");
            }

            // Check for Windows Desktop Runtime (Windows Forms support) using standard reflection approach
            StartupLogger.Log("Checking for .NET Desktop Runtime (Windows Forms)...");
            try
            {
                // Try to load a Windows Forms type - this is the standard way to check for Desktop Runtime
                Type? formType = Type.GetType("System.Windows.Forms.Form, System.Windows.Forms");
                if (formType != null)
                {
                    result.Messages.Add("✓ .NET Desktop Runtime (Windows Forms) available");
                    StartupLogger.Log("✓ Windows Forms type loaded successfully - Desktop Runtime present");
                }
                else
                {
                    result.Success = false;
                    result.Errors.Add("✗ .NET Desktop Runtime not found");
                    result.MissingComponents.Add(".NET 9 Desktop Runtime (Windows Forms)");
                    StartupLogger.Log("✗ Windows Forms type could not be loaded - Desktop Runtime missing");
                }
            }
            catch (Exception formEx)
            {
                result.Success = false;
                result.Errors.Add("✗ .NET Desktop Runtime not found");
                result.MissingComponents.Add(".NET 9 Desktop Runtime (Windows Forms)");
                StartupLogger.Log($"✗ Error loading Windows Forms type: {formEx.Message}");
            }

            // Log runtime information for diagnostics
            string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            string frameworkDescription = RuntimeInformation.FrameworkDescription;
            StartupLogger.Log($"Runtime Directory: {runtimeDir}");
            StartupLogger.Log($"Framework Description: {frameworkDescription}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"✗ Error checking .NET Runtime: {ex.Message}");
            StartupLogger.LogError("Error checking .NET Runtime", ex);
        }
    }

    private static void CheckArchitecture(CheckResult result)
    {
        try
        {
            bool is64BitOS = Environment.Is64BitOperatingSystem;
            bool is64BitProcess = Environment.Is64BitProcess;

            StartupLogger.Log($"OS Architecture: {(is64BitOS ? "64-bit" : "32-bit")}");
            StartupLogger.Log($"Process Architecture: {(is64BitProcess ? "64-bit" : "32-bit")}");

            if (is64BitProcess)
            {
                result.Messages.Add("✓ Running as 64-bit process");
                StartupLogger.Log("✓ Architecture check passed");
            }
            else
            {
                result.Success = false;
                result.Errors.Add("✗ Application requires 64-bit process");
                StartupLogger.Log("✗ Architecture check FAILED - not running as 64-bit");
            }

            if (!is64BitOS)
            {
                result.Success = false;
                result.Errors.Add("✗ Application requires 64-bit Windows");
                result.MissingComponents.Add("64-bit Windows Operating System");
                StartupLogger.Log("✗ OS Architecture check FAILED - 32-bit Windows detected");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"✗ Error checking architecture: {ex.Message}");
            StartupLogger.LogError("Error checking architecture", ex);
        }
    }

    private static void CheckRequiredDlls(CheckResult result)
    {
        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        StartupLogger.Log($"Checking for required DLLs in: {appDirectory}");

        var requiredDlls = new[]
        {
            "SimConnect_msfs_2020.dll",
            "SimConnect_msfs_2024.dll",
            "Tolk.dll",
            "nvdaControllerClient.dll",
            "Microsoft.FlightSimulator.SimConnect.dll"
        };

        foreach (var dll in requiredDlls)
        {
            string dllPath = Path.Combine(appDirectory, dll);
            if (File.Exists(dllPath))
            {
                result.Messages.Add($"✓ {dll} found");
                StartupLogger.Log($"✓ Found: {dll}");
            }
            else
            {
                result.Success = false;
                result.Errors.Add($"✗ {dll} not found");
                result.MissingComponents.Add(dll);
                StartupLogger.Log($"✗ Missing: {dll}");
            }
        }

        // Check navdatareader directory
        string navdataPath = Path.Combine(appDirectory, "navdatareader");
        if (Directory.Exists(navdataPath))
        {
            result.Messages.Add("✓ navdatareader directory found");
            StartupLogger.Log($"✓ Found: navdatareader directory");
        }
        else
        {
            result.Success = false;
            result.Errors.Add("✗ navdatareader directory not found");
            result.MissingComponents.Add("navdatareader directory");
            StartupLogger.Log($"✗ Missing: navdatareader directory");
        }
    }

    private static void CheckWindowsVersion(CheckResult result)
    {
        try
        {
            var osVersion = Environment.OSVersion;
            StartupLogger.Log($"Windows Version: {osVersion}");

            // Windows 10 is version 10.0, Windows 11 is also 10.0 with higher build number
            if (osVersion.Platform == PlatformID.Win32NT && osVersion.Version.Major >= 10)
            {
                result.Messages.Add($"✓ Windows {osVersion.Version} compatible");
                StartupLogger.Log("✓ Windows version check passed");
            }
            else
            {
                result.Success = false;
                result.Errors.Add($"✗ Windows 10 or higher required (detected: {osVersion})");
                StartupLogger.Log($"✗ Windows version check FAILED - {osVersion}");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"✗ Error checking Windows version: {ex.Message}");
            StartupLogger.LogError("Error checking Windows version", ex);
        }
    }

    /// <summary>
    /// Gets a user-friendly error message with instructions on how to fix issues.
    /// </summary>
    /// <param name="checkResult">The check result</param>
    /// <returns>User-friendly error message</returns>
    public static string GetUserFriendlyErrorMessage(CheckResult checkResult)
    {
        var message = "FBWBA failed to start due to missing requirements:\n\n";

        // Add errors
        foreach (var error in checkResult.Errors)
        {
            message += error + "\n";
        }

        message += "\n";

        // Add instructions based on missing components
        if (checkResult.MissingComponents.Any(c => c.Contains(".NET")))
        {
            message += "To fix .NET Runtime issues:\n";
            message += "1. Download and install .NET 9 Desktop Runtime (x64) from:\n";
            message += "   https://dotnet.microsoft.com/download/dotnet/9.0\n";
            message += "2. Look for 'Desktop Runtime' under '.NET Desktop Runtime 9.0'\n";
            message += "3. Download the 'x64' version\n";
            message += "4. Run the installer and restart this application\n\n";
        }

        if (checkResult.MissingComponents.Any(c => c.Contains(".dll") || c.Contains("navdatareader")))
        {
            message += "To fix missing DLL files:\n";
            message += "1. Make sure you extracted the complete application package\n";
            message += "2. All DLL files must be in the same folder as FBWBA.exe\n";
            message += "3. The 'navdatareader' folder must be present\n";
            message += "4. Re-download the application if files are missing\n\n";
        }

        message += $"\nA detailed log file has been saved to:\n{StartupLogger.GetLogFilePath()}\n";
        message += "\nPlease send this log file when reporting issues.";

        return message;
    }
}
