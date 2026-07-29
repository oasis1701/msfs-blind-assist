// Characterization against REAL SayIntentions traffic, captured 2026-07-28 from a
// live arrival at EDDF (LMML -> EDDF, landed 07L, taxiing to Terminal 3 Gate J1).
//
// Everything in here is verbatim from the SAPI getCommsHistory feed and the
// local flight.json. The earlier SayIntentions tests were written against a
// GUESSED schema; these are the wire format. Where the two disagree, this file
// is right.
//
// The clearance is the interesting one because it exercises, in a single real
// string, four things that were separately broken at some point: a gate
// destination whose clearance also names a runway to hold short of, taxiway
// designators spoken digit-by-digit ("November-1-1" = N11), a taxiway that is a
// strict prefix of another in the same clearance (Papa-8 then Papa; Lima then
// Lima-1-7), and a written zero-padded hold-short runway.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsLiveClearanceTests
{
    // Frankfurt Ground, 2026-07-28 22:33:18Z, comm id 51683714.
    private const string EddfTaxiClearance =
        "Taxi to Terminal 3 Gate J1 via Papa-8, Papa, November-1-1, Lima, Lima-1-7, hold short of runway 07C.";

    // The taxiways this clearance names, as navdata spells them.
    private static readonly string[] EddfTaxiways =
        { "P8", "P", "N11", "N", "L", "L17", "L1", "M", "A", "S" };

    [Fact]
    public void TheGateIsTheDestinationNotTheHoldShortRunway()
    {
        // The whole reason this rework exists: "hold short of runway 07C" must not
        // become the place we route an aircraft that was cleared to a gate.
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(EddfTaxiClearance));
        Assert.Equal("J1", SayIntentionsClearanceParser.ParseDestinationGate(EddfTaxiClearance));
        Assert.Equal("07C", SayIntentionsClearanceParser.ParseHoldShortRunway(EddfTaxiClearance));
    }

    [Fact]
    public void TheFullTaxiwaySequenceSurvives()
    {
        // Digit-by-digit designators ("November-1-1") and prefix collisions
        // ("Papa-8" before "Papa", "Lima" before "Lima-1-7") both resolve whole.
        Assert.Equal(
            new[] { "P8", "P", "N11", "L", "L17" },
            SayIntentionsClearanceParser.ParseTaxiways(EddfTaxiClearance, EddfTaxiways));
    }

    [Fact]
    public void NothingIsReportedMissingFromACleanParse()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(EddfTaxiClearance, EddfTaxiways);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ItIsRecognizedAsATaxiClearance()
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(EddfTaxiClearance));
    }

    // Frankfurt Tower, same session. Neither of these may ever be mistaken for a
    // taxi clearance — the landing one names a runway and would otherwise route
    // the aircraft back onto it.
    [Theory]
    [InlineData("07L, cleared to land")]
    [InlineData("All aircraft be advised, information Juliet is now current. QNH 1020.")]
    public void NonTaxiTransmissionsAreRejectedAsClearances(string message)
    {
        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(message));
    }

    // "Welcome to Frankfurt. Exit at Papa-eight if able. Contact ground on 121.805."
    // DOES contain "taxi"-free routing language and a phonetic taxiway, but has no
    // "via", so it yields no route rather than a bogus one-taxiway route.
    [Fact]
    public void ATowerExitSuggestionYieldsNoRoute()
    {
        const string towerExit =
            "Welcome to Frankfurt. Exit at Papa-eight if able. Contact ground on 121.805.";
        Assert.Empty(SayIntentionsClearanceParser.ParseTaxiways(towerExit, EddfTaxiways));
    }

    // flight.json's assigned_gate at EDDF is the full label "Terminal 3 Gate J1",
    // not the bare stand id. Normalizing it has to reach the same token the
    // clearance does, or the assigned gate can never match a navdata parking spot
    // and destination resolution falls through to a RUNWAY.
    [Fact]
    public void TheAssignedGateLabelNormalizesToTheStandId()
    {
        Assert.Equal(
            SayIntentionsClearanceParser.NormalizeParkingName("J1"),
            SayIntentionsClearanceParser.NormalizeParkingName("Terminal 3 Gate J1"));
    }

    // Both departure-runway candidates go stale on arrival: the live EDDF capture
    // held "5" from the LMML departure (EDDF has no 05) and `runway` held 07L, the
    // runway just LANDED on. Speaking either as "Departure runway" at the
    // destination is wrong twice over.
    [Fact]
    public void NoDepartureRunwayIsSpokenOnceArrived()
    {
        var arrived = new SayIntentionsFlightContext
        {
            CurrentAirport = "EDDF",
            Origin = "LMML",
            Destination = "EDDF",
            DepartureRunway = "05",
            Runway = "07L"
        };

        Assert.Null(MainForm.ResolveDepartureRunwayForStatus(arrived));
    }

    [Fact]
    public void TheDepartureRunwayIsStillSpokenBeforeDeparting()
    {
        var departing = new SayIntentionsFlightContext
        {
            CurrentAirport = "LMML",
            Origin = "LMML",
            Destination = "EDDF",
            DepartureRunway = "05"
        };

        Assert.Equal("05", MainForm.ResolveDepartureRunwayForStatus(departing));
    }

    // SayIntentions never assigns a DEPARTURE gate (confirmed by an SI developer), so
    // assigned_gate always names a stand at flight_destination. The readout used to
    // infer the role from where the aircraft was standing, which made it announce the
    // arrival stand as "Departure gate ... at <origin>" for the whole outbound leg —
    // a stand at an airport the pilot had not flown to yet, named as if it were under
    // their wheels. The live capture could not catch this: it was taken at EDDF, the
    // destination, where the two readings coincide.
    [Fact]
    public void TheAssignedGateIsAnnouncedAsAnArrivalGateBeforeDeparting()
    {
        var departing = new SayIntentionsFlightContext
        {
            CurrentAirport = "LMML",
            Origin = "LMML",
            Destination = "EDDF"
        };

        string spoken = MainForm.FormatSayIntentionsGateStatus(departing, "Terminal 3 Gate J1");

        Assert.Equal("Arrival gate Terminal 3 Gate J1 at EDDF.", spoken);
        Assert.DoesNotContain("Departure", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LMML", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheAssignedGateIsStillAnArrivalGateOnceArrived()
    {
        var arrived = new SayIntentionsFlightContext
        {
            CurrentAirport = "EDDF",
            Origin = "LMML",
            Destination = "EDDF"
        };

        Assert.Equal(
            "Arrival gate Terminal 3 Gate J1 at EDDF.",
            MainForm.FormatSayIntentionsGateStatus(arrived, "Terminal 3 Gate J1"));
    }

    // With no filed destination there is no airport to attach the stand to, but the
    // ROLE is still known — it is the one kind of gate SayIntentions assigns. The old
    // "Gate role unknown" wording was only ever a symptom of guessing the role.
    [Fact]
    public void TheGateRoleIsNeverUnknown()
    {
        var noFlightPlan = new SayIntentionsFlightContext { CurrentAirport = "LMML" };

        Assert.Equal("Arrival gate J1.", MainForm.FormatSayIntentionsGateStatus(noFlightPlan, "J1"));
    }
}

// The local flight.json from the SAME live capture, reduced to its shape. Field
// VALUES are verbatim (the api_key is a placeholder — the real one is never
// committed); the taxi_path array is cut from ~200 entries to three, which is
// enough to show what they are.
//
// This pins three things the earlier, guessed fixtures got wrong, and one they
// invented:
//   - assigned_gate is the full label, not a stand id.
//   - flight_plan_departing_runway is STALE. The aircraft is on the ground at EDDF
//     after landing, and the field still holds "5" from the LMML departure — EDDF
//     has no runway 05. It sits in the destination-resolution chain, so it must
//     never be reached ahead of a gate that resolves.
//   - flight.json carries NO clearance text and NO comms, so ClearanceText is null
//     and the taxi import has to fetch the clearance over the API every time.
//   - current_flight.taxi_path is GEOMETRY. There is no taxiway name anywhere in
//     it, so nothing reads it: SayIntentionsFlightContext carries no taxiway
//     sequence at all, and that guarantee is now structural rather than asserted.
public class SayIntentionsLiveFlightJsonTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "si-live-" + Guid.NewGuid().ToString("N"));

    private const string EddfFlightJson = """
    {
      "flight_details": {
        "api_key": "PLACEHOLDER",
        "hostname": "https://apipri.sayintentions.ai",
        "current_airport": "EDDF",
        "runway": "7L",
        "current_flight": {
          "flight_origin": "LMML",
          "flight_destination": "EDDF",
          "assigned_gate": "Terminal 3 Gate J1",
          "flight_plan_departing_runway": "5",
          "flight_plan_arriving_runway": "7L",
          "taxi_path": [
            { "heading": 93.92, "point": { "lon": 8.52, "lat": 50.04 } },
            { "heading": 93.88, "point": { "lon": 8.53, "lat": 50.04 } },
            { "heading": 94.01, "point": { "lon": 8.54, "lat": 50.04 } }
          ]
        }
      }
    }
    """;

    private SayIntentionsFlightContext ReadLiveContext()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "flight.json");
        File.WriteAllText(path, EddfFlightJson);
        return new SayIntentionsService(path).ReadFlightContext();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheLiveFieldsReadBackAsCaptured()
    {
        var context = ReadLiveContext();

        Assert.Null(context.Error);
        Assert.Equal("EDDF", context.CurrentAirport);
        Assert.Equal("LMML", context.Origin);
        Assert.Equal("EDDF", context.Destination);
        Assert.Equal("Terminal 3 Gate J1", context.AssignedGate);
        Assert.Equal("07L", context.ArrivalRunway);
        Assert.Equal("07L", context.Runway);
    }

    [Fact]
    public void TheDepartingRunwayIsStaleFromThePreviousLeg()
    {
        // Not a parse bug: SayIntentions really does leave the departure airport's
        // runway in place after arrival. EDDF has no 05 — the aircraft landed on 07L.
        var context = ReadLiveContext();

        Assert.Equal("05", context.DepartureRunway);
        Assert.NotEqual(context.DepartureRunway, context.ArrivalRunway);
    }

    [Fact]
    public void TheAssignedGateIsTheFullLabelAndStillReachesTheStand()
    {
        var context = ReadLiveContext();

        Assert.Equal(
            SayIntentionsClearanceParser.NormalizeParkingName("J1"),
            SayIntentionsClearanceParser.NormalizeParkingName(context.AssignedGate));
    }

    // The reason Alt+Shift+S always needs the network: there is nothing here to
    // parse a clearance out of, so the import falls through to getCommsHistory.
    [Fact]
    public void FlightJsonCarriesNoClearanceAndNoTransmission()
    {
        var context = ReadLiveContext();

        Assert.Null(context.ClearanceText);
        Assert.Null(context.LastFlightJsonTransmission);
    }
}
