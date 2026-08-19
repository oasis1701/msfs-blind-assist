namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure resolution of a saved guidance-tone device preference against the endpoints that
/// currently exist, plus the wording of the status line and the fallback announcement.
///
/// Deliberately free of any NAudio reference so it is unit-testable on a machine (or a CI
/// runner) with no audio hardware at all. Everything that actually touches WASAPI lives in
/// AudioOutputRouter.
/// </summary>
public static class AudioDeviceSelector
{
    /// <summary>Saved-setting value meaning "follow whatever Windows calls the default device".</summary>
    public const string FollowWindowsDefaultId = "";

    /// <summary>Label for the synthetic follow-the-default row in the settings combo.</summary>
    public const string DefaultDeviceLabel = "Windows default device";

    /// <summary>
    /// Resolves <paramref name="savedId"/> against <paramref name="available"/> and returns
    /// the actual endpoint id the tones should open. A saved ID that is empty follows the
    /// Windows default and is NOT a fallback; a saved ID that is absent from the list falls
    /// back to <paramref name="defaultDeviceId"/> and IS, so the caller can announce it once.
    /// Either way the returned <c>DeviceId</c> is <paramref name="defaultDeviceId"/> — empty
    /// only when even that is unknown, i.e. nothing is resolvable at all.
    /// </summary>
    public static AudioDeviceResolution Resolve(
        string? savedId,
        string? savedName,
        IReadOnlyList<AudioOutputDevice>? available,
        string defaultDeviceId,
        string defaultDeviceName)
    {
        savedId ??= string.Empty;
        savedName ??= string.Empty;
        available ??= Array.Empty<AudioOutputDevice>();
        defaultDeviceId ??= string.Empty;
        defaultDeviceName ??= string.Empty;

        if (string.IsNullOrWhiteSpace(savedId))
        {
            return new AudioDeviceResolution(
                defaultDeviceId,
                defaultDeviceName,
                false,
                $"Using {DescribeDefault(defaultDeviceName)}.");
        }

        foreach (AudioOutputDevice device in available)
        {
            if (string.Equals(device.Id, savedId, StringComparison.OrdinalIgnoreCase))
            {
                return new AudioDeviceResolution(device.Id, device.FriendlyName, false, $"Using {device.FriendlyName}.");
            }
        }

        // The device is not in the active-endpoint list right now (unplugged or disabled).
        // A rename does NOT land here: a WASAPI endpoint id is stable across renames, which
        // is exactly why UserSettings persists the id and not a WaveOut index.
        // Never rewrite the saved preference here — it is what brings the headset back on
        // reconnect.
        string missing = string.IsNullOrWhiteSpace(savedName) ? "Saved device" : savedName;
        return new AudioDeviceResolution(
            defaultDeviceId,
            defaultDeviceName,
            true,
            $"{missing} is not connected - using {DescribeDefault(defaultDeviceName)}.");
    }

    /// <summary>
    /// The spoken notice for a fallback, dispatched by AudioOutputRouter's sweep. Names the
    /// device that went away — the SAVED one, not the one now in use — so the pilot knows
    /// which piece of hardware to go and check rather than merely that something moved.
    /// </summary>
    public static string FallbackAnnouncement(string? savedName)
    {
        return string.IsNullOrWhiteSpace(savedName)
            ? "Guidance tone device is not available. Using the Windows default device."
            : $"Guidance tone device {savedName} is not available. Using the Windows default device.";
    }

    /// <summary>
    /// The spoken notice for a preferred device that has come back — the pilot reconnected
    /// the headset the tones had fallen back from, and the tones are moving onto it. Names
    /// the RECOVERED device, which is also the one now in use.
    /// </summary>
    public static string RecoveredAnnouncement(string? deviceName)
    {
        return string.IsNullOrWhiteSpace(deviceName)
            ? "Guidance tone device is back. Moving the guidance tones to it."
            : $"Guidance tone device {deviceName} is back. Moving the guidance tones to it.";
    }

    /// <summary>
    /// The spoken notice for Windows promoting a different default endpoint while the setting
    /// is "follow the default". Deliberately terse: the pilot did not ask for this and did not
    /// do anything wrong, so it reports the new destination and nothing else.
    /// </summary>
    public static string DefaultDeviceChangedAnnouncement(string? deviceName)
    {
        return string.IsNullOrWhiteSpace(deviceName)
            ? $"Guidance tones now on the {DefaultDeviceLabel}."
            : $"Guidance tones now on {deviceName}.";
    }

    /// <summary>
    /// The spoken notice for no endpoint being resolvable at all — every guidance tone is
    /// about to be silent, which a blind pilot must never have to infer from the absence of a
    /// sound they were steering with.
    /// </summary>
    public static string NoDeviceAvailableAnnouncement()
    {
        return "No audio device available for guidance tones.";
    }

    private static string DescribeDefault(string defaultDeviceName)
    {
        return string.IsNullOrWhiteSpace(defaultDeviceName)
            ? DefaultDeviceLabel
            : $"{DefaultDeviceLabel} ({defaultDeviceName})";
    }
}
