using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// AppVersion.Describe is the display formatter used by the About dialog and the Updates
/// settings tab. The full 40-character SourceLink sha is unusable read aloud by a screen
/// reader, so it is shortened to 7 characters — enough to identify the commit in a bug
/// report, which is the whole reason a preview user needs it.
///
/// AppVersion.Current reads a reflection attribute off MSFSBlindAssist.dll (GetExecutingAssembly
/// inside AppVersion), which carries the commit even under `dotnet test`; its value changes with
/// every build, so only its shape is pinned, by The_build_carries_its_commit.
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void The_build_carries_its_commit()
    {
        Assert.NotNull(AppVersion.Current);
        Assert.True(
            System.Text.RegularExpressions.Regex.IsMatch(AppVersion.Current!.BuildMetadata ?? "", "^[0-9a-f]{40}$"),
            "Every build must carry its full commit hash (SourceLink): the \"Sim-tested on:\" line in the PR " +
            "template and CORE-5 depend on Help, About showing it.");
    }

    [Fact]
    public void Describe_AppendsShortShaWhenBuildMetadataPresent()
    {
        var v = SemanticVersion.TryParse("8.0.1-pre.42+4f7e7ba5b91d3722ecae44de4f0dfd838f427f9f");
        Assert.Equal("8.0.1-pre.42 (build 4f7e7ba)", AppVersion.Describe(v));
    }

    [Fact]
    public void Describe_OmitsBuildSuffixWhenNoMetadata()
    {
        Assert.Equal("8.0.0", AppVersion.Describe(SemanticVersion.TryParse("v8.0.0")));
    }

    [Fact]
    public void Describe_HandlesShaShorterThanSevenCharacters()
    {
        Assert.Equal("8.0.0 (build abc)", AppVersion.Describe(SemanticVersion.TryParse("8.0.0+abc")));
    }

    [Fact]
    public void Describe_ReturnsUnknownForNull()
    {
        Assert.Equal("unknown", AppVersion.Describe(null));
    }
}
