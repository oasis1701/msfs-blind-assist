using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the one-shot waiter behind SimConnectManager.ReadFreshAsync: a fresh read completes on the
/// answer to its OWN request (or a period sample), times out to null (never a stale value), leaves
/// nothing registered when it gives up — so an abandoned read's late answer can never satisfy the
/// next read — shares one waiter per key between concurrent callers, and honours cancellation. The
/// properties the MD-11 walker's step protocol relies on.
/// </summary>
public class FreshReadWaitersTests
{
    [Fact]
    public async Task CompletesWithTheAnswerToItsOwnRequest_AndUnregisters()
    {
        var waiters = new FreshReadWaiters();
        var issued = new List<int>();

        var read = waiters.WaitAsync("K", issued.Add, timeoutMs: 5000);

        Assert.Single(issued);                    // the force-read is issued before waiting
        Assert.Equal(1, waiters.Count);
        Assert.True(waiters.Complete("K", issued[0], 2.0));
        Assert.Equal(2.0, await read);
        Assert.Equal(0, waiters.Count);
    }

    /// <summary>
    /// Finding X1. A read that timed out is abandoned: the late answer to ITS request must not
    /// complete the next read of the same key with a value sampled before that read asked.
    /// </summary>
    [Fact]
    public async Task ALateAnswerToATimedOutRead_DoesNotSatisfyTheNextRead()
    {
        var waiters = new FreshReadWaiters();
        var staleId = -1;
        var freshId = -1;

        Assert.Null(await waiters.WaitAsync("K", id => staleId = id, timeoutMs: 20));
        var second = waiters.WaitAsync("K", id => freshId = id, timeoutMs: 5000);
        Assert.NotEqual(staleId, freshId);

        Assert.False(waiters.Complete("K", staleId, 1.0));   // the abandoned request's answer lands on nobody
        Assert.False(second.IsCompleted);
        Assert.True(waiters.Complete("K", freshId, 2.0));    // only the second read's own answer completes it
        Assert.Equal(2.0, await second);
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public async Task EveryReadGetsItsOwnRequestId_CountingFromTheBase()
    {
        var waiters = new FreshReadWaiters(firstRequestId: 500);
        var issued = new List<int>();

        var a = waiters.WaitAsync("A", issued.Add, timeoutMs: 5000);
        var b = waiters.WaitAsync("A", issued.Add, timeoutMs: 5000);

        Assert.Equal(new[] { 500, 501 }, issued);
        waiters.FailAll();
        await a;
        await b;
    }

    /// <summary>
    /// The whole mechanism rests on the dispatcher routing a fresh answer to
    /// ProcessIndividualVariableResponse, which it does only for a request id at or above
    /// INDIVIDUAL_VARIABLE_BASE. Below it, every fresh answer would fall into the fixed-id switch,
    /// be discarded silently, and every ReadFreshAsync would time out to null — the walker would
    /// report "no movement" on every control with nothing in the log saying why.
    /// </summary>
    [Fact]
    public void TheFreshRequestRange_IsRoutedToTheIndividualVariablePath()
    {
        Assert.True(SimConnectManager.FreshRequestIdBase
                    >= (int)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);

        // ...and clear of every fixed request id, so no fresh answer can be mistaken for one.
        var fixedIds = Enum.GetValues<SimConnectManager.DATA_REQUESTS>().Select(v => (int)v);
        Assert.All(fixedIds, id => Assert.True(id < SimConnectManager.FreshRequestIdBase));
    }

    [Fact]
    public async Task TimesOutToNull_AndLeavesNothingRegistered()
    {
        var waiters = new FreshReadWaiters();
        var id = -1;

        var value = await waiters.WaitAsync("K", i => id = i, timeoutMs: 20);

        Assert.Null(value);                        // never a stale cached value
        Assert.Equal(0, waiters.Count);            // nothing left for a later caller to inherit
        Assert.False(waiters.Complete("K", id, 1.0));
        Assert.False(waiters.Complete("K", 1.0));
    }

    [Fact]
    public async Task ConcurrentCallersShareOneDelivery()
    {
        var waiters = new FreshReadWaiters();
        var issued = new List<int>();

        var first = waiters.WaitAsync("K", issued.Add, timeoutMs: 5000);
        var second = waiters.WaitAsync("K", issued.Add, timeoutMs: 5000);

        Assert.Equal(1, waiters.Count);
        Assert.True(waiters.Complete("K", issued[0], 7.0));  // an answer to any live sharer's request wakes all
        Assert.Equal(7.0, await first);
        Assert.Equal(7.0, await second);
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public async Task CancellationThrows_AndReleasesTheRead()
    {
        var waiters = new FreshReadWaiters();
        using var cts = new CancellationTokenSource();
        var id = -1;

        var read = waiters.WaitAsync("K", i => id = i, timeoutMs: 5000, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(0, waiters.Count);
        Assert.False(waiters.Complete("K", id, 1.0));
    }

    [Fact]
    public async Task AnIssueRequestThatThrows_LeavesNothingRegistered()
    {
        var waiters = new FreshReadWaiters();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => waiters.WaitAsync("K", _ => throw new InvalidOperationException("send failed"), timeoutMs: 5000));

        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public void CompleteWithoutAWaiterIsFalse()
    {
        var waiters = new FreshReadWaiters();

        Assert.False(waiters.Complete("K", 1, 1.0));
        Assert.False(waiters.Complete("K", 1.0));
    }

    /// <summary>A continuous-batch or periodic sample answers no request and completes any waiter.</summary>
    [Fact]
    public async Task APeriodSampleCompletesTheWaiter()
    {
        var waiters = new FreshReadWaiters();

        var read = waiters.WaitAsync("K", _ => { }, timeoutMs: 5000);

        Assert.True(waiters.Complete("K", 4.0));
        Assert.Equal(4.0, await read);
        Assert.Equal(0, waiters.Count);
    }

    [Fact]
    public async Task FailAllReleasesEveryWaiterWithNull()
    {
        var waiters = new FreshReadWaiters();
        var a = waiters.WaitAsync("A", _ => { }, timeoutMs: 5000);
        var b = waiters.WaitAsync("B", _ => { }, timeoutMs: 5000);

        waiters.FailAll();

        Assert.Null(await a);
        Assert.Null(await b);
        Assert.Equal(0, waiters.Count);
    }

    /// <summary>
    /// Two callers share one waiter; the first timing out must not orphan the second — and once
    /// the first has given up, its request's answer no longer counts for the survivor either.
    /// </summary>
    [Fact]
    public async Task ACallerThatOverlapsATimedOutOne_StillGetsItsOwnDelivery()
    {
        var waiters = new FreshReadWaiters();
        var issued = new List<int>();

        var first = waiters.WaitAsync("K", issued.Add, timeoutMs: 20);
        var second = waiters.WaitAsync("K", issued.Add, timeoutMs: 5000);
        Assert.Null(await first);

        Assert.Equal(1, waiters.Count);                          // the survivor keeps the key registered
        Assert.False(waiters.Complete("K", issued[0], 3.0));    // the timed-out sharer's answer is dropped
        Assert.False(second.IsCompleted);
        Assert.True(waiters.Complete("K", issued[1], 3.0));
        Assert.Equal(3.0, await second);
        Assert.Equal(0, waiters.Count);
    }
}
