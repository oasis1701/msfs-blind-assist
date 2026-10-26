// Owner requests (2026-10-22/23): the cockpit door the First Officer closes before start and opens at shutdown
// (S_COCKPIT_DOOR, 0 closed / 1 open, FenixActionExecutor.SetCockpitDoor) has its own panel, Cockpit > Cockpit
// Door, with both door video buttons, written the way the First Officer writes it. The door says itself when it
// moves; the two lights that only mirror it (the pedestal OPEN light and the overhead CTL upper light, measured
// 2026-10-23: both follow the door) stay silent. The door and the jumpseat block each other as in the cockpit.

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

    private static string Handler => FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "HandleUIVariableSet");

    [Fact]
    public void The_Cockpit_Door_panel_is_first_in_the_Cockpit_section_and_gone_from_the_Overhead()
    {
        var structure = Def.GetPanelStructure();
        Assert.Equal("Cockpit Door", structure["Cockpit"][0]);
        Assert.DoesNotContain("Cockpit Door", structure["Overhead"]);
    }

    [Fact]
    public void The_Cockpit_Door_panel_holds_the_door_and_both_video_buttons()
    {
        var panels = Def.GetPanelControls();
        Assert.Equal(new[] { "S_COCKPIT_DOOR", "S_OH_COCKPIT_DOOR_VIDEO", "S_PED_COCKPIT_DOOR_VIDEO" }, panels["Cockpit Door"]);
        Assert.DoesNotContain("S_PED_COCKPIT_DOOR_VIDEO", panels["Cockpit Other"]);
        var vars = Def.GetVariables();
        Assert.Equal(new[] { "Cockpit Door", "Overhead Video", "Pedestal Video" },
            panels["Cockpit Door"].Select(k => vars[k].DisplayName));
    }

    [Fact]
    public void The_cockpit_door_reads_closed_or_open_and_says_itself()
    {
        var door = Def.GetVariables()["S_COCKPIT_DOOR"];
        Assert.Equal("S_COCKPIT_DOOR", door.Name);
        Assert.Equal(SimVarType.LVar, door.Type);
        Assert.Equal(UpdateFrequency.Continuous, door.UpdateFrequency);
        Assert.True(door.IsAnnounced);
        Assert.Equal(new[] { (0d, "Closed"), (1d, "Open") },
            door.ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)));
    }

    [Theory]
    [InlineData("I_PED_COCKPIT_DOOR_U")]   // pedestal OPEN light
    [InlineData("I_OH_DOOR_CTL_U")]        // overhead CTL upper light
    public void Lights_that_only_mirror_the_door_stay_silent(string key)
    {
        var light = Def.GetVariables()[key];
        Assert.False(light.IsAnnounced);
        Assert.Equal(UpdateFrequency.OnRequest, light.UpdateFrequency);
    }

    [Fact]
    public void The_door_fault_light_has_a_plain_name()
    {
        var fault = Def.GetVariables()["I_PED_COCKPIT_DOOR_L"];
        Assert.Equal("Cockpit Door Fault", fault.DisplayName);
        Assert.True(fault.IsAnnounced);
    }

    [Fact]
    public void Setting_the_door_writes_it_as_the_First_Officer_does()
    {
        var branch = Regex.Match(Handler, @"if \(varKey == ""S_COCKPIT_DOOR""\)\s*\{[^}]*\}");
        Assert.True(branch.Success, "HandleUIVariableSet has no if (varKey == \"S_COCKPIT_DOOR\") branch");
        Assert.Contains("simConnect.SetLVar(\"S_COCKPIT_DOOR\", value);", branch.Value);
        Assert.Contains("return true;", branch.Value);
    }

    // The interlock: the Fenix hides the door's clickspot while the jumpseat is deployed and the jumpseat's while
    // the door is open (Interactions.xml, 3.0.0.7426), so a sighted pilot cannot make either move.
    [Theory]
    [InlineData("S_COCKPIT_DOOR", 1, 1.0, FenixA320CockpitDoor.StowJumpseatFirst)]   // open with the jumpseat out
    [InlineData("S_JUMPSEAT", 1, 1.0, FenixA320CockpitDoor.CloseDoorFirst)]          // jumpseat out with the door open
    [InlineData("S_COCKPIT_DOOR", 1, 0.0, null)]                                    // jumpseat stowed: open
    [InlineData("S_JUMPSEAT", 1, 0.0, null)]                                        // door closed: deploy
    [InlineData("S_COCKPIT_DOOR", 1, null, null)]                                   // unread: never blocks
    [InlineData("S_JUMPSEAT", 1, null, null)]
    [InlineData("S_COCKPIT_DOOR", 0, 1.0, null)]                                    // closing is never blocked
    [InlineData("S_JUMPSEAT", 0, 1.0, null)]                                        // stowing is never blocked
    public void The_door_and_the_jumpseat_block_each_other(string key, double value, double? other, string? refusal)
        => Assert.Equal(refusal, FenixA320CockpitDoor.Refusal(key, value, other));

    [Theory]
    [InlineData("S_COCKPIT_DOOR", 1, "S_JUMPSEAT")]
    [InlineData("S_JUMPSEAT", 1, "S_COCKPIT_DOOR")]
    [InlineData("S_COCKPIT_DOOR", 0, null)]
    [InlineData("S_JUMPSEAT", 0, null)]
    [InlineData("S_JUMPSEAT_HEADREST", 1, null)]
    public void Only_opening_the_door_or_deploying_the_jumpseat_checks_the_other(string key, double value, string? blocking)
        => Assert.Equal(blocking, FenixA320CockpitDoor.BlockingKey(key, value));

    [Fact]
    public void The_handler_checks_the_interlock_before_the_jumpseat_and_door_writes()
    {
        string handler = Handler;
        int check = handler.IndexOf("FenixA320CockpitDoor.BlockingKey(varKey, value)", System.StringComparison.Ordinal);
        Assert.True(check >= 0, "HandleUIVariableSet never checks FenixA320CockpitDoor.BlockingKey");
        Assert.True(check < handler.IndexOf("case \"S_JUMPSEAT\":", System.StringComparison.Ordinal));
        Assert.True(check < handler.IndexOf("if (varKey == \"S_COCKPIT_DOOR\")", System.StringComparison.Ordinal));
        // Every pick of either control is counted first, closing and stowing too, so a write still waiting
        // for its read yields to a newer pick (arrow down to Deployed and straight back to Stowed).
        int counted = handler.IndexOf("_cockpitDoorPicks.Next(varKey)", System.StringComparison.Ordinal);
        Assert.True(counted >= 0 && counted < check, "HandleUIVariableSet must count the pick before the interlock check");
    }

    [Fact]
    public void Only_the_door_and_the_jumpseat_are_interlocked()
    {
        Assert.True(FenixA320CockpitDoor.IsInterlocked("S_COCKPIT_DOOR"));
        Assert.True(FenixA320CockpitDoor.IsInterlocked("S_JUMPSEAT"));
        Assert.False(FenixA320CockpitDoor.IsInterlocked("S_JUMPSEAT_HEADREST"));
    }

    [Fact]
    public void A_newer_pick_of_the_same_control_supersedes_a_waiting_one()
    {
        var picks = new FenixA320CockpitDoor.Picks();
        int deploy = picks.Next("S_JUMPSEAT");
        Assert.True(picks.IsLatest("S_JUMPSEAT", deploy));
        int stow = picks.Next("S_JUMPSEAT");
        Assert.False(picks.IsLatest("S_JUMPSEAT", deploy));
        Assert.True(picks.IsLatest("S_JUMPSEAT", stow));
        picks.Next("S_COCKPIT_DOOR");                       // the other control does not supersede it
        Assert.True(picks.IsLatest("S_JUMPSEAT", stow));
    }

    [Fact]
    public void The_waiting_write_reads_the_other_control_quickly_and_yields_to_a_newer_pick()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "WriteUnlessBlockedAsync");
        // The jumpseat (on request) answers a fresh read within a frame; the door rides the 1 s batch, so its
        // cache (at most a batch old) is read instead of waiting a second for the next batch.
        Assert.Contains("simConnect.SupportsFreshReads(blockingKey)", body);
        Assert.Contains("simConnect.GetCachedVariableValue(blockingKey)", body);
        int read = body.IndexOf("GetCachedVariableValue(blockingKey)", System.StringComparison.Ordinal);
        int latest = body.IndexOf("_cockpitDoorPicks.IsLatest(varKey, pick)", System.StringComparison.Ordinal);
        Assert.True(latest > read, "the newer-pick check must come after the read, before the write or refusal");
        Assert.True(latest < body.IndexOf("SetLVar(varKey, value)", System.StringComparison.Ordinal));
        Assert.True(latest < body.IndexOf("AnnounceImmediate(refusal)", System.StringComparison.Ordinal));
    }
}
