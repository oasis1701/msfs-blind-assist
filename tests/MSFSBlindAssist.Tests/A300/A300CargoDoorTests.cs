using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The freighter's main cargo door (measured 2026-10-10): the 70 and 145 degree buttons pick how far it opens
/// (<c>A300_MAIN_CARGO_DOOR_70</c>/<c>_145</c>); the door switch moves it while held. <c>INI_MAIN_CARGO_DOOR</c> is
/// its travel, 0.7 at 70 degrees and 1 at 145; flags say closed, locked, and at 70 or 145.
/// </summary>
public class A300CargoDoorTests
{
    [Theory]
    [InlineData(0, 1, 1, 0, 0, "closed and locked")]
    [InlineData(0, 1, 0, 0, 0, "closed")]
    [InlineData(0, 0, 0, 0, 0, "unlocked")]
    [InlineData(0.44, 0, 0, 0, 0, "partly open, 44 percent")]
    [InlineData(0.7, 0, 0, 1, 0, "open 70 degrees")]
    [InlineData(1, 0, 0, 0, 1, "open 145 degrees")]
    public void The_door_reads_as_its_flags_and_travel_say(double travel, double closed, double locked, double at70, double at145, string text) =>
        Assert.Equal(text, A300CargoDoor.Status(travel, closed, locked, at70, at145));

    [Fact]
    public void The_cargo_door_panel_has_the_switch_that_moves_the_door()
    {
        // Left out until 2026-10-10 as if the angle buttons moved the door: they only pick the angle.
        var rows = A300PanelLayout.Place(A300ControlMap.Load()).RowsByPanel["Cargo Door"];
        var row = Assert.Single(rows, r => r.Key == "A300_MAIN_CARGO_DOOR_SWITCH");
        Assert.Equal("Cargo door switch", row.Name);
        Assert.Equal(new[] { "Open", "Neutral", "Close" }, row.Positions.Values);
    }

    [Fact]
    public void The_status_display_reads_the_door_and_the_angle_buttons_say_which_is_picked()
    {
        var cache = new Dictionary<string, double>
        {
            [A300CargoDoor.ClosedKey] = 0, [A300CargoDoor.LockedKey] = 0, [A300CargoDoor.At70Key] = 1, [A300CargoDoor.At145Key] = 0,
            ["A300_CARGO_DOOR_PICK_70"] = 1, ["A300_CARGO_DOOR_PICK_145"] = 0,
        };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.True(def.TryGetDisplayOverride(A300CargoDoor.StatusKey, 0.7, out var line));
        Assert.Equal("open 70 degrees", line);
        Assert.Equal("Cargo Door", A300Readouts.All.Single(r => r.Key == A300CargoDoor.StatusKey).Panel);

        Assert.True(def.TryDescribeControlState("A300_A300_MAIN_CARGO_DOOR_70", out var seventy));
        Assert.True(def.TryDescribeControlState("A300_A300_MAIN_CARGO_DOOR_145", out var full));
        Assert.Equal(("Selected", "Not selected"), (seventy, full));
        Assert.Contains("A300_CARGO_DOOR_PICK_70", def.GetVariables()["A300_A300_MAIN_CARGO_DOOR_70"].StateVariables!);
    }
}
