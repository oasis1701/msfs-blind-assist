using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Semver 2.0.0 precedence, pinned. Two rules here are load-bearing for the update
/// channels and are easy to get backwards:
///   - a release outranks a pre-release of the SAME version (8.0.1 > 8.0.1-pre.42), which
///     is what lets a real release supersede every preview built against it;
///   - numeric pre-release identifiers compare NUMERICALLY (pre.10 > pre.9). Lexical
///     comparison inverts that and permanently strands preview users on build 9.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("8.0.1", 8, 0, 1)]
    [InlineData("v8.0.1", 8, 0, 1)]
    [InlineData("V8.0.1", 8, 0, 1)]
    [InlineData("8.0", 8, 0, 0)]          // patch defaults to 0
    [InlineData("0.0.0", 0, 0, 0)]        // the local dev-build marker
    public void TryParse_ReadsCoreVersion(string text, int major, int minor, int patch)
    {
        var v = SemanticVersion.TryParse(text);
        Assert.NotNull(v);
        Assert.Equal(major, v!.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
    }

    [Fact]
    public void TryParse_SplitsPreReleaseAndBuildMetadata()
    {
        // Exactly the shape CI produces: -p:Version=8.0.1-pre.42 plus SourceLink's sha.
        var v = SemanticVersion.TryParse("8.0.1-pre.42+4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f");
        Assert.NotNull(v);
        Assert.Equal(8, v!.Major);
        Assert.Equal(0, v.Minor);
        Assert.Equal(1, v.Patch);
        Assert.Equal("pre.42", v.PreRelease);
        Assert.Equal("4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f", v.BuildMetadata);
        Assert.True(v.IsPreRelease);
    }

    [Fact]
    public void TryParse_HandlesBuildMetadataWithoutPreRelease()
    {
        // What a local dev build reports today: 0.0.0+<sha>.
        var v = SemanticVersion.TryParse("0.0.0+4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f");
        Assert.NotNull(v);
        Assert.Null(v!.PreRelease);
        Assert.False(v.IsPreRelease);
        Assert.Equal("4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f", v.BuildMetadata);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    [InlineData("8")]
    [InlineData("8.0.1.2.3")]
    [InlineData("preview")]
    public void TryParse_ReturnsNullOnGarbage(string? text)
    {
        Assert.Null(SemanticVersion.TryParse(text));
    }

    [Fact]
    public void Release_OutranksPreReleaseOfSameVersion()
    {
        var release = SemanticVersion.TryParse("8.0.1")!;
        var preview = SemanticVersion.TryParse("8.0.1-pre.42")!;
        Assert.True(release > preview);
        Assert.True(preview < release);
    }

    [Fact]
    public void NumericPreReleaseIdentifiers_CompareNumerically()
    {
        var nine = SemanticVersion.TryParse("8.0.1-pre.9")!;
        var ten = SemanticVersion.TryParse("8.0.1-pre.10")!;
        Assert.True(ten > nine);
    }

    [Fact]
    public void NumericPreReleaseIdentifier_RanksBelowAlphanumeric()
    {
        var numeric = SemanticVersion.TryParse("8.0.1-1")!;
        var alpha = SemanticVersion.TryParse("8.0.1-alpha")!;
        Assert.True(alpha > numeric);
    }

    [Fact]
    public void LongerPreReleaseIdentifierList_OutranksShorterPrefix()
    {
        var shorter = SemanticVersion.TryParse("8.0.1-pre")!;
        var longer = SemanticVersion.TryParse("8.0.1-pre.1")!;
        Assert.True(longer > shorter);
    }

    [Fact]
    public void BuildMetadata_IsIgnoredForPrecedence()
    {
        var a = SemanticVersion.TryParse("8.0.1-pre.42+aaaaaaa")!;
        var b = SemanticVersion.TryParse("8.0.1-pre.42+bbbbbbb")!;
        Assert.Equal(0, a.CompareTo(b));
        Assert.True(a == b);
    }

    [Fact]
    public void CoreVersion_ComparesNumerically_NotLexically()
    {
        Assert.True(SemanticVersion.TryParse("8.0.10")! > SemanticVersion.TryParse("8.0.9")!);
        Assert.True(SemanticVersion.TryParse("8.1.0")! > SemanticVersion.TryParse("8.0.99")!);
        Assert.True(SemanticVersion.TryParse("10.0.0")! > SemanticVersion.TryParse("9.9.9")!);
    }

    [Fact]
    public void DevBuildMarker_SortsBelowEveryRelease()
    {
        Assert.True(SemanticVersion.TryParse("8.0.0")! > SemanticVersion.TryParse("0.0.0")!);
        Assert.True(SemanticVersion.TryParse("0.0.1-pre.1")! > SemanticVersion.TryParse("0.0.0")!);
    }

    [Fact]
    public void PreviewSequence_OrdersAsCiProducesIt()
    {
        // The real progression: v8.0.0 released, previews accumulate, v8.1.0 released,
        // previews resume from a RESET counter — which is safe only because the base rose.
        var chain = new[]
        {
            "8.0.0", "8.0.1-pre.1", "8.0.1-pre.2", "8.0.1-pre.42", "8.1.0", "8.1.1-pre.1"
        };

        for (var i = 1; i < chain.Length; i++)
        {
            var lower = SemanticVersion.TryParse(chain[i - 1])!;
            var higher = SemanticVersion.TryParse(chain[i])!;
            Assert.True(higher > lower, $"{chain[i]} should outrank {chain[i - 1]}");
        }
    }

    [Fact]
    public void ToString_OmitsBuildMetadata()
    {
        var v = SemanticVersion.TryParse("8.0.1-pre.42+4f7e7ba")!;
        Assert.Equal("8.0.1-pre.42", v.ToString());
        Assert.Equal("8.0.0", SemanticVersion.TryParse("v8.0.0")!.ToString());
    }
}
