using NAudio.Wave;

namespace MSFSBlindAssist.Services;

/// <summary>
/// "Step on the ball" rudder-coordination cue: a HARD-PANNED white-noise tick. When the inclinometer
/// ball is out of centre, a tick plays entirely in the ear on the side of the rudder to press — ball
/// left → left ear → left rudder; ball right → right ear → right rudder. The tick repetition rate
/// rises the further the ball is out; when coordinated (within the deadband) or inactive it is silent.
/// Nothing else: no pitch, no proportional pan, no speech — a pure hard-panned tick.
///
/// Self-contained NAudio (its own WaveOut + a stereo white-noise provider with a sample-accurate
/// on/off rhythm), modelled on <see cref="ProximityBeeper"/>. Never throws to the caller.
/// </summary>
public sealed class SlipCueGenerator : IDisposable
{
    private const int SampleRate = 44100;
    private const int TickOnMs = 45;                 // length of each white-noise burst
    private static readonly int OnSamples = SampleRate * TickOnMs / 1000;

    /// <summary>Stereo white-noise provider. Emits noise into ONE channel (hard pan) during the tick
    /// "on" window, silence otherwise; the on/off rhythm is counted in samples so it's exact.</summary>
    private sealed class NoiseTickProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
        private readonly Random _rng = new();
        private readonly object _l = new();
        private int _side;            // -1 = left, +1 = right, 0 = silent
        private double _vol;
        private int _onSamples = OnSamples;
        private int _periodSamples = OnSamples * 4;
        private int _pos;

        public void Set(int side, double vol, int onSamples, int periodSamples)
        {
            lock (_l)
            {
                _side = side;
                _vol = vol;
                _onSamples = onSamples;
                _periodSamples = System.Math.Max(periodSamples, onSamples + 1);
                if (_pos >= _periodSamples) _pos = 0;
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_l)
            {
                // Step by stereo frame; guard the +1 so an odd count never writes past the buffer.
                for (int i = 0; i + 1 < count; i += 2)
                {
                    float s = 0f;
                    if (_side != 0 && _vol > 0.0)
                    {
                        if (_pos < _onSamples)
                            s = (float)((_rng.NextDouble() * 2.0 - 1.0) * _vol);
                        if (++_pos >= _periodSamples) _pos = 0;
                    }
                    buffer[offset + i] = _side < 0 ? s : 0f;       // left channel
                    buffer[offset + i + 1] = _side > 0 ? s : 0f;   // right channel
                }
            }
            return count;
        }
    }

    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private NoiseTickProvider? _provider;
    private double _volume = 0.2;

    public bool IsRunning { get { lock (_lock) { return _waveOut != null; } } }

    public void Start(double volume)
    {
        lock (_lock)
        {
            _volume = volume;
            if (_waveOut != null) return;
            try
            {
                _provider = new NoiseTickProvider();
                _provider.Set(0, _volume, OnSamples, OnSamples * 4);   // start silent
                _waveOut = new WaveOutEvent { NumberOfBuffers = 2, DesiredLatency = 100 };
                _waveOut.Init(_provider);
                _waveOut.Play();
            }
            catch
            {
                Cleanup();   // audio is optional feedback
            }
        }
    }

    /// <summary>
    /// Drive the cue each frame.
    /// </summary>
    /// <param name="ball">Signed coordination value: positive = ball to the RIGHT (press right
    ///   rudder), negative = left. Normalised so |ball| ~ 1.0 is fully deflected.</param>
    /// <param name="deadband">|ball| at or below which the aircraft is "coordinated" → silent.</param>
    /// <param name="fullScale">|ball| at which the tick reaches its fastest rate.</param>
    /// <param name="active">Master gate; false → silent.</param>
    public void Update(double ball, double deadband, double fullScale, bool active)
    {
        NoiseTickProvider? p;
        double vol;
        lock (_lock) { p = _provider; vol = _volume; }
        if (p == null) return;

        if (!active || System.Math.Abs(ball) <= deadband)
        {
            p.Set(0, vol, OnSamples, OnSamples * 4);   // silent
            return;
        }

        int side = ball > 0 ? 1 : -1;                  // ball side = rudder to press
        double span = System.Math.Max(0.0001, fullScale - deadband);
        double mag = System.Math.Min(1.0, (System.Math.Abs(ball) - deadband) / span);
        // Just out of the deadband → slow ticks (~420 ms apart); fully out → fast (~120 ms).
        int periodMs = (int)(420.0 - mag * 300.0);
        int period = System.Math.Max(OnSamples + 1, SampleRate * periodMs / 1000);
        p.Set(side, vol, OnSamples, period);
    }

    public void Stop()
    {
        lock (_lock) { Cleanup(); }
    }

    private void Cleanup()
    {
        try { _waveOut?.Stop(); _waveOut?.Dispose(); } catch { }
        _waveOut = null;
        _provider = null;
    }

    public void Dispose() => Stop();
}
