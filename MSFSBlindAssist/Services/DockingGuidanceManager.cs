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

    private const double GateWidthFeet = 20.0; // steering-tone width basis (narrow = tight centerline)

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TaxiSteeringTone _tone = new();
    private readonly ProximityBeeper _beeper = new();
    private readonly object _lock = new();

    private ParkingSpot? _gate;
    private DockState _state = DockState.Idle;
    private IReadOnlyList<DistanceMilestone> _milestones = Array.Empty<DistanceMilestone>();
    private bool[] _milestoneSaid = Array.Empty<bool>();
    private bool _slowDownSaid;

    public DockingGuidanceManager(ScreenReaderAnnouncer announcer)
        => _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));

    /// <summary>Set (or clear) the destination gate the pilot is taxiing to. Resets state + audio.</summary>
    public void SetDestinationGate(ParkingSpot? gate)
    {
        lock (_lock) { _gate = gate; ResetLocked(); }
    }

    public void UpdatePosition(double lat, double lon, double headingMag, double magVar, double groundSpeedKts)
    {
        lock (_lock)
        {
            try
            {
                if (_gate == null || !SettingsManager.Current.DockingGuidanceEnabled) { SilenceLocked(); return; }

                double sLat = _gate.StopLatitude ?? _gate.Latitude;
                double sLon = _gate.StopLongitude ?? _gate.Longitude;
                double centerHdg = _gate.StopHeading ?? _gate.Heading;

                double distM = NavigationCalculator.CalculateDistance(lat, lon, sLat, sLon) * DockingGeometry.MetresPerNm;
                double brg = NavigationCalculator.CalculateBearing(lat, lon, sLat, sLon);
                double hdgErr = DockingGeometry.NormalizeDeg180(brg - centerHdg);
                double alongM = DockingGeometry.AlongTrackMetres(distM, hdgErr);

                switch (_state)
                {
                    case DockState.Idle:
                    case DockState.Armed:
                        if (DockingGeometry.ShouldEngage(groundSpeedKts, alongM, hdgErr)) EngageLocked(alongM);
                        else _state = DockState.Armed;
                        break;

                    case DockState.Docking:
                        if (DockingGeometry.IsOvershoot(alongM))
                        {
                            _announcer.AnnounceImmediate("Stop. You have passed the stop position.");
                            SilenceLocked(); _state = DockState.Stopped; break;
                        }
                        if (DockingGeometry.IsStop(alongM))
                        {
                            _announcer.AnnounceImmediate("Stop.");
                            _beeper.Update(alongM, active: true); // solid
                            _tone.Stop();
                            _state = DockState.Stopped; break;
                        }
                        if (alongM > DockingGeometry.DisengageRangeMetres || groundSpeedKts >= DockingGeometry.EngageGroundSpeedKts)
                        {
                            SilenceLocked(); _state = DockState.Armed; break;
                        }
                        _tone.UpdateHeadingError(hdgErr, GateWidthFeet);
                        _beeper.Update(alongM, active: true);
                        if (!_slowDownSaid && alongM <= DockingGeometry.SlowDownMetres)
                        {
                            _slowDownSaid = true;
                            _announcer.AnnounceImmediate("Slow down.");
                            return; // one callout per frame, consistent with the milestone pattern
                        }
                        AnnounceMilestonesLocked(alongM);
                        break;

                    case DockState.Stopped:
                        if (alongM > DockingGeometry.DisengageRangeMetres) ResetLocked();
                        break;
                }
            }
            catch { SilenceLocked(); }
        }
    }

    private void EngageLocked(double alongM)
    {
        _state = DockState.Docking;
        _milestones = DistanceMilestones.Docking();
        _milestoneSaid = new bool[_milestones.Count];
        _slowDownSaid = false;
        string vdgs = FriendlyVdgs(_gate?.VdgsType);
        string dist = DistanceFormatter.FromMetres(alongM);
        _announcer.AnnounceImmediate(string.IsNullOrEmpty(vdgs)
            ? $"Docking guidance. {dist} to stop."
            : $"Docking guidance. {vdgs}. {dist} to stop.");
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

    private void SilenceLocked() { try { _tone.Stop(); } catch { } try { _beeper.Update(0, active: false); } catch { } }
    private void ResetLocked() { SilenceLocked(); try { _beeper.Stop(); } catch { } _state = DockState.Idle; _milestones = Array.Empty<DistanceMilestone>(); _milestoneSaid = Array.Empty<bool>(); _slowDownSaid = false; }

    public void Dispose() { try { _tone.Stop(); } catch { } _beeper.Dispose(); }
}
