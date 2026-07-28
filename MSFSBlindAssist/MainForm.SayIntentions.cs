using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

/// <summary>
/// SayIntentions hotkey handlers. Reads the active SI flight context and turns a
/// spoken taxi clearance into a pre-filled Taxi Guidance route.
///
/// All parsing lives in MSFSBlindAssist.Services.SayIntentions — this partial only
/// orchestrates. In particular it never builds its own TaxiGraph: the form loads
/// the airport once and hands back the taxiway names it already knows.
/// </summary>
public partial class MainForm
{
    private static readonly LogChannel _siLog = Log.Channel("sayintentions");

    private async Task AnnounceSayIntentionsLastTransmissionAsync()
    {
        try
        {
            var result = await sayIntentionsService.GetLastTransmissionAsync();
            announcer.AnnounceImmediate(result.Transmission != null
                ? $"SayIntentions last transmission. {result.Transmission.ToAnnouncement()}"
                : result.Error ?? "No SayIntentions transmission available.");
        }
        catch (Exception ex)
        {
            _siLog.Error("Last-transmission readout failed", ex);
            announcer.AnnounceImmediate($"SayIntentions transmission lookup failed. {ex.Message}");
        }
    }

    private async Task AnnounceSayIntentionsAssignedStatusAsync()
    {
        try
        {
            var result = await sayIntentionsService.GetAssignedStatusAsync();
            var context = result.Context;
            if (!string.IsNullOrWhiteSpace(context.Error))
            {
                announcer.AnnounceImmediate(context.Error);
                return;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(context.CurrentAirport))
                parts.Add($"Current airport {context.CurrentAirport}.");

            string? gate = FirstNonEmptySi(context.AssignedGate, result.Parking?.Name);
            if (!string.IsNullOrWhiteSpace(gate))
            {
                parts.Add(FormatSayIntentionsGateStatus(context, gate));
                string? nearbyParking = await GetSayIntentionsNearbyParkingStatusAsync(context, gate);
                if (!string.IsNullOrWhiteSpace(nearbyParking))
                    parts.Add(nearbyParking);
            }

            string? departureRunway = FirstNonEmptySi(
                context.ClearedForTakeoff, context.DepartureRunway, context.Runway);
            if (!string.IsNullOrWhiteSpace(departureRunway))
                parts.Add($"Departure runway {departureRunway}.");

            if (!string.IsNullOrWhiteSpace(context.ClearedForLanding))
                parts.Add($"Cleared to land runway {context.ClearedForLanding}.");
            else if (!string.IsNullOrWhiteSpace(context.ArrivalRunway))
                parts.Add($"Arrival runway {context.ArrivalRunway}.");

            announcer.AnnounceImmediate(parts.Count > 0
                ? "SayIntentions status. " + string.Join(" ", parts)
                : result.ParkingError ?? "No SayIntentions assigned gate or runway found for the active flight.");
        }
        catch (Exception ex)
        {
            _siLog.Error("Assigned-status readout failed", ex);
            announcer.AnnounceImmediate($"SayIntentions status lookup failed. {ex.Message}");
        }
    }

    private async Task BuildTaxiRouteFromSayIntentionsAsync()
    {
        if (airportDataProvider == null || !airportDataProvider.DatabaseExists)
        {
            announcer.AnnounceImmediate("Airport database not available. Configure database in settings.");
            return;
        }

        if (!ValidateDatabaseSimulatorMatch()) return;

        try
        {
            announcer.AnnounceImmediate("Reading SayIntentions taxi clearance.");

            var status = await sayIntentionsService.GetAssignedStatusAsync();
            var context = status.Context;
            if (!string.IsNullOrWhiteSpace(context.Error))
            {
                announcer.AnnounceImmediate(context.Error);
                return;
            }

            // Only fall back to the last radio transmission when it is actually
            // shaped like a taxi clearance — a landing clearance heard on rollout
            // must never become a taxi route.
            if (string.IsNullOrWhiteSpace(context.ClearanceText))
            {
                var last = await sayIntentionsService.GetLastTransmissionAsync();
                if (last.Transmission != null
                    && SayIntentionsClearanceParser.LooksLikeTaxiClearance(last.Transmission.Message))
                {
                    context.ClearanceText = last.Transmission.Message;
                }
            }

            var position = await GetFreshAircraftPositionAsync();
            string? icao = ResolveSayIntentionsAirport(context, position);
            if (string.IsNullOrWhiteSpace(icao))
            {
                announcer.AnnounceImmediate("SayIntentions route unavailable. No current airport found.");
                return;
            }

            // ONE graph build: the form loads the airport, and everything below
            // resolves against the taxiway names IT already knows.
            var form = GetOrCreateTaxiAssistForm();
            var knownTaxiways = await form.LoadAirportForExternalRouteAsync(
                position.Latitude, position.Longitude, position.HeadingMagnetic, icao);
            if (knownTaxiways.Count == 0)
            {
                announcer.AnnounceImmediate($"No taxi path data available for {icao}.");
                return;
            }

            string clearance = context.ClearanceText ?? "";
            if (!TryResolveSayIntentionsDestination(form, status, clearance, out bool isRunway, out string label))
            {
                announcer.AnnounceImmediate(
                    "SayIntentions route unavailable. No usable assigned runway or gate found.");
                return;
            }

            // The taxiways may come from SayIntentions' structured taxi_path, but a
            // hold-short only ever exists in the spoken clearance — so the plan is
            // always parsed, and its hold-shorts are then tied to whichever taxiway
            // sequence is actually applied.
            var plan = ParseClearanceTaxiPlan(clearance, knownTaxiways);
            var (taxiways, unknownTaxiways) = context.TaxiwaySequence.Count > 0
                ? MatchKnownTaxiways(context.TaxiwaySequence, knownTaxiways)
                : (plan.Taxiways, plan.UnknownTaxiways);
            var holdShorts = MapHoldShortsToTaxiways(plan.HoldShorts, taxiways);
            bool autoStart = SettingsManager.Current.SayIntentionsAutoStartTaxiGuidance;

            // Show BEFORE announcing so the screen reader's own form-focus
            // announcement does not collide with the route summary.
            form.Show();
            form.BringToFront();
            var outcome = form.ApplyExternalRoute(isRunway, label, taxiways, holdShorts, autoStart);

            _siLog.Info($"{icao} dest='{label}' runway={isRunway} " +
                        $"applied=[{string.Join(",", outcome.AppliedTaxiways)}] " +
                        $"skipped=[{string.Join(",", outcome.SkippedTaxiways)}] " +
                        $"notAtAirport=[{string.Join(",", unknownTaxiways)}] " +
                        $"holdShorts=[{string.Join(",", outcome.AppliedHoldShorts.Select(h => $"{h.Runway} after {h.AfterTaxiway}"))}] " +
                        $"holdShortsMissed=[{string.Join(",", outcome.SkippedHoldShortRunways)}] " +
                        $"autoStart={autoStart}");

            announcer.Announce(
                BuildExternalRouteAnnouncement(outcome, unknownTaxiways, label, autoStart));
        }
        catch (Exception ex)
        {
            _siLog.Error("Taxi route build failed", ex);
            announcer.AnnounceImmediate($"SayIntentions taxi route failed. {ex.Message}");
        }
    }

    /// <summary>Names every taxiway AND every hold-short that could not be applied.
    /// Silent degradation to a shortest-path route is invisible to a blind pilot, and
    /// a hold-short ATC gave that never reached the form is a runway-incursion
    /// risk — both have to be said out loud, applied or not.
    ///
    /// "Could not apply" has TWO sources and one line: a taxiway the airport has but the
    /// form could not seat (outcome.SkippedTaxiways), and a taxiway only the clearance
    /// knows (unknownTaxiways). The pilot needs the same thing from both — the name of
    /// the leg the route is not taking.</summary>
    internal static string BuildExternalRouteAnnouncement(
        TaxiAssistForm.ExternalRouteOutcome outcome, IReadOnlyList<string> unknownTaxiways,
        string destination, bool autoStart)
    {
        var parts = new List<string> { $"SayIntentions route to {destination}." };

        if (!outcome.DestinationApplied)
            parts.Add("Destination not set. Check the destination field.");

        parts.Add(outcome.AppliedTaxiways.Count > 0
            ? $"Via {string.Join(", ", outcome.AppliedTaxiways)}."
            : "No taxiways from the clearance matched this airport. Using shortest path.");

        var couldNotApply = new List<string>(outcome.SkippedTaxiways);
        couldNotApply.AddRange(unknownTaxiways);
        if (couldNotApply.Count > 0)
            parts.Add($"Could not apply {string.Join(", ", couldNotApply)}.");

        foreach (var holdShort in outcome.AppliedHoldShorts)
            parts.Add($"Hold short of runway {holdShort.Runway} after {holdShort.AfterTaxiway}.");

        if (outcome.SkippedHoldShortRunways.Count > 0)
            parts.Add($"Could not set hold short of runway {string.Join(", ", outcome.SkippedHoldShortRunways)}.");

        parts.Add(autoStart
            ? "Guidance started."
            : "Review the fields, then press Calculate Route to start guidance.");

        return string.Join(" ", parts);
    }

    /// <summary>Destination priority: the clearance's own runway, then its gate, then
    /// the assigned gate when already at the destination airport, then the departure
    /// runway, then the assigned gate, then the arrival runway. Each candidate must
    /// resolve to a real entry in the form's destination list to win.
    ///
    /// The whole list goes to the form in one call — asking candidate by candidate
    /// re-listed (and re-selected) the form's destinations on every probe, and left
    /// the pilot's own destination discarded when none of them resolved.</summary>
    private static bool TryResolveSayIntentionsDestination(
        TaxiAssistForm form, SayIntentionsStatusResult status, string clearance,
        out bool isRunway, out string label)
    {
        var context = status.Context;

        string? gate = FirstNonEmptySi(context.AssignedGate, status.Parking?.Name);
        bool atDestination = !string.IsNullOrWhiteSpace(context.CurrentAirport)
            && !string.IsNullOrWhiteSpace(context.Destination)
            && context.CurrentAirport.Equals(context.Destination, StringComparison.OrdinalIgnoreCase);

        var candidates = new List<TaxiAssistForm.ExternalDestination>
        {
            new(true, SayIntentionsClearanceParser.ParseDestinationRunway(clearance)),
            new(false, SayIntentionsClearanceParser.ParseDestinationGate(clearance))
        };

        if (atDestination)
            candidates.Add(new(false, gate));

        candidates.Add(new(true, FirstNonEmptySi(
            context.ClearedForTakeoff, context.DepartureRunway, context.Runway)));
        candidates.Add(new(false, gate));
        candidates.Add(new(true, FirstNonEmptySi(context.ClearedForLanding, context.ArrivalRunway)));

        return form.TryResolveExternalDestination(candidates, out isRunway, out label);
    }

    /// <summary>A "hold short of runway X" from the clearance, tied to the taxiway it
    /// FOLLOWS. AfterTaxiway is empty when the clearance named no taxiway ahead of
    /// it — nothing in the form can carry that, and the pilot is told so.</summary>
    internal readonly record struct ClearanceHoldShort(string AfterTaxiway, string Runway);

    private static readonly Regex ViaWord = new(
        @"\bVIA\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The taxiway sequence a clearance names, plus every hold-short tied to
    /// the taxiway it follows.
    ///
    /// The clearance is cut at the spans the parser masks out — hold-shorts AND
    /// crossings — and each piece is resolved on its own, so WHERE each hold-short
    /// falls in the sequence survives. Cutting on the parser's own mask is what keeps
    /// a second copy of the hold-short phrasing out of this file; the two would drift.
    ///
    /// A taxiway repeated across a hold-short is KEPT ("N, hold short 15R, N" — the
    /// KBOS pattern): the form carries one hold-short per row, so collapsing the
    /// repeat would throw the second one away. A repeat across a plain crossing still
    /// collapses, exactly as a single ParseTaxiways call would.
    ///
    /// UnknownTaxiways carries the names the clearance gave that this airport does not
    /// have, gathered across every piece so one falling either side of a hold-short is
    /// still reported.</summary>
    internal static (List<string> Taxiways, List<ClearanceHoldShort> HoldShorts, List<string> UnknownTaxiways)
        ParseClearanceTaxiPlan(string? clearance, IReadOnlyList<string> knownTaxiways)
    {
        var taxiways = new List<string>();
        var holdShorts = new List<ClearanceHoldShort>();
        var unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(clearance) || knownTaxiways.Count == 0)
            return (taxiways, holdShorts, unknown);

        bool routeStarted = false;
        bool repeatAllowed = false;

        foreach (var (text, maskedSpan) in SplitClearanceAtMaskedSpans(clearance))
        {
            // Only a piece that follows a "via" is a taxiway list. The parser needs
            // the keyword to find one, so a continuation piece is given one — but a
            // piece BEFORE the first "via" is left alone, or a gate name in
            // "taxi to gate A9" would be read as taxiway A9.
            bool hasVia = ViaWord.IsMatch(text);
            if (hasVia || routeStarted)
            {
                var (found, missing) = SayIntentionsClearanceParser.ScanTaxiways(
                    hasVia ? text : "via " + text, knownTaxiways);

                for (int i = 0; i < found.Count; i++)
                {
                    bool repeatsPrevious = taxiways.Count > 0
                        && taxiways[^1].Equals(found[i], StringComparison.OrdinalIgnoreCase);
                    if (repeatsPrevious && !(i == 0 && repeatAllowed)) continue;
                    taxiways.Add(found[i]);
                }

                foreach (string name in missing)
                {
                    if (!unknown.Contains(name, StringComparer.OrdinalIgnoreCase))
                        unknown.Add(name);
                }

                routeStarted = true;
            }

            string? runway = SayIntentionsClearanceParser.ParseHoldShortRunway(maskedSpan);
            repeatAllowed = !string.IsNullOrWhiteSpace(runway);
            if (repeatAllowed)
                holdShorts.Add(new ClearanceHoldShort(
                    taxiways.Count > 0 ? taxiways[^1] : "", runway!));
        }

        return (taxiways, holdShorts, unknown);
    }

    /// <summary>Cuts the clearance into the pieces BETWEEN the spans the parser masks,
    /// pairing each piece with the span that follows it (empty for the last piece).
    /// The mask blanks whole hold-short/crossing phrases and preserves length, so a
    /// span is simply where the two strings differ — walked across the spaces inside
    /// it, which the mask leaves identical.</summary>
    private static List<(string Text, string MaskedSpan)> SplitClearanceAtMaskedSpans(string clearance)
    {
        string masked = SayIntentionsClearanceParser.MaskHoldShortAndCrossings(clearance);
        var pieces = new List<(string Text, string MaskedSpan)>();

        // Defensive: the mask is documented length-preserving, but this file does not
        // own it. A length change means one piece — the whole clearance, no cuts.
        if (masked.Length != clearance.Length)
        {
            pieces.Add((clearance, ""));
            return pieces;
        }

        int pieceStart = 0;
        int i = 0;
        while (i < clearance.Length)
        {
            if (masked[i] == clearance[i]) { i++; continue; }

            int spanStart = i;
            int spanEnd = ++i;
            while (i < clearance.Length)
            {
                if (masked[i] != clearance[i]) spanEnd = ++i;
                else if (char.IsWhiteSpace(clearance[i])) i++;
                else break;
            }

            pieces.Add((clearance.Substring(pieceStart, spanStart - pieceStart),
                        clearance.Substring(spanStart, spanEnd - spanStart)));
            pieceStart = spanEnd;
        }

        pieces.Add((clearance.Substring(pieceStart), ""));
        return pieces;
    }

    /// <summary>Turns each hold-short's taxiway NAME into its position in the sequence
    /// being applied — which may be SayIntentions' structured taxi_path rather than
    /// the one parsed from speech. Repeats are consumed in order, so the second hold
    /// on a repeated taxiway lands on the second row rather than back on the first.
    /// A name the sequence does not carry maps to -1: the form reports it instead of
    /// hanging the hold-short on whatever row happens to be last.</summary>
    internal static List<TaxiAssistForm.ExternalHoldShort> MapHoldShortsToTaxiways(
        IReadOnlyList<ClearanceHoldShort> holdShorts, IReadOnlyList<string> taxiways)
    {
        var mapped = new List<TaxiAssistForm.ExternalHoldShort>();
        int searchFrom = 0;

        foreach (var holdShort in holdShorts)
        {
            int index = -1;
            for (int i = searchFrom; i < taxiways.Count; i++)
            {
                if (taxiways[i].Equals(holdShort.AfterTaxiway, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0) searchFrom = index + 1;
            mapped.Add(new TaxiAssistForm.ExternalHoldShort(index, holdShort.Runway));
        }

        return mapped;
    }

    /// <summary>Maps SayIntentions' structured taxi_path onto the airport's real
    /// taxiway names, reporting any the graph does not have.
    ///
    /// taxi_path is a list of discrete names rather than speech, so anything that fails
    /// to match here is unambiguously a taxiway this airport lacks — no phonetics, no
    /// judgement call. Dropping it silently left the pilot with a shorter route than
    /// ATC cleared and nothing said about it.</summary>
    internal static (List<string> Resolved, List<string> Unresolved) MatchKnownTaxiways(
        IReadOnlyList<string> wanted, IReadOnlyList<string> knownTaxiways)
    {
        var resolved = new List<string>();
        var unresolved = new List<string>();

        foreach (string value in wanted)
        {
            string normalized = SayIntentionsClearanceParser.NormalizeTaxiwayName(value);
            string? match = knownTaxiways.FirstOrDefault(t =>
                SayIntentionsClearanceParser.NormalizeTaxiwayName(t)
                    .Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(match))
                resolved.Add(match);
            else if (normalized.Length > 0
                     && !unresolved.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                unresolved.Add(normalized);
        }

        return (SayIntentionsClearanceParser.CollapseConsecutive(resolved), unresolved);
    }

    private string? ResolveSayIntentionsAirport(
        SayIntentionsFlightContext context, SimConnectManager.AircraftPosition position)
    {
        string? icao = FirstNonEmptySi(context.CurrentAirport, context.Origin, context.Destination);
        if (string.IsNullOrWhiteSpace(icao))
        {
            icao = airportDataProvider!
                .GetNearbyAirportICAOs(position.Latitude, position.Longitude, 5.0)
                .FirstOrDefault(c => c != null && c.Length == 4);
        }

        return string.IsNullOrWhiteSpace(icao) ? null : icao.ToUpperInvariant();
    }

    private async Task<SimConnectManager.AircraftPosition> GetFreshAircraftPositionAsync()
    {
        var fallback = simConnectManager.LastKnownPosition;
        var tcs = new TaskCompletionSource<SimConnectManager.AircraftPosition>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        simConnectManager.RequestAircraftPositionAsync(position => tcs.TrySetResult(position));

        Task completed = await Task.WhenAny(tcs.Task, Task.Delay(1500));
        if (completed == tcs.Task)
            return await tcs.Task;

        if (fallback.HasValue)
            return fallback.Value;

        throw new InvalidOperationException("Aircraft position unavailable.");
    }

    /// <summary>Reports whether the aircraft is actually sitting at the gate SI
    /// assigned. Purely informational — it never changes the route.</summary>
    private async Task<string?> GetSayIntentionsNearbyParkingStatusAsync(
        SayIntentionsFlightContext context, string assignedGate)
    {
        if (airportDataProvider == null) return null;

        string? icao = FirstNonEmptySi(context.CurrentAirport, context.Origin, context.Destination);
        if (string.IsNullOrWhiteSpace(icao)) return null;

        try
        {
            var position = await GetFreshAircraftPositionAsync();
            var spots = airportDataProvider.GetParkingSpots(icao.ToUpperInvariant());
            if (spots == null || spots.Count == 0) return null;

            ParkingSpot? nearest = null;
            double nearestMetres = double.MaxValue;
            foreach (var spot in spots)
            {
                double metres = TaxiGraph.CalculateDistanceMeters(
                    position.Latitude, position.Longitude, spot.Latitude, spot.Longitude);
                if (metres < nearestMetres) { nearestMetres = metres; nearest = spot; }
            }

            if (nearest == null || nearestMetres > 100.0) return null;

            string wanted = SayIntentionsClearanceParser.NormalizeParkingName(assignedGate);
            bool matchesAssigned =
                SayIntentionsClearanceParser.NormalizeParkingName(nearest.ToString()).Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || SayIntentionsClearanceParser.NormalizeParkingName(TaxiGraph.FormatParkingDisplayName(nearest)).Equals(wanted, StringComparison.OrdinalIgnoreCase);

            string proximity = nearestMetres < 30
                ? "near"
                : $"{(int)(nearestMetres * 3.28084)} feet from";

            return matchesAssigned
                ? $"Aircraft appears {proximity} assigned gate {assignedGate}."
                : $"Aircraft appears {proximity} {TaxiGraph.FormatParkingDisplayName(nearest)}, not assigned gate {assignedGate}.";
        }
        catch (Exception ex)
        {
            _siLog.Warn($"Nearby-parking check failed: {ex.Message}");
            return null;
        }
    }

    private static string FormatSayIntentionsGateStatus(SayIntentionsFlightContext context, string gate)
    {
        string? gateRole = null;
        string? gateAirport = null;

        if (SameIcaoSi(context.CurrentAirport, context.Origin))
        {
            gateRole = "Departure gate";
            gateAirport = context.Origin;
        }
        else if (SameIcaoSi(context.CurrentAirport, context.Destination))
        {
            gateRole = "Arrival gate";
            gateAirport = context.Destination;
        }
        else if (!string.IsNullOrWhiteSpace(context.Origin) && string.IsNullOrWhiteSpace(context.Destination))
        {
            gateRole = "Departure gate";
            gateAirport = context.Origin;
        }
        else if (!string.IsNullOrWhiteSpace(context.Destination) && string.IsNullOrWhiteSpace(context.Origin))
        {
            gateRole = "Arrival gate";
            gateAirport = context.Destination;
        }

        if (gateRole == null)
            return $"Assigned gate {gate}. Gate role unknown.";

        return string.IsNullOrWhiteSpace(gateAirport)
            ? $"{gateRole} {gate}."
            : $"{gateRole} {gate} at {gateAirport}.";
    }

    private static bool SameIcaoSi(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmptySi(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return null;
    }
}
