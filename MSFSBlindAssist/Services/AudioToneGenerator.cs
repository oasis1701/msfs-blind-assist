using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Generates continuous audio tones for hand fly mode feedback.
/// Provides real-time frequency and stereo panning control for pitch/bank indication.
/// Uses phase-continuous oscillator to eliminate clicks/pops during frequency changes.
///
/// REGISTRATION LIFETIME — from CONSTRUCTION until <see cref="Stop"/>/<see cref="Dispose"/>,
/// deliberately NOT "for as long as it is sounding". Registration is what makes a generator
/// visible to <see cref="AudioOutputRouter"/>'s routing sweeps, so tying it to sounding made
/// exactly the tones that most needed help invisible: a start whose open failed returned
/// before registering, and a sounding tone whose rebind failed was removed from the registry
/// on the way down and never put back. Either way the owner was still holding a non-null
/// generator that could never make a sound again, and no sweep could reach it. Registration
/// now means "this generator is alive and its owner has not stopped it", which is precisely
/// the set a sweep should be retrying — <see cref="RebindTo"/> is that retry, and it acts on a
/// generator that is sounding OR waiting for an endpoint (<see cref="NeedsDevice"/>).
///
/// LOCK ORDER — owner lock -> this class's <c>startStopLock</c> -> AudioOutputRouter's
/// <c>Gate</c>, never the reverse. <see cref="CurrentDeviceId"/> and <see cref="NeedsDevice"/>
/// are read by a sweep while it holds Gate, so both must stay lock-free field reads.
/// </summary>
public class AudioToneGenerator : IDisposable
{
    // Injectable so a test (or a future per-feature routing scheme) can hand this generator a
    // router of its own; defaults to the process-wide instance so every existing
    // `new AudioToneGenerator()` site keeps behaving exactly as before. EVERY router call in
    // this class goes through this field — reaching for AudioOutputRouter.Shared anywhere
    // below would make a constructed router's registry unreachable and turn the seam into
    // decoration.
    //
    // NULLABLE because resolving the shared router can fail — see the constructor. A null
    // router degrades to no registration, no sweeps and no tone; it is never an exception at
    // an owner.
    private readonly AudioOutputRouter? router;

    private AudioOutputSession? session;

    // Last commanded tone state, replayed by RebindTo onto a newly chosen device.
    private HandFlyWaveType lastWaveType = HandFlyWaveType.Sine;
    private double lastVolume = 0.5;
    private double lastFrequency = -1.0;
    private float lastPan;

    // The OWNER's device choice, in OpenFor's three-state encoding (null = the saved setting,
    // "" = explicitly the Windows default, anything else = that endpoint). Written by Start()
    // ONLY — never by StartLocked, and so never by a rebind: a rebind that recorded the
    // sweep's target here would pin the tone to that sweep's endpoint forever, and the next
    // settings change could no longer move it.
    private string? lastDeviceIdOverride;

    // Whether the chain built by the last successful StartLocked contains the sawtooth
    // low-pass. UpdateWaveType compares against it to notice a change of sawtooth-ness, which
    // the chain cannot absorb in place. Volatile because that read is lock-free.
    private volatile bool filterInChain;

    // Whether this generator is currently in the router's registry. Guarded by startStopLock.
    // Needed because AudioOutputRouter.Register APPENDS: registering twice puts two entries in
    // the registry, and every later sweep would then tear this one tone down and restart it
    // twice for a single device change.
    private bool registered;

    private PhaseContinuousOscillator? oscillator;
    private PanningSampleProvider? panningSampleProvider;
    private volatile bool isPlaying;
    private readonly object startStopLock = new(); // Only for Start/Stop, not audio updates

    // Where this tone is ACTUALLY playing, and whether it is waiting for an endpoint. Read by
    // AudioOutputRouter's sweep to decide whether this generator has to move: a tone needs to
    // move iff the endpoint it is bound to is not the endpoint the router resolved. That is a
    // per-generator fact, which is exactly what the retired process-global "last applied
    // device id" could not represent.
    //
    // BOTH ACCESSORS MUST STAY LOCK-FREE. The sweep reads them while holding the router's
    // Gate, and the lock order is owner -> startStopLock -> Gate; taking startStopLock from
    // inside either accessor would invert it. `volatile` gives the sweep a coherent read of
    // each field without any lock at all.
    private volatile string currentDeviceId = string.Empty;
    private volatile bool needsDevice;

    /// <summary>The endpoint id this tone is currently bound to, or empty when it is not
    /// bound to anything. Lock-free by contract — see the field comment.</summary>
    internal string CurrentDeviceId => currentDeviceId;

    /// <summary>Set when an open failed or the endpoint went away underneath this tone, so
    /// the next routing sweep always moves it regardless of what it is nominally bound to.
    /// Lock-free by contract — see the field comment.</summary>
    internal bool NeedsDevice => needsDevice;

    /// <summary>
    /// The router is injectable so tests and future per-feature routing can supply their own;
    /// it defaults to the process-wide instance so the existing construction sites stay
    /// byte-identical.
    ///
    /// Registration happens HERE, not at the first Start — see the class doc. Publishing
    /// `this` into a registry from a constructor is safe in this one case, but NOT because
    /// Register is the last statement (it is not — `registered` and `router` are assigned
    /// after it). It is safe because of WHAT a sweep can reach: everything a sweep reads
    /// directly — currentDeviceId, needsDevice, isPlaying, startStopLock — is a field
    /// initializer, and those all run before any constructor body; and the one call it can
    /// make, RebindTo, takes startStopLock and returns false at once on a generator that is
    /// neither playing nor waiting for a device, so it cannot reach `router` before the line
    /// below assigns it.
    ///
    /// NOTHING HERE MAY THROW. This class's contract is that audio degrades and never throws,
    /// and TakeoffAssistManager, ProximityBeeper and TaxiSteeringTone all construct with no
    /// try of their own. Resolving Shared is a real throw surface: it is a Lazy under
    /// ExecutionAndPublication whose factory starts the router's worker thread, and a Lazy
    /// CACHES its factory's exception permanently, so once it fails every later construction
    /// fails identically. A router that cannot be had leaves the field null and every router
    /// call in this class a no-op — silent, never fatal. A Register that throws leaves
    /// `registered` false, which EnsureRegisteredLocked simply retries at the first Start.
    /// </summary>
    public AudioToneGenerator(AudioOutputRouter? router = null)
    {
        AudioOutputRouter? resolved = null;
        try
        {
            resolved = router ?? AudioOutputRouter.Shared;
            resolved.Register(this);
            registered = true;
        }
        catch (Exception ex)
        {
            Log.Debug("Services", $"AudioToneGenerator could not register with the audio router: {ex.Message}");
        }

        this.router = resolved;
    }

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
    /// Forwarded verbatim to <see cref="AudioOutputRouter.OpenFor"/> — see its
    /// three-state contract doc. <c>null</c> (the default, and what every feature tone
    /// passes — taxi steering, takeoff centerline, hand fly, visual guidance, docking beeps)
    /// means use the saved output-device setting; only the settings panel's device audition
    /// passes <c>""</c> or a real device id here. Never collapse <c>""</c> to <c>null</c>
    /// before calling.
    ///
    /// It is also REMEMBERED, and a non-null value OUTRANKS a routing sweep's target for the
    /// life of this tone — see <see cref="RebindTo"/>. That is what stops a settings save from
    /// dragging a device audition off the very device it is auditioning.
    /// </param>
    public void Start(HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.5, double frequency = -1.0, string? deviceIdOverride = null)
    {
        lock (startStopLock)
        {
            if (isPlaying)
                return;

            // The owner's choice, recorded on the owner's own call and nowhere else. Recorded
            // BEFORE StartLocked so it survives an open that fails: the retry a later sweep
            // makes has to ask for the same device this owner asked for.
            lastDeviceIdOverride = deviceIdOverride;

            StartLocked(waveType, volume, frequency, deviceIdOverride, 0f);
        }
    }

    /// <summary>
    /// Start body, assuming startStopLock is already held. Split out so RebindTo can
    /// tear down and restart inside one critical section.
    /// </summary>
    /// <param name="initialPan">Pan to apply to the new chain. <see cref="Start"/> passes 0
    /// (centre); <see cref="RebindTo"/> passes the pan the tone was already at, so a device
    /// change never silently re-centres a steering cue.</param>
    private void StartLocked(HandFlyWaveType waveType, double volume, double frequency, string? deviceIdOverride, float initialPan)
    {
        if (frequency < 0)
            frequency = CenterFrequency;

        float pan = Math.Clamp(initialPan, -1.0f, 1.0f);

        // Recorded BEFORE the open is attempted rather than after it succeeds. RebindTo
        // replays these fields, and the replay that matters most is the one for a start that
        // FAILED — a tone that never opened has to come back as the tone its owner asked for,
        // not as the class defaults (Sine, 0.5, centre frequency, centred) that a
        // bottom-of-method assignment would have left standing.
        lastWaveType = waveType;
        lastVolume = volume;
        lastFrequency = frequency;
        lastPan = pan;

        try
        {
            // Registered BEFORE the open, and regardless of whether it works. Registration
            // means "alive and not stopped", so it must cover a start that fails too — that is
            // the whole mechanism by which a failed open is retried. Idempotent via the
            // `registered` flag, which also RE-registers a generator whose owner stopped it and
            // then started it again (TakeoffAssistManager reuses one instance for every
            // activation, so without this its centerline tone would be invisible to every
            // sweep after the first deactivation).
            EnsureRegisteredLocked();

            // The output is chosen first, because the oscillator has to be built at the
            // endpoint's OWN mix rate. Building at a fixed 44100 (as this did before the
            // device setting existed) makes NAudio insert its DMO resampler on the common
            // 48 kHz endpoint, and would make a rebind to a differently-clocked device play
            // the tone sharp.
            AudioOutputSession? opened = router?.OpenFor(deviceIdOverride);
            if (opened == null)
            {
                // Nothing opened at all. The generator STAYS REGISTERED with needsDevice set,
                // so the next sweep — a settings save, or (Task 7) a device arriving — names
                // it, calls RebindTo, and retries the open. Until then it is simply silent,
                // which is the correct degradation for optional feedback.
                needsDevice = true;
                currentDeviceId = string.Empty;
                Log.Warn("Services", "AudioToneGenerator start failed: no audio output device could be opened");
                return;
            }

            session = opened;
            currentDeviceId = opened.DeviceId ?? string.Empty;

            // NAudio's WasapiOut render loop catches AUDCLNT_E_DEVICE_INVALIDATED into a local
            // and hands it to RaisePlaybackStopped, which DISCARDS it when nothing is
            // subscribed -- and that throw path never reaches `playbackState = Stopped`. So
            // without this handler an endpoint yanked mid-flight left isPlaying true forever:
            // Start() early-returned, the owner kept feeding a dead stream, and the pilot's
            // only evidence was the absence of a sound they were steering by. Subscribed
            // BEFORE Init/Play, so a failure in either is still reported through it; Cleanup
            // detaches it again before the session is disposed.
            opened.Player.PlaybackStopped += OnPlaybackStopped;

            oscillator = new PhaseContinuousOscillator(opened.MixSampleRate, waveType, (float)frequency, volume);

            ISampleProvider audioSource = oscillator;
            bool wantsFilter = waveType == HandFlyWaveType.Sawtooth;
            if (wantsFilter)
            {
                // Sawtooth needs cutoff at 1200 Hz due to rich harmonic content.
                // Preserves character (fundamental + 2nd harmonic) while removing harshness.
                audioSource = new LowPassFilterProvider(oscillator, 1200f, 0.707f);
            }

            filterInChain = wantsFilter;

            panningSampleProvider = new PanningSampleProvider(audioSource)
            {
                // Set BEFORE Init/Play: WasapiOut's play thread fills the whole first buffer
                // (LatencyMs worth) before starting the client, so a pan restored after Play()
                // cannot reach it. For the taxi steering and takeoff centerline tones a centred
                // pan IS the "you are on the centreline" cue, so that first buffer would be a
                // wrong steering command, not a missing one.
                Pan = pan
            };

            opened.Player.Init(panningSampleProvider);
            opened.Player.Play();

            // Both flags flip only once there is a working, playing chain. isPlaying in
            // particular must not flip early: Cleanup() does not (and must not) reset it, so a
            // throw after an early set would leave isPlaying == true with session/oscillator
            // already nulled — an inconsistent state that also permanently blocks Start() from
            // retrying until an explicit Stop().
            needsDevice = false;
            isPlaying = true;

            // CATCH-UP. A command that arrived while OpenFor was constructing the WasapiOut
            // (a WASAPI enumerate plus an IAudioClient activation -- much the slowest part of
            // a start) wrote its snapshot field and then found a null oscillator, so the chain
            // above was built from the PARAMETERS rather than from the latest values. Re-apply
            // them now. ProximityBeeper's solid stop-tone is the case that cannot self-heal --
            // it sets the volume once and latches -- so without this the docking tone can come
            // back silent and stay silent for the rest of the dock. Pan has the same shape but
            // self-heals, because taxi steering rewrites it every position update.
            oscillator.SetGain(lastVolume);
            panningSampleProvider.Pan = lastPan;
        }
        catch (Exception ex)
        {
            // Log error but don't crash - audio is optional feedback
            Log.Debug("Services", $"AudioToneGenerator start failed: {ex.Message}");
            Cleanup();

            // AFTER Cleanup, so what survives is the failure. Every failure exit from this
            // method sets this and every success clears it, which is what makes the flag mean
            // exactly "this generator does not have a working output right now".
            needsDevice = true;
        }
    }

    /// <summary>
    /// Stops tone playback and takes this generator out of the router's registry — a tone its
    /// owner has stopped is not one a sweep should be moving or retrying.
    /// </summary>
    public void Stop()
    {
        lock (startStopLock)
        {
            // Unregistered FIRST and UNCONDITIONALLY, ahead of the isPlaying test: a start
            // whose open failed is registered while NOT playing, and Stop is the only thing
            // that can take it back out.
            UnregisterLocked();

            // Nothing is waiting for an endpoint any more, because nothing wants one.
            needsDevice = false;

            if (!isPlaying)
                return;

            Cleanup();
            isPlaying = false;
        }
    }

    /// <summary>
    /// Moves a tone to <paramref name="targetDeviceId"/>, preserving frequency, volume,
    /// waveform and pan. Called by AudioOutputRouter's worker thread, never by a tone owner —
    /// so a wrong device can be corrected mid-taxi without stopping guidance. Returns whether
    /// the tone is sounding afterwards.
    ///
    /// It acts on a generator that is SOUNDING **or** on one merely waiting for an endpoint
    /// (<see cref="NeedsDevice"/>): the second case is the retry for a start whose open failed,
    /// and is why registration outlives sounding at all. A generator that is neither —
    /// constructed but never started, or sitting between an owner's Stop and its next Start —
    /// is left alone and reports false; a sweep must never start a tone nobody asked for.
    ///
    /// WHICH DEVICE: a non-null <c>deviceIdOverride</c> given at <see cref="Start"/> WINS over
    /// the sweep's target, and the three-state encoding survives intact ("" is not null, so it
    /// keeps meaning "explicitly the Windows default"). Only a tone that asked for "the saved
    /// setting" follows the sweep. Without that rule a settings save would drag the settings
    /// panel's device audition onto the saved device — silently breaking the one control built
    /// to prove which device is which. The router still passes the RESOLVED endpoint id rather
    /// than the saved preference, so the tones that do follow it all land on one endpoint from
    /// one decision instead of re-resolving per generator.
    ///
    /// This restarts through StartLocked rather than swapping the IWavePlayer alone, because
    /// the new endpoint may mix at a different sample rate — and an oscillator built for the
    /// old rate would play sharp under a swapped player. Costs roughly the output latency as
    /// a gap, only on a deliberate device change.
    ///
    /// Any Configure() mapping survives: min/max frequency and the pitch/bank ranges are
    /// separate fields that neither Cleanup nor StartLocked touches.
    /// </summary>
    internal bool RebindTo(string targetDeviceId)
    {
        lock (startStopLock)
        {
            if (!isPlaying && !needsDevice)
                return false;

            string? deviceIdOverride = lastDeviceIdOverride;
            if (deviceIdOverride == null && !string.IsNullOrWhiteSpace(targetDeviceId))
            {
                // No override of this tone's own, so follow the sweep. A BLANK target is
                // treated as "no target named" and falls through to null (the saved setting):
                // the router never plans a rebind against a blank id, and UpdateWaveType's
                // restart-in-place passes CurrentDeviceId, which is blank when the endpoint
                // never reported one. Passing "" through would silently mean "the Windows
                // default device", which is a different request altogether.
                deviceIdOverride = targetDeviceId;
            }

            Cleanup();
            isPlaying = false;

            // The commanded state is read HERE, after the teardown, rather than snapshotted
            // before it. The four command methods are lock-free by design, so one issued
            // while this method is tearing the chain down lands in these fields — and a
            // snapshot taken earlier would silently overwrite it with the pre-rebind value.
            // That is the same lost-command shape those methods record-before-null-check for,
            // one level up.
            StartLocked(lastWaveType, lastVolume, lastFrequency, deviceIdOverride, lastPan);
            return isPlaying;
        }
    }

    /// <summary>
    /// Updates the tone frequency based on pitch angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pitchDegrees">Aircraft pitch in degrees (negative = nose down, positive = nose up).</param>
    public void UpdatePitch(double pitchDegrees)
    {
        // Read the field into a local ONCE: this method takes no lock (by design, for
        // real-time smoothness), so RebindTo can null the `oscillator` field on another
        // thread between the null-check and the use below. Re-reading the field for the use
        // would race; using the captured local cannot — worst case it writes into an
        // oscillator that RebindTo has already orphaned, which is harmless, whereas
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

        // Deliberately recorded AFTER the null check, unlike the four commands below. This is
        // the one command no owner issues on an EDGE: hand-fly and visual guidance write pitch
        // every frame, so one dropped in the rebind window is superseded milliseconds later,
        // while every fixed-frequency tone (taxi steering, takeoff centerline, docking beeps)
        // never calls this at all and is replayed from the frequency StartLocked recorded.
        lastFrequency = targetFrequency;
    }

    /// <summary>
    /// Sets stereo panning directly.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="pan">Pan value from -1.0 (full left) to +1.0 (full right).</param>
    public void SetPan(float pan)
    {
        // Recorded BEFORE the null check — see UpdateVolume for why.
        float clamped = Math.Clamp(pan, -1.0f, 1.0f);
        lastPan = clamped;

        // See UpdatePitch for why the field is captured once into a local before use.
        PanningSampleProvider? panProvider = panningSampleProvider;
        if (panProvider == null || !isPlaying)
            return;

        panProvider.Pan = clamped;
    }

    /// <summary>
    /// Updates stereo panning based on bank angle.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="bankDegrees">Aircraft bank in degrees using standard convention (negative = left, positive = right).</param>
    public void UpdateBank(double bankDegrees)
    {
        // Map bank angle to stereo pan using standard right-positive convention:
        //   ±bankRangeDeg → ±1.0 (full left / full right). Default ±10°; Configure() may narrow
        //   it (visual landing guidance defaults to ±5° for tighter pan precision near matched
        //   state). Positive bank (right wing down) → positive pan (right speaker).
        // NOTE: SimConnect's PLANE_BANK_DEGREES is left-positive; callers must negate before
        // passing in (VisualGuidanceManager does this via its StandardBank helper; HandFlyManager
        // negates inline). The PID's bank command output is already right-positive.
        double clampedBank = Math.Clamp(bankDegrees, -bankRangeDeg, bankRangeDeg);
        float pan = (float)(clampedBank / bankRangeDeg);

        // Recorded BEFORE the null check — see UpdateVolume for why.
        lastPan = pan;

        // See UpdatePitch for why the field is captured once into a local before use.
        PanningSampleProvider? panProvider = panningSampleProvider;
        if (panProvider == null || !isPlaying)
            return;

        panProvider.Pan = pan;
    }

    /// <summary>
    /// Updates volume level.
    /// Lock-free for smooth real-time updates.
    /// </summary>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    public void UpdateVolume(double volume)
    {
        // Recorded BEFORE the null check: RebindTo nulls the oscillator for the duration of a
        // device change, and several owners write volume on an EDGE rather than every frame.
        // ProximityBeeper's solid "you are at the stop" tone is the sharp case — it writes the
        // volume once and then latches, so a write dropped in that window would leave the tone
        // silent for the rest of the dock. TaxiSteeringTone.SetSilent has the same shape
        // pointed the other way.
        lastVolume = volume;

        // See UpdatePitch for why the field is captured once into a local before use.
        PhaseContinuousOscillator? osc = oscillator;
        if (osc == null)
            return;

        osc.SetGain(volume);
    }

    /// <summary>
    /// Updates wave type.
    /// Lock-free for smooth real-time updates, EXCEPT on a change of sawtooth-ness, which has
    /// to rebuild the chain and therefore takes startStopLock — see below.
    /// </summary>
    /// <param name="waveType">New wave type.</param>
    public void UpdateWaveType(HandFlyWaveType waveType)
    {
        bool wantsFilter = waveType == HandFlyWaveType.Sawtooth;
        if (wantsFilter != filterInChain && isPlaying)
        {
            // The filter chain is decided in StartLocked and cannot be edited in place, so a
            // change of sawtooth-ness has to restart. Without this, StartLocked's replay on the
            // next device change would insert or drop the 1200 Hz filter and audibly re-timbre a
            // tone the pilot has been flying to, with no setting having changed.
            lastWaveType = waveType;
            RebindTo(CurrentDeviceId);
            return;
        }

        // Recorded BEFORE the null check — see UpdateVolume for why.
        lastWaveType = waveType;

        // See UpdatePitch for why the field is captured once into a local before use.
        PhaseContinuousOscillator? osc = oscillator;
        if (osc == null)
            return;

        osc.SetWaveType(waveType);
    }

    /// <summary>
    /// Gets whether tone is currently playing.
    /// </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// NAudio's ONLY channel for "the endpoint died underneath a playing stream" — the render
    /// thread catches the fault, stops filling buffers and reports it here. Nothing else in
    /// this class can notice: the tone simply stops making sound while every field still says
    /// it is playing.
    ///
    /// NON-BLOCKING BY CONTRACT, for two separate reasons:
    ///   * It can arrive on WasapiOut's own play thread — the thread that just failed — so any
    ///     WASAPI work done here would run on it.
    ///   * <see cref="Cleanup"/> disposes the session while holding startStopLock, and that
    ///     dispose calls Player.Stop(), which JOINS the render thread. A handler that took
    ///     startStopLock would therefore park the render thread on a lock held by the thread
    ///     waiting for that render thread to exit — a deadlock, on the ordinary Stop path.
    /// So this marks state and asks for a sweep; the router's worker does the re-resolve, in
    /// the right lock order, on a thread that holds nothing.
    ///
    /// It deliberately writes neither <c>isPlaying</c> nor <c>currentDeviceId</c>: both flip
    /// only under startStopLock (a lock-free write here could clobber a value a concurrent
    /// StartLocked had just set), and neither is needed for recovery —
    /// <see cref="RebindTo"/> acts on <see cref="NeedsDevice"/> alone, and the planner rebinds
    /// any generator carrying it regardless of what it is nominally bound to.
    /// </summary>
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // An ordinary Stop() raises this too, with a null Exception. Only a fault is news —
        // and a fault is the one a blind pilot cannot see for themselves.
        if (e?.Exception == null)
        {
            return;
        }

        needsDevice = true;
        Log.Warn("Services", $"Guidance tone output stopped unexpectedly: {e.Exception.Message}");

        // NotifyDeviceLost rather than RequestSweep: it is the router's own name for exactly
        // this call site, and it documents that the CALLER sets NeedsDevice first (the router
        // cannot — it is this class's field). Guarded because audio must never throw at an
        // owner, and this can run during a router's teardown.
        try { router?.NotifyDeviceLost(this); } catch { }
    }

    /// <summary>
    /// Adds this generator to the router's registry unless it is already there.
    /// Caller holds startStopLock.
    /// </summary>
    private void EnsureRegisteredLocked()
    {
        if (registered || router == null)
            return;

        router.Register(this);
        registered = true;
    }

    /// <summary>
    /// Removes this generator from the router's registry if it is in it.
    /// Caller holds startStopLock. Takes the router's Gate, which is the INNER lock — the
    /// allowed direction.
    /// </summary>
    private void UnregisterLocked()
    {
        if (!registered)
            return;

        // The flag drops either way. Stop()/Dispose() must never throw at an owner (audio is
        // optional feedback, by contract), and a router that will not take this generator out
        // of its registry is no reason to go on claiming it is in there.
        registered = false;
        try
        {
            // `registered` can only be true if a non-null router accepted a Register, so the
            // ?. can never actually skip a live registration -- it is here because the field
            // is nullable and that invariant lives two methods away.
            router?.Unregister(this);
        }
        catch
        {
            // Ignore registry errors on the way down.
        }
    }

    /// <summary>
    /// Cleans up audio resources. Deliberately does NOT unregister: registration outlives
    /// sounding (see the class doc), and Cleanup runs both for a deliberate Stop and as the
    /// failure path of a start — the second of which has to stay registered so a sweep can
    /// retry it.
    /// </summary>
    private void Cleanup()
    {
        try
        {
            AudioOutputSession? closing = session;
            if (closing != null)
            {
                // Detached BEFORE the dispose, and unconditionally. The dispose calls
                // Player.Stop(), which raises PlaybackStopped — with a null Exception, so the
                // handler would ignore it, but a still-attached handler on a session being
                // torn down is one more thing reachable from the render thread this very call
                // is joining. A player with no handler cannot surprise it.
                try { closing.Player.PlaybackStopped -= OnPlaybackStopped; } catch { }
                closing.Dispose();
            }

            session = null;
            oscillator = null;
            panningSampleProvider = null;
            currentDeviceId = string.Empty;

            // Nothing is bound any more. needsDevice is deliberately NOT touched here: every
            // exit from StartLocked sets it explicitly, and Stop() clears it.
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

        // Stop() has already done this. Repeated (and guarded) so a generator can never be
        // left in the registry after its owner disposed it, whatever Stop() grows into.
        lock (startStopLock)
        {
            UnregisterLocked();
        }

        GC.SuppressFinalize(this);
    }
}
