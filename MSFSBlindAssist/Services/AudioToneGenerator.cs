using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Generates continuous audio tones for hand fly mode feedback.
/// Provides real-time frequency and stereo panning control for pitch/bank indication.
/// </summary>
public class AudioToneGenerator : IDisposable
{
    private WaveOutEvent? waveOut;
    private SignalGenerator? toneGenerator;
    private PanningSampleProvider? panningSampleProvider;
    private bool isPlaying;
    private readonly object lockObject = new();

    // Frequency range for pitch indication
    private const float MIN_FREQUENCY = 200f;  // Dive (negative pitch)
    private const float MAX_FREQUENCY = 800f;  // Climb (positive pitch)
    private const float CENTER_FREQUENCY = 500f; // Level flight

    // Bank angle range for full stereo panning
    private const double BANK_FULL_RANGE = 45.0; // ±45 degrees

    /// <summary>
    /// Starts continuous tone playback with initial frequency and panning.
    /// </summary>
    /// <param name="waveType">Wave type for tone generation.</param>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.5)
    {
        lock (lockObject)
        {
            if (isPlaying)
                return;

            try
            {
                // Create tone generator with center frequency
                toneGenerator = new SignalGenerator(44100, 1) // 44.1kHz, mono (channel 1)
                {
                    Frequency = CENTER_FREQUENCY,
                    Type = ConvertWaveType(waveType),
                    Gain = Math.Clamp(volume, 0.0, 1.0)
                };

                // Wrap in panning provider for stereo control
                panningSampleProvider = new PanningSampleProvider(toneGenerator)
                {
                    Pan = 0f // Center
                };

                // Initialize playback device
                waveOut = new WaveOutEvent
                {
                    NumberOfBuffers = 2,
                    DesiredLatency = 100 // Low latency for responsive feedback
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
        lock (lockObject)
        {
            if (!isPlaying)
                return;

            Cleanup();
            isPlaying = false;
        }
    }

    /// <summary>
    /// Updates the tone frequency based on pitch angle.
    /// </summary>
    /// <param name="pitchDegrees">Aircraft pitch in degrees (negative = nose down, positive = nose up).</param>
    public void UpdatePitch(double pitchDegrees)
    {
        lock (lockObject)
        {
            if (toneGenerator == null || !isPlaying)
                return;

            // Map pitch to frequency
            // -10° to +10° maps to 200-800 Hz (500 Hz center)
            // Clamp to reasonable pitch range
            double clampedPitch = Math.Clamp(pitchDegrees, -10.0, 10.0);

            // Linear mapping: pitch -> frequency
            // Formula: frequency = center + (pitch * range/20)
            double frequencyRange = (MAX_FREQUENCY - MIN_FREQUENCY) / 2.0; // 300 Hz
            float targetFrequency = (float)(CENTER_FREQUENCY + (clampedPitch * (frequencyRange / 10.0)));

            toneGenerator.Frequency = targetFrequency;
        }
    }

    /// <summary>
    /// Updates stereo panning based on bank angle.
    /// </summary>
    /// <param name="bankDegrees">Aircraft bank in degrees (negative = left, positive = right).</param>
    public void UpdateBank(double bankDegrees)
    {
        lock (lockObject)
        {
            if (panningSampleProvider == null || !isPlaying)
                return;

            // Map bank angle to stereo pan
            // -45° to +45° maps to -1.0 (full left) to +1.0 (full right)
            double clampedBank = Math.Clamp(bankDegrees, -BANK_FULL_RANGE, BANK_FULL_RANGE);
            float pan = (float)(clampedBank / BANK_FULL_RANGE);

            panningSampleProvider.Pan = pan;
        }
    }

    /// <summary>
    /// Updates volume level.
    /// </summary>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void UpdateVolume(double volume)
    {
        lock (lockObject)
        {
            if (toneGenerator == null)
                return;

            toneGenerator.Gain = Math.Clamp(volume, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Updates wave type.
    /// </summary>
    /// <param name="waveType">New wave type.</param>
    public void UpdateWaveType(HandFlyWaveType waveType)
    {
        lock (lockObject)
        {
            if (toneGenerator == null)
                return;

            toneGenerator.Type = ConvertWaveType(waveType);
        }
    }

    /// <summary>
    /// Gets whether tone is currently playing.
    /// </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// Converts HandFlyWaveType to NAudio SignalGeneratorType.
    /// </summary>
    private static SignalGeneratorType ConvertWaveType(HandFlyWaveType waveType)
    {
        return waveType switch
        {
            HandFlyWaveType.Sine => SignalGeneratorType.Sin,
            HandFlyWaveType.Triangle => SignalGeneratorType.Triangle,
            HandFlyWaveType.Sawtooth => SignalGeneratorType.SawTooth,
            HandFlyWaveType.Square => SignalGeneratorType.Square,
            _ => SignalGeneratorType.Sin
        };
    }

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
            toneGenerator = null;
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
