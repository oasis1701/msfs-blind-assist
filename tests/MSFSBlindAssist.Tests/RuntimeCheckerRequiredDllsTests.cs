using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>The start-up check's list of files a complete install has. Measured 2026-11-24: after the move to Prism
/// the app refused to start ("Tolk.dll not found", "nvdaControllerClient.dll not found"), and ZDSR read that dialog
/// aloud.</summary>
public class RuntimeCheckerRequiredDllsTests
{
    [Fact]
    public void Prism_is_required_and_the_screen_reader_libraries_it_replaced_are_not()
    {
        Assert.Contains("prism.dll", RuntimeChecker.RequiredDlls);
        Assert.Contains("Prismatoid.dll", RuntimeChecker.RequiredDlls);
        Assert.DoesNotContain("Tolk.dll", RuntimeChecker.RequiredDlls);
        Assert.DoesNotContain("nvdaControllerClient.dll", RuntimeChecker.RequiredDlls);
    }

    [Fact]
    public void Every_required_dll_is_in_the_build_output()
    {
        string? output = AppOutputFolder(ClaudeContextBudgetTests.RepoRoot(), AppContext.BaseDirectory);
        if (output == null) return;   // built into a folder of its own (-p:BaseOutputPath): no app folder to match
        Assert.True(Directory.Exists(output), $"The app was not built into {output}, beside these tests");
        foreach (string dll in RuntimeChecker.RequiredDlls)
            Assert.True(File.Exists(Path.Combine(output, dll)), $"{dll} is not in {output}");
    }

    [Theory]
    [InlineData(@"C:\repo\tests\MSFSBlindAssist.Tests\bin\x64\Debug\net10.0-windows\", @"C:\repo\MSFSBlindAssist\bin\x64\Debug\net10.0-windows")]
    [InlineData(@"C:\repo\tests\MSFSBlindAssist.Tests\bin\x64\Release\net10.0-windows", @"C:\repo\MSFSBlindAssist\bin\x64\Release\net10.0-windows")]
    [InlineData(@"C:\scratch\out\Debug\net10.0-windows\", null)]
    public void The_app_folder_checked_is_the_one_built_with_these_tests(string testFolder, string? expected)
    {
        // The configuration and platform these tests were built in, never a fixed "Debug": a Release run used to
        // check whatever an older Debug build had left behind.
        Assert.Equal(expected, AppOutputFolder(@"C:\repo", testFolder));
    }

    /// <summary>The app's output folder of the build these tests belong to: the path of this test assembly under
    /// the test project (bin\x64\Debug\net10.0-windows) taken under MSFSBlindAssist. Null when the tests were built
    /// outside their project folder.</summary>
    private static string? AppOutputFolder(string repoRoot, string testFolder)
    {
        string testProject = Path.Combine(repoRoot, "tests", "MSFSBlindAssist.Tests");
        string relative = Path.GetRelativePath(testProject, testFolder.TrimEnd('\\', '/'));
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal)) return null;
        return Path.Combine(repoRoot, "MSFSBlindAssist", relative);
    }
}
