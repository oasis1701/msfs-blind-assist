// CI runners have no audio endpoint, which makes them the right place to pin the one property
// that matters most about this class: it degrades, it never throws. Every call site is a tone
// start inside a feature a blind pilot is steering with, so an exception escaping here would be
// a crash, not a missing beep.
//
// Routing DECISIONS are tested in AudioRebindPlannerTests against the pure planner; nothing here
// depends on which endpoints happen to exist.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioOutputRouterTests
{
    [Fact]
    public void EnumerateNeverThrows_AndAlwaysReturnsAList()
    {
        using var router = new AudioOutputRouter();

        Assert.NotNull(router.Enumerate());
    }

    [Fact]
    public void OpenForAnUnknownDeviceNeverThrows()
    {
        using var router = new AudioOutputRouter();

        // Either it falls back to a real default endpoint, or (on a machine with no audio at
        // all) it returns null. Both are fine; throwing is not.
        router.OpenFor("{0.0.0.00000000}.{not-a-real-device}")?.Dispose();
    }

    [Fact]
    public void RequestSweepNeverThrows_WithNoLiveTones()
    {
        using var router = new AudioOutputRouter();

        router.RequestSweep("unit test");
    }

    [Fact]
    public void RequestBaselineSweepNeverThrows_WithNoLiveTones()
    {
        using var router = new AudioOutputRouter();

        // Runs once per session from MainForm, before anything else could have asked for a
        // sweep. It reaches the same WASAPI enumerate/resolve path as an ordinary sweep, so on
        // a CI runner with no audio endpoint at all it must still degrade rather than throw.
        // That it stays SILENT cannot be asserted here — the sink is never wired in a test —
        // so what is pinned is the half a runner can judge.
        router.RequestBaselineSweep();
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var router = new AudioOutputRouter();

        router.Dispose();
        router.Dispose();
    }

    [Fact]
    public void SharedIsAlwaysAvailable()
    {
        Assert.NotNull(AudioOutputRouter.Shared);
    }
}
