using System;
using System.Windows.Forms;

namespace FBWBAUpdater
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Check if we have the correct number of arguments
            if (args.Length != 3)
            {
                MessageBox.Show(
                    "Invalid arguments.\n\n" +
                    "Usage: FBWBAUpdater.exe <zipPath> <appDirectory> <appExecutablePath>",
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
