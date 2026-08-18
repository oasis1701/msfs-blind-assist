// CI runners have no audio endpoint at all, which makes them the perfect place to pin the
// one property that matters most about this service: it degrades, it never throws. Every
// call site is a tone start on a background thread inside a feature a blind pilot is
// steering with, so an exception escaping here would be a crash, not a missing beep.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioOutputDeviceServiceTests
{
    [Fact]
    public void EnumerateNeverThrows_AndAlwaysReturnsAList()
    {
        IReadOnlyList<AudioOutputDevice> devices = AudioOutputDeviceService.Enumerate();

        Assert.NotNull(devices);
    }

    [Fact]
    public void EnumerateReturnsRealEndpointsOnly_NeverTheSyntheticDefaultRow()
    {
        // The empty-Id "Windows default device" row is added by the settings UI, not here,
        // so that AudioDeviceSelector never has to special-case it inside `available`.
        foreach (AudioOutputDevice device in AudioOutputDeviceService.Enumerate())
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
        }
    }

    [Fact]
    public void CreatePlayerWithAnUnknownDeviceNeverThrows()
    {
        // Either it falls back to a real default endpoint, or (on a machine with no audio at
        // all) it returns null. Both are fine; throwing is not.
        AudioOutputSession? session = AudioOutputDeviceService.CreatePlayer("{0.0.0.00000000}.{not-a-real-device}");

        session?.Dispose();
    }

    [Fact]
    public void ResolveCurrentNeverThrows_AndAlwaysProducesStatusText()
    {
        AudioDeviceResolution resolution = AudioOutputDeviceService.ResolveCurrent();

        Assert.False(string.IsNullOrWhiteSpace(resolution.StatusText));
    }

    [Fact]
    public void ApplyDeviceChangeNeverThrowsWithNoLiveTones()
    {
        AudioOutputDeviceService.ApplyDeviceChange();
    }
}
