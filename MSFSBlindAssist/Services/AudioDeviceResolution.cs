namespace MSFSBlindAssist.Services;

/// <summary>
/// The outcome of resolving a saved device preference against the endpoints that actually
/// exist right now. <paramref name="DeviceId"/> is empty when the tones should follow the
/// Windows default device — either because that is what the pilot chose, or because the
/// device they chose is not currently connected (<paramref name="FellBack"/> true).
/// The saved preference itself is never rewritten on a fallback: the headset must be used
/// again the moment it is plugged back in.
/// </summary>
public readonly record struct AudioDeviceResolution(
    string DeviceId,
    string DeviceName,
    bool FellBack,
    string StatusText);
