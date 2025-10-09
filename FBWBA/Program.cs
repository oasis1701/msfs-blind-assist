using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

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
    }
}
