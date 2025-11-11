using NAudio.Dsp;
using NAudio.Wave;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Applies a low-pass filter to soften harsh harmonics from square waves.
/// Uses NAudio's BiQuadFilter for smooth frequency rolloff.
/// </summary>
public class LowPassFilterProvider : ISampleProvider
{
    private readonly ISampleProvider sourceProvider;
    private readonly BiQuadFilter[] filters;
    private readonly int channels;

    /// <summary>
    /// Gets the wave format of the audio stream.
    /// </summary>
    public WaveFormat WaveFormat => sourceProvider.WaveFormat;

    /// <summary>
    /// Creates a new low-pass filter provider.
    /// </summary>
    /// <param name="sourceProvider">Source audio provider to filter.</param>
    /// <param name="cutoffFrequency">Cutoff frequency in Hz (frequencies above this are attenuated).</param>
    /// <param name="q">Q factor (0.707 = Butterworth response - maximally flat, no resonance).</param>
    public LowPassFilterProvider(ISampleProvider sourceProvider, float cutoffFrequency, float q = 0.707f)
    {
        this.sourceProvider = sourceProvider;
        channels = sourceProvider.WaveFormat.Channels;
        filters = new BiQuadFilter[channels];

        // Create one filter per channel for stereo support
        for (int n = 0; n < channels; n++)
        {
            filters[n] = BiQuadFilter.LowPassFilter(
                sourceProvider.WaveFormat.SampleRate,
                cutoffFrequency,
                q
            );
        }
    }

    /// <summary>
    /// Reads filtered audio samples from the source provider.
    /// </summary>
    /// <param name="buffer">Buffer to fill with filtered samples.</param>
    /// <param name="offset">Offset in buffer to start writing.</param>
    /// <param name="count">Number of samples to read.</param>
    /// <returns>Number of samples read.</returns>
    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = sourceProvider.Read(buffer, offset, count);

        // Apply filter to each sample
        for (int i = 0; i < samplesRead; i++)
        {
            int channelIndex = i % channels;
            buffer[offset + i] = filters[channelIndex].Transform(buffer[offset + i]);
        }

        return samplesRead;
    }
}
