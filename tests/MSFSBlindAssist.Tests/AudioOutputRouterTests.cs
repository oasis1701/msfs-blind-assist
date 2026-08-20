// CI runners have no audio endpoint, which makes them the right place to pin the one property
// that matters most about this class: it degrades, it never throws. Every call site is a tone
// start inside a feature a blind pilot is steering with, so an exception escaping here would be
// a crash, not a missing beep.
//
// Routing DECISIONS are tested in AudioRebindPlannerTests against the pure planner; nothing here
// depends on which endpoints happen to exist.
//
// In the SettingsManagerGlobalState collection because a sweep reads SettingsManager.Current on
// the router's worker thread (SafeSavedDeviceId/SafeSavedDeviceName), and OpenFor reaches it too.
// SettingsSeedTests reflectively repoints SettingsDirectory/SettingsFilePath and nulls
// _currentSettings, and outside the collection it runs in parallel with this class -- so a
// Current miss landing in that window would run Load() -> SeedFenixMonitorDefaults -> Save()
// against whichever path happened to be live.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
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

        // Torn down AT ONCE, inside the test. Resolving Shared started its worker thread and
        // registered a live IMMNotificationClient, and nothing else in the run would ever
        // dispose them — so a real endpoint event minutes later (headset plugged in on a dev
        // machine) would drive a sweep that reads SettingsManager.Current on a background
        // thread this class's SettingsManagerGlobalState collection cannot serialize: the
        // collection serializes test CLASSES, not a worker that outlives them, and
        // SettingsSeedTests reflectively repoints the settings paths mid-run. Disposing here
        // closes that window; Dispose is idempotent (pinned above) and the Lazy keeps
        // returning the same — now inert — instance, so this assertion stays meaningful and
        // no later test resurrects the worker.
        AudioOutputRouter.Shared.Dispose();
    }
}
