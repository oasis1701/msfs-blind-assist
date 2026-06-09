using System;
using System.Collections.Generic;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// The aircraft identity fields GSX <c>.py</c> offset functions dispatch on.
/// Mirrors GSX's internal <c>aircraftData</c> object:
///   <c>icaoTypeDesignator</c> (e.g. "B77W"), <c>idMajor</c> (e.g. 777),
///   <c>idMinor</c> (e.g. 300), <c>aircraftGroup</c> (e.g. "Heavy"/"ARC-E").
/// Pure value type, no dependencies.
/// </summary>
public sealed record GsxAircraftId(string Icao, int IdMajor, int IdMinor, string Group);

/// <summary>
/// Resolves an ICAO type designator (from SimConnect ATC MODEL / TYPE) to the
/// (idMajor, idMinor, aircraftGroup) GSX would assign it. Built-in table of common
/// designators; unknown ICAOs still resolve to a usable id (idMajor 0 forces the
/// base/0 offset in idMajor-keyed tables, while ICAO-keyed tables may still hit).
/// </summary>
public static class GsxAircraftIdMap
{
    // Group strings mirror the broad GSX "aircraftGroup" buckets. Profiles that key
    // on group use either these ("Heavy"/"Medium"/"Super") or ICAO-Annex-14 codes
    // ("ARC-C".."ARC-E"); we can only seed one, and the Aerosoft-derived "Heavy"/
    // "Medium"/"Super" family is the most common. Group is a last-resort fallback in
    // ICAOAircraftOffsets (after ICAO and idMajor), so a miss here is non-fatal.
    private static readonly Dictionary<string, GsxAircraftId> Table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Boeing 777
            ["B77W"] = new("B77W", 777, 300, "Heavy"),
            ["B77L"] = new("B77L", 777, 200, "Heavy"),
            ["B772"] = new("B772", 777, 200, "Heavy"),
            ["B773"] = new("B773", 777, 300, "Heavy"),
            // Boeing 787
            ["B788"] = new("B788", 787, 8, "Heavy"),
            ["B789"] = new("B789", 787, 9, "Heavy"),
            ["B78X"] = new("B78X", 787, 10, "Heavy"),
            // Airbus A380
            ["A388"] = new("A388", 380, 800, "Super"),
            // Airbus A320 family
            ["A320"] = new("A320", 320, 0, "Medium"),
            ["A20N"] = new("A20N", 320, 1, "Medium"),
            ["A319"] = new("A319", 319, 0, "Medium"),
            ["A19N"] = new("A19N", 319, 1, "Medium"),
            ["A321"] = new("A321", 321, 0, "Medium"),
            ["A21N"] = new("A21N", 321, 1, "Medium"),
            ["A318"] = new("A318", 318, 0, "Medium"),
            // Airbus A330
            ["A332"] = new("A332", 330, 200, "Heavy"),
            ["A333"] = new("A333", 330, 300, "Heavy"),
            // Airbus A350
            ["A359"] = new("A359", 350, 1000, "Heavy"),
            ["A35K"] = new("A35K", 350, 1000, "Heavy"),
            // Boeing 737
            ["B738"] = new("B738", 737, 800, "Medium"),
            ["B739"] = new("B739", 737, 900, "Medium"),
            ["B737"] = new("B737", 737, 700, "Medium"),
            // Boeing 767
            ["B763"] = new("B763", 767, 300, "Heavy"),
            // Boeing 747
            ["B744"] = new("B744", 747, 400, "Heavy"),
        };

    /// <summary>
    /// Resolves <paramref name="icaoType"/> to a <see cref="GsxAircraftId"/>.
    /// Always succeeds: returns <c>true</c> with a table hit, or <c>false</c> with a
    /// best-effort fallback id <c>(icao, 0, "")</c> that is still safe to evaluate.
    /// </summary>
    public static bool TryResolve(string? icaoType, out GsxAircraftId id)
    {
        var key = (icaoType ?? string.Empty).Trim();
        if (key.Length > 0 && Table.TryGetValue(key, out var hit))
        {
            id = hit;
            return true;
        }

        // Unknown: keep the raw ICAO so ICAO-keyed tables can still hit; idMajor 0
        // and empty group force the base offset everywhere else.
        id = new GsxAircraftId(key.ToUpperInvariant(), 0, 0, string.Empty);
        return false;
    }
}
