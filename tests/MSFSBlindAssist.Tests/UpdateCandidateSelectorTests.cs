using System.Collections.Generic;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Channel selection, pinned. The preview channel is deliberately a SUPERSET of the
/// release channel: it takes the highest version among pre-releases AND releases, so the
/// "preview release is absent" case (right after a release is cut and the rolling preview
/// is retired) needs no branch of its own — it falls out of taking the highest.
/// </summary>
public class UpdateCandidateSelectorTests
{
    private static ReleaseCandidate Release(string tag) =>
        new(tag, tag, "notes", IsPrerelease: false, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

    private static ReleaseCandidate Preview(string tag) =>
        new(tag, tag, "notes", IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

    private static SemanticVersion V(string text) => SemanticVersion.TryParse(text)!;

    [Fact]
    public void ReleaseChannel_SkipsPreReleases()
    {
        var candidates = new List<ReleaseCandidate> { Preview("8.0.1-pre.42"), Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("7.0.0"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("v8.0.0", result.Release!.TagName);
    }

    [Fact]
    public void PreviewChannel_TakesHighestOfBothKinds()
    {
        var candidates = new List<ReleaseCandidate> { Preview("8.0.1-pre.42"), Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Preview, V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("8.0.1-pre.42", result.Release!.TagName);
    }

    [Fact]
    public void PreviewChannel_FallsBackToReleaseWhenPreviewAbsent()
    {
        // The state immediately after release.yml retires the rolling preview.
        var candidates = new List<ReleaseCandidate> { Release("v8.1.0"), Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Preview, V("8.0.1-pre.42"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("v8.1.0", result.Release!.TagName);
    }

    [Fact]
    public void PreviewChannel_PrefersANewerReleaseOverAStalePreview()
    {
        // A stale preview built against v8.0.0 must lose to the freshly cut v8.1.0.
        var candidates = new List<ReleaseCandidate> { Preview("8.0.1-pre.42"), Release("v8.1.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Preview, V("8.0.1-pre.42"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("v8.1.0", result.Release!.TagName);
    }

    [Fact]
    public void ReleaseChannel_ReportsDowngradeWhenRunningANewerPreview()
    {
        var candidates = new List<ReleaseCandidate> { Preview("8.0.1-pre.42"), Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("8.0.1-pre.42"));

        Assert.Equal(UpdateVerdict.DowngradeAvailable, result.Verdict);
        Assert.Equal("v8.0.0", result.Release!.TagName);
    }

    [Fact]
    public void ReportsUpToDateWhenChosenEqualsCurrent()
    {
        var candidates = new List<ReleaseCandidate> { Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpToDate, result.Verdict);
    }

    [Fact]
    public void BuildMetadataOnCurrentVersion_DoesNotMakeItLookOutdated()
    {
        // The running app reports 8.0.0+<sha>; the tag is plain v8.0.0. Same version.
        var candidates = new List<ReleaseCandidate> { Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(
            candidates, UpdateChannel.Release, V("8.0.0+4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f"));

        Assert.Equal(UpdateVerdict.UpToDate, result.Verdict);
    }

    [Fact]
    public void DraftsAreExcluded()
    {
        var candidates = new List<ReleaseCandidate>
        {
            new("v9.0.0", "v9.0.0", "notes", IsPrerelease: false, IsDraft: true, ZipDownloadUrl: "https://example/z.zip"),
            Release("v8.0.0")
        };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("7.0.0"));

        Assert.Equal("v8.0.0", result.Release!.TagName);
    }

    [Fact]
    public void UnparseableTagIsSkipped_NotFatal()
    {
        // One malformed tag must not blind the updater.
        var candidates = new List<ReleaseCandidate> { Release("nightly"), Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("7.0.0"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("v8.0.0", result.Release!.TagName);
    }

    [Fact]
    public void EmptyList_ReportsNoCandidate_NotUpToDate()
    {
        // Distinct from UpToDate so the manual path never claims "you are on the latest
        // version" when it in fact found nothing at all.
        var result = UpdateCandidateSelector.Select(new List<ReleaseCandidate>(), UpdateChannel.Release, V("8.0.0"));

        Assert.Equal(UpdateVerdict.NoCandidate, result.Verdict);
        Assert.Null(result.Release);
    }

    [Fact]
    public void AllTagsUnparseable_ReportsNoCandidate()
    {
        var candidates = new List<ReleaseCandidate> { Release("nightly"), Release("latest") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, V("8.0.0"));

        Assert.Equal(UpdateVerdict.NoCandidate, result.Verdict);
    }

    [Fact]
    public void UnknownCurrentVersion_OffersTheHighestCandidate()
    {
        var candidates = new List<ReleaseCandidate> { Release("v8.0.0") };

        var result = UpdateCandidateSelector.Select(candidates, UpdateChannel.Release, current: null);

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("v8.0.0", result.Release!.TagName);
    }

    [Fact]
    public void PreviewChannel_SelectsTheRollingPreview_WhoseTagIsNotAVersion()
    {
        // The literal strings preview.yml publishes: a FIXED tag "preview" (it is
        // force-moved on every merge, so it cannot carry a version) with the version in
        // the name. Reading only the tag made this candidate unparseable and the whole
        // Preview channel inert.
        var rollingPreview = new ReleaseCandidate(
            "preview", "Preview build 8.0.1-pre.7", "notes",
            IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

        var result = UpdateCandidateSelector.Select(
            new List<ReleaseCandidate> { rollingPreview, Release("v8.0.0") },
            UpdateChannel.Preview,
            V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpdateAvailable, result.Verdict);
        Assert.Equal("preview", result.Release!.TagName);
        Assert.Equal("8.0.1-pre.7", result.Version!.ToString());
    }

    [Fact]
    public void ReleaseChannel_StillExcludesTheRollingPreview()
    {
        var rollingPreview = new ReleaseCandidate(
            "preview", "Preview build 8.0.1-pre.7", "notes",
            IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

        var result = UpdateCandidateSelector.Select(
            new List<ReleaseCandidate> { rollingPreview, Release("v8.0.0") },
            UpdateChannel.Release,
            V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpToDate, result.Verdict);
    }

    [Fact]
    public void ANonVersionNameIsNotMinedForAVersion()
    {
        // A hand-titled release must not have a version read out of it by accident.
        var oddlyNamed = new ReleaseCandidate(
            "nightly", "Nightly 9.9.9 experimental", "notes",
            IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

        var result = UpdateCandidateSelector.Select(
            new List<ReleaseCandidate> { oddlyNamed, Release("v8.0.0") },
            UpdateChannel.Preview,
            V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpToDate, result.Verdict);
    }

    [Fact]
    public void APreviewNameWithGarbageAfterThePrefixIsSkipped()
    {
        var broken = new ReleaseCandidate(
            "preview", "Preview build not-a-version", "notes",
            IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

        var result = UpdateCandidateSelector.Select(
            new List<ReleaseCandidate> { broken, Release("v8.0.0") },
            UpdateChannel.Preview,
            V("8.0.0"));

        Assert.Equal(UpdateVerdict.UpToDate, result.Verdict);
    }
}
