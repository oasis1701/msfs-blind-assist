using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace FBWBAUpdater
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("FBWBA Updater");
            Console.WriteLine("=============");
            Console.WriteLine();

            if (args.Length != 3)
            {
                Console.WriteLine("Usage: FBWBAUpdater.exe <zipPath> <appDirectory> <appExecutablePath>");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            string zipPath = args[0];
            string appDirectory = args[1];
            string appExecutablePath = args[2];

            Console.WriteLine($"ZIP Path: {zipPath}");
            Console.WriteLine($"App Directory: {appDirectory}");
            Console.WriteLine($"App Executable: {appExecutablePath}");
            Console.WriteLine();

            try
            {
                // Step 1: Verify ZIP file exists
                if (!File.Exists(zipPath))
                {
                    throw new FileNotFoundException("Update ZIP file not found", zipPath);
                }

                Console.WriteLine("Update file verified.");

                // Step 2: Wait for main application to close
                Console.WriteLine("Waiting for FBWBA to close...");
                WaitForProcessToExit("FBWBA");
                Thread.Sleep(1000); // Additional delay to ensure file handles are released

                // Step 3: Create backup directory
                string backupDir = Path.Combine(Path.GetTempPath(), "FBWBA_Backup_" + DateTime.Now.Ticks);
                Directory.CreateDirectory(backupDir);
                Console.WriteLine($"Backup directory created: {backupDir}");

                // Step 4: Backup current files (for rollback if needed)
                Console.WriteLine("Creating backup...");
                BackupFiles(appDirectory, backupDir);

                // Step 5: Extract update files
                Console.WriteLine("Extracting update...");
                ExtractUpdate(zipPath, appDirectory);

                // Step 6: Clean up ZIP file
                Console.WriteLine("Cleaning up temporary files...");
                File.Delete(zipPath);

                // Step 7: Restart application
                Console.WriteLine("Restarting FBWBA...");
                Process.Start(appExecutablePath);

                Console.WriteLine();
                Console.WriteLine("Update completed successfully!");
                Console.WriteLine("FBWBA will now restart.");
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine($"ERROR: Update failed!");
                Console.WriteLine($"Details: {ex.Message}");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Waits for a process with the given name to exit
        /// </summary>
        static void WaitForProcessToExit(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);

            foreach (Process process in processes)
            {
                try
                {
                    process.WaitForExit(10000); // Wait up to 10 seconds
                }
                catch
                {
                    // Process may have already exited
                }
            }
        }

        /// <summary>
        /// Backs up important files for potential rollback
        /// </summary>
        static void BackupFiles(string sourceDir, string backupDir)
        {
            try
            {
                // Backup only the executable and critical DLLs (not the entire directory)
                string[] filesToBackup = new[]
                {
                    "FBWBA.exe",
                    "FBWBA.exe.config",
                    "FBWBAUpdater.exe"
                };

                foreach (string fileName in filesToBackup)
                {
                    string sourcePath = Path.Combine(sourceDir, fileName);
                    if (File.Exists(sourcePath))
                    {
                        string destPath = Path.Combine(backupDir, fileName);
                        File.Copy(sourcePath, destPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Backup failed: {ex.Message}");
                // Continue anyway - backup is optional
            }
        }

        /// <summary>
        /// Extracts the update ZIP to the application directory
        /// </summary>
        static void ExtractUpdate(string zipPath, string destinationDir)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    try
                    {
                        // Skip directory entries
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        // Build the full path
                        string destinationPath = Path.Combine(destinationDir, entry.FullName);

                        // Create directory if needed
                        string directoryPath = Path.GetDirectoryName(destinationPath);
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

                        // Skip if it's the updater itself (don't overwrite while running)
                        if (entry.Name.Equals("FBWBAUpdater.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"Skipping: {entry.FullName} (updater executable)");
                            continue;
                        }

                        // Extract file (overwrite existing)
                        Console.WriteLine($"Extracting: {entry.FullName}");
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to extract {entry.FullName}: {ex.Message}");
                        // Continue with other files
                    }
                }
            }
        }
    }
}
