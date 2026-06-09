using System;
using System.Collections.Generic;
using System.Globalization;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// The aircraft identity fields GSX <c>.py</c> offset functions dispatch on.
/// Mirrors GSX's internal <c>aircraftData</c> object:
///   <c>icaoTypeDesignator</c> (e.g. "B77W"), <c>idMajor</c> (e.g. 777),
///   <c>idMinor</c> (e.g. 300), <c>aircraftGroup</c> (e.g. "Heavy").
/// <c>ArcCode</c> is the ICAO Annex-14 Aerodrome Reference Code letter ("E") derived
/// from wingspan — many scenery authors key their group tables on "ARC-E" (and a few on
/// the bare "E"), so the evaluator tries both forms. Pure value type, no dependencies.
/// </summary>
public sealed record GsxAircraftId(string Icao, int IdMajor, int IdMinor, string Group, string ArcCode)
{
    /// <summary>Back-compat 4-arg ctor (no ARC) — used by older callers/tests.</summary>
    public GsxAircraftId(string Icao, int IdMajor, int IdMinor, string Group)
        : this(Icao, IdMajor, IdMinor, Group, string.Empty) { }
}

/// <summary>
/// Resolves an ICAO type designator (+ optional wingspan in metres) to the
/// (idMajor, idMinor, aircraftGroup, ARC) GSX would assign it.
///
/// The PRIMARY mechanism is DERIVATION from the ICAO designator pattern (idMajor/idMinor)
/// and the wingspan (ARC/group) — so the resolver works for ANY aircraft, including ones
/// never seen before, with near-zero hardcoding. The static <see cref="ExceptionTable"/>
/// is a thin list of genuinely irregular designators whose digits don't follow the family
/// pattern (e.g. the A350 / A380 / BCS family). Unknown ICAOs still resolve to a usable id;
/// the raw ICAO is always preserved so ICAO-keyed tables hit regardless, and a derived
/// idMajor of 0 simply forces the base/0 offset in idMajor-keyed tables.
/// </summary>
public static class GsxAircraftIdMap
{
    // Genuinely irregular designators only — ones the deriver below CANNOT produce from the
    // pattern (idMinor that doesn't fall out of the 4th char, or a non-obvious idMajor).
    // Everything regular (B73x/B74x/.../A31x/A32x, most widebodies) is DERIVED, not listed.
    private static readonly Dictionary<string, GsxAircraftId> ExceptionTable =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Boeing 787: GSX keys idMinor on the BARE dash number (8/9/10), not the
            // d*100 the Boeing deriver would produce (would give 800/900/10 — wrong for
            // 788/789). EDDF's table787 = {0:0, 9:1.65, 10:5.3} confirms the bare form.
            ["B788"] = new("B788", 787, 8, "Heavy", "ARC-E"),
            ["B789"] = new("B789", 787, 9, "Heavy", "ARC-E"),
            ["B78X"] = new("B78X", 787, 10, "Heavy", "ARC-E"),
            // A350: GSX uses idMinor 1000 for both -900 and -1000K (irregular vs. the
            // A33x->Z*100 rule that would give 900 / 0).
            ["A359"] = new("A359", 350, 1000, "Heavy", "ARC-E"),
            ["A35K"] = new("A35K", 350, 1000, "Heavy", "ARC-F"),
            // A380: 4th char '8' would derive idMinor 800 via the widebody rule, which is
            // correct — but pin idMajor/group/ARC explicitly so it's unmistakable.
            ["A388"] = new("A388", 380, 800, "Super", "ARC-F"),
            // Airbus neo narrowbodies: idMajor follows the literal A32x but GSX tags the neo
            // with idMinor 1 (the only place idMinor is non-zero on a narrowbody).
            ["A20N"] = new("A20N", 320, 1, "Medium", "ARC-C"),
            ["A19N"] = new("A19N", 319, 1, "Medium", "ARC-C"),
            ["A21N"] = new("A21N", 321, 1, "Medium", "ARC-C"),
            // Bombardier C-Series / Airbus A220 — no clean numeric family.
            ["BCS1"] = new("BCS1", 0, 0, "Medium", "ARC-C"),
            ["BCS3"] = new("BCS3", 0, 0, "Medium", "ARC-C"),
            ["A223"] = new("A223", 0, 0, "Medium", "ARC-C"),
            ["A221"] = new("A221", 0, 0, "Medium", "ARC-C"),
        };

    /// <summary>
    /// Resolves <paramref name="icaoType"/> (+ optional <paramref name="wingspanMetres"/>) to
    /// a <see cref="GsxAircraftId"/>. Always succeeds: returns <c>true</c> when idMajor was
    /// derived or matched, <c>false</c> with a still-usable best-effort id otherwise. Never throws.
    /// </summary>
    public static bool TryResolve(string? icaoType, double wingspanMetres, out GsxAircraftId id)
    {
        string key = (icaoType ?? string.Empty).Trim();
        string upper = key.ToUpperInvariant();
        string arc = wingspanMetres > 0 ? ArcFromWingspanMetres(wingspanMetres) : string.Empty;
        string grp = wingspanMetres > 0 ? CategoryFromWingspanMetres(wingspanMetres) : string.Empty;

        // 1) Exception table — irregular designators. Overlay a wingspan-derived ARC/group
        // when the caller supplied one (it's more authoritative than the table's seed, and
        // lets the same airframe resolve identically whether or not it's in the table).
        if (key.Length > 0 && ExceptionTable.TryGetValue(key, out var hit))
        {
            id = hit with
            {
                ArcCode = arc.Length > 0 ? arc : hit.ArcCode,
                Group = grp.Length > 0 ? grp : hit.Group,
            };
            return true;
        }

        // 2) Derive idMajor/idMinor from the ICAO pattern.
        bool derived = TryDeriveFromIcao(upper, out int idMajor, out int idMinor);
        id = new GsxAircraftId(upper, idMajor, idMinor, grp, arc);
        return derived;
    }

    /// <summary>Back-compat overload (no wingspan) — group/ARC left empty.</summary>
    public static bool TryResolve(string? icaoType, out GsxAircraftId id)
        => TryResolve(icaoType, 0.0, out id);

    /// <summary>
    /// Derives (idMajor, idMinor) from a raw ICAO type designator pattern. Returns <c>true</c>
    /// when a family idMajor was produced. Best-effort idMinor; unknown 4th char → 0 (safe →
    /// base offset). The raw ICAO is still preserved by the caller so ICAO-keyed tables hit.
    /// </summary>
    public static bool TryDeriveFromIcao(string icao, out int idMajor, out int idMinor)
    {
        idMajor = 0;
        idMinor = 0;
        if (string.IsNullOrEmpty(icao)) return false;
        icao = icao.ToUpperInvariant();

        // --- Boeing: B7Xd... -> idMajor = 707 + X*10 (B73->737 ... B78->787). ---
        if (icao.Length >= 4 && icao[0] == 'B' && icao[1] == '7' && char.IsDigit(icao[2]))
        {
            int x = icao[2] - '0';                 // the family digit (3..9)
            idMajor = 707 + x * 10;                // 737, 747, 757, 767, 777, 787, 797...
            char d = icao[3];
            if (char.IsDigit(d))
                idMinor = (d - '0') * 100;         // 738->800, 772->200, 789->900, 744->400
            else
                idMinor = d switch
                {
                    'W' => 300,                    // B77W (777-300ER)
                    'L' => 200,                    // B77L (777-200LR/F)
                    'X' => 10,                     // B78X (787-10)
                    _ => 0,
                };
            return true;
        }

        // --- Airbus: A3YZ. ---
        if (icao.Length >= 4 && icao[0] == 'A' && icao[1] == '3' &&
            char.IsDigit(icao[2]) && char.IsDigit(icao[3]))
        {
            int y = icao[2] - '0';
            int z = icao[3] - '0';
            if (y == 1 || y == 2)
            {
                // Narrowbody: idMajor is the literal 3-digit (A318->318 ... A321->321).
                idMajor = 300 + y * 10 + z;        // 3,1,8 -> 318
                idMinor = 0;
                return true;
            }
            if (y == 3 || y == 4 || y == 5 || y == 6 || y == 8)
            {
                // Widebody: idMajor = 300 + Y*10 (A332->330, A359->350, A388->380).
                idMajor = 300 + y * 10;
                idMinor = z * 100;                 // A332->200, A333->300, A338->800, A339->900
                return true;
            }
            return false;
        }

        // --- Embraer E-Jets: E1XX / E2XX -> idMajor = literal 3-digit body (E190->190). ---
        // ICAO designators are E170/E75L/E75S/E190/E195/E290/E295 etc. Pull the trailing
        // numeric where it's a clean 3-digit family.
        if (icao.Length >= 4 && icao[0] == 'E' && char.IsDigit(icao[1]) &&
            char.IsDigit(icao[2]) && char.IsDigit(icao[3]))
        {
            idMajor = int.Parse(icao.Substring(1, 3), CultureInfo.InvariantCulture);
            idMinor = 0;
            return true;
        }

        // No clean family pattern -> leave idMajor 0 (safe: base offset; ICAO-keyed tables
        // still hit on the raw ICAO).
        return false;
    }

    /// <summary>
    /// ICAO Annex-14 Aerodrome Reference Code letter from wingspan in METRES, returned in the
    /// "ARC-X" form scenery group tables use:
    ///   A &lt;15, B 15–&lt;24, C 24–&lt;36, D 36–&lt;52, E 52–&lt;65, F 65–&lt;80 (≥80 → F).
    /// Empty string for a non-positive wingspan.
    /// </summary>
    public static string ArcFromWingspanMetres(double wingspanMetres)
    {
        if (wingspanMetres <= 0) return string.Empty;
        char c =
            wingspanMetres < 15 ? 'A' :
            wingspanMetres < 24 ? 'B' :
            wingspanMetres < 36 ? 'C' :
            wingspanMetres < 52 ? 'D' :
            wingspanMetres < 65 ? 'E' :
            'F';                                   // 65–<80 and ≥80 both → F
        return "ARC-" + c;
    }

    /// <summary>
    /// Broad GSX-style category ("Light"/"Medium"/"Heavy"/"Super") from wingspan in METRES.
    /// A last-resort group fallback for profiles that key on the Aerosoft-style buckets rather
    /// than the ARC code. Empty for a non-positive wingspan.
    /// </summary>
    public static string CategoryFromWingspanMetres(double wingspanMetres)
    {
        if (wingspanMetres <= 0) return string.Empty;
        if (wingspanMetres < 24) return "Light";   // regional/biz (ARC-A/B)
        if (wingspanMetres < 36) return "Medium";  // narrowbody (ARC-C)
        if (wingspanMetres < 65) return "Heavy";   // widebody (ARC-D/E)
        return "Super";                            // A380/747-8-class (ARC-F)
    }
}
