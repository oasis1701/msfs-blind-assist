using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Validates the navdatareader configuration MSFS Blind Assist ships in Resources.
///
/// The config REPLACES navdatareader's built-in one rather than merging with it, so a
/// truncated or corrupt copy is not a partial config — it is a config with every filter
/// empty. navdatareader accepts such a file without complaint (only a zero-byte file is
/// rejected), which means File.Exists alone cannot tell a usable config from one that
/// silently disables the whole filter set.
/// </summary>
public static class NavdataReaderConfig
{
    /// <summary>The one MSFSBA-specific entry; its presence is what makes the file ours.</summary>
    public const string JetwaysExclusion = "*_jetways.bgl";

    private const string ExcludeFilenamesKey = "ExcludeFilenames";

    /// <summary>
    /// True when <paramref name="configText"/> carries an active ExcludeFilenames entry
    /// including the jetways exclusion.
    /// </summary>
    public static bool IsUsable(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
            return false;

        foreach (string raw in configText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                continue;   // comment or blank — a commented-out key is not an active one

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            if (!line.AsSpan(0, eq).Trim().Equals(ExcludeFilenamesKey, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string value in line[(eq + 1)..].Split(','))
            {
                if (value.Trim().Equals(JetwaysExclusion, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads <paramref name="path"/> and applies <see cref="IsUsable"/>. Any I/O failure —
    /// missing, locked, unreadable — counts as unusable, because the caller's next move is
    /// to refuse the build either way.
    /// </summary>
    public static bool IsUsableFile(string path)
    {
        try
        {
            return IsUsable(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Warn("Database", $"Could not read the navdatareader config at {path}: {ex.Message}");
            return false;
        }
    }
}
