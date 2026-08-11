using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>One GitHub release, flattened out of the API JSON so the selector stays pure.</summary>
public sealed record ReleaseCandidate(
    string TagName,
    string? Name,
    string? Body,
    bool IsPrerelease,
    bool IsDraft,
    string? ZipDownloadUrl);

/// <summary>What the update check concluded.</summary>
public enum UpdateVerdict
{
    /// <summary>Nothing usable was found. Distinct from UpToDate — see the tests.</summary>
    NoCandidate,

    /// <summary>The best candidate is the version already running.</summary>
    UpToDate,

    /// <summary>The best candidate is newer than the version running.</summary>
    UpdateAvailable,

    /// <summary>
    /// The best candidate is OLDER than the version running. Reached when a user on a
    /// preview build switches back to the release channel; offered as an explicit,
    /// clearly-labelled downgrade rather than silently doing nothing.
    /// </summary>
    DowngradeAvailable
}

public sealed record UpdateSelection(UpdateVerdict Verdict, ReleaseCandidate? Release, SemanticVersion? Version);

/// <summary>
/// Picks which release the update check should offer. Pure — takes an already-parsed list
/// so no HTTP is involved and every rule below is directly testable.
/// </summary>
public static class UpdateCandidateSelector
{
    /// <summary>
    /// The prefix `preview.yml` puts in front of the version in the rolling pre-release's
    /// NAME. This string is a CONTRACT between that workflow and this class — change one
    /// and you must change the other, or the preview channel silently goes inert again.
    /// </summary>
    internal const string PreviewNamePrefix = "Preview build ";

    /// <summary>
    /// A candidate's version, taken from its tag when the tag carries one, and otherwise
    /// from its name.
    ///
    /// The rolling preview's tag is the FIXED string "preview": one tag is force-moved on
    /// every merge so a single pre-release is updated in place, and a tag cannot both stay
    /// fixed and carry a changing version. Its version therefore travels in the release
    /// name instead ("Preview build 8.0.1-pre.7"). Reading only the tag — which is what
    /// this class did originally — made every preview unparseable, so it was filtered out
    /// as a malformed candidate and the Preview channel could never offer anything.
    ///
    /// The prefix is matched EXACTLY rather than, say, taking the last whitespace-separated
    /// token: a loose rule would happily read a version out of a hand-titled release that
    /// was never meant to be one.
    /// </summary>
    internal static SemanticVersion? ResolveVersion(ReleaseCandidate candidate)
    {
        var fromTag = SemanticVersion.TryParse(candidate.TagName);
        if (fromTag is not null) return fromTag;

        var name = candidate.Name;
        if (name is null || !name.StartsWith(PreviewNamePrefix, StringComparison.Ordinal)) return null;

        return SemanticVersion.TryParse(name[PreviewNamePrefix.Length..].Trim());
    }

    public static UpdateSelection Select(
        IEnumerable<ReleaseCandidate> candidates,
        UpdateChannel channel,
        SemanticVersion? current)
    {
        var best = candidates
            .Where(c => !c.IsDraft)
            // Release channel takes releases only. Preview channel takes EVERYTHING and
            // lets the version comparison decide — which is what makes it a superset and
            // removes any need to special-case a missing preview.
            .Where(c => channel == UpdateChannel.Preview || !c.IsPrerelease)
            .Select(c => new { Candidate = c, Version = ResolveVersion(c) })
            // A tag nobody can parse is skipped, never fatal: one malformed tag must not
            // blind the updater to every other release.
            .Where(x => x.Version is not null)
            .OrderByDescending(x => x.Version!)
            .FirstOrDefault();

        if (best is null) return new UpdateSelection(UpdateVerdict.NoCandidate, null, null);

        if (current is null)
        {
            return new UpdateSelection(UpdateVerdict.UpdateAvailable, best.Candidate, best.Version);
        }

        var comparison = best.Version!.CompareTo(current);
        var verdict = comparison switch
        {
            > 0 => UpdateVerdict.UpdateAvailable,
            0 => UpdateVerdict.UpToDate,
            _ => UpdateVerdict.DowngradeAvailable
        };

        return new UpdateSelection(verdict, best.Candidate, best.Version);
    }
}
