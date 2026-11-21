// A source guard beside SpokenDecimalCultureTests: no statement in the app formats a number with a
// decimal (or a group separator) in the PC's culture, apart from a log line. CI runs under en-US,
// where $"{value:F3}" passes unnoticed; under a German or Swedish Windows it reads "118,300". The
// scan walks each source file as C# (strings, interpolation holes, comments) and holds every decimal
// format against the calls that enclose it: one of them must format with the invariant culture, or
// be a log call.
//
// A failure names the file and line. Wrap the string in FormattableString.Invariant(...) (or
// string.Create(CultureInfo.InvariantCulture, ...)); only a new LOG helper belongs in LogHelpers, and
// only a new logger's name in LoggerName.
//
// What the scan cannot see: a number with no format at all ($"{value}"), a bare G or R (they write
// what no format writes) and a format held in a variable (x.ToString(fmt)). SpokenDecimalCultureTests
// is for those, and tools/DecimalSweep lists the first kind by hand, with the compiler's types.

using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class DecimalFormatSourceTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    /// <summary>The shipped projects: what they write can reach the pilot or another program.</summary>
    private static readonly string[] ShippedProjects =
    {
        "MSFSBlindAssist", "MSFSBlindAssist.DisplayLayouts", "MSFSBlindAssistUpdater", "plugins/MSFSBlindAssist.VPilotPlugin",
    };

    [Fact]
    public void No_decimal_is_formatted_in_the_pcs_culture_outside_a_log_line()
    {
        string root = RepoRoot();
        var found = new List<string>();
        foreach (string project in ShippedProjects)
        {
            string dir = Path.Combine(root, project);
            Assert.True(Directory.Exists(dir), $"{project} was not found under {root}");
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
                if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal)) continue;
                foreach (var hit in DecimalFormatScan.Find(File.ReadAllText(file)))
                    found.Add($"{project}/{relative}:{hit.Line}: {hit.Text}");
            }
        }
        Assert.True(found.Count == 0,
            $"{found.Count} format a decimal in the PC's culture (wrap in FormattableString.Invariant):\n" + string.Join("\n", found.Take(400)));
    }

    private static List<string> Scan(string code) => DecimalFormatScan.Find(code).Select(h => h.Text).ToList();

    [Theory]
    [InlineData("var s = $\"COM1 active {value:F3}\";")]
    [InlineData("var s = $\"Mach {m:0.00}\";")]
    [InlineData("announcer.Announce($\"{(left ? \"Left\" : \"Right\")} {Math.Abs(deg):0.0} degrees\");")]
    [InlineData("var s = cond ? $\"{a:F1} NM\" : \"none\";")]
    [InlineData("var s = freq.ToString(\"F2\");")]
    [InlineData("var s = string.Format(\"{0:F1} miles\", d);")]
    [InlineData("var s = $\"{count:N0} feet\";")]
    [InlineData("var s = $@\"path {x:0.#}\";")]
    [InlineData("list.Add($\"{x:+0.0;-0.0} m\");")]
    [InlineData("Run(() => { double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v); announcer.Announce($\"Set to {v:F3}\"); });")]
    // A string further along than a provider's place, and a Create that takes no provider.
    [InlineData("var s = string.Format(\"{0}: {1}\", label, $\"{nm:F1} NM\");")]
    [InlineData("var t = Tuple.Create(label, $\"{v:0.0} volts\");")]
    // A log method's name on something that is no logger.
    [InlineData("writer.WriteLine($\"{lat:F6},{lon:F6}\");")]
    [InlineData("speech.Error($\"Mach {m:0.00}\");")]
    // A bare F, N or P of a number (two decimals), and G with its digits.
    [InlineData("var s = $\"{value:F} and {ratio:P}\";")]
    [InlineData("var s = value.ToString(\"N\");")]
    [InlineData("var s = $\"{tolerance:G3} degrees\";")]
    // A string inside a hole is formatted before the invariant call around the outer string sees it.
    [InlineData("var s = FormattableString.Invariant($\"Speed {(isMach ? $\"Mach {m:0.00}\" : \"250 knots\")}\");")]
    public void A_decimal_formatted_in_the_pcs_culture_is_found(string code) => Assert.Single(Scan(code));

    [Theory]
    [InlineData("var s = FormattableString.Invariant($\"COM1 active {value:F3}\");")]
    [InlineData("var s = string.Create(CultureInfo.InvariantCulture, $\"Mach {m:0.00}\");")]
    [InlineData("var s = string.Format(CultureInfo.InvariantCulture, \"{0:F1} miles\", d);")]
    [InlineData("var s = freq.ToString(\"F2\", CultureInfo.InvariantCulture);")]
    [InlineData("Log.Debug(\"Taxi\", $\"gs={gs:F1}kt \" + $\"lat={lat:F6}\");")]
    [InlineData("_log.Info($\"gs={gs:F1}kt\");")]
    [InlineData("var s = $\"{value:F0} feet, heading {hdg:000}, {count} items\";")]
    [InlineData("var s = $\"{(cond ? 1.5 : 2.0)} {now:HH:mm:ss}\";")]
    [InlineData("// var s = $\"{value:F3}\";\nvar t = 1;")]
    [InlineData("var s = \"{value:F3}\";")]
    [InlineData("var c = '\"'; var s = $\"{value:F0}\";")]
    [InlineData("var s = Inv($\"{nm:0.0} miles\");")]
    [InlineData("string tmp = $\"{cache}.{Guid.NewGuid():N}.tmp\";")]
    [InlineData("var s = string.Create(inv, $\"{count:N0} places\");")]
    [InlineData("sb.AppendFormat(provider, \"{0:F1} miles\", d);")]
    [InlineData("Startup.Info($\"took {seconds:F1} s\");")]
    [InlineData("System.Diagnostics.Debug.WriteLine($\"{x:F2}\");")]
    [InlineData("_taxiFormLog.Error($\"gap {gap:F1} m\");")]
    [InlineData("var s = $\"{startTime:F} {sessionGuid:N}\";")]
    [InlineData("var s = Guid.NewGuid().ToString(\"N\");")]
    // Inside a hole: an invariant call of its own, or a log call around the whole.
    [InlineData("var s = FormattableString.Invariant($\"Speed {(isMach ? FormattableString.Invariant($\"Mach {m:0.00}\") : \"250 knots\")}\");")]
    [InlineData("Log.Debug(\"Fcu\", $\"speed {(isMach ? $\"Mach {m:0.00}\" : \"knots\")}\");")]
    public void An_invariant_or_logged_or_whole_number_format_is_left_alone(string code) => Assert.Empty(Scan(code));

    [Fact]
    public void A_hit_carries_its_line()
    {
        var hit = Assert.Single(DecimalFormatScan.Find("var a = 1;\nvar b = 2;\nvar s = $\"{v:F1}\";\n"));
        Assert.Equal(3, hit.Line);
    }
}

/// <summary>Finds numbers formatted with a decimal in the current culture in C# source.</summary>
internal static class DecimalFormatScan
{
    public sealed record Hit(int Line, string Text);

    /// <summary>Log helpers known by their own name, whatever they are called on. Their text goes to a log file,
    /// never to the pilot: its numbers may follow the PC's culture.</summary>
    private static readonly HashSet<string> LogHelpers = new(StringComparer.Ordinal)
    {
        "RolloutDiag", "DiagLog", "Log", "LogLine",
    };

    /// <summary>Names a logger's methods share with other types (a TextWriter's WriteLine into a file another program
    /// reads, a speech helper's Error): they log only when called on a logger.</summary>
    private static readonly HashSet<string> LogMethods = new(StringComparer.Ordinal)
    {
        "Debug", "Info", "Warn", "Error", "WriteLine", "Trace",
    };

    /// <summary>What a logger is called in this code: Log, _log, _taxiFormLog, PluginLog, a LogChannel held as Channel
    /// or _channel, the Startup log and the framework's Debug.</summary>
    private static readonly Regex LoggerName = new(@"(log|channel)$|^Startup$|^Debug$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether a call writes a log line: a log helper, or a log method called on a logger.</summary>
    private static bool IsLogCall(string name, string last) =>
        LogHelpers.Contains(last) || (LogMethods.Contains(last) && LoggerName.IsMatch(ReceiverOf(name, last)));

    /// <summary>What a call is made on, without its namespaces: "Log" of Utils.Logging.Log.Warn, "" of a bare call.</summary>
    private static string ReceiverOf(string name, string last)
    {
        if (name.Length <= last.Length) return "";
        string receiver = name[..(name.Length - last.Length - 1)];
        return receiver[(receiver.LastIndexOf('.') + 1)..];
    }

    /// <summary>Whether the code between a call's parenthesis and a string is ONE argument and its comma, and that
    /// argument no string itself: the place of a format provider.</summary>
    private static bool IsLoneProvider(ReadOnlySpan<char> before)
    {
        var argument = before.Trim();
        if (argument.Length < 2 || argument[^1] != ',') return false;
        argument = argument[..^1].Trim();
        return argument.Length > 0 && argument.IndexOfAny(',', '"') < 0;
    }

    /// <summary>Calls that format what they are handed with the invariant culture.</summary>
    private static readonly HashSet<string> InvariantCallees = new(StringComparer.Ordinal)
    {
        "FormattableString.Invariant", "Invariant", "Inv", "InvariantLogLine.Format",
    };

    /// <summary>Calls that format with the culture they are handed.</summary>
    private static readonly HashSet<string> CultureTakingCallees = new(StringComparer.Ordinal)
    {
        "Create", "Format", "AppendFormat", "ToString",
    };

    public static List<Hit> Find(string source)
    {
        var scanner = new Scanner(source);
        scanner.Code(inHole: false);
        return scanner.Hits;
    }

    /// <summary>Code named as a Guid, a date or a time: sessionGuid, Guid.NewGuid(), startTime, DateTime.Now.</summary>
    private static readonly Regex NotANumber = new(@"(?:Guid|Date|Time|Now|(?<![A-Za-z])(?:guid|date|time|now))(?![a-z])");

    /// <summary>Whether a format specifier writes a decimal or a group separator. <paramref name="subject"/> is the
    /// code being formatted, where the scan has it.</summary>
    internal static bool IsDecimalSpec(string spec, string subject = "")
    {
        spec = spec.Trim();
        if (spec.Length == 0 || spec.IndexOfAny(new[] { '(', ')', '?', '"', '\'' }) >= 0) return false;
        // With its digit, and a bare E: a number's format and nothing else's.
        if (Regex.IsMatch(spec, @"^[FfEeGg][1-9]\d*$|^[NnPpCc]\d+$|^[Ee]$")) return true;
        // A bare F, N or P writes a number with two decimals, but is also a Guid's format (N, P) and a date's (F):
        // it counts unless what it formats is named as one ({Guid.NewGuid():N}, {startTime:F}).
        if (Regex.IsMatch(spec, @"^[FfNnPp]$")) return !NotANumber.IsMatch(subject);
        return Regex.IsMatch(spec, @"^[+\-0#.,;% ]+$") && Regex.IsMatch(spec, @"[.,][0#]");
    }

    private sealed class Scanner
    {
        private readonly string _s;
        private int _i;
        private readonly List<(string Name, int Open)> _calls = new();
        // How many of _calls were open when each enclosing interpolation hole opened, innermost last.
        private readonly List<int> _holeFloors = new();
        public List<Hit> Hits { get; } = new();

        public Scanner(string source) => _s = source;

        private char At(int k) => k < _s.Length ? _s[k] : '\0';

        /// <summary>Reads code up to the end of the source, or, in a hole, up to the brace that closes it.</summary>
        public void Code(bool inHole)
        {
            int braces = 0, floor = _calls.Count;
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (c == '/' && At(_i + 1) == '/') { SkipLine(); continue; }
                if (c == '/' && At(_i + 1) == '*') { int end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal); _i = end < 0 ? _s.Length : end + 2; continue; }
                if (c == '#' && AtLineStart(_i)) { SkipLine(); continue; }
                if (c == '\'') { SkipChar(); continue; }
                if (TryString()) continue;
                if (c == '(') { _calls.Add((CalleeBefore(_i), _i)); _i++; continue; }
                if (c == ')') { if (_calls.Count > floor) _calls.RemoveAt(_calls.Count - 1); _i++; continue; }
                if (inHole)
                {
                    if (c == '{') braces++;
                    else if (c == '}')
                    {
                        if (braces == 0) { _calls.RemoveRange(floor, _calls.Count - floor); return; }
                        braces--;
                    }
                }
                _i++;
            }
        }

        private bool AtLineStart(int k)
        {
            for (int j = k - 1; j >= 0 && _s[j] != '\n'; j--)
                if (!char.IsWhiteSpace(_s[j])) return false;
            return true;
        }

        private void SkipLine()
        {
            int end = _s.IndexOf('\n', _i);
            _i = end < 0 ? _s.Length : end + 1;
        }

        private void SkipChar()
        {
            _i++;                                   // the opening quote
            if (At(_i) == '\\') _i += 2; else _i++;
            while (_i < _s.Length && _s[_i] != '\'' && _s[_i] != '\n') _i++;   // A and the like
            if (At(_i) == '\'') _i++;
        }

        /// <summary>Reads a string literal starting here, if one does.</summary>
        private bool TryString()
        {
            int k = _i, dollars = 0;
            bool verbatim = false;
            while (At(k) == '$' || At(k) == '@')
            {
                if (_s[k] == '$') dollars++; else verbatim = true;
                k++;
            }
            if (At(k) != '"') return false;
            int quotes = 0;
            while (At(k + quotes) == '"') quotes++;
            int start = _i;
            if (quotes >= 3) { _i = k + quotes; Raw(quotes, dollars, start); return true; }
            _i = k + 1;
            if (dollars > 0) Interpolated(verbatim, start);
            else Plain(verbatim, start);
            return true;
        }

        private void Plain(bool verbatim, int start)
        {
            var text = new StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (verbatim && c == '"' && At(_i + 1) == '"') { text.Append('"'); _i += 2; continue; }
                if (!verbatim && c == '\\') { text.Append(At(_i + 1)); _i += 2; continue; }
                if (c == '"') { _i++; break; }
                if (!verbatim && c == '\n') break;   // an unterminated literal: give up on it
                text.Append(c);
                _i++;
            }
            string literal = text.ToString();
            // A composite format ("{0:F1}") handed to string.Format and the like.
            foreach (Match m in Regex.Matches(literal, @"\{\d+(?:,-?\d+)?:([^{}]+)\}"))
                if (IsDecimalSpec(m.Groups[1].Value)) { Report(start); return; }
            // x.ToString("F2") with no culture after it.
            if (Before(start).EndsWith("ToString(", StringComparison.Ordinal) && NextCode(_i) == ')' && IsDecimalSpec(literal, ToStringReceiver(start)))
                Report(start);
        }

        private void Interpolated(bool verbatim, int start)
        {
            while (_i < _s.Length)
            {
                char c = _s[_i];
                if (verbatim && c == '"' && At(_i + 1) == '"') { _i += 2; continue; }
                if (!verbatim && c == '\\') { _i += 2; continue; }
                if (c == '{' && At(_i + 1) == '{') { _i += 2; continue; }
                if (c == '}' && At(_i + 1) == '}') { _i += 2; continue; }
                if (c == '"') { _i++; return; }
                if (c == '{') { Hole(start); continue; }
                _i++;
            }
        }

        private void Raw(int quotes, int dollars, int start)
        {
            string close = new('"', quotes);
            while (_i < _s.Length)
            {
                if (string.CompareOrdinal(_s, _i, close, 0, quotes) == 0 && At(_i + quotes) != '"') { _i += quotes; return; }
                if (dollars > 0 && _s[_i] == '{')
                {
                    int run = 0;
                    while (At(_i + run) == '{') run++;
                    if (run == dollars) { _i += run - 1; Hole(start); _i += dollars - 1; continue; }
                    _i += run;
                    continue;
                }
                _i++;
            }
        }

        /// <summary>Reads one interpolation hole, the cursor on its opening brace, and reports a decimal format.</summary>
        private void Hole(int stringStart)
        {
            _i++;
            int from = _i;
            _holeFloors.Add(_calls.Count);
            Code(inHole: true);
            _holeFloors.RemoveAt(_holeFloors.Count - 1);
            string hole = _s.Substring(from, Math.Min(_i, _s.Length) - from);
            if (At(_i) == '}') _i++;
            int colon = hole.LastIndexOf(':');
            if (colon >= 0 && At(from + colon - 1) != ':' && IsDecimalSpec(hole[(colon + 1)..], hole[..colon]))
                Report(stringStart, at: from);
        }

        /// <summary>Reports the literal at <paramref name="start"/> unless a call around it formats invariantly or logs.</summary>
        private void Report(int start, int at = -1)
        {
            // A call opened outside the innermost hole around this literal is handed that hole's string, already
            // formatted in the PC's culture, so it cannot make this one invariant:
            // FormattableString.Invariant($"{(m ? $"{x:F2}" : "")}") formats x before Invariant runs. A log call can:
            // whatever it is handed ends in the log.
            int floor = _holeFloors.Count > 0 ? _holeFloors[^1] : 0;
            for (int k = 0; k < _calls.Count; k++)
            {
                var (name, open) = _calls[k];
                string last = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
                if (IsLogCall(name, last)) return;
                if (k < floor) continue;
                if (InvariantCallees.Contains(name) || InvariantCallees.Contains(last)) return;
                // A formatting call handed the invariant culture before the string. Only a formatting call: any other
                // call around the string may mention the culture for a reason of its own (a parse two lines up).
                if (!CultureTakingCallees.Contains(last)) continue;
                var before = _s.AsSpan(open + 1, start - open - 1);
                if (before.Contains("InvariantCulture", StringComparison.Ordinal)) return;
                // string.Create(inv, $"…"), string.Format(provider, "…") and sb.AppendFormat(provider, "…"): a format
                // provider handed in as the FIRST argument, under whatever name, is a culture chosen on purpose. A
                // string further along is an argument, formatted before the call (string.Format("{0}: {1}", label,
                // $"{nm:F1} NM")), and another type's Create or Format takes no provider (Tuple.Create(label, $"…")).
                bool takesProvider = last == "AppendFormat"
                    || ((last is "Create" or "Format") && ReceiverOf(name, last) is "string" or "String");
                if (takesProvider && IsLoneProvider(before)) return;
            }
            int where = at < 0 ? start : at, line = 1;
            for (int j = 0; j < where; j++) if (_s[j] == '\n') line++;
            int lineStart = where == 0 ? 0 : _s.LastIndexOf('\n', where - 1) + 1, lineEnd = _s.IndexOf('\n', where);
            string text = _s[lineStart..(lineEnd < 0 ? _s.Length : lineEnd)].Trim();
            if (Hits.Count == 0 || Hits[^1].Line != line) Hits.Add(new Hit(line, text.Length > 200 ? text[..200] : text));
        }

        /// <summary>The code before <paramref name="k"/>, without the white space next to it.</summary>
        private string Before(int k)
        {
            int j = k;
            while (j > 0 && char.IsWhiteSpace(_s[j - 1])) j--;
            return _s[Math.Max(0, j - 12)..j];
        }

        /// <summary>What the x.ToString( before <paramref name="k"/> is called on: "freq.", "Guid.NewGuid().".</summary>
        private string ToStringReceiver(int k)
        {
            int end = k;
            while (end > 0 && char.IsWhiteSpace(_s[end - 1])) end--;
            end = Math.Max(0, end - "ToString(".Length);
            int j = end;
            while (j > 0 && (char.IsLetterOrDigit(_s[j - 1]) || _s[j - 1] is '_' or '.' or '?' or '!' or '(' or ')')) j--;
            return _s[j..end];
        }

        private char NextCode(int k)
        {
            while (k < _s.Length && char.IsWhiteSpace(_s[k])) k++;
            return At(k);
        }

        /// <summary>The name called by the parenthesis at <paramref name="open"/>: "Log.Debug", "announcer.Announce", "" for none.</summary>
        private string CalleeBefore(int open)
        {
            int j = open;
            while (j > 0 && char.IsWhiteSpace(_s[j - 1])) j--;
            int end = j;
            while (j > 0 && (char.IsLetterOrDigit(_s[j - 1]) || _s[j - 1] is '_' or '.' or '?' or '!')) j--;
            return _s[j..end].Replace("?", "").Replace("!", "");
        }
    }
}
