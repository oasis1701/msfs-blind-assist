// Characterization tests for reading %LOCALAPPDATA%\SayIntentionsAI\flight.json.
//
// The file is written by another process while we read it, so the reader opens
// with FileShare.ReadWrite | FileShare.Delete and treats every malformed or
// missing case as a spoken error string rather than an exception — a blind
// pilot pressing Ctrl+S must always hear something actionable.
//
// SayIntentions writes a different subset of fields per flight phase, so every
// field is optional and a missing one degrades to "not available".

using System.Globalization;
using System.Text.Json;
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
    public void EmptyFlightDetailsYieldNoErrorAndNoData()
    {
        var context = ServiceFor("""{ "flight_details": {} }""").ReadFlightContext();
        Assert.Null(context.Error);
        Assert.Null(context.CurrentAirport);
        Assert.Null(context.ClearanceText);
    }

    // One comms record carries BOTH sides of the exchange under one stamp and one id.
    //
    // DIRECTION IS FROM SAYINTENTIONS' POINT OF VIEW, NOT THE PILOT'S:
    // incoming_message is what SI RECEIVED (the pilot speaking) and outgoing_message
    // is what SI SENT (ATC). Verified against a live EDDF session — every turn pair
    // reads incoming "Request taxi" / outgoing "Taxi to Terminal 3 Gate J1 via …".
    // The intuitive reading is backwards and made Ctrl+S announce the pilot's own
    // readback as ATC.
    //
    // "Read the last transmission" must give the pilot the ATC call — their own
    // readback only repeats what they just said themselves.
    [Fact]
    public void AtcCallWinsOverThePilotReadbackInTheSameRecord()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              {
                "id": 7,
                "channel": "COM1",
                "stamp_zulu": "2026-03-04T12:00:00Z",
                "incoming_message": "Cleared to land runway 15L, wilco",
                "outgoing_message": "Cleared to land runway 15L"
              }
            ]
          }
        }
        """).ReadFlightContext();

        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Equal("ATC", context.LastFlightJsonTransmission!.Speaker);
        Assert.Equal("Cleared to land runway 15L", context.LastFlightJsonTransmission.Message);
    }

    // ...but only WITHIN one record. A pilot transmission in a later record is
    // genuinely the last thing said and must still win.
    [Fact]
    public void ALaterPilotTransmissionStillWinsAcrossRecords()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 1, "channel": "COM1", "outgoing_message": "Cleared to land runway 15L" },
              { "id": 2, "channel": "COM1", "incoming_message": "Tower, request taxi to the gate" }
            ]
          }
        }
        """).ReadFlightContext();

        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Equal("Pilot", context.LastFlightJsonTransmission!.Speaker);
    }

    // stamp_zulu is a UTC wire format, not a locale-formatted date. Parsing it with
    // the current culture reorders the history on a d/M/y machine and speaks the
    // wrong "last transmission" — so the parse is pinned to InvariantCulture with
    // AssumeUniversal, and this test proves it by forcing a d/M/y culture.
    [Fact]
    public void TimestampsAreParsedCultureIndependently()
    {
        var service = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 2, "channel": "COM1", "stamp_zulu": "01/02/2026 10:00:00",
                "incoming_message": "Contact ground on one two one point niner" },
              { "id": 1, "channel": "COM1", "stamp_zulu": "02/01/2026 09:00:00",
                "incoming_message": "Taxi to runway 15L via Alpha" }
            ]
          }
        }
        """);

        var previous = CultureInfo.CurrentCulture;
        SayIntentionsFlightContext context;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            context = service.ReadFlightContext();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Invariant reading: 01/02 is 2 January, 02/01 is 1 February — the later one.
        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Contains("Taxi to runway", context.LastFlightJsonTransmission!.Message);
        Assert.Equal(
            new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc),
            context.LastFlightJsonTransmission.StampZulu);
    }
}

// Characterization tests for the SAPI error envelope.
//
// SayIntentions answers both endpoints with a JSON object that may or may not
// carry an "error" member, and the member's VALUE decides — a success response
// legitimately sends {"error": false} or {"error": null}. Keying on the member's
// mere presence spoke "SayIntentions comms history unavailable. false." over a
// perfectly good response, and a bare {"message": "Invalid API key"} (no "error"
// member at all) was read as success, hiding the real reason behind the generic
// "no communication history found".
public class SayIntentionsApiErrorTests
{
    private static bool TryGetError(string json, out string? error)
    {
        using var doc = JsonDocument.Parse(json);
        return SayIntentionsService.TryGetApiError(doc.RootElement, out error);
    }

    [Theory]
    [InlineData("""{ "error": false, "message": "OK" }""")]
    [InlineData("""{ "error": null, "message": "3 records" }""")]
    [InlineData("""{ "error": 0, "message": "OK" }""")]
    [InlineData("""{ "error": "", "message": "OK" }""")]
    [InlineData("""{ "error": "   " }""")]
    [InlineData("""{ "message": "3 records returned" }""")]
    [InlineData("""{ "comms": [] }""")]
    [InlineData("[]")]
    public void SuccessShapesAreNotErrors(string json)
    {
        Assert.False(TryGetError(json, out string? error));
        Assert.Null(error);
    }

    [Fact]
    public void AStringErrorMemberIsTheReason()
    {
        Assert.True(TryGetError("""{ "error": "Invalid API key" }""", out string? error));
        Assert.Equal("Invalid API key", error);
    }

    // The presence lookup is case-insensitive to match the value lookup: PR #86
    // mixed a case-insensitive read with a case-sensitive TryGetProperty, so a
    // payload using "Error" silently parsed as success.
    [Fact]
    public void ErrorMemberLookupIsCaseInsensitive()
    {
        Assert.True(TryGetError("""{ "Error": "Invalid API key" }""", out string? error));
        Assert.Equal("Invalid API key", error);
    }

    // "true" and "1" are not something a pilot can act on — the sibling message is.
    [Theory]
    [InlineData("""{ "error": true, "message": "Rate limit exceeded" }""")]
    [InlineData("""{ "error": 1, "message": "Rate limit exceeded" }""")]
    public void ATruthyFlagBorrowsTheSiblingMessage(string json)
    {
        Assert.True(TryGetError(json, out string? error));
        Assert.Equal("Rate limit exceeded", error);
    }

    [Fact]
    public void ATruthyFlagWithNoMessageStillErrorsWithNoReason()
    {
        Assert.True(TryGetError("""{ "error": true }""", out string? error));
        Assert.Null(error);
    }

    // A "message" sitting next to a real payload is a status line, not a rejection —
    // reporting an error there would throw away a response that carries the answer.
    [Fact]
    public void AnErrorShapedMessageAlongsideAPayloadIsNotAnError()
    {
        Assert.False(TryGetError(
            """{ "parking": { "name": "A9" }, "message": "no gate found nearby" }""",
            out string? error));
        Assert.Null(error);
    }

    // No "error" member at all, but the payload is plainly a rejection: the real
    // reason must reach the pilot instead of the generic "no history found".
    [Theory]
    [InlineData("""{ "message": "Invalid API key" }""", "Invalid API key")]
    [InlineData("""{ "message": "Unauthorized" }""", "Unauthorized")]
    [InlineData("""{ "message": "API key required" }""", "API key required")]
    public void AnErrorShapedMessageWithNoErrorMemberIsAnError(string json, string expected)
    {
        Assert.True(TryGetError(json, out string? error));
        Assert.Equal(expected, error);
    }
}
