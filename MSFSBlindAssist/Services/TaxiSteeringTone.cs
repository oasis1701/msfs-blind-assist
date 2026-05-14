using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Audio steering tone for taxi guidance.
///
/// Design: A continuous tone that uses STEREO PANNING to tell the pilot which way to steer.
/// - Pan LEFT (tone in left ear) = turn LEFT
/// - Pan RIGHT (tone in right ear) = turn RIGHT
/// - SILENT when heading is correct (aligned with target bearing)
///
/// No frequency or volume changes — only panning indicates correction direction and magnitude.
/// The tone generator is kept alive at zero volume to avoid audio glitches from rapid start/stop.
///
/// Heading error is calculated as: bearing_to_next_waypoint - aircraft_heading.
/// Negative = need to turn left, positive = need to turn right.
/// </summary>
public class TaxiSteeringTone : IDisposable
{
    private AudioToneGenerator? _toneGenerator;
    private volatile bool _isActive;
    private volatile bool _isPaused;
    private readonly object _lock = new();

    // Heading error thresholds (degrees) — HYSTERESIS to avoid start/stop flapping
    // from GPS/heading jitter at the threshold. Once silent, tone stays silent until
    // error exceeds the ACTIVATION threshold; once sounding, it stays on until error
    // drops below the SILENT threshold. The gap is the dead-band that kills flapping.
    //
    // These BASELINE thresholds are calibrated for a typical 60 ft taxiway.
    // On narrower pavement (e.g. 25 ft Code A taxiways at small airports),
    // the same heading error produces drift off centerline faster, so the
    // activation/silent thresholds scale down. On wide pavement (150 ft runways)
    // the thresholds scale up so the tone isn't constantly chirping at modest
    // heading errors. See UpdateHeadingError(headingErr, widthFt) overload.
    private const double SILENT_THRESHOLD_DEG = 3.0;        // Below this while playing → go silent
    private const double ACTIVATION_THRESHOLD_DEG = 6.0;    // Must exceed this to START playing
    private const double MAX_PAN_THRESHOLD_DEG = 30.0;      // At ±30° or more: full pan
    private const double BASELINE_WIDTH_FEET = 60.0;        // reference width for threshold scaling
    private const double MIN_SCALE = 0.65;                  // narrowest taxiways → tighter tolerance
    private const double MAX_SCALE = 1.40;                  // runway-wide → looser tolerance

    // Minimum sustain time after tone starts — prevents a single noisy sample
    // from triggering a brief chirp that immediately dies. User feedback: tone
    // was starting and stopping within ~500ms without actually turning.
    private const double MIN_SUSTAIN_MS = 400.0;

    // Tone parameters
    private const float TONE_FREQUENCY = 440f; // Fixed A4 tone

    private HandFlyWaveType _waveType = HandFlyWaveType.Sine;
    private double _configuredVolume = 0.05;
    private bool _isSilent = true;
    private DateTime _soundingSince = DateTime.MinValue;

    // Pulse mode: when on, the tone toggles on/off at PULSE_HZ instead of
    // sustaining continuously. Pan direction is unchanged. Used by the runway-
    // lineup phase to give a stopped-and-misaligned cue without speech (pilot's
    // hands/feet are on rudder + throttle, can't press hotkeys, and verbal cues
    // steal attention from rudder control). Phase derived from wall-clock so
    // the pulse keeps going at the right cadence even if UpdateHeadingError
    // is called at variable rates.
    private bool _pulseActive = false;
    private const double PULSE_HZ = 3.0;  // 3 cycles/second — clearly distinct from continuous

    /// <summary>
    /// When true, the stereo pan is inverted: a tone that would normally
    /// play in the right ear (telling the pilot to turn right) plays in
    /// the left ear instead, and vice versa. The pilot then steers
    /// AWAY from the tone to centre it. Mirrors TakeoffAssistInvertPanning.
    /// User-toggled in Taxi Guidance Options; the manager re-reads the
    /// setting on each call to <see cref="UpdateHeadingErrorWithThresholds"/>
    /// (cheap — single setting lookup, no allocations).
    /// </summary>
    public bool InvertPan { get; set; } = false;

    /// <summary>
    /// When true, the pan is forced to full left (-1) or full right (+1) —
    /// no intermediate values, no magnitude information. The pan curve
    /// (sqrt + 0.25 floor) is bypassed entirely. Useful for users on
    /// stereo speakers where partial pan is hard to tell apart from
    /// "centred" — a hard-panned tone comes out of one speaker only,
    /// unambiguously. Activation / silent thresholds still apply, so the
    /// tone still goes silent when within the silence band; the change is
    /// purely the audio output side. <see cref="InvertPan"/> still
    /// applies on top.
    /// </summary>
    public bool HardPan { get; set; } = false;

    public bool IsActive => _isActive;
    public bool IsTonePlaying => _isActive && !_isSilent && !_isPaused;

    /// <summary>
    /// Activates the steering tone system. No audible sound until heading error is detected.
    /// The tone generator is started immediately at zero volume to avoid startup latency.
    /// </summary>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.05)
    {
        lock (_lock)
        {
            _waveType = waveType;
            _configuredVolume = volume;
            _isActive = true;
            _isPaused = false;
            _isSilent = true;
            // Reset pulse mode on every Start so a previous session's pulse
            // state doesn't leak into a new route. Bug scenario: user enters
            // runway lineup → pulse fires (stopped + misaligned) → user stops
            // guidance mid-lineup → new route starts → first Taxiing UpdateHeadingError
            // call uses the width-scaled overload (which doesn't touch
            // _pulseActive), so the leaked-true pulse pulses the taxiing tone
            // at 3 Hz when it should be continuous.
            _pulseActive = false;

            // Create tone generator now, but at zero volume (silent)
            if (_toneGenerator == null)
            {
                _toneGenerator = new AudioToneGenerator();
                _toneGenerator.Start(_waveType, 0.0, TONE_FREQUENCY);
            }
        }
    }

    /// <summary>
    /// Stops the steering tone system entirely.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            _isActive = false;
            _isPaused = false;
            _isSilent = true;
            // Defense-in-depth: also clear pulse on Stop so any future Start
            // call inherits a clean state even if Start's reset is ever
            // accidentally removed.
            _pulseActive = false;
            DisposeToneGenerator();
        }
    }

    /// <summary>
    /// Updates the heading error. Called every position update.
    /// headingErrorDegrees: negative = need to turn left, positive = need to turn right.
    /// The tone pans in the direction the user should steer.
    /// </summary>
    public void UpdateHeadingError(double headingErrorDegrees)
    {
        UpdateHeadingError(headingErrorDegrees, BASELINE_WIDTH_FEET);
    }

    /// <summary>
    /// Width-aware overload. Scales the hysteresis thresholds based on pavement width
    /// so narrow taxiways (25 ft Code A) get tighter tolerances and wide runways get
    /// looser tolerances, matching how much heading error translates into lateral drift.
    /// </summary>
    public void UpdateHeadingError(double headingErrorDegrees, double pathWidthFeet)
    {
        // Width scaling: sqrt so scaling is less aggressive near the baseline.
        // 25 ft  → 0.65x  (2.0° silent / 3.9° activation / 19.5° max-pan)
        // 60 ft  → 1.00x  (3.0° / 6.0° / 30.0°)  ← baseline
        // 75 ft  → 1.12x  (3.35° / 6.7° / 33.5°)
        // 150 ft → 1.40x  (4.2° / 8.4° / 42°)    — wide runways
        double width = pathWidthFeet > 0 ? pathWidthFeet : BASELINE_WIDTH_FEET;
        double scale = Math.Sqrt(width / BASELINE_WIDTH_FEET);
        scale = Math.Clamp(scale, MIN_SCALE, MAX_SCALE);

        UpdateHeadingErrorWithThresholds(
            headingErrorDegrees,
            SILENT_THRESHOLD_DEG * scale,
            ACTIVATION_THRESHOLD_DEG * scale,
            MAX_PAN_THRESHOLD_DEG * scale);
    }

    /// <summary>
    /// Explicit-threshold overload. Bypasses width scaling so callers can demand
    /// tighter (or looser) hysteresis than the width-scaling system would pick.
    ///
    /// Used by runway lineup, where the width-scaled minimum (≈1.95° silent /
    /// 3.9° activation at width=25, MIN_SCALE=0.65) is still too loose: a pilot
    /// approaching alignment from a 10° error releases rudder pressure when the
    /// tone goes silent at ~2°, drifts back to ~3° on the way to settling, and
    /// the tone STAYS silent because 3° is below the 3.9° activation threshold.
    /// Result: aircraft sits 3° off heading with no audio cue. For takeoff
    /// precision that's already 5% sideways drift per second of takeoff roll.
    /// Callers that need tight precision pass thresholds in the 0.5° / 1° / 15°
    /// range so the tone keeps panning until the heading is genuinely centered.
    /// </summary>
    public void UpdateHeadingErrorWithThresholds(
        double headingErrorDegrees,
        double silentThresholdDeg,
        double activationThresholdDeg,
        double maxPanThresholdDeg)
    {
        // Hold _lock so Stop()/Dispose() cannot null out _toneGenerator mid-call.
        // Critical section is cheap (no I/O, audio mixer is already async).
        // Without this, SimConnect-thread UpdateHeadingError could race with
        // UI-thread StopGuidance → Stop → DisposeToneGenerator and call
        // SetPan/UpdateVolume on a freed NAudio buffer.
        lock (_lock)
        {
        if (!_isActive || _isPaused) return;

        double absError = Math.Abs(headingErrorDegrees);

        // Hysteresis state machine (kills threshold-boundary flapping):
        //   CURRENTLY SILENT: stay silent unless |err| > activation threshold
        //   CURRENTLY SOUNDING: keep sounding until |err| < silent threshold
        //                       AND tone has been sounding at least MIN_SUSTAIN_MS.
        if (_isSilent)
        {
            if (absError <= activationThresholdDeg)
                return; // not enough error to activate — stay silent
        }
        else
        {
            // Sounding. Only allow silencing once we've sustained for MIN_SUSTAIN_MS
            // (prevents a transient spike from producing a 50ms blip).
            if (absError <= silentThresholdDeg &&
                (DateTime.UtcNow - _soundingSince).TotalMilliseconds >= MIN_SUSTAIN_MS)
            {
                SetSilent();
                return;
            }
        }

        // Pan magnitude: hard-pan mode forces ±1 regardless of error
        // magnitude (for speaker users — single-side audio is unambiguous);
        // otherwise the regular sqrt curve gives proportional feedback.
        double normalizedError;
        if (HardPan)
        {
            normalizedError = 1.0;
        }
        else
        {
            // Non-linear (square root) so small errors get audibly panned.
            // sqrt makes the first ramp of pan cover ~40% of the stereo field.
            // Normalized: 0 at activation threshold, 1 at max-pan threshold.
            double rawNorm = (absError - activationThresholdDeg) /
                             (maxPanThresholdDeg - activationThresholdDeg);
            rawNorm = Math.Clamp(rawNorm, 0.0, 1.0);

            // 0.25 floor so the very first moment of activation is already
            // audibly off-center (not dead silent just because error == activation+ε).
            normalizedError = 0.25 + 0.75 * Math.Sqrt(rawNorm);
            normalizedError = Math.Min(normalizedError, 1.0);
        }

        // Pan in the direction the user should turn
        // headingError > 0 = need to turn right = pan RIGHT (positive)
        // headingError < 0 = need to turn left = pan LEFT (negative)
        float pan = (float)(Math.Sign(headingErrorDegrees) * normalizedError);
        if (InvertPan) pan = -pan;

        SetTone(pan);
        } // end lock(_lock)
    }

    /// <summary>
    /// Pauses the tone (e.g., at hold-short points).
    /// </summary>
    public void Pause()
    {
        lock (_lock)
        {
            _isPaused = true;
            SetSilent();
        }
    }

    /// <summary>
    /// Resumes after pause.
    /// </summary>
    public void Resume()
    {
        lock (_lock)
        {
            _isPaused = false;
        }
    }

    /// <summary>
    /// Toggles pulse mode on/off. When on, the tone (if currently sounding)
    /// alternates volume between configured and zero at PULSE_HZ. Pan is
    /// unaffected — the user still hears the direction-of-correction in the
    /// stereo image, but the rhythmic on/off makes "stopped and not aligned"
    /// audibly distinct from "moving and slightly off". When off, the tone
    /// behaves as before (continuous at configured volume when sounding).
    /// </summary>
    public void SetPulse(bool active)
    {
        lock (_lock)
        {
            _pulseActive = active;
        }
    }

    private void SetSilent()
    {
        if (_isSilent) return;
        _isSilent = true;
        _toneGenerator?.UpdateVolume(0.0);
        _toneGenerator?.SetPan(0f);
    }

    private void SetTone(float pan)
    {
        if (_toneGenerator == null) return;

        if (_isSilent)
        {
            _isSilent = false;
            _soundingSince = DateTime.UtcNow;
        }

        // Always refresh volume each frame the tone is sounding. This must run
        // regardless of _pulseActive — otherwise a pulse→continuous transition
        // can leave the tone stuck at zero volume. Bug scenario: stopped and
        // misaligned → pulse fires (volume alternates 0 / configured at 3 Hz)
        // → pilot starts moving → SetPulse(false) → next frame _pulseActive is
        // false, so the previous "only refresh in pulse mode" path skipped
        // UpdateVolume. If the last pulse cycle had set volume to 0 (silent
        // half), the tone stayed silent in continuous mode until something
        // else (going silent and re-activating, e.g., oversteer) reset it.
        // A 30 Hz UpdateVolume call is cheap; correctness beats the micro-op.
        _toneGenerator.UpdateVolume(EffectiveVolume());

        _toneGenerator.SetPan(Math.Clamp(pan, -1f, 1f));
    }

    /// <summary>
    /// Returns the volume to apply this frame: configured volume normally,
    /// or 0 / configured alternating at PULSE_HZ when pulse mode is on.
    /// Phase is derived from UTC ticks so the cadence is perfectly steady
    /// regardless of caller jitter.
    /// </summary>
    private double EffectiveVolume()
    {
        if (!_pulseActive) return _configuredVolume;

        double periodMs = 1000.0 / PULSE_HZ;
        double halfPeriodMs = periodMs * 0.5;
        long ms = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        bool on = ((ms / (long)halfPeriodMs) % 2L) == 0L;
        return on ? _configuredVolume : 0.0;
    }

    private void DisposeToneGenerator()
    {
        _toneGenerator?.Stop();
        _toneGenerator?.Dispose();
        _toneGenerator = null;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
