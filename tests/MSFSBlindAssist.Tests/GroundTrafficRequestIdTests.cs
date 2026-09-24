// The ground-traffic monitor's sweep request ids. PR #247 B1 review / implementer concern 5: a sweep
// older than 3 s is treated as lost and re-issued — under the SAME request id, so when the old one then
// completed it was credited to the NEW request, including the watch-readiness gate, although its
// radius and intake were the old ones. The camera read paid for the same pattern once (one shared
// request id let an abandoned read's late reply complete the NEXT read) and rotates over a range of
// ids; the ground sweep now does too.

using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficRequestIdTests
{
    private static uint First => (uint)SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC;
    private static uint Last => First + SimConnectManager.GroundTrafficRequestIdCount - 1;

    [Fact]
    public void Sweeps_rotate_over_eight_ids()
        => Assert.Equal(8u, SimConnectManager.GroundTrafficRequestIdCount);

    [Fact]
    public void Every_id_in_the_range_is_a_ground_traffic_sweep()
    {
        for (uint id = First; id <= Last; id++)
            Assert.True(SimConnectManager.IsGroundTrafficRequestId(id), $"request id {id}");
    }

    [Fact]
    public void The_ids_either_side_of_the_range_are_not()
    {
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(First - 1));
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(Last + 1));
        // TCAS's own sweep is never taken for the monitor's (PR #247 review L5).
        Assert.False(SimConnectManager.IsGroundTrafficRequestId(
            (uint)SimConnectManager.DATA_REQUESTS.REQUEST_AI_TRAFFIC));
    }

    /// <summary>
    /// A request issued under an id already in use REPLACES that request (the MD-11 MCDU manager's
    /// note), so the range must hold no other request id: not a named one, not a definition id (a
    /// request-id namespace too — RequestSingleValue issues a DEF_* as its request id), not a
    /// per-variable id (INDIVIDUAL_VARIABLE_BASE upward), and not one of the hand-numbered ids the
    /// dispatcher matches by cast (324-328 takeoff assist / hand fly, 330-337 V-speeds, 370-372
    /// waypoint / hand fly, 505-508 guidance frames). The last is why the range is not 501-508: a
    /// sweep under 507 would cancel taxi guidance's own position stream.
    /// </summary>
    [Fact]
    public void The_range_is_clear_of_every_other_request_id()
    {
        Assert.True(Last < (uint)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);

        var named = Enum.GetValues<SimConnectManager.DATA_REQUESTS>().Select(v => (uint)(int)v).Where(id => id != First);
        Assert.DoesNotContain(named, id => id >= First && id <= Last);

        var definitions = Enum.GetValues<SimConnectManager.DATA_DEFINITIONS>().Select(v => (uint)(int)v);
        Assert.DoesNotContain(definitions, id => id >= First && id <= Last);

        uint[] handNumbered = { 324, 325, 326, 327, 328, 330, 331, 332, 333, 334, 335, 336, 337, 370, 371, 372, 505, 506, 507, 508 };
        Assert.DoesNotContain(handNumbered, id => id >= First && id <= Last);
    }

    // ── The source itself (PR #247 final review H8) ──────────────────────────────────────────
    // The hand-kept list above cannot see a FUTURE hand-numbered cast. These scan every *.cs under
    // MSFSBlindAssist/ for a raw (DATA_REQUESTS)NNN cast — what actually ships — and hold the same line.
    // Qualified or not (PR #247 re-review M6): FlyByWireA320Definition.cs already casts to
    // (SimConnect.SimConnectManager.DATA_REQUESTS), a form the first pattern could not see.

    [Fact]
    public void No_hand_numbered_request_id_in_the_source_lies_in_the_sweep_range()
    {
        var ids = HandNumberedRequestIds();
        // The scan must see the guidance frames it exists to protect, or it is scanning nothing.
        Assert.All(new uint[] { 505, 506, 507, 508 }, id => Assert.Contains(id, ids));
        Assert.DoesNotContain(ids, id => id >= First && id <= Last);
    }

    [Fact]
    public void The_hand_numbered_guidance_frames_are_not_named_request_ids()
    {
        for (int id = 505; id <= 508; id++)
            Assert.False(Enum.IsDefined(typeof(SimConnectManager.DATA_REQUESTS), id),
                $"request id {id} is a hand-numbered guidance frame AND a named DATA_REQUESTS value");
    }

    /// <summary>
    /// Every id cast by hand to DATA_REQUESTS anywhere in the app's own source — bare
    /// ((DATA_REQUESTS)507), class-qualified ((SimConnectManager.DATA_REQUESTS)507) or
    /// namespace-qualified ((SimConnect.SimConnectManager.DATA_REQUESTS)507).
    /// </summary>
    private static HashSet<uint> HandNumberedRequestIds()
    {
        var cast = new Regex(@"\((?:[\w.]+\.)?DATA_REQUESTS\)\s*(\d+)");
        string app = Path.Combine(RepoRoot(), "MSFSBlindAssist");
        var ids = new HashSet<uint>();
        foreach (string file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            // Build output (generated files under bin/ and obj/) is not source.
            var parts = Path.GetRelativePath(app, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("bin") || parts.Contains("obj")) continue;
            foreach (Match m in cast.Matches(File.ReadAllText(file)))
                ids.Add(uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return ids;
    }

    /// <summary>
    /// The repository root: the nearest directory above the test assembly that holds
    /// MSFSBlindAssist.sln. (The suite has no shared helper for this — its other file-reading tests
    /// read fixtures copied next to the assembly — so it is found the usual way, walking up.)
    /// </summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
