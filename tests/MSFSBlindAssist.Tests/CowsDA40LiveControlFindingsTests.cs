using System.IO;
using System.Linq;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What pressing every DA40 panel control through the real definition, live, turned up
/// (2026-10-05, both airframes, the control probe in docs/da40.md).
/// </summary>
public class CowsDA40LiveControlFindingsTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(System.AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new System.InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void TheFmaIsAStatusTheModelWritesNotAnOption(DA40Variant variant)
    {
        // COWS_KILL_FMA is rewritten every frame from whether the autopilot computer is
        // alive; offered as a switch it was overwritten within a frame.
        var def = new CowsDA40Definition(variant);
        var v = def.GetVariables()["DA40_OPT_KILL_FMA"];
        Assert.True(v.RenderAsReadOnlyStatus);
        Assert.DoesNotContain("DA40_OPT_KILL_FMA", def.GetPanelControls()["Aircraft Options"]);
        Assert.Contains("DA40_OPT_KILL_FMA", def.GetPanelDisplayVariables()["GFC 700"]);
    }

    [Fact]
    public void VerticalSpeedUsesTheEventsTheGfc700Intercepts()
    {
        // The Working Title GFC 700 state manager handles AP_VS_ON/OFF and has no case for
        // AP_VS_HOLD_ON/OFF, which left the autopilot in pitch hold.
        string src = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Aircraft", "DA40", "CowsDA40Definition.Autopilot.cs"));
        Assert.Contains("\"AP_VS_ON\" : \"AP_VS_OFF\"", src);
        Assert.DoesNotContain("\"AP_VS_HOLD_ON\"", src);
    }
}
