// Tests for the advisory PR scope check (CORE-22 to CORE-25, plus CORE-5's "Sim-tested on:" line):
// GuideBuilder.ScopeLines reads the description's lines, UnifiedDiff reads the added lines of a diff,
// and ScopeCheck.Evaluate joins them into the pass or fail and the message the workflow shows. All of it is pure, so
// every case is a literal; messages are asserted loosely (Contains). Every tool, test class, branch and commit hash
// below is made up. A made-up tool path or test class name is built by concatenation, so that the check run on this
// very file reads no dangling name in it.

using GuideBuilder;

namespace MSFSBlindAssist.Tests;

public class PrScopeCheckTests
{
    private const string Alpha = "ALPHA_JET";
    private const string Beta = "BETA_PROP";
    private const string Gamma = "GAMMA_JET";

    private static readonly string[] Commits =
    [
        "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678",
        "b2c3d4e5f60718293a4b5c6d7e8f901234567890",
    ];

    private static string Tool(string name) => "tools/" + name;

    private static string TestsOf(string name) => name + "Tests";

    private static string Switch(params string[] codes)
        => string.Join("\n", codes.Select(c => $"    \"{c}\" => new {c.Replace("_", "")}Definition(),")) +
           "\n    _ => new AlphaJetDefinition(),";

    private static ScopeCheckInput Input(
        string description = "",
        string baseBranch = "main",
        string[]? addedPaths = null,
        AddedLine[]? addedLines = null,
        string[]? tree = null,
        string[]? testClasses = null,
        string switchBefore = "",
        string switchAfter = "",
        int[]? stackedWith = null,
        string[]? prCommits = null,
        string[]? baseNames = null)
        => new(description, baseBranch, addedPaths ?? [], addedLines ?? [], tree ?? [], testClasses ?? [],
            switchBefore, switchAfter, stackedWith ?? [], prCommits ?? Commits, baseNames ?? []);

    // ---- ScopeLines -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Variants: PMDG 737 series", "PMDG 737 series", null)]
    [InlineData("- **Variants:** X", "X", null)]
    [InlineData("**Planned in**: docs/parent/scope.md", null, "docs/parent/scope.md")]
    [InlineData("planned in: the private repo's plan", null, "the private repo's plan")]
    [InlineData("Intro\r\nVariants:   Gamma family  \r\nPlanned in: docs/a.md\r\n", "Gamma family", "docs/a.md")]
    public void ScopeLines_reads_variants_and_planned_in(string description, string? variants, string? plannedIn)
    {
        ScopeLinesResult result = ScopeLines.Parse(description);

        Assert.Equal(variants, result.Variants);
        Assert.Equal(plannedIn, result.PlannedIn);
    }

    [Fact]
    public void ScopeLines_ignores_comments_and_blank_lines()
    {
        ScopeLinesResult blank = ScopeLines.Parse(
            "<!--\n  Variants: <family>\n  Planned in: <link>\n-->\nVariants:\nPlanned in:   ");
        Assert.Null(blank.Variants);
        Assert.Null(blank.PlannedIn);

        ScopeLinesResult placeholders = ScopeLines.Parse("Variants: <family>\nPlanned in: <link>");
        Assert.Null(placeholders.Variants);
        Assert.Null(placeholders.PlannedIn);

        ScopeLinesResult autolink = ScopeLines.Parse("Planned in: <https://example.com/parent/scope.md>");
        Assert.Equal("https://example.com/parent/scope.md", autolink.PlannedIn);
    }

    [Fact]
    public void ScopeLines_reads_every_sim_tested_on_line()
    {
        ScopeLinesResult result = ScopeLines.Parse(
            "Sim-tested on: a1b2c3d\r\n- **Sim-tested on:** v0.0.0 (build b2c3d4e), c3d4e5f\nSim tested on: d4e5f60");
        Assert.Equal(["a1b2c3d", "v0.0.0 (build b2c3d4e), c3d4e5f", "d4e5f60"], result.SimTestedOn);

        ScopeLinesResult none = ScopeLines.Parse(
            "<!--\n  Sim-tested on: <build from Help, About>\n-->\nSim-tested on:\n");
        Assert.Empty(none.SimTestedOn);
    }

    [Fact]
    public void Pr_template_answers_nothing_by_itself()
    {
        // The template carries all three labels, but its examples sit inside comments and its visible
        // "Sim-tested on:" line is empty, so a PR opened from it unchanged has answered none of them.
        string template = File.ReadAllText(
            Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "pull_request_template.md"));

        ScopeLinesResult result = ScopeLines.Parse(template);

        Assert.Null(result.Variants);
        Assert.Null(result.PlannedIn);
        Assert.Empty(result.SimTestedOn);
        Assert.Contains(ScopeLines.VariantsLabel + ":", template);
        Assert.Contains(ScopeLines.PlannedInLabel + ":", template);
        Assert.Contains(ScopeLines.SimTestedOnLabel + ":", template);
    }

    // ---- UnifiedDiff ------------------------------------------------------------------------------------------

    [Fact]
    public void UnifiedDiff_reads_added_lines_per_file()
    {
        const string diff = """
            diff --git a/a.cs b/a.cs
            --- a/a.cs
            +++ b/a.cs
            @@ -1,2 +1,2 @@
            +x
            -y
            +++i;
            diff --git a/old.cs b/old.cs
            deleted file mode 100644
            --- a/old.cs
            +++ /dev/null
            @@ -1 +0,0 @@
            -gone
            diff --git a/b.md b/b.md
            new file mode 100644
            --- /dev/null
            +++ b/b.md
            @@ -0,0 +1 @@
            +z
            """;

        IReadOnlyList<AddedLine> lines = UnifiedDiff.AddedLines(diff);

        Assert.Equal([new AddedLine("a.cs", "x"), new AddedLine("a.cs", "++i;"), new AddedLine("b.md", "z")], lines);
    }

    // ---- ScopeCheck -------------------------------------------------------------------------------------------

    [Fact]
    public void AircraftCodes_reads_the_switch_arms()
    {
        Assert.Equal([Alpha, Beta], ScopeCheck.AircraftCodes(Switch(Alpha, Beta)));
    }

    [Fact]
    public void Pass_for_a_plain_pr_into_main()
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input());

        Assert.True(pass);
        Assert.Contains("CORE-22", message);
        Assert.Contains("CORE-23", message);
        Assert.Contains("CORE-25", message);
        Assert.Contains("CORE-5", message);
    }

    [Fact]
    public void Base_other_than_main_needs_planned_in()
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(baseBranch: "feature/parent"));
        Assert.False(pass);
        Assert.Contains("CORE-23", message);

        (bool ok, _) = ScopeCheck.Evaluate(Input(
            description: "Planned in: docs/parent/scope.md", baseBranch: "feature/parent"));
        Assert.True(ok);
    }

    [Fact]
    public void Two_new_aircraft_need_variants()
    {
        string before = Switch(Alpha);
        string after = Switch(Alpha, Beta, Gamma);

        (bool pass, string message) = ScopeCheck.Evaluate(Input(switchBefore: before, switchAfter: after));
        Assert.False(pass);
        Assert.Contains("CORE-22", message);

        (bool ok, _) = ScopeCheck.Evaluate(Input(
            description: "Variants: Gamma family", switchBefore: before, switchAfter: after));
        Assert.True(ok);

        (bool one, _) = ScopeCheck.Evaluate(Input(switchBefore: before, switchAfter: Switch(Alpha, Beta)));
        Assert.True(one);
    }

    [Fact]
    public void Reordered_switch_arms_add_no_aircraft()
    {
        (bool pass, _) = ScopeCheck.Evaluate(Input(switchBefore: Switch(Alpha, Beta), switchAfter: Switch(Beta, Alpha)));
        Assert.True(pass);
    }

    [Fact]
    public void Aircraft_and_feature_fragments_together_fail()
    {
        string[] added = ["changelog.d/7-alpha.aircraft.md", "changelog.d/7-chocks.feature.md"];

        (bool pass, string message) = ScopeCheck.Evaluate(Input(
            description: "Variants: Alpha family", addedPaths: added));

        Assert.False(pass);
        Assert.Contains("CORE-22", message);
        Assert.Contains("split", message);
        Assert.Contains("aircraft's fragment", message);
    }

    [Fact]
    public void Two_aircraft_fragments_need_variants()
    {
        string[] added = ["changelog.d/7-alpha.aircraft.md", "changelog.d/7-beta.aircraft.md"];

        (bool pass, string message) = ScopeCheck.Evaluate(Input(addedPaths: added));
        Assert.False(pass);
        Assert.Contains("CORE-22", message);

        (bool ok, _) = ScopeCheck.Evaluate(Input(description: "Variants: Alpha family", addedPaths: added));
        Assert.True(ok);
    }

    [Fact]
    public void Dangling_tool_and_test_names_fail()
    {
        string tool = Tool("made-up-ocr");
        string tests = TestsOf("MadeUpReader");

        (bool pass, string message) = ScopeCheck.Evaluate(Input(addedLines:
            [new AddedLine("MSFSBlindAssist/Aircraft/X.cs", $"// rebuilt by {tool}, pinned by {tests}")]));

        Assert.False(pass);
        Assert.Contains("CORE-25", message);
        Assert.Contains(tool, message);
        Assert.Contains(tests, message);
    }

    [Fact]
    public void Existing_tools_nested_tests_and_one_offs_pass()
    {
        string real = Tool("real-gen");
        string nested = TestsOf("NestedThing");
        string method = TestsOf("ReleaseWidget");
        string[] tree = [real + "/gen.py"];
        string[] testClasses = [nested, method];

        AddedLine[] lines =
        [
            new("MSFSBlindAssist/A.cs", $"see {real}"),
            new("MSFSBlindAssist/A.cs", $"pinned by {nested}"),
            new("tests/B.cs", $"calls {method}"),
            new("tests/B.cs", $"measured with {Tool("sweep-x")}, a one-off script, not kept"),
            new("tests/B.cs", $"generated by {Tool("layout-gen")} in the private repo"),
            new("docs/notes.md", $"{Tool("nowhere-gen")} and {TestsOf("Nowhere")}"),
        ];

        (bool pass, string message) = ScopeCheck.Evaluate(Input(addedLines: lines, tree: tree, testClasses: testClasses));
        Assert.True(pass, message);

        (bool readme, string readmeMessage) = ScopeCheck.Evaluate(Input(
            addedLines: [new AddedLine(Tool("other-gen") + "/README.md", $"see {Tool("dangling-gen")}")],
            tree: tree, testClasses: testClasses));
        Assert.False(readme);
        Assert.Contains(Tool("dangling-gen"), readmeMessage);
    }

    [Fact]
    public void Names_already_on_the_base_pass()
    {
        string tool = Tool("kept-out-sweep");
        string tests = TestsOf("OldThing");

        (bool pass, string message) = ScopeCheck.Evaluate(Input(
            addedLines: [new AddedLine("MSFSBlindAssist/Navigation/Y.cs", $"// {tool} pinned by {tests}")],
            baseNames: [tool, tests]));

        Assert.True(pass, message);
    }

    [Fact]
    public void Stacked_with_another_open_pr_fails_only_into_main()
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(stackedWith: [12]));
        Assert.False(pass);
        Assert.Contains("CORE-23", message);
        Assert.Contains("12", message);

        (bool ok, _) = ScopeCheck.Evaluate(Input(
            description: "Planned in: docs/parent/scope.md", baseBranch: "feature/parent", stackedWith: [12]));
        Assert.True(ok);
    }

    // ---- Sim-tested on (CORE-5) -------------------------------------------------------------------------------

    [Theory]
    [InlineData("Sim-tested on: a1b2c3d")]
    [InlineData("Sim-tested on: A1B2C3D")]
    [InlineData("Sim-tested on: a1b2c3d4e5f60718293a4b5c6d7e8f9012345678")]
    [InlineData("Sim-tested on: v0.0.0 (build b2c3d4e)")]
    [InlineData("Sim-tested on: a1b2c3d, b2c3d4e")]
    public void Sim_tested_on_a_commit_of_this_pr_passes(string line)
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(description: line));

        Assert.True(pass, message);
    }

    [Theory]
    [InlineData("Sim-tested on: 0badc0d", "0badc0d")]
    [InlineData("Sim-tested on: 0BADC0D", "0BADC0D")]
    [InlineData("Sim-tested on: v0.0.0 (build 0badc0d)", "0badc0d")]
    [InlineData("Sim-tested on: 0badc0de0badc0de0badc0de0badc0de0badc0de", "0badc0de0badc0de0badc0de0badc0de0badc0de")]
    public void Sim_tested_on_an_unknown_build_fails(string line, string token)
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(description: line));

        Assert.False(pass);
        Assert.Contains("CORE-5", message);
        Assert.Contains(token, message);
    }

    [Fact]
    public void Sim_tested_on_two_builds_names_only_the_unknown_one()
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(description: "Sim-tested on: a1b2c3d, 0badc0d"));

        Assert.False(pass);
        Assert.Contains("0badc0d", message);
        Assert.DoesNotContain("a1b2c3d", message);
    }

    [Theory]
    [InlineData("Sim-tested on: not tested in the sim")]
    [InlineData("Sim-tested on: v0.0.0")]
    [InlineData("Sim-tested on: 20261125")]
    [InlineData("Sim-tested on: defaced")]
    [InlineData("")]
    public void Sim_tested_on_without_a_hash_checks_nothing(string line)
    {
        (bool pass, string message) = ScopeCheck.Evaluate(Input(description: line, prCommits: []));

        Assert.True(pass, message);
    }

    // ---- Messages ---------------------------------------------------------------------------------------------

    [Fact]
    public void Messages_say_the_check_is_advisory()
    {
        ScopeCheckInput[] failing =
        [
            Input(baseBranch: "feature/parent"),
            Input(switchBefore: Switch(Alpha), switchAfter: Switch(Alpha, Beta, Gamma)),
            Input(addedPaths: ["changelog.d/7-alpha.aircraft.md", "changelog.d/7-chocks.feature.md"]),
            Input(addedLines: [new AddedLine("tests/B.cs", "pinned by " + TestsOf("MadeUpReader"))]),
            Input(stackedWith: [12]),
            Input(description: "Sim-tested on: 0badc0d"),
        ];

        foreach (ScopeCheckInput input in failing)
        {
            (bool pass, string message) = ScopeCheck.Evaluate(input);

            Assert.False(pass);
            Assert.Contains("advisory", message);
            Assert.Contains("CONTRIBUTING.md", message);
        }
    }
}
