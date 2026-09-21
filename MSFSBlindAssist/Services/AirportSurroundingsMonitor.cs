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
/// of them idle, so <see cref="RunwayProbe"/> asks the pavement itself. And it must not START
/// BACKGROUND WORK during a rollout (the first tick on the ground lands about 2 s after
/// touchdown) — <see cref="MayStartBuild"/> holds off BOTH jobs a tick can start, the first-time
/// catalog build and the probe's own graph warm-up, until a quiet moment.</para>
/// </summary>
public sealed class AirportSurroundingsMonitor : IDisposable
{
    private const int PollMs = 2000;
    private static readonly TimeSpan IcaoRefresh = TimeSpan.FromSeconds(30);

    /// <summary>How long a warm-up that left the probe unable to answer is believed before another
    /// is allowed. Once-and-never-again is the wrong memory for a warm that THREW (a transient
    /// database read) or found no taxi data to build from: the probe would then answer null for
    /// that airport for the whole session, silently switching the runway silence off. Same length
    /// as SurroundingsCatalogCache.FailureMemory, and for the same reason — long enough that a 2 s
    /// poll cannot hammer a broken build, short enough that the session recovers.</summary>
    internal static readonly TimeSpan ProbeWarmRetry = TimeSpan.FromSeconds(60);

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
    private DateTime _probeWarmedAt = DateTime.MinValue;
    private bool _probeNoGraphLogged;
    private Task? _probeWarm;
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
    /// <summary>Warms whatever RunwayProbe reads, off the UI thread, once per airport (retried at
    /// <see cref="ProbeWarmRetry"/> while the probe still cannot answer).</summary>
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
        // A warm-up still running is let go, never waited on: it observes its own exception and
        // its only effect is a graph cached for an airport we have just stopped tracking.
        _probeWarmedIcao = ""; _probeWarmedAt = DateTime.MinValue; _probeNoGraphLogged = false; _probeWarm = null;
        _lastSeen = null;
    }

    /// <summary>
    /// May a BACKGROUND JOB start on this tick? One policy, both of the jobs a tick can start: the
    /// first-time catalog build (the GSX list, possibly a scenery-package index) and the runway
    /// probe's graph warm-up. Never during a takeoff, a rollout (the first tick on the ground is
    /// ~2 s after touchdown), a lineup, a hold or docking.
    /// </summary>
    internal static bool MayStartBuild(bool suppressed, double groundSpeedKts)
        => !suppressed && groundSpeedKts <= PassingCalloutGate.MaxSpeedKts;

    /// <summary>
    /// Should a warm-up start on this tick? PURE, because the caller's state machine is where the
    /// retry went wrong once: true only when the probe could NOT answer on this tick (there is
    /// nothing to prepare when it already can), no warm-up is still running, and either this
    /// airport is not the one last warmed or <see cref="ProbeWarmRetry"/> has passed since that
    /// warm STARTED. Nothing here reads the probe — the caller hands it the tick's own single
    /// read.
    /// </summary>
    internal static bool ShouldWarmProbe(bool probeAnswered, bool sameAirportAsLastWarm, TimeSpan sinceLastWarm, bool warmInFlight)
        => !probeAnswered
           && !warmInFlight
           && (!sameAirportAsLastWarm || sinceLastWarm >= ProbeWarmRetry);

    /// <summary>
    /// Did the aircraft move further between two polls than taxiing could account for — i.e. was it
    /// put somewhere rather than driven there? STRICTLY further than <see cref="JumpMetres"/> is a
    /// jump; the threshold itself is not. (The tests pin where that threshold SITS, to within a
    /// millimetre either side, not which way the comparison falls exactly on it: converting metres
    /// to degrees and back through the haversine's own Asin(Sin(...)) cannot land a test input on
    /// the exact double, so the operator at that one point is deliberately untested — and no real
    /// pair of positions can reach it either.)
    ///
    /// <para>A NaN in either position is NEVER a jump (every comparison against NaN is false): an
    /// unreadable sample is not evidence that the aircraft moved, and keeping the tracks is the safe
    /// direction — the same NaN reaches the gate as a NaN range, where no pass can arm either.
    /// (0,0) is taken at face value as the real coordinate it is, so a step to or from null island
    /// reads as a teleport and drops the tracks; also the safe direction, since a reset can only
    /// lose a callout, never invent one.</para>
    /// </summary>
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
                // The probe is read here at most ONCE, and only on a tick that may actually start
                // something — a suppressed or fast tick takes neither the lock nor the disk.
                if (MayStartBuild(suppressed, p.GroundSpeedKnots))
                {
                    _ = _cache.GetAsync(_icao);
                    WarmProbeIfNeeded(provider, p, now, RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude) != null);
                }
                return;
            }
            if (catalog == null || catalog.Features.Count == 0) return;
            if (suppressed) return;

            // ONE probe read per tick, serving BOTH things that need it: the warm-up decision
            // below and the silence under it. The read takes TaxiGuidanceManager._stateLock, which
            // is the lock a warm-up itself holds while it builds, so a second read on the same
            // tick is a second chance to wait on it — and that is exactly how the retry, when it
            // read the probe for itself, quietly went from once a minute to every tick.
            bool? onRunway = RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude);

            // ONE quiet-moment policy, BOTH background jobs — and the warm-up is the HEAVIER of
            // the two: it holds that same _stateLock across the taxi paths, the parking list, the
            // runway rows and the graph build. Ungated, the first ground tick after a touchdown
            // started that build at ~120 kt and the next tick blocked the UI thread — the
            // SimConnect pump, the queued announcer and the hotkeys — for the length of it, during
            // the rollout. Waiting costs at most a late FIRST callout: the airborne Reset() emptied
            // the tracks, so the gate needs two fed ticks and 15 m of closing before it can arm,
            // and a build started at the first quiet tick normally lands inside that window.
            if (MayStartBuild(suppressed, p.GroundSpeedKnots)) WarmProbeIfNeeded(provider, p, now, onRunway != null);

            // Takeoff Assist and the taxi states cannot see a takeoff flown without the assist or
            // a landing without an exit plan. A runway is never where a building callout belongs.
            if (onRunway == true) return;

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
    /// Builds whatever <see cref="RunwayProbe"/> reads for this airport on a thread-pool thread —
    /// the probe itself may not build anything, and the graph it wants can take a noticeable moment
    /// to assemble. Once per airport, then at most once per <see cref="ProbeWarmRetry"/> while the
    /// probe still cannot answer (a warm that threw, or an airport whose taxi data would not build,
    /// caches no graph, and a stamp alone would leave the runway silence off there for the whole
    /// session), and never twice at once.
    ///
    /// <para>This method NEVER reads the probe: <paramref name="probeAnswered"/> is the tick's own
    /// single read, and <see cref="ShouldWarmProbe"/> decides from state alone. The provider is
    /// captured on the UI thread, so a database switch cannot swap it out from under a warm-up in
    /// flight, and the task is only ever polled for completion — never waited on.</para>
    /// </summary>
    private void WarmProbeIfNeeded(IAirportDataProvider provider, SimConnectManager.AircraftPosition p, DateTime now, bool probeAnswered)
    {
        if (WarmRunwayProbe == null) return;

        bool sameAirport = string.Equals(_probeWarmedIcao, _icao, StringComparison.OrdinalIgnoreCase);
        // A warm-up for a DIFFERENT airport is no longer this airport's business — let it finish
        // on its own and stop counting it as in flight, or the first warm here waits out a build
        // whose result this airport will never read.
        if (!sameAirport) { _probeWarm = null; _probeNoGraphLogged = false; }

        if (!ShouldWarmProbe(probeAnswered, sameAirport, now - _probeWarmedAt, _probeWarm is { IsCompleted: false })) return;

        // Reached with sameAirport only on a retry, i.e. a warm-up that left the probe unable to
        // answer. Said once per airport: the no-data case leaves no other trace at all, and
        // repeating it every minute would bury the line that matters.
        if (sameAirport && !_probeNoGraphLogged)
        {
            _probeNoGraphLogged = true;
            Log.Debug("Surroundings", $"runway probe: no graph for {_icao} after a warm-up (no taxi data, or the build failed); retrying at most once a minute");
        }

        _probeWarmedIcao = _icao; _probeWarmedAt = now;
        string icao = _icao; double lat = p.Latitude, lon = p.Longitude; var warm = WarmRunwayProbe;
        _probeWarm = Task.Run(() => { try { warm(provider, icao, lat, lon); } catch (Exception ex) { Log.Warn("Surroundings", $"runway probe warm-up failed: {ex.Message}"); } });
    }

    public void Dispose() { _timer.Stop(); _timer.Dispose(); }
}
