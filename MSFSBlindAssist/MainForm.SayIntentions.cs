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

            var taxiways = context.TaxiwaySequence.Count > 0
                ? MatchKnownTaxiways(context.TaxiwaySequence, knownTaxiways)
                : SayIntentionsClearanceParser.ParseTaxiways(clearance, knownTaxiways);

            string? holdShort = SayIntentionsClearanceParser.ParseHoldShortRunway(clearance);
            bool autoStart = SettingsManager.Current.SayIntentionsAutoStartTaxiGuidance;

            // Show BEFORE announcing so the screen reader's own form-focus
            // announcement does not collide with the route summary.
            form.Show();
            form.BringToFront();
            var outcome = form.ApplyExternalRoute(isRunway, label, taxiways, holdShort, autoStart);

            _siLog.Info($"{icao} dest='{label}' runway={isRunway} " +
                        $"applied=[{string.Join(",", outcome.AppliedTaxiways)}] " +
                        $"skipped=[{string.Join(",", outcome.SkippedTaxiways)}] " +
                        $"holdShort={holdShort ?? "none"} autoStart={autoStart}");

            announcer.Announce(BuildExternalRouteAnnouncement(outcome, label, autoStart));
        }
        catch (Exception ex)
        {
            _siLog.Error("Taxi route build failed", ex);
            announcer.AnnounceImmediate($"SayIntentions taxi route failed. {ex.Message}");
        }
    }

    /// <summary>Names every taxiway that could not be applied. Silent degradation to
    /// a shortest-path route is invisible to a blind pilot.</summary>
    private static string BuildExternalRouteAnnouncement(
        TaxiAssistForm.ExternalRouteOutcome outcome, string destination, bool autoStart)
    {
        var parts = new List<string> { $"SayIntentions route to {destination}." };

        parts.Add(outcome.AppliedTaxiways.Count > 0
            ? $"Via {string.Join(", ", outcome.AppliedTaxiways)}."
            : "No taxiways from the clearance matched this airport. Using shortest path.");

        if (outcome.SkippedTaxiways.Count > 0)
            parts.Add($"Could not apply {string.Join(", ", outcome.SkippedTaxiways)}.");

        parts.Add(autoStart
            ? "Guidance started."
            : "Review the fields, then press Calculate Route to start guidance.");

        return string.Join(" ", parts);
    }

    /// <summary>Destination priority: the clearance's own runway, then its gate, then
    /// the assigned gate when already at the destination airport, then the departure
    /// runway, then the assigned gate, then the arrival runway. Each candidate must
    /// resolve to a real entry in the form's destination list to win.</summary>
    private static bool TryResolveSayIntentionsDestination(
        TaxiAssistForm form, SayIntentionsStatusResult status, string clearance,
        out bool isRunway, out string label)
    {
        var context = status.Context;

        string? clearanceRunway = SayIntentionsClearanceParser.ParseDestinationRunway(clearance);
        if (TryDestination(form, true, clearanceRunway, out label)) { isRunway = true; return true; }

        string? clearanceGate = SayIntentionsClearanceParser.ParseDestinationGate(clearance);
        if (TryDestination(form, false, clearanceGate, out label)) { isRunway = false; return true; }

        string? gate = FirstNonEmptySi(context.AssignedGate, status.Parking?.Name);
        bool atDestination = !string.IsNullOrWhiteSpace(context.CurrentAirport)
            && !string.IsNullOrWhiteSpace(context.Destination)
            && context.CurrentAirport.Equals(context.Destination, StringComparison.OrdinalIgnoreCase);

        if (atDestination && TryDestination(form, false, gate, out label)) { isRunway = false; return true; }

        string? departureRunway = FirstNonEmptySi(
            context.ClearedForTakeoff, context.DepartureRunway, context.Runway);
        if (TryDestination(form, true, departureRunway, out label)) { isRunway = true; return true; }

        if (TryDestination(form, false, gate, out label)) { isRunway = false; return true; }

        string? arrivalRunway = FirstNonEmptySi(context.ClearedForLanding, context.ArrivalRunway);
        if (TryDestination(form, true, arrivalRunway, out label)) { isRunway = true; return true; }

        isRunway = false;
        label = "";
        return false;
    }

    private static bool TryDestination(TaxiAssistForm form, bool isRunway, string? identifier, out string label)
    {
        label = "";
        return !string.IsNullOrWhiteSpace(identifier)
            && form.TryResolveExternalDestination(isRunway, identifier, out label);
    }

    /// <summary>Maps SayIntentions' structured taxi_path onto the airport's real
    /// taxiway names, dropping anything the graph does not have.</summary>
    private static List<string> MatchKnownTaxiways(
        IReadOnlyList<string> wanted, IReadOnlyList<string> knownTaxiways)
    {
        var resolved = new List<string>();
        foreach (string value in wanted)
        {
            string normalized = SayIntentionsClearanceParser.NormalizeTaxiwayName(value);
            string? match = knownTaxiways.FirstOrDefault(t =>
                SayIntentionsClearanceParser.NormalizeTaxiwayName(t)
                    .Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                resolved.Add(match);
        }

        return SayIntentionsClearanceParser.CollapseConsecutive(resolved);
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
