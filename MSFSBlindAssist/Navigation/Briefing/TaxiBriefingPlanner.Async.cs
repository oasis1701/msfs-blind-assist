// MSFSBlindAssist/Navigation/Briefing/TaxiBriefingPlanner.Async.cs
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Navigation.Briefing;

public static partial class TaxiBriefingPlanner
{
    /// <summary>Whole-briefing budget for both airports' database reads, graph builds and routing.
    /// A leg that overruns reports "timed out"; the AI briefing never waits longer than this.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(20);

    private const string LogCategory = "taxi_briefing";

    /// <summary>
    /// Both legs, each computed on a background thread inside its own try/catch, so a failure or a
    /// timeout on one leg becomes that leg's <see cref="TaxiLegBriefing.Unavailable"/> and never an
    /// exception to the caller. A null or absent database makes both legs unavailable with one reason.
    /// </summary>
    public static async Task<TaxiBriefing> PlanAsync(TaxiBriefingRequest request, IAirportDataProvider? provider,
                                                     GateDataSource? gateSource, TimeSpan budget, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        var token = cts.Token;

        var taxiOut = await PlanLegSafelyAsync(request.OriginIcao, request.OriginRunway, provider, gateSource, token,
            g => PlanTaxiOut(request, g)).ConfigureAwait(false);
        var taxiIn = await PlanLegSafelyAsync(request.DestinationIcao, request.DestinationRunway, provider, gateSource, token,
            g => PlanTaxiIn(request, g)).ConfigureAwait(false);
        return new TaxiBriefing(request.Aircraft, taxiOut, taxiIn);
    }

    private static async Task<TaxiLegBriefing> PlanLegSafelyAsync(string icao, string runway, IAirportDataProvider? provider,
        GateDataSource? gateSource, CancellationToken ct, Func<GraphBundle, TaxiLegBriefing> plan)
    {
        if (provider == null || !provider.DatabaseExists)
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no navigation database loaded");
        if (string.IsNullOrWhiteSpace(icao))
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "no airport in the flight plan");

        try
        {
            // WaitAsync bounds the wait even when the work inside ignores the token (a graph build
            // cannot be cancelled); an abandoned build finishes in the background and is discarded.
            return await Task.Run(async () =>
            {
                var (bundle, reason) = await TaxiBriefingGraphSource.BuildAsync(provider, gateSource, icao, ct).ConfigureAwait(false);
                if (bundle == null)
                {
                    Log.Info(LogCategory, $"{icao} {runway}: no graph — {reason}");
                    return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, reason ?? "no ground data for this airport");
                }
                ct.ThrowIfCancellationRequested();
                var leg = plan(bundle);
                Log.Info(LogCategory, Summarise(leg));
                return leg;
            }, ct).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Warn(LogCategory, $"{icao} {runway}: taxi route computation timed out");
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, "taxi route computation timed out");
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"{icao} {runway}: {ex}");
            return TaxiLegBriefing.UnavailableLeg(icao, runway, BriefingTier.None, $"taxi route could not be computed ({ex.Message})");
        }
    }

    private static string Summarise(TaxiLegBriefing leg) => leg.Unavailable != null
        ? $"{leg.Icao} {leg.Runway} tier={leg.Tier} unavailable=\"{leg.Unavailable}\""
        : $"{leg.Icao} {leg.Runway} tier={leg.Tier} endpoint=\"{leg.EndpointDescription}\" taxiways=[{string.Join(",", leg.Taxiways)}] " +
          $"holds=[{string.Join(";", leg.HoldShorts.Select(h => $"{h.Runway}@{h.Taxiway}{(h.BeforeEntering ? "(entry)" : "")}"))}] " +
          $"exit={leg.Exit?.Exit.TaxiwayName ?? "-"} next={leg.Exit?.NextExit?.TaxiwayName ?? "-"} distM={leg.DistanceMetres:0}";
}
