// Regression coverage for two Task 6 review findings on AudioOutputDeviceService:
//
// 1) ApplyDeviceChange_DetectsChange_EvenAfterARaceWindowCreatePlayerCall pins the seed-once
//    fix (_lastAppliedSeeded). The pre-fix code reseeded _lastAppliedDeviceId from the live
//    saved setting on EVERY CreatePlayer(deviceIdOverride: null) call, not just the session's
//    first. A tone that starts in the window between a settings save and MainForm's
//    ApplyDeviceChange() call would seed the field onto the NEW id before ApplyDeviceChange
//    ever compares -- so that comparison reads new==new, early-returns, and any tone already
//    sounding on the OLD device is never rebound; re-saving the same device couldn't recover
//    it either, since the comparison still matched. This test reproduces exactly that race
//    window with a CreatePlayer() call sandwiched between the settings save and
//    ApplyDeviceChange(), and uses the fallback-announcement latch (which ApplyDeviceChange
//    resets only when it detects a real change) as the observable proxy for "did the rebind
//    actually happen" -- the one thing reachable from outside the class without a live audio
//    endpoint or a registered AudioToneGenerator.
//
// 2) AnnounceFallback_Sink_IsInvokedOffTheCallingThread pins the Task.Run dispatch inside
//    AnnounceFallbackOnce, which exists so the sink is never invoked while a caller's
//    AudioToneGenerator.startStopLock is held (see the LOCK ORDER note on
//    AudioOutputDeviceService and the doc on AnnounceFallback itself). If that dispatch were
//    ever replaced with a direct, synchronous call, this test would observe the sink running
//    on the calling thread and fail.
//
// Neither test needs real audio hardware: both only ever drive CreatePlayer() with a
// deliberately-bogus device id, which is guaranteed to fail TryOpenById before the code ever
// reaches a real endpoint. Whatever TryOpenDefault() does afterward (open a real device,
// or fail and log, on a machine with no audio at all) is irrelevant to what these tests
// assert.
//
// AudioOutputDeviceService keeps three relevant statics with no reset hook: _lastAppliedDeviceId,
// _lastAppliedSeeded and _fallbackAnnouncedForId. _lastAppliedSeeded in particular latches true
// forever after the first CreatePlayer(null) call anywhere in the process (by design), so these
// tests cannot assume they are the first thing to touch it. Every scenario below is instead
// bootstrapped through ApplyDeviceChange, which unconditionally overwrites _lastAppliedDeviceId
// whenever the requested id differs from whatever it currently holds -- paired with a freshly
// generated GUID id per run, that makes each scenario's starting point deterministic regardless
// of what any earlier test left behind, and regardless of test execution order.
//
// Shares the SettingsManagerGlobalState collection with SettingsSeedTests (see
// SettingsManagerGlobalStateCollection) and, as of this change, with AudioOutputDeviceServiceTests:
// all three read or write SettingsManager.Current, and the tests here additionally depend on
// AudioOutputDeviceService's own fallback-latch statics landing in a known state between steps,
// so nothing else may be touching either global concurrently.
//
// Never writes to disk: SettingsManager.Current is mutated in place (a plain property set on
// the already-cached UserSettings instance) and restored in Dispose -- Save() is never called,
// so the real %APPDATA%\MSFSBlindAssist\settings.json is never touched.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public class AudioOutputDeviceServiceFallbackTests : IDisposable
{
    private static readonly TimeSpan AnnounceWaitTimeout = TimeSpan.FromSeconds(5);

    private readonly string _originalDeviceId;
    private readonly string _originalDeviceName;
    private readonly Action<string>? _originalAnnounceFallback;

    public AudioOutputDeviceServiceFallbackTests()
    {
        _originalDeviceId = SettingsManager.Current.GuidanceToneDeviceId;
        _originalDeviceName = SettingsManager.Current.GuidanceToneDeviceName;
        _originalAnnounceFallback = AudioOutputDeviceService.AnnounceFallback;
    }

    public void Dispose()
    {
        SettingsManager.Current.GuidanceToneDeviceId = _originalDeviceId;
        SettingsManager.Current.GuidanceToneDeviceName = _originalDeviceName;
        AudioOutputDeviceService.AnnounceFallback = _originalAnnounceFallback;
    }

    [Fact]
    public void ApplyDeviceChange_DetectsChange_EvenAfterARaceWindowCreatePlayerCall()
    {
        string oldId = "{unit-test-old-" + Guid.NewGuid() + "}";

        int announceCount = 0;
        var signal = new ManualResetEventSlim(false);
        AudioOutputDeviceService.AnnounceFallback = _ =>
        {
            Interlocked.Increment(ref announceCount);
            signal.Set();
        };

        // Bootstrap: guarantee _lastAppliedSeeded is true, without caring what device id it
        // latches onto. "" never reaches the announce path (CreatePlayer skips it for a blank
        // requested id), so this can never register as an announcement in either build.
        SettingsManager.Current.GuidanceToneDeviceId = "";
        AudioOutputDeviceService.CreatePlayer()?.Dispose();

        // Deterministically plant a known "OLD device" baseline. Once _lastAppliedSeeded is
        // true, CreatePlayer's own seed step is permanently a no-op for the rest of this
        // process, so ApplyDeviceChange is the ONLY remaining path that still writes
        // _lastAppliedDeviceId -- and because oldId is a fresh GUID, this update is guaranteed
        // to fire (it can never accidentally equal whatever _lastAppliedDeviceId already held).
        SettingsManager.Current.GuidanceToneDeviceId = oldId;
        AudioOutputDeviceService.ApplyDeviceChange();

        // A tone actually starts on the OLD device: one fallback announcement (oldId can't
        // resolve to a real endpoint), and the announced-for latch settles on oldId.
        signal.Reset();
        AudioOutputDeviceService.CreatePlayer()?.Dispose();
        Assert.True(signal.Wait(AnnounceWaitTimeout), "Expected the first fallback announcement.");
        Assert.Equal(1, announceCount);

        // The pilot saves a new choice: Windows default (id "").
        SettingsManager.Current.GuidanceToneDeviceId = "";

        // THE RACE WINDOW: a tone starts in the gap between the settings save and the
        // ApplyDeviceChange() call MainForm.ApplyRuntimeSettings makes when the Settings
        // dialog closes. The requested id is blank, so this call can never itself announce --
        // but under the pre-fix behavior (reseed _lastAppliedDeviceId on every
        // CreatePlayer(null) call), it would silently move _lastAppliedDeviceId to "" right
        // here, before ApplyDeviceChange ever gets a chance to compare against the true prior
        // value.
        AudioOutputDeviceService.CreatePlayer()?.Dispose();

        // Fixed: _lastAppliedDeviceId is still oldId (the race-window call above was a no-op
        // seed), so "" != oldId is correctly detected as a real change, and the announced-for
        // latch is reset. Buggy: _lastAppliedDeviceId was already clobbered to "" above, so
        // this reads ""=="" and early-returns, leaving the latch stuck on oldId.
        AudioOutputDeviceService.ApplyDeviceChange();

        // The pilot re-selects the OLD device. A tone starting on it now must be announced
        // again -- fixed: yes, the latch was reset above. Buggy: no, AnnounceFallbackOnce
        // silently no-ops because the latch still reads oldId from the very first
        // announcement, and re-selecting the same device can't clear a latch that never saw
        // a change.
        SettingsManager.Current.GuidanceToneDeviceId = oldId;
        signal.Reset();
        AudioOutputDeviceService.CreatePlayer()?.Dispose();
        bool secondSignal = signal.Wait(AnnounceWaitTimeout);

        Assert.True(secondSignal,
            "Expected a second fallback announcement after the device was reselected. " +
            "The announcement latch appears stuck -- this is the exact symptom of the " +
            "pre-fix 'reseed on every CreatePlayer(null) call' defect.");
        Assert.Equal(2, announceCount);
    }

    [Fact]
    public void AnnounceFallback_Sink_IsInvokedOffTheCallingThread()
    {
        string bogusId = "{unit-test-thread-" + Guid.NewGuid() + "}";
        int callingThreadId = Thread.CurrentThread.ManagedThreadId;
        int? sinkThreadId = null;
        var signal = new ManualResetEventSlim(false);

        AudioOutputDeviceService.AnnounceFallback = _ =>
        {
            sinkThreadId = Thread.CurrentThread.ManagedThreadId;
            signal.Set();
        };

        SettingsManager.Current.GuidanceToneDeviceId = bogusId;
        AudioOutputDeviceService.CreatePlayer()?.Dispose();

        bool signaled = signal.Wait(AnnounceWaitTimeout);

        Assert.True(signaled, "The fallback sink was never invoked within the timeout.");
        Assert.NotNull(sinkThreadId);
        Assert.NotEqual(callingThreadId, sinkThreadId!.Value);
    }
}
