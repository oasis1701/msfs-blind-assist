// Pins AudioToneGenerator's REGISTRATION/LIFETIME contract, which is the half of this class a
// CI runner with no audio endpoint can still judge. Everything asserted here is true with or
// without an output device: no test asserts that a tone actually sounded.
//
// Every generator is given its OWN router rather than AudioOutputRouter.Shared. That is the
// point of the injectable-router constructor — a test that registered into the process-wide
// registry would leave entries behind for every other test in the run.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class AudioToneGeneratorTests
{
    [Fact]
    public void RebindIsRefusedOnAGeneratorThatWasNeverStarted()
    {
        using var router = new AudioOutputRouter();
        using var generator = new AudioToneGenerator(router);

        // Registration begins at CONSTRUCTION, so a sweep can reach a generator whose owner
        // has not started it yet. It must still be left alone: a routing sweep may move or
        // retry a tone, never conjure one nobody asked for.
        Assert.False(generator.RebindTo("{0.0.0.00000000}.{some-endpoint}"));
        Assert.False(generator.IsPlaying);
    }

    [Fact]
    public void RebindIsRefusedAfterTheOwnerStopsTheTone()
    {
        using var router = new AudioOutputRouter();
        using var generator = new AudioToneGenerator(router);

        generator.Start(HandFlyWaveType.Sine, volume: 0.0, frequency: 440.0);
        generator.Stop();

        // Stop is what ends the registration, so in production no sweep gets this far. The
        // guard is asserted anyway because Stop and a sweep already in flight can interleave.
        Assert.False(generator.RebindTo("{0.0.0.00000000}.{some-endpoint}"));
        Assert.False(generator.IsPlaying);
        Assert.False(generator.NeedsDevice);
    }

    [Fact]
    public void StartLeavesNeedsDeviceAsTheExactInverseOfIsPlaying()
    {
        using var router = new AudioOutputRouter();
        using var generator = new AudioToneGenerator(router);

        generator.Start(HandFlyWaveType.Sine, volume: 0.0, frequency: 440.0);

        // The invariant that makes a failed open recoverable: EVERY failure exit from the
        // start path sets NeedsDevice and every success clears it. On a machine with an
        // endpoint this is (playing, not needing); on a CI runner without one it is (not
        // playing, needing) — and it is the second case that a later sweep retries.
        Assert.Equal(generator.IsPlaying, !generator.NeedsDevice);
    }

    [Fact]
    public void StopAndDisposeNeverThrowOnAGeneratorThatNeverStarted()
    {
        using var router = new AudioOutputRouter();
        var generator = new AudioToneGenerator(router);

        // Both are reachable on a start that failed (the settings panel disposes an audition
        // tone that never began), and audio is optional feedback: neither may throw at an
        // owner. Stop is also idempotent, and Dispose runs after it.
        generator.Stop();
        generator.Stop();
        generator.Dispose();
        generator.Dispose();
    }

    [Fact]
    public void AGeneratorCanBeStartedAgainAfterBeingStopped()
    {
        using var router = new AudioOutputRouter();
        using var generator = new AudioToneGenerator(router);

        // TakeoffAssistManager builds ONE generator and starts/stops it per activation, so
        // stop-then-start has to leave the generator in a usable state (and, invisibly here,
        // back in the router's registry — Stop unregisters it).
        generator.Start(HandFlyWaveType.Sine, volume: 0.0, frequency: 440.0);
        generator.Stop();
        generator.Start(HandFlyWaveType.Sine, volume: 0.0, frequency: 440.0);

        Assert.Equal(generator.IsPlaying, !generator.NeedsDevice);
    }
}
