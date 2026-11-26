namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// BlueBird's tablet words the reader still needs: the five apps, each app's side menu, the sentence-case
/// spelling of a label (<see cref="Pretty"/>) and the edit distance the top bar uses. The tablet is read from the
/// module's memory with BlueBird's exact text, so nothing here corrects a read. Pure.
/// </summary>
public static class Bbs757EfbVocabulary
{
    /// <summary>The five apps on the home screen, as their page titles read.</summary>
    public static readonly string[] Apps = { "AIRCRAFT", "NAVIGATION", "SYSTEMS", "CHECKLIST", "SETTINGS" };

    /// <summary>Each app's side menu, top to bottom (the menu items the side column offers).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Menus = new Dictionary<string, string[]>
    {
        ["AIRCRAFT"] = new[] { "COCKPIT PRESETS", "FUEL", "WEIGHT & BALANCE", "GROUND SERVICES", "CABIN & DOORS", "MAINTENANCE", "FAILURES" },
        ["NAVIGATION"] = new[] { "ACARS DASHBOARD", "OPERATING FP", "WEATHER & NOTAM", "CHARTS", "MAP", "PERF CALC" },
        ["SETTINGS"] = new[] { "GENERAL", "AIRCRAFT", "LIVERY OPTIONS", "CALLOUTS", "CABIN AUDIO", "AIRAC DATA" },
        ["CHECKLIST"] = new[] { "RESET ALL", "INTERACTIVE", "BEFORE START", "AFTER START", "BEFORE TAKE OFF", "AFTER TAKE OFF",
                                "IN RANGE", "APPROACH", "BEFORE LANDING", "AFTER LANDING", "SHUT DOWN" },
    };

    /// <summary>Levenshtein distance.</summary>
    public static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>"COCKPIT PRESETS" → "Cockpit presets"; keeps short all-caps tokens (GPU, APU, IRS,
    /// CG, ZFW, ACARS, NM, PB, SVC, A/C) as they are, so a screen reader spells them.</summary>
    public static string Pretty(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Any(char.IsLower)) return s;
        var words = s.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length <= 1 || Acronyms.Contains(w.Trim('(', ')', ':', ',')) || w.Any(char.IsDigit)) continue;
            if (!w.Any(char.IsLetter)) continue;
            // A word joined by "/" is checked part by part ("GPU/JETWAY" is GPU/jetway); a whole word in the acronyms ("A/C") stays.
            var parts = w.Split('/');
            for (int j = 0; j < parts.Length; j++)
            {
                var part = parts[j];
                if (part.Length <= 1 || Acronyms.Contains(part.Trim('(', ')', ':', ',')) || part.Any(char.IsDigit) || !part.Any(char.IsLetter)) continue;
                parts[j] = (i == 0 && j == 0 ? part.Substring(0, 1) : part.Substring(0, 1).ToLowerInvariant()) + part.Substring(1).ToLowerInvariant();
            }
            words[i] = string.Join('/', parts);
        }
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "GPU", "APU", "IRS", "CG", "ZFW", "ACARS", "NM", "PB", "SVC", "A/C", "EFB", "TOD", "CPDLC", "FP", "NOTAM", "PERF",
        "AIRAC", "SIMBRIEF", "ID", "EHSI", "VNAV", "EGPWS", "RA", "KTS", "V1", "CBs", "MAC", "%MAC", "FWD", "AFT", "L", "R",
        "BBS", "MSFS", "GND", "SRVC", "OK", "OFP",
        // Navigation's Weather & NOTAM and Charts tabs (BlueBird's 2026-10-31 update).
        "METAR", "TAF", "ATIS", "SID", "STAR",
        // The performance calculator's result rows (V speeds, runway distances), spoken as the letters they are.
        "VR", "TODA", "TORA", "ASDA", "LDA",
        // Callouts' DH rows, Weather's WX and ALT/LVL, the ACARS dashboard's ETOPS, PERF calc's FLX TMP.
        "DH", "WX", "ALT", "LVL", "ETOPS", "FLX", "TMP",
    };
}
