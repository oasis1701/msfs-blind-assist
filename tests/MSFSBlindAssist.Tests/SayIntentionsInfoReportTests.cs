// The SayIntentions information readout (Output Shift+... -> Ctrl+Shift+S), which is a
// list of LINES rather than one spoken string.
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

    private static IReadOnlyList<string> KbosReport() =>
        SayIntentionsInfoReport.Build(KbosContext(), assignedGate: null,
            departureRunway: "22L", nearbyParkingStatus: null);

    // --- the two things that exist nowhere else in the app --------------------------

    [Fact]
    public void TheAtisLetterAndActiveRunwaysAreReported()
    {
        var report = KbosReport();

        Assert.Contains("Information: U", report);
        Assert.Contains("Landing runways: 22L", report);
        Assert.Contains("Departing runways: 22L, 22R", report);
    }

    // The ATIS arrives as one ~400-character blob. Spoken whole, a pilot who wants the
    // wind has to hear the airport name, the Zulu time and the runways first, every
    // time. One sentence per line makes it one arrow key.
    [Fact]
    public void TheAtisIsSplitIntoOneSentencePerLine()
    {
        var report = KbosReport();

        Assert.Contains("Arriving runway 22L.", report);
        Assert.Contains("Wind 160 at 8.", report);
        Assert.Contains("Temperature 22, dewpoint 18.", report);
        Assert.DoesNotContain(report, line => line == KbosAtis);
    }

    // A decimal is not a sentence end. Splitting on every period would have cut the
    // altimeter in half.
    [Fact]
    public void SentenceSplittingDoesNotCutDecimals()
    {
        var weather = new SayIntentionsAirportWeather
        {
            Airport = "KBOS",
            Atis = "Altimeter 29.73. Density altitude 1000 feet."
        };
        var report = SayIntentionsInfoReport.Build(
            new SayIntentionsFlightContext { DepartureWeather = weather }, null, null, null);

        Assert.Contains("Altimeter 29.73.", report);
    }

    [Fact]
    public void TheMetarStaysOnASingleLine()
    {
        Assert.Contains($"METAR: {KbosMetar}", KbosReport());
    }

    // --- values that must not be mangled ---------------------------------------------

    // Altimeter 29.73 must not become "29,73" on a machine with a comma decimal
    // separator: the METAR sitting two lines below it says A2973, and the two have to
    // agree.
    [Fact]
    public void AviationNumbersAreCultureInvariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            var report = KbosReport();
            Assert.Contains("Altimeter: 29.73 inches", report);
            Assert.Contains("Wind: 160 at 8 knots", report);
            Assert.Contains("Density altitude: 1,000 feet", report);
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

    [Fact]
    public void AZeroGustIsNotReportedAsGusting()
    {
        var weather = new SayIntentionsAirportWeather { WindSpeed = 8, WindDirection = 160, WindGusting = 0 };
        Assert.Equal("160 at 8 knots", SayIntentionsInfoReport.FormatWind(weather));

        weather = new SayIntentionsAirportWeather { WindSpeed = 8, WindDirection = 160, WindGusting = 22 };
        Assert.Equal("160 at 8 knots, gusting 22", SayIntentionsInfoReport.FormatWind(weather));
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
        var report = SayIntentionsInfoReport.Build(
            KbosContext(), "Terminal 3 Gate J1", "22L", null);

        Assert.Contains("Assigned arrival gate: Terminal 3 Gate J1", report);
        Assert.DoesNotContain(report, line => line.Contains("Departure gate"));
    }

    // --- structure --------------------------------------------------------------------

    // A section whose every field is missing must not leave a bare heading for the
    // pilot to arrow past.
    [Fact]
    public void AnEmptyWeatherSectionIsOmittedEntirely()
    {
        var report = SayIntentionsInfoReport.Build(
            new SayIntentionsFlightContext { CurrentAirport = "KBOS" }, null, null, null);

        Assert.DoesNotContain(report, line => line.EndsWith(" weather", StringComparison.Ordinal));
    }

    // The gate line is unconditional, so the report is never literally empty. Opening a
    // window on a session where SayIntentions is not running would cost the pilot a
    // focus change and an Escape to learn what one spoken sentence says.
    [Fact]
    public void AReportWithNothingButThePlaceholderGateDoesNotCountAsContent()
    {
        var empty = SayIntentionsInfoReport.Build(
            new SayIntentionsFlightContext(), null, null, null);

        Assert.False(SayIntentionsInfoReport.HasContent(empty));
        Assert.True(SayIntentionsInfoReport.HasContent(KbosReport()));
    }

    [Fact]
    public void TheDepartureRunwayAndClearedToLandAppearInTheGateSection()
    {
        var landing = KbosContext();
        landing.ClearedForLanding = "22L";

        var report = SayIntentionsInfoReport.Build(landing, null, null, null);

        Assert.Contains("Cleared to land runway: 22L", report);
        Assert.DoesNotContain(report, line => line.StartsWith("Departure runway", StringComparison.Ordinal));
    }
}
