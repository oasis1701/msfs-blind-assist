// The release and preview workflows put the PMDG display layouts beside the exe, in the folder LayoutStore reads
// (UPD-10). Deleting either workflow step, moving the bundle step past the zip, or renaming the folder on either side
// alone would ship a ZIP whose display keys all go to the AI read, with nothing failing.

using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Tests;

public class LayoutBundlerWorkflowTests
{
    private const string BundleProject = "tools/LayoutBundle/LayoutBundle.csproj";

    private static string[] Lines(string workflow) =>
        File.ReadAllLines(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), ".github", "workflows", workflow));

    private static int BundleLine(string[] lines) => Array.FindIndex(lines, l => l.Contains(BundleProject));

    [Theory]
    [InlineData("release.yml")]
    [InlineData("preview.yml")]
    public void The_workflow_runs_LayoutBundle(string workflow)
        => Assert.True(BundleLine(Lines(workflow)) >= 0,
            $"{workflow} no longer runs {BundleProject}: its ZIP would ship without the display layouts (UPD-10).");

    [Theory]
    [InlineData("release.yml")]
    [InlineData("preview.yml")]
    public void The_workflow_fetches_the_layouts_branch(string workflow)
        => Assert.True(Array.Exists(Lines(workflow), l => l.Trim() == "ref: layouts"),
            $"{workflow} no longer checks out the layouts store with \"ref: layouts\": the bundle step would have no layouts branch to read (UPD-10).");

    [Theory]
    [InlineData("release.yml")]
    [InlineData("preview.yml")]
    public void The_bundle_step_writes_the_folder_the_app_reads(string workflow)
    {
        string[] lines = Lines(workflow);
        int bundle = BundleLine(lines);
        Assert.True(bundle >= 0, $"{workflow} no longer runs {BundleProject} (UPD-10).");
        string expected = @"net10.0-windows\" + LayoutStore.BundledFolderName;
        string output = lines[bundle].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];

        Assert.True(output.EndsWith(expected, StringComparison.Ordinal),
            $"{workflow}'s bundle step writes to {output}, but the app reads the layouts from {expected} beside the exe "
            + $"(LayoutStore.BundledFolderName): change both together (UPD-10).");
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("preview.yml")]
    public void The_bundle_step_runs_before_the_zip(string workflow)
    {
        string[] lines = Lines(workflow);
        int bundle = BundleLine(lines);
        int zip = Array.FindIndex(lines, l => l.Contains("Compress-Archive"));
        Assert.True(bundle >= 0, $"{workflow} no longer runs {BundleProject} (UPD-10).");
        Assert.True(zip >= 0, $"{workflow} has no Compress-Archive line: this test no longer knows where its ZIP is made.");

        Assert.True(bundle < zip,
            $"{workflow} runs {BundleProject} after Compress-Archive: its ZIP would be made without the display layouts (UPD-10).");
    }
}
