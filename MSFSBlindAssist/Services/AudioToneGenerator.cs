using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Generates continuous audio tones for hand fly mode feedback.
/// Provides real-time frequency and stereo panning control for pitch/bank indication.
/// Uses phase-continuous oscillator to eliminate clicks/pops during frequency changes.
/// </summary>
public class AudioToneGenerator : IDisposable
{
    private AudioOutputSession? session;

    // Last commanded tone state, replayed by RebindOutput onto a newly chosen device.
    private HandFlyWaveType lastWaveType = HandFlyWaveType.Sine;
    private double lastVolume = 0.5;
    private double lastFrequency = -1.0;
    private float lastPan;
    private string? lastDeviceIdOverride;
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
    /// The class-level defaults are 200–800 Hz over ±10° pitch and pan saturation at ±10° bank
    /// — appropriate for hand-fly mode's tone (which never calls Configure). Tightening the
    /// ranges increases the matching slope: more Hz of beat per degree of pitch error, more
    /// pan delta per degree of bank error. Visual landing guidance currently uses ±6° pitch
    /// (50 Hz/°) and ±5° bank (0.20 pan/°) by default — see <c>VisualGuidanceProfile</c>. The
    /// trade-off is earlier saturation outside the approach envelope. Widen the ranges for
    /// aircraft with larger attitude envelopes (aerobatic, fighter) via the profile.
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
    /// <param name="deviceIdOverride">
    /// Forwarded verbatim to <see cref="AudioOutputDeviceService.CreatePlayer"/> — see its
    /// three-state contract doc. <c>null</c> (the default, and what every feature tone
    /// passes — taxi steering, takeoff centerline, hand fly, visual guidance, docking beeps)
    /// means use the saved output-device setting; only the settings panel's device audition
    /// passes <c>""</c> or a real device id here. Never collapse <c>""</c> to <c>null</c>
    /// before calling.
    /// </param>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.5, double frequency = -1.0, string? deviceIdOverride = null)
    {
        lock (startStopLock)
        {
            if (isPlaying)
                return;

            StartLocked(waveType, volume, frequency, deviceIdOverride);
        }
    }

    /// <summary>
    /// Start body, assuming startStopLock is already held. Split out so RebindOutput can
    /// tear down and restart inside one critical section.
    /// </summary>
    private void StartLocked(HandFlyWaveType waveType, double volume, double frequency, string? deviceIdOverride)
    {
        if (frequency < 0)
            frequency = CenterFrequency;

        try
        {
            // The output is chosen first, because the oscillator has to be built at the
            // endpoint's OWN mix rate. Building at a fixed 44100 (as this did before the
            // device setting existed) makes NAudio insert its DMO resampler on the common
            // 48 kHz endpoint, and would make a rebind to a differently-clocked device play
            // the tone sharp.
            AudioOutputSession? opened = AudioOutputDeviceService.CreatePlayer(deviceIdOverride);
            if (opened == null)
            {
                Log.Warn("Services", "AudioToneGenerator start failed: no audio output device could be opened");
                return;
            }

            session = opened;

            oscillator = new PhaseContinuousOscillator(opened.MixSampleRate, waveType, (float)frequency, volume);

            ISampleProvider audioSource = oscillator;
            if (waveType == HandFlyWaveType.Sawtooth)
            {
                // Sawtooth needs cutoff at 1200 Hz due to rich harmonic content.
                // Preserves character (fundamental + 2nd harmonic) while removing harshness.
                audioSource = new LowPassFilterProvider(oscillator, 1200f, 0.707f);
            }

            panningSampleProvider = new PanningSampleProvider(audioSource)
            {
                Pan = 0f // Center
            };

            opened.Player.Init(panningSampleProvider);
            opened.Player.Play();

            lastWaveType = waveType;
            lastVolume = volume;
            lastFrequency = frequency;
            lastPan = 0f;
            lastDeviceIdOverride = deviceIdOverride;

            // Register BEFORE isPlaying flips true: if Register throws, the catch below calls
            // Cleanup(), which does not (and must not) reset isPlaying — so setting it early
            // would leave isPlaying == true with session/oscillator already nulled out, an
            // inconsistent state that also permanently blocks Start() from retrying until an
            // explicit Stop() is called.
            AudioOutputDeviceService.Register(this);
            isPlaying = true;
        }
        catch (Exception ex)
        {
            // Log error but don't crash - audio is optional feedback
            Log.Debug("Services", $"AudioToneGenerator start failed: {ex.Message}");
            Cleanup();
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
    /// Moves a sounding tone to the currently selected output device, preserving frequency,
    /// volume, waveform and pan. Called by AudioOutputDeviceService when the pilot changes
    /// the device, so a wrong device can be corrected mid-taxi without stopping guidance.
    ///
    /// This restarts through StartLocked rather than swapping the IWavePlayer alone, because
    /// the new endpoint may mix at a different sample rate — and an oscillator built for the
    /// old rate would play sharp under a swapped player. Costs roughly the output latency as
    /// a gap, only on a deliberate device change.
    ///
    /// Any Configure() mapping survives: min/max frequency and the pitch/bank ranges are
    /// separate fields that neither Cleanup nor StartLocked touches.
    /// </summary>
    internal void RebindOutput()
    {
        float panToRestore;

        lock (startStopLock)
        {
            if (!isPlaying)
                return;

            HandFlyWaveType waveType = lastWaveType;
            double volume = lastVolume;
            double frequency = lastFrequency;
            string? deviceIdOverride = lastDeviceIdOverride;
            panToRestore = lastPan;

            Cleanup();
            isPlaying = false;

            StartLocked(waveType, volume, frequency, deviceIdOverride);
        }

        // Outside the lock: SetPan takes no lock, and holding startStopLock across it buys
        // nothing.
        SetPan(panToRestore);
    }

    /// <summary>
    /// Updates the tone frequency based on pitch angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pitchDegrees">Aircraft pitch in degrees (negative = nose down, positive = nose up).</param>
    public void UpdatePitch(double pitchDegrees)
    {
        // Read the field into a local ONCE: this method takes no lock (by design, for
        // real-time smoothness), so RebindOutput can null the `oscillator` field on another
        // thread between the null-check and the use below. Re-reading the field for the use
        // would race; using the captured local cannot — worst case it writes into an
        // oscillator that RebindOutput has already orphaned, which is harmless, whereas
        // dereferencing a field that just went null is a NullReferenceException reachable
        // from any high-frequency caller (e.g. TaxiSteeringTone.SetTone at ~30 Hz).
        PhaseContinuousOscillator? osc = oscillator;
        if (osc == null || !isPlaying)
            return;

        // Map pitch (degrees) to frequency (Hz). ±pitchRangeDeg saturates to min/max frequency;
        // 0° pitch sits at the centre frequency. Default mapping: ±10° → 200–800 Hz (500 Hz centre).
        double clampedPitch = Math.Clamp(pitchDegrees, -pitchRangeDeg, pitchRangeDeg);
        double halfFrequencyRange = (maxFrequency - minFrequency) / 2.0;
        double targetFrequency = CenterFrequency + (clampedPitch * (halfFrequencyRange / pitchRangeDeg));

        // Phase-continuous oscillator smoothly transitions to new frequency (no clicks/pops)
        osc.SetFrequency(targetFrequency);
        lastFrequency = targetFrequency;
    }

    /// <summary>
    /// Sets stereo panning directly.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pan">Pan value from -1.0 (full left) to +1.0 (full right).</param>
    public void SetPan(float pan)
    {
        // See UpdatePitch for why the field is captured once into a local before use.
        PanningSampleProvider? panProvider = panningSampleProvider;
        if (panProvider == null || !isPlaying)
            return;

        lastPan = Math.Clamp(pan, -1.0f, 1.0f);
        panProvider.Pan = lastPan;
    }

    /// <summary>
    /// Updates stereo panning based on bank angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="bankDegrees">Aircraft bank in degrees using standard convention (negative = left, positive = right).</param>
    public void UpdateBank(double bankDegrees)
    {
        // See UpdatePitch for why the field is captured once into a local before use.
        PanningSampleProvider? panProvider = panningSampleProvider;
        if (panProvider == null || !isPlaying)
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

        lastPan = pan;
        panProvider.Pan = pan;
    }

    /// <summary>
    /// Updates volume level.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void UpdateVolume(double volume)
    {
        // See UpdatePitch for why the field is captured once into a local before use.
        PhaseContinuousOscillator? osc = oscillator;
        if (osc == null)
            return;

        osc.SetGain(volume);
        lastVolume = volume;
    }

    /// <summary>
    /// Updates wave type.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="waveType">New wave type.</param>
    public void UpdateWaveType(HandFlyWaveType waveType)
    {
        // See UpdatePitch for why the field is captured once into a local before use.
        PhaseContinuousOscillator? osc = oscillator;
        if (osc == null)
            return;

        osc.SetWaveType(waveType);
        lastWaveType = waveType;
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
            AudioOutputDeviceService.Unregister(this);
            session?.Dispose();
            session = null;
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
