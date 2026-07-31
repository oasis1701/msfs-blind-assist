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

        Assert.Null(MainForm.ResolveDepartureRunwayForStatus(arrived, onGround: true));
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

        Assert.Equal("05", MainForm.ResolveDepartureRunwayForStatus(departing, onGround: true));
    }

    // The runway you departed from is ground information. Airborne it is the last
    // thing the status readout had left to say, so it repeated a stale ground fact
    // for the whole cruise while the arrival gate and arrival runway — the parts that
    // are still about something the pilot can act on — sat behind it.
    [Fact]
    public void NoDepartureRunwayIsSpokenOnceAirborne()
    {
        var enRoute = new SayIntentionsFlightContext
        {
            CurrentAirport = "LMML",
            Origin = "LMML",
            Destination = "EDDF",
            DepartureRunway = "05",
            Runway = "05"
        };

        Assert.Null(MainForm.ResolveDepartureRunwayForStatus(enRoute, onGround: false));
    }

    // A live KBOS clearance-delivery transmission, 2026-07-29. It contains "via", which
    // is why the shape guard used to accept it: the import then found no taxiways, fell
    // back to shortest path to the departure runway, and announced itself as a
    // SayIntentions route with nothing to say it had not come from a taxi clearance.
    // A taxi clearance says TAXI.
    [Fact]
    public void ClearanceDeliveryIsNotATaxiClearance()
    {
        const string ifr =
            "Cleared to Miami via the SSOXS7 departure. Then as filed. Climb and maintain "
            + "5,000. Expect FL360 one-zero minutes after departure. Departure on 133.0. "
            + "Squawk 6422. And your departure runway was changed to 22L";

        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(ifr));
    }

    // The pilot's readback of the same clearance must fail the guard too — SayIntentions
    // publishes readbacks as transmissions, and a readback is the newest thing on the
    // frequency at exactly the moment a pilot might press the import key.
    [Fact]
    public void AClearanceReadbackIsNotATaxiClearanceEither()
    {
        const string readback =
            "Cleared to Miami via SSOXS7 departure, then as filed. Climb and maintain five "
            + "thousand. Expect FL360 one-zero minutes after departure. Departure on 133.0. "
            + "Squawk 6422. Departure runway 22L.";

        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(readback));
    }

    // The real taxi clearance from the same session still passes.
    [Fact]
    public void TheLiveTaxiClearanceStillPassesTheGuard()
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(
            "Runway 22L taxi via Alpha, November, hold short of runway 15R. "
            + "Advise you have information Victor."));
    }

    // --- KDTW, live, 2026-07-31: a crossing clearance written with a HYPHEN ------------
    //
    // Detroit Ground, 23:41:34Z, after the aircraft had been holding short of 4R. The
    // hyphen is the whole point: the mask matched CROSS followed by whitespace, so it
    // never saw this crossing at all, and the crossing runway is exactly what the mask
    // exists to hide from ParseDestinationRunway. Left unmasked, the leftmost "runway 4R"
    // became the DESTINATION — the import would have routed a taxiing aircraft AT the
    // active runway it had just been cleared to cross, which is the failure the whole
    // hold-short-masking rule was written for, reached through a spelling.
    //
    // KDTW's taxiways as navdata spells them, for the legs this clearance names.
    private static readonly string[] KdtwTaxiways = { "A", "A5", "K", "Q", "R", "U9", "V" };

    private const string KdtwCrossingClearance = "cross-runway 4R, then continue taxi via K, Q";

    [Fact]
    public void AHyphenatedCrossingRunwayIsNotTheDestination()
    {
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(KdtwCrossingClearance));
    }

    [Fact]
    public void AHyphenatedCrossingIsNotAHoldShortEither()
    {
        // Masked, but not captured: the pilot was cleared THROUGH this runway, so nothing
        // may set a hold-short at it.
        Assert.Null(SayIntentionsClearanceParser.ParseHoldShortRunway(KdtwCrossingClearance));
    }

    [Fact]
    public void TheTaxiwaysAfterAHyphenatedCrossingSurvive()
    {
        Assert.Equal(
            new[] { "K", "Q" },
            SayIntentionsClearanceParser.ParseTaxiways(KdtwCrossingClearance, KdtwTaxiways));
    }

    [Fact]
    public void AContinuationClearanceIsStillATaxiClearance()
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(KdtwCrossingClearance));
    }

    // The advisory issued four seconds later, verbatim. It is what the import used to
    // test — and reject — as "the last transmission", finding no clearance behind it.
    [Fact]
    public void AHoldShortAdvisoryIsNotATaxiClearance()
    {
        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(
            "hold short of runway 4R, 737 on the runway"));
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
//   - current_flight.taxi_path is GEOMETRY, not taxiway names — SI puts no name
//     anywhere in it. It IS read now (TheTaxiPathIsReadAsCoordinatesOnly, below):
//     point.lat/point.lon only, into TaxiPathPoints. See SayIntentionsService's
//     reader for why no other member of an entry is ever touched, and the
//     rewritten CLAUDE.md invariant for the hazard that guards against widening it.
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

    /// <summary>Writes a custom flight.json fixture and returns its path, for tests
    /// that need a shape ReadLiveContext's fixed EDDF payload doesn't carry.</summary>
    private string WriteFlightJson(string json)
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "flight.json");
        File.WriteAllText(path, json);
        return path;
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

    // The reason Ctrl+Shift+Y always needs the network: there is nothing here to
    // parse a clearance out of, so the import falls through to getCommsHistory.
    [Fact]
    public void FlightJsonCarriesNoClearanceAndNoTransmission()
    {
        var context = ReadLiveContext();

        Assert.Null(context.ClearanceText);
        Assert.Null(context.LastFlightJsonTransmission);
    }

    // The reason this reader exists at all: taxi_path is coordinates SI publishes
    // for its own route rendering, and Task 1's snapper (SayIntentionsTaxiPathSnapper)
    // turns coordinates into a taxiway sequence by snapping to the airport's own
    // graph. This only pins that the coordinates arrive intact — nothing here reads
    // a name, because taxi_path carries none (see the class comment above).
    [Fact]
    public void TheTaxiPathIsReadAsCoordinatesOnly()
    {
        var context = ReadLiveContext();      // fixture already carries 3 geometry entries
        Assert.Equal(3, context.TaxiPathPoints.Count);
        Assert.Equal(50.04, context.TaxiPathPoints[0].Latitude, 2);
        Assert.Equal(8.52,  context.TaxiPathPoints[0].Longitude, 2);
    }

    // flight_details.timestamp turned out NOT to be the ISO-ish "stamp_zulu" shape
    // used elsewhere in this same file (see ParseZuluStamp) — it is a raw Unix
    // epoch in SECONDS, fractional. Confirmed against ten real wire captures (LSZH
    // and EGLL, 2026-07-29/30, docs/superpowers/plans/2026-07-29-geometry-captures/),
    // every one of which carried it in exactly this shape, a few seconds ahead of
    // the file's own last-write time. 1785357161.40969 is drawn verbatim from one of
    // those captures; independently verified against it as 2026-07-29T20:32:41.409Z
    // via `python -c "import datetime; print(datetime.datetime.fromtimestamp(
    // 1785357161.40969, tz=datetime.timezone.utc))"`, not by re-deriving the same
    // formula this test is meant to check. The fractional part is kept (not dropped
    // to a whole-seconds literal) because the later safety gate this stamp feeds
    // needs offset-safe, sub-second-accurate comparisons against a clearance's own
    // timestamp — precision this test has to actually exercise, not assume.
    [Fact]
    public void TheTaxiPathStampReadsARealUnixEpoch()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "flight.json");
        File.WriteAllText(path, """
        {
          "flight_details": {
            "api_key": "PLACEHOLDER",
            "current_airport": "EDDF",
            "timestamp": 1785357161.40969,
            "current_flight": {
              "taxi_path": [
                { "heading": 93.92, "point": { "lon": 8.52, "lat": 50.04 } }
              ]
            }
          }
        }
        """);

        var context = new SayIntentionsService(path).ReadFlightContext();

        Assert.NotNull(context.TaxiPathStampUtc);
        // Assert.Equal(DateTime, DateTime, TimeSpan) compares by subtraction and
        // ignores Kind — Kind is asserted explicitly since it is exactly the
        // offset-safety property the later safety gate depends on.
        Assert.Equal(DateTimeKind.Utc, context.TaxiPathStampUtc!.Value.Kind);
        Assert.Equal(
            new DateTime(2026, 7, 29, 20, 32, 41, 409, DateTimeKind.Utc),
            context.TaxiPathStampUtc!.Value,
            TimeSpan.FromMilliseconds(1));
    }

    // This capture has no flight_details.timestamp at all (flight.json's "every
    // field is optional" rule applies here too) — TaxiPathStampUtc must still
    // carry an answer rather than going null out from under a path that IS
    // present, so it falls back to the file's own last-write time.
    [Fact]
    public void TheTaxiPathStampFallsBackToFileTimeWhenAbsent()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "flight.json");
        File.WriteAllText(path, EddfFlightJson);

        // Stamp a distinct PAST mtime after writing, so the fallback's source is
        // unambiguous: the fixture write and a naive assertion both happen within
        // moments of "now" anyway, so a fallback wrongly implemented as
        // DateTime.UtcNow would satisfy a same-instant comparison identically to the
        // real file-mtime read. A multi-year-old mtime cannot be confused with
        // "now" by any tolerance worth using.
        var distinctPastUtc = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, distinctPastUtc);

        var context = new SayIntentionsService(path).ReadFlightContext();

        Assert.NotNull(context.TaxiPathStampUtc);
        Assert.Equal(DateTimeKind.Utc, context.TaxiPathStampUtc!.Value.Kind);
        Assert.Equal(distinctPastUtc, context.TaxiPathStampUtc!.Value, TimeSpan.FromSeconds(1));
    }

    // Code review finding (Important 1): UnixEpoch.AddSeconds(unixSeconds.Value) is
    // unguarded, and ArgumentOutOfRangeException is not in ReadFlightContext's catch
    // list (JsonException/IOException/UnauthorizedAccessException) — so it used to
    // escape the whole method. SayIntentions migrating `timestamp` to MILLISECONDS is
    // the commonest way an epoch field drifts, and publishes exactly this shape
    // (1785357161409). The pilot would hear "SayIntentions transmission lookup
    // failed. Value to add was out of range. (Parameter 'value')" on Ctrl+S, the same
    // on Ctrl+Shift+S, and no route at all from Ctrl+Shift+Y — clearance text, last
    // transmission, gate, runways and weather all lost at once, for a value that only
    // ever should have cost the taxi-path stamp.
    [Fact]
    public void TheTaxiPathStampFallsBackWhenTimestampIsMilliseconds()
    {
        string path = WriteFlightJson("""
        {
          "flight_details": {
            "current_airport": "EDDF",
            "timestamp": 1785357161409,
            "current_flight": {
              "taxi_path": [
                { "heading": 93.92, "point": { "lon": 8.52, "lat": 50.04 } }
              ]
            }
          }
        }
        """);
        var distinctPastUtc = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, distinctPastUtc);

        var context = new SayIntentionsService(path).ReadFlightContext();

        Assert.Null(context.Error);
        Assert.Single(context.TaxiPathPoints); // the path itself still reads fine
        Assert.NotNull(context.TaxiPathStampUtc);
        Assert.Equal(DateTimeKind.Utc, context.TaxiPathStampUtc!.Value.Kind);
        Assert.Equal(distinctPastUtc, context.TaxiPathStampUtc!.Value, TimeSpan.FromSeconds(1));
    }

    // The rest of the reviewer's verified-throwing shapes (a grossly-out-of-range
    // float, a numeric string that parses to +Infinity, and a 16-digit
    // microsecond-scale epoch), plus Minor finding 1: `timestamp: 0` or negative are
    // both "successful" conversions today (0 -> 1970, negative -> 1913), so the mtime
    // fallback never runs for them either even though neither is a real generation
    // time. Direction is safe either way (never newer than a live clearance, so
    // geometry is never wrongly preferred over it), but a 0 written for "unset" would
    // otherwise permanently and silently disable the geometry route. All five must
    // land on the file-mtime fallback without throwing.
    [Theory]
    [InlineData("1e30")]                 // grossly out of range
    [InlineData("17853571614096900")]    // a 16-digit microsecond-scale epoch
    [InlineData("\"Infinity\"")]         // numeric-looking string that parses to +Inf
    [InlineData("0")]                    // an explicit "unset" sentinel
    [InlineData("-1785357161")]          // negative -> a valid but nonsensical 1913 date
    public void TheTaxiPathStampFallsBackForImplausibleTimestamps(string rawTimestampToken)
    {
        string path = WriteFlightJson("""
        {
          "flight_details": {
            "current_airport": "EDDF",
            "timestamp": __TOKEN__,
            "current_flight": {
              "taxi_path": [
                { "heading": 93.92, "point": { "lon": 8.52, "lat": 50.04 } }
              ]
            }
          }
        }
        """.Replace("__TOKEN__", rawTimestampToken));
        var distinctPastUtc = new DateTime(2020, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, distinctPastUtc);

        var context = new SayIntentionsService(path).ReadFlightContext();

        Assert.Null(context.Error);
        Assert.NotNull(context.TaxiPathStampUtc);
        Assert.Equal(DateTimeKind.Utc, context.TaxiPathStampUtc!.Value.Kind);
        Assert.Equal(distinctPastUtc, context.TaxiPathStampUtc!.Value, TimeSpan.FromSeconds(1));
    }

    // Code review finding (Important 2): ReadTaxiPathPoints must SKIP an entry missing
    // a coordinate, never default it to (0, 0) — a zeroed point sails past the
    // snapper's 25 m tolerance to nowhere useful, silently reporting a clean read as a
    // partly-unreadable route. The behaviour is already correct; nothing pinned it
    // before this test, so a future "simplification" to `GetDouble(point, "lat") ?? 0`
    // would leave every other test green while injecting exactly that bug.
    [Fact]
    public void MalformedTaxiPathEntriesAreSkippedNotZeroed()
    {
        string path = WriteFlightJson("""
        {
          "flight_details": {
            "current_airport": "EDDF",
            "current_flight": {
              "taxi_path": [
                { "heading": 93.92, "point": { "lon": 8.52, "lat": 50.04 } },
                { "heading": 90.0, "point": { "lon": 8.53 } },
                { "heading": 91.0, "point": { "lat": 50.05 } },
                { "heading": 92.0 },
                { "heading": 94.01, "point": { "lon": 8.54, "lat": 50.06 } }
              ]
            }
          }
        }
        """);

        var context = new SayIntentionsService(path).ReadFlightContext();

        Assert.Null(context.Error);
        Assert.Equal(2, context.TaxiPathPoints.Count);
        Assert.Equal(50.04, context.TaxiPathPoints[0].Latitude, 2);
        Assert.Equal(8.52, context.TaxiPathPoints[0].Longitude, 2);
        Assert.Equal(50.06, context.TaxiPathPoints[1].Latitude, 2);
        Assert.Equal(8.54, context.TaxiPathPoints[1].Longitude, 2);
        Assert.DoesNotContain(context.TaxiPathPoints, p => p.Latitude == 0 && p.Longitude == 0);
    }
}
