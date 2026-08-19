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
    /// The spoken notice for a fallback. Queued, once per session — see
    /// <see cref="AudioOutputDeviceService"/>. Names the device so the pilot knows which one
    /// went away rather than merely that something did.
    /// </summary>
    public static string FallbackAnnouncement(string? savedName)
    {
        return string.IsNullOrWhiteSpace(savedName)
            ? "Guidance tone device is not available. Using the Windows default device."
            : $"Guidance tone device {savedName} is not available. Using the Windows default device.";
    }

    private static string DescribeDefault(string defaultDeviceName)
    {
        return string.IsNullOrWhiteSpace(defaultDeviceName)
            ? DefaultDeviceLabel
            : $"{DefaultDeviceLabel} ({defaultDeviceName})";
    }
}
