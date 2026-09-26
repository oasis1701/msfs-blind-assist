using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

/// <summary>Which SayIntentions arrival gate the route briefing takes: THIS flight's only, the flight file's first, then
/// the parking service's, each with its own position.</summary>
public class SayIntentionsArrivalGateTests
{
    private static SayIntentionsFlightContext Ctx(bool exists, string? origin, string? dest, string? gate) => new()
    {
        FlightJsonExists = exists, Origin = origin, Destination = dest, AssignedGate = gate,
    };

    [Fact]
    public void No_flight_json_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(false, "EGLL", "KJFK", "Gate 6"), "EGLL", "KJFK"));

    [Fact]
    public void Null_context_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(null, "EGLL", "KJFK"));

    [Fact]
    public void Another_flight_s_gate_is_ignored()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KLAX", "Gate 6"), "EGLL", "KJFK"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "LMML", "KJFK", "Gate 6"), "EGLL", "KJFK"));
    }

    [Fact]
    public void Matching_flight_yields_the_hint()
    {
        var ctx = Ctx(true, "egll", "kjfk ", "Terminal 1 Gate 6");
        ctx.AssignedGatePosition = new GeoPoint(40.64, -73.78);
        var hint = SayIntentionsArrivalGate.From(ctx, "EGLL", "KJFK")!;

        Assert.Equal("Terminal 1 Gate 6", hint.Label);
        Assert.Equal(40.64, hint.Position!.Value.Latitude);
    }

    [Fact]
    public void Blank_gate_means_no_hint()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KJFK", " "), "EGLL", "KJFK"));

    [Fact]
    public void The_assigned_gate_label_is_trimmed()
        => Assert.Equal("Gate 6", SayIntentionsArrivalGate.From(Ctx(true, "EGLL", "KJFK", "  Gate 6 \t"), "EGLL", "KJFK")!.Label);

    // ── SayIntentionsArrivalGate: the parking-service fallback ─────────────────────────────

    private static SayIntentionsParking Parking(string? name, double? lat = null, double? lon = null) =>
        new() { Name = name, Latitude = lat, Longitude = lon };

    [Fact]
    public void The_flight_file_gate_wins_over_the_parking_service()
    {
        var ctx = Ctx(true, "KMEM", "KATL", "Terminal 1 Gate 17");
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);
        var hint = SayIntentionsArrivalGate.From(ctx, Parking("B 12", 35.04, -89.98), "KMEM", "KATL")!;

        Assert.Equal("Terminal 1 Gate 17", hint.Label);
        Assert.Equal(33.64, hint.Position!.Value.Latitude);
        Assert.Equal(SayIntentionsGateSource.FlightFile, hint.Source);
    }

    [Fact]
    public void With_no_gate_in_the_file_the_parking_service_gate_is_used_with_its_own_position()
    {
        // Live KMEM→KATL (2026-09-26): flight.json's assigned_gate was empty while SayIntentions showed a concourse B
        // arrival gate, served only by getParking — which MSFS Blind Assist's SayIntentions window already fell back to.
        var hint = SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", ""), Parking(" B 12 ", 33.6407, -84.4277), "KMEM", "KATL")!;

        Assert.Equal("B 12", hint.Label);
        Assert.Equal(new GeoPoint(33.6407, -84.4277), hint.Position!.Value);
        Assert.Equal(SayIntentionsGateSource.ParkingService, hint.Source);
    }

    [Fact]
    public void A_parking_service_gate_never_takes_the_file_s_position()
    {
        var ctx = Ctx(true, "KMEM", "KATL", null);
        ctx.AssignedGatePosition = new GeoPoint(33.64, -84.43);   // a stray coordinate with no gate name beside it
        Assert.Null(SayIntentionsArrivalGate.From(ctx, Parking("B 12"), "KMEM", "KATL")!.Position);
    }

    [Fact]
    public void A_parking_service_position_at_zero_zero_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 0, 0), "KMEM", "KATL")!.Position);

    [Fact]
    public void A_parking_service_position_missing_a_coordinate_is_no_position()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.64, null), "KMEM", "KATL")!.Position);

    [Fact]
    public void No_gate_anywhere_means_no_hint()
    {
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", null), null, "KMEM", "KATL"));
        Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KATL", " "), Parking("  "), "KMEM", "KATL"));
    }

    [Fact]
    public void Another_flight_s_parking_gate_is_ignored()
        => Assert.Null(SayIntentionsArrivalGate.From(Ctx(true, "KMEM", "KDFW", null), Parking("B 12"), "KMEM", "KATL"));

    [Fact]
    public void The_status_overload_reads_the_file_and_the_parking_service()
    {
        var status = new SayIntentionsStatusResult(Ctx(true, "KMEM", "KATL", null), Parking("B 12", 33.6407, -84.4277), null);
        Assert.Equal("B 12", SayIntentionsArrivalGate.FromStatus(status, "KMEM", "KATL")!.Label);
        Assert.Null(SayIntentionsArrivalGate.FromStatus(null, "KMEM", "KATL"));
    }

    [Fact]
    public void This_flight_means_the_file_exists_and_both_airports_match()
    {
        Assert.True(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "kmem ", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(false, "KMEM", "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, "KMEM", "KDFW", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(Ctx(true, null, "KATL", null), "KMEM", "KATL"));
        Assert.False(SayIntentionsArrivalGate.IsThisFlight(null, "KMEM", "KATL"));
    }
}
