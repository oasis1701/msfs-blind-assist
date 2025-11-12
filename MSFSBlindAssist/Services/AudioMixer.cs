using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Mixes multiple audio tone generators for simultaneous playback.
/// Used for visual guidance to combine current attitude tone (hand fly) with desired attitude tone (guidance).
/// </summary>
public class AudioMixer : IDisposable
{
    private WaveOutEvent? waveOut;
    private MixingSampleProvider? mixer;
    private volatile bool isPlaying;
    private readonly object startStopLock = new();
    private readonly List<ISampleProvider> activeSources = new();

    /// <summary>
    /// Starts the audio mixer.
    /// </summary>
    public void Start()
    {
        lock (startStopLock)
        {
            if (isPlaying)
                return;

            try
            {
                // Create mixer with CD-quality audio format
                mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
                {
                    ReadFully = true // Ensure all sources are read even if one ends
                };

                // Initialize playback device
                waveOut = new WaveOutEvent
                {
                    NumberOfBuffers = 2,
                    DesiredLatency = 150 // Match AudioToneGenerator latency
                };

                waveOut.Init(mixer);
                waveOut.Play();
                isPlaying = true;

                System.Diagnostics.Debug.WriteLine("[AudioMixer] Started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioMixer] Start failed: {ex.Message}");
                Cleanup();
            }
        }
    }

    /// <summary>
    /// Adds an audio source to the mixer.
    /// </summary>
    /// <param name="source">The audio sample provider to add.</param>
    public void AddSource(ISampleProvider source)
    {
        if (mixer == null || source == null)
            return;

        lock (startStopLock)
        {
            if (!activeSources.Contains(source))
            {
                mixer.AddMixerInput(source);
                activeSources.Add(source);
                System.Diagnostics.Debug.WriteLine($"[AudioMixer] Added source ({activeSources.Count} total)");
            }
        }
    }

    /// <summary>
    /// Removes an audio source from the mixer.
    /// </summary>
    /// <param name="source">The audio sample provider to remove.</param>
    public void RemoveSource(ISampleProvider source)
    {
        if (mixer == null || source == null)
            return;

        lock (startStopLock)
        {
            if (activeSources.Contains(source))
            {
                mixer.RemoveMixerInput(source);
                activeSources.Remove(source);
                System.Diagnostics.Debug.WriteLine($"[AudioMixer] Removed source ({activeSources.Count} remaining)");
            }
        }
    }

    /// <summary>
    /// Stops the audio mixer and all sources.
    /// </summary>
    public void Stop()
    {
        lock (startStopLock)
        {
            if (!isPlaying)
                return;

            Cleanup();
            isPlaying = false;
            System.Diagnostics.Debug.WriteLine("[AudioMixer] Stopped");
        }
    }

    /// <summary>
    /// Gets whether the mixer is currently playing.
    /// </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// Cleans up audio resources.
    /// </summary>
    private void Cleanup()
    {
        try
        {
            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;
            mixer = null;
            activeSources.Clear();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Disposes audio resources.
    /// </summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
