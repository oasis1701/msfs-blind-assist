using System.IO;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Settings;

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
    private IReadOnlyList<DistanceMilestone> _milestones = Array.Empty<DistanceMilestone>();
    private bool[] _milestoneSaid = Array.Empty<bool>();
    private bool _slowDownSaid;
    private double _doorOffsetMetres; // longitudinal offset (metres, forward of datum); 0 = align datum
    private string _doorSide = ""; // "left" / "right" / "" — preferred passenger door side, for jetway orientation
    private double _lastDoorAlongM;  // last door-aligned forward distance (m), for the status query
    private GsxOffset _stopOffset = GsxOffset.Zero; // GSX .py per-aircraft stop offset (metres); Zero = base navdata stop
    // Cue 2: GSX gatedistancethreshold override for engage range (null = use DockingGeometry.EngageRangeMetres).
    // Clamped to [20, 70] m when non-null. Set from the .ini gate's gatedistancethreshold field.
    private double? _engageRangeOverrideMetres;

    // Throttled telemetry so a live docking run can be diagnosed post-hoc.
    private static readonly string DockLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MSFSBlindAssist", "logs", "docking.log");
    private DateTime _lastDockLogUtc = DateTime.MinValue;

    public DockingGuidanceManager(ScreenReaderAnnouncer announcer)
        => _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

    /// <summary>True while docking is actively guiding (Docking or Stopped) — used to suppress the taxi steering tone.</summary>
    public bool IsActive { get { lock (_lock) { return _state == DockState.Docking || _state == DockState.Stopped; } } }

    /// <summary>
    /// True when docking owns the terminal arrival for the current destination — a gate is set
    /// and docking guidance is enabled — REGARDLESS of whether the final approach has formally
    /// engaged yet. Taxi uses this to suppress its OWN terminal arrival callouts for the whole
    /// gate approach (not just the engaged window), so it never says "Stop. Hold position." at
    /// its route-end node while docking is still guiding the aircraft a few metres deeper to the
    /// precise GSX stop.
    /// </summary>
    public bool OwnsArrival { get { lock (_lock) { return _gate != null && SettingsManager.Current.DockingGuidanceEnabled; } } }

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
                return $"Docking. {DistanceFormatter.FromMetres(Math.Max(0.0, _lastDoorAlongM))} to {what}.";
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

    /// <summary>Per-aircraft longitudinal door offset (metres, forward of datum). 0 = align the datum (no GSX data).</summary>
    public void SetDoorOffsetMetres(double metres) { lock (_lock) { _doorOffsetMetres = metres; } }

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
                if (_gate == null || !SettingsManager.Current.DockingGuidanceEnabled) { SilenceLocked(); return; }

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

                double distM = NavigationCalculator.CalculateDistance(lat, lon, sLat, sLon) * DockingGeometry.MetresPerNm;
                double brg = NavigationCalculator.CalculateBearing(lat, lon, sLat, sLon);
                double hdgErr = DockingGeometry.NormalizeDeg180(brg - centerHdg);
                double alongM = DockingGeometry.AlongTrackMetres(distM, hdgErr);
                // Door-aligned distance: stop when the DOOR reaches the gate stop, not the datum.
                // For deice areas the aircraft datum (not a door at a jetway) aligns to the pad,
                // so the effective offset is 0. For normal gates use the per-aircraft door offset.
                // When offset is 0 (unknown), doorAlongM == alongM and behaviour is unchanged.
                // Stop the aircraft DATUM at the parking/stop position. The MSFS parking
                // position (and a GSX stop position) is where the aircraft REFERENCE sits when
                // correctly parked — the scenery jetway is placed to reach the door for THAT
                // datum location. Subtracting the per-aircraft door offset was wrong: it
                // describes where the door is on the airframe, NOT a stop offset, and it parked
                // a B777 ~26 m short (datum a whole fuselage back — GSX read it as "way off")
                // while starving the lateral convergence of distance. Align the datum directly.
                // (The LATERAL door offset still feeds the "jetway on your left/right" cue.)
                double doorAlongM = alongM;
                _lastDoorAlongM = doorAlongM;

                // Intercept-angle lineup to the gate centerline (the line through the stop
                // along the stop heading). Corrects BOTH cross-track AND heading — this is
                // the cue docking pans with on the final approach, so the park ends up square
                // on the centerline instead of "a bit right and askew". hdgErr (bearing-to-
                // stop vs centerline) is still used for the engage cone check below.
                double acHdgTrue = headingMag + magVar;
                double lineupErr = ComputeLineupError(lat, lon, acHdgTrue, sLat, sLon, centerHdg, alongM, out double crossFt);

                DockLog(groundSpeedKts, distM, alongM, doorAlongM, hdgErr, lineupErr, crossFt, centerHdg, acHdgTrue, sLat, sLon, lat, lon);

                switch (_state)
                {
                    case DockState.Idle:
                    case DockState.Armed:
                        // Cue 2: use the gate's gatedistancethreshold as engage range when set.
                        bool shouldEngage = _engageRangeOverrideMetres.HasValue
                            ? DockingGeometry.ShouldEngage(groundSpeedKts, alongM, hdgErr, _engageRangeOverrideMetres.Value)
                            : DockingGeometry.ShouldEngage(groundSpeedKts, alongM, hdgErr);
                        if (shouldEngage) EngageLocked(doorAlongM);
                        else _state = DockState.Armed;
                        break;

                    case DockState.Docking:
                        if (DockingGeometry.IsOvershoot(doorAlongM))
                        {
                            _announcer.AnnounceImmediate("Stop. You have passed the stop position.");
                            _beeper.Stop(); SilenceLocked(); _state = DockState.Stopped; fireCompleted = true; break;
                        }
                        if (DockingGeometry.IsStop(doorAlongM))
                        {
                            // Cue 3: announce "GSX docking complete." instead of bare "Stop."
                            // when the gate is a GSX .ini stand with a real VDGS stop position
                            // (StopLatitude != null). Reaching OUR computed stop IS the reliable
                            // signal — no external GSX L-var needed. Deice areas and navdata-only
                            // gates (no VDGS stop position) keep the plain "Stop." callout.
                            // FSDT_GSX_OPERATEJETWAYS_STATE was investigated and rejected: it
                            // fires only when the user manually triggers the jetway, not on
                            // aircraft arrival, so it cannot serve as an auto-docked signal.
                            string stopMsg = (_gate?.StopLatitude != null && _gate?.IsDeiceArea != true)
                                ? "GSX docking complete."
                                : "Stop.";
                            _announcer.AnnounceImmediate(stopMsg);
                            _tone.Stop(); // lateral steering done — kill the pan tone
                            // Hold a SOLID continuous tone (the beeper's _solid mode fires when
                            // doorAlongM <= StopTolerance) as a "docked — hold position" marker.
                            // Do NOT stop the beeper here: the pilot wants the tone to persist until
                            // they end guidance (Stop button → SetDestinationGate(null) → ResetLocked)
                            // or taxi away. Previously _beeper.Stop() at the same 0.3 m threshold the
                            // solid tone begins made the solid tone dead code — the beep just vanished.
                            _beeper.Update(doorAlongM, active: true);
                            _state = DockState.Stopped; fireCompleted = true; break;
                        }
                        if (alongM > DockingGeometry.DisengageRangeMetres || groundSpeedKts >= DockingGeometry.EngageGroundSpeedKts)
                        {
                            SilenceLocked(); _state = DockState.Armed; break;
                        }
                        // Docking owns the precise lateral cue on the final approach (taxi's
                        // tone is muted while docking is engaged — see MainForm). Intercept-
                        // angle to the gate centerline corrects cross-track AND converges the
                        // heading to the gate, so the final park is square, not askew. The
                        // connector turns happen earlier, before docking engages, and are
                        // steered by taxi's route-following tone.
                        _tone.UpdateHeadingErrorWithThresholds(lineupErr, DockSilentThresholdDeg, DockActivationThresholdDeg, DockMaxPanThresholdDeg);
                        _beeper.Update(doorAlongM, active: true);
                        if (!_slowDownSaid && doorAlongM <= DockingGeometry.SlowDownMetres && groundSpeedKts > DockingGeometry.SlowDownSpeedKts)
                        {
                            _slowDownSaid = true;
                            _announcer.AnnounceImmediate("Slow down.");
                            return; // one callout per frame, consistent with the milestone pattern
                        }
                        AnnounceMilestonesLocked(doorAlongM);
                        break;

                    case DockState.Stopped:
                        // Keep the solid "docked — hold position" tone sounding (doorAlongM is ~0
                        // while parked, so the beeper stays in its continuous _solid mode) until the
                        // pilot ends guidance (Stop button → SetDestinationGate(null) → ResetLocked,
                        // which stops the beeper) or taxis away (disengage below).
                        _beeper.Update(doorAlongM, active: true);
                        if (doorAlongM > DockingGeometry.DisengageRangeMetres) ResetLocked();
                        break;
                }
            }
            catch { SilenceLocked(); }
        }

        // Fire OUTSIDE the lock: the handler calls back into TaxiGuidanceManager
        // (its own lock), so raising it while holding _lock risks lock-order coupling.
        if (fireCompleted) { try { DockingCompleted?.Invoke(); } catch { } }
    }

    private void EngageLocked(double doorAlongM)
    {
        _state = DockState.Docking;
        _milestones = DistanceMilestones.Docking();
        _milestoneSaid = new bool[_milestones.Count];
        for (int i = 0; i < _milestones.Count; i++)
            if (doorAlongM < _milestones[i].TriggerMetres) _milestoneSaid[i] = true; // already past this milestone at engage
        _slowDownSaid = false;
        string dist = DistanceFormatter.FromMetres(doorAlongM);

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
    /// post-hoc: state, ground speed, raw + along-track + door-aligned distances, the
    /// lateral cross-axis angle (hdgErr), whether the lateral tone is muted by taxi,
    /// the gate stop heading, and the aircraft heading. Path:
    /// %LOCALAPPDATA%\MSFSBlindAssist\logs\docking.log. Never throws.
    /// </summary>
    private void DockLog(double gs, double distM, double alongM, double doorAlongM,
                         double hdgErr, double lineupErr, double crossFt,
                         double stopHeadingTrue, double acHdgTrue,
                         double stopLat, double stopLon, double acLat, double acLon)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDockLogUtc).TotalMilliseconds < 500) return;
        _lastDockLogUtc = now;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DockLogPath)!);
            File.AppendAllText(DockLogPath, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} state={1} gs={2:F1} dist={3:F1} along={4:F1} doorAlong={5:F1} " +
                "hdgErr={6:F1} lineupErr={7:F1} crossFt={8:F1} stopHdgTrue={9:F1} acHdgTrue={10:F1} " +
                "offset={11:F2} stopOffL={12:F2} stopOffLat={13:F2} deice={14} " +
                "stopLat={15:F8} stopLon={16:F8} acLat={17:F8} acLon={18:F8}{19}",
                DateTime.Now, _state, gs, distM, alongM, doorAlongM,
                hdgErr, lineupErr, crossFt, stopHeadingTrue, acHdgTrue,
                _doorOffsetMetres, _stopOffset.LongitudinalMetres, _stopOffset.LateralMetres,
                _gate?.IsDeiceArea == true, stopLat, stopLon, acLat, acLon, Environment.NewLine));
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
        double absCross = track.AbsCrossTrackFeet;

        // Intercept ramp for a JETWAY-PRECISE gate lead-in. The previous 8 ft deadband stopped
        // correcting once cross-track fell below ~8 ft, so the aircraft parked up to ~8 ft (2.4 m)
        // off the gate centerline (a live B77W dock at EDDF A66 sat at ~8.2 ft the whole approach
        // because it was inside the deadband from the start). Drop the deadband to 1 ft — keep a
        // hair so SimConnect position jitter doesn't hunt the tone left/right at the exact centre —
        // and steepen the saturation (60→40 ft) so a small residual still gets a meaningful
        // correction angle and actually closes. KEY: cross-track convergence is a function of
        // DISTANCE travelled, not time (d(cross)/d(forward) = −sin(angle)), so this closes the
        // same amount per metre at 1 kt as at 5 kt — "even at 1 kt" needs no special handling, and
        // the continuous sqrt ramp never springs a late turn. The intercept still eases to 0° at
        // the line; the distance fade below squares the final heading so the park is precise AND
        // not askew.
        const double MaxInterceptDeg = 35.0, DeadbandFt = 1.0, SaturationFt = 40.0;
        double intercept = 0.0;
        if (absCross > DeadbandFt)
        {
            double eff = absCross - DeadbandFt;
            double span = SaturationFt - DeadbandFt;
            intercept = MaxInterceptDeg * Math.Sqrt(Math.Clamp(eff / span, 0.0, 1.0)) * Math.Sign(crossFt);
        }

        // FADE the intercept to zero over the FINAL few metres so that AT the stop the cue
        // is the pure gate heading, not the convergence-biased heading. This mirrors a real
        // painted lead-in line: it angles you toward the centerline, then straightens onto it
        // right at the stop so you finish centered AND square. A stationary nose-in aircraft
        // cannot reduce lateral offset (no sideways motion), so "keep converging" at the stop
        // is futile and reads as a wrong-way cue (a slight RIGHT bias while 10 ft left when
        // the pilot just wants to square LEFT to the gate). Square the heading to the pure gate
        // heading by 2.5 m out (fade 6→2.5 m), NOT crammed into the final metre. A live B77W dock
        // entered the box ~5° over-rotated and the squaring cue only got strong in the last ~2.5 m
        // — too late to finish the turn at 1–2 kt, so the pilot stopped mid-turn ~2° off. Finishing
        // the square by 2.5 m gives a clear early "turn to align" cue AND leaves a straight, already-
        // aligned creep over the final 2.5 m to the stop. The tight 1 ft deadband still centres the
        // lateral well before this zone, so finishing square here costs ~nothing in cross-track.
        const double FadeStartM = 6.0, FadeEndM = 2.5;
        double fade = Math.Clamp((alongMetres - FadeEndM) / (FadeStartM - FadeEndM), 0.0, 1.0);
        intercept *= fade;

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
    private void ResetLocked() { SilenceLocked(); try { _beeper.Stop(); } catch { } _state = DockState.Idle; _milestones = Array.Empty<DistanceMilestone>(); _milestoneSaid = Array.Empty<bool>(); _slowDownSaid = false; }

    public void Dispose()
    {
        // Set the flag under the lock so any in-progress UpdatePosition sees it
        // before we tear down audio. The beeper is disposed outside the lock to
        // avoid holding _lock across the beeper's own internal teardown.
        lock (_lock) { if (_disposed) return; _disposed = true; try { _tone.Stop(); } catch { } }
        _beeper.Dispose();
    }
}
