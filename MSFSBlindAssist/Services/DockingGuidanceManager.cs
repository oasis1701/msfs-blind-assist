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
    private bool _lateralSuppressed; // taxi guidance is steering the route — mute docking's own lateral tone
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
    /// When true, docking does NOT play its own lateral steering tone — taxi guidance
    /// is actively steering the route (including the connector turns into the gate), so a
    /// second panning tone would both confuse the pilot and fight the route geometry.
    /// Docking keeps its proximity beep, distance milestones, and stop logic regardless.
    /// Set back to false once taxi has finished steering (reached/parked) so docking
    /// provides the final precise lateral nudge to the stop.
    /// </summary>
    public void SetLateralToneSuppressed(bool suppressed) { lock (_lock) { _lateralSuppressed = suppressed; } }

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
                double effOffset = (_gate?.IsDeiceArea == true) ? 0.0 : _doorOffsetMetres;
                double doorAlongM = alongM - effOffset;
                _lastDoorAlongM = doorAlongM;
                DockLog(groundSpeedKts, distM, alongM, doorAlongM, hdgErr, centerHdg, headingMag, magVar);

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
                        // Lateral cue: only when taxi guidance is NOT steering the route.
                        // While taxi owns steering (connector turns into the gate), docking's
                        // straight-to-stop tone would fight the route, so stay silent on it —
                        // the beep + milestones still convey closing distance. Once taxi has
                        // finished steering, docking resumes the precise lateral nudge.
                        if (_lateralSuppressed)
                        {
                            _tone.Pause();
                        }
                        else
                        {
                            _tone.Resume();
                            _tone.UpdateHeadingErrorWithThresholds(hdgErr, DockSilentThresholdDeg, DockActivationThresholdDeg, DockMaxPanThresholdDeg);
                        }
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
                         double hdgErr, double stopHeadingTrue, double headingMag, double magVar)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastDockLogUtc).TotalMilliseconds < 500) return;
        _lastDockLogUtc = now;
        try
        {
            double acHdgTrue = headingMag + magVar;
            Directory.CreateDirectory(Path.GetDirectoryName(DockLogPath)!);
            File.AppendAllText(DockLogPath, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff} state={1} gs={2:F1} dist={3:F1} along={4:F1} doorAlong={5:F1} " +
                "hdgErr={6:F1} latMuted={7} stopHdgTrue={8:F1} acHdgTrue={9:F1} offset={10:F2} deice={11}{12}",
                DateTime.Now, _state, gs, distM, alongM, doorAlongM,
                hdgErr, _lateralSuppressed, stopHeadingTrue, acHdgTrue,
                _doorOffsetMetres, _gate?.IsDeiceArea == true, Environment.NewLine));
        }
        catch { /* logging must never break docking */ }
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
