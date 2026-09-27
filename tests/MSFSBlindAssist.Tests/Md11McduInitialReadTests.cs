using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the shape of the MCDU feed's start-up read.
///
/// Reported (2026-09-07): "When loading the app in flight and going into the FMC, the FMC
/// appears blank until a user initially presses a key." Measured with a read-only probe against
/// the live aircraft the same day (16:56-16:57): an ON_SET+CHANGED subscription on MD11MCDU —
/// the app's only request — delivered NOTHING in 8 s, and nothing in a further 10 s; a
/// PERIOD.ONCE read on the same definitions returned all three current pages within 22 ms. The
/// app's own logs show the same gap on two earlier starts: registered 16:23:19, first delivery
/// 16:23:40; registered 10:38:13, first delivery 10:39:11 — every time all three units at once,
/// with nothing in the app having pressed a key. The MD-11 writes the area only when a screen
/// changes, and SimConnect hands an ON_SET subscription only the writes that follow it.
///
/// So the manager reads each unit ONCE at start-up alongside its subscription — on a DIFFERENT
/// request id. That is not tidiness: SimConnect treats a request re-issued under an existing id
/// as a replacement, so a ONCE on the subscription's own id would cancel the subscription and
/// leave the window frozen on its first page. This codebase has already been bitten by exactly
/// that on ordinary data requests (see the A380 FCU ALT managed bullet in CLAUDE.md).
/// </summary>
public class Md11McduInitialReadTests
{
    [Theory]
    [InlineData(Md11McduUnit.Left)]
    [InlineData(Md11McduUnit.Center)]
    [InlineData(Md11McduUnit.Right)]
    public void A_snapshot_and_its_subscription_use_different_request_ids_that_map_to_one_unit(Md11McduUnit unit)
    {
        var (subscription, snapshot) = Md11McduDataManager.RequestIdsFor(unit);

        Assert.NotEqual(subscription, snapshot);
        Assert.Equal(unit, Md11McduDataManager.UnitForRequest(subscription));
        Assert.Equal(unit, Md11McduDataManager.UnitForRequest(snapshot));
    }

    [Fact]
    public void Request_ids_are_distinct_across_all_units()
    {
        var all = new List<uint>();
        foreach (var unit in new[] { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right })
        {
            var (subscription, snapshot) = Md11McduDataManager.RequestIdsFor(unit);
            all.Add(subscription);
            all.Add(snapshot);
        }

        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void An_unknown_request_id_maps_to_no_unit()
    {
        // The dispatcher hands every SIMCONNECT_RECV_CLIENT_DATA past this manager; PMDG's and
        // MobiFlight's ids must fall through, not decode as an MCDU.
        Assert.Null(Md11McduDataManager.UnitForRequest(0));
        Assert.Null(Md11McduDataManager.UnitForRequest(0x504D4400));
        // The real neighbours on the connection: PMDG 777 (50000-50003), NG3 (51000-51002),
        // MobiFlight (3000-3003).
        foreach (var id in new uint[] { 50000, 50003, 51000, 51002, 3000, 3003 })
            Assert.Null(Md11McduDataManager.UnitForRequest(id));
    }
}
