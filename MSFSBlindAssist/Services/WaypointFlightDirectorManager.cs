using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using G = MSFSBlindAssist.Navigation.WaypointFlightDirectorGeometry;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Synthetic, audio Waypoint Flight Director. Guides a blind pilot HAND-FLYING to the waypoints
/// they tracked in the 5 Shift+F slots, sequencing them 1→5. Lateral guidance is rendered as the
/// stereo pan of the "desired" tone (commanded bank); vertical guidance as that tone's frequency
/// (commanded pitch). A second "current" tone mirrors the actual attitude — the pilot flies to make
/// the two tones identical, the same dual-tone idiom as Visual Landing Guidance, generalised to the
/// en-route phase.
///
/// Entirely computed from stock SimVars carried on the existing VISUAL_GUIDANCE_DATA stream — works
/// on ANY aircraft, IFR or VFR, with no autopilot, no real flight director and no per-aircraft code.
/// Pure command math lives in <see cref="WaypointFlightDirectorGeometry"/>; this class is the
/// stateful shell (tones, sequencing, announcements, AP auto-mute). It NEVER touches the controls.
/// Mirrors <c>VisualGuidanceManager</c> deliberately (deferred tone Start, StandardBank convention).
/// </summary>
public class WaypointFlightDirectorManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;

    private bool isActive;
    public bool IsActive => isActive;

    /// <summary>Fired on Toggle. MainForm validates (slot 1 present?), Initializes, and arbitrates
    /// the shared stream + HandFly/VG audio in the handler.</summary>
    public event EventHandler<bool>? WaypointFlightDirectorActiveChanged;

    // Dual tones (desired = commanded attitude, current = actual). Started lazily on the first
    // ProcessUpdate with real data, exactly like VG, to avoid the brief fused-tone glitch.
    private AudioToneGenerator? desiredTone;
    private AudioToneGenerator? currentTone;
    private bool tonesNeedStart;
    private HandFlyWaveType desiredWaveType = HandFlyWaveType.Triangle;
    private HandFlyWaveType currentWaveType = HandFlyWaveType.Sine;
    private double desiredVolume = 0.05;
    private double currentVolume = 0.05;
    private bool hardPan;
    private bool apAutoMute = true;

    private WaypointFlightDirectorProfile profile = new();
    private WaypointTracker? tracker;
    private int activeSlot = 1;

    // Cached per-frame aircraft state (fed by MainForm from the VISUAL_GUIDANCE_* events).
    private double lat, lon, altMsl, hdgMag, groundTrack, groundSpeedKts, vsFpm, magvar;
    private double actualPitchDeg;   // standard convention (positive = nose up), as fed
    private double actualBankDegSc;  // raw SimConnect bank (left-positive), as fed
    private double aoaDeg;
    private bool apMaster;
    private bool hasLat, hasLon, hasAlt, hasTrack;

    // Lateral rate-lead: derive a turn rate from the ground-track derivative.
    private double lastTrackForRate;
    private DateTime lastRateTime = DateTime.MinValue;
    private double yawRateDegPerSec;

    private bool todAnnounced;       // synthetic top-of-descent/-climb cue, once per leg
    private bool apMutedAnnounced;   // one-shot AP-auto-mute callout edge
    private DateTime routineSuppressedUntil = DateTime.MinValue;

    public WaypointFlightDirectorManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>Toggle on/off. On (inactive→active) just flips state + fires the event; MainForm's
    /// handler validates the slots and calls <see cref="Initialize"/>. On (active→inactive) tears down.</summary>
    public void Toggle()
    {
        if (isActive)
        {
            Stop();
        }
        else
        {
            isActive = true;
            WaypointFlightDirectorActiveChanged?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Arm the FD on slot 1 with the aircraft's tuning profile + audio prefs. Caller (MainForm) has
    /// already confirmed slot 1 is non-empty. Defers tone Start to the first ProcessUpdate.
    /// </summary>
    public void Initialize(WaypointTracker waypointTracker, WaypointFlightDirectorProfile fdProfile,
        HandFlyWaveType desiredWave, double desiredVol,
        HandFlyWaveType currentWave, double currentVol,
        bool hardPanTone, bool apAutoMuteEnabled)
    {
        DisposeTones();   // defensive (idempotent re-init)

        tracker = waypointTracker;
        profile = fdProfile;
        desiredWaveType = desiredWave;
        desiredVolume = desiredVol;
        currentWaveType = currentWave;
        currentVolume = currentVol;
        hardPan = hardPanTone;
        apAutoMute = apAutoMuteEnabled;

        activeSlot = 1;
        todAnnounced = false;
        apMutedAnnounced = false;
        hasLat = hasLon = hasAlt = hasTrack = false;
        lastRateTime = DateTime.MinValue;
        yawRateDegPerSec = 0;

        desiredTone = new AudioToneGenerator();
        currentTone = new AudioToneGenerator();
        desiredTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz,
                              profile.TonePitchRangeDeg, profile.ToneBankRangeDeg);
        currentTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz,
                              profile.TonePitchRangeDeg, profile.ToneBankRangeDeg);
        tonesNeedStart = true;

        string ident = tracker.GetSlotIdent(activeSlot) ?? "waypoint";
        announcer.AnnounceImmediate($"Flight director active. Tracking {ident}.");
    }

    public void Stop(bool announce = true)
    {
        if (!isActive && desiredTone == null && currentTone == null)
            return;

        DisposeTones();
        isActive = false;
        if (announce)
            announcer.AnnounceImmediate("Flight director off.");
        WaypointFlightDirectorActiveChanged?.Invoke(this, false);
    }

    /// <summary>Suppress any future routine spoken callouts for ~3 s while the pilot reads a hotkey.</summary>
    public void NotifyManualQuery() => routineSuppressedUntil = DateTime.UtcNow.AddSeconds(3);

    // ---- Per-frame feeders (MainForm forwards the VISUAL_GUIDANCE_* events) -------------------
    public void UpdateLatitude(double v) { lat = v; hasLat = true; }
    public void UpdateLongitude(double v) { lon = v; hasLon = true; }
    public void UpdateAltitudeMSL(double v) { altMsl = v; hasAlt = true; }
    public void UpdateHeading(double v) => hdgMag = v;
    public void UpdateGroundTrack(double v) { groundTrack = v; hasTrack = true; }
    public void UpdateGroundSpeed(double v) => groundSpeedKts = v;
    public void UpdateVerticalSpeed(double v) => vsFpm = v;
    public void UpdateMagVar(double v) => magvar = v;
    public void UpdatePitch(double standardPitchDeg) => actualPitchDeg = standardPitchDeg;
    public void UpdateBank(double simConnectBankDeg) => actualBankDegSc = simConnectBankDeg;
    public void UpdateAoA(double v) => aoaDeg = v;
    public void UpdateApMaster(double v) => apMaster = v > 0.5;

    /// <summary>SimConnect PLANE BANK DEGREES is left-positive; the AudioToneGenerator + commanded
    /// bank are right-positive. Negate. (Same helper as VisualGuidanceManager.)</summary>
    private static double StandardBank(double simConnectBank) => -simConnectBank;

    /// <summary>Called once per frame when AGL arrives (all caches fresh). Computes the commands and
    /// drives the tones; runs the leg sequencer.</summary>
    public void ProcessUpdate()
    {
        if (!isActive || tracker == null) return;
        if (!hasLat || !hasLon || !hasAlt || !hasTrack) return;

        var slot = tracker.GetSlot(activeSlot);
        if (slot == null)   // route ran out from under us
        {
            announcer.AnnounceImmediate("Final waypoint reached.");
            Stop(announce: false);
            return;
        }

        double distNm = NavigationCalculator.CalculateDistance(lat, lon, slot.Value.Latitude, slot.Value.Longitude);
        double brgMag = NavigationCalculator.CalculateMagneticBearing(lat, lon, slot.Value.Latitude, slot.Value.Longitude, magvar);

        // Arrival → sequence to the next leg.
        if (G.HasArrived(distNm, brgMag, groundTrack, profile.CaptureRadiusNm))
        {
            AdvanceLeg();
            return;
        }

        UpdateYawRate();

        // Lateral: use wind-corrected ground track above the speed floor; fall back to heading when
        // ground track is unreliable (slow / near the ground).
        double effectiveTrack = groundSpeedKts >= profile.LowSpeedFloorKts ? groundTrack : hdgMag;
        double trackErr = G.TrackError(brgMag, effectiveTrack);
        double cmdBank = G.CommandedBankDeg(trackErr, yawRateDegPerSec,
                                            profile.KRollDegPerDegTrack, profile.BankRateLeadSec, profile.MaxBankDeg);

        // Vertical: nominal (hold-level: pitch ≈ AoA) unless an active crossing constraint commands
        // a climb/descent. Live AoA encodes weight/flap/speed so this needs no performance model.
        double cmdPitch = G.CommandedPitchDeg(0.0, aoaDeg, profile.MaxPitchDeg);
        if (slot.Value.Constraint != AltitudeConstraintType.None && slot.Value.CrossingAltitude.HasValue)
        {
            double projected = G.ProjectedCrossingAltFt(altMsl, vsFpm, distNm, groundSpeedKts);
            var (vActive, targetAlt) = G.ResolveVerticalTarget(
                slot.Value.Constraint, slot.Value.CrossingAltitude, slot.Value.CrossingAltitudeUpper, projected);
            if (vActive)
            {
                double reqFpa = G.RequiredFpaDeg(targetAlt, altMsl, distNm);
                cmdPitch = G.CommandedPitchDeg(reqFpa, aoaDeg, profile.MaxPitchDeg);

                if (!todAnnounced && G.IsTopOfChangeReached(altMsl, targetAlt, distNm, profile.NominalGradientDeg))
                {
                    announcer.AnnounceImmediate(targetAlt > altMsl ? "Begin climb." : "Begin descent.");
                    todAnnounced = true;
                }
            }
        }

        StartTonesIfNeeded();
        if (desiredTone == null || currentTone == null) return;

        // Desired tone: commanded attitude. Current tone: actual attitude. Pilot zero-beats them.
        ApplyBank(desiredTone, cmdBank);
        desiredTone.UpdatePitch(cmdPitch);
        ApplyBank(currentTone, StandardBank(actualBankDegSc));
        currentTone.UpdatePitch(actualPitchDeg);

        ApplyApAutoMute();
    }

    private void AdvanceLeg()
    {
        activeSlot++;
        todAnnounced = false;

        if (activeSlot > 5 || tracker == null || tracker.IsSlotEmpty(activeSlot))
        {
            announcer.AnnounceImmediate("Final waypoint reached. Flight director off.");
            Stop(announce: false);
            return;
        }

        var s = tracker.GetSlot(activeSlot);
        if (s == null) { Stop(announce: false); return; }

        double distNm = NavigationCalculator.CalculateDistance(lat, lon, s.Value.Latitude, s.Value.Longitude);
        double brgMag = NavigationCalculator.CalculateMagneticBearing(lat, lon, s.Value.Latitude, s.Value.Longitude, magvar);
        announcer.AnnounceImmediate($"Next, {s.Value.Ident}, {distNm:F0} miles, bearing {brgMag:F0}.");
    }

    private void UpdateYawRate()
    {
        DateTime now = DateTime.UtcNow;
        if (lastRateTime != DateTime.MinValue)
        {
            double dt = (now - lastRateTime).TotalSeconds;
            if (dt > 0.01 && dt < 2.0)
            {
                double raw = G.NormalizeSigned(groundTrack - lastTrackForRate) / dt;
                raw = Math.Clamp(raw, -15.0, 15.0);
                // light EMA so a single noisy track sample doesn't whip the rate-lead
                yawRateDegPerSec = 0.7 * yawRateDegPerSec + 0.3 * raw;
            }
        }
        lastTrackForRate = groundTrack;
        lastRateTime = now;
    }

    /// <summary>Apply commanded/actual bank to a tone, honouring the hard-pan setting (snap to
    /// ±full / centre with a 1° deadband) — mirrors VisualGuidanceManager.ApplyBank.</summary>
    private void ApplyBank(AudioToneGenerator tone, double bankDegreesStandard)
    {
        if (hardPan)
        {
            float pan = Math.Abs(bankDegreesStandard) < 1.0
                ? 0f
                : (bankDegreesStandard > 0 ? 1f : -1f);
            tone.SetPan(pan);
        }
        else
        {
            tone.UpdateBank(bankDegreesStandard);
        }
    }

    private void ApplyApAutoMute()
    {
        bool muted = apAutoMute && apMaster;
        desiredTone?.UpdateVolume(muted ? 0.0 : desiredVolume);
        currentTone?.UpdateVolume(muted ? 0.0 : currentVolume);

        // Edge-triggered spoken callout, skipped during the manual-readout grace window so it never
        // talks over a hotkey the pilot just pressed (the state flag still flips so it stays correct).
        bool inGrace = DateTime.UtcNow < routineSuppressedUntil;
        if (muted && !apMutedAnnounced)
        {
            if (!inGrace) announcer.Announce("Autopilot engaged. Flight director standing by.");
            apMutedAnnounced = true;
        }
        else if (!muted && apMutedAnnounced)
        {
            if (!inGrace) announcer.Announce("Autopilot off. Flight director active.");
            apMutedAnnounced = false;
        }
    }

    private void StartTonesIfNeeded()
    {
        if (!tonesNeedStart || desiredTone == null || currentTone == null) return;

        try
        {
            desiredTone.Start(desiredWaveType, desiredVolume);
        }
        catch { /* audio is optional feedback */ }

        if (desiredTone == null || !desiredTone.IsPlaying)
        {
            // No reference tone → don't start a meaningless constant follower.
            DisposeTones();
            return;
        }

        try
        {
            currentTone.Start(currentWaveType, currentVolume);
        }
        catch { /* follower failed; desired alone still conveys the command */ }

        tonesNeedStart = false;
    }

    private void DisposeTones()
    {
        try { desiredTone?.Stop(); desiredTone?.Dispose(); } catch { }
        try { currentTone?.Stop(); currentTone?.Dispose(); } catch { }
        desiredTone = null;
        currentTone = null;
        tonesNeedStart = false;
    }

    public void Dispose()
    {
        DisposeTones();
        GC.SuppressFinalize(this);
    }
}
