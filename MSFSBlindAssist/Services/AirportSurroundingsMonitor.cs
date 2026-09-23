using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Opt-in "Passing Concourse B, on the left." — and the "Off the pavement" surface callout, which
/// shares its samples. Asks for its own position every 2 s on a UI-thread timer (same shape as
/// GroundTrafficMonitor — the taxi position stream is taxi-scoped and is OFF when no route is
/// loaded, so this cannot ride it) and judges every AIRCRAFT_POSITION answer where it lands
/// (<see cref="OnPositionReceived"/>): its own request's and any other feature made, each a
/// fresh sample whose surface, position and ground flag belong together. Resolves the airport at
/// most every 30 s, reads the catalog from the cache via the non-building TryGetCached (a first
/// build for an airport is kicked off on a thread-pool thread and picked up on a later sample —
/// never built synchronously on the UI thread), and hands the ranked list to the pure gate.
/// Queued speech only.
///
/// <para>Two things a sample must never do, both of which cost a pilot at the worst moment.
/// It must not SPEAK on a runway: <see cref="SuppressCheck"/> reads Takeoff Assist and the taxi
/// states, and a takeoff flown without the assist or a landing without an exit plan leaves all
/// of them idle, so <see cref="RunwayProbe"/> asks the pavement itself. And it must not START
/// BACKGROUND WORK during a rollout (the first sample on the ground lands within about 2 s of
/// touchdown) — <see cref="MayStartBuild"/> holds off BOTH jobs a sample can start, the first-time
/// catalog build and the probe's own runway-row warm-up, until a quiet moment.</para>
/// </summary>
public sealed class AirportSurroundingsMonitor : IDisposable
{
    /// <summary>The monitor's own poll period — the SPARSEST cadence a sample can arrive at, since
    /// ground traffic, TCAS and hotkey one-shots can all deliver an <c>AIRCRAFT_POSITION</c> answer
    /// sooner. Internal so the surface tests pin that sparsest cadence: the distance a sample carries
    /// is AT LEAST this period times the ground speed, and because the rule is distance-based, a
    /// denser real sample only makes it MORE exact — a test fed a WIDER distance cannot see the rule
    /// that matters (PR #230 review, SC-2).</summary>
    internal const int PollMs = 2000;
    private static readonly TimeSpan IcaoRefresh = TimeSpan.FromSeconds(30);

    /// <summary>How long a warm-up that left the probe unable to answer is believed before another
    /// is allowed. Once-and-never-again is the wrong memory for a warm that THREW (a transient
    /// database read): the probe would then answer null for that airport for the whole session,
    /// silently switching the runway silence off. (An airport with no taxi data, or no runways, is
    /// no longer such a case — the warm-up reads the runway rows alone, and an empty list answers
    /// "not on a runway".) Same length as SurroundingsCatalogCache.FailureMemory, and for the same
    /// reason — long enough that a 2 s poll cannot hammer a broken read, short enough that the
    /// session recovers.</summary>
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
    // Everything per-sample that needs no sim — the last position, the jump test, the
    // unreadable-sample guard, both switches and the surface gate — pure, and pinned at this
    // monitor's own PollMs cadence (the SPARSEST a sample can arrive at) in
    // SurroundingsSampleTrackerTests.
    private readonly SurroundingsSampleTracker _samples = new();
    // Captured on the UI thread at construction so a callout can be POSTED back to it rather than
    // spoken synchronously inside OnPositionReceived — see PostAnnounce.
    private readonly SynchronizationContext? _syncContext;

    private string _icao = "";
    private DateTime _icaoAt = DateTime.MinValue;
    /// <summary>
    /// The runway probe's warm-up as ONE value: the airport it was last started for, when, its task
    /// (polled for completion, never waited on), and whether the "still cannot be answered" line has
    /// been written for that airport. They were four fields that every reset and every airport change
    /// had to clear together.
    /// </summary>
    private sealed record ProbeWarmUp(string Icao, DateTime StartedAt, Task Work, bool CannotAnswerLogged);
    private ProbeWarmUp? _probeWarmUp;
    private bool _resetWhileAirborne;
    private AirportFeatureCatalog? _lastCatalog;
    private bool _disposed;

    /// <summary>
    /// The passing callouts' opt-in. Turning it back ON re-baselines the passing tracks: an approach
    /// recorded before it went off was not watched while it was off, and read against the next
    /// sample it could arm a pass nobody saw. What the pilot was already told (the fired memory and
    /// the global gap) survives, exactly as on a catalog swap. Assigning the value it already has —
    /// the settings dialog assigns both switches on every OK — changes nothing.
    /// </summary>
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
    /// <summary>
    /// "Off the pavement, on grass." — INDEPENDENT of <see cref="Enabled"/>, which is the opt-in
    /// for the passing callouts. Those are a convenience; this one tells a pilot who cannot see
    /// the taxiway edge that they have left it, so it is not something to bury behind the same
    /// switch. It has its own setting. Turning it back ON starts from a silent baseline: whatever
    /// was driven while it was off was never watched, so it is not news
    /// (<see cref="SurroundingsSampleTracker.SetSurfaceEnabled"/>).
    /// </summary>
    public bool SurfaceCalloutsEnabled
    {
        get => _samples.SurfaceEnabled;
        set
        {
            if (_samples.SetSurfaceEnabled(value))
                Log.Debug("Surroundings", "surface callout switched on: the next surface is a silent baseline");
        }
    }
    /// <summary>True while callouts must stay silent because a FEATURE says so — takeoff assist,
    /// docking, the taxi rollout/lineup/hold states, announcer suppressed. None of those is on
    /// during a takeoff or a landing flown without them; the runway itself is
    /// <see cref="RunwayProbe"/>'s job.</summary>
    public Func<bool>? SuppressCheck { get; set; }
    /// <summary>True/false = on/off a runway; null = unknown (nothing read for this airport yet). Runs on the UI thread.</summary>
    public Func<string, double, double, bool?>? RunwayProbe { get; set; }
    /// <summary>
    /// PREPARES the warm-up for whatever RunwayProbe reads: called on the UI thread, in the same
    /// turn that read the provider, and returns the work to run off it (once per airport, retried at
    /// <see cref="ProbeWarmRetry"/> while the probe still cannot answer). Two steps so that whatever
    /// the work must know about THIS moment — the database generation that goes with the provider —
    /// is captured here rather than on the pool thread, where a database switch could fall in
    /// between and file the previous database's runways under the new one.
    /// </summary>
    public Func<IAirportDataProvider, string, Action>? PrepareRunwayProbeWarmUp { get; set; }

    public AirportSurroundingsMonitor(ScreenReaderAnnouncer announcer, SimConnectManager sim, Func<IAirportDataProvider?> provider, SurroundingsCatalogCache cache)
    {
        _announcer = announcer; _sim = sim; _provider = provider; _cache = cache;
        // Captured here, on the UI thread this monitor is always constructed on, so a callout can
        // be posted back to this same thread instead of spoken inline — see PostAnnounce.
        _syncContext = SynchronizationContext.Current;
        // Every AIRCRAFT_POSITION answer is judged where it lands — this monitor's own 2 s request
        // and any other feature made — so the surface, the position and the ground flag it acts
        // on are one sample. Raised only by ProcessAircraftPosition (the case-4 frame, the only one
        // carrying the surface fields), on the UI thread (WndProc dispatch).
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
        // A reconnect, an aircraft switch, a database switch or a turnaround liftoff: the pilot
        // has not driven off anything, so the next surface is a silent baseline, and the next
        // position is measured from nothing. The two switches are settings and survive.
        _samples.Reset();
        _icao = ""; _icaoAt = DateTime.MinValue;
        // A warm-up still running is let go, never waited on: it observes its own exception, and
        // it stores nothing once the probe has been asked about another airport (the manager keeps
        // a warm-up's shapes only for the airport the probe is being asked about) or after a
        // database switch (its generation is stale before it can publish). At most, until the next
        // probe read, it can still store a memo for the airport we were tracking, which answers for
        // no other airport.
        _probeWarmUp = null;
        _lastCatalog = null;
    }

    /// <summary>
    /// May a BACKGROUND JOB start on this position sample? One policy, both of the jobs a sample
    /// can start: the first-time catalog build (the GSX list, possibly a scenery-package index) and
    /// the runway probe's warm-up. Never during a takeoff, a rollout (the first sample on the
    /// ground lands within about 2 s of touchdown), a lineup, a hold or docking.
    /// </summary>
    internal static bool MayStartBuild(bool suppressed, double groundSpeedKts)
        => !suppressed && groundSpeedKts <= PassingCalloutGate.MaxSpeedKts;

    /// <summary>
    /// Should a warm-up start on this position sample? PURE, because the caller's state machine is
    /// where the retry went wrong once: true only when the probe could NOT answer on this sample
    /// (there is nothing to prepare when it already can), no warm-up is still running, and either
    /// this airport is not the one last warmed or <see cref="ProbeWarmRetry"/> has passed since that
    /// warm STARTED. Nothing here reads the probe — the caller hands it the sample's own single
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
    /// direction. The monitor itself never asks with one — <see cref="SurroundingsSampleTracker"/>
    /// skips a non-finite position before this test — so this is the rule for any other caller.
    /// (0,0) is taken at face value as the real coordinate it is, so a step to or from null island
    /// reads as a teleport and drops the tracks; also the safe direction, since a reset can only
    /// lose a callout, never invent one.</para>
    /// </summary>
    internal static bool IsPositionJump(double fromLat, double fromLon, double toLat, double toLon)
        => TaxiGeo.HaversineMeters(fromLat, fromLon, toLat, toLon) > JumpMetres;

    private void OnTick(object? sender, EventArgs e)
    {
        // The timer only ASKS. The answer is judged where it lands (OnPositionReceived), so every
        // callout acts on the fresh sample: reading LastKnownPosition straight after asking — as
        // this tick once did — handed the monitor the PREVIOUS poll's answer, so every callout came
        // a poll late, with a ground flag from yet another sample. Two independent features share
        // the answer; the surface callout is NOT behind Enabled — that switch is the passing
        // callouts' opt-in. With neither on, nothing is asked for and nothing is sampled, which is
        // why turning either back on forgets the last position (SurroundingsSampleTracker): nothing
        // may be measured across the pause.
        if (!_sim.IsConnected) return;
        if (!Enabled && !SurfaceCalloutsEnabled) return;
        _sim.RequestAircraftPosition();
    }

    /// <summary>
    /// One <c>AIRCRAFT_POSITION</c> answer — this monitor's own 2 s request, or one another feature
    /// made (ground traffic and TCAS every 3 s, Where Am I, Look Around, the liftoff confirm). Each
    /// is a genuine fresh sample and is judged the same way: the tracker measures distance between
    /// whatever samples it is given, and every gate downstream is time- or distance-based, never
    /// sample-count-based. Raised only by <c>SimConnectManager.ProcessAircraftPosition</c> — the
    /// case-4 frame, the only one carrying the surface fields — on the UI thread (WndProc
    /// dispatch), so the surface, the position and the ground flag used here are ONE sample.
    ///
    /// <para>The WHOLE body is guarded. <c>ProcessAircraftPosition</c> does catch around the event,
    /// but this monitor is its first permanent subscriber: a throw here would abort the multicast,
    /// and every <c>RequestAircraftPositionAsync</c> one-shot behind it — Alt+Y, Alt+L, the liftoff
    /// confirm — would miss this answer.</para>
    /// </summary>
    private void OnPositionReceived(object? sender, SimConnectManager.AircraftPosition p)
    {
        try
        {
            // An answer another feature asked for while both switches are off is not a sample
            // either: the monitor samples nothing while both are off, which is what lets turning a
            // switch back on forget the last position (SurroundingsSampleTracker).
            if (!Enabled && !SurfaceCalloutsEnabled) return;

            // The ground flag of THIS sample (the same test ProcessAircraftPosition uses for
            // LastKnownOnGround), never a cached one: the flag, the position and the surface are
            // one sample. A flag that is not a number counts as airborne — the safe direction: it
            // can only reset, never speak.
            if (!(p.SimOnGround >= 0.5))
            {
                // The next landing's ranges have nothing to do with the departure's, and a building
                // whose range was still closing at rotation would read as "now opening" on the
                // rollout. Once per airborne episode — repeating it for a whole cruise buys nothing.
                if (!_resetWhileAirborne) { Reset(); _resetWhileAirborne = true; }
                return;
            }
            _resetWhileAirborne = false;

            // Everything about this sample that needs no sim, no catalog and no ICAO — the last
            // position, the teleport test, the unreadable-sample guard and the surface gate — is
            // the tracker's, where it is pinned at PollMs, the SPARSEST cadence a sample can arrive
            // at (ground traffic, TCAS and hotkey one-shots can all deliver one sooner).
            var sample = _samples.Sample(p.Latitude, p.Longitude, p.SurfaceType, p.SurfaceInfoValid, p.GroundSpeedKnots);

            // A position that is not a finite number: nothing on this sample can use it (the
            // resolve and the ranking below would each read it), and the tracker kept the last
            // READABLE one as the reference for the next sample.
            if (!sample.Usable) return;

            // A teleport moves every tracked building by hundreds of metres in one poll, so a range
            // that had been closing reads as "now opening" — a false "Passing X" at the moment of
            // the teleport. The tracks describe a continuous drive; they do not survive being
            // moved. The tracker has already dropped the surface baseline for the same reason: an
            // aircraft PUT on the grass has not driven off anything.
            if (sample.Jumped)
            {
                // No ICAO in the line: the resolve that names it runs below, and a jump can land
                // here before this airport has ever been named.
                _gate.Reset();
                Log.Debug("Surroundings", "position jump: passing-callout tracks and surface baseline dropped");
            }

            // The surface callout runs BEFORE every airport-dependent guard below — it needs no
            // navdata, no catalog and no ICAO, and it must keep working at a field the database has
            // never heard of. It is also deliberately NOT behind SuppressCheck: running off the
            // side during a takeoff roll or a landing rollout is the worst case there is, and those
            // are exactly the states that suppression silences.
            if (sample.SurfaceCallout is { } surfaceCall)
            {
                // Queued, not immediate: AnnounceImmediate discards whatever is being spoken, and
                // this codebase has been bitten repeatedly by one callout cutting another off
                // mid-word. A one- or two-second wait behind the queue is the lesser cost. POSTED,
                // not spoken here — see PostAnnounce.
                PostAnnounce(surfaceCall);
                Log.Debug("Surroundings", $"surface callout: {surfaceCall} (type={p.SurfaceType:F0} valid={p.SurfaceInfoValid:F0} gs={p.GroundSpeedKnots:F1})");
            }
            if (!Enabled) return;   // the rest of this sample belongs to the passing callouts

            // The first sample of a flight, after a Reset() or after a pause in sampling (both
            // switches off) is only RECORDED: there is nothing to measure it from — no distance for
            // the surface gate, no jump test — so the passing half acts from the next one.
            if (sample.First) return;

            var provider = _provider();
            if (provider == null) return;

            var now = DateTime.UtcNow;
            if (now - _icaoAt > IcaoRefresh)
            {
                _icaoAt = now;
                // The same resolver both hotkeys use — the airport whose box the aircraft is in,
                // never the nearest reference point (which is a heliport at a third of the
                // stands at some hubs), and short idents included.
                string next = CurrentAirport.Resolve(provider, p.Latitude, p.Longitude) ?? "";
                if (!string.Equals(next, _icao, StringComparison.OrdinalIgnoreCase)) { _icao = next; _gate.Reset(); _lastCatalog = null; }
            }
            if (_icao.Length == 0) return;

            bool suppressed = SuppressCheck?.Invoke() == true || _announcer.Suppressed;

            // Never build synchronously here, on the UI thread — a first-time scenery scan/DB read
            // would stall the whole message pump. TryGetCached is a lock-only read; when nothing
            // usable is cached yet (or it's stale), ask for the background build and evaluate
            // callouts on a later sample once it lands in the cache. No in-flight guard of our own:
            // GetAsync is single-flight per ICAO and remembers a failed build, so these samples can
            // neither stack builds nor hammer a broken one — but even a background build competes
            // for the disk and the database at a moment the pilot is busy, so it waits for one the
            // pilot is not.
            if (!_cache.TryGetCached(_icao, out var catalog))
            {
                // The probe is read here at most ONCE, and only on a sample that may actually start
                // something — a suppressed or fast sample takes neither the lock nor the disk.
                if (MayStartBuild(suppressed, p.GroundSpeedKnots))
                {
                    _ = _cache.GetAsync(_icao);
                    WarmProbeIfNeeded(provider, now, RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude) != null);
                }
                return;
            }
            if (catalog == null || catalog.Features.Count == 0) return;

            // A rebuild (a late OSM answer, a GSX publish, a degraded catalog past its lifetime)
            // hands back a different INSTANCE, and a feature's geometry basis can change with it —
            // a stand cluster becomes a building outline — so a track carried across the swap sees
            // a range STEP rather than the next sample of an approach. Only the approaches go; what
            // the pilot has already been told is not re-said (RebaselineTracks, never Reset).
            if (!ReferenceEquals(catalog, _lastCatalog))
            {
                if (_lastCatalog != null) _gate.RebaselineTracks();
                _lastCatalog = catalog;
            }

            if (suppressed) return;

            // ONE probe read per position sample, serving BOTH things that need it: the warm-up
            // decision below and the silence under it. The read takes TaxiGuidanceManager._stateLock,
            // which a Where-Am-I lookup (Alt+Y, Alt+L) holds across its whole graph build, so a
            // second read on the same sample is a second chance to wait on it — and that is exactly
            // how the retry, when it read the probe for itself, quietly went from once a minute to
            // every poll.
            bool? onRunway = RunwayProbe?.Invoke(_icao, p.Latitude, p.Longitude);

            // ONE quiet-moment policy, BOTH background jobs. The warm-up used to be the heavier of
            // the two — a whole taxi graph built under that same _stateLock, which, started at
            // ~120 kt on the first ground tick after a touchdown, blocked the UI thread (the
            // SimConnect pump, the queued announcer, the hotkeys) for the length of it during the
            // rollout. It now reads the runway rows alone and takes the lock only to publish, but it
            // still reads the database, and one policy serves both jobs. Waiting costs at most a late
            // FIRST callout: the airborne Reset() emptied the tracks, so the gate needs two fed
            // samples and 15 m of closing before it can arm, and a warm-up started at the first
            // quiet sample lands well inside that window.
            if (MayStartBuild(suppressed, p.GroundSpeedKnots)) WarmProbeIfNeeded(provider, now, onRunway != null);

            // Takeoff Assist and the taxi states cannot see a takeoff flown without the assist or
            // a landing without an exit plan. A runway is never where a building callout belongs.
            if (onRunway == true) return;

            double hdgTrue = RelativeDirection.Normalize360(p.HeadingMagnetic + p.MagneticVariation);
            // PassingCalloutGate.RankRadiusMetres, never a literal: the gate only ever sees what
            // this list contains and starts TRACKING a feature at this window's edge, so a pass
            // radius wider than the window would be silently capped here, and one within
            // MinApproachMetres of it could never close enough to arm.
            var ranked = SurroundingsReport.Rank(catalog, p.Latitude, p.Longitude, hdgTrue, PassingCalloutGate.RankRadiusMetres);

            var hit = _gate.Evaluate(ranked, p.GroundSpeedKnots, now);
            if (hit == null) return;
            string phrase = $"Passing {hit.Feature.SpokenName}, {RelativeDirection.Side(hit.RelativeBearingDeg)}.";
            // POSTED, not spoken here — see PostAnnounce.
            PostAnnounce(phrase);
            Log.Debug("Surroundings", $"callout {_icao}: {phrase} dist={hit.DistanceMetres:F0} rel={hit.RelativeBearingDeg:F0}");
        }
        catch (Exception ex)
        {
            Log.Warn("Surroundings", $"monitor sample failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Delivers a callout OUTSIDE the SimConnect dispatch that produced the sample, never spoken
    /// synchronously inside <see cref="OnPositionReceived"/>. This monitor is that event's FIRST
    /// permanent subscriber, so a <c>RequestAircraftPositionAsync</c> one-shot behind it — Where Am
    /// I, Look Around — answers the SAME sample later in the SAME multicast and may call
    /// <c>AnnounceImmediate</c> synchronously, which cancels whatever this monitor had just queued.
    /// Posting to the UI thread's own <see cref="SynchronizationContext"/> (captured at
    /// construction) lets that interrupting readout speak first and this callout follow it, instead
    /// of being cancelled within milliseconds of being queued.
    ///
    /// <para>Falls back to a direct (still queued) <c>Announce</c> when no context was captured. If
    /// the captured context's own marshaling is gone by the time this posts, <c>Post</c> throws
    /// <see cref="InvalidOperationException"/> — the callout is dropped and logged once, never
    /// thrown onward. The posted callback itself speaks nothing once the monitor is disposed.</para>
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
    /// Starts the runway probe's warm-up on a thread-pool thread — the probe itself reads nothing,
    /// because its caller is the monitor's position handler, on the UI thread. Once per airport,
    /// then at most once per <see cref="ProbeWarmRetry"/> while the probe still cannot answer (a
    /// warm-up whose database read threw publishes nothing, and a stamp alone would leave the runway
    /// silence off there for the whole session), and never twice at once.
    ///
    /// <para>This method NEVER reads the probe: <paramref name="probeAnswered"/> is the sample's own
    /// single read, and <see cref="ShouldWarmProbe"/> decides from state alone. The warm-up is
    /// PREPARED here, on the UI thread and in the same turn that read <paramref name="provider"/>
    /// (<see cref="PrepareRunwayProbeWarmUp"/>), so a database switch can neither swap the provider
    /// out from under it nor let it file the previous database's runways under the new one; the
    /// task is only ever polled for completion — never waited on.</para>
    /// </summary>
    private void WarmProbeIfNeeded(IAirportDataProvider provider, DateTime now, bool probeAnswered)
    {
        var prepare = PrepareRunwayProbeWarmUp;
        if (prepare == null) return;

        // A warm-up for a DIFFERENT airport is no longer this airport's business: it finishes on
        // its own and is not counted as in flight, or the first warm-up here would wait out a read
        // whose result this airport never uses. Its result cannot evict this airport's memo either:
        // the manager keeps a warm-up's shapes only for the airport the probe is being asked about.
        var last = _probeWarmUp;
        bool sameAirport = last != null && string.Equals(last.Icao, _icao, StringComparison.OrdinalIgnoreCase);
        TimeSpan sinceLast = sameAirport ? now - last!.StartedAt : TimeSpan.MaxValue;
        bool inFlight = sameAirport && !last!.Work.IsCompleted;
        if (!ShouldWarmProbe(probeAnswered, sameAirport, sinceLast, inFlight)) return;

        // Reached with sameAirport only on a retry: a warm-up that left the probe unable to answer.
        // Said once per airport — nothing else records it, and repeating it every minute would bury
        // the line that matters.
        //
        // What the line may claim is exactly what is known. The warm-up publishes whatever the
        // runway rows describe — an EMPTY list at a field with no runways, which answers "not on a
        // runway" — a late warm-up for another airport can no longer evict this one's memo, and a
        // database switch resets this monitor, so the one way left to reach here is a warm-up whose
        // database read THREW, which its own catch below has already logged.
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
