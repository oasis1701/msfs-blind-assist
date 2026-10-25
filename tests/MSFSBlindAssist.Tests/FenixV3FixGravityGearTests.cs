// Fenix gravity gear: one sequenced control (handle up, crank 3 turns; crank back, handle stowed).

using System.Globalization;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FenixV3FixGravityGearTests
{
    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    private static (FenixA320GravityGearWalker walker, List<(string key, double value)> writes) Make(
        double?[] reads, Func<TimeSpan, Task>? delay = null)
    {
        var writes = new List<(string, double)>();
        int i = 0;
        var walker = new FenixA320GravityGearWalker(
            (k, v) => writes.Add((k, v)),
            _ => Task.FromResult(reads[Math.Min(i++, reads.Length - 1)]),
            delay ?? (_ => Task.CompletedTask));
        return (walker, writes);
    }

    [Fact]
    public void CrankSteps_cover_three_turns_on_the_grid()
    {
        var up = FenixA320GravityGear.CrankSteps(0, 3);
        Assert.Equal(60, up.Count);
        Assert.Equal(0.05, up[0]);
        Assert.Equal(3.0, up[^1]);

        var down = FenixA320GravityGear.CrankSteps(3, 0);
        Assert.Equal(60, down.Count);
        Assert.Equal(2.95, down[0]);
        Assert.Equal(0.0, down[^1]);

        var mid = FenixA320GravityGear.CrankSteps(1.5, 3);
        Assert.Equal(30, mid.Count);
        Assert.Equal(1.55, mid[0]);

        Assert.Empty(FenixA320GravityGear.CrankSteps(3, 3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.02, 0)]
    [InlineData(3, 3)]
    [InlineData(2.98, 3)]
    [InlineData(1.5, 1.5)]
    public void DescriptionKey_maps_only_the_ends(double crank, double expected)
    {
        Assert.Equal(expected, FenixA320GravityGear.DescriptionKey(crank));
    }

    [Fact]
    public async Task Extend_from_stowed_raises_the_handle_then_cranks_to_3()
    {
        var (walker, writes) = Make(new double?[] { 0, 3 });
        var result = await walker.MoveAsync(true);

        Assert.Equal((FenixA320GravityGear.HandleKey, 1.0), writes[0]);
        var crank = writes.Skip(1).ToList();
        Assert.Equal(60, crank.Count);
        Assert.All(crank, w => Assert.Equal(FenixA320GravityGear.CrankKey, w.key));
        Assert.Equal(3.0, crank[^1].value);
        Assert.Equal("Gravity gear: cranked 3 turns.", result);
    }

    [Fact]
    public async Task Stow_from_extended_cranks_back_then_stows_the_handle()
    {
        var (walker, writes) = Make(new double?[] { 3, 0 });
        var result = await walker.MoveAsync(false);

        Assert.Equal(61, writes.Count);
        Assert.All(writes.Take(60), w => Assert.Equal(FenixA320GravityGear.CrankKey, w.key));
        Assert.Equal(0.0, writes[59].value);
        Assert.Equal((FenixA320GravityGear.HandleKey, 0.0), writes[^1]);
        Assert.Equal("Gravity gear: crank stowed.", result);
    }

    [Fact]
    public async Task Extend_from_a_partial_crank_continues_from_there()
    {
        var (walker, writes) = Make(new double?[] { 1.5, 3 });
        await walker.MoveAsync(true);

        var crank = writes.Where(w => w.key == FenixA320GravityGear.CrankKey).ToList();
        Assert.Equal(30, crank.Count);
        Assert.Equal(1.55, crank[0].value);
    }

    [Fact]
    public async Task An_unread_crank_moves_nothing()
    {
        var (walker, writes) = Make(new double?[] { null });
        var result = await walker.MoveAsync(true);

        Assert.Equal("Gravity gear: no read-back, nothing moved.", result);
        Assert.Empty(writes);
    }

    [Fact]
    public async Task A_second_request_while_turning_is_refused()
    {
        var gate = new TaskCompletionSource();
        var (walker, writes) = Make(new double?[] { 0, 3 }, _ => gate.Task);

        var first = walker.MoveAsync(true);
        Assert.True(walker.IsRunning);
        int before = writes.Count;

        var second = await walker.MoveAsync(false);
        Assert.Equal("Gravity gear: crank already turning.", second);
        Assert.Equal(before, writes.Count);

        gate.SetResult();
        var result = await first;
        Assert.Equal("Gravity gear: cranked 3 turns.", result);
        Assert.False(walker.IsRunning);
    }

    // An aircraft switch or a disconnect mid-crank stops the writes there and says nothing over the next aircraft.
    [Fact]
    public async Task A_move_no_longer_wanted_stops_writing_and_is_silent()
    {
        var writes = new List<(string, double)>();
        bool wanted = true;
        var walker = new FenixA320GravityGearWalker(
            (k, v) => { writes.Add((k, v)); if (writes.Count == 10) wanted = false; },
            _ => Task.FromResult<double?>(0),
            _ => Task.CompletedTask,
            () => wanted);

        var result = await walker.MoveAsync(true);

        Assert.Null(result);
        Assert.Equal(10, writes.Count);
        Assert.False(walker.IsRunning);
    }

    [Fact]
    public async Task A_move_unwanted_before_it_starts_writes_nothing()
    {
        var writes = new List<(string, double)>();
        var walker = new FenixA320GravityGearWalker(
            (k, v) => writes.Add((k, v)), _ => Task.FromResult<double?>(0), _ => Task.CompletedTask, () => false);

        Assert.Null(await walker.MoveAsync(true));
        Assert.Empty(writes);
    }

    // A stop is silent after an aircraft switch, but a dropped connection leaves the same Fenix part-cranked: said.
    [Fact]
    public void A_stopped_move_is_said_only_when_the_aircraft_was_not_switched_away()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        int start = src.IndexOf("private async Task MoveGravityGearAsync", StringComparison.Ordinal);
        string body = src.Substring(start, src.IndexOf("/// <summary>", start, StringComparison.Ordinal) - start);
        Assert.Contains("if (!_switchedAway) announcer?.AnnounceImmediate(FenixA320GravityGear.StoppedText);", body);
    }

    [Fact]
    public void Partial_read_back_uses_a_point_under_sv_SE()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Gravity gear: crank at 1.5 turns.", FenixA320GravityGear.Describe(1.5));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    // MD11-8: the crank delivery is consumed while a move runs, so no read reaches the combo refresh.
    [Fact]
    public async Task The_crank_delivery_is_consumed_only_while_a_move_runs()
    {
        var def = new FenixA320Definition();
        var gate = new TaskCompletionSource();
        var (walker, _) = Make(new double?[] { 0, 3 }, _ => gate.Task);
        def.SetGravityGearWalkerForTests(walker);

        Assert.False(def.ProcessSimVarUpdate(FenixA320GravityGear.CrankKey, 0, null!));

        var move = walker.MoveAsync(true);
        Assert.True(walker.IsRunning);
        Assert.True(def.ProcessSimVarUpdate(FenixA320GravityGear.CrankKey, 0, null!));

        gate.SetResult();
        await move;
        Assert.False(def.ProcessSimVarUpdate(FenixA320GravityGear.CrankKey, 3, null!));
    }

    [Fact]
    public void The_move_force_reads_the_crank_once_it_ends()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs"));
        int start = src.IndexOf("private async Task MoveGravityGearAsync", StringComparison.Ordinal);
        Assert.True(start > 0);
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        string body = src.Substring(start, end - start);
        int moved = body.IndexOf("await _gravityGearWalker.MoveAsync", StringComparison.Ordinal);
        int read = body.IndexOf("RequestVariable(FenixA320GravityGear.CrankKey, forceUpdate: true)", StringComparison.Ordinal);
        Assert.True(moved > 0 && read > moved, "the end-of-move force-read must follow the awaited move");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Fact]
    public void The_extension_control_is_stowed_or_extended()
    {
        var def = new FenixA320Definition();
        var v = Vars["S_HYD_GRAVITY_GEAR_EXTEND"];
        Assert.Equal(new Dictionary<double, string> { [0] = "Stowed", [3] = "Extended" }, v.ValueDescriptions);
        Assert.Equal(3, v.DescriptionKeyFor(2.99));
        Assert.Equal(1.5, v.DescriptionKeyFor(1.5));

        var cockpitOther = def.GetPanelControls()["Cockpit Other"];
        Assert.Contains("S_HYD_GRAVITY_GEAR_EXTEND", cockpitOther);
        Assert.DoesNotContain("S_HYD_GRAVITY_GEAR_EXTEND_ROTATION", cockpitOther);
    }
}
