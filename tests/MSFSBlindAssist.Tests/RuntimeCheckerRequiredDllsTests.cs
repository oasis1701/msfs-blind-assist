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
        string output = Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "bin", "x64", "Debug", "net10.0-windows");
        if (!Directory.Exists(output)) return;   // CI builds the tests without the app's Debug output folder
        foreach (string dll in RuntimeChecker.RequiredDlls)
            Assert.True(File.Exists(Path.Combine(output, dll)), $"{dll} is not in {output}");
    }
}
