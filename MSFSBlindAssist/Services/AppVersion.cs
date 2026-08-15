using System;
using System.Reflection;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The running application's version, read ONCE from the assembly.
///
/// Reads <see cref="AssemblyInformationalVersionAttribute"/>, never AssemblyVersion.
/// AssemblyVersion cannot carry a pre-release identifier: building with
/// -p:Version=8.0.1-pre.42 yields AssemblyVersion 8.0.1.0, identical to 8.0.1-pre.7. The
/// informational version keeps the full string, and SourceLink appends the commit sha —
/// so a preview user's bug report identifies the exact commit.
/// </summary>
public static class AppVersion
{
    /// <summary>The running version, or null if the assembly carries nothing readable.</summary>
    public static SemanticVersion? Current { get; } = ReadCurrent();

    /// <summary>Human-readable form for the About dialog and the Updates settings tab.</summary>
    public static string DisplayString { get; } = Describe(Current);

    /// <summary>
    /// Formats a version for display: "8.0.1-pre.42 (build 4f7e7ba)". The sha is cut to
    /// 7 characters — the full 40 are unusable read aloud by a screen reader.
    /// </summary>
    public static string Describe(SemanticVersion? version)
    {
        if (version is null) return "unknown";

        var sha = version.BuildMetadata;
        if (string.IsNullOrEmpty(sha)) return version.ToString();

        var shortSha = sha.Length > 7 ? sha[..7] : sha;
        return $"{version} (build {shortSha})";
    }

    private static SemanticVersion? ReadCurrent()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var parsed = SemanticVersion.TryParse(informational);
        if (parsed is not null) return parsed;

        // Fallback only if the attribute is missing or malformed. Loses any pre-release
        // identifier, which is exactly the limitation this class exists to avoid — but a
        // degraded version beats none.
        var fallback = assembly.GetName().Version;
        return fallback is null
            ? null
            : SemanticVersion.TryParse($"{fallback.Major}.{fallback.Minor}.{fallback.Build}");
    }
}
