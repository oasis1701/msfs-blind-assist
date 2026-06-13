using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Reads a GSX <c>.py</c> profile and exposes (1) the gate-&gt;offset-function map and
/// (2) the parsed body of each <c>customOffset</c> function, classified into one of the
/// known dispatch idioms. Pure text parsing — no Python execution, no app dependencies.
/// Every failure path degrades to "unknown / unclassified" so the evaluator can fall
/// back to a zero (base-position) offset and never throw.
/// </summary>
public sealed class GsxPyProfileReader
{
    /// <summary>A scalar or 2-tuple value found in a GSX offset table.</summary>
    public readonly record struct TableValue(double Longitudinal, double Lateral)
    {
        public static TableValue Scalar(double v) => new(v, 0.0);
    }

    public enum IdiomKind
    {
        /// <summary>Function body couldn't be classified — caller uses 0 (base).</summary>
        Unclassified,
        /// <summary>Constant 0 (empty table, <c>Distance()</c>, etc.).</summary>
        Zero,
        /// <summary>Plain <c>table.get(idMajor, 0)</c> / <c>standardoffset(ad, table)</c>.</summary>
        ByIdMajor,
        /// <summary><c>HandleAircraftOffsets(ad, specificTables, genericTable)</c>.</summary>
        HandleAircraftOffsets,
        /// <summary><c>ICAOAircraftOffsets(...)</c> or its inline equivalent.</summary>
        IcaoAircraftOffsets,
        /// <summary>
        /// Group-keyed only: <c>table.get(aircraftData.aircraftGroup, 0)</c> or a
        /// <c>X if aircraftData.aircraftGroup == "ARC-Y" else 0</c> ternary
        /// (common in MK-Studios profiles). Looks up <see cref="OffsetFunction.TableGroup"/>.
        /// </summary>
        ByGroup,
    }

    /// <summary>One entry of a HandleAircraftOffsets <c>specificTables</c> mapping.</summary>
    public readonly record struct SpecificEntry(Dictionary<int, TableValue> SubTable, int FallbackKey);

    /// <summary>A fully-parsed and classified offset function.</summary>
    public sealed class OffsetFunction
    {
        public IdiomKind Kind { get; init; } = IdiomKind.Unclassified;

        // ByIdMajor / HandleAircraftOffsets generic table (keyed by idMajor).
        public Dictionary<int, TableValue> GenericTable { get; init; } = new();

        // HandleAircraftOffsets: idMajor -> (subTable, fallbackKey).
        public Dictionary<int, SpecificEntry> SpecificTables { get; init; } = new();

        // ICAOAircraftOffsets tables.
        public Dictionary<string, TableValue> TableIcao { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, TableValue> AircraftValues { get; init; } = new();
        public Dictionary<string, TableValue> TableGroup { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly string _text;
    // Normalized gate key ("66" / "66A") -> function name.
    private readonly Dictionary<string, string> _gateToFunc = new(StringComparer.OrdinalIgnoreCase);
    // Function name -> raw body text (lazily classified/cached).
    private readonly Dictionary<string, string> _funcBodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OffsetFunction> _classifiedCache = new(StringComparer.Ordinal);

    private GsxPyProfileReader(string text)
    {
        _text = text;
        ParseGateMap();
        IndexFunctionBodies();
    }

    /// <summary>Loads and parses a profile. Throws only on file I/O errors.</summary>
    public static GsxPyProfileReader Load(string pyPath)
    {
        string text = File.ReadAllText(pyPath);
        return new GsxPyProfileReader(text);
    }

    /// <summary>For tests: build directly from in-memory profile text.</summary>
    public static GsxPyProfileReader FromText(string text) => new(text);

    /// <summary>
    /// Resolves a gate number + optional suffix to its offset-function name.
    /// Matches a suffix-specific entry first ("66A"), else the bare number ("66").
    /// </summary>
    public bool TryGetOffsetFunctionName(int number, string? suffix, out string funcName)
    {
        suffix = (suffix ?? string.Empty).Trim();
        if (suffix.Length > 0 &&
            _gateToFunc.TryGetValue(number.ToString(CultureInfo.InvariantCulture) + suffix.ToUpperInvariant(),
                out var specific))
        {
            funcName = specific;
            return true;
        }

        if (_gateToFunc.TryGetValue(number.ToString(CultureInfo.InvariantCulture), out var bare))
        {
            funcName = bare;
            return true;
        }

        funcName = string.Empty;
        return false;
    }

    /// <summary>All distinct (gateKey -&gt; functionName) entries — used by the sweep probe.</summary>
    public IReadOnlyDictionary<string, string> GateMap => _gateToFunc;

    /// <summary>Raw body text of a function (diagnostics only). Empty if unknown.</summary>
    public string GetFunctionBody(string funcName)
        => _funcBodies.TryGetValue(funcName, out var b) ? b : string.Empty;

    /// <summary>
    /// Parses and classifies a function body. Returns an Unclassified function (never null)
    /// on any failure or unknown name.
    /// </summary>
    public OffsetFunction GetFunction(string funcName)
    {
        if (string.IsNullOrEmpty(funcName))
            return new OffsetFunction();

        if (_classifiedCache.TryGetValue(funcName, out var cached))
            return cached;

        OffsetFunction parsed;
        try
        {
            parsed = ParseFunction(funcName);
        }
        catch
        {
            parsed = new OffsetFunction();
        }

        _classifiedCache[funcName] = parsed;
        return parsed;
    }

    // ---- gate map parsing -------------------------------------------------

    // Matches a single gate->function entry within a parkings dict body:
    //   66 : (Terminal1AN, customOffsetA54A58A62A66, )
    //   "66A" : (Terminal1AN, customOffsetA54586266A, )
    //   22  : (AutoGateNames(Intl),Gate_22),
    // Captures the key (number or "numSuffix") and the LAST identifier before the
    // closing paren that names a function. We capture the whole tuple body then pull
    // the function name out, because the first element can itself be a call with commas.
    private static readonly Regex GateEntryRegex = new(
        """^\s*(?:"(?<k>\d+[A-Za-z]*)"|(?<k>\d+))\s*:\s*\((?<body>.*?)\)\s*,?\s*$""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private void ParseGateMap()
    {
        // Only scan inside the parkings = { ... } region(s). Some profiles define more
        // than one (rare) — process every match line in the file; the regex is anchored
        // to the gate-entry shape so non-gate lines are ignored.
        foreach (Match m in GateEntryRegex.Matches(_text))
        {
            string key = m.Groups["k"].Value;
            string body = m.Groups["body"].Value;
            string? func = ExtractFunctionNameFromTupleBody(body);
            if (func == null)
                continue; // name-only entry (no offset func) -> base 0; skip.

            string norm = NormalizeGateKey(key);
            // First definition wins; later duplicates (commented-out re-adds rarely slip in) ignored.
            if (!_gateToFunc.ContainsKey(norm))
                _gateToFunc[norm] = func;
        }
    }

    private static string NormalizeGateKey(string key)
    {
        // Split leading digits from the suffix, uppercase the suffix.
        int i = 0;
        while (i < key.Length && char.IsDigit(key[i])) i++;
        string num = key.Substring(0, i);
        string suf = key.Substring(i).ToUpperInvariant();
        return num + suf;
    }

    // A gate entry tuple is (categoryName, offsetFunc, ) — the offset function is the
    // SECOND element. Examples:
    //   "Terminal1AN, customOffsetA54A58A62A66, "   -> [Terminal1AN, customOffsetA54A58A62A66]
    //   "AutoGateNames(Intl), Gate_22"              -> [AutoGateNames(Intl), Gate_22]
    //   "ManualGateNames(GA_32,\"Satena\"), "       -> name-only, NO function (-> base 0)
    //   "k, "                                       -> name-only, NO function (-> base 0)
    // We split on top-level commas, drop trailing empties, and require element[1] to be
    // a bare identifier (the function). element[0] is the category (often itself bare,
    // e.g. "Terminal1AN", or a call) and is never the function.
    private static string? ExtractFunctionNameFromTupleBody(string body)
    {
        var parts = SplitTopLevel(body)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        if (parts.Count < 2)
            return null; // single-element (name-only) tuple -> no offset function
        string second = parts[1];
        return IsBareIdentifier(second) ? second : null;
    }

    private static bool IsBareIdentifier(string s)
    {
        if (s.Length == 0) return false;
        if (s.IndexOf('(') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\'') >= 0)
            return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
        foreach (char c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        // Exclude Python literals that aren't functions.
        return s is not ("None" or "True" or "False");
    }

    private static List<string> SplitTopLevel(string s)
    {
        var result = new List<string>();
        int depth = 0;
        bool inStr = false;
        char strCh = '\0';
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                if (c == strCh) inStr = false;
                continue;
            }
            switch (c)
            {
                case '"':
                case '\'':
                    inStr = true; strCh = c; break;
                case '(':
                case '[':
                case '{':
                    depth++; break;
                case ')':
                case ']':
                case '}':
                    if (depth > 0) depth--; break;
                case ',':
                    if (depth == 0)
                    {
                        result.Add(s.Substring(start, i - start));
                        start = i + 1;
                    }
                    break;
            }
        }
        if (start <= s.Length)
            result.Add(s.Substring(start));
        return result;
    }

    // ---- function body indexing -------------------------------------------

    private static readonly Regex DefRegex = new(
        @"^def\s+(?<name>\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private void IndexFunctionBodies()
    {
        var defs = DefRegex.Matches(_text);
        for (int i = 0; i < defs.Count; i++)
        {
            string name = defs[i].Groups["name"].Value;
            int bodyStart = defs[i].Index;
            int bodyEnd = (i + 1 < defs.Count) ? defs[i + 1].Index : _text.Length;
            // Trim back from the next def to drop a preceding decorator line (@...).
            if (!_funcBodies.ContainsKey(name))
                _funcBodies[name] = _text.Substring(bodyStart, bodyEnd - bodyStart);
        }
    }

    // ---- function body parsing / classification ---------------------------

    private static readonly Regex NamedTableRegex = new(
        @"(?<name>\b\w+)\s*=\s*\{(?<body>[^{}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private OffsetFunction ParseFunction(string funcName)
    {
        if (!_funcBodies.TryGetValue(funcName, out var body))
            return new OffsetFunction();

        // 1) Parse every "<name> = { ... }" dict literal in the body.
        var intTables = new Dictionary<string, Dictionary<int, TableValue>>(StringComparer.Ordinal);
        var strTables = new Dictionary<string, Dictionary<string, TableValue>>(StringComparer.Ordinal);

        foreach (Match m in NamedTableRegex.Matches(body))
        {
            string name = m.Groups["name"].Value;
            string dictBody = m.Groups["body"].Value;
            ParseDict(dictBody, out var intD, out var strD, out bool hasStrKeys);
            if (hasStrKeys)
                strTables[name] = strD;
            else
                intTables[name] = intD;
        }

        // 2) Detect the dispatch idiom from the body text.
        if (Regex.IsMatch(body, @"\bICAOAircraftOffsets\s*\(") ||
            Regex.IsMatch(body, @"\bLateral_offset\s*\(\s*TableIcao\.get") ||
            // inline ICAO lookup: TableIcao.get(...aircraftValues.get(...TableGroup.get
            (body.Contains("TableIcao") && body.Contains("TableGroup")) ||
            // plain ICAO-only table: TableIcao.get(aircraftData.icaoTypeDesignator, 0)
            Regex.IsMatch(body, @"\w+\.get\s*\(\s*aircraftData\.icaoTypeDesignator"))
        {
            // The ICAO table is usually named "TableIcao"; for plain ICAO-only profiles
            // capture whatever variable feeds .get(...icaoTypeDesignator...).
            var icaoVarMatch = Regex.Match(body, @"(?<t>\w+)\.get\s*\(\s*aircraftData\.icaoTypeDesignator");
            string icaoVar = icaoVarMatch.Success ? icaoVarMatch.Groups["t"].Value : "TableIcao";
            var icaoTable = strTables.TryGetValue("TableIcao", out var ti) ? ti
                : (strTables.TryGetValue(icaoVar, out var ti2) ? ti2 : null);

            return new OffsetFunction
            {
                Kind = IdiomKind.IcaoAircraftOffsets,
                TableIcao = icaoTable != null
                    ? ToCaseInsensitive(icaoTable) : new(StringComparer.OrdinalIgnoreCase),
                AircraftValues = intTables.TryGetValue("aircraftValues", out var av) ? av : new(),
                TableGroup = strTables.TryGetValue("TableGroup", out var tg)
                    ? ToCaseInsensitive(tg) : new(StringComparer.OrdinalIgnoreCase),
            };
        }

        if (Regex.IsMatch(body, @"\bHandleAircraftOffsets\s*\("))
        {
            var specific = ParseSpecificTables(body, intTables);
            return new OffsetFunction
            {
                Kind = IdiomKind.HandleAircraftOffsets,
                GenericTable = intTables.TryGetValue("table", out var gt) ? gt : new(),
                SpecificTables = specific,
            };
        }

        // standardoffset(ad, aircraftValues) OR plain table.get(idMajor, ...)
        if (Regex.IsMatch(body, @"\bstandardoffset\s*\("))
        {
            return new OffsetFunction
            {
                Kind = IdiomKind.ByIdMajor,
                GenericTable = intTables.TryGetValue("aircraftValues", out var av2) ? av2
                    : (intTables.TryGetValue("table", out var t2) ? t2 : new()),
            };
        }

        // plain table.get(aircraftData.idMajor, 0)
        var byIdMajorMatch = Regex.Match(body, @"(?<t>\w+)\.get\s*\(\s*aircraftData\.idMajor");
        if (byIdMajorMatch.Success)
        {
            string tname = byIdMajorMatch.Groups["t"].Value;
            if (intTables.TryGetValue(tname, out var bt))
            {
                // Empty table -> constant 0.
                return new OffsetFunction
                {
                    Kind = bt.Count == 0 ? IdiomKind.Zero : IdiomKind.ByIdMajor,
                    GenericTable = bt,
                };
            }
        }

        // Group-keyed dict: someTable.get(aircraftData.aircraftGroup, 0)
        var byGroupMatch = Regex.Match(body, @"(?<t>\w+)\.get\s*\(\s*aircraftData\.aircraftGroup");
        if (byGroupMatch.Success)
        {
            string gname = byGroupMatch.Groups["t"].Value;
            if (strTables.TryGetValue(gname, out var gt))
            {
                return new OffsetFunction
                {
                    Kind = gt.Count == 0 ? IdiomKind.Zero : IdiomKind.ByGroup,
                    TableGroup = ToCaseInsensitive(gt),
                };
            }
        }

        // Group ternary: <value> if aircraftData.aircraftGroup == "ARC-X" else <value>
        // (possibly chained). Build a small group table from the equality clauses.
        if (Regex.IsMatch(body, @"aircraftData\.aircraftGroup\s*=="))
        {
            var gtable = ParseGroupTernary(body);
            if (gtable.Count > 0)
                return new OffsetFunction { Kind = IdiomKind.ByGroup, TableGroup = gtable };
        }

        // Distance() / return None / Distance.fromMeters(0) / unrecognized constant -> 0.
        if (Regex.IsMatch(body, @"return\s+Distance\s*\(\s*\)") ||
            Regex.IsMatch(body, @"return\s+None") ||
            Regex.IsMatch(body, @"return\s+Distance\.fromMeters\s*\(\s*-?\d+(?:\.\d+)?\s*\)"))
        {
            return new OffsetFunction { Kind = IdiomKind.Zero };
        }

        return new OffsetFunction(); // Unclassified
    }

    // specificTables mapping is the second arg to HandleAircraftOffsets:
    //   { 737 : (table737, 0), 787 : (table787, 8), ... }
    private static readonly Regex SpecificEntryRegex = new(
        @"(?<major>\d+)\s*:\s*\(\s*(?<sub>\w+)\s*,\s*(?<fb>-?\d+)\s*\)",
        RegexOptions.Compiled);

    private Dictionary<int, SpecificEntry> ParseSpecificTables(
        string body, Dictionary<string, Dictionary<int, TableValue>> intTables)
    {
        var result = new Dictionary<int, SpecificEntry>();
        // Isolate the HandleAircraftOffsets(...) call so we don't match the generic
        // table's own entries. Find the call and scan its argument region.
        int call = body.IndexOf("HandleAircraftOffsets(", StringComparison.Ordinal);
        string region = call >= 0 ? body.Substring(call) : body;

        foreach (Match m in SpecificEntryRegex.Matches(region))
        {
            int major = int.Parse(m.Groups["major"].Value, CultureInfo.InvariantCulture);
            string subName = m.Groups["sub"].Value;
            int fb = int.Parse(m.Groups["fb"].Value, CultureInfo.InvariantCulture);
            if (intTables.TryGetValue(subName, out var sub))
                result[major] = new SpecificEntry(sub, fb);
        }
        return result;
    }

    // Parse "VALUE if aircraftData.aircraftGroup == "ARC-X" else ..." chains into a
    // group -> value table. e.g. 6.1 if aircraftData.aircraftGroup == "ARC-D" else 0.
    private static readonly Regex GroupTernaryRegex = new(
        "(?<val>-?\\d+(?:\\.\\d+)?)\\s*if\\s+aircraftData\\.aircraftGroup\\s*==\\s*\"(?<grp>[^\"]+)\"",
        RegexOptions.Compiled);

    private static Dictionary<string, TableValue> ParseGroupTernary(string body)
    {
        var t = new Dictionary<string, TableValue>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in GroupTernaryRegex.Matches(body))
        {
            if (double.TryParse(m.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                t[m.Groups["grp"].Value] = TableValue.Scalar(v);
        }
        return t;
    }

    // Parse a dict body into int-keyed or string-keyed entries.
    // Values may be scalars (1.65, -2.5) or 2-tuples ((9.35,-3.75)).
    private static readonly Regex DictEntryRegex = new(
        """(?:"(?<sk>[^"]*)"|'(?<sk>[^']*)'|(?<ik>-?\d+))\s*:\s*(?<val>\([^()]*\)|-?\d+(?:\.\d+)?)""",
        RegexOptions.Compiled);

    private static void ParseDict(
        string dictBody,
        out Dictionary<int, TableValue> intD,
        out Dictionary<string, TableValue> strD,
        out bool hasStrKeys)
    {
        intD = new();
        strD = new(StringComparer.OrdinalIgnoreCase);
        hasStrKeys = false;

        foreach (Match m in DictEntryRegex.Matches(dictBody))
        {
            if (!TryParseValue(m.Groups["val"].Value, out var val))
                continue;

            if (m.Groups["sk"].Success)
            {
                hasStrKeys = true;
                strD[m.Groups["sk"].Value] = val; // last wins (matches Python dict)
            }
            else if (m.Groups["ik"].Success)
            {
                int k = int.Parse(m.Groups["ik"].Value, CultureInfo.InvariantCulture);
                intD[k] = val; // last wins
            }
        }
    }

    private static bool TryParseValue(string raw, out TableValue val)
    {
        raw = raw.Trim();
        val = TableValue.Scalar(0);
        if (raw.Length == 0) return false;

        if (raw[0] == '(')
        {
            // Tuple (a, b)
            string inner = raw.Trim('(', ')');
            var parts = inner.Split(',');
            if (parts.Length >= 2 &&
                double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
            {
                val = new TableValue(a, b);
                return true;
            }
            return false;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double s))
        {
            val = TableValue.Scalar(s);
            return true;
        }
        return false;
    }

    private static Dictionary<string, TableValue> ToCaseInsensitive(Dictionary<string, TableValue> src)
    {
        if (src.Comparer == StringComparer.OrdinalIgnoreCase) return src;
        var d = new Dictionary<string, TableValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in src) d[kv.Key] = kv.Value;
        return d;
    }
}
