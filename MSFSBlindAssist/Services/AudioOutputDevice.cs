namespace MSFSBlindAssist.Services;

/// <summary>
/// One selectable audio output endpoint, as presented to the settings UI.
/// <paramref name="Id"/> is the WASAPI endpoint ID — stable across reboots and across other
/// devices being plugged in or removed, which is why it and not a device index is what gets
/// persisted. An empty Id is the synthetic "follow the Windows default device" row, which the
/// UI adds itself; <see cref="AudioOutputDeviceService.Enumerate"/> returns real endpoints only.
/// </summary>
public readonly record struct AudioOutputDevice(string Id, string FriendlyName);
