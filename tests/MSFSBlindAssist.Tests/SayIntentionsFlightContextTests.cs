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

    // outgoing_message throughout: that is the ATC side (see the direction note below),
    // and only ATC transmissions are eligible for this readout at all.
    [Fact]
    public void LatestRadioTransmissionWinsOverCabinChatter()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 1, "channel": "COM1", "outgoing_message": "Taxi to runway 15L via Alpha" },
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

    // ...and ACROSS records too. Preferring ATC only within one record still let a
    // pilot transmission in a LATER record win — and a readback is normally the
    // newest thing on the frequency at exactly the moment someone presses the key,
    // so the readout announced the pilot their own words back, prefixed "Pilot:".
    // A Pilot-speaker transmission is now dropped outright, never merely outranked.
    [Fact]
    public void ALaterPilotTransmissionNeverWins()
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
        Assert.Equal("ATC", context.LastFlightJsonTransmission!.Speaker);
        Assert.Equal("Cleared to land runway 15L", context.LastFlightJsonTransmission.Message);
    }

    // The live shape: the controller clears the taxi, the pilot reads it back, and the
    // readback carries the newer stamp. What the pilot needs to hear is the clearance,
    // not their own recital of it — so the ATC call BEFORE the readback is returned.
    [Fact]
    public void TheAtcCallBeforeAPilotReadbackIsWhatIsReturned()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 11, "channel": "COM1", "stamp_zulu": "2026-03-04T12:00:00Z",
                "outgoing_message": "Taxi to Terminal 3 Gate J1 via November, hold short of runway 18" },
              { "id": 12, "channel": "COM1", "stamp_zulu": "2026-03-04T12:00:12Z",
                "incoming_message": "November, holding short of runway 18, Speedbird 12" }
            ]
          }
        }
        """).ReadFlightContext();

        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Equal("ATC", context.LastFlightJsonTransmission!.Speaker);
        Assert.StartsWith("Taxi to Terminal 3 Gate J1", context.LastFlightJsonTransmission.Message);
    }

    // A message with no direction at all comes from the bare-"message" fallback, so
    // it is NOT identified as the pilot and stays eligible. Dropping it would leave a
    // payload shape we cannot classify silent, and for a readout whose whole job is
    // to say what was heard, silence is the worse failure. It also cannot be mistaken
    // for the pilot when spoken — with no speaker, ToAnnouncement prefixes nothing.
    [Fact]
    public void ATransmissionWithNoSpeakerIsStillEligible()
    {
        var context = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 1, "channel": "COM1", "outgoing_message": "Cleared to land runway 15L" },
              { "id": 2, "channel": "COM1", "message": "Contact ground on one two one point niner" }
            ]
          }
        }
        """).ReadFlightContext();

        Assert.NotNull(context.LastFlightJsonTransmission);
        Assert.Equal("", context.LastFlightJsonTransmission!.Speaker);
        Assert.Contains("Contact ground", context.LastFlightJsonTransmission.Message);
    }

    // Nothing but the pilot's own calls: there is no ATC transmission to speak, and
    // the pilot must be told that rather than left with silence or a misleading
    // "nothing found" — they DID hear something, it just was not the controller.
    [Fact]
    public async Task AHistoryOfOnlyPilotTransmissionsIsSaidOutLoud()
    {
        var service = ServiceFor("""
        {
          "flight_details": {
            "comms": [
              { "id": 1, "channel": "COM1", "incoming_message": "Ground, Speedbird 12, request taxi" },
              { "id": 2, "channel": "COM1", "incoming_message": "Holding short of runway 18, Speedbird 12" }
            ]
          }
        }
        """);

        var result = await service.GetLastTransmissionAsync();

        Assert.Null(result.Transmission);
        Assert.Equal("No ATC transmission yet. Only your own calls so far.", result.Error);
    }

    // No transmissions of any kind is a different failure and must not borrow the
    // pilot-only wording: nothing was heard at all.
    [Fact]
    public async Task AnEmptyFlightJsonDoesNotClaimTheHistoryWasAllPilot()
    {
        var service = ServiceFor("""{ "flight_details": {} }""");

        var result = await service.GetLastTransmissionAsync();

        Assert.Null(result.Transmission);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("your own calls", result.Error!);
    }

    // The API key setting is gone — the key always comes from flight.json — so no
    // error may send the pilot looking for a settings field that no longer exists.
    [Theory]
    [InlineData("""{ "flight_details": {} }""")]
    [InlineData("""{ "flight_details": { "comms": [ { "id": 1, "channel": "COM1", "incoming_message": "Request taxi" } ] } }""")]
    public async Task NoErrorPointsThePilotAtASettingThatNoLongerExists(string json)
    {
        var result = await ServiceFor(json).GetLastTransmissionAsync();

        Assert.NotNull(result.Error);
        Assert.DoesNotContain("settings", result.Error!, StringComparison.OrdinalIgnoreCase);
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
                "outgoing_message": "Contact ground on one two one point niner" },
              { "id": 1, "channel": "COM1", "stamp_zulu": "02/01/2026 09:00:00",
                "outgoing_message": "Taxi to runway 15L via Alpha" }
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
