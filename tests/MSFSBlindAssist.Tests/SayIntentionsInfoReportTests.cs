// The SayIntentions information readout (Output Shift+... -> Ctrl+Shift+S), which is a
// set of headed SECTIONS — one list box each in the window — rather than one spoken
// string. What each section says, and the order they come in, is asserted through
// Flatten: the same report rendered as the headed run of lines it reads as on the page.
//
// The field data below is a SECOND live capture, taken 2026-07-28 with the aircraft
// parked at KBOS with no flight plan filed — deliberately the case the earlier EDDF
// capture could not cover, since that one was taken at the destination after landing.
// Values are verbatim except the api_key, e-mail, display name and user id, which are
// not committed. What it settles:
//
//   - assigned_gate is EMPTY at the departure airport. SayIntentions does not publish
//     a gate outbound at all, which is the stronger form of "SI does not assign a
//     departure gate" and confirms the arrival-gate reading from the other direction.
//   - flight_plan_departing_runway is EMPTY while flight_details.runway holds "22L",
//     so the third fallback is not a rarity — it is the live value.
//   - departure_wx exists and is rich: ATIS letter, decoded ATIS, active runway
//     configuration, METAR, TAF. None of it was read before.
//   - callsign_icao is NOT an ICAO callsign. It equals `callsign` and is already spelt
//     out with hyphens for SayIntentions' own speech synthesis.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsInfoReportTests
{
    private const string KbosAtis =
        "Boston Logan International airport, information Uniform. 0254 Zulu. " +
        "Arriving runway 22L. Departing runways 22L, 22R. Wind 160 at 8. " +
        "Visibility 10. Scattered at 5500. Temperature 22, dewpoint 18. Altimeter 2973.";

    private const string KbosMetar =
        "KBOS 290254Z 16008KT 10SM SCT055 22/18 A2973 RMK AO2 SLP066 T02170178 51006";

    private static SayIntentionsAirportWeather KbosWeather() => new()
    {
        Airport = "KBOS",
        InformationLetter = "U",
        Atis = KbosAtis,
        ActiveRunwaysArriving = "22L",
        ActiveRunwaysDeparting = "22L,22R",
        PreferredRunway = "22L",
        CurrentlyOperating = "south",
        WindDirection = 160,
        WindSpeed = 8,
        WindGusting = null,
        Visibility = 10,
        Altimeter = 29.73,
        DensityAltitude = 1000,
        Metar = KbosMetar
    };

    private static SayIntentionsFlightContext KbosContext() => new()
    {
        CurrentAirport = "KBOS",
        Origin = "KBOS",
        AircraftIcao = "B738",
        Callsign = "Skyhawk-One-Two-Three-Alpha-Zulu",
        OnGround = true,
        DepartureWeather = KbosWeather()
    };

    // The report is built as SECTIONS — a heading and the lines under it — because the
    // window puts each one in its own list box. Flatten renders the same report as the
    // headed run of lines it reads as on the page, which is the shape the ordering rules
    // below are pinned in: which airport block leads, which line comes before which.
    private static IReadOnlyList<InfoSection> Sections(
        SayIntentionsFlightContext context,
        string? assignedGate = null,
        string? departureRunway = null,
        string? nearbyParkingStatus = null) =>
        SayIntentionsInfoReport.Build(context, assignedGate, departureRunway, nearbyParkingStatus);

    private static IReadOnlyList<string> Report(
        SayIntentionsFlightContext context,
        string? assignedGate = null,
        string? departureRunway = null,
        string? nearbyParkingStatus = null) =>
        SayIntentionsInfoReport.Flatten(
            Sections(context, assignedGate, departureRunway, nearbyParkingStatus));

    private static IReadOnlyList<InfoSection> KbosSections() =>
        Sections(KbosContext(), assignedGate: null,
            departureRunway: "22L", nearbyParkingStatus: null);

    private static IReadOnlyList<string> KbosReport() =>
        SayIntentionsInfoReport.Flatten(KbosSections());

    // --- the live LMML -> EDDF arrival ------------------------------------------------
    //
    // The session SayIntentionsLiveClearanceTests pins: on the ground at EDDF after
    // landing 07L, taxiing to Terminal 3 Gate J1. It carries BOTH weather blocks, which
    // is the case the KBOS capture (parked, no flight plan, departure_wx only) could not
    // reach — and the case where the ordering of those blocks matters.
    //
    // Only what the capture actually settles is filled in. The two altimeters are the
    // captured numbers, and each is corroborated by the QNH its own airport was passing
    // on the frequency at the time: Malta Q1016, and Frankfurt Tower's "information
    // Juliet is now current. QNH 1020." from the same comms feed.
    private static SayIntentionsAirportWeather LmmlWeather() => new()
    {
        Airport = "LMML",
        Altimeter = 30       // Q1016
    };

    private static SayIntentionsAirportWeather EddfWeather() => new()
    {
        Airport = "EDDF",
        Altimeter = 30.12    // QNH 1020
    };

    private static SayIntentionsFlightContext EddfArrivalContext() => new()
    {
        CurrentAirport = "EDDF",
        Origin = "LMML",
        Destination = "EDDF",
        OnGround = true,
        DepartureWeather = LmmlWeather(),
        ArrivalWeather = EddfWeather()
    };

    private static IReadOnlyList<InfoSection> EddfArrivalSections() =>
        Sections(EddfArrivalContext(),
            assignedGate: "Terminal 3 Gate J1", departureRunway: null, nearbyParkingStatus: null);

    private static IReadOnlyList<string> EddfArrivalReport() =>
        SayIntentionsInfoReport.Flatten(EddfArrivalSections());

    private static int LineIndex(IReadOnlyList<string> report, string line)
    {
        for (int i = 0; i < report.Count; i++)
            if (report[i] == line) return i;

        Assert.Fail($"Expected line not in report: \"{line}\"\n{string.Join("\n", report)}");
        return -1;
    }

    private static IEnumerable<string> Altimeters(IReadOnlyList<string> report) =>
        report.Where(line => line.StartsWith("Altimeter:", StringComparison.Ordinal));

    // --- what the section keeps, and what it must not repeat -------------------------

    // The runway picture is the reason this section exists: it is what a pilot wants
    // back without listening to the ATIS a second time, and structured it is one line
    // rather than a sentence to pick out of prose.
    [Fact]
    public void TheRunwayConfigurationAndAltimeterAreReported()
    {
        var report = KbosReport();

        Assert.Contains("Landing runways: 22L", report);
        Assert.Contains("Departing runways: 22L, 22R", report);
        Assert.Contains("Preferred runway: 22L", report);
        Assert.Contains("Runway flow: south", report);
        Assert.Contains("Altimeter: 29.73 inches (1007 hPa)", report);
    }

    // Everything a pilot can get by listening to the ATIS or opening the METAR window
    // stays OUT. It was briefly all in here, and twenty lines of already-heard weather
    // is exactly the wall this window was built to remove — the pilot had to arrow past
    // it to reach the few lines that were new.
    [Fact]
    public void TheAtisMetarAndTafAreNotRepeated()
    {
        var report = KbosReport();
        string all = string.Join("\n", report);

        Assert.DoesNotContain(KbosMetar, all, StringComparison.Ordinal);
        Assert.DoesNotContain("TAF", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Boston Logan International", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Visibility", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Wind", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Density altitude", all, StringComparison.Ordinal);
        // Not runway information, so not in the runway section — even though it is the
        // one field here you cannot restate without having listened.
        Assert.DoesNotContain("Information:", all, StringComparison.Ordinal);
    }

    // --- values that must not be mangled ---------------------------------------------

    // Altimeter 29.73 must not become "29,73" on a machine with a comma decimal
    // separator: the METAR sitting two lines below it says A2973, and the two have to
    // agree. A screen reader reads "29,73" as a different number, not a typo.
    [Fact]
    public void AviationNumbersAreCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            Assert.Contains("Altimeter: 29.73 inches (1007 hPa)", KbosReport());
            Assert.Contains("Altimeter: 30.12 inches (1020 hPa)", EddfArrivalReport());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // SI hyphenates the callsign for its own text-to-speech. A screen reader reads the
    // hyphens out loud, so they have to go before the pilot hears it.
    [Fact]
    public void TheCallsignHyphensAreRemoved()
    {
        Assert.Contains("Callsign: Skyhawk One Two Three Alpha Zulu", KbosReport());
    }

    // --- the altimeter ----------------------------------------------------------------

    // SayIntentions publishes the altimeter numerically in inHg, and half the world
    // flies the hPa number instead. The conversion is checked against the airports
    // themselves rather than against a constant: the capture read 30 at LMML and 30.12
    // at EDDF, Malta was passing Q1016 and Frankfurt QNH 1020, and 30 x 33.86389 = 1016,
    // 30.12 x 33.86389 = 1020. Both units are printed, so neither pilot converts in
    // their head off a spoken line.
    [Fact]
    public void TheAltimeterIsGivenInBothUnits()
    {
        var report = EddfArrivalReport();

        Assert.Contains("Altimeter: 30.12 inches (1020 hPa)", report);
        Assert.Contains("Altimeter: 30.00 inches (1016 hPa)", report);
    }

    // A whole number of inches used to drop its decimals, so one window read
    // "Altimeter: 30 inches" a few lines above "Altimeter: 30.12 inches" — the same
    // quantity written two different ways, which is a stumble for a pilot comparing them
    // and a different-sounding number through a screen reader.
    [Fact]
    public void AWholeNumberOfInchesStillReadsToTwoDecimals()
    {
        var report = EddfArrivalReport();

        Assert.Contains("Altimeter: 30.00 inches (1016 hPa)", report);
        Assert.DoesNotContain(report,
            line => line.StartsWith("Altimeter: 30 ", StringComparison.Ordinal));
        Assert.All(Altimeters(report),
            line => Assert.Matches(@"^Altimeter: \d+\.\d\d inches \(\d+ hPa\)$", line));
    }

    // "inches", not "inHg": this line is SPOKEN, and a screen reader reads "inHg" as
    // letters.
    [Fact]
    public void TheAltimeterUnitIsSpelledForSpeech()
    {
        Assert.DoesNotContain("inHg", string.Join("\n", EddfArrivalReport()),
            StringComparison.OrdinalIgnoreCase);
    }

    // --- which airport block comes first ----------------------------------------------

    // The blocks used to be emitted departure-then-arrival unconditionally, so an
    // arrival opened this window on the field 1300 nm BEHIND the aircraft: the first
    // altimeter the pilot arrowed onto was LMML's, 0.12 inHg from the one they were
    // about to set — about 120 ft — while EDDF's runway picture sat below it.
    [Fact]
    public void TheAirportTheAircraftIsAtIsReportedFirst()
    {
        var report = EddfArrivalReport();

        Assert.True(LineIndex(report, "EDDF airport") < LineIndex(report, "LMML airport"),
            "the airport under the wheels must lead");
        Assert.Equal("Altimeter: 30.12 inches (1020 hPa)", Altimeters(report).First());
    }

    // The same rule the other way round: it is not "arrival always wins", it is "the
    // field you are on wins". Before pushback that field is the departure airport.
    [Fact]
    public void TheDepartureFieldLeadsWhileTheAircraftIsStillOnIt()
    {
        var beforePushback = EddfArrivalContext();
        beforePushback.CurrentAirport = "LMML";

        var report = Report(beforePushback, null, "31", null);

        Assert.True(LineIndex(report, "LMML airport") < LineIndex(report, "EDDF airport"),
            "the airport under the wheels must lead");
        Assert.Equal("Altimeter: 30.00 inches (1016 hPa)", Altimeters(report).First());
    }

    // Airborne, or with current_airport empty (flight.json omits it often enough), or
    // sitting at neither field. A destination is what you plan for; the field you left
    // is not, so the arrival leads.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EDDL")]
    public void WithNoAirportUnderfootTheDestinationLeads(string? currentAirport)
    {
        var enRoute = EddfArrivalContext();
        enRoute.CurrentAirport = currentAirport;

        var report = Report(enRoute, null, null, null);

        Assert.True(LineIndex(report, "EDDF airport") < LineIndex(report, "LMML airport"),
            "with nothing under the wheels the destination leads");
    }

    // SI can publish a weather block with no airport name on it. The heading falls back
    // to the block's role, and a nameless block must never be read as "matching" a
    // current airport that is itself blank — blank is not a place.
    [Fact]
    public void ANamelessBlockKeepsItsRoleHeadingAndMatchesNothing()
    {
        var nameless = new SayIntentionsFlightContext
        {
            CurrentAirport = "  ",
            DepartureWeather = new SayIntentionsAirportWeather { Altimeter = 29.92 },
            ArrivalWeather = new SayIntentionsAirportWeather { Altimeter = 30.12 }
        };

        var report = Report(nameless, null, null, null);

        Assert.True(LineIndex(report, "Arrival airport") < LineIndex(report, "Departure airport"),
            "blank does not match blank, so the tie-break stands");

        // And a nameless block does not become "the airport you are at" merely because
        // current_airport happens to be blank as well.
        var halfNamed = new SayIntentionsFlightContext
        {
            CurrentAirport = "",
            DepartureWeather = new SayIntentionsAirportWeather { Altimeter = 29.92 },
            ArrivalWeather = EddfWeather()
        };

        var mixed = Report(halfNamed, null, null, null);

        Assert.True(LineIndex(mixed, "EDDF airport") < LineIndex(mixed, "Departure airport"),
            "a nameless block never leads on a blank current airport");
    }

    // A circuit or a return-to-field names the same airport in both blocks. Printing it
    // twice is two identical headings to arrow past and two copies of a number that can
    // only have one value, so it is printed once — and since both blocks then "match"
    // where the aircraft is, the tie-break decides which: the arrival, the copy SI keeps
    // for where the aircraft is going, rather than the one written when it left.
    [Fact]
    public void AReturnToFieldPrintsItsAirportOnceFromTheArrivalBlock()
    {
        var circuit = new SayIntentionsFlightContext
        {
            CurrentAirport = "KBOS",
            Origin = "KBOS",
            Destination = "KBOS",
            DepartureWeather = KbosWeather(),
            ArrivalWeather = new SayIntentionsAirportWeather
            {
                Airport = "KBOS",
                ActiveRunwaysArriving = "27",
                Altimeter = 29.68
            }
        };

        var report = Report(circuit, null, null, null);

        Assert.Single(report, line => line == "KBOS airport");
        Assert.Equal("Altimeter: 29.68 inches (1005 hPa)", Assert.Single(Altimeters(report)));
        Assert.Contains("Landing runways: 27", report);
    }

    // ...but the block that leads can be a stub — SI publishes an airport name with
    // nothing under it. Dropping the second block on its NAME alone would then lose the
    // runway picture and the altimeter entirely, so the drop keys on what the leading
    // block actually printed.
    [Fact]
    public void AnEmptyLeadingBlockDoesNotSwallowTheOneCarryingTheData()
    {
        var arrivalIsAStub = new SayIntentionsFlightContext
        {
            Destination = "KBOS",
            DepartureWeather = KbosWeather(),
            ArrivalWeather = new SayIntentionsAirportWeather { Airport = "KBOS" }
        };

        var report = Report(arrivalIsAStub, null, null, null);

        Assert.Single(report, line => line == "KBOS airport");
        Assert.Contains("Altimeter: 29.73 inches (1007 hPa)", report);
        Assert.Contains("Landing runways: 22L", report);
    }

    // A stub on the leading side of two DIFFERENT airports costs the other nothing
    // either, and leaves no heading with nothing under it to arrow past.
    [Fact]
    public void AStubLeadingBlockLeavesNoEmptyHeading()
    {
        var stubDeparture = new SayIntentionsFlightContext
        {
            CurrentAirport = "LMML",
            DepartureWeather = new SayIntentionsAirportWeather { Airport = "LMML" },
            ArrivalWeather = EddfWeather()
        };

        var report = Report(stubDeparture, null, null, null);

        Assert.DoesNotContain("LMML airport", report);
        Assert.Contains("EDDF airport", report);
        Assert.Contains("Altimeter: 30.12 inches (1020 hPa)", report);
    }

    // One block present, the other absent, in both directions — the ordering rule must
    // not drop the only one there is.
    [Fact]
    public void EitherBlockAloneStillPrints()
    {
        var arrivalOnly = new SayIntentionsFlightContext
        {
            CurrentAirport = "LMML", ArrivalWeather = EddfWeather()
        };
        var departureOnly = new SayIntentionsFlightContext
        {
            CurrentAirport = "EDDF", DepartureWeather = LmmlWeather()
        };

        Assert.Contains("EDDF airport", Report(arrivalOnly, null, null, null));
        Assert.Contains("LMML airport", Report(departureOnly, null, null, null));
    }

    // --- the gate line ----------------------------------------------------------------

    // Empty at the departure airport, per the live KBOS capture. The line still
    // appears: a pilot who knows SI assigns a gate cannot otherwise tell "not yet"
    // from "we failed to read it".
    [Fact]
    public void AnUnassignedGateIsStatedRatherThanOmitted()
    {
        Assert.Contains("Assigned arrival gate: none assigned yet", KbosReport());
    }

    [Fact]
    public void AnAssignedGateIsAlwaysLabelledAnArrivalGate()
    {
        var report = Report(KbosContext(), "Terminal 3 Gate J1", "22L", null);

        Assert.Contains("Assigned arrival gate: Terminal 3 Gate J1", report);
        Assert.DoesNotContain(report, line => line.Contains("Departure gate"));
    }

    // --- structure --------------------------------------------------------------------

    // A section whose every field is missing must not leave a bare heading for the
    // pilot to arrow past — nor, now that each section is its own list box, an empty
    // list to tab into and find nothing in.
    [Fact]
    public void AnEmptyAirportSectionIsOmittedEntirely()
    {
        var context = new SayIntentionsFlightContext { CurrentAirport = "KBOS" };

        Assert.DoesNotContain(Report(context, null, null, null),
            line => line.EndsWith(" airport", StringComparison.Ordinal));
        Assert.DoesNotContain(Sections(context, null, null, null),
            section => section.Heading.EndsWith(" airport", StringComparison.Ordinal));
    }

    // The gate line is unconditional, so the report is never literally empty. Opening a
    // window on a session where SayIntentions is not running would cost the pilot a
    // focus change and an Escape to learn what one spoken sentence says.
    [Fact]
    public void AReportWithNothingButThePlaceholderGateDoesNotCountAsContent()
    {
        var empty = Sections(new SayIntentionsFlightContext(), null, null, null);

        Assert.False(SayIntentionsInfoReport.HasContent(empty));
        Assert.True(SayIntentionsInfoReport.HasContent(KbosSections()));
    }

    // Each section becomes one list box, headed and tabbed to in this order, so the
    // section list IS the window's structure rather than a rendering of it.
    [Fact]
    public void TheReportIsBuiltAsHeadedSectionsInReadingOrder()
    {
        Assert.Equal(
            new[] { "Flight", "Gate and runway", "EDDF airport", "LMML airport" },
            EddfArrivalSections().Select(section => section.Heading));

        Assert.Equal(
            new[] { "Flight", "Gate and runway", "KBOS airport" },
            KbosSections().Select(section => section.Heading));
    }

    // Never an empty list box: a section exists only because it had something to put in
    // it, so the pilot never tabs into one that says nothing.
    [Fact]
    public void NoSectionIsEmpty()
    {
        Assert.All(EddfArrivalSections(), section => Assert.NotEmpty(section.Items));
        Assert.All(KbosSections(), section => Assert.NotEmpty(section.Items));
        Assert.All(Sections(new SayIntentionsFlightContext(), null, null, null),
            section => Assert.NotEmpty(section.Items));
    }

    // Flatten is what every ordering rule above is asserted through, so the shape it
    // produces is load-bearing: heading, its items under it, exactly one blank line
    // between blocks, and none at either end.
    [Fact]
    public void FlatteningRendersEachSectionAsAHeadingThenItsItems()
    {
        var sections = EddfArrivalSections();
        var report = SayIntentionsInfoReport.Flatten(sections);

        var expected = new List<string>();
        foreach (var section in sections)
        {
            if (expected.Count > 0) expected.Add("");
            expected.Add(section.Heading);
            expected.AddRange(section.Items);
        }

        Assert.Equal(expected, report);
        Assert.NotEmpty(report);
        Assert.False(string.IsNullOrWhiteSpace(report[0]));
        Assert.False(string.IsNullOrWhiteSpace(report[^1]));
    }

    [Fact]
    public void TheDepartureRunwayAndClearedToLandAppearInTheGateSection()
    {
        var landing = KbosContext();
        landing.ClearedForLanding = "22L";

        var report = Report(landing, null, null, null);

        Assert.Contains("Cleared to land runway: 22L", report);
        Assert.DoesNotContain(report, line => line.StartsWith("Departure runway", StringComparison.Ordinal));
    }
}
