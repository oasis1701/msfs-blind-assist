using System;
using System.Globalization;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="UpdateCandidateSelector.ResolveVersion"/> exercised directly (via
/// InternalsVisibleTo; see Properties/InternalsVisibleTo.cs).
///
/// These exist because testing it only THROUGH <c>Select</c> cannot reach most of its
/// branches: <c>Select</c>'s channel filter drops a pre-release before version resolution
/// runs, and its null-version filter discards the candidate afterwards, so several
/// outcomes are indistinguishable from the outside. Three of the tests added alongside the
/// fix passed identically against the pre-fix one-line implementation for exactly that
/// reason — they were filtered out before reaching the code they meant to pin.
///
/// The branch that matters most is the FIRST one: tag before name. <c>Select</c> can never
/// show it, because a candidate whose tag parses never reveals whether the name was
/// consulted.
/// </summary>
public class UpdateCandidateResolveVersionTests
{
    private static ReleaseCandidate Candidate(string tagName, string? name) =>
        new(tagName, name, "notes", IsPrerelease: true, IsDraft: false, ZipDownloadUrl: "https://example/z.zip");

    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheTagWins_WhenBothTagAndNameCarryAVersion()
    {
        // The precedence rule, and the one branch Select can never expose: a candidate
        // whose tag parses never reveals whether the name was consulted. Flip the order in
        // ResolveVersion and only this test notices.
        var candidate = Candidate("v8.0.0", "Preview build 9.9.9");

        Assert.Equal("8.0.0", UpdateCandidateSelector.ResolveVersion(candidate)!.ToString());
    }

    [Fact]
    public void AnUnparseableName_NeverSpoilsAParseableTag()
    {
        var candidate = Candidate("v8.0.0", "Some hand-written release title");

        Assert.Equal("8.0.0", UpdateCandidateSelector.ResolveVersion(candidate)!.ToString());
    }

    [Fact]
    public void FallsBackToTheName_WhenTheTagIsNotAVersion()
    {
        // The real rolling preview: preview.yml force-moves the FIXED tag "preview" on
        // every merge, so the version can only travel in the name.
        var candidate = Candidate("preview", "Preview build 8.0.1-pre.7");

        var resolved = UpdateCandidateSelector.ResolveVersion(candidate);

        Assert.Equal("8.0.1-pre.7", resolved!.ToString());
        Assert.True(resolved.IsPreRelease);
    }

    [Fact]
    public void TheNameFallbackRoundTripsWhatPreviewYmlActuallyPublishes()
    {
        // Built from the constant rather than a copied literal, so the cross-file contract
        // breaks loudly at BOTH ends: preview.yml writes
        //   name: Preview build ${{ steps.version.outputs.VERSION }}
        // and this reconstructs that exact string from PreviewNamePrefix.
        const string version = "8.0.1-pre.61";
        var publishedName = UpdateCandidateSelector.PreviewNamePrefix + version;

        Assert.Equal("Preview build 8.0.1-pre.61", publishedName);
        Assert.Equal(version, UpdateCandidateSelector.ResolveVersion(Candidate("preview", publishedName))!.ToString());
    }

    [Fact]
    public void ANullName_ResolvesToNullWithoutThrowing()
    {
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", null)));
    }

    [Fact]
    public void ANameThatIsExactlyThePrefix_ResolvesToNullWithoutThrowing()
    {
        // The slice past the prefix is empty here; it must not throw, and an empty string
        // is not a version.
        Assert.Null(UpdateCandidateSelector.ResolveVersion(
            Candidate("preview", UpdateCandidateSelector.PreviewNamePrefix)));
    }

    [Fact]
    public void GarbageAfterThePrefix_ResolvesToNull()
    {
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", "Preview build not-a-version")));
    }

    [Fact]
    public void WhitespaceAroundTheVersion_IsTolerated()
    {
        Assert.Equal("8.0.1-pre.7",
            UpdateCandidateSelector.ResolveVersion(Candidate("preview", "Preview build   8.0.1-pre.7  "))!.ToString());
    }

    [Fact]
    public void TextBeforeThePrefix_ResolvesToNull()
    {
        // StartsWith is anchored, deliberately. A title that merely CONTAINS the prefix was
        // not written by preview.yml and must not be mined for a version.
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", "Nightly Preview build 9.9.9")));
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", " Preview build 9.9.9")));
    }

    [Fact]
    public void ThePrefixMatchIsCaseSensitive()
    {
        // Ordinal, not OrdinalIgnoreCase. Loosening this widens what counts as a preview
        // title for no gain — preview.yml emits exactly one casing.
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", "preview build 8.0.1-pre.7")));
        Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", "PREVIEW BUILD 8.0.1-pre.7")));
    }

    [Fact]
    public void ThePrefixMatchIsCultureIndependent()
    {
        // The prefix contains "build", and tr-TR folds the letter i to the dotless ı. A
        // culture-sensitive comparison here would stop matching for Turkish-locale users
        // and silently take their preview channel inert — the same class of bug the
        // SayIntentions integration already paid for once.
        UnderCulture("tr-TR", () =>
        {
            Assert.Equal("8.0.1-pre.7",
                UpdateCandidateSelector.ResolveVersion(Candidate("preview", "Preview build 8.0.1-pre.7"))!.ToString());
            Assert.Null(UpdateCandidateSelector.ResolveVersion(Candidate("preview", "PREVIEW BUILD 8.0.1-pre.7")));
        });
    }

    [Fact]
    public void AVersionShapedTagStillResolvesFromTheTagAlone_WithNoName()
    {
        Assert.Equal("8.1.0", UpdateCandidateSelector.ResolveVersion(Candidate("v8.1.0", null))!.ToString());
    }
}
