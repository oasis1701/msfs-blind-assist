// The ONE documented exception to "every log write goes through Utils/Logging/Log":
// this code runs inside vPilot's process on .NET Framework and cannot reference the
// app's logger. It still resolves into the canonical %APPDATA%\MSFSBlindAssist\logs
// folder, so "send me your logs" remains one folder.
//
// Unlike the vPilot-to-TTS original this does NOT truncate on load — a plugin that
// wipes its own log every time vPilot starts destroys the evidence from the session
// the user is reporting.

using System;
using System.IO;

namespace MSFSBlindAssist.VPilotPlugin
{
    internal static class PluginLog
    {
        private const long MaxBytes = 1024 * 1024; // 1 MB, then one .old rollover
        private static readonly object Gate = new object();
        private static readonly string LogPath;

        static PluginLog()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MSFSBlindAssist", "logs");
            try { Directory.CreateDirectory(dir); } catch { }
            LogPath = Path.Combine(dir, "vpilot-plugin.log");
        }

        public static void Info(string message) { Write("INFO", message); }
        public static void Error(string message) { Write("ERROR", message); }

        private static void Write(string level, string message)
        {
            try
            {
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [vpilot-plugin] {2}",
                    DateTime.Now, level, message);
                lock (Gate)
                {
                    Rollover();
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
            catch { }
        }

        private static void Rollover()
        {
            try
            {
                var info = new FileInfo(LogPath);
                if (!info.Exists || info.Length < MaxBytes) return;
                string old = LogPath + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(LogPath, old);
            }
            catch { }
        }
    }
}
