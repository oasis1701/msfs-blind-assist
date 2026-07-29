using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Builds the SayIntentions information readout as a list of LINES.
///
/// Lines, not one string, is the whole point. This readout used to be spoken as a
/// single run-on sentence, which was tolerable while it held three facts. With the
/// ATIS, the active runway configuration, the METAR and the TAF in it, a blind pilot
/// needs to move through it at their own pace and re-read one part without hearing all
/// of it again — so the caller puts these lines in a read-only text box and the screen
/// reader walks them with the arrow keys.
///
/// It is kept SHORT on purpose. Anything a pilot can get by listening to the ATIS or
/// opening the METAR window does not belong here — see AddAirport.
///
/// Pure and covered by SayIntentionsInfoReportTests: no I/O, no UI, no SimConnect.
/// Every field is optional and a missing one drops its line rather than printing
/// "unknown" — an empty label is noise to arrow past.
/// </summary>
public static class SayIntentionsInfoReport
{
    public static IReadOnlyList<string> Build(
        SayIntentionsFlightContext context,
        string? assignedGate,
        string? departureRunway,
        string? nearbyParkingStatus)
    {
        var lines = new List<string>();

        AddFlight(lines, context);
        AddGateAndRunway(lines, context, assignedGate, departureRunway, nearbyParkingStatus);

        AddAirport(lines, context.DepartureWeather, "Departure");
        AddAirport(lines, context.ArrivalWeather, "Arrival");

        return lines;
    }

    /// <summary>
    /// True when the report says anything worth opening a window for.
    ///
    /// The gate line is emitted unconditionally — "none assigned yet" is real
    /// information to a pilot who knows SI assigns one on arrival — which means the
    /// report is never literally empty, and a naive Count check would open a window on
    /// a session where SayIntentions is not running at all. So the test is whether any
    /// line carries something beyond that placeholder and the section headings.
    /// </summary>
    public static bool HasContent(IReadOnlyList<string> lines) =>
        lines.Any(line =>
            !string.IsNullOrWhiteSpace(line)
            && line != "Gate and runway"
            && !line.StartsWith("Assigned arrival gate: none", StringComparison.Ordinal));

    private static void AddFlight(List<string> lines, SayIntentionsFlightContext context)
    {
        var section = new List<string>();

        Add(section, "Current airport", context.CurrentAirport);
        Add(section, "Origin", context.Origin);
        Add(section, "Destination", context.Destination);
        Add(section, "Aircraft", context.AircraftIcao);

        // callsign_icao is NOT an ICAO callsign — a live capture had it identical to
        // `callsign` and already spelt out ("Skyhawk-One-Two-Three-Alpha-Zulu"). The
        // hyphens are SayIntentions' text-to-speech markup, not part of the callsign,
        // and a screen reader reads them aloud.
        Add(section, "Callsign", CleanCallsign(context.Callsign));

        if (!string.IsNullOrWhiteSpace(context.FlightPlanRoute))
            Add(section, "Route", context.FlightPlanRoute);

        AddSection(lines, "Flight", section);
    }

    private static void AddGateAndRunway(
        List<string> lines, SayIntentionsFlightContext context,
        string? assignedGate, string? departureRunway, string? nearbyParkingStatus)
    {
        var section = new List<string>();

        // SayIntentions does not assign a departure gate at all, so this is always the
        // arrival stand — and it stays blank until the arrival is under way. Saying so
        // is better than dropping the line: a pilot who has heard about assigned gates
        // and sees nothing cannot tell "none yet" from "we failed to read it".
        section.Add(string.IsNullOrWhiteSpace(assignedGate)
            ? "Assigned arrival gate: none assigned yet"
            : $"Assigned arrival gate: {assignedGate}");

        if (!string.IsNullOrWhiteSpace(nearbyParkingStatus))
            section.Add(nearbyParkingStatus);

        Add(section, "Departure runway", departureRunway);

        if (!string.IsNullOrWhiteSpace(context.ClearedForLanding))
            section.Add($"Cleared to land runway: {context.ClearedForLanding}");
        else
            Add(section, "Arrival runway", context.ArrivalRunway);

        AddSection(lines, "Gate and runway", section);
    }

    /// <summary>
    /// The airport section carries ONLY what a pilot cannot get by listening to the
    /// ATIS or reading the METAR.
    ///
    /// It briefly carried both in full — decoded ATIS sentence by sentence, METAR, TAF,
    /// wind, visibility, density altitude — and that was the wrong call. Every one of
    /// those is already available: the ATIS from SayIntentions itself, the METAR from
    /// the METAR window. Repeating them here made the pilot arrow through twenty lines
    /// they had heard already to reach the handful they had not, which is precisely the
    /// wall this window was built to remove.
    ///
    /// What stays is the RUNWAY picture and the altimeter — the parts worth having
    /// cached precisely so the pilot does not have to sit through the ATIS again to
    /// recover them. Structured, so "which runway will SI give me" is one line rather
    /// than a sentence to pick out of ATIS prose.
    ///
    /// The ATIS letter is deliberately not here either. It is the one thing in the
    /// block you genuinely cannot restate without having listened — but it is also not
    /// runway information, and this section is the runway information.
    /// </summary>
    private static void AddAirport(
        List<string> lines, SayIntentionsAirportWeather? weather, string role)
    {
        if (weather == null) return;

        string airport = string.IsNullOrWhiteSpace(weather.Airport) ? role : weather.Airport!;
        var section = new List<string>();

        Add(section, "Landing runways", SpaceAfterCommas(weather.ActiveRunwaysArriving));
        Add(section, "Departing runways", SpaceAfterCommas(weather.ActiveRunwaysDeparting));
        Add(section, "Preferred runway", SpaceAfterCommas(weather.PreferredRunway));
        Add(section, "Runway flow", weather.CurrentlyOperating);

        if (weather.Altimeter.HasValue)
            section.Add($"Altimeter: {Number(weather.Altimeter.Value)} inches");

        AddSection(lines, $"{airport} airport", section);
    }

    /// <summary>SayIntentions hyphenates the callsign for its own speech synthesis
    /// ("Skyhawk-One-Two-Three-Alpha-Zulu"). A screen reader reads those hyphens, so
    /// they come out before the words. Spaces read the same and sound right.</summary>
    internal static string? CleanCallsign(string? callsign) =>
        string.IsNullOrWhiteSpace(callsign)
            ? null
            : Regex.Replace(callsign.Trim(), @"\s*-\s*", " ");

    /// <summary>SI packs runway lists as "22L,22R". Without a space after the comma a
    /// screen reader runs the two designators together into one word.</summary>
    internal static string? SpaceAfterCommas(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : Regex.Replace(value, @",\s*", ", ");

    private static string Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e9
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private static void Add(List<string> section, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) section.Add($"{label}: {value.Trim()}");
    }

    /// <summary>Appends a headed section, separated from the previous one by a blank
    /// line. A section whose every field was missing contributes nothing — no empty
    /// heading to arrow past.</summary>
    private static void AddSection(List<string> lines, string heading, List<string> section)
    {
        if (section.Count == 0) return;
        if (lines.Count > 0) lines.Add("");
        lines.Add(heading);
        lines.AddRange(section);
    }
}
