// A traffic sweep reports the user aircraft under its REAL object id, never the
// SIMCONNECT_OBJECT_ID_USER alias 0, so the filter learns that id from the answers to our own
// requests. Live 2026-09-29: a FlyByWire A320 with no ATC ID was spoken to its own pilot as
// "Stop, Fly By Wire A320 very close, ahead, 0 feet."

using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class OwnAircraftFilterTests
{
    [Fact]
    public void The_learned_object_id_is_own_aircraft_even_with_no_callsign()
    {
        var filter = new OwnAircraftFilter();
        filter.Observe(1);

        Assert.True(filter.IsOwnAircraft(1, "", ""));
        Assert.False(filter.IsOwnAircraft(7, "", ""));
    }

    [Fact]
    public void Before_an_id_is_learned_only_a_matching_callsign_filters()
    {
        var filter = new OwnAircraftFilter();

        Assert.True(filter.IsOwnAircraft(1, "G-NPTD", "g-nptd "));
        Assert.False(filter.IsOwnAircraft(1, "", ""));
        Assert.False(filter.IsOwnAircraft(1, "ASXGSA", "ASXGS"));
    }

    [Fact]
    public void Once_an_id_is_learned_a_shared_callsign_no_longer_filters()
    {
        // AI and multiplayer aircraft share placeholder callsigns; one matching the pilot's must
        // still be reported as traffic once the pilot's own object id is known.
        var filter = new OwnAircraftFilter();
        filter.Observe(524288);

        Assert.False(filter.IsOwnAircraft(700000, "G-NPTD", "G-NPTD"));
        Assert.True(filter.IsOwnAircraft(524288, "", "G-NPTD"));
    }

    [Fact]
    public void Two_empty_callsigns_are_not_the_same_aircraft()
    {
        Assert.False(OwnAircraftFilter.IsOwnAircraft(5, userObjectId: 0, "", ""));
        Assert.False(OwnAircraftFilter.IsOwnAircraft(5, userObjectId: 0, null, "  "));
    }

    [Fact]
    public void The_request_alias_zero_is_never_learned_and_always_filtered()
    {
        var filter = new OwnAircraftFilter();

        Assert.False(filter.Observe(0));
        Assert.Equal(0u, filter.UserObjectId);
        Assert.True(filter.IsOwnAircraft(0, "N123", "G-ABCD"));
    }

    [Fact]
    public void Observe_reports_only_a_change_and_follows_a_new_id()
    {
        var filter = new OwnAircraftFilter();

        Assert.True(filter.Observe(1));
        Assert.False(filter.Observe(1));
        Assert.True(filter.Observe(3));
        Assert.True(filter.IsOwnAircraft(3, "", ""));
        Assert.False(filter.IsOwnAircraft(1, "", ""));
    }

    [Fact]
    public void Reset_forgets_the_id()
    {
        var filter = new OwnAircraftFilter();
        filter.Observe(1);
        filter.Reset();

        Assert.Equal(0u, filter.UserObjectId);
        Assert.False(filter.IsOwnAircraft(1, "", ""));
    }

    // ── Position backstop ─────────────────────────────────────────────────────────────────────
    // Closes every gap the id leaves open (before it is learned with no callsign, a sweep landing
    // between an aircraft load and the re-learn): the own aircraft is reported at its own spot.

    private const double Lat = 35.0425, Lon = -89.9767;   // KMEM
    private const double OneMetreLat = 1.0 / 111132.0;

    [Fact]
    public void An_entry_on_the_ground_at_the_own_position_is_the_own_aircraft()
    {
        Assert.True(OwnAircraftFilter.SharesOwnPosition(Lat, Lon, true, Lat, Lon, true));
        Assert.True(OwnAircraftFilter.SharesOwnPosition(Lat + 4 * OneMetreLat, Lon, true, Lat, Lon, true));
    }

    [Fact]
    public void An_entry_beyond_the_same_position_radius_is_traffic()
        => Assert.False(OwnAircraftFilter.SharesOwnPosition(Lat + 6 * OneMetreLat, Lon, true, Lat, Lon, true));

    [Fact]
    public void The_backstop_needs_both_on_the_ground_and_a_known_own_position()
    {
        Assert.False(OwnAircraftFilter.SharesOwnPosition(Lat, Lon, false, Lat, Lon, true));
        Assert.False(OwnAircraftFilter.SharesOwnPosition(Lat, Lon, true, Lat, Lon, false));
        Assert.False(OwnAircraftFilter.SharesOwnPosition(Lat, Lon, true, null, null, true));
        Assert.False(OwnAircraftFilter.SharesOwnPosition(0, 0, true, 0, 0, true));
        Assert.False(OwnAircraftFilter.SharesOwnPosition(double.NaN, Lon, true, Lat, Lon, true));
    }

    // ── Wiring in SimConnectManager (no harness: pinned from the source) ─────────────────────

    [Fact]
    public void Every_per_object_answer_is_observed_before_any_early_return()
    {
        string body = MethodBody("SimConnectManager.Dispatch.cs", "void SimConnect_OnRecvSimobjectData(");
        int observe = body.IndexOf("_ownAircraft.Observe(data.dwObjectID)", StringComparison.Ordinal);
        int firstReturn = body.IndexOf("return;", StringComparison.Ordinal);
        Assert.True(observe >= 0, "SimConnect_OnRecvSimobjectData no longer observes the user object id");
        Assert.True(firstReturn < 0 || observe < firstReturn,
            "Observe must run before the individual-variable / camera early returns, or answers arriving there never teach the id");
    }

    [Fact]
    public void The_id_is_forgotten_on_both_drop_paths_and_on_every_aircraft_load()
    {
        string disconnect = MethodBody("SimConnectManager.cs", "public void Disconnect()");
        Assert.Contains("_ownAircraft.Reset();", disconnect);

        string connect = MethodBody("SimConnectManager.cs", "public void Connect()");
        int catchAt = connect.IndexOf("catch (COMException)", StringComparison.Ordinal);
        Assert.True(catchAt >= 0, "Connect() no longer has its COMException drop path");
        Assert.Contains("_ownAircraft.Reset();", connect.Substring(catchAt));

        string loaded = MethodBody("SimConnectManager.Dispatch.cs", "void SimConnect_OnRecvEventFilename(");
        Assert.Contains("_ownAircraft.Reset();", loaded);
    }

    [Fact]
    public void A_traffic_entry_passes_both_filters_before_it_is_raised()
    {
        string body = MethodBody("SimConnectManager.Dispatch.cs", "void ProcessAiTrafficEntry(");
        int raised = body.IndexOf("AiTrafficReceived?.Invoke", StringComparison.Ordinal);
        int byId = body.IndexOf("_ownAircraft.IsOwnAircraft(", StringComparison.Ordinal);
        int byPosition = body.IndexOf("OwnAircraftFilter.SharesOwnPosition(", StringComparison.Ordinal);
        Assert.True(raised > 0 && byId >= 0 && byPosition >= 0 && byId < raised && byPosition < raised,
            "ProcessAiTrafficEntry must drop the own aircraft by id and by position before raising AiTrafficReceived");
    }

    // The text from <signature> to the next member declaration — enough for these plain method shapes.
    private static string MethodBody(string file, string signature)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "SimConnect", file));
        int at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{signature} not found in {file}");
        var next = Regex.Match(text.Substring(at + signature.Length),
            @"^    (?:public|private|internal|protected)", RegexOptions.Multiline);
        return next.Success ? text.Substring(at, signature.Length + next.Index) : text.Substring(at);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
