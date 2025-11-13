using NAudio.Wave;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Phase-continuous wavetable oscillator with smooth frequency transitions.
/// Eliminates clicks/pops when frequency changes by maintaining phase continuity.
/// </summary>
public class PhaseContinuousOscillator : ISampleProvider
{
    private readonly int sampleRate;
    private readonly float[] wavetable;
    private readonly int wavetableSize;

    private double phase = 0.0;
    private double currentPhaseStep; // Lock-free updates (tearing is acceptable for audio)
    private double targetPhaseStep;
    private double gain;

    private const double PORTAMENTO_SMOOTHING = 0.2; // 20% interpolation per sample (fast, smooth)
    private const int WAVETABLE_SIZE = 4096; // Power of 2 for efficient modulo

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// Creates a new phase-continuous oscillator.
    /// </summary>
    /// <param name="sampleRate">Sample rate in Hz (e.g., 44100).</param>
    /// <param name="waveType">Waveform type.</param>
    /// <param name="initialFrequency">Initial frequency in Hz.</param>
    /// <param name="initialGain">Initial gain (0.0 to 1.0).</param>
    public PhaseContinuousOscillator(int sampleRate, HandFlyWaveType waveType, double initialFrequency, double initialGain)
    {
        this.sampleRate = sampleRate;
        this.gain = initialGain;

        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1); // Mono

        // Pre-compute wavetable
        wavetableSize = WAVETABLE_SIZE;
        wavetable = new float[wavetableSize];
        GenerateWavetable(waveType);

        // Initialize phase step
        currentPhaseStep = initialFrequency * wavetableSize / sampleRate;
        targetPhaseStep = currentPhaseStep;
    }

    /// <summary>
    /// Sets target frequency (smoothly transitions to avoid clicks).
    /// </summary>
    public void SetFrequency(double frequency)
    {
        targetPhaseStep = frequency * wavetableSize / sampleRate;
    }

    /// <summary>
    /// Sets gain/volume (0.0 to 1.0).
    /// </summary>
    public void SetGain(double newGain)
    {
        gain = Math.Clamp(newGain, 0.0, 1.0);
    }

    /// <summary>
    /// Updates waveform type (regenerates wavetable).
    /// </summary>
    public void SetWaveType(HandFlyWaveType waveType)
    {
        GenerateWavetable(waveType);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            // Smooth portamento: interpolate current step toward target
            currentPhaseStep += (targetPhaseStep - currentPhaseStep) * PORTAMENTO_SMOOTHING;

            // Advance phase
            phase += currentPhaseStep;

            // Wrap phase (keeps it within wavetable bounds)
            while (phase >= wavetableSize)
                phase -= wavetableSize;
            while (phase < 0)
                phase += wavetableSize;

            // Read from wavetable with linear interpolation
            int index0 = (int)phase;
            int index1 = (index0 + 1) % wavetableSize;
            double frac = phase - index0;

            float sample = (float)((wavetable[index0] * (1.0 - frac)) + (wavetable[index1] * frac));

            // Apply gain
            buffer[offset + i] = sample * (float)gain;
        }

        return count;
    }

    /// <summary>
    /// Generates wavetable for specified waveform type.
    /// </summary>
    private void GenerateWavetable(HandFlyWaveType waveType)
    {
        switch (waveType)
        {
            case HandFlyWaveType.Sine:
                GenerateSineWave();
                break;
            case HandFlyWaveType.Triangle:
                GenerateTriangleWave();
                break;
            case HandFlyWaveType.Sawtooth:
                GenerateSawtoothWave();
                break;
            case HandFlyWaveType.Square:
                GenerateSquareWave();
                break;
        }
    }

    private void GenerateSineWave()
    {
        for (int i = 0; i < wavetableSize; i++)
        {
            double angle = 2.0 * Math.PI * i / wavetableSize;
            wavetable[i] = (float)Math.Sin(angle);
        }
    }

    private void GenerateTriangleWave()
    {
        for (int i = 0; i < wavetableSize; i++)
        {
            double t = (double)i / wavetableSize;
            if (t < 0.25)
                wavetable[i] = (float)(4.0 * t);
            else if (t < 0.75)
                wavetable[i] = (float)(2.0 - 4.0 * t);
            else
                wavetable[i] = (float)(4.0 * t - 4.0);
        }
    }

    private void GenerateSawtoothWave()
    {
        for (int i = 0; i < wavetableSize; i++)
        {
            double t = (double)i / wavetableSize;
            wavetable[i] = (float)(2.0 * t - 1.0);
        }
    }

    private void GenerateSquareWave()
    {
        // Generate "warm sine" - fundamental sine + second harmonic for richness
        // This creates a smooth, warm tone distinct from pure sine
        for (int i = 0; i < wavetableSize; i++)
        {
            double angle = 2.0 * Math.PI * i / wavetableSize;
            double fundamental = Math.Sin(angle);                    // Pure sine
            double secondHarmonic = Math.Sin(2.0 * angle) * 0.25;   // 2x frequency at 25% amplitude
            wavetable[i] = (float)(fundamental + secondHarmonic);
        }
    }
}
