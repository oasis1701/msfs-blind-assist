// Tests for Services/HelpLinks (the Help menu) and the bundled guide's file-name contract with
// tools/user-guide-publish.sh, which release.yml and preview.yml run to build the one-page guide next to the exe (GUIDE-3). A rename on either side alone would
// leave Help, User Guide opening the online guide in every release, with nothing failing.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class HelpLinksTests
{
    [Fact]
    public void ResolveGuide_opens_the_bundled_guide_when_it_is_there()
    {
        const string folder = @"C:\MSFSBA";
        string expected = Path.Combine(folder, HelpLinks.BundledGuideFileName);

        Assert.Equal((expected, true), HelpLinks.ResolveGuide(folder, path => path == expected));
    }

    [Fact]
    public void ResolveGuide_falls_back_to_the_online_guide()
        => Assert.Equal((HelpLinks.OnlineGuideUrl, false), HelpLinks.ResolveGuide("C:\\MSFSBA", _ => false));

    [Fact]
    public void The_publish_script_writes_the_guide_where_the_app_looks()
    {
        string script = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "tools", "user-guide-publish.sh"));

        Assert.Contains($"\"MSFSBlindAssist/bin/x64/Release/net10.0-windows/{HelpLinks.BundledGuideFileName}\"", script);
        Assert.Contains("one-page --guide guide", script);
    }

    [Theory]
    [InlineData("release.yml", "user-guide-publish.sh bundle release")]
    [InlineData("preview.yml", "user-guide-publish.sh bundle preview")]
    public void The_workflows_bundle_the_guide_with_the_publish_script(string workflow, string call)
    {
        string text = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "workflows", workflow));

        Assert.Contains(call, text);
        Assert.Contains("user-guide-publish.sh refresh-site", text);
    }

    [Fact]
    public void The_online_guide_is_the_one_the_one_page_guide_names()
        => Assert.Equal(GuideBuilder.OnePageBuilder.OnlineGuideUrl, HelpLinks.OnlineGuideUrl);

    [Fact]
    public void The_online_guide_is_on_the_website()
        => Assert.StartsWith(HelpLinks.WebsiteUrl, HelpLinks.OnlineGuideUrl);
}
