using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// WHEN the MD-11 definition is disposed with walks in flight: every live walk is cancelled, and a
/// source its walk already disposed (DebouncedWalk's finally races the Dispose snapshot) must not
/// abort the sweep before the live sources behind it are reached — a walk left running finishes
/// against the next aircraft's registrations and speaks "did not move" for it.
/// </summary>
public class Md11WalkCancellationTests
{
    [Fact]
    public void CancelAll_CancelsEveryLiveSource_PastADisposedOne()
    {
        var first = new CancellationTokenSource();
        var gone = new CancellationTokenSource();
        gone.Dispose();                                   // its walk left a moment ago
        var already = new CancellationTokenSource();
        already.Cancel();                                 // superseded by a newer selection
        var last = new CancellationTokenSource();         // the Dial-A-Flap walk, appended last

        var n = Md11WalkCancellation.CancelAll(new CancellationTokenSource?[] { first, null, gone, already, last });

        Assert.Equal(2, n);
        Assert.True(first.IsCancellationRequested);
        Assert.True(last.IsCancellationRequested);
    }

    [Fact]
    public void CancelAll_WithNothingInFlight_CancelsNothing()
    {
        Assert.Equal(0, Md11WalkCancellation.CancelAll(new CancellationTokenSource?[] { null }));
    }
}
