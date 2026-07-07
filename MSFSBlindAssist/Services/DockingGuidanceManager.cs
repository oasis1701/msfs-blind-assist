using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Audio docking guidance to a gate stop position: lateral steering tone + an
/// accelerating proximity beep + spoken metric milestones. Standalone — does not
/// modify TaxiGuidanceManager. Fed from the same ~30 Hz position handler.
/// </summary>
public sealed class DockingGuidanceManager : IDisposable
{
    private enum DockState { Idle, Armed, Docking, Stopped }

    // Lateral steering-tone precision for docking. Docking demands a far tighter
    // centerline hold than normal taxi. The width-scaled UpdateHeadingError overload
    // bottoms out at silent≈1.95° / activation≈3.9° / max-pan≈19.5° (MIN_SCALE clamp
    // at any width ≤ 25 ft), which lets the aircraft sit ~3° off the gate axis with
    // NO audio cue — the same too-loose failure documented for runway lineup. Drive
    // the tone with the runway-lineup precision profile instead: keep panning until
    // the heading-to-stop is centred within ½°, re-activate past 1°, full pan by 15°.
    private const double DockSilentThresholdDeg = 0.5;
    private const double DockActivationThresholdDeg = 1.0;
    private const double DockMaxPanThresholdDeg = 15.0;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiSteeringTone _tone = new();
    private readonly ProximityBeeper _beeper = new();
    private readonly object _lock = new();

    private bool _disposed;
    private ParkingSpot? _gate;
    private DockState _state = DockState.Idle;
    // Lock-free mirror of "state is Docking or Stopped", updated under _lock at every state
    // transition. The 30 Hz MainForm position handler reads IsActive right after UpdatePosition;
    // a volatile read here avoids a second lock acquisition per frame (the SettingsManager
    // static lock is contended by Save(), which holds it across a JSON serialize + disk write).
    private volatile bool _isActiveSnap;
    // Lock-free mirror of "Armed with a resolved stop target still meaningfully
    // AHEAD of the aircraft" — the pre-engage window in which taxi guidance must
    // say "continue ahead for docking" instead of "parking brake" / "hold
    // position". KATL F3 2026-06-11: the GSX stop (incl. the 777's 11.3 m .py
    // offset) sat ~34 m past the navdata parking point while the gate's GSX
    // gatedistancethreshold shrank the engage range to ~24 m — the pilot was
    // told "Parking brake." at the navdata point and sat for 26 s with docking
    // Armed-but-not-engaged until they crept forward on their own. Updated every
    // frame in the Idle/Armed branch; cleared on engage, reset, dispose.
    private volatile bool _armedAwaitingSnap;
    private const double PENDING_MIN_AHEAD_M = 3.0;   // stop ≈ navdata point → normal wording
    private IReadOnlyList<DistanceMilestone> _milestones = Array.Empty<DistanceMilestone>();
    private bool[] _milestoneSaid = Array.Empty<bool>();
    private bool _slowDownSaid;
    private bool _overshootStop;      // the Stopped state was entered via overshoot — no solid "docked" tone
    private DateTime _stoppedSinceUtc = DateTime.MinValue; // first frame of a gs<0.5 kt standstill while Docking
    private bool _stoppedShortSaid;   // one-shot for the stopped-short reminder; re-arms on movement
    private string _doorSide = ""; // "left" / "right" / "" — preferred passenger door side, for jetway orientation
    private double _lastAlongM;  // last forward (along-track) distance to the stop (m), for the status query
    private GsxOffset _stopOffset = GsxOffset.Zero; // GSX .py per-aircraft stop offset (metres); Zero = base navdata stop
    // Cue 2: GSX gatedistancethreshold override for engage range (null = use DockingGeometry.EngageRangeMetres).
    // Clamped to [20, 70] m when non-null. Set from the .ini gate's gatedistancethreshold field.
    private double? _engageRangeOverrideMetres;

    /// <summary>How long the aircraft must sit still (gs &lt; 0.5 kt) mid-approach before the stopped-short reminder fires.</summary>
    private const double StoppedShortSeconds = 4.0;
    /// <summary>Stopped-short reminder only fires inside this window (m) — past the stop band, short of the gate.</summary>
    private const double StoppedShortMaxMetres = 10.0;
    /// <summary>Backing up this far past the stop re-arms the state machine (Stopped → Idle) so a retry dock re-engages.</summary>
    private const double RearmBackupMetres = 3.0;
    /// <summary>Far-field telemetry/lineup math is skipped beyond this raw distance (m) unless engaged.</summary>
    private const double DetailRangeMetres = 150.0;

    // Throttled telemetry so a live docking run can be diagnosed post-hoc.
    private static readonly LogChannel _dockLog = Log.Channel("docking");
    private DateTime _lastDockLogUtc = DateTime.MinValue;
    // One-shot diagnostic for the occupancy clamp (written to docking-aircraft.log, the same
    // file as the STOPOFFSET line — also written by TaxiAssistForm/MainForm.AircraftSwitch/
    // SimConnectManager.Dispatch, all now serialized through this same LogChannel). Reset per
    // gate in ResetLocked so a re-dock re-logs once; NOT written per frame (the clamp math runs
    // every frame but the file write is latched).
    private static readonly LogChannel _aircraftLog = Log.Channel("docking-aircraft");
    private bool _occupancyClampLogged;

    public DockingGuidanceManager(ScreenReaderAnnouncer announcer)
        => _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

    /// <summary>
    /// True while docking is actively guiding (Docking or Stopped). Drives BOTH taxi-side
    /// couplings in MainForm: the steering-tone mute AND the terminal arrival-callout
    /// suppression (<c>SetDockingActive</c>). ENGAGE-LATCHED by design: taxi speaks its normal
    /// arrival callouts (parking countdown, "Stop. Hold position.", "Destination reached") right
    /// up until docking actually engages — so a navdata gate where docking never engages (wrong
    /// approach cone, stop &gt; engage range, heading data off) still gets the full taxi arrival
    /// sequence instead of total verbal silence. Once engaged, docking owns the rest of the
    /// arrival (through Stopped) and taxi stays quiet so the two never contradict. Lock-free
    /// volatile read — safe from the per-frame MainForm handler.
    /// </summary>
    public bool IsActive => _isActiveSnap;

    /// <summary>
    /// True while docking is ARMED for the current gate with a stop target still
    /// meaningfully ahead of the aircraft (along-track &gt; 3 m) but not yet engaged —
    /// the window where taxi guidance reaches its navdata endpoint first and must
    /// redirect the pilot FORWARD ("continue ahead for docking guidance") instead of
    /// announcing "parking brake" / "hold position" at the wrong spot. Lock-free
    /// volatile read — safe from the per-frame MainForm handler.
    /// </summary>
    public bool IsArmedAwaitingEngage => _armedAwaitingSnap;

    /// <summary>
    /// Raised ONCE when the final approach reaches the stop (the "GSX docking complete." / "Stop."
    /// moment, including an overshoot stop). Lets the host stop taxi guidance so the whole flow
    /// ends cleanly instead of taxi sitting in LiningUp forever after the aircraft is parked.
    /// Raised OUTSIDE the internal lock, on the SimConnect position thread.
    /// </summary>
    public event Action? DockingCompleted;

    /// <summary>
    /// One-line status for the manual status hotkey (Output mode, Y), used INSTEAD of the
    /// taxi status while docking owns the final approach — so the two never report conflicting
    /// distances ("25 m to gate" from taxi vs "20 m to stop" from docking). Empty when not active.
    /// </summary>
    public string GetStatusAnnouncement()
    {
        lock (_lock)
        {
            string what = _gate?.IsDeiceArea == true ? "deicing pad" : "stop";
            if (_state == DockState.Stopped) return "At the stop. Hold position.";
            if (_state == DockState.Docking)
                return $"Docking. {DistanceFormatter.FromMetres(Math.Max(0.0, _lastAlongM))} to {what}.";
            return string.Empty;
        }
    }

    /// <summary>Set (or clear) the destination gate the pilot is taxiing to. Resets state + audio.</summary>
    public void SetDestinationGate(ParkingSpot? gate)
    {
        // Clear any prior gate's stop offset and engage-range override — the caller recomputes
        // + SetStopOffset / SetEngageRangeMetres for the new gate; until then defaults apply.
        lock (_lock) { _gate = gate; _stopOffset = GsxOffset.Zero; _engageRangeOverrideMetres = null; ResetLocked(); }
    }

    /// <summary>"left" / "right" / "" — the preferred passenger door side, for jetway orientation.</summary>
    public void SetDoorSide(string side) { lock (_lock) { _doorSide = side ?? ""; } }

    /// <summary>
    /// GSX per-aircraft stop offset (metres) from the destination gate's <c>.py</c> profile —
    /// <c>LongitudinalMetres</c> forward along the gate stop heading, <c>LateralMetres</c>
    /// perpendicular (right = +). Shifts the stop TARGET so the park ends where GSX's VDGS
    /// would stop this airframe, instead of the bare navdata base. <see cref="GsxOffset.Zero"/>
    /// (the default, and the value for deice areas) reproduces today's behaviour exactly.
    /// </summary>
    public void SetStopOffset(GsxOffset offset) { lock (_lock) { _stopOffset = offset; } }

    /// <summary>
    /// Override the engage range (metres) from the gate's GSX <c>gatedistancethreshold</c> value.
    /// Clamped to [20, 70] m so an unusually small or large profile value doesn't produce a
    /// non-functional engage window. Pass <c>null</c> (or call with no argument) to revert to
    /// the fixed <see cref="DockingGeometry.EngageRangeMetres"/> (50 m) constant.
    /// <para>Called from <c>TaxiAssistForm.ApplyGsxStopOffset</c> alongside the other gate
    /// setters whenever a destination gate is selected.</para>
    /// </summary>
    public void SetEngageRangeMetres(double? metres)
    {
        lock (_lock)
        {
            if (metres.HasValue)
                _engageRangeOverrideMetres = Math.Clamp(metres.Value, 20.0, 70.0);
            else
                _engageRangeOverrideMetres = null;
        }
    }

    public void UpdatePosition(double lat, double lon, double headingMag, double magVar, double groundSpeedKts)
    {
        bool fireCompleted = false;
        lock (_lock)
        {
            if (_disposed) return;
            try
            {
                if (_gate == null || !SettingsManager.Current.DockingGuidanceEnabled)
                {
                    // Disabling docking (or losing the gate) MID-APPROACH must fully reset, not
                    // just silence: leaving _state latched at Docking/Stopped kept IsActive true
                    // forever, so MainForm went on muting taxi's steering tone every frame and the
                    // pilot had NO lateral cue for the final gate turn until something called
                    // SetDestinationGate. ResetLocked returns to Idle and clears the snapshot.
                    if (_state != DockState.Idle) ResetLocked();
                    else SilenceLocked();
                    return;
                }

                // NOTE: for navdata-only gates that have no GSX StopLatitude, the target
                // falls back to the parking-spot centre (Latitude/Longitude). In that case
                // the door aligns to the parking centre rather than a real jetway stop
                // position — a data limitation, not a bug.
                double sLat = _gate.StopLatitude ?? _gate.Latitude;
                double sLon = _gate.StopLongitude ?? _gate.Longitude;
                double centerHdg = _gate.StopHeading ?? _gate.Heading;

                // Apply the GSX .py per-aircraft stop offset BEFORE any distances are computed,
                // so every cue (distM/alongM/lineup/milestones) references the shifted stop. The
                // offset moves the stop point LongitudinalMetres along the gate stop heading and
                // LateralMetres perpendicular (right = +). Deice areas keep Zero (datum-aligned).
                // With GsxOffset.Zero the shift is a no-op and behaviour is identical to before.
                if ((_stopOffset.LongitudinalMetres != 0.0 || _stopOffset.LateralMetres != 0.0)
                    && _gate.IsDeiceArea != true)
                    ShiftStop(ref sLat, ref sLon, centerHdg, _stopOffset);

                // Occupancy-safe clamp: keep the aircraft DATUM inside GSX's static occupancy
                // circle (radius ≈ gatedistancethreshold, centred on this_parking_pos =
                // _gate.Latitude/Longitude) so GSX registers the aircraft as PARKED and offers
                // arrival services + the jetway, rather than "reposition". The .py offset above
                // can push the datum past that circle at gates whose threshold is tight relative
                // to the stop distance (KBOS E13: datum 31.5 m > 25 m circle → reposition).
                // Strict NO-OP for deice pads, navdata-only gates (no StopLatitude / threshold),
                // VDGS-reliant gates whose stop sits beyond the threshold (EDDF A66), and any
                // datum already inside its circle. See
                // docs/superpowers/specs/2026-06-13-gsx-vdgs-nose-stop-datum-handoff.md.
                if (_gate.StopLatitude.HasValue && _gate.StopLongitude.HasValue
                    && _gate.GateDistanceThreshold.HasValue && _gate.IsDeiceArea != true)
                {
                    bool clamped = DockingGeometry.ClampStopToOccupancy(
                        _gate.Latitude, _gate.Longitude,
                        _gate.StopLatitude.Value, _gate.StopLongitude.Value,
                        centerHdg, _gate.GateDistanceThreshold.Value,
                        ref sLat, ref sLon,
                        out double baseStopAlong, out double desiredAlong, out double clampedAlong);
                    if (clamped && !_occupancyClampLogged)
                    {
                        _occupancyClampLogged = true;
                        LogOccupancyClamp(_gate.GateDistanceThreshold.Value,
                            baseStopAlong, desiredAlong, clampedAlong, sLat, sLon);
                    }
                }

                double distM = NavigationCalculator.CalculateDistance(lat, lon, sLat, sLon) * DockingGeometry.MetresPerNm;
                double brg = NavigationCalculator.CalculateBearing(lat, lon, sLat, sLon);
                double hdgErr = DockingGeometry.NormalizeDeg180(brg - centerHdg);
                // Forward distance to the stop. The aircraft DATUM stops at the parking/stop
                // position: the MSFS parking position (and a GSX stop position) is where the
                // aircraft REFERENCE sits when correctly parked — the scenery jetway is placed to
                // reach the door for THAT datum location. (An earlier build subtracted the
                // per-aircraft gsx.cfg door offset here; that was wrong — it describes where the
                // door is ON the airframe, not a stop offset — and parked a B777 ~26 m short.
                // Do not reintroduce it. The door SIDE still feeds the jetway-side cue.)
                double alongM = DockingGeometry.AlongTrackMetres(distM, hdgErr);
                _lastAlongM = alongM;

                // Intercept-angle lineup to the gate centerline (the line through the stop
                // along the stop heading) — the cue docking pans with on the final approach.
                // Only computed near the gate / while engaged: in Idle/Armed at taxi distances
                // it fed nothing but the telemetry line, and the log itself has no diagnostic
                // value 3 km from the stop (it also cost an open/append/close file write 2×/s
                // on the SimConnect thread, under this lock, for the entire taxi).
                bool wantDetail = _state == DockState.Docking || _state == DockState.Stopped || distM < DetailRangeMetres;
                double acHdgTrue = headingMag + magVar;
                double lineupErr = 0.0, crossFt = 0.0;
                if (wantDetail)
                {
                    lineupErr = ComputeLineupError(lat, lon, acHdgTrue, sLat, sLon, centerHdg, alongM, out crossFt);
                    DockLog(groundSpeedKts, distM, alongM, hdgErr, lineupErr, crossFt, centerHdg, acHdgTrue, sLat, sLon, lat, lon);
                }
                double absCrossM = Math.Abs(crossFt) * 0.3048;

                switch (_state)
                {
                    case DockState.Idle:
                    case DockState.Armed:
                        // Cue 2: use the gate's gatedistancethreshold as engage range when set.
                        // Engage is additionally gated on cross-track FEASIBILITY (see
                        // DockingGeometry.ShouldEngage): while the aircraft is still on the
                        // apron lane / mid gate-turn with more lateral offset than the
                        // intercept profile can close, docking stays Armed and taxi guidance
                        // keeps the tone for the turn — the engage-latched arrival ownership
                        // this class documents. crossFt is only computed when wantDetail, but
                        // wantDetail is always true within engage range (distM < 150 ≥ any
                        // engage range), so gate the check on it for safety.
                        bool shouldEngage = wantDetail && DockingGeometry.ShouldEngage(
                            groundSpeedKts, alongM, hdgErr, absCrossM,
                            _engageRangeOverrideMetres ?? DockingGeometry.EngageRangeMetres);
                        // Pre-engage "stop is still ahead" snapshot for taxi's arrival
                        // wording (see _armedAwaitingSnap). Computed only with detail
                        // math available — far-field frames leave the last value, which
                        // is false until the aircraft first comes within detail range.
                        if (wantDetail)
                            _armedAwaitingSnap = !shouldEngage && alongM > PENDING_MIN_AHEAD_M;
                        if (shouldEngage) EngageLocked(alongM);
                        else _state = DockState.Armed;
                        break;

                    case DockState.Docking:
                        if (DockingGeometry.IsOvershoot(alongM))
                        {
                            _announcer.AnnounceImmediate("Stop. You have passed the stop position.");
                            // Silence (don't dispose) the beeper so a back-up-and-retry dock can
                            // re-engage with working audio; mark the stop as an overshoot so the
                            // Stopped state doesn't hold the solid "docked" tone over a bad park.
                            SilenceLocked(); _overshootStop = true;
                            _state = DockState.Stopped; _isActiveSnap = true; fireCompleted = true; break;
                        }
                        // Lateral miss — the approach can no longer converge (off the centerline
                        // in the squaring zone by more than the full intercept could close).
                        // Announce a verbal stop-and-retry instead of letting the tone steer
                        // garbage (KATL C55 2026-06-10: the squaring fade snapped the cue to the
                        // raw gate heading while ~75 ft off the line — a sudden hard right pan).
                        // No cross-track feet in the phrase — the tone is the lateral instrument;
                        // a feet quantity has no spatial reference for a blind pilot.
                        // Deice pads are wide and datum-aligned; they keep along-only semantics.
                        if (_gate?.IsDeiceArea != true && DockingGeometry.IsLateralMiss(alongM, absCrossM))
                        {
                            string side = crossFt > 0 ? "left" : "right"; // + = left of centerline
                            _announcer.AnnounceImmediate(
                                $"Stop. Too far {side} of the gate centerline. Back up and try again.");
                            SilenceLocked(); _overshootStop = true;
                            _state = DockState.Stopped; _isActiveSnap = true; fireCompleted = true; break;
                        }
                        if (DockingGeometry.IsStop(alongM)
                            && (_gate?.IsDeiceArea == true || absCrossM <= DockingGeometry.StopMaxCrossMetres))
                        {
                            // Cue 3: announce "GSX docking complete." instead of bare "Stop."
                            // when the gate is a GSX .ini stand with a real VDGS stop position
                            // (StopLatitude != null). Reaching OUR computed stop IS the reliable
                            // signal — no external GSX L-var needed. Deice areas and navdata-only
                            // gates (no VDGS stop position) keep the plain "Stop." callout.
                            // FSDT_GSX_OPERATEJETWAYS_STATE was investigated and rejected: it
                            // fires only when the user manually triggers the jetway, not on
                            // aircraft arrival, so it cannot serve as an auto-docked signal.
                            // Cross-gated (non-deice): "docking complete" requires the aircraft
                            // within StopMaxCrossMetres of the centerline — without it, the KATL
                            // C55 run would have announced a good dock 60 ft off the gate axis.
                            // Narrow residual band (cross 2.0–2.21 m at along 0–0.3 m): neither
                            // stop nor lateral-miss fires; the pilot creeps on and gets the
                            // overshoot "Stop." at −1 m — still a verbal closure.
                            string stopMsg = (_gate?.StopLatitude != null && _gate?.IsDeiceArea != true)
                                ? "GSX docking complete."
                                : "Stop.";
                            _announcer.AnnounceImmediate(stopMsg);
                            _tone.Stop(); // lateral steering done — kill the pan tone
                            // Hold a SOLID continuous tone (the beeper's _solid mode fires when
                            // alongM <= StopTolerance) as a "docked — hold position" marker.
                            // Do NOT stop the beeper here: the pilot wants the tone to persist until
                            // they end guidance (Stop button → SetDestinationGate(null) → ResetLocked)
                            // or move off the stop. Previously _beeper.Stop() at the same 0.3 m
                            // threshold the solid tone begins made the solid tone dead code.
                            _beeper.Update(alongM, active: true);
                            _state = DockState.Stopped; _isActiveSnap = true; fireCompleted = true; break;
                        }
                        if (alongM > DockingGeometry.DisengageRangeMetres || groundSpeedKts >= DockingGeometry.EngageGroundSpeedKts)
                        {
                            SilenceLocked(); _state = DockState.Armed; _isActiveSnap = false; break;
                        }
                        // Docking owns the precise lateral cue on the final approach (taxi's
                        // tone is muted while docking is engaged — see MainForm). Intercept-
                        // angle to the gate centerline corrects cross-track AND converges the
                        // heading to the gate, so the final park is square, not askew. The
                        // connector turns happen earlier, before docking engages, and are
                        // steered by taxi's route-following tone.
                        _tone.UpdateHeadingErrorWithThresholds(lineupErr, DockSilentThresholdDeg, DockActivationThresholdDeg, DockMaxPanThresholdDeg);
                        _beeper.Update(alongM, active: true);
                        if (!_slowDownSaid && alongM <= DockingGeometry.SlowDownMetres && groundSpeedKts > DockingGeometry.SlowDownSpeedKts)
                        {
                            _slowDownSaid = true;
                            _announcer.AnnounceImmediate("Slow down.");
                            return; // one callout per frame, consistent with the milestone pattern
                        }
                        AnnounceMilestonesLocked(alongM);
                        // Stopped-short reminder: engaged, parked mid-approach (past the milestones,
                        // short of the stop band), sitting still. Without this the pilot gets an
                        // endless fast-but-not-solid beep and no verbal closure — taxi's own
                        // "Stop. Hold position." stopped-in-zone cue is suppressed while docking
                        // owns the arrival, so docking must provide the closure itself.
                        if (groundSpeedKts < 0.5)
                        {
                            if (_stoppedSinceUtc == DateTime.MinValue) _stoppedSinceUtc = DateTime.UtcNow;
                            else if (!_stoppedShortSaid
                                     && alongM > DockingGeometry.StopToleranceMetres
                                     && alongM <= StoppedShortMaxMetres
                                     && (DateTime.UtcNow - _stoppedSinceUtc).TotalSeconds >= StoppedShortSeconds)
                            {
                                _stoppedShortSaid = true;
                                _announcer.AnnounceImmediate(
                                    $"{DistanceFormatter.FromMetres(alongM)} to stop. Continue forward.");
                            }
                        }
                        else { _stoppedSinceUtc = DateTime.MinValue; _stoppedShortSaid = false; }
                        break;

                    case DockState.Stopped:
                        // Keep the solid "docked — hold position" tone sounding while parked on the
                        // stop (alongM ~0 keeps the beeper in its continuous _solid mode) — except
                        // after an overshoot stop, where a "docked" marker over a bad park would
                        // mislead. Ends when the pilot ends guidance (Stop button →
                        // SetDestinationGate(null) → ResetLocked) or moves off the stop (below).
                        if (!_overshootStop) _beeper.Update(alongM, active: true);
                        // Two escape paths, both required:
                        // • ABSOLUTE distance (not along-track) for taxi-away — along-track goes
                        //   NEGATIVE once the stop is behind the aircraft, so the old
                        //   `alongM > 75` check could never fire for a forward taxi-out and the
                        //   state (and solid tone) latched forever. Raw distance works in every
                        //   direction, including a next-flight stale gate hundreds of km away.
                        // • BACK-UP re-arm for a retry: a pilot who overshoots (or wants a better
                        //   park) backs up a few metres — re-arming to Idle lets the normal
                        //   Idle/Armed → ShouldEngage path re-engage with fresh milestones.
                        if (distM > DockingGeometry.DisengageRangeMetres || alongM > RearmBackupMetres)
                            ResetLocked();
                        break;
                }
            }
            catch { SilenceLocked(); }
        }

        // Fire OUTSIDE the lock: the handler calls back into TaxiGuidanceManager
        // (its own lock), so raising it while holding _lock risks lock-order coupling.
        if (fireCompleted) { try { DockingCompleted?.Invoke(); } catch { } }
    }

    private void EngageLocked(double alongM)
    {
        _state = DockState.Docking;
        _isActiveSnap = true; _armedAwaitingSnap = false;
        _milestones = DistanceMilestones.Docking();
        _milestoneSaid = new bool[_milestones.Count];
        for (int i = 0; i < _milestones.Count; i++)
            if (alongM < _milestones[i].TriggerMetres) _milestoneSaid[i] = true; // already past this milestone at engage
        _slowDownSaid = false;
        _overshootStop = false;
        _stoppedSinceUtc = DateTime.MinValue;
        _stoppedShortSaid = false;
        string dist = DistanceFormatter.FromMetres(alongM);

        if (_gate?.IsDeiceArea == true)
        {
            // Deice areas: datum-aligned pad, no VDGS, no jetway/door-side phrase.
            _announcer.AnnounceImmediate($"Deicing guidance. {dist} to stop.");
        }
        else
        {
            string vdgs = FriendlyVdgs(_gate?.VdgsType);
            string orientationPhrase = string.IsNullOrEmpty(_doorSide)
                ? ""
                : (_gate?.HasJetway == true ? $" Jetway on your {_doorSide}." : $" Door on your {_doorSide}.");
            string baseMsg = string.IsNullOrEmpty(vdgs)
                ? $"Docking guidance. {dist} to stop."
                : $"Docking guidance. {vdgs}. {dist} to stop.";
            _announcer.AnnounceImmediate(baseMsg + orientationPhrase);
        }

        _tone.InvertPan = SettingsManager.Current.TaxiGuidanceInvertSteeringTone;
        _tone.HardPan = SettingsManager.Current.TaxiGuidanceHardPanTone;
        _tone.Start(SettingsManager.Current.TaxiGuidanceToneWaveform, SettingsManager.Current.TaxiGuidanceToneVolume);
        _beeper.Start(SettingsManager.Current.DockingBeepWaveform, SettingsManager.Current.DockingBeepVolume);
    }

    private void AnnounceMilestonesLocked(double alongM)
    {
        for (int i = 0; i < _milestones.Count; i++)
        {
            if (!_milestoneSaid[i] && alongM <= _milestones[i].TriggerMetres)
            {
                _milestoneSaid[i] = true;
                _announcer.AnnounceImmediate($"{_milestones[i].Label} to stop.");
                return;
            }
        }
    }

    /// <summary>
    /// Maps a GSX <c>parkingsystem</c> value to a brief spoken phrase for the engage callout.
    /// Returns an empty string for types that need no callout (deice VDGS, dummy, unknown).
    /// <para>Families confirmed against installed .ini profiles (June 2026):</para>
    /// <list type="bullet">
    ///   <item>Safedock*/SafeDock* → "SafeDock display"</item>
    ///   <item>Marshaller → "Marshaller"</item>
    ///   <item>Agnis* → "AGNIS"</item>
    ///   <item>Apis* → "APIS"</item>
    ///   <item>Rlg* → "lead-in lights"</item>
    ///   <item>VgdsDeIce* — deice-area VDGS; already excluded by the deice branch, but silenced here too</item>
    ///   <item>Vgds*, Honeywell*, Dummy, "1", unknown → empty (no callout)</item>
    /// </list>
    /// </summary>
    private static string FriendlyVdgs(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        if (v.StartsWith("Safedock", StringComparison.OrdinalIgnoreCase)) return "SafeDock display";
        if (v.StartsWith("Marshaller", StringComparison.OrdinalIgnoreCase)) return "Marshaller";
        if (v.StartsWith("Agnis", StringComparison.OrdinalIgnoreCase)) return "AGNIS";
        if (v.StartsWith("Apis", StringComparison.OrdinalIgnoreCase)) return "APIS";
        if (v.StartsWith("Rlg", StringComparison.OrdinalIgnoreCase)) return "lead-in lights";
        // VgdsDeIce* = deice-area system; already handled by the deice branch in EngageLocked,
        // but guard here so if a deice gate somehow reaches this path it stays silent.
        // Generic Vgds* / Honeywell* / unknown → no spoken type (not actionable for blind pilot).
        return string.Empty;
    }

    /// <summary>
    /// Appends a throttled telemetry line (≤ ~2/s) so a live docking run can be diagnosed
    /// post-hoc: state, ground speed, raw + along-track distances, the bearing-vs-centerline
    /// angle (hdgErr), the intercept lineup error + signed cross-track, the gate stop heading,
    /// the aircraft heading, the applied GSX stop offset, and absolute stop/aircraft coords.
    /// Only written near the gate or while engaged (see wantDetail in UpdatePosition). Path:
    /// the canonical AppLogs folder (%APPDATA%\MSFSBlindAssist\logs\docking.log). Never throws.
    /// </summary>
    private void DockLog(double gs, double distM, double alongM,
                         double hdgErr, double lineupErr, double crossFt,
                         double stopHeadingTrue, double acHdgTrue,
                         double stopLat, double stopLon, double acLat, double acLon)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDockLogUtc).TotalMilliseconds < 500) return;
        _lastDockLogUtc = now;
        try
        {
            _dockLog.Info(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "state={0} gs={1:F1} dist={2:F1} along={3:F1} " +
                "hdgErr={4:F1} lineupErr={5:F1} crossFt={6:F1} stopHdgTrue={7:F1} acHdgTrue={8:F1} " +
                "stopOffL={9:F2} stopOffLat={10:F2} deice={11} " +
                "stopLat={12:F8} stopLon={13:F8} acLat={14:F8} acLon={15:F8}",
                _state, gs, distM, alongM,
                hdgErr, lineupErr, crossFt, stopHeadingTrue, acHdgTrue,
                _stopOffset.LongitudinalMetres, _stopOffset.LateralMetres,
                _gate?.IsDeiceArea == true, stopLat, stopLon, acLat, acLon));
        }
        catch { /* logging must never break docking */ }
    }

    /// <summary>
    /// One-shot diagnostic written the first frame the occupancy clamp moves the stop for a
    /// gate (latched by <c>_occupancyClampLogged</c>, reset per gate in ResetLocked). Lands in
    /// docking-aircraft.log next to the STOPOFFSET line so a "GSX still says reposition" report
    /// can be checked at a glance: thr = gatedistancethreshold, gap = base-stop along-track,
    /// desired = the .py-shifted along-track, clamped = the new along-track. Never throws.
    /// </summary>
    private void LogOccupancyClamp(double threshold, double baseStopAlong,
                                   double desiredAlong, double clampedAlong, double sLat, double sLon)
    {
        try
        {
            _aircraftLog.Info(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "OCCUPANCY-CLAMP  icao='{0}' gate='{1}' vdgs='{2}' thr={3:F1} " +
                "gap={4:F1} desired={5:F1} -> clamped={6:F1} (margin {7:F1}) stopLat={8:F8} stopLon={9:F8}",
                _gate?.AirportICAO ?? "", _gate?.ToString() ?? "", _gate?.VdgsType ?? "",
                threshold, baseStopAlong, desiredAlong, clampedAlong,
                DockingGeometry.OccupancyClampMarginMetres, sLat, sLon));
        }
        catch { /* logging must never break docking */ }
    }

    /// <summary>
    /// Intercept-angle lineup error (degrees) to the gate centerline — the line through the
    /// stop position (<paramref name="sLat"/>,<paramref name="sLon"/>) along the stop heading
    /// (<paramref name="centerHdgTrue"/>). Mirrors the runway/gate lineup: the desired heading
    /// is the centerline heading biased toward the line by an intercept that rises on a sqrt
    /// curve with cross-track (0° at the line, up to 30° far off), so steering to it corrects
    /// BOTH lateral offset and heading. Positive = steer right. <paramref name="crossFt"/> is
    /// the signed cross-track (+ = left of centerline).
    /// </summary>
    private static double ComputeLineupError(
        double lat, double lon, double acHdgTrue,
        double sLat, double sLon, double centerHdgTrue, double alongMetres, out double crossFt)
    {
        var track = RunwayCenterlineTracker.Compute(lat, lon, acHdgTrue, sLat, sLon, centerHdgTrue);
        crossFt = track.CrossTrackFeet;

        // Intercept ramp + squaring fade live in DockingGeometry.LineupInterceptDeg so the
        // probe pins the exact numbers. Design notes preserved from the live tuning passes:
        //   • 1 ft deadband / 40 ft saturation (EDDF A66 B77W: the old 8 ft deadband parked
        //     the aircraft ~8 ft off the line because it never corrected inside it). Cross
        //     convergence is a function of DISTANCE travelled, not time, so the profile
        //     closes the same amount per metre at 1 kt as at 5 kt.
        //   • Squaring fade 6 → 2.5 m: finish the align turn early enough to complete it at
        //     creep speed (the B77W entered the box ~5° over-rotated when the fade was
        //     crammed into the final metre).
        //   • The fade is CROSS-GATED (full ≤ 4 ft, off ≥ 8 ft): keyed on along-track alone
        //     it snapped the desired heading from (gate−35°) to the raw gate heading while
        //     the aircraft was still ~75 ft off the line — the KATL C55 (2026-06-10) sudden
        //     hard-right pan. Off the line, the cue keeps converging until the lateral-miss
        //     callout adjudicates; the miss fires from the same squaring zone, so the tone
        //     never silently steers an unreachable approach.
        double intercept = DockingGeometry.LineupInterceptDeg(crossFt, alongMetres);

        double desiredHdg = centerHdgTrue + intercept;
        return DockingGeometry.NormalizeDeg180(desiredHdg - acHdgTrue);
    }

    /// <summary>
    /// Shifts the stop point (<paramref name="sLat"/>,<paramref name="sLon"/>) by the GSX
    /// stop offset: <paramref name="offset"/>.LongitudinalMetres along <paramref name="stopHeadingTrue"/>
    /// (forward-positive) and LateralMetres perpendicular (heading+90°, right-positive). Uses an
    /// equirectangular metres→degrees conversion, which is exact enough at the ~tens-of-metres
    /// scale of a gate offset. <c>GsxOffset.Zero</c> leaves the point unchanged.
    /// </summary>
    private static void ShiftStop(ref double sLat, ref double sLon, double stopHeadingTrue, GsxOffset offset)
        => DockingGeometry.ShiftStopMetres(
            sLat, sLon, stopHeadingTrue,
            offset.LongitudinalMetres, offset.LateralMetres, out sLat, out sLon);

    private void SilenceLocked() { try { _tone.Stop(); } catch { } try { _beeper.Update(0, active: false); } catch { } }
    private void ResetLocked()
    {
        SilenceLocked(); try { _beeper.Stop(); } catch { }
        _state = DockState.Idle; _isActiveSnap = false; _armedAwaitingSnap = false;
        _milestones = Array.Empty<DistanceMilestone>(); _milestoneSaid = Array.Empty<bool>();
        _slowDownSaid = false; _overshootStop = false;
        _stoppedSinceUtc = DateTime.MinValue; _stoppedShortSaid = false;
        _occupancyClampLogged = false;
    }

    public void Dispose()
    {
        // Set the flag under the lock so any in-progress UpdatePosition sees it
        // before we tear down audio. The beeper is disposed outside the lock to
        // avoid holding _lock across the beeper's own internal teardown.
        lock (_lock) { if (_disposed) return; _disposed = true; _isActiveSnap = false; _armedAwaitingSnap = false; try { _tone.Stop(); } catch { } }
        _beeper.Dispose();
    }
}
