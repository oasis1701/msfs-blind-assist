using System.Text.Json;
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
    /// <para>
    /// A stand missing only <c>lat</c>/<c>lon</c> is dropped (it cannot be placed at all).
    /// A stand missing only <c>heading</c> is KEPT with <see cref="ParkingSpot.Heading"/> set
    /// to <see cref="double.NaN"/> rather than dropped or defaulted to 0 — see
    /// <see cref="HasUsableHeading"/> and the comment in <c>ReadOne</c> for why. A heading
    /// GSX DID publish is normalized to 0-360 (<c>GsxProfileParser.NormalizeHeading</c>, the
    /// same helper the <c>.ini</c> path uses) — GSX publishes signed headings and this one is
    /// spoken to the pilot.
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

    /// <summary>
    /// The one canonical check for "does this spot carry a real, usable heading" — false for
    /// a spot <see cref="Read"/> emitted with <see cref="double.NaN"/> because GSX's
    /// <c>handlerData</c> omitted <c>heading</c> for that stand (real, if rare: 1/238 at
    /// KJFK — "Gate 1A" at Terminal 8 - Concourse B). Later stages should call this instead
    /// of spelling out <c>double.IsNaN(spot.Heading)</c> themselves: a later join (e.g. the
    /// GSX <c>.ini</c>'s <c>this_parking_pos</c>) may recover a real heading for a spot this
    /// returns false for today, and whatever is still unusable after that must never reach
    /// docking or the UI — dropping that residual case belongs to whichever later stage owns
    /// that join, not to this reader.
    /// </summary>
    public static bool HasUsableHeading(ParkingSpot? spot) => spot is not null && !double.IsNaN(spot.Heading);

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

        // Position is not just missing orientation -- with no lat/lon the stand cannot be
        // PLACED at all, and ParkingSpot.Latitude/Longitude are non-nullable doubles with no
        // "unknown" to store. Same "cannot be placed -> drop" rule GsxNavdataMerger.Merge
        // already applies elsewhere. Never observed missing at KJFK (238/238 present).
        double? lat = Double(p, "lat");
        double? lon = Double(p, "lon");
        if (!lat.HasValue || !lon.HasValue)
        {
            Log.Debug("Gsx", $"parking reader: dropping \"{uiGateName}\" ({icao}) - missing lat/lon.");
            return null;
        }

        // Heading is different: GSX omits it on 7/238 KJFK stands -- 5 Vehicle + 1 Fuel
        // (already excluded above) AND 1 real, otherwise-selectable Gate Heavy stand,
        // "Gate 1A" at Terminal 8 - Concourse B (a DIFFERENT, unrelated "Gate 1A" at
        // Terminal 1 DOES carry a real heading and is unaffected by any of this).
        // Dropping a real, otherwise-normal stand just because GSX omitted one field would
        // leave a blind pilot unable to find it with no explanation -- worse than the
        // fabricated-0-heading failure this originally avoided, not better. And the data is
        // often recoverable: the .ini's this_parking_pos ("lat lon heading") can supply
        // exactly this value, via the same coordinate join GsxStopPositionJoiner (Task 4)
        // already performs for the stop position. So a missing heading emits double.NaN
        // rather than dropping the stand -- NaN can never be mistaken for a real bearing
        // (unlike 0, which points due north and would silently steer docking there), and any
        // geometry computed on it produces a visible NaN instead of a plausible-but-wrong
        // turn. Use HasUsableHeading(spot) rather than testing double.IsNaN(spot.Heading)
        // directly. Whatever is STILL NaN after Task 4's join must never reach the UI or
        // docking -- dropping that residual case is a later stage's job, not this reader's.
        //
        // The published value is also SIGNED -- 122 of the 231 headings in the KJFK capture
        // are negative, and 37 of 68 on a live ENGM read. It is normalized to 0-360 through
        // GsxProfileParser.NormalizeHeading, the SAME helper the .ini path has always applied
        // to this_parking_pos, because it is the same GSX data and must not read differently
        // for having arrived over a different transport. Steering and docking geometry are
        // wrap-safe either way, but the number is SPOKEN ("Align with {stand}, heading -90"),
        // and a heading a pilot cannot find on their heading indicator is worse than useless.
        // NormalizeHeading passes NaN through unchanged, which is what keeps the sentinel
        // above intact -- see its own doc comment.
        double? heading = Double(p, "heading");
        double effectiveHeading = heading.HasValue
            ? GsxProfileParser.NormalizeHeading(heading.Value)
            : double.NaN;
        if (!heading.HasValue)
            Log.Warn("Gsx", $"parking reader: \"{uiGateName}\" ({icao}) has no published heading from GSX -- emitting with Heading=NaN instead of dropping it; the .ini join may recover a real value.");

        double? maxWingspan = Double(p, "maxWingspan");
        string? vdgs = Str(p, "parkingSystem");
        var (name, number, suffix) = ParseStandIdentity(uiGateName);

        return new ParkingSpot
        {
            AirportICAO = icao ?? string.Empty,

            // Name/Number/Suffix follow the app-wide stand-identity convention -- the same one
            // GsxGateMapper.ToParkingSpot uses on the .ini path: Name is the CONCOURSE LETTER
            // ("B"), Number the stand number, Suffix the trailing letter. Three subsystems read
            // Name expecting exactly that shape (GateAliasResolver via StandId.Parse,
            // SayIntentions' NormalizeParkingName gate matching, and MainForm's parked-at-the-
            // assigned-gate check), and all three break on anything else -- see ParkingSpot.
            // TerminalName's doc comment for what putting terminal prose here cost.
            Name = name,
            Number = number,
            Suffix = suffix,

            // uiGateName ALONE collides constantly across terminals at a real airport --
            // verified at KJFK: "Gate 2" alone names 5 physically different stands across 5
            // terminals (48 distinct uiGateName values collide at least once, out of 238).
            // uiTerminalName never repeats a shared uiGateName (0 collisions across all 238
            // (uiTerminalName, uiGateName) pairs), so it is what actually keeps the dropdown
            // distinguishable -- not merely descriptive colour. It rides in its OWN field,
            // which ParkingSpot.Describe() renders after the first spaced dash (i.e. in the
            // part every stand-id consumer already discards).
            TerminalName = TerminalNameOrEmpty(p),

            Type = ResolveNavdataType(p, Int(p, "type")),

            Latitude = lat.Value,
            Longitude = lon.Value,
            Heading = effectiveHeading,   // may be double.NaN -- see HasUsableHeading

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
    /// Splits a GSX <c>uiGateName</c> ("Gate 20A", "Stand H6", "Ramp 51") into the app-wide
    /// (Name, Number, Suffix) stand identity, via the ONE canonical parse
    /// <see cref="Services.StandId.Parse"/> — the same function <c>GateAliasResolver</c> and
    /// <c>GateSearchFilter</c> use. Sharing it is the point: this reader and the alias
    /// resolver agree on what a stand is called by construction rather than by two
    /// hand-written regexes that can drift. It also drops the leading category word
    /// ("Gate"/"Stand"/"Ramp"/…) for free, and handles the "Gate A12A" shape (letter, number
    /// AND trailing suffix) that the two regexes this replaced both fell through.
    /// <para>
    /// A name carrying NO number at all ("Helipad") has no stand identity to split, so the
    /// whole trimmed <c>uiGateName</c> becomes <see cref="ParkingSpot.Name"/> — better a
    /// readable label than a blank one, and <c>GateAliasResolver</c> ignores a spot with no
    /// number anyway. <see cref="ParkingSpot.GsxIdentifier"/> keeps the untouched original
    /// string regardless: nothing here ever reaches <c>gate.select</c>.
    /// </para>
    /// </summary>
    private static (string Name, int Number, string Suffix) ParseStandIdentity(string uiGateName)
    {
        var id = Services.StandId.Parse(uiGateName);
        return id.HasNumber
            ? (id.Letter, id.Number, id.Suffix)
            : (uiGateName.Trim(), 0, string.Empty);
    }

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
