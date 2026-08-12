using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxRemoteStateTests
{
    private static GsxFrame Patch(string key, string valueJson) =>
        GsxFrame.Parse($$"""{"v":1,"type":"patch","path":"/{{key}}","value":{{valueJson}}}""");

    [Fact]
    public void Snapshot_seeds_all_top_level_keys_and_ignores_envelope()
    {
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse("""{"v":1,"type":"snapshot","ts":1,"parking":"Gate 20A","menuShown":false}"""));
        Assert.True(s.HasSnapshot);
        Assert.True(s.TryGet("parking", out var p));
        Assert.Equal("Gate 20A", p.GetString());
        Assert.False(s.TryGet("v", out _));
        Assert.False(s.TryGet("type", out _));
        Assert.False(s.TryGet("ts", out _));
    }

    [Fact]
    public void Patch_replaces_one_key_and_never_deep_merges()
    {
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","operators":{"handling":"OneJet","fuel":"UGE"}}"""));
        s.Apply(Patch("operators", """{"handling":"Swissport"}"""));

        Assert.True(s.TryGet("operators", out var op));
        Assert.Equal("Swissport", op.GetProperty("handling").GetString());
        // the whole key was replaced - the old "fuel" member must be GONE, not merged
        Assert.False(op.TryGetProperty("fuel", out _));
    }

    [Fact]
    public void Null_patch_value_deletes_the_key()
    {
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","receipt":{"operator":"X"}}"""));
        s.Apply(Patch("receipt", "null"));
        Assert.False(s.TryGet("receipt", out _));
    }

    [Fact]
    public void Later_snapshot_replaces_everything()
    {
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","parking":"A1","airline":"BAW"}"""));
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","parking":"B2"}"""));
        Assert.True(s.TryGet("parking", out var p));
        Assert.Equal("B2", p.GetString());
        Assert.False(s.TryGet("airline", out _));
    }

    [Fact]
    public void Changed_fires_with_the_key_name()
    {
        var s = new GsxRemoteState();
        var seen = new List<string>();
        s.Changed += k => seen.Add(k);
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","parking":"A1"}"""));
        s.Apply(Patch("services", "[]"));
        Assert.Contains("*", seen);          // snapshot = everything changed
        Assert.Contains("services", seen);
    }

    [Fact]
    public void Engine_event_tracks_running_and_restarting()
    {
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse("""{"type":"hello","protocol":1,"gsxRunning":true}"""));
        Assert.True(s.GsxRunning);

        s.Apply(GsxFrame.Parse("""{"type":"event","topic":"engine","gsxRunning":false,"restarting":true}"""));
        Assert.False(s.GsxRunning);
        Assert.True(s.Restarting);

        // engine back up clears the restart latch
        s.Apply(GsxFrame.Parse("""{"type":"event","topic":"engine","gsxRunning":true}"""));
        Assert.True(s.GsxRunning);
        Assert.False(s.Restarting);
    }

    [Fact]
    public void Real_snapshot_fixture_loads_expected_keys()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-snapshot.json"));
        var s = new GsxRemoteState();
        s.Apply(GsxFrame.Parse(json));
        Assert.True(s.TryGet("services", out _));
        Assert.True(s.TryGet("settings", out _));
        Assert.True(s.TryGet("parking", out _));
        Assert.True(s.TryGet("menuShown", out _));
    }

    [Fact]
    public void Clear_preserves_restart_latch_across_socket_loss()
    {
        var s = new GsxRemoteState();

        // Latch the restart flag via an engine event
        s.Apply(GsxFrame.Parse("""{"type":"event","topic":"engine","gsxRunning":false,"restarting":true}"""));
        Assert.True(s.Restarting);

        // Load a snapshot to set HasSnapshot
        s.Apply(GsxFrame.Parse("""{"type":"snapshot","parking":"A1"}"""));
        Assert.True(s.HasSnapshot);

        // Simulate socket loss with Clear()
        s.Clear();

        // After socket loss:
        // - Restarting latch SURVIVES (for reconnect UX)
        // - Other state resets
        Assert.True(s.Restarting);
        Assert.False(s.GsxRunning);
        Assert.False(s.HasSnapshot);
        Assert.False(s.TryGet("parking", out _));
    }

    [Fact]
    public void Engine_event_raises_changed_with_engine_key()
    {
        var s = new GsxRemoteState();
        var seen = new List<string>();
        s.Changed += k => seen.Add(k);

        s.Apply(GsxFrame.Parse("""{"type":"event","topic":"engine","gsxRunning":true}"""));

        Assert.Contains("engine", seen);
    }
}
