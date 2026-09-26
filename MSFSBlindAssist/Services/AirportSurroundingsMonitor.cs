using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The opt-in "Passing Concourse B, on the left." callouts, plus the "Off the pavement" surface
/// callout that shares its samples. A UI-thread timer asks for the aircraft position every
/// <see cref="PollMs"/>, and every AIRCRAFT_POSITION answer (this monitor's or another feature's)
/// is judged in <see cref="OnPositionReceived"/>. The catalog comes from the cache without ever
/// building on the UI thread; speech is always queued.
///
/// <para>Two rules: never speak on a runway (<see cref="SuppressCheck"/> covers the taxi and
/// takeoff states, <see cref="RunwayProbe"/> the pavement itself), and never start background work
/// during a takeoff or rollout (<see cref="MayStartBuild"/>).</para>
/// </summary>
public sealed class AirportSurroundingsMonitor : IDisposable
{
    /// <summary>The monitor's own poll period — the sparsest cadence a sample can arrive at. The
    /// surface tests are pinned at this cadence.</summary>
    internal const int PollMs = 2000;
    private static readonly TimeSpan IcaoRefresh = TimeSpan.FromSeconds(30);

    /// <summary>How long before a warm-up that left the probe unable to answer (its database read
    /// threw) is retried.</summary>
    internal static readonly TimeSpan ProbeWarmRetry = TimeSpan.FromSeconds(60);

    /// <summary>A move this large between polls is a teleport, not taxiing: six times the 41 m a
    /// 40 kt aircraft covers in one poll. Tripping it only forgets tracks, never invents a
    /// callout.</summary>
    internal const double JumpMetres = 250.0;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SimConnectManager _sim;
    private readonly Func<IAirportDataProvider?> _provider;
    private readonly SurroundingsCatalogCache _cache;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PassingCalloutGate _gate = new();
    // The per-sample rules that need no sim (last position, jump test, both switches, surface gate).
    private readonly SurroundingsSampleTracker _samples = new();
    // Captured on the UI thread so callouts are posted back to it (see PostAnnounce).
    private readonly SynchronizationContext? _syncContext;

    private string _icao = "";
    private DateTime _icaoAt = DateTime.MinValue;
    // True while a CurrentAirport.Resolve runs on a pool thread (see RefreshIcao). UI thread only.
    private bool _icaoResolving;
    // Bumped by every Reset, so an airport lookup started before one publishes nothing.
    private int _resets;
    /// <summary>The runway probe's warm-up: which airport, when it started, the task (only ever
    /// polled), and whether its "cannot be answered" line was logged.</summary>
    private sealed record ProbeWarmUp(string Icao, DateTime StartedAt, Task Work, bool CannotAnswerLogged);
    private ProbeWarmUp? _probeWarmUp;
    private bool _resetWhileAirborne;
    private AirportFeatureCatalog? _lastCatalog;
    private bool _disposed;

    /// <summary>The passing callouts' switch. Turning it back on re-baselines the tracks (approaches
    /// recorded while off were not watched); what was already said is kept.</summary>
    public bool Enabled
    {
        get => _samples.PassingEnabled;
        set
        {
            if (!_samples.SetPassingEnabled(value)) return;
            _gate.RebaselineTracks();
            Log.Debug("Surroundings", "passing callouts switched on: tracks re-baselined");
        }
    }
    /// <summary>The surface callout's own switch, independent of <see cref="Enabled"/>. Turning it
    /// back on starts from a silent baseline.</summary>
    public bool SurfaceCalloutsEnabled
    {
        get => _samples.SurfaceEnabled;
        set
        {
            if (_samples.SetSurfaceEnabled(value))
                Log.Debug("Surroundings", "surface callout switched on: the next surface is a silent baseline");
        }
    }
    /// <summary>True while a feature (takeoff assist, docking, a taxi rollout/lineup/hold state,
    /// announcer suppression) requires silence.</summary>
    public Func<bool>? SuppressCheck { get; set; }
    /// <summary>True/false = on/off a runway; null = unknown (nothing read for this airport yet). Runs on the UI thread.</summary>
    public Func<string, double, double, bool?>? RunwayProbe { get; set; }
    /// <summary>Prepares the probe's warm-up on the UI thread, in the same turn that read the
    /// provider, and returns the work to run off it — so the database generation it captures always
    /// matches that provider.</summary>
    public Func<IAirportDataProvider, string, Action>? PrepareRunwayProbeWarmUp { get; set; }

    public AirportSurroundingsMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim, Func<IAirportDataProvider?> provider, SurroundingsCatalogCache cache)
    {
        _announcer = announcer; _sim = sim; _provider = provider; _cache = cache;
        _syncContext = SynchronizationContext.Current;
        // Raised only for the full position frame (the one carrying the surface fields), on the UI thread.
        _sim.AircraftPositionReceived += OnPositionReceived;
        _timer = new System.Windows.Forms.Timer { Interval = PollMs };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>Aircraft switch, reconnect, turnaround liftoff, database switch: forget what was
    /// seen, which airport this is, and that its runway probe was warmed.</summary>
    public void Reset()
    {
        _gate.Reset();
        // The switches are settings and survive; the next surface is a silent baseline.
        _samples.Reset();
        _icao = ""; _icaoAt = DateTime.MinValue;
        _resets++;
        // A running warm-up is let go, never waited on; it can no longer publish for another airport.
        _probeWarmUp = null;
        _lastCatalog = null;
    }

    /// <summary>May a background job (first catalog build, probe warm-up) start on this sample?
    /// Never while suppressed or faster than taxi speed — i.e. never on a takeoff or rollout.</summary>
    internal static bool MayStartBuild(bool suppressed, double groundSpeedKts)
        => !suppressed && groundSpeedKts <= PassingCalloutGate.MaxSpeedKts;

    /// <summary>Start a probe warm-up? Only when the probe could not answer on this sample, none is
    /// running, and this airport was not warmed within <see cref="ProbeWarmRetry"/>.</summary>
    internal static bool ShouldWarmProbe(bool probeAnswered, bool sameAirportAsLastWarm, TimeSpan sinceLastWarm, bool warmInFlight)
        => !probeAnswered
           && !warmInFlight
           && (!sameAirportAsLastWarm || sinceLastWarm >= ProbeWarmRetry);

    /// <summary>Did the aircraft move STRICTLY further than <see cref="JumpMetres"/> between two
    /// polls? A NaN position is never a jump.</summary>
    internal static bool IsPositionJump(double fromLat, double fromLon, double toLat, double toLon)
        => TaxiGeo.HaversineMeters(fromLat, fromLon, toLat, toLon) > JumpMetres;

    private void OnTick(object? sender, EventArgs e)
    {
        // The tick only asks; the answer is judged where it lands, so every callout uses a fresh
        // sample. With both switches off nothing is sampled.
        if (!_sim.IsConnected) return;
        if (!Enabled && !SurfaceCalloutsEnabled) return;
        _sim.RequestAircraftPosition();
    }

    /// <summary>
    /// One AIRCRAFT_POSITION answer, from this monitor's request or any other feature's. The whole
    /// body is guarded: this is the event's first subscriber, and a throw would starve every
    /// one-shot behind it (Alt+Y, Alt+L, the liftoff confirm).
    /// </summary>
    private void OnPositionReceived(object? sender, SimConnectManager.AircraftPosition p)
    {
        try
        {
            if (!Enabled && !SurfaceCalloutsEnabled) return;

            // This sample's own ground flag; not-a-number counts as airborne.
            if (!(p.SimOnGround >= 0.5))
            {
                // Once per airborne episode: the next landing's ranges are unrelated to the departure's.
                if (!_resetWhileAirborne) { Reset(); _resetWhileAirborne = true; }
                return;
            }
            _resetWhileAirborne = false;

            var sample = _samples.Sample(p.Latitude, p.Longitude, p.SurfaceType, p.SurfaceInfoValid, p.GroundSpeedKnots);
            if (!sample.Usable) return;

            // A teleport would make every closing range read as "now opening".
            if (sample.Jumped)
            {
                _gate.Reset();
                Log.Debug("Surroundings", "position jump: passing-callout tracks and surface baseline dropped");
            }

            // The surface callout needs no airport and is deliberately NOT behind SuppressCheck:
            // running off the side on a takeoff roll or rollout is the worst case.
            if (sample.SurfaceCallout is { } surfaceCall)
            {
                PostAnnounce(surfaceCall);
                Log.Debug("Surroundings", $"surface callout: {surfaceCall} (type={p.SurfaceType:F0} valid={p.SurfaceInfoValid:F0} gs={p.GroundSpeedKnots:F1})");
            }
            if (!Enabled) return;   // the rest belongs to the passing callouts

            // The first sample after a reset or pause is only recorded; there is nothing to measure from.
            if (sample.First) return;

            var provider = _provider();
            if (provider == null) return;

            var now = DateTime.UtcNow;
            if (now - _icaoAt > IcaoRefresh && !_icaoResolving)
            {
                _icaoAt = now;
                RefreshIcao(provider, p.Latitude, p.Longitude);
            }
            if (_icao.Length == 0) return;

            bool suppressed = SuppressCheck?.Invoke() == true || _announcer.Suppressed;

            // Never build here on the UI thread: ask the cache for a background build and pick the
            // catalog up on a later sample.
            if (!_cache.TryGetCached(_icao, out var catalog))
            {
                if (MayStartBuild(suppressed, p.GroundSpeedKnots))
                {
                    _ = _cache.GetAsync(_icao);
                    WarmProbeIfNeeded(provider, now, RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude) != null);
                }
                return;
            }
            if (catalog.Features.Count == 0) return;

            // A rebuilt catalog can change a feature's geometry, so tracks carried across it would see
            // a range step. Re-baseline the tracks; keep what was already said.
            if (!ReferenceEquals(catalog, _lastCatalog))
            {
                if (_lastCatalog != null) _gate.RebaselineTracks();
                _lastCatalog = catalog;
            }

            // A sample skipped here never reaches the gate, so its tracks would keep a closest point
            // from before the silence and arm on it afterwards (a building passed before a runway
            // crossing called out on the far side). Re-baseline them; keep what was already said.
            if (suppressed) { _gate.RebaselineTracks(); return; }

            // One probe read per sample serves both the warm-up decision and the runway silence.
            bool? onRunway = RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude);

            if (MayStartBuild(suppressed, p.GroundSpeedKnots)) WarmProbeIfNeeded(provider, now, onRunway != null);

            // A runway is never where a building callout belongs.
            if (onRunway == true) { _gate.RebaselineTracks(); return; }

            double hdgTrue = RelativeDirection.Normalize360(p.HeadingMagnetic + p.MagneticVariation);
            // RankRadiusMetres, never a literal: the gate tracks a feature from this window's edge.
            var ranked = SurroundingsReport.Rank(catalog, p.Latitude, p.Longitude, hdgTrue, PassingCalloutGate.RankRadiusMetres);

            var hit = _gate.Evaluate(ranked, p.GroundSpeedKnots, now);
            if (hit == null) return;
            string phrase = $"Passing {hit.Feature.SpokenName}, {RelativeDirection.Side(hit.RelativeBearingDeg)}.";
            PostAnnounce(phrase);
            Log.Debug("Surroundings", $"callout {_icao}: {phrase} dist={hit.DistanceMetres:F0} rel={hit.RelativeBearingDeg:F0}");
        }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"monitor sample failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Which airport the aircraft is at: <see cref="CurrentAirport.Resolve"/> is a database query,
    /// so it runs on a pool thread — never inside this UI-thread handler, which runs ahead of the
    /// Alt+Y and Alt+L one-shots on every sample — and its answer is adopted back on the UI thread.
    /// An answer from before a <see cref="Reset"/> (airborne, reconnect, aircraft or database
    /// switch) is dropped. With no context to post to (not the UI thread) it resolves inline.
    /// </summary>
    private void RefreshIcao(IAirportDataProvider provider, double lat, double lon)
    {
        var context = _syncContext;
        if (context == null) { AdoptIcao(CurrentAirport.Resolve(provider, lat, lon) ?? ""); return; }

        _icaoResolving = true;
        int resets = _resets;
        _ = Task.Run(() =>
        {
            string? next = null;
            try { next = CurrentAirport.Resolve(provider, lat, lon) ?? ""; }
            catch (Exception ex) { Log.Warn("Surroundings", $"current-airport lookup failed: {ex.Message}"); }
            try
            {
                context.Post(_ =>
                {
                    _icaoResolving = false;
                    if (_disposed || resets != _resets || next == null) return;
                    AdoptIcao(next);
                }, null);
            }
            catch (InvalidOperationException ex)
            {
                Log.Debug("Surroundings", $"could not post the airport lookup, context unavailable: {ex.Message}");
            }
        });
    }

    /// <summary>A different airport forgets the old one's tracks and catalog.</summary>
    private void AdoptIcao(string next)
    {
        if (string.Equals(next, _icao, StringComparison.OrdinalIgnoreCase)) return;
        _icao = next; _gate.Reset(); _lastCatalog = null;
    }

    /// <summary>
    /// Queues a callout by POSTING it to the UI thread rather than speaking inside the position
    /// handler: a one-shot readout behind this handler in the same event (Where Am I, Look Around)
    /// may interrupt, and would otherwise cancel the callout within milliseconds. Speaks nothing
    /// after disposal.
    /// </summary>
    private void PostAnnounce(string phrase)
    {
        if (_syncContext == null) { _announcer.Announce(phrase); return; }
        try
        {
            _syncContext.Post(_ =>
            {
                if (_disposed) return;
                _announcer.Announce(phrase);
            }, null);
        }
        catch (InvalidOperationException ex)
        {
            Log.Debug("Surroundings", $"could not post callout, context unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts the runway probe's warm-up on a pool thread when <see cref="ShouldWarmProbe"/> says
    /// so. <paramref name="probeAnswered"/> is the sample's own single probe read; the work is
    /// prepared here, in the turn that read <paramref name="provider"/>.
    /// </summary>
    private void WarmProbeIfNeeded(IAirportDataProvider provider, DateTime now, bool probeAnswered)
    {
        var prepare = PrepareRunwayProbeWarmUp;
        if (prepare == null) return;

        // A warm-up for another airport is not counted as in flight here.
        var last = _probeWarmUp;
        bool sameAirport = last != null && string.Equals(last.Icao, _icao, StringComparison.OrdinalIgnoreCase);
        TimeSpan sinceLast = sameAirport ? now - last!.StartedAt : TimeSpan.MaxValue;
        bool inFlight = sameAirport && !last!.Work.IsCompleted;
        if (!ShouldWarmProbe(probeAnswered, sameAirport, sinceLast, inFlight)) return;

        // Reached for the same airport only on a retry after a failed read; logged once.
        bool logged = sameAirport && last!.CannotAnswerLogged;
        if (sameAirport && !logged)
        {
            logged = true;
            Log.Debug("Surroundings", $"runway probe: {_icao} still cannot be answered after a warm-up — its runway read failed; retrying at most once a minute");
        }

        var work = prepare(provider, _icao);
        var task = Task.Run(() => { try { work(); } catch (Exception ex) { Log.Warn("Surroundings", $"runway probe warm-up failed: {ex.Message}"); } });
        _probeWarmUp = new ProbeWarmUp(_icao, now, task, logged);
    }

    public void Dispose()
    {
        _disposed = true;
        _sim.AircraftPositionReceived -= OnPositionReceived;
        _timer.Stop();
        _timer.Dispose();
    }
}
