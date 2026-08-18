// AudioOutputSession's DeviceId/DeviceName exist so a caller auditioning a device (the
// settings panel's future "Test Tone" button) can tell whether the sound it just heard
// actually came from the device it asked for, or from a silent fallback to the default
// endpoint. The interesting failure mode - reading a real MMDevice's ID/FriendlyName after
// the device has vanished between opening and construction - needs a live endpoint to reach
// and is covered by manual/in-sim verification, not here (same reasoning as
// AudioOutputDeviceServiceTests: no CI runner has a WASAPI endpoint). What IS testable
// without hardware is the null-device path: Build() in AudioOutputDeviceService never
// actually passes a null device in production, but the constructor still has to degrade to
// empty strings rather than throw if it ever did, and that path is exercised here directly
// via the internal constructor (InternalsVisibleTo is already wired for this project - see
// MSFSBlindAssist/Properties/InternalsVisibleTo.cs - and used the same way by several
// existing Gsx*ResolverTests).

using MSFSBlindAssist.Services;
using NAudio.Wave;

namespace MSFSBlindAssist.Tests;

public class AudioOutputSessionTests
{
    private sealed class FakeWavePlayer : IWavePlayer
    {
        public float Volume { get; set; }
        public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

#pragma warning disable CS0067 // required by IWavePlayer; this fake never needs to raise it
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
#pragma warning restore CS0067

        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop() => PlaybackState = PlaybackState.Stopped;
        public void Init(IWaveProvider waveProvider) { }
        public void Dispose() { }
    }

    [Fact]
    public void DeviceIdAndDeviceName_AreEmptyStrings_WhenNoDeviceIsSupplied()
    {
        using var session = new AudioOutputSession(new FakeWavePlayer(), 48000, device: null);

        Assert.Equal(string.Empty, session.DeviceId);
        Assert.Equal(string.Empty, session.DeviceName);
    }

    [Fact]
    public void DisposeNeverThrows_WithNoDevice()
    {
        var session = new AudioOutputSession(new FakeWavePlayer(), 48000, device: null);

        session.Dispose();
    }

    [Fact]
    public void PlayerAndMixSampleRate_AreExposedAsGiven()
    {
        var player = new FakeWavePlayer();
        using var session = new AudioOutputSession(player, 48000, device: null);

        Assert.Same(player, session.Player);
        Assert.Equal(48000, session.MixSampleRate);
    }
}
