using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Turns GSX's own live parking table — <c>handlerData.airport.parkings</c> — into
/// <see cref="ParkingSpot"/>s for the CURRENT airport (the one GSX has loaded). Replaces
/// the <c>.ini</c>-only <see cref="GsxProfileParser"/>/<see cref="GsxNavdataMerger"/> path
/// for that one airport, which is what lets a <c>.py</c>-profile airport (EDDF and similar)
/// get a real gate list for the first time instead of silently falling back to navdata.
/// See docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"Data reference" and §"ParkingSpot is both the list model and the docking input".
///
/// <para>
/// Routing (which ICAO this should be called for, and whether the result should be used
/// at all) is <c>GateDataSource</c>'s job, not this reader's — this type only maps whatever
/// <see cref="JsonElement"/> it is given.
/// </para>
///
/// <para>
/// <b>Never throws.</b> Every accessor below is <c>ValueKind</c>-guarded before reading
/// (same defensive style as <c>GsxServiceState</c>/<c>GsxBilling</c>/<c>GsxGateSelectResult</c>
/// in this namespace) because the guide states the <c>airport</c>/<c>gate</c> Remote API
/// proxies are a BLACKLIST (everything reflectable that isn't marked internal) rather than
/// a whitelist — so no field's presence is guaranteed, and a per-entry <c>try/catch</c>
/// backstops the guards so one malformed stand can never take the rest of the list down.
/// </para>
/// </summary>
public static class GsxRemoteParkingReader
{
    // uiType values that are not real, selectable stands — GSX's own vehicle spawn points
    // and fuel-truck parking spots. Excluded exactly as today's .ini/navdata path already
    // excludes non-gate/parking categories. KJFK capture: 6 Vehicle + 1 Fuel out of 238.
    private const string UiTypeVehicle = "Vehicle";
    private const string UiTypeFuel = "Fuel";

    // GSX type-constant NAME -> the numeric input GsxGateMapper.MapGsxTypeToNavdataType has
    // always expected for that category (its own doc comment: "GSX .ini type uses the MSFS
    // SDK parking-type enum"). See ResolveNavdataType for why this indirection exists.
    // GATE_EXTRA(15)/RAMP_GA_EXTRA(14) are omitted on purpose: GsxGateMapper has no
    // navdata-side case for either (falls through to its own `_ => 0`), and FUEL(12)/
    // VEHICLE(13) never reach this stage — those stands are excluded earlier in ReadOne.
    private static readonly IReadOnlyDictionary<string, int> NameToKnownGsxTypeInt =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["RAMP_GA"] = 1,
            ["RAMP_GA_SMALL"] = 2,
            ["RAMP_GA_MEDIUM"] = 3,
            ["RAMP_GA_LARGE"] = 4,
            ["RAMP_CARGO"] = 5,
            ["RAMP_MIL_CARGO"] = 6,
            ["RAMP_MIL_COMBAT"] = 7,
            ["GATE_SMALL"] = 8,
            ["GATE_MEDIUM"] = 9,
            ["GATE_HEAVY"] = 10,
            ["DOCK_GA"] = 11,
        };

    // "Gate 20A" -> number 20, trailing suffix "A" (the common shape — 229/238 KJFK stands).
    private static readonly Regex TrailingSuffix = new(@"^(\d+)([A-Za-z]*)$", RegexOptions.Compiled);

    // "Stand H6" -> leading letter "H", number 6 (9/238 KJFK remote GA hardstands). See
    // ParseNumberAndSuffix for the known display-only quirk this shape produces.
    private static readonly Regex LeadingPrefix = new(@"^([A-Za-z]+)(\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// Maps every entry of <c>handlerDataAirport.parkings</c> to a <see cref="ParkingSpot"/>.
    /// <paramref name="handlerDataAirport"/> is the <c>handlerData.airport</c> sub-object
    /// (not the whole <c>handlerData</c> frame) — the caller (<c>GateDataSource</c>) is
    /// responsible for confirming <c>handlerData.airport.icao</c> matches the airport being
    /// asked about before calling this; this method does not re-check that.
    /// <paramref name="icao"/> is used only to stamp <see cref="ParkingSpot.AirportICAO"/>.
    /// <para>
    /// <see cref="ParkingSpot.StopLatitude"/>/<see cref="ParkingSpot.StopLongitude"/>/
    /// <see cref="ParkingSpot.StopHeading"/> are always left null here — the API never
    /// publishes a stop position (verified null on all 238 KJFK stands); joining the
    /// <c>.ini</c>'s <c>parkingsystem_stopposition</c> onto these spots is a separate step.
    /// </para>
    /// Never throws: a non-object input, a missing/non-array <c>parkings</c>, or any
    /// malformed entry all degrade to an empty or partial list.
    /// </summary>
    public static List<ParkingSpot> Read(JsonElement handlerDataAirport, string icao)
    {
        var result = new List<ParkingSpot>();

        if (handlerDataAirport.ValueKind != JsonValueKind.Object) return result;
        if (!handlerDataAirport.TryGetProperty("parkings", out var parkingsEl)
            || parkingsEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var p in parkingsEl.EnumerateArray())
        {
            ParkingSpot? spot;
            try
            {
                spot = ReadOne(p, icao);
            }
            catch (Exception ex)
            {
                // Defensive backstop: every accessor below already guards its own
                // ValueKind, but one malformed/unexpectedly-shaped entry must never take
                // the whole list down — same idiom as GsxGateSelectResult.FromFrame's
                // outer catch. Never logs the raw entry (handlerData carries user data).
                Log.Debug("Gsx", $"parking reader: skipped one unreadable entry: {ex.Message}");
                spot = null;
            }
            if (spot != null) result.Add(spot);
        }

        Log.Debug("Gsx", $"parking reader: {result.Count} selectable parking(s) for {icao}.");
        return result;
    }

    private static ParkingSpot? ReadOne(JsonElement p, string icao)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;

        string? uiType = Str(p, "uiType");
        if (string.Equals(uiType, UiTypeVehicle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uiType, UiTypeFuel, StringComparison.OrdinalIgnoreCase))
            return null; // not a selectable stand

        string? uiGateName = Str(p, "uiGateName");
        if (string.IsNullOrWhiteSpace(uiGateName))
            return null; // nothing to select or label this entry with (never observed at KJFK: 238/238 present)

        // Position and heading both feed docking/taxi guidance downstream, and
        // ParkingSpot.Latitude/Longitude/Heading are non-nullable doubles -- there is no
        // "unknown" to store on the spot itself. Fabricating 0 would silently steer a blind
        // pilot at a wrong position/heading, which is worse than the stand being temporarily
        // absent from the list -- same "cannot be placed -> drop" rule GsxNavdataMerger.Merge
        // already applies. Real KJFK capture: lat/lon are 238/238 (never observed missing);
        // heading is 231/238 -- one real, otherwise-selectable Gate Heavy stand ("Gate 1A" at
        // Terminal 8 - Concourse B; a DIFFERENT "Gate 1A" at Terminal 1 does carry a heading
        // and is unaffected) is dropped by this rule.
        double? lat = Double(p, "lat");
        double? lon = Double(p, "lon");
        if (!lat.HasValue || !lon.HasValue)
        {
            Log.Debug("Gsx", $"parking reader: dropping \"{uiGateName}\" ({icao}) - missing lat/lon.");
            return null;
        }

        double? heading = Double(p, "heading");
        if (!heading.HasValue)
        {
            Log.Warn("Gsx", $"parking reader: dropping \"{uiGateName}\" ({icao}) - GSX published no heading for a selectable stand.");
            return null;
        }

        double? maxWingspan = Double(p, "maxWingspan");
        string? vdgs = Str(p, "parkingSystem");
        var (number, suffix) = ParseNumberAndSuffix(uiGateName);

        return new ParkingSpot
        {
            AirportICAO = icao ?? string.Empty,

            // uiGateName ALONE collides constantly across terminals at a real airport --
            // verified at KJFK: "Gate 2" alone names 5 physically different stands across 5
            // terminals (48 distinct uiGateName values collide at least once, out of 238).
            // uiTerminalName never repeats a shared uiGateName (0 collisions across all 238
            // (uiTerminalName, uiGateName) pairs), so it is what actually keeps the dropdown
            // distinguishable -- this is why it is used here, not merely descriptive colour.
            Name = TerminalNameOrEmpty(p),
            Number = number,
            Suffix = suffix,

            Type = ResolveNavdataType(p, Int(p, "type")),

            Latitude = lat.Value,
            Longitude = lon.Value,
            Heading = heading.Value,

            // GSX-sourced Radius is METRES (maxwingspan/2) -- NEVER feet. ParkingSpot.FitsAircraft
            // and SayIntentions' gate-position matching both branch on Source to pick the right
            // unit; getting this wrong makes every tolerance 3.28x wrong. No maxwingspan -> a
            // permissive 100 m fallback so the stand isn't spuriously filtered out, mirroring
            // GsxGateMapper.ToParkingSpot's existing .ini-path fallback (same value, same reasoning).
            MaxWingspanMeters = maxWingspan,
            Radius = maxWingspan.HasValue ? maxWingspan.Value / 2.0 : 100.0,

            HasJetway = ReadBool(p, "hasJetway"),
            AirlineCodes = AirlineCodesJoined(p),

            Source = GateSource.Gsx,
            VdgsType = string.IsNullOrWhiteSpace(vdgs) ? null : vdgs,
            GateDistanceThreshold = Double(p, "gateDistanceThreshold"),

            // GSX's own identifier, verbatim and UNCHANGED by any of the parsing above --
            // GsxRemoteGateSelector sends exactly this to gate.select. Never rebuild it from
            // Name/Number/Suffix or Describe(): that round-trip is exactly how the wrong
            // stand gets selected (spec ruling).
            GsxIdentifier = uiGateName,

            // Left null on purpose -- the API never publishes a stop position (stopPosition
            // is null on all 238 KJFK stands). GsxStopPositionJoiner (a later task) fills
            // these from the GSX .ini when one exists for this airport.
            StopLatitude = null,
            StopLongitude = null,
            StopHeading = null,
        };
    }

    private static string TerminalNameOrEmpty(JsonElement p) => Str(p, "uiTerminalName")?.Trim() ?? string.Empty;

    /// <summary>
    /// Extracts (Number, Suffix) from the tail of a GSX <c>uiGateName</c> such as
    /// "Gate 20A" or "Stand H6". The leading category word ("Gate"/"Stand"/"Parking"/
    /// "Ramp"/…) is discarded — <see cref="ParkingSpot.Name"/> carries
    /// <c>uiTerminalName</c> instead (see <see cref="ReadOne"/> for why).
    /// </summary>
    private static (int Number, string Suffix) ParseNumberAndSuffix(string uiGateName)
    {
        string[] tokens = uiGateName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return (0, string.Empty);
        string tail = tokens[^1];

        var trailing = TrailingSuffix.Match(tail);
        if (trailing.Success && trailing.Groups[1].Value.Length > 0)
            return (SafeParseInt(trailing.Groups[1].Value), trailing.Groups[2].Value.ToUpperInvariant());

        // "Stand H6"/"Stand H12" (KJFK's Terminal 5 - Remote GA hardstands, 9/238) glue the
        // letter BEFORE the digits instead of after. Still mapped to (Number, Suffix) --
        // there is nowhere else on ParkingSpot to put a lone extra letter, and
        // ParkingSpot.Describe()'s fixed "{Number}{Suffix}" template is explicitly untouched
        // by this reader (spec: "Describe()/ToString() are untouched, so every dropdown
        // reads identically") -- so this shape renders reordered as "6H" rather than the
        // source "H6". GsxIdentifier (what is actually SENT to gate.select) always carries
        // the untouched original string regardless, so this is a display-only quirk, not a
        // selection-safety issue.
        var leading = LeadingPrefix.Match(tail);
        if (leading.Success)
            return (SafeParseInt(leading.Groups[2].Value), leading.Groups[1].Value.ToUpperInvariant());

        return (0, string.Empty);
    }

    private static int SafeParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;

    /// <summary>
    /// Resolves this parking's navdata-numbered <see cref="ParkingSpot.Type"/> from its
    /// GSX <c>type</c> int — WITHOUT trusting that raw int's numbering directly. Every
    /// parking on the wire re-publishes GSX's own named type constants alongside its
    /// `type` value (verified: all 238 KJFK entries carry GATE_SMALL/GATE_MEDIUM/…/
    /// RAMP_MIL_COMBAT, always the same values). This method finds which constant NAME
    /// equals this entry's `type`, then translates that stable NAME through
    /// <see cref="NameToKnownGsxTypeInt"/> to the fixed input
    /// <see cref="GsxGateMapper.MapGsxTypeToNavdataType"/> has always expected for that
    /// category. If GSX ever renumbers its own enum (e.g. GATE_MEDIUM becomes 20 instead
    /// of 9), the live constants say so and this still resolves correctly — trusting
    /// `type` directly would silently mis-map instead.
    /// <para>
    /// Degrades to 0 (unknown — <see cref="ParkingSpot.GetFilterCategory"/> already renders
    /// that as "Other") when <paramref name="gsxTypeValue"/> is null, when no published
    /// constant matches it (a category not in <see cref="NameToKnownGsxTypeInt"/>, e.g.
    /// GATE_EXTRA/RAMP_GA_EXTRA — not observed at KJFK), or when the constants are absent
    /// from the payload entirely (best-effort — the guide says these fields are never
    /// guaranteed).
    /// </para>
    /// </summary>
    private static int ResolveNavdataType(JsonElement parking, int? gsxTypeValue)
    {
        if (!gsxTypeValue.HasValue || parking.ValueKind != JsonValueKind.Object) return 0;

        foreach (var candidate in NameToKnownGsxTypeInt)
        {
            if (parking.TryGetProperty(candidate.Key, out var constEl)
                && constEl.ValueKind == JsonValueKind.Number
                && constEl.TryGetInt32(out int constVal)
                && constVal == gsxTypeValue.Value)
            {
                return GsxGateMapper.MapGsxTypeToNavdataType(candidate.Value);
            }
        }

        return 0;
    }

    // ── JSON accessor helpers ───────────────────────────────────────────────
    // Same ValueKind-guarded style as GsxServiceState/GsxBilling/GsxGateSelectResult in
    // this namespace: every read is `TryGetProperty` + an explicit ValueKind check, never
    // a bare GetString()/GetDouble()/GetInt32() that could throw on an absent or
    // wrong-shaped field.

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
           ? v.GetString() : null;

    private static int? Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
           ? i : null;

    private static double? Double(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d)
           ? d : null;

    /// <summary>GSX publishes <c>hasJetway</c> as a 0/1 NUMBER on the live wire (verified),
    /// not a JSON boolean — but a real boolean is accepted too, best-effort, in case a
    /// future GSX build or a different proxy serializes it that way. Anything else
    /// (absent, wrong kind) is "no" — matching the existing .ini path's default.</summary>
    private static bool ReadBool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetInt32(out int i) && i != 0,
            _ => false,
        };
    }

    /// <summary>GSX publishes <c>airlineCodes</c> as a JSON array of strings (e.g.
    /// <c>["DAL","AMX"]</c>); <see cref="ParkingSpot.AirlineCodes"/> is a plain string, so
    /// this joins them for display. Absent/wrong-shaped/empty all degrade to "" — matching
    /// <see cref="ParkingSpot"/>'s own default.</summary>
    private static string AirlineCodesJoined(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty("airlineCodes", out var v)
            || v.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var codes = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
            {
                string? s = item.GetString();
                if (!string.IsNullOrEmpty(s)) codes.Add(s);
            }
        return string.Join(", ", codes);
    }
}
