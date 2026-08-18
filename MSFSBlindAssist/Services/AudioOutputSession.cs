using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MSFSBlindAssist.Services;

/// <summary>
/// An open output: the player, the endpoint it is bound to, and that endpoint's mix sample
/// rate. The MMDevice is bundled because NAudio's WasapiOut.Dispose() disposes the audio
/// client but NOT the device it was constructed from, so something has to own it; making the
/// pair one disposable keeps that ownership from leaking into AudioToneGenerator.
///
/// MixSampleRate is the reason this returns a rate at all: generating the tone AT the
/// endpoint's own rate keeps NAudio's DMO resampler out of the signal chain in the common
/// case (most endpoints mix at 48 kHz, while the tone chain historically hardcoded 44.1 kHz).
///
/// DeviceId/DeviceName report the endpoint actually opened — which CreatePlayer can silently
/// resolve to something other than what was requested (a missing saved device, or an unknown
/// deviceIdOverride, both fall back to the default endpoint). A caller auditioning a device
/// before committing to it (the settings panel's "Test Tone") needs to be able to tell the
/// pilot which device the sound actually came from, not just that a session was returned.
/// </summary>
public sealed class AudioOutputSession : IDisposable
{
    public IWavePlayer Player { get; }
    public int MixSampleRate { get; }
    public string DeviceId { get; }
    public string DeviceName { get; }

    private readonly MMDevice? _device;

    internal AudioOutputSession(IWavePlayer player, int mixSampleRate, MMDevice? device)
    {
        Player = player;
        MixSampleRate = mixSampleRate;
        _device = device;

        string id = string.Empty;
        string name = string.Empty;
        try
        {
            if (device != null)
            {
                id = device.ID;
                name = device.FriendlyName;
            }
        }
        catch
        {
            // Endpoint properties can throw if the device vanished between opening and here.
            // DeviceId/DeviceName are reporting-only — falling back to empty strings must
            // never fail the session itself, which is already open and playable.
        }

        DeviceId = id;
        DeviceName = name;
    }

    public void Dispose()
    {
        try { Player.Stop(); } catch { /* already stopped or device gone */ }
        try { Player.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
    }
}
