using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MSFSBlindAssist.Services;

/// <summary>
/// An open output: the player, the endpoint it is bound to, and that endpoint's mix sample
/// rate. The MMDevice is bundled because WasapiOut.Dispose() disposes the audio client but not the
/// device it was constructed from, so the session has to keep the device reachable for as long
/// as the player is alive. Note that MMDevice.Dispose() itself is close to a no-op in NAudio
/// 2.3.0 -- it disposes only AudioEndpointVolume/AudioSessionManager, neither of which this
/// path ever touches, and never releases the IMMDevice RCW; that release comes from ordinary
/// GC finalization. Keeping the pair in one disposable is still the right ownership call, but
/// do not read the Dispose() call below as what frees the COM object.
///
/// MixSampleRate is the reason this returns a rate at all: generating the tone AT the endpoint's
/// own rate keeps the engine's sample-rate converter out of the signal chain (most endpoints mix
/// at 48 kHz, while the tone chain historically hardcoded 44.1 kHz). That is a QUALITY choice,
/// not a correctness one, and the two claims that used to justify it were both wrong against
/// NAudio 2.3.0: the whole IsFormatSupported / ResamplerDmoStream / dmoResamplerNeeded block sits
/// inside `if (shareMode == AudioClientShareMode.Exclusive)`, so the DMO resampler NEVER ran on
/// this shared-mode path at any rate; and the oscillator declares the same rate it generates at
/// while Init sets OutputWaveFormat from the provider, so declared and generated cannot diverge
/// and a rebind to a differently-clocked endpoint could not have played the tone sharp either.
/// See AudioToneGenerator.StartLocked and docs/audio.md, which carry the same correction.
///
/// DeviceId/DeviceName report the endpoint actually opened — which AudioOutputRouter.OpenFor
/// can silently resolve to something other than what was requested (a missing saved device, or
/// an unknown deviceIdOverride, both fall back to the default endpoint). It is also what a
/// routing sweep compares each generator's binding against. A caller auditioning a device
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
