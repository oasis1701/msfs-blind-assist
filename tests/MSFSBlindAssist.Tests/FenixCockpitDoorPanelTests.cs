// Owner request (2026-10-22): the cockpit door the First Officer closes before start and opens at shutdown
// (S_COCKPIT_DOOR, 0 closed / 1 open, FenixActionExecutor.SetCockpitDoor) is on the Overhead > Cockpit Door
// panel too, written the way the First Officer writes it. The pedestal "Cockpit Door Open" light
// (I_PED_COCKPIT_DOOR_U) already announces a change, so the panel row itself stays silent.

using System.Linq;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixCockpitDoorPanelTests
{
    private static readonly FenixA320Definition Def = new();

    private static string DefinitionPath => System.IO.Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs");

    [Fact]
    public void The_cockpit_door_is_on_the_Cockpit_Door_panel()
        => Assert.Contains("S_COCKPIT_DOOR", Def.GetPanelControls()["Cockpit Door"]);

    [Fact]
    public void The_cockpit_door_reads_closed_or_open_on_request_and_stays_silent()
    {
        var door = Def.GetVariables()["S_COCKPIT_DOOR"];
        Assert.Equal("S_COCKPIT_DOOR", door.Name);
        Assert.Equal("Cockpit Door", door.DisplayName);
        Assert.Equal(SimVarType.LVar, door.Type);
        Assert.Equal(UpdateFrequency.OnRequest, door.UpdateFrequency);
        Assert.False(door.IsAnnounced);
        Assert.Equal(new[] { (0d, "Closed"), (1d, "Open") },
            door.ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
    }

    [Fact]
    public void Setting_the_door_writes_it_as_the_First_Officer_does()
    {
        string handler = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "HandleUIVariableSet");
        var branch = Regex.Match(handler, @"if \(varKey == ""S_COCKPIT_DOOR""\)\s*\{[^}]*\}");
        Assert.True(branch.Success, "HandleUIVariableSet has no if (varKey == \"S_COCKPIT_DOOR\") branch");
        Assert.Contains("simConnect.SetLVar(\"S_COCKPIT_DOOR\", value);", branch.Value);
        Assert.Contains("return true;", branch.Value);
    }
}
