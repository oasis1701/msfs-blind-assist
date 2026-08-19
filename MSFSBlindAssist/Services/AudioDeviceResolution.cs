namespace MSFSBlindAssist.Services;

/// <summary>
/// The outcome of resolving a saved device preference against the endpoints that actually
/// exist right now. <paramref name="DeviceId"/> is the actual endpoint id the tones should
/// open — the saved device when it is present, otherwise the live Windows default device,
/// whether that is what the pilot chose (<paramref name="FellBack"/> false) or because the
/// device they chose is not currently connected (<paramref name="FellBack"/> true). It is
/// empty only when nothing is resolvable at all — no saved device present AND no default
/// endpoint could be determined either.
/// The saved preference itself is never rewritten on a fallback: the headset must be used
/// again the moment it is plugged back in.
/// </summary>
public readonly record struct AudioDeviceResolution(
    string DeviceId,
    string DeviceName,
    bool FellBack,
    string StatusText);
