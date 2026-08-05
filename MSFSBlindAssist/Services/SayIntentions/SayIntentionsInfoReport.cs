using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// One headed block of the readout: a heading, and the lines that sit under it.
///
/// A section never carries an empty <see cref="Items"/> list — a block whose every
/// field was missing is dropped whole rather than emitted as a bare heading, so the
/// caller can hand each section straight to a control without checking for one that
/// has nothing in it.
/// </summary>
public sealed record InfoSection(string Heading, IReadOnlyList<string> Items);

/// <summary>
/// Builds the SayIntentions information readout as headed SECTIONS.
///
/// Sections, not one string, is the whole point. This readout used to be spoken as a
/// single run-on sentence, which was tolerable while it held three facts. With the
/// gate, the runway configuration and two airports' altimeters in it, a blind pilot
/// needs to move through it at their own pace and re-read one part without hearing all
/// of it again — so the caller puts each section in its own list box and the screen
/// reader walks the items with the arrow keys, Tab moving between sections.
///
/// The structure is what makes that possible: a list ITEM is a discrete object, so it
/// brailles as one unit and the reader announces its position ("3 of 7"), neither of
/// which a run of text in a box can give. <see cref="Flatten"/> renders the same
/// report as the flat line list it reads as on the page — headings, items, one blank
/// line between blocks.
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
    /// <summary>
    /// Whether a current_airport value is a US ARTCC facility ident rather than an
    /// airport. Live log, 2026-08-04/05 (KDEN→KSFO): in the cruise SI publishes the
    /// CONTROLLING CENTER's ident in current_airport — KZLC (Salt Lake), then KZOA
    /// (Oakland) at the handoff — and restores the real airport on the ground.
    /// Presented as "Current airport", a center ident tells a blind pilot something
    /// false about where they are.
    ///
    /// BOTH keys are required, deliberately: US ARTCC idents are exactly KZ + two
    /// letters, but so are the real airports KZPH (Zephyrhills) and KZZV
    /// (Zanesville) — the shape alone must never demote a real airport, so the
    /// caller supplies whether the navigation database knows the ident, and only a
    /// shape-matching UNKNOWN becomes a facility. An unknown ident of any other
    /// shape stays "Current airport": a small strip missing from navdata is more
    /// likely than a non-US facility, and this label asserts only the observed
    /// pattern.
    /// </summary>
    public static bool LooksLikeArtccFacility(string? ident, bool isKnownAirport)
    {
        if (isKnownAirport || string.IsNullOrWhiteSpace(ident)) return false;
        string trimmed = ident.Trim();
        return trimmed.Length == 4
            && trimmed[0] == 'K' && trimmed[1] == 'Z'
            && char.IsAsciiLetterUpper(trimmed[2]) && char.IsAsciiLetterUpper(trimmed[3]);
    }

    public static IReadOnlyList<InfoSection> Build(
        SayIntentionsFlightContext context,
        string? assignedGate,
        string? departureRunway,
        string? nearbyParkingStatus,
        bool currentAirportIsKnownAirport = true)
    {
        var sections = new List<InfoSection>();

        AddFlight(sections, context, currentAirportIsKnownAirport);
        AddGateAndRunway(sections, context, assignedGate, departureRunway, nearbyParkingStatus);
        AddAirports(sections, context);

        return sections;
    }

    /// <summary>
    /// The same report as one flat run of lines: each heading, its items under it, one
    /// blank line between blocks.
    ///
    /// This is the shape the report reads as on the page, and the shape its ordering is
    /// pinned in — which airport block leads, which line comes before which.
    /// </summary>
    public static IReadOnlyList<string> Flatten(IReadOnlyList<InfoSection> sections)
    {
        var lines = new List<string>();

        foreach (var section in sections)
        {
            if (lines.Count > 0) lines.Add("");
            lines.Add(section.Heading);
            lines.AddRange(section.Items);
        }

        return lines;
    }

    /// <summary>The one gate-section item that is a placeholder rather than content.
    /// <see cref="HasContent"/> must treat a report carrying only this line as empty,
    /// and the two sites must never spell it separately — a reword of the emit site
    /// silently broke the emptiness test before this const tied them.</summary>
    internal const string NoAssignedGateItem = "Assigned arrival gate: none assigned yet";

    /// <summary>
    /// True when the report says anything worth opening a window for.
    ///
    /// The gate line is emitted unconditionally — "none assigned yet" is real
    /// information to a pilot who knows SI assigns one on arrival — which means the
    /// report is never literally empty, and a naive Count check would open a window on
    /// a session where SayIntentions is not running at all. So the test is whether any
    /// section carries an item beyond that placeholder. Headings never count: a heading
    /// only exists because its section had something to put under it.
    /// </summary>
    public static bool HasContent(IReadOnlyList<InfoSection> sections) =>
        sections.Any(section => section.Items.Any(item =>
            !string.IsNullOrWhiteSpace(item)
            && !item.Equals(NoAssignedGateItem, StringComparison.Ordinal)));

    private static void AddFlight(
        List<InfoSection> sections, SayIntentionsFlightContext context,
        bool currentAirportIsKnownAirport)
    {
        var items = new List<string>();

        Add(items,
            LooksLikeArtccFacility(context.CurrentAirport, currentAirportIsKnownAirport)
                ? "Controlling facility"
                : "Current airport",
            context.CurrentAirport);
        Add(items, "Origin", context.Origin);
        Add(items, "Destination", context.Destination);
        Add(items, "Aircraft", context.AircraftIcao);

        // callsign_icao is NOT an ICAO callsign — a live capture had it identical to
        // `callsign` and already spelt out ("Skyhawk-One-Two-Three-Alpha-Zulu"). The
        // hyphens are SayIntentions' text-to-speech markup, not part of the callsign,
        // and a screen reader reads them aloud.
        Add(items, "Callsign", CleanCallsign(context.Callsign));

        if (!string.IsNullOrWhiteSpace(context.FlightPlanRoute))
            Add(items, "Route", context.FlightPlanRoute);

        AddSection(sections, "Flight", items);
    }

    private static void AddGateAndRunway(
        List<InfoSection> sections, SayIntentionsFlightContext context,
        string? assignedGate, string? departureRunway, string? nearbyParkingStatus)
    {
        var items = new List<string>();

        // SayIntentions does not assign a departure gate at all, so this is always the
        // arrival stand — and it stays blank until the arrival is under way. Saying so
        // is better than dropping the line: a pilot who has heard about assigned gates
        // and sees nothing cannot tell "none yet" from "we failed to read it".
        items.Add(string.IsNullOrWhiteSpace(assignedGate)
            ? NoAssignedGateItem
            : $"Assigned arrival gate: {assignedGate}");

        if (!string.IsNullOrWhiteSpace(nearbyParkingStatus))
            items.Add(nearbyParkingStatus);

        Add(items, "Departure runway", departureRunway);

        if (!string.IsNullOrWhiteSpace(context.ClearedForLanding))
            items.Add($"Cleared to land runway: {context.ClearedForLanding}");
        else
            Add(items, "Arrival runway", context.ArrivalRunway);

        AddSection(sections, "Gate and runway", items);
    }

    /// <summary>
    /// Emits the two airport blocks, the one the pilot needs first.
    ///
    /// They used to go out departure-then-arrival unconditionally, which on an arrival
    /// opened the window on the field the aircraft had LEFT: a live LMML -> EDDF capture,
    /// on the ground at EDDF, led with LMML's runway picture and LMML's altimeter — 1300
    /// nm behind the aircraft, and 0.12 inHg from the setting they were about to use,
    /// which is about 120 ft.
    ///
    /// The departure block leads only when it names the airport the aircraft is AT and
    /// the arrival block does not. Everything else — airborne, current_airport empty
    /// (flight.json omits it often enough), sitting at neither field, or both blocks
    /// naming the same one — leads with the ARRIVAL: a destination is what you plan for,
    /// and the field you left is not.
    ///
    /// A blank airport name matches nothing, not even a blank current_airport: the
    /// heading falls back to the block's role and the tie-break decides the order.
    ///
    /// One airport is printed ONCE. A circuit or a return-to-field names the same field
    /// in both blocks, and two identical headings carrying one number is nothing but
    /// lines to arrow past. The drop keys on a heading that has actually been PRINTED,
    /// never on the name alone — SI publishes airport names with nothing under them, and
    /// a stub in front must not cost the other block its runway picture.
    /// </summary>
    private static void AddAirports(List<InfoSection> sections, SayIntentionsFlightContext context)
    {
        bool departureLeads =
            IsAt(context.DepartureWeather, context.CurrentAirport)
            && !IsAt(context.ArrivalWeather, context.CurrentAirport);

        var printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (departureLeads)
        {
            AddAirport(sections, context.DepartureWeather, "Departure", printed);
            AddAirport(sections, context.ArrivalWeather, "Arrival", printed);
        }
        else
        {
            AddAirport(sections, context.ArrivalWeather, "Arrival", printed);
            AddAirport(sections, context.DepartureWeather, "Departure", printed);
        }
    }

    private static bool IsAt(SayIntentionsAirportWeather? weather, string? currentAirport) =>
        !string.IsNullOrWhiteSpace(weather?.Airport)
        && !string.IsNullOrWhiteSpace(currentAirport)
        && string.Equals(weather.Airport!.Trim(), currentAirport.Trim(),
            StringComparison.OrdinalIgnoreCase);

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
        List<InfoSection> sections, SayIntentionsAirportWeather? weather, string role,
        HashSet<string> printed)
    {
        if (weather == null) return;

        string airport = string.IsNullOrWhiteSpace(weather.Airport) ? role : weather.Airport!.Trim();
        string heading = $"{airport} airport";
        if (printed.Contains(heading)) return;

        var items = new List<string>();

        Add(items, "Landing runways", SpaceAfterCommas(weather.ActiveRunwaysArriving));
        Add(items, "Departing runways", SpaceAfterCommas(weather.ActiveRunwaysDeparting));
        Add(items, "Preferred runway", SpaceAfterCommas(weather.PreferredRunway));
        Add(items, "Runway flow", weather.CurrentlyOperating);

        if (weather.Altimeter.HasValue)
            items.Add(Altimeter(weather.Altimeter.Value));

        // A block with nothing under it never claims the heading, so the other block
        // still gets to print it.
        if (items.Count == 0) return;

        AddSection(sections, heading, items);
        printed.Add(heading);
    }

    /// <summary>
    /// The altimeter in BOTH units, because half the world flies the other one.
    ///
    /// SayIntentions publishes it numerically in inHg. The conversion is checked against
    /// the airports themselves rather than taken on trust: the live LMML -> EDDF capture
    /// read 30 and 30.12, and 30 x 33.86389 = 1016, 30.12 x 33.86389 = 1020 — exactly the
    /// Q1016 and QNH 1020 those two fields were passing on the frequency at the time.
    ///
    /// inHg is fixed at two decimals. Whole values used to drop theirs, so one window
    /// read "Altimeter: 30 inches" a few lines above "Altimeter: 30.12 inches" — one
    /// quantity written two ways, which is a stumble for a pilot comparing them and a
    /// different-sounding number through a screen reader. hPa is whole, as it is spoken.
    ///
    /// "inches" rather than "inHg": this line is READ ALOUD, and a screen reader spells
    /// "inHg" out letter by letter. Both numbers are invariant — a comma decimal
    /// separator makes the reader say a different number, not a typo.
    /// </summary>
    internal static string Altimeter(double inchesOfMercury)
    {
        // 1 inHg = 3386.389 Pa = 33.86389 hPa.
        double hectopascals = Math.Round(inchesOfMercury * 33.86389, MidpointRounding.AwayFromZero);

        return string.Format(CultureInfo.InvariantCulture,
            "Altimeter: {0:F2} inches ({1:F0} hPa)", inchesOfMercury, hectopascals);
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

    private static void Add(List<string> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) items.Add($"{label}: {value.Trim()}");
    }

    /// <summary>Appends a headed section. A section whose every field was missing
    /// contributes nothing — no empty heading to arrow past, and no empty list box to
    /// tab through.</summary>
    private static void AddSection(List<InfoSection> sections, string heading, List<string> items)
    {
        if (items.Count == 0) return;
        sections.Add(new InfoSection(heading, items));
    }
}
