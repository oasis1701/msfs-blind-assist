using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Generates continuous audio tones for hand fly mode feedback.
/// Provides real-time frequency and stereo panning control for pitch/bank indication.
/// Uses phase-continuous oscillator to eliminate clicks/pops during frequency changes.
/// </summary>
public class AudioToneGenerator : IDisposable
{
    private WaveOutEvent? waveOut;
    private PhaseContinuousOscillator? oscillator;
    private PanningSampleProvider? panningSampleProvider;
    private volatile bool isPlaying;
    private readonly object startStopLock = new(); // Only for Start/Stop, not audio updates

    // Frequency range for pitch indication
    private const float MIN_FREQUENCY = 200f;  // Dive (negative pitch)
    private const float MAX_FREQUENCY = 800f;  // Climb (positive pitch)
    private const float CENTER_FREQUENCY = 500f; // Level flight

    // Bank angle range for full stereo panning
    private const double BANK_FULL_RANGE = 20.0; // ±20 degrees

    /// <summary>
    /// Starts continuous tone playback with initial frequency and panning.
    /// </summary>
    /// <param name="waveType">Wave type for tone generation.</param>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.5)
    {
        lock (startStopLock)
        {
            if (isPlaying)
                return;

            try
            {
                // Create phase-continuous oscillator (eliminates clicks/pops)
                oscillator = new PhaseContinuousOscillator(44100, waveType, CENTER_FREQUENCY, volume);

                // Apply low-pass filter for square and sawtooth waves to remove harsh harmonics
                ISampleProvider audioSource = oscillator;
                if (waveType == HandFlyWaveType.Square)
                {
                    // Filter cutoff at 1600 Hz removes harsh 5th+ harmonics
                    // Q=0.707 gives smooth Butterworth response (no resonance)
                    audioSource = new LowPassFilterProvider(oscillator, 1600f, 0.707f);
                }
                else if (waveType == HandFlyWaveType.Sawtooth)
                {
                    // Sawtooth needs lower cutoff (1200 Hz) due to richer harmonic content
                    // Preserves character (fundamental + 2nd harmonic) while removing harshness
                    audioSource = new LowPassFilterProvider(oscillator, 1200f, 0.707f);
                }

                // Wrap in panning provider for stereo control
                panningSampleProvider = new PanningSampleProvider(audioSource)
                {
                    Pan = 0f // Center
                };

                // Initialize playback device with increased latency to prevent buffer underruns
                waveOut = new WaveOutEvent
                {
                    NumberOfBuffers = 2,
                    DesiredLatency = 150 // Increased from 100ms to prevent crackling
                };

                waveOut.Init(panningSampleProvider);
                waveOut.Play();
                isPlaying = true;
            }
            catch (Exception ex)
            {
                // Log error but don't crash - audio is optional feedback
                System.Diagnostics.Debug.WriteLine($"AudioToneGenerator start failed: {ex.Message}");
                Cleanup();
            }
        }
    }

    /// <summary>
    /// Stops tone playback.
    /// </summary>
    public void Stop()
    {
        lock (startStopLock)
        {
            if (!isPlaying)
                return;

            Cleanup();
            isPlaying = false;
        }
    }

    /// <summary>
    /// Updates the tone frequency based on pitch angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pitchDegrees">Aircraft pitch in degrees (negative = nose down, positive = nose up).</param>
    public void UpdatePitch(double pitchDegrees)
    {
        if (oscillator == null || !isPlaying)
            return;

        // Map pitch to frequency
        // -10° to +10° maps to 200-800 Hz (500 Hz center)
        // Clamp to reasonable pitch range
        double clampedPitch = Math.Clamp(pitchDegrees, -10.0, 10.0);

        // Linear mapping: pitch -> frequency
        // Formula: frequency = center + (pitch * range/20)
        double frequencyRange = (MAX_FREQUENCY - MIN_FREQUENCY) / 2.0; // 300 Hz
        double targetFrequency = CENTER_FREQUENCY + (clampedPitch * (frequencyRange / 10.0));

        // Phase-continuous oscillator smoothly transitions to new frequency (no clicks/pops)
        oscillator.SetFrequency(targetFrequency);
    }

    /// <summary>
    /// Updates stereo panning based on bank angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="bankDegrees">Aircraft bank in degrees (negative = left, positive = right).</param>
    public void UpdateBank(double bankDegrees)
    {
        if (panningSampleProvider == null || !isPlaying)
            return;

        // Map bank angle to stereo pan
        // -20° to +20° maps to -1.0 (full left) to +1.0 (full right)
        double clampedBank = Math.Clamp(bankDegrees, -BANK_FULL_RANGE, BANK_FULL_RANGE);
        float pan = -(float)(clampedBank / BANK_FULL_RANGE); // Negate to fix inverted panning

        panningSampleProvider.Pan = pan;
    }

    /// <summary>
    /// Updates volume level.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void UpdateVolume(double volume)
    {
        if (oscillator == null)
            return;

        oscillator.SetGain(volume);
    }

    /// <summary>
    /// Updates wave type.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="waveType">New wave type.</param>
    public void UpdateWaveType(HandFlyWaveType waveType)
    {
        if (oscillator == null)
            return;

        oscillator.SetWaveType(waveType);
    }

    /// <summary>
    /// Gets whether tone is currently playing.
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
            oscillator = null;
            panningSampleProvider = null;
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
