namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure resolution of a saved guidance-tone device preference against the endpoints that
/// currently exist, plus the wording of the status line and the fallback announcement.
///
/// Deliberately free of any NAudio reference so it is unit-testable on a machine (or a CI
/// runner) with no audio hardware at all. Everything that actually touches WASAPI lives in
/// <see cref="AudioOutputDeviceService"/>.
/// </summary>
public static class AudioDeviceSelector
{
    /// <summary>Saved-setting value meaning "follow whatever Windows calls the default device".</summary>
    public const string FollowWindowsDefaultId = "";

    /// <summary>Label for the synthetic follow-the-default row in the settings combo.</summary>
    public const string DefaultDeviceLabel = "Windows default device";

    /// <summary>
    /// Resolves <paramref name="savedId"/> against <paramref name="available"/>.
    /// A saved ID that is empty follows the Windows default and is NOT a fallback; a saved ID
    /// that is absent from the list falls back to the default and IS, so the caller can
    /// announce it once.
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
        defaultDeviceName ??= string.Empty;

        if (string.IsNullOrWhiteSpace(savedId))
        {
            return new AudioDeviceResolution(
                FollowWindowsDefaultId,
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

        // The device is gone (unplugged, disabled, or renamed away). Never rewrite the saved
        // preference here — it is what brings the headset back on reconnect.
        string missing = string.IsNullOrWhiteSpace(savedName) ? "Saved device" : savedName;
        return new AudioDeviceResolution(
            FollowWindowsDefaultId,
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
