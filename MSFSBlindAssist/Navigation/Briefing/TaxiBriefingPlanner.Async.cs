// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs
using System.Globalization;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Navigation.Briefing;

public static partial class TaxiBriefingPlanner
{
    /// <summary>Budget for both airports' database reads, graph builds and routing. The two legs run at the
    /// same time under it: a leg that overruns reports "timed out" on its own, and the taxi section's
    /// computation never runs longer than this. The aircraft-position read (at most 1.5 s) and the
    /// SayIntentions read (bounded by its own timeouts) happen before it, so the whole taxi section can
    /// take a few seconds more.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);

    private const string LogCategory = "taxi_briefing";

    /// <summary>
    /// Both legs, started together and computed concurrently, each on a background thread inside its own
    /// try/catch, so a failure or a timeout on one leg becomes that leg's
    /// <see cref="TaxiLegBriefing.Unavailable"/> and never an exception to the caller. A null or absent
    /// database makes both legs unavailable with one reason.
    /// </summary>
    /// <param name="budget">Shared by both legs, and the only thing ever reported as "timed out".</param>
    /// <param name="ct">The caller's own cancellation, never reported as a timeout: cancelled on entry, or
    /// while a leg is still computing, PlanAsync throws an <see cref="OperationCanceledException"/>
    /// carrying this token. A briefing whose legs had both finished before the cancellation was seen is
    /// still returned.</param>
    public static async Task<TaxiBriefing> PlanAsync(TaxiBriefingRequest request, IAirportDataProvider? provider,
                                                     GateDataSource? gateSource, TimeSpan budget, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        var token = cts.Token;

        // A flight whose origin and destination are the SAME airport builds ONE graph for both legs, never two
        // — built twice at once, they were the identical graph, and nothing in the planner shares a graph
        // between threads. The shared build is cached in the Lazy, so a slow or cancelled build is awaited by
        // both legs rather than started twice; oneLegAtATime then serializes the two plan(bundle) calls, since
        // a bundle's graph is not safe to read from two threads at once.
        Func<string, Task<(GraphBundle? Bundle, string? Reason)>> build =
            icao => TaxiBriefingGraphSource.BuildAsync(provider!, gateSource, icao, token);
        SemaphoreSlim? oneLegAtATime = null;
        if (provider != null && SameAirport(request.OriginIcao, request.DestinationIcao))
        {
            // buildOne is a SEPARATE local holding today's build delegate: a lambda captures a LOCAL VARIABLE by
            // reference, not its value at the time the lambda is created, so closing the shared factory over
            // `build` itself (then reassigning `build` below) would make the factory call itself the first time
            // it runs — Lazy<T> throws on that re-entrant access. Capturing a variable that is never reassigned
            // avoids it.
            var buildOne = build;
            var shared = new Lazy<Task<(GraphBundle? Bundle, string? Reason)>>(() => buildOne(request.OriginIcao));
            build = _ => shared.Value;
            oneLegAtATime = new SemaphoreSlim(1, 1);
        }

        // Both legs are STARTED before either is awaited. Awaited one after the other, a departure airport
        // that used the whole budget (an OpenStreetMap fetch for an airport with no navdata taxiways can)
        // left the arrival leg to start on an already-cancelled token: it was reported as timed out without
        // a single database read.
        var taxiOut = PlanLegSafelyAsync(request.OriginIcao, request.OriginRunway, request.OriginRunwayNote, provider, build, oneLegAtATime, token, ct,
            g => PlanTaxiOut(request, g));
        var taxiIn = PlanLegSafelyAsync(request.DestinationIcao, request.DestinationRunway, request.DestinationRunwayNote, provider, build, oneLegAtATime, token, ct,
            g => PlanTaxiIn(request, g));
        await Task.WhenAll(taxiOut, taxiIn).ConfigureAwait(false);
        return new TaxiBriefing(request.Aircraft, await taxiOut.ConfigureAwait(false), await taxiIn.ConfigureAwait(false));
    }

    /// <summary>Both ICAOs non-blank and equal ignoring case — the flight's two legs share one graph.</summary>
    private static bool SameAirport(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One leg, which writes exactly one summary line to debug.log: the outcome the AI is told. It is
    /// written here, once the wait is over, and never from inside the computation — a computation abandoned
    /// at the budget keeps running (a graph build cannot be cancelled) and must not log a result after the
    /// leg has been reported as timed out. Throws only for the caller's own cancellation.
    /// </summary>
    /// <param name="build">Builds the leg's <see cref="GraphBundle"/> — shared with the other leg (and its own
    /// build serialized behind <paramref name="oneLegAtATime"/>) when both legs are the same airport.</param>
    /// <param name="oneLegAtATime">Non-null only when both legs share one airport: taken around <c>plan(bundle)</c>
    /// so the two legs never read the shared bundle's graph from two threads at once.</param>
    private static async Task<TaxiLegBriefing> PlanLegSafelyAsync(string icao, string runway, string? runwayNote,
        IAirportDataProvider? provider, Func<string, Task<(GraphBundle? Bundle, string? Reason)>> build,
        SemaphoreSlim? oneLegAtATime, CancellationToken budget, CancellationToken caller,
        Func<GraphBundle, TaxiLegBriefing> plan)
    {
        // A leg made unavailable HERE -- before PlanTaxiOut/PlanTaxiIn ever runs -- would otherwise carry no notes at
        // all, dropping the runway note the request was given (a leg that could not be computed still deserves to
        // say why SayIntentions' runway was used).
        var notes = runwayNote is { Length: > 0 } ? new[] { runwayNote } : null;

        if (provider == null || !provider.DatabaseExists)
            return LogLeg(TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no navigation database loaded", notes: notes));
        if (string.IsNullOrWhiteSpace(icao))
            return LogLeg(TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no airport in the flight plan", notes: notes));

        var work = Task.Run(async () =>
        {
            var (bundle, reason) = await build(icao).ConfigureAwait(false);
            if (bundle == null)
                return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, reason ?? "no ground data for this airport", notes: notes);
            budget.ThrowIfCancellationRequested();
            if (oneLegAtATime != null) await oneLegAtATime.WaitAsync(budget).ConfigureAwait(false);
            try
            {
                return plan(bundle);
            }
            finally
            {
                oneLegAtATime?.Release();
            }
        }, budget);

        try
        {
            // WaitAsync bounds the wait even when the work inside ignores the token; abandoned work finishes
            // in the background and is discarded.
            return LogLeg(await work.WaitAsync(budget).ConfigureAwait(false));
        }
        // In both abandon paths the summary line is written BEFORE the fault observer is attached: work that has
        // already faulted runs the observer at once, and its line must follow the summary, never precede it.
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            Log.Info(LogCategory, $"{LogField(icao)} {LogField(runway)}: cancelled by the caller");
            ObserveAbandonedLeg(work, icao, runway);
            throw new OperationCanceledException(caller);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            var timedOut = TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "taxi route computation timed out", notes: notes);
            Log.Warn(LogCategory, Summarise(timedOut));
            ObserveAbandonedLeg(work, icao, runway);
            return timedOut;
        }
        catch (Exception ex)
        {
            // Includes a cancellation neither token asked for: a failure, not a timeout.
            var failed = TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, $"taxi route could not be computed ({ex.Message})", notes: notes);
            Log.Warn(LogCategory, $"{Summarise(failed)} {ex}");
            return failed;
        }
    }

    private static TaxiLegBriefing LogLeg(TaxiLegBriefing leg)
    {
        Log.Info(LogCategory, Summarise(leg));
        return leg;
    }

    /// <summary>
    /// Nothing awaits an abandoned computation any more, so a fault it hits later would surface only when
    /// the task is finalized — as an unobserved task exception in startup.log, at no particular time and
    /// naming no airport. Observe it here and log it after the leg's summary; the leg is not changed.
    /// </summary>
    private static void ObserveAbandonedLeg(Task work, string icao, string runway) =>
        _ = work.ContinueWith(
            t => Log.Warn(LogCategory,
                $"{LogField(icao)} {LogField(runway)}: abandoned taxi route computation failed: {t.Exception!.GetBaseException()}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static string Summarise(TaxiLegBriefing leg)
    {
        string head = $"{LogField(leg.Icao)} {LogField(leg.Runway)} tier={leg.Tier}";
        string body = leg.Unavailable != null
            ? $"{head} unavailable=\"{leg.Unavailable}\""
            : $"{head} endpoint=\"{leg.EndpointDescription}\" taxiways=[{string.Join(",", leg.Taxiways)}] " +
              $"turns=[{string.Join(",", leg.TaxiwayTurns.Select(t => t ?? "-"))}] standTurn=\"{leg.StandTurn ?? "-"}\" " +
              $"holds=[{string.Join(";", leg.HoldShorts.Select(h => $"{h.Runway}@{h.Taxiway}{(h.BeforeEntering ? "(entry)" : "")}"))}] " +
              $"exit={(leg.Exit is { } x ? TaxiBriefingRenderer.ExitName(x.Exit) : "-")} " +
              $"next={(leg.Exit?.NextExit is { } n ? TaxiBriefingRenderer.ExitName(n) : "-")} " +
              $"distM={leg.DistanceMetres.ToString("0", CultureInfo.InvariantCulture)}";
        // The runway note and a parking-service refusal live only in Notes -- without this, debug.log named the
        // runway used but never said SayIntentions assigned it, or that a gate was refused and why.
        return leg.Notes.Count > 0 ? $"{body} notes=\"{string.Join(" | ", leg.Notes)}\"" : body;
    }

    /// <summary>A blank airport or runway logs as "-", so no line starts with a space.</summary>
    private static string LogField(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
