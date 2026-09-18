using System.Collections.Generic;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// Pure parsing/patching for the <c>[SDK]</c> block PMDG's own SDK requires in a variant's
/// <c>&lt;family&gt;_Options.ini</c> (e.g. <c>737_Options.ini</c>, <c>777_Options.ini</c>) before
/// the CDU (and every other third-party data-broadcast consumer) can see that aircraft at all.
/// Confirmed against PMDG's own forum, not guessed: the required keys are
/// <c>EnableDataBroadcast</c> and <c>EnableCDUBroadcast.0</c>/<c>.1</c> (left/right CDU), plus
/// <c>EnableCDUBroadcast.2</c> for an aircraft with a center CDU (the 777; the 737 NG3 has only
/// two).
///
/// <para>
/// PMDG's own writer can silently drop a hand-edited <c>[SDK]</c> block on the next in-sim save
/// if it isn't separated from the surrounding content by blank lines — multiple forum threads
/// report exactly this ("paste it in, it gets overwritten"). <see cref="ApplyBroadcastSettings"/>
/// always leaves a blank line before a newly-appended section and a trailing blank line at EOF to
/// avoid that trap. When a <c>[SDK]</c> section already exists, only its own keys are touched —
/// nothing else in the file is reformatted or reordered.
/// </para>
/// </summary>
public static class PMDGOptionsIniFormat
{
    public const string SdkSectionHeader = "[SDK]";
    public const string EnableDataBroadcastKey = "EnableDataBroadcast";
    public const string EnableCduBroadcastKeyPrefix = "EnableCDUBroadcast.";
    private const string EnabledValue = "1";

    /// <summary>
    /// The exact keys PMDG's SDK requires under <c>[SDK]</c> for this aircraft family.
    /// <paramref name="requireCenterCdu"/> is true only for an aircraft with a center CDU (the
    /// PMDG 777 — the 737 NG3 has left/right only).
    /// </summary>
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

    /// <summary>
    /// True only when every key <see cref="RequiredKeys"/> lists is present under the file's
    /// (first) <c>[SDK]</c> section with value exactly <c>1</c>. Missing the section entirely,
    /// missing one key, or a key present with any other value (e.g. <c>0</c>, blank, or the
    /// space-in-the-name typo <c>"Enable Data Broadcast"</c> a live PMDG forum report made) all
    /// count as "not configured".
    /// </summary>
    public static bool HasBroadcastEnabled(IReadOnlyList<string> lines, bool requireCenterCdu)
    {
        var values = ReadSdkValues(lines);
        foreach (var key in RequiredKeys(requireCenterCdu))
        {
            if (!values.TryGetValue(key, out var value)) return false;
            if (value.Trim() != EnabledValue) return false;
        }
        return true;
    }

    /// <summary>
    /// Reads the key=value pairs from the file's first <c>[SDK]</c> section (case-insensitive
    /// section/key match, PMDG's own casing preserved as the returned key). A later duplicate key
    /// within that section wins (mirrors how most INI readers behave); a section that doesn't
    /// exist yields an empty map. Public so the check and the patch share one reader.
    /// </summary>
    public static Dictionary<string, string> ReadSdkValues(IReadOnlyList<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var (start, end) = FindSdkSectionBody(lines);
        if (start < 0) return values;

        for (int i = start; i < end; i++)
        {
            if (!TryParseKeyValue(lines[i], out var key, out var value)) continue;
            values[key] = value;
        }
        return values;
    }

    /// <summary>
    /// Returns a new line list with every key <see cref="RequiredKeys"/> lists set to <c>1</c>
    /// under <c>[SDK]</c>. An existing key is updated in place (its line replaced, position
    /// preserved); a missing key is appended at the end of the existing section body. When the
    /// file has no <c>[SDK]</c> section at all, a new one is appended at EOF, preceded by a blank
    /// separating line (when the file is non-empty and doesn't already end in one) and followed by
    /// a trailing blank line — the formatting PMDG's own writer needs to keep the block on its
    /// next save. Every other line is left untouched, in place.
    /// </summary>
    public static List<string> ApplyBroadcastSettings(IReadOnlyList<string> lines, bool requireCenterCdu)
    {
        var result = new List<string>(lines);
        var required = RequiredKeys(requireCenterCdu);
        var (start, end) = FindSdkSectionBody(result);

        if (start < 0)
        {
            AppendNewSdkSection(result, required);
            return result;
        }

        // Track which required keys were found (and where) so any left over get appended.
        var foundAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = start; i < end; i++)
        {
            if (!TryParseKeyValue(result[i], out var key, out _)) continue;
            foundAt[key] = i;
        }

        foreach (var key in required)
        {
            if (foundAt.TryGetValue(key, out var lineIndex))
            {
                result[lineIndex] = $"{key}={EnabledValue}";
            }
            else
            {
                result.Insert(end, $"{key}={EnabledValue}");
                end++;
            }
        }
        return result;
    }

    /// <summary>
    /// Locates the first <c>[SDK]</c> header (case-insensitive) and its body range
    /// [start, end) — start is the line right after the header, end is the next section header
    /// or EOF. Returns (-1, -1) when no <c>[SDK]</c> section exists.
    /// </summary>
    private static (int start, int end) FindSdkSectionBody(IReadOnlyList<string> lines)
    {
        int headerIndex = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i], out var name) && string.Equals(name, "SDK", StringComparison.OrdinalIgnoreCase))
            {
                headerIndex = i;
                break;
            }
        }
        if (headerIndex < 0) return (-1, -1);

        int end = lines.Count;
        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            if (IsSectionHeader(lines[i], out _)) { end = i; break; }
        }
        return (headerIndex + 1, end);
    }

    private static void AppendNewSdkSection(List<string> lines, IReadOnlyList<string> requiredKeys)
    {
        if (lines.Count > 0 && lines[^1].Trim().Length != 0)
        {
            lines.Add(string.Empty);
        }
        lines.Add(SdkSectionHeader);
        foreach (var key in requiredKeys)
        {
            lines.Add($"{key}={EnabledValue}");
        }
        lines.Add(string.Empty);
    }

    private static bool IsSectionHeader(string rawLine, out string name)
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
