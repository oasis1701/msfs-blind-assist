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
            .Select(c => new { Candidate = c, Version = SemanticVersion.TryParse(c.TagName) })
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
