// Characterization tests for AudioDeviceSelector, the pure half of guidance-tone output
// device selection. Kept free of NAudio so it runs on CI runners, which have no audio
// endpoint at all. The three cases that matter: no saved selection (follow Windows),
// saved selection present, and saved selection missing (fall back but KEEP the preference).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AudioDeviceSelectorTests
{
    private const string DefaultId = "{0.0.0.00000000}.{default-guid}";
    private const string DefaultName = "Speakers (Realtek Audio)";
    private const string HeadsetId = "{0.0.0.00000000}.{headset-guid}";
    private const string HeadsetName = "Headphones (Sennheiser USB Headset)";

    private static IReadOnlyList<AudioOutputDevice> TwoDevices() => new List<AudioOutputDevice>
    {
        new(DefaultId, DefaultName),
        new(HeadsetId, HeadsetName),
    };

    [Fact]
    public void EmptySavedId_ResolvesToTheLiveDefaultEndpointId()
    {
        var result = AudioDeviceSelector.Resolve("", "", TwoDevices(), DefaultId, DefaultName);

        Assert.Equal(DefaultId, result.DeviceId);
        Assert.False(result.FellBack);
        Assert.Contains(DefaultName, result.StatusText);
    }

    [Fact]
    public void SavedDevicePresent_IsChosen_AndIsNotAFallback()
    {
        var result = AudioDeviceSelector.Resolve(HeadsetId, HeadsetName, TwoDevices(), DefaultId, DefaultName);

        Assert.Equal(HeadsetId, result.DeviceId);
        Assert.Equal(HeadsetName, result.DeviceName);
        Assert.False(result.FellBack);
        Assert.Contains(HeadsetName, result.StatusText);
    }

    [Fact]
    public void SavedDeviceMissing_FallsBackToDefault_AndNamesTheMissingDevice()
    {
        var onlyDefault = new List<AudioOutputDevice> { new(DefaultId, DefaultName) };

        var result = AudioDeviceSelector.Resolve(HeadsetId, HeadsetName, onlyDefault, DefaultId, DefaultName);

        Assert.Equal(DefaultId, result.DeviceId);
        Assert.True(result.FellBack);
        Assert.Contains(HeadsetName, result.StatusText);
        Assert.Contains(DefaultName, result.StatusText);
    }

    [Fact]
    public void SavedDeviceMissing_ResolvesToTheDefaultEndpointId_AndFlagsFellBack()
    {
        var onlyDefault = new List<AudioOutputDevice> { new(DefaultId, DefaultName) };

        var result = AudioDeviceSelector.Resolve(HeadsetId, HeadsetName, onlyDefault, DefaultId, DefaultName);

        Assert.Equal(DefaultId, result.DeviceId);
        Assert.True(result.FellBack);
    }

    [Fact]
    public void NoDefaultEndpointKnown_LeavesDeviceIdEmpty()
    {
        var result = AudioDeviceSelector.Resolve(HeadsetId, HeadsetName, new List<AudioOutputDevice>(), "", "");

        Assert.Equal(string.Empty, result.DeviceId);
        Assert.True(result.FellBack);
    }

    [Fact]
    public void SavedDeviceMissingWithNoRememberedName_StillProducesReadableStatus()
    {
        var result = AudioDeviceSelector.Resolve(HeadsetId, "", new List<AudioOutputDevice>(), DefaultId, DefaultName);

        Assert.True(result.FellBack);
        Assert.Contains("Saved device", result.StatusText);
        Assert.DoesNotContain(HeadsetId, result.StatusText);
    }

    [Fact]
    public void DeviceIdMatchIsCaseInsensitive()
    {
        var result = AudioDeviceSelector.Resolve(HeadsetId.ToUpperInvariant(), HeadsetName, TwoDevices(), DefaultId, DefaultName);

        Assert.Equal(HeadsetId, result.DeviceId);
        Assert.False(result.FellBack);
    }

    [Fact]
    public void NullInputsAreTolerated()
    {
        var result = AudioDeviceSelector.Resolve(null, null, null, DefaultId, DefaultName);

        Assert.Equal(DefaultId, result.DeviceId);
        Assert.False(result.FellBack);
    }

    [Fact]
    public void FallbackAnnouncement_NamesTheDeviceAndSaysTonesMoved()
    {
        string message = AudioDeviceSelector.FallbackAnnouncement(HeadsetName);

        Assert.Contains(HeadsetName, message);
        Assert.Contains("default", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallbackAnnouncement_WithoutARememberedName_IsStillASentence()
    {
        string message = AudioDeviceSelector.FallbackAnnouncement("");

        Assert.Contains("Guidance tone device", message);
        Assert.EndsWith(".", message);
    }
}
