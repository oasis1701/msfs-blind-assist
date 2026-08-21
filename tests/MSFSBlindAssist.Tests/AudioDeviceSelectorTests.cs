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

    [Fact]
    public void RecoveredAnnouncement_NamesTheDeviceThatCameBack()
    {
        string message = AudioDeviceSelector.RecoveredAnnouncement(HeadsetName);

        Assert.Contains(HeadsetName, message);
        Assert.EndsWith(".", message);
    }

    [Fact]
    public void RecoveredAnnouncement_WithoutAName_IsStillASentence()
    {
        string message = AudioDeviceSelector.RecoveredAnnouncement("");

        Assert.Contains("Guidance tone device", message);
        Assert.EndsWith(".", message);
    }

    [Fact]
    public void DefaultDeviceChangedAnnouncement_NamesTheEndpointWindowsPromoted()
    {
        string message = AudioDeviceSelector.DefaultDeviceChangedAnnouncement(DefaultName);

        Assert.Contains(DefaultName, message);
        Assert.EndsWith(".", message);
    }

    [Fact]
    public void DefaultDeviceChangedAnnouncement_WithoutAName_FallsBackToTheDefaultDeviceLabel()
    {
        // Windows promoted something whose name could not be read. The pilot still has to be
        // told the tones moved, so this must never degrade to silence or to a dangling
        // "Guidance tones now on ." — it names the synthetic label the settings combo uses.
        string message = AudioDeviceSelector.DefaultDeviceChangedAnnouncement("");

        Assert.Contains(AudioDeviceSelector.DefaultDeviceLabel, message);
        Assert.EndsWith(".", message);
    }

    [Fact]
    public void NoDeviceAvailableAnnouncement_SaysTheGuidanceTonesHaveNoOutput()
    {
        string message = AudioDeviceSelector.NoDeviceAvailableAnnouncement();

        Assert.Contains("guidance tones", message, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".", message);
    }

    [Fact]
    public void EveryRouteNoticeSaysSomethingDifferent()
    {
        // The four notices exist because they ask for four different reactions — go and check
        // the headset, nothing to do it is back, Windows moved you, there is no sound at all.
        // Two that read alike would collapse into one for a pilot who only ever HEARS them,
        // so the distinctness is the feature, not an accident of the wording.
        var messages = new[]
        {
            AudioDeviceSelector.FallbackAnnouncement(HeadsetName),
            AudioDeviceSelector.RecoveredAnnouncement(HeadsetName),
            AudioDeviceSelector.DefaultDeviceChangedAnnouncement(DefaultName),
            AudioDeviceSelector.NoDeviceAvailableAnnouncement(),
        };

        Assert.Equal(messages.Length, messages.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
