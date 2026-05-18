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

    // Default pitch→frequency mapping. Min = dive (negative pitch), max = climb (positive pitch),
    // center = level flight. Per-instance overrides via Configure(...) before Start().
    private const float DEFAULT_MIN_FREQUENCY = 200f;
    private const float DEFAULT_MAX_FREQUENCY = 800f;
    private const double DEFAULT_PITCH_RANGE_DEG = 10.0;
    private const double DEFAULT_BANK_RANGE_DEG = 10.0;  // bank (degrees) at which pan saturates to ±1.0

    // Effective mapping (defaults preserved when Configure is not called).
    private float minFrequency = DEFAULT_MIN_FREQUENCY;
    private float maxFrequency = DEFAULT_MAX_FREQUENCY;
    private double pitchRangeDeg = DEFAULT_PITCH_RANGE_DEG;
    private double bankRangeDeg = DEFAULT_BANK_RANGE_DEG;
    private float CenterFrequency => (minFrequency + maxFrequency) / 2f;

    /// <summary>
    /// Optional per-instance configuration for both axes of the attitude→audio mapping. Call
    /// BEFORE <see cref="Start"/> (config is captured at Start time).
    ///
    /// Defaults are 200–800 Hz over ±10° pitch and pan saturation at ±10° bank — appropriate for
    /// transport jets. Tightening the ranges (e.g., visual landing guidance uses ±8° pitch and
    /// ±5° bank by default) increases the matching slope: more Hz of beat per degree of pitch
    /// error, more pan delta per degree of bank error. The trade-off is earlier saturation
    /// outside the approach envelope. Widen the ranges for aircraft with larger attitude
    /// envelopes (aerobatic, fighter) via the aircraft's <c>VisualGuidanceProfile</c>.
    /// </summary>
    public void Configure(float minFrequencyHz, float maxFrequencyHz, double pitchRangeDegrees, double bankRangeDegrees)
    {
        if (isPlaying)
            return;  // mapping is captured at Start(); change before starting
        if (minFrequencyHz > 0 && maxFrequencyHz > minFrequencyHz && pitchRangeDegrees > 0 && bankRangeDegrees > 0)
        {
            minFrequency = minFrequencyHz;
            maxFrequency = maxFrequencyHz;
            pitchRangeDeg = pitchRangeDegrees;
            bankRangeDeg = bankRangeDegrees;
        }
    }

    /// <summary>
    /// Starts continuous tone playback with initial frequency and panning. Call
    /// <see cref="Configure"/> first if a non-default pitch→frequency mapping is needed.
    /// </summary>
    /// <param name="waveType">Wave type for tone generation.</param>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    /// <param name="frequency">Initial frequency in Hz. Pass a negative value (the default) to use
    ///   the configured centre frequency, which honours any prior <see cref="Configure"/> call.</param>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.5, double frequency = -1.0)
    {
        if (frequency < 0)
            frequency = CenterFrequency;
        lock (startStopLock)
        {
            if (isPlaying)
                return;

            try
            {
                // Create phase-continuous oscillator (eliminates clicks/pops)
                oscillator = new PhaseContinuousOscillator(44100, waveType, (float)frequency, volume);

                // Apply low-pass filter for sawtooth wave to remove harsh harmonics
                ISampleProvider audioSource = oscillator;
                if (waveType == HandFlyWaveType.Sawtooth)
                {
                    // Sawtooth needs cutoff at 1200 Hz due to rich harmonic content
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

        // Map pitch (degrees) to frequency (Hz). ±pitchRangeDeg saturates to min/max frequency;
        // 0° pitch sits at the centre frequency. Default mapping: ±10° → 200–800 Hz (500 Hz centre).
        double clampedPitch = Math.Clamp(pitchDegrees, -pitchRangeDeg, pitchRangeDeg);
        double halfFrequencyRange = (maxFrequency - minFrequency) / 2.0;
        double targetFrequency = CenterFrequency + (clampedPitch * (halfFrequencyRange / pitchRangeDeg));

        // Phase-continuous oscillator smoothly transitions to new frequency (no clicks/pops)
        oscillator.SetFrequency(targetFrequency);
    }

    /// <summary>
    /// Sets stereo panning directly.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pan">Pan value from -1.0 (full left) to +1.0 (full right).</param>
    public void SetPan(float pan)
    {
        if (panningSampleProvider == null || !isPlaying)
            return;

        panningSampleProvider.Pan = Math.Clamp(pan, -1.0f, 1.0f);
    }

    /// <summary>
    /// Updates stereo panning based on bank angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="bankDegrees">Aircraft bank in degrees using standard convention (negative = left, positive = right).</param>
    public void UpdateBank(double bankDegrees)
    {
        if (panningSampleProvider == null || !isPlaying)
            return;

        // Map bank angle to stereo pan using standard right-positive convention:
        //   ±bankRangeDeg → ±1.0 (full left / full right). Default ±10°; Configure() may narrow
        //   it (visual landing guidance defaults to ±5° for tighter pan precision near matched
        //   state). Positive bank (right wing down) → positive pan (right speaker).
        // NOTE: SimConnect's PLANE_BANK_DEGREES is left-positive; callers must negate before
        // passing in (VisualGuidanceManager does this via its StandardBank helper; HandFlyManager
        // negates inline). The PID's bank command output is already right-positive.
        double clampedBank = Math.Clamp(bankDegrees, -bankRangeDeg, bankRangeDeg);
        float pan = (float)(clampedBank / bankRangeDeg);

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
    /// Sets the oscillator frequency directly in Hz.
    /// Lock-free for smooth real-time updates via phase-continuous oscillator.
    /// </summary>
    /// <param name="frequencyHz">Frequency in Hz (e.g., 440.0 for A4).</param>
    public void SetFrequency(double frequencyHz)
    {
        if (oscillator == null || !isPlaying)
            return;

        oscillator.SetFrequency(frequencyHz);
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
