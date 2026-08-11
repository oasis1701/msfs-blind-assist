// ONE source, TWO assemblies. This file is compiled into the vPilot plugin (net48,
// running inside vPilot's process) and LINKED into MSFS Blind Assist (net10) by a
// <Compile Include=... Link=...> in MSFSBlindAssist.csproj — never copied. Same trick
// tools/PMDGDispatchTester uses for PMDGNG3DataStruct.cs, for the same reason: the two
// ends of a wire protocol must not be able to drift apart.
//
// Keep it framework-neutral: no nullable annotations, no C# newer than the net48
// project's default, no dependency outside System/System.Text.
#nullable disable

using System.Text;

namespace MSFSBlindAssist.VPilot
{
    /// <summary>
    /// The named-pipe line protocol: <c>type \t from \t message</c>, one line per event.
    /// Backslash, tab, CR and LF are escaped so a multi-line private message cannot
    /// desync the reader.
    /// </summary>
    public static class VPilotWireFormat
    {
        /// <summary>
        /// The named pipe both ends use. It lives HERE, in the file linked into both
        /// assemblies, for the same reason the escaping does: this is one half of a wire
        /// contract, and a contract with two independent copies is a contract that drifts.
        /// It must never go back to "MSFSBlindAssist" + the legacy "vPilot-to-TTS" name:
        /// NamedPipeServerStream allows one server instance per name by default, so if a
        /// user still runs the old standalone tray app it owns that name and this app's
        /// server cannot start at all.
        /// </summary>
        public const string PipeName = "MSFSBlindAssist.vPilot";

        public static string Encode(string type, string from, string message)
        {
            return Escape(type) + "\t" + Escape(from) + "\t" + Escape(message);
        }

        public static bool TryDecode(string line, out string type, out string from, out string message)
        {
            type = "";
            from = "";
            message = "";

            if (string.IsNullOrEmpty(line))
                return false;

            string[] parts = line.Split('\t');
            if (parts.Length != 3)
                return false;

            type = Unescape(parts[0]);
            from = Unescape(parts[1]);
            message = Unescape(parts[2]);
            return true;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0)
                return s ?? "";

            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                // A trailing lone backslash is kept literally rather than eating the end
                // of the field.
                if (s[i] != '\\' || i == s.Length - 1)
                {
                    sb.Append(s[i]);
                    continue;
                }

                char next = s[++i];
                switch (next)
                {
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    // Unknown escape: keep both characters, so an ordinary Windows path
                    // in a chat message survives unchanged.
                    default: sb.Append('\\').Append(next); break;
                }
            }
            return sb.ToString();
        }
    }
}
