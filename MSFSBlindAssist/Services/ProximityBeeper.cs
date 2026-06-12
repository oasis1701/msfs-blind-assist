using System.Diagnostics;
using System.Threading;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Accelerating proximity beep (car-parking-sensor metaphor) for docking distance.
/// Owns its own AudioToneGenerator; a timer produces the rhythm. The position loop
/// only calls Update(distance, active). Thread-safe; never throws to the caller.
/// </summary>
public sealed class ProximityBeeper : IDisposable
{
    private const double BeepFrequencyHz = 660.0;
    private const int BeepOnMs = 60;
    private const int TimerTickMs = 15;

    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private AudioToneGenerator? _gen;
    private System.Threading.Timer? _timer;
    private double _volume = 0.05;
    private HandFlyWaveType _waveform = HandFlyWaveType.Sine;
    private double _intervalMs = DockingGeometry.BeepIntervalFarMs;
    private double _smoothedIntervalMs = DockingGeometry.BeepIntervalFarMs;
    private bool _active;
    private bool _solid;
    private bool _beepOn;
    private double _sinceLastBeepMs;
    private double _beepOnElapsedMs;
    private double _lastTickMs;

    public void Start(HandFlyWaveType waveform, double volume)
    {
        lock (_lock)
        {
            _volume = volume;
            _active = false; _solid = false; _beepOn = false;
            _sinceLastBeepMs = 0; _beepOnElapsedMs = 0;
            _smoothedIntervalMs = DockingGeometry.BeepIntervalFarMs;
            _intervalMs = DockingGeometry.BeepIntervalFarMs;
            _lastTickMs = _stopwatch.Elapsed.TotalMilliseconds;
            // The generator is kept alive across Start calls (a docking re-engage goes
            // through SilenceLocked, which does NOT Stop the beeper), so a waveform
            // setting changed between engagements must recreate it — reusing the old
            // generator would silently keep the stale waveform.
            if (_gen != null && waveform != _waveform)
            {
                try { _gen.Stop(); } catch { }
                _gen.Dispose();
                _gen = null;
            }
            _waveform = waveform;
            if (_gen == null)
            {
                _gen = new AudioToneGenerator();
                _gen.Start(waveform, 0.0, BeepFrequencyHz); // silent, kept alive
            }
            _timer ??= new System.Threading.Timer(OnTick, null, 0, TimerTickMs);
        }
    }

    /// <summary>Set distance + whether the beeper should sound. distanceMetres = forward distance to stop.</summary>
    public void Update(double distanceMetres, bool active)
    {
        lock (_lock)
        {
            _active = active;
            if (!active) { _solid = false; SetGenVolume(0.0); _beepOn = false; return; }
            double raw = DockingGeometry.BeepIntervalMs(distanceMetres);
            _smoothedIntervalMs += 0.25 * (raw - _smoothedIntervalMs);
            _intervalMs = _smoothedIntervalMs;
            _solid = distanceMetres <= DockingGeometry.StopToleranceMetres;
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            double now = _stopwatch.Elapsed.TotalMilliseconds;
            lock (_lock)
            {
                double dt = now - _lastTickMs;
                _lastTickMs = now;
                if (dt <= 0 || dt > 200) dt = TimerTickMs;

                if (_gen == null) return;
                if (!_active) { if (_beepOn) { SetGenVolume(0.0); _beepOn = false; } return; }
                if (_solid) { if (!_beepOn) { SetGenVolume(_volume); _beepOn = true; } return; }

                if (_beepOn)
                {
                    _beepOnElapsedMs += dt;
                    if (_beepOnElapsedMs >= BeepOnMs) { SetGenVolume(0.0); _beepOn = false; _sinceLastBeepMs = 0; }
                }
                else
                {
                    _sinceLastBeepMs += dt;
                    if (_sinceLastBeepMs >= _intervalMs) { SetGenVolume(_volume); _beepOn = true; _beepOnElapsedMs = 0; }
                }
            }
        }
        catch { /* never throw from timer */ }
    }

    private void SetGenVolume(double v) { try { _gen?.UpdateVolume(v); } catch { } }

    public void Stop()
    {
        lock (_lock)
        {
            _active = false; _solid = false; _beepOn = false;
            _timer?.Dispose(); _timer = null;
            try { _gen?.Stop(); } catch { }
            _gen?.Dispose();
            _gen = null;
        }
    }

    public void Dispose() => Stop();
}
