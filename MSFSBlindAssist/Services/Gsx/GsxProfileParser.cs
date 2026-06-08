using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Parses a GSX profile .ini (under %APPDATA%\Virtuali\GSX\MSFS) into parking
/// positions. Tolerant: unknown sections/keys are ignored; malformed lines are
/// skipped, never thrown.
/// </summary>
public static class GsxProfileParser
{
    // "none" is the GSX parking-name enum 0 (real, unnamed stands — ~543 across profiles).
    private static readonly string[] ParkingCategories =
        { "gate", "parking", "none", "ramp", "stand", "dock", "cargo", "tie", "hangar", "mil" };

    public static List<GsxGate> Parse(string iniPath) => ParseLines(File.ReadAllLines(iniPath));

    public static List<GsxGate> ParseLines(IEnumerable<string> lines)
    {
        var gates = new List<GsxGate>();
        GsxGate? current = null;
        bool currentIsRecognized = false; // true = section header parsed as a gate/parking category

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                CommitSection(gates, current, currentIsRecognized);
                string header = line.Substring(1, line.Length - 2).Trim();
                if (TryParseSectionHeader(header, out var g))
                {
                    current = g;
                    currentIsRecognized = true;
                }
                else
                {
                    // Unknown header — create a tentative gate in case is_deicearea = 1 appears
                    current = new GsxGate { RawSectionName = header };
                    currentIsRecognized = false;
                }
                continue;
            }

            if (current == null) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            string val = line.Substring(eq + 1).Trim();
            ApplyKey(current, key, val);
        }
        CommitSection(gates, current, currentIsRecognized);
        return gates;
    }

    // Decides whether a completed section should be added to the list.
    // Recognized gate/parking sections are always added (existing behaviour).
    // Unrecognized sections are added only when is_deicearea = 1 was seen.
    // For deice areas the display label is ALWAYS derived from uiname (preferred)
    // or a cleaned-up raw section name — regardless of what the header parse set in
    // Concourse. This ensures that headers like "[deicing -medium- - taxiway s- abeam
    // stand 177m -entry face south-]" (which match the "stand" token and would
    // otherwise produce Concourse = "STAND" for every pad) get unique, human-readable
    // labels. Normal gate/parking sections are completely unaffected.
    private static void CommitSection(List<GsxGate> gates, GsxGate? g, bool recognized)
    {
        if (g == null) return;
        if (!recognized && !g.IsDeiceArea) return;

        if (g.IsDeiceArea)
        {
            // Prefer the explicit uiname when the profile supplies one (e.g. EGLL "DeIcing-Vader South").
            // Fall back to a cleaned version of the raw section header so each pad is still unique.
            string label = !string.IsNullOrWhiteSpace(g.Uiname)
                ? g.Uiname.Trim()
                : CleanSectionName(g.RawSectionName);
            g.Concourse = label;
        }

        gates.Add(g);
    }

    // Strip surrounding brackets/dashes, collapse interior runs of dashes/spaces.
    // e.g. "deicing -medium- - taxiway s- abeam stand 177m -entry face south-"
    //   -> "deicing medium taxiway s abeam stand 177m entry face south"
    private static string CleanSectionName(string raw)
    {
        // Replace runs of dashes (with optional surrounding spaces) with a single space.
        string s = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*-+\s*", " ");
        // Collapse multiple spaces.
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim();
    }

    // Handles every real-world shape seen across profiles (verified vs OMDB/EGLL/EIDW):
    //   "gate c 18"      -> concourse C, number 18
    //   "gate c 18 l"    -> concourse C, number 18, suffix L  (separate suffix token)
    //   "gate 218l"      -> concourse "", number 218, suffix L  (suffix attached to number)
    //   "gate 209"       -> concourse "" (pure-numeric gate, e.g. EGLL)
    //   "gate t 1a"      -> concourse T, number 1, suffix A  (terminal stand, non-L/R suffix)
    //   "parking 120c"   -> concourse P, number 120, suffix C
    //   "w parking 4"    -> concourse W (direction prefix before the category), number 4
    private static bool TryParseSectionHeader(string header, out GsxGate gate)
    {
        gate = new GsxGate { RawSectionName = header };
        var tokens = header.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;

        // The category token (gate/parking/...) may be preceded by a direction prefix
        // (e.g. "w parking 4", "ne parking 7"), so scan for it rather than assuming index 0.
        int catIdx = -1;
        for (int i = 0; i < tokens.Length; i++)
            if (Array.IndexOf(ParkingCategories, tokens[i].ToLowerInvariant()) >= 0) { catIdx = i; break; }
        if (catIdx < 0) return false;

        string category = tokens[catIdx].ToLowerInvariant();
        string directionPrefix = string.Join(" ", tokens.Take(catIdx)).ToUpperInvariant(); // "" or "W"/"NE"

        var concourse = new System.Text.StringBuilder();
        int number = 0; bool numberSet = false; string suffix = "";

        for (int i = catIdx + 1; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (!numberSet)
            {
                // number, optionally with a trailing alpha suffix glued on: "218l", "1a", "120c"
                var m = Regex.Match(t, "^(\\d+)([A-Za-z]*)$");
                if (m.Success)
                {
                    number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    numberSet = true;
                    if (m.Groups[2].Value.Length > 0) suffix = m.Groups[2].Value.ToUpperInvariant();
                }
                else if (IsAllLetters(t)) // concourse letter(s) before the number: "c", "t", "l"
                {
                    if (concourse.Length > 0) concourse.Append(' ');
                    concourse.Append(t.ToUpperInvariant());
                }
                // else: unrecognized token, ignore
            }
            else if (suffix.Length == 0 && IsAllLetters(t)) // standalone suffix after the number: "gate 537 l"
            {
                suffix = t.ToUpperInvariant();
            }
        }

        if (concourse.Length > 0)                          gate.Concourse = concourse.ToString();
        else if (directionPrefix.Length > 0)               gate.Concourse = directionPrefix;   // "W", "NE"
        else if (category == "parking")                    gate.Concourse = "P";
        else if (category == "gate" || category == "none") gate.Concourse = "";                // -> "Spot N - …"
        else                                               gate.Concourse = category.ToUpperInvariant();

        gate.Category = category;
        gate.Number = number;
        gate.Suffix = suffix;
        // Never emit an empty identity (no number AND no concourse).
        return numberSet || gate.Concourse.Length > 0;
    }

    private static bool IsAllLetters(string s)
    {
        if (s.Length == 0) return false;
        foreach (char c in s) if (!char.IsLetter(c)) return false;
        return true;
    }

    private static void ApplyKey(GsxGate g, string key, string val)
    {
        switch (key)
        {
            case "this_parking_pos":
                if (TryParseTriple(val, out double la, out double lo, out double hd))
                { g.Latitude = la; g.Longitude = lo; g.Heading = NormalizeHeading(hd); g.HasParkingPos = true; }
                break;
            case "parkingsystem_stopposition":
                if (TryParseTriple(val, out double sla, out double slo, out double shd))
                { g.StopLatitude = sla; g.StopLongitude = slo; g.StopHeading = NormalizeHeading(shd); }
                break;
            case "hasjetway":
                g.HasJetway = val.Trim() == "1";
                break;
            case "type":
                if (int.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t))
                    g.GsxType = t;
                break;
            case "maxwingspan":
                if (double.TryParse(val.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double mw))
                    g.MaxWingspanMeters = mw;
                break;
            case "parkingsystem":
                g.VdgsType = val.Trim();
                break;
            case "airlinecodes":
                g.AirlineCodes = val.Trim();
                break;
            case "is_deicearea":
                if (val.Trim() == "1") g.IsDeiceArea = true;
                break;
            case "uiname":
                // Always capture uiname so CommitSection can use it for deice-area display labels
                // regardless of whether the section header was parsed as a gate/parking category.
                // For normal gate/parking sections Category != "" and Concourse is already set from
                // the header — we don't overwrite Concourse here; CommitSection reads g.Uiname only
                // for deice areas where the override is safe and intentional.
                g.Uiname = val.Trim();
                // Legacy: also set Concourse for unrecognised-header sections (Category == "") so
                // existing code paths (non-deice sections that happen to have a uiname) still work.
                if (string.IsNullOrEmpty(g.Category))
                    g.Concourse = val.Trim();
                break;
        }
    }

    private static bool TryParseTriple(string val, out double a, out double b, out double c)
    {
        a = b = c = 0;
        var parts = val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        bool ok = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                & double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b);
        if (parts.Length >= 3)
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out c);
        return ok;
    }

    private static double NormalizeHeading(double h)
    {
        h %= 360.0;
        if (h < 0) h += 360.0;
        return h;
    }
}
