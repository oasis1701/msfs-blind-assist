// Pins the safety and the key commands of .github/workflows/pr-scope.yml, the advisory PR scope check
// (CORE-22 to CORE-25). The logic is GuideBuilder's check-scope; this test only guards how the workflow feeds it.

namespace MSFSBlindAssist.Tests;

public class PrScopeWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "workflows", "pr-scope.yml"));

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    [Fact]
    public void Workflow_is_safe_and_runs_check_scope()
    {
        string yml = Workflow();

        Assert.Contains("pull_request:", yml);
        foreach (string type in new[] { "opened", "edited", "synchronize", "reopened" })
            Assert.Contains(type, yml);
        Assert.DoesNotContain("pull_request_target", yml);

        // The description and the head reach the shell only through env, never inside a run: script.
        Assert.Contains("PR_BODY: ${{ github.event.pull_request.body }}", yml);
        Assert.Equal(1, Count(yml, "github.event.pull_request.body"));
        Assert.Contains("PR_HEAD: ${{ github.event.pull_request.head.sha }}", yml);
        Assert.Equal(1, Count(yml, "github.event.pull_request.head.sha"));
        Assert.Contains("PR_NUMBER: ${{ github.event.pull_request.number }}", yml);
        Assert.Equal(1, Count(yml, "github.event.pull_request.number"));

        Assert.Contains("contents: read", yml);
        Assert.Contains("pull-requests: read", yml);
        Assert.Contains("check-scope", yml);

        // No token stays in git's config while the PR's own code builds.
        Assert.Contains("persist-credentials: false", yml);

        // A PR that renames or deletes the switch file reads as an empty switch, not a failed step.
        Assert.Contains("> switch-before.txt 2>/dev/null || : > switch-before.txt", yml);
        Assert.Contains("> switch-after.txt 2>/dev/null || : > switch-after.txt", yml);

        // The CLI's own errors reach the summary and the annotation too.
        Assert.Contains("> result.txt 2>&1 || status=$?", yml);

        // main's own commits stay out of the list, so a build of main alone is flagged.
        Assert.Contains("git rev-list \"origin/$GITHUB_BASE_REF..$PR_HEAD\"", yml);
        Assert.Contains("--pr-commits", yml);
        Assert.Contains("--base-names", yml);

        // A runner's diff.noprefix or diff.mnemonicPrefix setting must not change the +++ b/ headers.
        Assert.Contains("--src-prefix=a/ --dst-prefix=b/", yml);

        // Only other PRs into main with a lower number are compared.
        Assert.Contains("baseRefName", yml);
        Assert.Contains("merge-base --all", yml);
        Assert.Contains("-lt \"$PR_NUMBER\"", yml);
    }
}
