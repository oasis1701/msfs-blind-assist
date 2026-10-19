// Tests for the advisory "User guide line" PR check (CORE-19): GuideBuilder.UserGuideLine reads the line from a PR
// description, UserGuideImpact decides whether the check asks, and PullRequestCheck.Evaluate joins the two into the
// pass or fail and the message .github/workflows/user-guide-line.yml shows. All three are pure, so every case is a
// literal; messages are asserted loosely (Contains), since their wording is a UX detail.

using GuideBuilder;

namespace MSFSBlindAssist.Tests;

public class UserGuideLineTests
{
    [Theory]
    [InlineData("User guide: needed: Part 7, TFDi MD-11, Hotkeys", UserGuideAnswer.Needed)]
    [InlineData("User guide: Needed - Appendix D, Hotkey Reference", UserGuideAnswer.Needed)]
    [InlineData("User guide: none, because this restores the documented behavior", UserGuideAnswer.None)]
    [InlineData("User guide: None: CI only", UserGuideAnswer.None)]
    [InlineData("User guide: none because internal refactor", UserGuideAnswer.None)]
    [InlineData("User guide: none, because only the <nav> markup changed", UserGuideAnswer.None)]
    [InlineData("User guide: needed: the <kbd> examples in Part 2", UserGuideAnswer.Needed)]
    public void Parse_accepts_each_answer(string description, UserGuideAnswer expected)
    {
        UserGuideLineResult result = UserGuideLine.Parse(description);

        Assert.Equal(expected, result.Answer);
        Assert.True(result.Answered);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("- **User guide:** none, because CI only")]
    [InlineData("**User guide:** none, because CI only")]
    [InlineData("**User guide**: none, because CI only")]
    [InlineData("- __User guide__: none, because CI only")]
    [InlineData("  * User guide: none, because CI only")]
    [InlineData("user guide: none, because CI only")]
    [InlineData("USER GUIDE:none, because CI only")]
    public void Parse_tolerates_list_markers_bold_and_case(string description)
        => Assert.Equal(UserGuideAnswer.None, UserGuideLine.Parse(description).Answer);

    [Fact]
    public void Parse_returns_the_value_trimmed()
        => Assert.Equal("none, because CI only", UserGuideLine.Parse("User guide:   none, because CI only  \r\n").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## Summary\n\nFixes the thing.")]
    [InlineData("## User guide\n\nUser guide:\n")]
    [InlineData("User guide:   \r\n")]
    [InlineData("See the user guide: it explains this.")]
    public void Parse_reports_missing_when_no_line_says_anything(string? description)
    {
        UserGuideLineResult result = UserGuideLine.Parse(description);

        Assert.Equal(UserGuideAnswer.Missing, result.Answer);
        Assert.False(result.Answered);
    }

    [Fact]
    public void Parse_ignores_the_examples_inside_an_html_comment()
    {
        const string description = "## User guide\n\n<!--\n  User guide: none, because <reason>\n  User guide: needed: Part 4" +
            "\n-->\n\nUser guide:\n";

        Assert.Equal(UserGuideAnswer.Missing, UserGuideLine.Parse(description).Answer);
    }

    [Fact]
    public void Parse_treats_an_unclosed_comment_as_hiding_the_rest()
        => Assert.Equal(UserGuideAnswer.Missing, UserGuideLine.Parse("<!-- draft\nUser guide: none, because CI only").Answer);

    [Fact]
    public void Parse_takes_an_answer_typed_on_a_later_line_when_the_template_line_was_left_empty()
        => Assert.Equal(UserGuideAnswer.None, UserGuideLine.Parse("User guide:\r\n\r\nUser guide: none, because CI only\r\n").Answer);

    [Fact]
    public void Parse_reads_crlf_descriptions_as_github_sends_them()
    {
        UserGuideLineResult result = UserGuideLine.Parse("## User guide\r\n\r\nUser guide: needed: Part 4, Taxi\r\n\r\n## Test plan\r\n");

        Assert.Equal(UserGuideAnswer.Needed, result.Answer);
        Assert.Equal("needed: Part 4, Taxi", result.Value);
    }

    [Theory]
    [InlineData("User guide: none, because <reason>", "<reason>")]
    [InlineData("User guide: needed: <chapter and section>", "<chapter and section>")]
    [InlineData("User guide: none, because <why the guide is still right>", "<why the guide is still right>")]
    [InlineData("User guide: needed: <Chapter>", "<Chapter>")]
    public void Parse_rejects_a_placeholder_left_in(string description, string placeholder)
    {
        UserGuideLineResult result = UserGuideLine.Parse(description);

        Assert.Equal(UserGuideAnswer.Invalid, result.Answer);
        Assert.Contains(placeholder, result.Error);
    }

    [Theory]
    [InlineData("User guide: none", "no reason")]
    [InlineData("User guide: none, because", "no reason")]
    [InlineData("User guide: needed", "not where")]
    [InlineData("User guide: needed:", "not where")]
    [InlineData("User guide: N/A", "neither form")]
    [InlineData("User guide: TBD", "neither form")]
    [InlineData("User guide: <https://github.com/oasis1701/msfs-blind-assist-site/pull/12>", "neither form")]
    public void Parse_rejects_an_incomplete_or_unknown_answer(string description, string because)
    {
        UserGuideLineResult result = UserGuideLine.Parse(description);

        Assert.Equal(UserGuideAnswer.Invalid, result.Answer);
        Assert.False(result.Answered);
        Assert.Contains(because, result.Error);
    }

    [Fact]
    public void The_pr_template_left_unfilled_reads_as_missing()
    {
        string template = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "pull_request_template.md"));

        Assert.Contains("User guide:", template);
        Assert.Equal(UserGuideAnswer.Missing, UserGuideLine.Parse(template).Answer);
    }

    [Fact]
    public void The_pr_template_with_its_line_filled_in_reads_as_answered()
    {
        // A Windows checkout may hold the template with CRLF endings; GitHub can send either.
        string template = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "pull_request_template.md"))
            .Replace("\r\n", "\n");
        string filled = template.Replace("\nUser guide:\n", "\nUser guide: none, because CI only\n");

        Assert.NotEqual(template, filled);
        Assert.Equal(UserGuideAnswer.None, UserGuideLine.Parse(filled).Answer);
    }

    [Theory]
    [InlineData("changelog.d/320-volanta-friends.feature.md", "adds changelog.d/320-volanta-friends.feature.md")]
    [InlineData("changelog.d/320-a300.aircraft.md", "adds changelog.d/320-a300.aircraft.md")]
    [InlineData("changelog.d/320-tone-fade.improvement.md", "adds changelog.d/320-tone-fade.improvement.md")]
    public void Impact_asks_when_a_guide_relevant_fragment_is_added(string added, string reason)
        => Assert.Equal(new[] { reason }, UserGuideImpact.Reasons(new[] { added }, new[] { added }));

    [Theory]
    [InlineData("changelog.d/320-autobrake.fix.md")]
    [InlineData("changelog.d/320-ci.internal.md")]
    [InlineData("changelog.d/README.md")]
    [InlineData("docs/changelog.d/320-x.feature.md")]
    [InlineData("MSFSBlindAssist/MainForm.cs")]
    public void Impact_does_not_ask_for_other_added_files(string added)
        => Assert.Empty(UserGuideImpact.Reasons(new[] { added }, new[] { added }));

    [Fact]
    public void Impact_does_not_ask_when_an_older_feature_fragment_is_only_edited()
        => Assert.Empty(UserGuideImpact.Reasons(Array.Empty<string>(), new[] { "changelog.d/301-volanta.feature.md" }));

    [Theory]
    [InlineData("MSFSBlindAssist/HotkeyGuides/TFDi_MD11_Hotkeys.txt")]
    [InlineData(@"MSFSBlindAssist\HotkeyGuides\PMDG_737_Hotkeys.txt")]
    public void Impact_asks_when_a_hotkey_guide_changes(string changed)
        => Assert.Equal(new[] { "changes " + changed.Replace('\\', '/') }, UserGuideImpact.Reasons(Array.Empty<string>(), new[] { changed }));

    [Fact]
    public void Impact_lists_every_reason_once()
    {
        IReadOnlyList<string> reasons = UserGuideImpact.Reasons(
            new[] { "changelog.d/320-a.feature.md", "changelog.d/320-a.feature.md", "" },
            new[] { "changelog.d/320-a.feature.md", "MSFSBlindAssist/HotkeyGuides/HS787_Hotkeys.txt" });

        Assert.Equal(new[] { "adds changelog.d/320-a.feature.md", "changes MSFSBlindAssist/HotkeyGuides/HS787_Hotkeys.txt" }, reasons);
    }

    [Fact]
    public void Impact_finds_the_guide_pages_a_pr_touches()
        => Assert.Equal(new[] { "guide/taxi-guidance.md" },
            UserGuideImpact.GuideChanges(new[] { "MSFSBlindAssist/MainForm.cs", @"guide\taxi-guidance.md", "docs/guide.md" }));

    [Fact]
    public void Evaluate_fails_an_asked_pr_with_no_guide_change_and_no_line()
    {
        (bool pass, string message) = PullRequestCheck.Evaluate("## Summary\n\nAdds friends.",
            new[] { "changelog.d/320-friends.feature.md" }, new[] { "changelog.d/320-friends.feature.md" });

        Assert.False(pass);
        Assert.Contains("adds changelog.d/320-friends.feature.md", message);
        Assert.Contains("guide/", message);
        Assert.Contains("User guide: needed:", message);
        Assert.Contains("User guide: none, because", message);
        Assert.Contains("advisory", message);
    }

    [Fact]
    public void Evaluate_passes_an_asked_pr_that_updates_the_guide()
    {
        (bool pass, string message) = PullRequestCheck.Evaluate("",
            new[] { "changelog.d/320-friends.feature.md" }, new[] { "changelog.d/320-friends.feature.md", "guide/networks-and-integrations.md" });

        Assert.True(pass);
        Assert.Contains("guide/networks-and-integrations.md", message);
    }

    [Fact]
    public void Evaluate_fails_an_asked_pr_with_a_bad_line_and_says_what_is_wrong()
    {
        (bool pass, string message) = PullRequestCheck.Evaluate("User guide: none",
            new[] { "changelog.d/320-friends.feature.md" }, Array.Empty<string>());

        Assert.False(pass);
        Assert.Contains("no reason", message);
    }

    [Fact]
    public void Evaluate_passes_an_asked_pr_that_answers()
    {
        (bool pass, string message) = PullRequestCheck.Evaluate("User guide: needed: Part 6, Volanta",
            Array.Empty<string>(), new[] { "MSFSBlindAssist/HotkeyGuides/HS787_Hotkeys.txt" });

        Assert.True(pass);
        Assert.Contains("needed: Part 6, Volanta", message);
        Assert.Contains("changes MSFSBlindAssist/HotkeyGuides/HS787_Hotkeys.txt", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("User guide: TBD")]
    [InlineData("User guide: none, because CI only")]
    public void Evaluate_passes_a_pr_it_does_not_ask_whatever_its_line_says(string description)
    {
        (bool pass, string message) = PullRequestCheck.Evaluate(description,
            new[] { "changelog.d/320-autobrake.fix.md" }, new[] { "changelog.d/320-autobrake.fix.md", "MSFSBlindAssist/MainForm.cs" });

        Assert.True(pass);
        Assert.Contains("Not asked", message);
    }

    [Fact]
    public void Evaluate_still_points_out_a_bad_line_when_not_asked()
        => Assert.Contains("neither form", PullRequestCheck.Evaluate("User guide: TBD", Array.Empty<string>(), Array.Empty<string>()).Message);
}
