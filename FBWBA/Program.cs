using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FBWBA.Utils;

namespace FBWBA
{
    static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // CRITICAL: Select and copy the correct SimConnect.dll BEFORE any SimConnect types are loaded
            if (!EnsureCorrectSimConnectDll())
            {
                MessageBox.Show(
                    "Failed to load the correct SimConnect.dll version.\n\n" +
                    "Please ensure SimConnect_msfs_2020.dll and SimConnect_msfs_2024.dll are present in the application directory.",
                    "SimConnect DLL Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // Allocate a console for NVDA to monitor
            AllocConsole();

            // Hide the console window but keep it active for NVDA
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }

            Console.WriteLine("FlyByWire Blind Access starting...");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// Detects which Flight Simulator is running and copies the appropriate SimConnect.dll
        /// This must run BEFORE any code that references SimConnect types
        /// </summary>
        /// <returns>True if DLL was successfully selected and copied, false otherwise</returns>
        private static bool EnsureCorrectSimConnectDll()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string targetDllPath = Path.Combine(appDirectory, "SimConnect.dll");

                // Detect which simulator is running using shared utility
                string detectedSimulator = SimulatorDetector.DetectRunningSimulator();
                string sourceDllName = SimulatorDetector.GetSimConnectDllName(detectedSimulator);

                Console.WriteLine($"[Program] Detected simulator: {detectedSimulator} - using {sourceDllName}");

                string sourceDllPath = Path.Combine(appDirectory, sourceDllName);

                // Verify source DLL exists
                if (!File.Exists(sourceDllPath))
                {
                    Console.WriteLine($"[Program] ERROR: Source DLL not found: {sourceDllPath}");
                    return false;
                }

                // Check if we need to copy (avoid unnecessary file operations)
                bool needsCopy = true;
                if (File.Exists(targetDllPath))
                {
                    // Compare file sizes and timestamps to see if they're the same
                    FileInfo sourceInfo = new FileInfo(sourceDllPath);
                    FileInfo targetInfo = new FileInfo(targetDllPath);

                    if (sourceInfo.Length == targetInfo.Length &&
                        Math.Abs((sourceInfo.LastWriteTime - targetInfo.LastWriteTime).TotalSeconds) < 2)
                    {
                        needsCopy = false;
                        Console.WriteLine($"[Program] SimConnect.dll already matches {sourceDllName}");
                    }
                }

                // Copy the appropriate DLL
                if (needsCopy)
                {
                    // If target exists, try to delete it first (may fail if loaded)
                    if (File.Exists(targetDllPath))
                    {
                        try
                        {
                            File.Delete(targetDllPath);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // DLL is already loaded - can't replace it
                            // This is OK if it's already the right version
                            Console.WriteLine("[Program] WARNING: SimConnect.dll is already loaded, cannot replace");
                        }
                    }

                    File.Copy(sourceDllPath, targetDllPath, overwrite: true);
                    Console.WriteLine($"[Program] Copied {sourceDllName} to SimConnect.dll");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Program] ERROR in EnsureCorrectSimConnectDll: {ex.Message}");
                Debug.WriteLine($"[Program] ERROR in EnsureCorrectSimConnectDll: {ex}");
                return false;
            }
        }
    }
}
