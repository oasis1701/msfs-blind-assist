// Tests for MainForm.BuildSayIntentionsDestinationCandidates — the ordered
// destination-candidate list the SayIntentions taxi-route import resolves
// against (MainForm.SayIntentions.cs).
//
// The load-bearing rule: on a KNOWN ARRIVAL — the airport being routed at IS
// flight_destination and SayIntentions has assigned an arrival gate — no
// runway may ever be offered as a taxi destination. Two live arrivals (KSTL
// 2026-08-15 "Gate A2" → "Runway 12R", KLAX 2026-08-19 "American Eagle
// Terminal Gate 52A" → "Runway 24R", twice each) were routed at the runway
// they had just landed on because the runway candidates outranked or
// outlived the gate: the clearance-runway candidate ran FIRST (an arrival
// clearance that names the landing runway outside a masked hold-short/cross
// span wins before the gate is consulted), and when the gate could not seat
// at all (neither scenery carries the stand under SayIntentions' label) the
// chain fell through to the flight-plan arrival runway. ATC does not taxi an
// arriving aircraft to a runway; an unresolvable gate must FAIL LOUDLY, not
// route somewhere plausible.

using MSFSBlindAssist.Services.SayIntentions;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsDestinationCandidatesTests
{
    private const string KlaxArrivalClearance =
        "American 123, runway 24R, exit right at Charlie 4, cross runway 24L, " +
        "taxi to the gate via Whiskey, Delta, Kilo, Bravo, Charlie 4.";

    private static SayIntentionsFlightContext KlaxArrivalContext() => new()
    {
        Destination = "KLAX",
        AssignedGate = "American Eagle Terminal Gate 52A",
        AssignedGatePosition = new GeoPoint(33.9414, -118.4013),
        DepartureRunway = "22R",
        ArrivalRunway = "24R"
    };

    [Fact]
    public void Known_arrival_offers_no_runway_candidate_at_all()
    {
        var (candidates, _) = MainForm.BuildSayIntentionsDestinationCandidates(
            KlaxArrivalContext(), parkingName: null, KlaxArrivalClearance, "KLAX");

        Assert.All(candidates, c => Assert.False(c.IsRunway));
    }

    [Fact]
    public void Known_arrival_ends_with_the_assigned_gate_carrying_its_position()
    {
        var (candidates, arrivalGate) = MainForm.BuildSayIntentionsDestinationCandidates(
            KlaxArrivalContext(), parkingName: null, KlaxArrivalClearance, "KLAX");

        var assigned = candidates[^1];
        Assert.Equal("American Eagle Terminal Gate 52A", assigned.Identifier);
        Assert.Equal(new GeoPoint(33.9414, -118.4013), assigned.Position);
        Assert.Equal("American Eagle Terminal Gate 52A", arrivalGate);
    }

    [Fact]
    public void Known_arrival_puts_the_clearance_gate_ahead_of_the_assigned_gate()
    {
        // The controller's own words outrank flight.json: a clearance naming a
        // specific gate is probed before the assigned-gate record.
        var (candidates, _) = MainForm.BuildSayIntentionsDestinationCandidates(
            KlaxArrivalContext(), parkingName: null,
            "Runway 12R, taxi to gate A2 via Lima, Delta, Charlie 5, Charlie.", "KLAX");

        Assert.Equal(2, candidates.Count);
        Assert.Equal("A2", candidates[0].Identifier);
        Assert.Equal("American Eagle Terminal Gate 52A", candidates[1].Identifier);
    }

    [Fact]
    public void Departure_keeps_the_runway_first_chain()
    {
        // The KLAS 2026-08-20 departure: destination is the NEXT airport, no
        // assigned gate — the pre-existing runway-first order must be untouched.
        var context = new SayIntentionsFlightContext
        {
            Destination = "KMDW",
            DepartureRunway = "26R",
            ArrivalRunway = "4R"
        };

        var (candidates, arrivalGate) = MainForm.BuildSayIntentionsDestinationCandidates(
            context, parkingName: null,
            "Runway 26R, taxi via Whiskey, Golf 1, Golf, Charlie, Bravo.", "KLAS");

        Assert.Null(arrivalGate);
        Assert.True(candidates[0].IsRunway);
        Assert.Equal("26R", candidates[0].Identifier);
        // Departure-runway and arrival-runway fallbacks stay in the chain.
        Assert.Contains(candidates, c => c.IsRunway && c.Identifier == "26R");
        Assert.Contains(candidates, c => c.IsRunway && c.Identifier == "4R");
    }

    [Fact]
    public void At_the_destination_without_an_assigned_gate_the_runway_chain_survives()
    {
        // Arrival airport but SayIntentions has not published a gate yet: nothing
        // marks this as a gate arrival, so the old chain stands (the pilot can
        // press again once the gate appears).
        var context = new SayIntentionsFlightContext
        {
            Destination = "KLAX",
            ArrivalRunway = "24R"
        };

        var (candidates, arrivalGate) = MainForm.BuildSayIntentionsDestinationCandidates(
            context, parkingName: null, KlaxArrivalClearance, "KLAX");

        Assert.Null(arrivalGate);
        Assert.Contains(candidates, c => c.IsRunway);
    }

    [Fact]
    public void The_getParking_fallback_name_also_marks_a_known_arrival()
    {
        var context = new SayIntentionsFlightContext { Destination = "KBOS" };

        var (candidates, arrivalGate) = MainForm.BuildSayIntentionsDestinationCandidates(
            context, parkingName: "Gate B5", "Continue taxi via Alpha, Kilo, Bravo.", "KBOS");

        Assert.Equal("Gate B5", arrivalGate);
        Assert.All(candidates, c => Assert.False(c.IsRunway));
        Assert.Equal("Gate B5", candidates[^1].Identifier);
    }

    [Fact]
    public void The_failure_message_names_the_gate_the_controller_said_when_it_differs()
    {
        // ATC revised the gate on the frequency ("taxi to gate B7") away from
        // flight.json's assigned "Gate A9". If neither seats, a message naming only
        // A9 sends the pilot against the controller's actual instruction.
        string msg = MainForm.ComposeUnresolvedArrivalGateMessage(
            "Terminal 1 Gate A9", "B7", "KBOS");

        Assert.Contains("B7", msg);
        Assert.Contains("Terminal 1 Gate A9", msg);
        Assert.Contains("KBOS", msg);
    }

    [Fact]
    public void The_failure_message_does_not_repeat_a_clearance_gate_that_is_the_assigned_one()
    {
        // "Gate A2" and the parsed clearance gate "A2" are the same stand spelled
        // two ways; naming it twice reads as two different gates.
        string msg = MainForm.ComposeUnresolvedArrivalGateMessage("Gate A2", "A2", "KSTL");

        Assert.Contains("Gate A2", msg);
        Assert.DoesNotContain("ATC named", msg);
    }

    [Fact]
    public void The_failure_message_without_a_clearance_gate_names_only_the_assigned_one()
    {
        string msg = MainForm.ComposeUnresolvedArrivalGateMessage(
            "American Eagle Terminal Gate 52A", null, "KLAX");

        Assert.Contains("American Eagle Terminal Gate 52A", msg);
        Assert.Contains("KLAX", msg);
    }

    [Fact]
    public void An_assigned_gate_at_another_airport_never_suppresses_the_runway_chain()
    {
        // The arrival stand belongs to flight_destination; at any other airport
        // it neither becomes a candidate nor demotes the runway candidates.
        var context = new SayIntentionsFlightContext
        {
            Destination = "KLAX",
            AssignedGate = "Gate 52A",
            DepartureRunway = "22R"
        };

        var (candidates, arrivalGate) = MainForm.BuildSayIntentionsDestinationCandidates(
            context, parkingName: null, "Runway 22R, taxi via Alpha.", "KJFK");

        Assert.Null(arrivalGate);
        Assert.True(candidates[0].IsRunway);
        Assert.DoesNotContain(candidates, c => c.Identifier == "Gate 52A");
    }
}
