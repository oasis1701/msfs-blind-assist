using System.Diagnostics.CodeAnalysis;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>
/// Turns SayIntentions' state into the briefing's arrival-gate hint, or null. SayIntentions assigns an ARRIVAL gate
/// only, so the hint is offered only when SayIntentions' flight is THIS OFP's flight (<see cref="IsThisFlight"/>) and a
/// gate is set. The gate is the flight file's <c>assigned_gate</c>, with the file's position; when the file has none,
/// the parking service's answer with ITS position — the order MSFS Blind Assist's SayIntentions window uses
/// (<c>SayIntentionsService.GetAssignedStatusAsync</c>), so the two never disagree. A name is never paired with the
/// other source's position. No timestamp is consulted (a stale file for the same city pair is accepted and labelled
/// as SayIntentions' assignment).
/// </summary>
public static class SayIntentionsArrivalGate
{
    /// <summary>SayIntentions is flying this flight: its file exists and its origin and destination are the flight
    /// plan's. The briefing's one test before it takes anything from SayIntentions — the gate and the runways alike.</summary>
    public static bool IsThisFlight([NotNullWhen(true)] SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        ctx != null && ctx.FlightJsonExists &&
        IcaoEquals(ctx.Origin, departureIcao) && IcaoEquals(ctx.Destination, arrivalIcao);

    /// <summary>The flight file's gate only.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, string departureIcao, string arrivalIcao) =>
        From(ctx, null, departureIcao, arrivalIcao);

    /// <summary>The flight file's gate, else the parking service's.</summary>
    public static SayIntentionsGateHint? From(SayIntentionsFlightContext? ctx, SayIntentionsParking? parking,
                                              string departureIcao, string arrivalIcao)
    {
        if (!IsThisFlight(ctx, departureIcao, arrivalIcao)) return null;
        if (!string.IsNullOrWhiteSpace(ctx.AssignedGate))
            return new SayIntentionsGateHint(ctx.AssignedGate.Trim(), ctx.AssignedGatePosition);
        if (parking == null || string.IsNullOrWhiteSpace(parking.Name)) return null;
        // (0, 0) is what two absent numbers look like once read as zero — the flight-file reader refuses it too.
        GeoPoint? position = parking.Latitude is double lat && parking.Longitude is double lon && (lat != 0 || lon != 0)
            ? new GeoPoint(lat, lon)
            : null;
        return new SayIntentionsGateHint(parking.Name.Trim(), position, SayIntentionsGateSource.ParkingService);
    }

    /// <summary>What <c>SayIntentionsService.GetAssignedStatusAsync</c> returned: the flight file and, when the file had
    /// no gate, the parking service.</summary>
    public static SayIntentionsGateHint? FromStatus(SayIntentionsStatusResult? status, string departureIcao, string arrivalIcao) =>
        status == null ? null : From(status.Context, status.Parking, departureIcao, arrivalIcao);

    private static bool IcaoEquals(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
