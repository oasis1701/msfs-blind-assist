using System.Text;

namespace MSFSBlindAssist.Database;

/// <summary>
/// Builds the navdatareader command line. Pure — depends only on its arguments — so the
/// quoting rules below are unit-testable without launching a process.
/// </summary>
public static class NavdataReaderArguments
{
    /// <summary>
    /// Assembles the full argument string.
    ///
    /// <paramref name="basePath"/> is deliberately LAST. Windows argument parsing lets a
    /// mis-escaped quote swallow everything after it, so if -b came first a malformed base
    /// path would absorb -c and the shipped config would silently never be applied — with
    /// no warning, because File.Exists on the config had already passed.
    /// </summary>
    public static string Build(string simFlag, string outputPath, string? configPath, string? basePath)
    {
        var sb = new StringBuilder();
        sb.Append("-f ").Append(simFlag);
        sb.Append(" -o ").Append(Quote(outputPath));

        if (!string.IsNullOrEmpty(configPath))
            sb.Append(" -c ").Append(Quote(configPath));

        if (!string.IsNullOrEmpty(basePath))
            sb.Append(" -b ").Append(Quote(basePath));

        return sb.ToString();
    }

    /// <summary>
    /// Wraps a path in quotes, doubling any trailing backslashes.
    ///
    /// CommandLineToArgvW treats a backslash immediately before the closing quote as an
    /// escape, so a drive-root path like E:\ would arrive at the child process as E:" —
    /// a directory that cannot exist. Doubling only the trailing run is the documented fix
    /// and leaves interior backslashes alone.
    /// </summary>
    internal static string Quote(string path)
    {
        int trailing = 0;
        while (trailing < path.Length && path[path.Length - 1 - trailing] == '\\')
            trailing++;

        return "\"" + path + new string('\\', trailing) + "\"";
    }
}
