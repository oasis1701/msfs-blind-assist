using System.IO;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
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
        lock (_lock) { _gate = gate; ResetLocked(); }
    }

    /// <summary>Per-aircraft longitudinal door offset (metres, forward of datum). 0 = align the datum (no GSX data).</summary>
    public void SetDoorOffsetMetres(double metres) { lock (_lock) { _doorOffsetMetres = metres; } }

    /// <summary>"left" / "right" / "" — the preferred passenger door side, for jetway orientation.</summary>
    public void SetDoorSide(string side) { lock (_lock) { _doorSide = side ?? ""; } }

    public void UpdatePosition(double lat, double lon, double headingMag, double magVar, double groundSpeedKts)
    {
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

                DockLog(groundSpeedKts, distM, alongM, doorAlongM, hdgErr, lineupErr, crossFt, centerHdg, acHdgTrue);

                switch (_state)
                {
                    case DockState.Idle:
                    case DockState.Armed:
                        if (DockingGeometry.ShouldEngage(groundSpeedKts, alongM, hdgErr)) EngageLocked(doorAlongM);
                        else _state = DockState.Armed;
                        break;

                    case DockState.Docking:
                        if (DockingGeometry.IsOvershoot(doorAlongM))
                        {
                            _announcer.AnnounceImmediate("Stop. You have passed the stop position.");
                            _beeper.Stop(); SilenceLocked(); _state = DockState.Stopped; break;
                        }
                        if (DockingGeometry.IsStop(doorAlongM))
                        {
                            _announcer.AnnounceImmediate("Stop.");
                            _beeper.Stop();
                            _tone.Stop();
                            _state = DockState.Stopped; break;
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
                        if (doorAlongM > DockingGeometry.DisengageRangeMetres) ResetLocked();
                        break;
                }
            }
            catch { SilenceLocked(); }
        }
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

    private static string FriendlyVdgs(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        if (v.StartsWith("Safedock", StringComparison.OrdinalIgnoreCase)) return "SafeDock";
        if (v.StartsWith("Marshaller", StringComparison.OrdinalIgnoreCase)) return "Marshaller";
        if (v.StartsWith("Apis", StringComparison.OrdinalIgnoreCase)) return "APIS";
        if (v.StartsWith("Agnis", StringComparison.OrdinalIgnoreCase)) return "AGNIS";
        if (v.StartsWith("Honeywell", StringComparison.OrdinalIgnoreCase)) return "Honeywell";
        if (v.StartsWith("Rlg", StringComparison.OrdinalIgnoreCase)) return "RLG";
        if (v.StartsWith("Vgds", StringComparison.OrdinalIgnoreCase)) return "VDGS";
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
                         double stopHeadingTrue, double acHdgTrue)
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
                "offset={11:F2} deice={12}{13}",
                DateTime.Now, _state, gs, distM, alongM, doorAlongM,
                hdgErr, lineupErr, crossFt, stopHeadingTrue, acHdgTrue,
                _doorOffsetMetres, _gate?.IsDeiceArea == true, Environment.NewLine));
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

        // Intercept ramp tuned for a gate lead-in: a touch sharper than the runway's
        // 30°/100 ft so it actually closes a 10–15 ft entry over the approach (the gentle
        // ramp only commanded ~7° at 13 ft and stalled there). 8 ft deadband + sqrt curve
        // still ease it to zero on the line.
        const double MaxInterceptDeg = 35.0, DeadbandFt = 8.0, SaturationFt = 60.0;
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
        // the pilot just wants to square LEFT to the gate). Start the fade LATE (6 m) so the
        // lateral keeps closing as long as possible, then square up over the last ~4.5 m.
        const double FadeStartM = 6.0, FadeEndM = 1.5;
        double fade = Math.Clamp((alongMetres - FadeEndM) / (FadeStartM - FadeEndM), 0.0, 1.0);
        intercept *= fade;

        double desiredHdg = centerHdgTrue + intercept;
        return DockingGeometry.NormalizeDeg180(desiredHdg - acHdgTrue);
    }

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
