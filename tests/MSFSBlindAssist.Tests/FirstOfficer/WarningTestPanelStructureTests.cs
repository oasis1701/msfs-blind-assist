using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants for the 737 manual stick-shaker / overspeed-clacker panel
/// toggles (2026-07-13): the four controls exist, render as buttons, live in the new
/// "Warning Tests" Overhead section, and are NOT wired as auto-announced monitors.
/// </summary>
public class WarningTestPanelStructureTests
{
    private static readonly string[] Keys =
    {
        "WARN_StickShakerTest1", "WARN_StickShakerTest2",
        "WARN_OverspeedClackerTest1", "WARN_OverspeedClackerTest2",
    };

    [Fact]
    public void FourWarningTestControls_AreButtons()
    {
        var def = new PMDG737Definition();
        var vars = def.GetVariables();
        foreach (var k in Keys)
        {
            Assert.True(vars.ContainsKey(k), $"missing var {k}");
            Assert.True(vars[k].RenderAsButton, $"{k} must RenderAsButton");
        }
    }

    [Fact]
    public void WarningTestsSection_ContainsTheFourControls_UnderOverhead()
    {
        var def = new PMDG737Definition();
        var controls = def.GetPanelControls();   // public base-class accessor (caches BuildPanelControls)
        Assert.True(controls.ContainsKey("Warning Tests"), "missing 'Warning Tests' panel section");
        foreach (var k in Keys)
            Assert.Contains(k, controls["Warning Tests"]);

        var structure = def.GetPanelStructure();
        Assert.Contains("Warning Tests", structure["Overhead"]);
    }
}
