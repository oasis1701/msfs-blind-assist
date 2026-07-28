// Characterization tests for reading %LOCALAPPDATA%\SayIntentionsAI\flight.json.
//
// The file is written by another process while we read it, so the reader opens
// with FileShare.ReadWrite | FileShare.Delete and treats every malformed or
// missing case as a spoken error string rather than an exception — a blind
// pilot pressing Ctrl+S must always hear something actionable.
//
// SayIntentions writes a different subset of fields per flight phase, so every
// field is optional and a missing one degrades to "not available".

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsFlightContextTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "si-tests-" + Guid.NewGuid().ToString("N"));

    private SayIntentionsService ServiceFor(string json)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "flight.json");
        File.WriteAllText(path, json);
        return new SayIntentionsService(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingFileIsReportedNotThrown()
    {
        var service = new SayIntentionsService(Path.Combine(_dir, "does-not-exist.json"));
        var context = service.ReadFlightContext();
        Assert.False(context.FlightJsonExists);
        Assert.Null(context.Error);
    }

    [Fact]
    public void MalformedJsonBecomesASpokenError()
    {
        var context = ServiceFor("{ not json").ReadFlightContext();
        Assert.NotNull(context.Error);
        Assert.Contains("malformed", context.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FlightDetailsAreExtracted()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "api_key": "KEY123",
            "hostname": "https://apipri.sayintentions.ai",
            "current_airport": "CYYZ",
            "cleared_for_takeoff": "15L",
            "current_flight": {
              "flight_origin": "CYYZ",
              "flight_destination": "KBOS",
              "assigned_gate": "A9",
              "flight_plan_departing_runway": "5"
            }
          }
        }
        """).ReadFlightContext();

        Assert.Equal("KEY123", context.ApiKey);
        Assert.Equal("CYYZ", context.CurrentAirport);
        Assert.Equal("KBOS", context.Destination);
        Assert.Equal("A9", context.AssignedGate);
        Assert.Equal("15L", context.ClearedForTakeoff);
        Assert.Equal("05", context.DepartureRunway);   // zero-padded by CleanRunway
    }

    [Fact]
    public void LatestRadioTransmissionWinsOverCabinChatter()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 1, "channel": "COM1", "incoming_message": "Taxi to runway 15L via Alpha" },
              { "id": 2, "channel": "PA", "outgoing_message": "Cabin crew, prepare for departure" }
            ]
          }
        }
        """).ReadFlightContext();

        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Contains("Taxi to runway", context.LastFlightJsonTransmission!.Message);
    }

    [Fact]
    public void TaxiPathArrayIsRead()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "current_flight": { "taxi_path": ["AT", "R", "B"] }
          }
        }
        """).ReadFlightContext();

        Assert.Equal(new[] { "AT", "R", "B" }, context.TaxiwaySequence);
    }

    [Fact]
    public void EmptyFlightDetailsYieldNoErrorAndNoData()
    {
        var context = ServiceFor("""{ "flight_details": {} }""").ReadFlightContext();
        Assert.Null(context.Error);
        Assert.Null(context.CurrentAirport);
        Assert.Empty(context.TaxiwaySequence);
    }
}
