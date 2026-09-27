using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The bound on a screenshot capture. PrintWindow is a synchronous cross-process call with no
/// timeout of its own - into a simulator that has stopped pumping messages it never returns - and
/// a display read holds its one-at-a-time flag until the capture does, so an unbounded capture
/// refused every later display read for the rest of the session. The real capture needs a
/// simulator window; these drive the wrapper with stand-in work.
/// </summary>
public class ScreenshotServiceTests
{
    [Fact]
    public async Task ACaptureThatNeverReturns_IsAbandonedAsNull()
    {
        // Never disposed: the abandoned worker may still be waking on it after the test returns.
        var stuck = new ManualResetEventSlim(false);
        try
        {
            byte[]? result = await ScreenshotService.CaptureWithinAsync(() =>
            {
                stuck.Wait(TimeSpan.FromSeconds(5));   // stands in for a PrintWindow into a hung simulator
                return new byte[] { 1, 2, 3 };
            }, TimeSpan.FromMilliseconds(50));

            // A wrapper that waited for its worker would return these bytes 5 s late; the 5 s cap
            // on the wait is what keeps such a regression from hanging the run.
            Assert.Null(result);
        }
        finally
        {
            stuck.Set();   // let the pool thread go
        }
    }

    [Fact]
    public async Task ACaptureThatFinishes_ComesBackWhole()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47 };

        byte[]? result = await ScreenshotService.CaptureWithinAsync(() => png, TimeSpan.FromSeconds(10));

        Assert.Same(png, result);
    }

    [Fact]
    public async Task ACaptureThatThrows_StillThrows_OnlyTheTimeoutIsSwallowed()
    {
        // The callers' own catch blocks handle a real capture error exactly as before.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScreenshotService.CaptureWithinAsync(() => throw new InvalidOperationException("GDI+ failed"),
                TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void TheShippedBound_IsTenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), ScreenshotService.CaptureTimeout);
    }
}
