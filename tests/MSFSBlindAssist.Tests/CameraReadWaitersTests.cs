using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the per-request camera read registry (review round 2, B3). Every camera read used to
/// share one waiter and one request id, so a read that timed out left its answer on the wire and
/// that late answer completed the NEXT read — a false "could not switch", or a stale AlreadyThere
/// that skipped the camera write and captured the wrong display. Each read now answers under its
/// own id from a small rotating range, and an abandoned id's answer completes nothing.
/// </summary>
public class CameraReadWaitersTests
{
    private static readonly CameraViewReading Cockpit = new(State: 2, ViewType: 1, ViewIndex: 0);
    private static readonly CameraViewReading InstrumentView3 = new(State: 2, ViewType: 2, ViewIndex: 2);

    [Fact]
    public async Task TheAnswerToAReadsOwnId_CompletesIt()
    {
        var waiters = new CameraReadWaiters(firstRequestId: 341, count: 8);

        var (id, read) = waiters.Begin();

        Assert.Equal(341, id);
        Assert.False(read.IsCompleted);
        Assert.True(waiters.Complete(id, InstrumentView3));
        Assert.Equal(InstrumentView3, await read);
    }

    [Fact]
    public async Task ALateAnswerToAnAbandonedRead_CompletesNothing_AndNeverTheNextRead()
    {
        var waiters = new CameraReadWaiters(341, 8);

        var (staleId, stale) = waiters.Begin();
        waiters.Abandon(staleId);                              // the read timed out
        Assert.Null(await stale);                              // released: nothing is left hanging
        var (freshId, fresh) = waiters.Begin();
        Assert.NotEqual(staleId, freshId);

        Assert.False(waiters.Complete(staleId, Cockpit));      // the camera as it was BEFORE the switch
        Assert.False(fresh.IsCompleted);                       // must not answer the next read
        Assert.True(waiters.Complete(freshId, InstrumentView3));
        Assert.Equal(InstrumentView3, await fresh);
    }

    [Fact]
    public async Task AnAnswerCompletesOnlyTheReadRegisteredUnderItsId()
    {
        var waiters = new CameraReadWaiters(341, 8);
        var (firstId, first) = waiters.Begin();
        var (secondId, second) = waiters.Begin();

        Assert.True(waiters.Complete(secondId, InstrumentView3));

        Assert.False(first.IsCompleted);
        Assert.Equal(InstrumentView3, await second);
        Assert.True(waiters.Complete(firstId, Cockpit));
        Assert.Equal(Cockpit, await first);
    }

    [Fact]
    public void IdsRotateWithinTheRange()
    {
        var waiters = new CameraReadWaiters(341, 3);
        var ids = new List<int>();

        for (int i = 0; i < 7; i++)
        {
            var (id, _) = waiters.Begin();
            ids.Add(id);
            Assert.True(waiters.Complete(id, Cockpit));        // each read answered before the next
        }

        Assert.Equal(new[] { 341, 342, 343, 341, 342, 343, 341 }, ids);
    }

    [Fact]
    public void AnIdStillAwaited_IsSkippedWhenTheRotationComesRound()
    {
        var waiters = new CameraReadWaiters(341, 3);
        var (held, _) = waiters.Begin();                       // 341: never answered, never abandoned
        var (second, _) = waiters.Begin();
        Assert.True(waiters.Complete(second, Cockpit));        // 342
        var (third, _) = waiters.Begin();
        Assert.True(waiters.Complete(third, Cockpit));         // 343

        var (next, _) = waiters.Begin();                       // the rotation is back at 341, still awaited

        Assert.Equal(341, held);
        Assert.Equal(342, next);
    }

    [Fact]
    public async Task WhenEveryIdIsAwaited_BeginHandsOutNoId_AndANullRead()
    {
        var waiters = new CameraReadWaiters(341, 2);
        waiters.Begin();
        waiters.Begin();

        var (id, read) = waiters.Begin();

        Assert.Equal(CameraReadWaiters.NoRequestId, id);
        Assert.Null(await read);
    }

    [Fact]
    public async Task FailAll_ReleasesEveryWaitingReadWithNull()
    {
        var waiters = new CameraReadWaiters(341, 8);
        var (firstId, first) = waiters.Begin();
        var (_, second) = waiters.Begin();

        waiters.FailAll();

        Assert.Null(await first);
        Assert.Null(await second);
        Assert.False(waiters.Complete(firstId, Cockpit));      // nothing is registered any more
    }

    [Fact]
    public void OwnsExactlyItsRange()
    {
        var waiters = new CameraReadWaiters(341, 8);

        Assert.False(waiters.Owns(340));
        Assert.True(waiters.Owns(341));
        Assert.True(waiters.Owns(348));
        Assert.False(waiters.Owns(349));
    }

    /// <summary>
    /// The manager's range sits below INDIVIDUAL_VARIABLE_BASE — at or above it the dispatcher
    /// hands the answer to the single-value path, which would cast the three-double camera struct
    /// as a SingleValue — and holds no other request id: no named one but its own base, and none
    /// of the hand-numbered ids the dispatcher matches by cast (324-328 takeoff assist / hand fly,
    /// 330-337 V-speeds, 370-372 waypoint / hand fly, 505-508 guidance frames).
    /// </summary>
    [Fact]
    public void TheManagersCameraRange_IsBelowTheIndividualBase_AndClearOfEveryOtherRequestId()
    {
        int first = (int)SimConnectManager.DATA_REQUESTS.REQUEST_CAMERA_VIEW;
        int last = first + SimConnectManager.CameraReadIdCount - 1;

        Assert.True(last < (int)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);

        var named = Enum.GetValues<SimConnectManager.DATA_REQUESTS>().Select(v => (int)v).Where(id => id != first);
        Assert.DoesNotContain(named, id => id >= first && id <= last);

        // DATA_DEFINITIONS is a request-id namespace TOO, not only a definition one: RequestSingleValue
        // issues a DEF_* as the request id, and the FBW A320's speeds table issues (DATA_REQUESTS)defId.
        // Its own members read DEF_CAMERA_VIEW = 341 (this range's base, excluded below) and
        // DEF_AI_TRAFFIC = 500 — so 342 is the natural next pick for a new definition, and it would
        // route a SingleValue answer into the (CameraViewData) cast: an InvalidCastException swallowed
        // by ProcessWindowMessage, and a hotkey readout that silently never answers.
        var definitions = Enum.GetValues<SimConnectManager.DATA_DEFINITIONS>().Select(v => (int)v).Where(id => id != first);
        Assert.DoesNotContain(definitions, id => id >= first && id <= last);

        int[] handNumbered = { 324, 325, 326, 327, 328, 330, 331, 332, 333, 334, 335, 336, 337, 370, 371, 372, 505, 506, 507, 508 };
        Assert.DoesNotContain(handNumbered, id => id >= first && id <= last);
    }
}
