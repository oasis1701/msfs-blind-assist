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
    private bool hasLat, hasLon, hasAlt, hasTrack, hasAoa;

    // Plausibility band for the live INCIDENCE ALPHA reading. Real in-flight AoA spans roughly
    // -10° (a pushover) to +25° (past the stall of any transport aircraft), so anything outside
    // this — or non-finite — is a sensor/addon artifact rather than an attitude, and the profile's
    // TypicalApproachAoaDeg is used instead. NOTE the honest limit of this check: an addon that
    // simply never publishes INCIDENCE ALPHA reads a constant 0.0, which is INSIDE the band and
    // indistinguishable from genuine zero-AoA flight on any single sample. We deliberately do NOT
    // guess at that — substituting ~5° over a real 0° would command a persistently nose-high level
    // attitude, which is worse than the flat command it would replace.
    private const double MinPlausibleAoaDeg = -10.0;
    private const double MaxPlausibleAoaDeg = 25.0;

    // Lateral rate-lead: derive a turn rate from the ground-track derivative.
    private double lastTrackForRate;
    private DateTime lastRateTime = DateTime.MinValue;
    private double yawRateDegPerSec;
    private double iasKts;

    // Speed-restriction cue state. -1 too slow, 0 complying, +1 too fast; null until the first
    // verdict on this leg. Edge-triggered — it speaks on a CHANGE of verdict, never per frame.
    private int? speedVerdict;

    // ATC issues speed adjustments in 5-knot increments, so 5 kt is the natural "out of
    // compliance" band. Returning to compliance needs 3 kt — the gap is hysteresis, without which
    // sitting exactly on the boundary flips the verdict back and forth and talks continuously.
    private const double SpeedDeviationKts = 5.0;
    private const double SpeedComplyKts = 3.0;

    private bool apMutedAnnounced;   // one-shot AP-auto-mute callout edge

    // Optional "centered tone change": while laterally on track (commanded bank within the
    // deadband) the desired tone switches to a user-chosen waveform, so the timbre change is an
    // extra centered/not-centered cue on top of the pan. Off by default (tone stays its normal
    // waveform always). Only the desired tone changes, so it stays distinct from the current tone.
    private bool centeredToneEnabled;
    private HandFlyWaveType centeredWaveType = HandFlyWaveType.Square;
    private HandFlyWaveType appliedDesiredWave = HandFlyWaveType.Triangle;
    private const double CenteredDeadbandDeg = 1.5;

    // Command slew limiting (anti-fluctuation): cap how fast the rendered bank/pitch commands move
    // between frames so the tones don't jump on every track/heading wiggle.
    private double lastCmdBank, lastCmdPitch;
    private bool cmdInit;
    private DateTime lastCmdTime = DateTime.MinValue;

    // Per-leg start distance, captured on the leg's first frame. Distinguishes an INBOUND course leg
    // (started well outside the fix → enable the abeam station-passage fallback so a leg passed wide
    // still sequences) from an OUTBOUND radial (starts at/behind the fix, where abeam would misfire).
    private double legStartDistNm;
    private bool legStartCaptured;
    // Capture-arrival arming (anti-cascade): a leg only "arrives" by capture radius once it has been
    // approached from OUTSIDE that radius. If the leg STARTS inside the radius — the FD was engaged
    // overhead the fix, or it's an outbound radial from the fix — the initial dwell must NOT count,
    // otherwise the first frames cascade through every slot. Such a leg sequences instead once the
    // aircraft has flown clear of the radius while moving (station passage away from the fix).
    private bool legInsideAtStart;
    private bool legArmedCapture;

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
        bool hardPanTone, bool apAutoMuteEnabled,
        bool centeredToneOn, HandFlyWaveType centeredWave)
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
        centeredToneEnabled = centeredToneOn;
        centeredWaveType = centeredWave;
        appliedDesiredWave = desiredWave;

        // Start on the first FILLED slot (the user may have tracked into 2-3, not 1, from the EFB).
        // Caller (MainForm) has confirmed HasAnyWaypoint(), so NextFilledSlot(1) finds one; the ?? 1 is
        // a defensive fallback if that invariant is ever violated (first ProcessUpdate then stops clean).
        int firstFilled = tracker.NextFilledSlot(1);
        activeSlot = firstFilled == 0 ? 1 : firstFilled;
        apMutedAnnounced = false;
        hasLat = hasLon = hasAlt = hasTrack = hasAoa = false;
        lastRateTime = DateTime.MinValue;
        yawRateDegPerSec = 0;
        cmdInit = false;   // command slew baseline re-seeds on the first frame
        legStartCaptured = false;
        speedVerdict = null;   // re-state the first leg's restriction on every engage
        iasKts = 0.0;          // stale IAS must not produce a cue before the stream arrives

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
    public void UpdateAoA(double v) { aoaDeg = v; hasAoa = true; }

    /// <summary>
    /// The angle of attack the pitch command is built on: the LIVE INCIDENCE ALPHA reading when it
    /// has arrived and is plausible (the normal case — it encodes weight/flap/speed, which is what
    /// lets the FD work with no performance model), otherwise the per-aircraft
    /// <see cref="WaypointFlightDirectorProfile.TypicalApproachAoaDeg"/> fallback.
    /// </summary>
    private double EffectiveAoaDeg =>
        hasAoa && double.IsFinite(aoaDeg) && aoaDeg >= MinPlausibleAoaDeg && aoaDeg <= MaxPlausibleAoaDeg
            ? aoaDeg
            : profile.TypicalApproachAoaDeg;
    public void UpdateApMaster(double v) => apMaster = v > 0.5;

    /// <summary>Indicated airspeed (knots). IAS, not ground speed: ARINC 424 §5.72 codes a leg's
    /// speed limit in knots IAS and ATC issues adjustments in IAS, so a 240 kt restriction met at
    /// 240 kt GROUND speed is a bust in any wind.</summary>
    public void UpdateIas(double v) => iasKts = v;

    /// <summary>SimConnect PLANE BANK DEGREES is left-positive; the AudioToneGenerator + commanded
    /// bank are right-positive. Negate. (Same helper as VisualGuidanceManager.)</summary>
    private static double StandardBank(double simConnectBank) => -simConnectBank;

    /// <summary>Called once per frame when AGL arrives (all caches fresh). Computes the commands and
    /// drives the tones; runs the leg sequencer.</summary>
    public void ProcessUpdate()
    {
        if (!isActive || tracker == null) return;
        if (!hasLat || !hasLon || !hasAlt || !hasTrack) return;

        double cmdBank, cmdPitch;

        var slot = tracker.GetSlot(activeSlot);
        if (slot == null)   // route ran out from under us
        {
            announcer.AnnounceImmediate("Final waypoint reached.");
            Stop(announce: false);
            return;
        }

        // ARINC "to altitude" leg (CA/FA/VA — e.g. ANUT1D's "climb course 220° to 500 ft"):
        // a course and a target altitude, but NO fix. Everything below measures distance and
        // bearing to Latitude/Longitude, which for these is (0°N, 0°E), so they get their own law.
        if (!slot.Value.HasPosition)
        {
            ProcessToAltitudeLeg(slot.Value);
            return;
        }

        AnnounceSpeedRestriction(slot.Value);

        double slotLat = slot.Value.Latitude, slotLon = slot.Value.Longitude;
        bool isCourseLeg = slot.Value.Course.HasValue;
        double distNm = NavigationCalculator.CalculateDistance(lat, lon, slotLat, slotLon);
        double brgMag = NavigationCalculator.CalculateMagneticBearing(lat, lon, slotLat, slotLon, magvar);

        bool withinCapture = distNm <= profile.CaptureRadiusNm;
        bool moving = groundSpeedKts >= profile.LowSpeedFloorKts;

        // Record the leg's start state once (the first frame all caches are fresh): its distance (to tell
        // an inbound course leg from an outbound radial below) and whether it started INSIDE the capture
        // radius. Capture-arrival is armed only once the fix has been approached from outside the radius.
        if (!legStartCaptured)
        {
            legStartDistNm = distNm;
            legStartCaptured = true;
            legInsideAtStart = withinCapture;
            legArmedCapture = !withinCapture;
        }
        if (!withinCapture) legArmedCapture = true;   // left (or never entered) the zone → arm capture

        // Arrival → sequence to the next leg.
        //   captureArrival  : inside the radius, having approached from OUTSIDE it (a real fly-in). Counts
        //                     at any speed. NOT triggered by the initial dwell of a leg started overhead.
        //   clearedFromStart: a DIRECT-TO leg that STARTED on the fix (engaged parked or overhead); it
        //                     sequences once the aircraft has flown clear of the radius while MOVING —
        //                     never on the first frames (kills the parked/overhead cascade the un-armed
        //                     capture caused). ⚠️ Deliberately NOT applied to a course leg: an OUTBOUND
        //                     RADIAL starts on the fix and leaves the radius within seconds by
        //                     definition, so this rule would sequence away the very radial the pilot
        //                     asked to fly, on every use. A radial holds until the pilot advances or
        //                     turns the FD off (docs "Course / radial tracking").
        //   stationPassage  : the abeam test ALONE (only when MOVING). ⚠️ Must NOT be G.HasArrived: that
        //                     ORs in an unconditional `dist <= captureRadius`, which bypasses the
        //                     legArmedCapture gate entirely — engaging airborne inside the radius of a
        //                     direct-to fix then sequenced past it on frame 1, making the whole armed-
        //                     capture state machine dead code for direct-to legs. A COURSE leg uses abeam
        //                     only when it started well OUTSIDE the fix (an inbound CF leg passed wide) —
        //                     an outbound radial starts behind the fix, where abeam would misfire.
        bool captureArrival = legArmedCapture && withinCapture;
        bool clearedFromStart = legInsideAtStart && moving && !withinCapture && !isCourseLeg;
        bool stationPassage = moving && G.IsPastAbeam(brgMag, groundTrack);
        bool startedFar = legStartDistNm > profile.CaptureRadiusNm * 4.0;   // inbound, not an outbound radial
        bool arrived = captureArrival || clearedFromStart
            || (isCourseLeg ? (startedFar && stationPassage) : stationPassage);
        if (arrived)
        {
            AdvanceLeg();
            return;
        }

        // Lateral: use wind-corrected ground track above the speed floor; fall back to heading when
        // ground track is unreliable (slow / near the ground).
        double effectiveTrack = groundSpeedKts >= profile.LowSpeedFloorKts ? groundTrack : hdgMag;

        // Derive the turn rate from the SAME signal the error uses, so below the speed floor the
        // rate-lead doesn't ride the noisy ground-track derivative while the error is on heading.
        UpdateYawRate(effectiveTrack);
        double trackErr;
        if (isCourseLeg)
        {
            // Course / radial tracking: capture and hold the course line THROUGH the fix (airway
            // leg, approach course, radial) instead of direct-to. Generalised ILS localizer capture.
            double courseMag = slot.Value.Course!.Value;
            // Work the whole intercept in the TRUE frame, converting each angle with the variation it's
            // actually referenced to (what real RNAV/FMS does):
            //   - the COURSE is defined at the fix, so lift it to true with the fix's REFERENCE variation
            //     (navaid station declination / fix local variation from navdata). This matters: a VOR
            //     radial is defined by the station's declination, which — because VORs are re-aligned
            //     rarely — can differ from today's variation by several degrees, and far from the fix the
            //     aircraft's own magvar is a different value again. Fall back to the aircraft magvar only
            //     when navdata gave us none (ReferenceMagVar == null).
            //   - the aircraft GROUND TRACK is measured at the aircraft, so lift it to true with the
            //     aircraft's own live magvar.
            // (east +: true = mag + var.)
            double refVar = slot.Value.ReferenceMagVar ?? magvar;
            double courseTrue = courseMag + refVar;
            double brgFixToAcTrue = NavigationCalculator.CalculateBearing(slotLat, slotLon, lat, lon);
            double xtNm = G.CrossTrackNm(distNm, brgFixToAcTrue, courseTrue);
            double desiredTrackTrue = G.CourseInterceptTrackDeg(courseTrue, xtNm,
                                                                profile.MaxInterceptDeg, profile.InterceptDegPerNm);
            double effectiveTrackTrue = effectiveTrack + magvar;   // aircraft magnetic track → true
            trackErr = G.NormalizeSigned(desiredTrackTrue - effectiveTrackTrue);
        }
        else
        {
            // Direct-to: steer straight at the fix (wind-corrected via ground track).
            trackErr = G.TrackError(brgMag, effectiveTrack);
        }
        cmdBank = G.CommandedBankDeg(trackErr, yawRateDegPerSec,
                                     profile.KRollDegPerDegTrack, profile.BankRateLeadSec, profile.MaxBankDeg);

        // Vertical: nominal (hold-level: pitch ≈ AoA) unless an active crossing constraint commands
        // a climb/descent. Live AoA encodes weight/flap/speed so this needs no performance model.
        cmdPitch = G.CommandedPitchDeg(0.0, EffectiveAoaDeg, profile.MaxPitchDeg);
        if (slot.Value.Constraint != AltitudeConstraintType.None && slot.Value.CrossingAltitude.HasValue)
        {
            double projected = G.ProjectedCrossingAltFt(altMsl, vsFpm, distNm, groundSpeedKts);
            var (vActive, targetAlt) = G.ResolveVerticalTarget(
                slot.Value.Constraint, slot.Value.CrossingAltitude, slot.Value.CrossingAltitudeUpper, projected);
            if (vActive)
            {
                // Vertical guidance toward the crossing altitude (no spoken top-of-descent cue —
                // the tone IS the instrument; the pilot judges when to start down).
                double reqFpa = G.RequiredFpaDeg(targetAlt, altMsl, distNm);

                // Descent-arm gate: a CLIMB is commanded immediately (e.g. climb to meet a SID
                // at-or-above), but a DESCENT only once it's geometrically due — the required angle has
                // reached a normal gradient (DescentArmFpaDeg), or the fix is within VerticalArmRangeNm
                // for a shallow step that never gets that steep. Otherwise hold level so a far
                // constrained fix doesn't nudge a premature descent at cruise. Tone-only, not a TOD cue.
                bool descentDue = reqFpa >= 0.0
                                  || -reqFpa >= profile.DescentArmFpaDeg
                                  || distNm <= profile.VerticalArmRangeNm;
                if (descentDue)
                    cmdPitch = G.CommandedPitchDeg(reqFpa, EffectiveAoaDeg, profile.MaxPitchDeg);
            }
        }

        // Slew-limit both commands so the tones don't fluctuate frame-to-frame (the bank/pitch
        // commands otherwise jump on every track/heading wiggle). Caps come from the profile.
        SlewCommands(ref cmdBank, ref cmdPitch);

        StartTonesIfNeeded();
        if (desiredTone == null || currentTone == null) return;

        // Desired tone: commanded attitude. Current tone: actual attitude. Pilot zero-beats them.
        ApplyBank(desiredTone, cmdBank);
        desiredTone.UpdatePitch(cmdPitch);
        ApplyBank(currentTone, StandardBank(actualBankDegSc));
        currentTone.UpdatePitch(actualPitchDeg);

        ApplyCenteredWaveform(cmdBank);
        ApplyApAutoMute();
    }

    /// <summary>Altitude band inside which a to-altitude leg counts as satisfied, and inside
    /// which its pitch command levels off. 50 ft matches the crossing-constraint tolerance.</summary>
    private const double ToAltitudeToleranceFt = 50.0;

    /// <summary>
    /// Lateral + vertical for an ARINC "to altitude" leg (CA/FA/VA). It carries a course and a
    /// target altitude but no fix, so:
    /// <list type="bullet">
    /// <item>LATERAL degrades to a pure course HOLD — the cross-track term needs a fix to measure
    /// against, and there isn't one. Still wind-corrected, because the error is taken against
    /// ground track exactly as the course-leg branch does.</item>
    /// <item>VERTICAL is flown at the profile's pitch limit until the altitude is met, then levels.
    /// There is no distance, so the required-FPA geometry has nothing to work with — and a SID's
    /// initial climb is flown at the aircraft's climb capability anyway.</item>
    /// <item>ARRIVAL is by ALTITUDE, not by distance or abeam.</item>
    /// </list>
    /// </summary>
    private void ProcessToAltitudeLeg(WaypointSlotData s)
    {
        AnnounceSpeedRestriction(s);
        if (ToAltitudeSatisfied(s)) { AdvanceLeg(); return; }

        double effectiveTrack = groundSpeedKts >= profile.LowSpeedFloorKts ? groundTrack : hdgMag;
        UpdateYawRate(effectiveTrack);

        // Course hold. Both angles lifted into the TRUE frame with the variation each is
        // referenced to, the same convention as the course-leg branch (east +: true = mag + var).
        double refVar = s.ReferenceMagVar ?? magvar;
        double courseTrue = (s.Course ?? effectiveTrack) + refVar;
        double trackErr = G.NormalizeSigned(courseTrue - (effectiveTrack + magvar));
        double cmdBank = G.CommandedBankDeg(trackErr, yawRateDegPerSec,
                                            profile.KRollDegPerDegTrack, profile.BankRateLeadSec,
                                            profile.MaxBankDeg);

        // Level unless the altitude is still to be made; ±90 saturates the clamp, so the command
        // is simply "climb (or descend) at the profile limit".
        double cmdPitch = G.CommandedPitchDeg(0.0, EffectiveAoaDeg, profile.MaxPitchDeg);
        if (s.CrossingAltitude.HasValue)
        {
            double err = s.CrossingAltitude.Value - altMsl;
            if (Math.Abs(err) > ToAltitudeToleranceFt)
                cmdPitch = G.CommandedPitchDeg(Math.Sign(err) * 90.0, EffectiveAoaDeg, profile.MaxPitchDeg);
        }

        SlewCommands(ref cmdBank, ref cmdPitch);
        StartTonesIfNeeded();
        if (desiredTone == null || currentTone == null) return;
        ApplyBank(desiredTone, cmdBank);
        desiredTone.UpdatePitch(cmdPitch);
        ApplyBank(currentTone, StandardBank(actualBankDegSc));
        currentTone.UpdatePitch(actualPitchDeg);
        ApplyCenteredWaveform(cmdBank);
        ApplyApAutoMute();
    }

    /// <summary>Whether a to-altitude leg's terminating condition is met. The ARINC descriptor
    /// decides which side counts: a "+" (at or above) leg ends on reaching the altitude, a "-"
    /// (at or below) leg on being under it.</summary>
    private bool ToAltitudeSatisfied(WaypointSlotData s)
    {
        if (!s.CrossingAltitude.HasValue) return false;   // nothing to terminate on
        double target = s.CrossingAltitude.Value;
        return s.Constraint == AltitudeConstraintType.AtOrBelow
            ? altMsl <= target + ToAltitudeToleranceFt
            : altMsl >= target - ToAltitudeToleranceFt;
    }

    /// <summary>
    /// Speaks a leg's ARINC speed restriction as an ACTION — "increase speed to 240" / "reduce
    /// speed to 240" — and confirms once compliance is reached. Edge-triggered on the verdict, so
    /// it says each thing once rather than every frame.
    /// <para>
    /// Compared against INDICATED airspeed. ARINC 424 §5.72 codes the limit in knots IAS and ATC
    /// phrases adjustments in IAS; ground speed would read compliant into a headwind and busted
    /// with a tailwind at the identical throttle setting.
    /// </para>
    /// </summary>
    private void AnnounceSpeedRestriction(WaypointSlotData s)
    {
        if (!s.SpeedLimitKts.HasValue) { speedVerdict = null; return; }
        if (iasKts <= 0.0) return;                      // no airspeed yet this session
        if (groundSpeedKts < profile.LowSpeedFloorKts) return;   // parked / taxiing: not a cue

        double limit = s.SpeedLimitKts.Value;
        double delta = iasKts - limit;
        int verdict = speedVerdict ?? 0;
        if (Math.Abs(delta) > SpeedDeviationKts) verdict = Math.Sign(delta);
        else if (Math.Abs(delta) < SpeedComplyKts) verdict = 0;

        if (speedVerdict == verdict) return;
        bool first = speedVerdict == null;
        speedVerdict = verdict;

        // Nothing to say if the leg was already being flown at its restriction when it became
        // active — the pilot is complying and has not been told to do anything.
        if (first && verdict == 0) return;

        announcer.Announce(verdict switch
        {
            < 0 => $"Increase speed to {limit:F0}",
            > 0 => $"Reduce speed to {limit:F0}",
            _   => $"Speed {limit:F0}"
        });
    }

    private void AdvanceLeg()
    {
        legStartCaptured = false;   // re-measure the start distance for the new leg
        speedVerdict = null;        // each leg states its own restriction afresh

        // Advance to the next FILLED slot, skipping empty INTERIOR slots so a gap (e.g. the user
        // tracked slots 1, 2, 4 from the EFB, or slot 3 was a position-less leg that couldn't be
        // tracked) doesn't silently end the route — fly whatever slots are filled, in order, up to 5.
        //
        // Then keep going, IN THIS FRAME, across any further slots the aircraft has already flown
        // past. Sequencing one slot per frame instead spoke one AnnounceImmediate per slot, and
        // AnnounceImmediate interrupts — engaging with several tracked fixes already behind you
        // produced a burst of half-spoken waypoint names ending in "Final waypoint reached."
        // The skipped fixes are summarised in the single callout below instead.
        int skipped = 0;
        int next = tracker?.NextFilledSlot(activeSlot + 1) ?? 0;
        while (next != 0)
        {
            var candidate = tracker!.GetSlot(next);
            if (candidate == null || !IsAlreadyBehind(candidate.Value)) break;
            skipped++;
            next = tracker.NextFilledSlot(next + 1);
        }

        if (next == 0)
        {
            announcer.AnnounceImmediate("Final waypoint reached. Flight director off.");
            Stop(announce: false);
            return;
        }
        activeSlot = next;

        var s = tracker!.GetSlot(activeSlot);
        if (s == null) { Stop(announce: false); return; }

        string skipNote = skipped switch
        {
            0 => "",
            1 => " Skipped 1 waypoint already behind you.",
            _ => $" Skipped {skipped} waypoints already behind you."
        };
        // A to-altitude leg has no fix to measure to — name the course and altitude instead.
        if (!s.Value.HasPosition)
        {
            string alt = s.Value.CrossingAltitude.HasValue ? $", {s.Value.CrossingAltitude.Value:F0} feet" : "";
            string crs = s.Value.Course.HasValue ? $", course {s.Value.Course.Value:F0}" : "";
            announcer.AnnounceImmediate($"Next, {s.Value.Ident}{crs}{alt}.{skipNote}");
            return;
        }

        double distNm = NavigationCalculator.CalculateDistance(lat, lon, s.Value.Latitude, s.Value.Longitude);
        double brgMag = NavigationCalculator.CalculateMagneticBearing(lat, lon, s.Value.Latitude, s.Value.Longitude, magvar);
        announcer.AnnounceImmediate($"Next, {s.Value.Ident}, {distNm:F0} miles, bearing {brgMag:F0}.{skipNote}");
    }

    /// <summary>
    /// True if the aircraft has ALREADY flown past this fix, so sequencing onto it would only
    /// advance again on the next frame. Used solely to collapse a multi-slot skip into one callout
    /// (see <see cref="AdvanceLeg"/>); the per-leg arrival logic in <see cref="ProcessUpdate"/>
    /// remains the authority for a leg actually being flown.
    ///
    /// Deliberately conservative — it only reports the unambiguous case, station passage while
    /// MOVING:
    ///   - A fix INSIDE the capture radius is NOT "behind": that is the engaged-overhead case the
    ///     armed-capture logic (legInsideAtStart / legArmedCapture) exists to handle, and skipping
    ///     it here would resurrect exactly the cascade that logic prevents.
    ///   - A COURSE leg is never skipped. An outbound radial legitimately starts behind the
    ///     aircraft, so "behind" carries no information there — and a course the pilot deliberately
    ///     set is intent worth honouring rather than silently dropping.
    /// </summary>
    private bool IsAlreadyBehind(WaypointSlotData slot)
    {
        if (slot.Course.HasValue) return false;
        if (groundSpeedKts < profile.LowSpeedFloorKts) return false;

        double d = NavigationCalculator.CalculateDistance(lat, lon, slot.Latitude, slot.Longitude);
        if (d <= profile.CaptureRadiusNm) return false;

        double b = NavigationCalculator.CalculateMagneticBearing(lat, lon, slot.Latitude, slot.Longitude, magvar);
        return Math.Abs(G.NormalizeSigned(b - groundTrack)) > 90.0;
    }

    private void UpdateYawRate(double track)
    {
        DateTime now = DateTime.UtcNow;
        if (lastRateTime != DateTime.MinValue)
        {
            double dt = (now - lastRateTime).TotalSeconds;
            if (dt > 0.01 && dt < 2.0)
            {
                double raw = G.NormalizeSigned(track - lastTrackForRate) / dt;
                raw = Math.Clamp(raw, -15.0, 15.0);
                // light EMA so a single noisy track sample doesn't whip the rate-lead
                yawRateDegPerSec = 0.7 * yawRateDegPerSec + 0.3 * raw;
            }
        }
        lastTrackForRate = track;
        lastRateTime = now;
    }

    /// <summary>Rate-limit the rendered bank/pitch commands (deg/sec caps from the profile) so the
    /// tones don't fluctuate frame-to-frame on track/heading jitter. Re-seeds on the first frame of
    /// a session (cmdInit).</summary>
    private void SlewCommands(ref double cmdBank, ref double cmdPitch)
    {
        DateTime now = DateTime.UtcNow;
        if (!cmdInit)
        {
            lastCmdBank = cmdBank;
            lastCmdPitch = cmdPitch;
            lastCmdTime = now;
            cmdInit = true;
            return;
        }
        double dt = (now - lastCmdTime).TotalSeconds;
        lastCmdTime = now;
        if (dt <= 0 || dt > 1.0) dt = 1.0 / 30.0;   // fallback ~one frame on a gap
        double maxBankStep = profile.MaxBankRateDegPerSec * dt;
        double maxPitchStep = profile.MaxPitchRateDegPerSec * dt;
        cmdBank = lastCmdBank + Math.Clamp(cmdBank - lastCmdBank, -maxBankStep, maxBankStep);
        cmdPitch = lastCmdPitch + Math.Clamp(cmdPitch - lastCmdPitch, -maxPitchStep, maxPitchStep);
        lastCmdBank = cmdBank;
        lastCmdPitch = cmdPitch;
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

    /// <summary>When the "centered tone change" option is on, swap the DESIRED tone's waveform to
    /// the user-chosen one while laterally on track (|commanded bank| within the deadband), and back
    /// to its normal waveform when off track — an extra timbre cue for centered vs not. No-op when
    /// the option is off (default). Only the desired tone changes, so it stays distinct from the
    /// current tone (no phase-cancel at the matched state).</summary>
    private void ApplyCenteredWaveform(double cmdBankDeg)
    {
        if (!centeredToneEnabled || desiredTone == null) return;
        HandFlyWaveType want = Math.Abs(cmdBankDeg) <= CenteredDeadbandDeg ? centeredWaveType : desiredWaveType;
        if (want != appliedDesiredWave)
        {
            desiredTone.UpdateWaveType(want);
            appliedDesiredWave = want;
        }
    }

    private void ApplyApAutoMute()
    {
        bool muted = apAutoMute && apMaster;
        desiredTone?.UpdateVolume(muted ? 0.0 : desiredVolume);
        currentTone?.UpdateVolume(muted ? 0.0 : currentVolume);

        // While muted, keep re-baselining the yaw-rate estimate AND zero its value so it can't carry
        // a stale turn-rate lead into the first bank command after the autopilot disengages.
        if (muted) { lastRateTime = DateTime.MinValue; yawRateDegPerSec = 0.0; }

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
            // The reference tone failed to start (audio device busy/unavailable). The FD is an audio
            // feature, so it cannot function — fail LOUDLY and shut down rather than the old
            // DisposeTones()-and-return, which left the FD "active" with no tones AND no retry
            // (tonesNeedStart cleared, the null-tone guard short-circuits every later frame), i.e.
            // silently dead for the whole session. Stop() disposes the tones + releases the stream.
            announcer.AnnounceImmediate("Flight director audio unavailable.");
            Stop(announce: false);
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
