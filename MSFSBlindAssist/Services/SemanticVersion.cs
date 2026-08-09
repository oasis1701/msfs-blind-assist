using System;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// A semantic version (semver 2.0.0) with the precedence rules the update check depends on.
///
/// Replaces the old <c>UpdateService.ParseVersion</c>, which regex-stripped the pre-release
/// suffix and compared with <see cref="Version"/>. That could not distinguish 8.0.1-pre.42
/// from 8.0.1-pre.7, which makes any pre-release channel impossible: the app can never
/// decide that a newer preview supersedes the one it is running.
///
/// Two precedence rules are load-bearing and easy to get backwards:
///   - A version WITHOUT a pre-release outranks the same version WITH one
///     (8.0.1 &gt; 8.0.1-pre.42). This is what lets a real release supersede every preview
///     built against it, and is why the preview channel needs no special case for
///     "a release came out".
///   - Numeric pre-release identifiers compare NUMERICALLY, not lexically, so
///     pre.10 &gt; pre.9. Ordinal string comparison inverts this and would strand every
///     preview user on build 9 forever.
///
/// Build metadata (the "+sha" SourceLink appends) is parsed and kept for display but is
/// IGNORED for precedence, per the spec.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    // Patch is optional so a hypothetical two-part tag (v9.1) still reads; anything else
    // is rejected rather than guessed at. Leading zeros are tolerated deliberately: this
    // parses tags written by humans, and strict-semver pedantry here would only turn a
    // readable version into "unparseable".
    private static readonly Regex Pattern = new(
        @"^(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?" +
        @"(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?" +
        @"(?:\+(?<build>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private SemanticVersion(int major, int minor, int patch, string? preRelease, string? buildMetadata)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
        BuildMetadata = buildMetadata;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>The identifiers after '-', e.g. "pre.42". Null for a release.</summary>
    public string? PreRelease { get; }

    /// <summary>The identifiers after '+', e.g. the SourceLink commit sha. Ignored for precedence.</summary>
    public string? BuildMetadata { get; }

    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Parses a version or tag name. Returns null for anything unreadable; never throws.</summary>
    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var trimmed = text.Trim().TrimStart('v', 'V');
        var match = Pattern.Match(trimmed);
        if (!match.Success) return null;

        if (!int.TryParse(match.Groups["major"].Value, out var major)) return null;
        if (!int.TryParse(match.Groups["minor"].Value, out var minor)) return null;

        var patch = 0;
        if (match.Groups["patch"].Success && !int.TryParse(match.Groups["patch"].Value, out patch)) return null;

        var pre = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        var build = match.Groups["build"].Success ? match.Groups["build"].Value : null;

        return new SemanticVersion(major, minor, patch, pre, build);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        // A release outranks a pre-release of the same core version.
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    // Semver's numeric identifiers are `0` or [1-9][0-9]* — no leading zeros, no sign.
    // Using long.TryParse instead would (a) accept "01", making "pre.01" and "pre.1"
    // compare equal while hashing differently, breaking the Equals/GetHashCode contract,
    // (b) overflow past ~19 digits, and (c) accept "-5", which the regex admits as an
    // ordinary identifier and semver ranks alphanumerically.
    private static bool IsNumericIdentifier(string identifier)
    {
        if (identifier.Length == 0) return false;
        if (identifier.Length > 1 && identifier[0] == '0') return false;

        foreach (var c in identifier)
        {
            if (c is < '0' or > '9') return false;
        }

        return true;
    }

    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        var shared = Math.Min(a.Length, b.Length);

        for (var i = 0; i < shared; i++)
        {
            var aNumeric = IsNumericIdentifier(a[i]);
            var bNumeric = IsNumericIdentifier(b[i]);

            int result;
            if (aNumeric && bNumeric)
            {
                // Compared by digit count then ordinally — no leading zeros are possible
                // here, so this is exact numeric ordering with no overflow at any length.
                result = a[i].Length != b[i].Length
                    ? a[i].Length.CompareTo(b[i].Length)
                    : string.CompareOrdinal(a[i], b[i]);
            }
            else if (aNumeric) result = -1;               // numeric ranks BELOW alphanumeric
            else if (bNumeric) result = 1;
            else result = string.CompareOrdinal(a[i], b[i]);

            if (result != 0) return Math.Sign(result);
        }

        // All shared identifiers equal: the longer list wins (8.0.1-pre.1 > 8.0.1-pre).
        return a.Length.CompareTo(b.Length);
    }

    public bool Equals(SemanticVersion? other) => CompareTo(other) == 0 && other is not null;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    /// <summary>The precedence-relevant version. Never includes build metadata — dialogs show this.</summary>
    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public static bool operator >(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) > 0;
    public static bool operator <(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) < 0;
    public static bool operator >=(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) >= 0;
    public static bool operator <=(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) <= 0;
    public static bool operator ==(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) == 0;
    public static bool operator !=(SemanticVersion? a, SemanticVersion? b) => Compare(a, b) != 0;

    private static int Compare(SemanticVersion? a, SemanticVersion? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        return a.CompareTo(b);
    }
}
