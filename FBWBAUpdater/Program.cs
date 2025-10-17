using System;
using System.IO;
using System.Windows.Forms;

namespace FBWBAUpdater
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Log arguments for diagnostics
            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "FBWBA_Updater_Args.log");
                File.WriteAllText(logPath,
                    $"Updater started at: {DateTime.Now}\n" +
                    $"Arguments count: {args.Length}\n" +
                    string.Join("\n", Array.ConvertAll(args, (arg) => $"  Arg: '{arg}'"))
                );
            }
            catch
            {
                // Ignore logging errors
            }

            // Check if we have the correct number of arguments
            if (args.Length != 3)
            {
                string diagnosticInfo = $"Received {args.Length} arguments:\n\n";
                for (int i = 0; i < args.Length; i++)
                {
                    diagnosticInfo += $"Arg {i}: {args[i]}\n";
                }

                MessageBox.Show(
                    "Invalid arguments.\n\n" +
                    "Usage: FBWBAUpdater.exe <zipPath> <appDirectory> <appExecutablePath>\n\n" +
                    diagnosticInfo + "\n" +
                    $"A log file has been saved to:\n{Path.Combine(Path.GetTempPath(), "FBWBA_Updater_Args.log")}",
                    "FBWBA Updater - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            string zipPath = args[0];
            string appDirectory = args[1];
            string appExecutablePath = args[2];

            // Enable visual styles for Windows Forms
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run the updater form
            Application.Run(new UpdaterForm(zipPath, appDirectory, appExecutablePath));
        }
    }
}
