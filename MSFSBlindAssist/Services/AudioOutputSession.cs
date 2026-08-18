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
/// </summary>
public sealed class AudioOutputSession : IDisposable
{
    public IWavePlayer Player { get; }
    public int MixSampleRate { get; }

    private readonly MMDevice? _device;

    internal AudioOutputSession(IWavePlayer player, int mixSampleRate, MMDevice? device)
    {
        Player = player;
        MixSampleRate = mixSampleRate;
        _device = device;
    }

    public void Dispose()
    {
        try { Player.Stop(); } catch { /* already stopped or device gone */ }
        try { Player.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
    }
}
