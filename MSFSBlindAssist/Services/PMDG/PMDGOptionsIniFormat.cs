namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Reads the <c>[SDK]</c> block PMDG's SDK requires in a variant's <c>&lt;family&gt;_Options.ini</c>
/// (<c>737_Options.ini</c>, <c>777_Options.ini</c>) before the CDU, or any other data-broadcast
/// consumer, can see that aircraft: <c>EnableDataBroadcast</c> and <c>EnableCDUBroadcast.0</c>/<c>.1</c>
/// (left and right CDU), plus <c>EnableCDUBroadcast.2</c> for the 777's center CDU (the 737 NG3 has
/// two). Pure, and read-only by design: MSFSBA never writes this file ([PSDK-1]); it tells the pilot
/// which lines to add (<see cref="SdkBlock"/>).
/// </summary>
public static class PMDGOptionsIniFormat
{
    private const string SdkSectionName = "SDK";
    private const string EnableDataBroadcastKey = "EnableDataBroadcast";
    private const string EnableCduBroadcastKeyPrefix = "EnableCDUBroadcast.";
    private const string EnabledValue = "1";

    /// <summary>The keys PMDG's SDK requires under <c>[SDK]</c>, in the order the pilot is shown them.</summary>
    public static IReadOnlyList<string> RequiredKeys(bool requireCenterCdu)
    {
        var keys = new List<string>
        {
            EnableDataBroadcastKey,
            EnableCduBroadcastKeyPrefix + "0",
            EnableCduBroadcastKeyPrefix + "1",
        };
        if (requireCenterCdu) keys.Add(EnableCduBroadcastKeyPrefix + "2");
        return keys;
    }

    /// <summary>The exact lines the pilot is told to paste: the header, then every required key set to 1.</summary>
    public static IReadOnlyList<string> SdkBlock(bool requireCenterCdu)
    {
        var lines = new List<string> { $"[{SdkSectionName}]" };
        foreach (var key in RequiredKeys(requireCenterCdu)) lines.Add($"{key}={EnabledValue}");
        return lines;
    }

    /// <summary>
    /// Null when the file's <c>[SDK]</c> block is usable, otherwise a short phrase for the pilot:
    /// "no [SDK] section", "2 [SDK] sections, keep only one" (a stray duplicate block is a reported
    /// cause of a blank CDU), or "missing or not set to 1: " and the keys, in <see cref="RequiredKeys"/>
    /// order. A key counts only under <c>[SDK]</c>, with exactly the required name (a spaced
    /// "Enable Data Broadcast" does not) and the value 1; section and key names ignore case.
    /// </summary>
    public static string? DescribeProblem(IReadOnlyList<string> lines, bool requireCenterCdu)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int sdkSections = 0;
        bool inSdk = false;
        foreach (var raw in lines)
        {
            if (TryParseSectionHeader(raw, out var section))
            {
                inSdk = string.Equals(section, SdkSectionName, StringComparison.OrdinalIgnoreCase);
                if (inSdk) sdkSections++;
                continue;
            }
            if (inSdk && TryParseKeyValue(raw, out var key, out var value)) values[key] = value;
        }

        if (sdkSections == 0) return $"no [{SdkSectionName}] section";
        if (sdkSections > 1) return $"{sdkSections} [{SdkSectionName}] sections, keep only one";

        var missing = new List<string>();
        foreach (var key in RequiredKeys(requireCenterCdu))
        {
            if (!values.TryGetValue(key, out var value) || value != EnabledValue) missing.Add(key);
        }
        return missing.Count == 0 ? null : "missing or not set to 1: " + string.Join(", ", missing);
    }

    private static bool TryParseSectionHeader(string rawLine, out string name)
    {
        name = string.Empty;
        string line = rawLine.Trim();
        if (line.Length < 2 || line[0] != '[' || line[^1] != ']') return false;
        name = line.Substring(1, line.Length - 2).Trim();
        return true;
    }

    private static bool TryParseKeyValue(string rawLine, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        string line = rawLine.Trim();
        if (line.Length == 0 || line[0] == ';') return false;

        int eq = line.IndexOf('=');
        if (eq <= 0) return false;

        key = line.Substring(0, eq).Trim();
        value = line.Substring(eq + 1).Trim();
        return key.Length > 0;
    }
}
