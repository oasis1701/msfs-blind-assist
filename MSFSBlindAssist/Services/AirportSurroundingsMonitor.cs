using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Opt-in "Passing Concourse B, on the left." Polls own position every 2 s on the UI-thread
/// timer (same shape as GroundTrafficMonitor — the taxi position stream is taxi-scoped and
/// is OFF when no route is loaded, so this cannot ride it). Resolves the airport at most every
/// 30 s, reads the catalog from the cache via the non-building TryGetCached (a first build for
/// an airport is kicked off on a thread-pool thread and picked up on a later tick — never built
/// synchronously on this UI-thread timer), and hands the ranked list to the pure gate. Queued
/// speech only.
///
/// <para>Two things a tick must never do, both of which cost a pilot at the worst moment.
/// It must not SPEAK on a runway: <see cref="SuppressCheck"/> reads Takeoff Assist and the taxi
/// states, and a takeoff flown without the assist or a landing without an exit plan leaves all
/// of them idle, so <see cref="RunwayProbe"/> asks the pavement itself. And it must not START a
/// first-time catalog build during a rollout (the first tick on the ground lands about 2 s after
/// touchdown) — <see cref="MayStartBuild"/> holds that off until a quiet moment.</para>
/// </summary>
public sealed class AirportSurroundingsMonitor : IDisposable
{
    private const int PollMs = 2000;
    private static readonly TimeSpan IcaoRefresh = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Further than any taxiing aircraft can travel between two polls, so only a teleport (the
    /// gate-teleport dialog, slew, a flight reload) trips it: the gate's own ceiling is
    /// PassingCalloutGate.MaxSpeedKts — 40 kt, 20.6 m/s — over a <see cref="PollMs"/> poll, about
    /// 41 m. Six times that leaves a fast landing rollout (140 kt ≈ 145 m per poll) and a late
    /// timer tick comfortably under it, and the only cost of tripping it anyway is a forgotten
    /// track, never a wrong callout.
    /// </summary>
    internal const double JumpMetres = 250.0;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Func<IAirportDataProvider?> _provider;
    private readonly SurroundingsCatalogCache _cache;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PassingCalloutGate _gate = new();

    private string _icao = "";
    private DateTime _icaoAt = DateTime.MinValue;
    private string _probeWarmedIcao = "";
    private (double Lat, double Lon)? _lastSeen;
    private bool _resetWhileAirborne;

    public bool Enabled { get; set; }
    /// <summary>True while callouts must stay silent because a FEATURE says so — takeoff assist,
    /// docking, the taxi rollout/lineup/hold states, announcer suppressed. None of those is on
    /// during a takeoff or a landing flown without them; the runway itself is
    /// <see cref="RunwayProbe"/>'s job.</summary>
    public Func<bool>? SuppressCheck { get; set; }
    /// <summary>True/false = on/off a runway; null = unknown (no graph yet). Runs on the UI thread.</summary>
    public Func<string, double, double, bool?>? RunwayProbe { get; set; }
    /// <summary>Warms whatever RunwayProbe reads, off the UI thread, once per airport.</summary>
    public Action<IAirportDataProvider, string, double, double>? WarmRunwayProbe { get; set; }

    public AirportSurroundingsMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim, Func<IAirportDataProvider?> provider, SurroundingsCatalogCache cache)
    {
        _announcer = announcer; _sim = sim; _provider = provider; _cache = cache;
        _timer = new System.Windows.Forms.Timer { Interval = PollMs };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Aircraft switch, reconnect, turnaround liftoff, database switch: forget what was
    /// seen, which airport this is, and that its runway probe was warmed.</summary>
    public void Reset()
    {
        _gate.Reset();
        _icao = ""; _icaoAt = DateTime.MinValue;
        _probeWarmedIcao = ""; _lastSeen = null;
    }

    /// <summary>A first-time build reads the GSX list and may index a scenery package. Never start
    /// one during a takeoff, a rollout (the first tick on the ground is ~2 s after touchdown), a
    /// lineup, a hold or docking.</summary>
    internal static bool MayStartBuild(bool suppressed, double groundSpeedKts)
        => !suppressed && groundSpeedKts <= PassingCalloutGate.MaxSpeedKts;

    /// <summary>Did the aircraft move further between two polls than taxiing could account for —
    /// i.e. was it put somewhere rather than driven there? See <see cref="JumpMetres"/>.</summary>
    internal static bool IsPositionJump(double fromLat, double fromLon, double toLat, double toLon)
        => TaxiGeo.HaversineMeters(fromLat, fromLon, toLat, toLon) > JumpMetres;

    private void OnTick(object? sender, EventArgs e)
    {
        if (!Enabled || !_sim.IsConnected) return;
        if (_sim.LastKnownOnGround != true)
        {
            // The next landing's ranges have nothing to do with the departure's, and a building
            // whose range was still closing at rotation would read as "now opening" on the
            // rollout. Once per airborne episode — repeating it for a whole cruise buys nothing.
            if (!_resetWhileAirborne) { Reset(); _resetWhileAirborne = true; }
            return;
        }
        _resetWhileAirborne = false;

        _sim.RequestAircraftPosition();
        var pos = _sim.LastKnownPosition;
        if (pos == null) return;
        var p = pos.Value;

        // A teleport moves every tracked building by hundreds of metres in one poll, so a range
        // that had been closing reads as "now opening" — a false "Passing X" at the moment of the
        // teleport. The tracks describe a continuous drive; they do not survive being moved.
        if (_lastSeen is { } last && IsPositionJump(last.Lat, last.Lon, p.Latitude, p.Longitude))
        {
            // No ICAO in the line: the resolve that names it runs below, and a jump can land here
            // before this airport has ever been named.
            _gate.Reset();
            Log.Debug("Surroundings", "position jump: passing-callout tracks dropped");
        }
        _lastSeen = (p.Latitude, p.Longitude);

        var provider = _provider();
        if (provider == null) return;

        try
        {
            var now = DateTime.UtcNow;
            if (now - _icaoAt > IcaoRefresh)
            {
                _icaoAt = now;
                // The same resolver both hotkeys use — the airport whose box the aircraft is in,
                // never the nearest reference point (which is a heliport at a third of the
                // stands at some hubs), and short idents included.
                string next = CurrentAirport.Resolve(provider, p.Latitude, p.Longitude) ?? "";
                if (!string.Equals(next, _icao, StringComparison.OrdinalIgnoreCase)) { _icao = next; _gate.Reset(); }
            }
            if (_icao.Length == 0) return;

            bool suppressed = SuppressCheck?.Invoke() == true || _announcer.Suppressed;

            // Never build synchronously on this UI-thread timer tick — a first-time scenery
            // scan/DB read would stall the whole message pump. TryGetCached is a lock-only read;
            // when nothing usable is cached yet (or it's stale), ask for the background build and
            // evaluate callouts on a later tick once it lands in the cache. No in-flight guard of
            // our own: GetAsync is single-flight per ICAO and remembers a failed build, so this
            // 2 s poll can neither stack builds nor hammer a broken one — but even a background
            // build competes for the disk and the database at a moment the pilot is busy, so it
            // waits for one the pilot is not.
            if (!_cache.TryGetCached(_icao, out var catalog))
            {
                if (MayStartBuild(suppressed, p.GroundSpeedKnots)) { _ = _cache.GetAsync(_icao); WarmProbeOnce(provider, p); }
                return;
            }
            if (catalog == null || catalog.Features.Count == 0) return;

            // Not gated on suppression: the probe's graph is exactly what silences the rest of a
            // takeoff roll, so the roll is no time to discover it was never built.
            WarmProbeOnce(provider, p);
            if (suppressed) return;
            // Takeoff Assist and the taxi states cannot see a takeoff flown without the assist or
            // a landing without an exit plan. A runway is never where a building callout belongs.
            if (RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude) == true) return;

            double hdgTrue = RelativeDirection.Normalize360(p.HeadingMagnetic + p.MagneticVariation);
            var ranked = SurroundingsReport.Rank(catalog, p.Latitude, p.Longitude, hdgTrue, 250.0);

            var hit = _gate.Evaluate(ranked, p.GroundSpeedKnots, now);
            if (hit == null) return;
            string phrase = $"Passing {hit.Feature.SpokenName}, {RelativeDirection.Side(hit.RelativeBearingDeg)}.";
            _announcer.Announce(phrase);
            Log.Debug("Surroundings", $"callout {_icao}: {phrase} dist={hit.DistanceMetres:F0} rel={hit.RelativeBearingDeg:F0}");
        }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"monitor tick failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds whatever <see cref="RunwayProbe"/> reads for this airport, ONCE, on a thread-pool
    /// thread — the probe itself may not build anything, and the graph it wants can take a
    /// noticeable moment to assemble. The provider is captured on the UI thread, so a database
    /// switch cannot swap it out from under the warm-up in flight.
    /// </summary>
    private void WarmProbeOnce(IAirportDataProvider provider, SimConnectManager.AircraftPosition p)
    {
        if (WarmRunwayProbe == null || string.Equals(_probeWarmedIcao, _icao, StringComparison.OrdinalIgnoreCase)) return;
        _probeWarmedIcao = _icao;
        string icao = _icao; double lat = p.Latitude, lon = p.Longitude; var warm = WarmRunwayProbe;
        Task.Run(() => { try { warm(provider, icao, lat, lon); } catch (Exception ex) { Log.Warn("Surroundings", $"runway probe warm-up failed: {ex.Message}"); } });
    }

    public void Dispose() { _timer.Stop(); _timer.Dispose(); }
}
