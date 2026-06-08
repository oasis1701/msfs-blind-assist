// GsxParkingNameEnum — helpers for the GSX SetGate_Name integer enum.
//
// GSX manual enum (SetGate_Name L-var):
//   0  = NONE
//   1  = PARKING   (generic parking — no concourse letter)
//  10  = GATE      (generic gate    — no concourse letter)
//  11  = DOCK      (dock            — no concourse letter)
//  12  = GATE_A
//  13  = GATE_B
//  14  = GATE_C
//   …  (contiguous; the manual's duplicate "18" is an OCR typo)
//  19  = GATE_H
//   …
//  37  = GATE_Z
//
// So letter 'A' maps to code 12, 'B' to 13, …, 'Z' to 37.
// Formula: code = letter – 'A' + 12  /  letter = 'A' + (code – 12).

using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx;

/// <summary>
/// Maps the GSX <c>SetGate_Name</c> integer enum to/from concourse letters,
/// and provides a <see cref="Matches"/> helper that compares a GSX-confirmed
/// gate selection to a target <see cref="ParkingSpot"/>.
/// </summary>
public static class GsxParkingNameEnum
{
    // Enum codes for the letter-less kinds.
    public const int None    = 0;
    public const int Parking = 1;
    public const int Gate    = 10;
    public const int Dock    = 11;

    // First / last codes for the lettered GATE_A..GATE_Z range.
    private const int GateACode = 12;
    private const int GateZCode = 37;   // 'Z' - 'A' + 12 = 37

    // ─────────────────────────────────────────────────────────────────────
    // Enum ↔ letter conversions.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the <c>SetGate_Name</c> enum code for a concourse letter
    /// (case-insensitive), or <see langword="null"/> if <paramref name="c"/>
    /// is not a letter in A..Z.
    /// </summary>
    public static int? LetterToEnum(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c < 'A' || c > 'Z') return null;
        return GateACode + (c - 'A');
    }

    /// <summary>
    /// Returns the concourse letter for a <c>SetGate_Name</c> enum code in
    /// the GATE_A..GATE_Z range (12..37), or <see langword="null"/> for codes
    /// outside that range (NONE, PARKING, GATE, DOCK, unknown).
    /// </summary>
    public static char? EnumToLetter(int code)
    {
        if (code < GateACode || code > GateZCode) return null;
        return (char)('A' + (code - GateACode));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Match helper.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the GSX-confirmed selection
    /// (<paramref name="setGateName"/>, <paramref name="setGateNumber"/>,
    /// <paramref name="setGateSuffix"/>) equals the target
    /// <paramref name="spot"/>.
    /// </summary>
    /// <remarks>
    /// Matching rules:
    /// <list type="bullet">
    ///   <item>Number must equal <c>spot.Number</c>.</item>
    ///   <item>Suffix must match (case-insensitive; empty == empty).</item>
    ///   <item>Concourse letter from <c>setGateName</c>:
    ///     <list type="bullet">
    ///       <item>GATE_A..GATE_Z → letter must equal the concourse letter
    ///         extracted from <c>spot.Name</c>.</item>
    ///       <item>NONE/PARKING/GATE/DOCK → no concourse letter to compare;
    ///         match on number+suffix only, which already covers pure-numeric
    ///         parking spots.</item>
    ///     </list>
    ///   </item>
    /// </list>
    /// Concourse letter extraction from <c>spot.Name</c> mirrors the
    /// gate-merger convention: strip a single leading <c>'G'</c> (e.g. "GA"
    /// → "A"), then take the first character if it is a letter; otherwise
    /// no concourse letter.
    /// </remarks>
    public static bool Matches(int setGateName, int setGateNumber, string setGateSuffix, ParkingSpot spot)
    {
        // Number must match.
        if (setGateNumber != spot.Number) return false;

        // Suffix must match (case-insensitive, null ≡ empty).
        string gSuffix = setGateSuffix ?? string.Empty;
        string sSuffix = spot.Suffix    ?? string.Empty;
        if (!string.Equals(gSuffix, sSuffix, StringComparison.OrdinalIgnoreCase)) return false;

        // Concourse letter check.
        char? gsxLetter = EnumToLetter(setGateName);

        if (gsxLetter.HasValue)
        {
            // GSX reports a specific concourse letter — must match the spot.
            char? spotLetter = ExtractConcourseLetter(spot.Name);
            if (!spotLetter.HasValue) return false;   // spot has no concourse letter
            return char.ToUpperInvariant(gsxLetter.Value) == char.ToUpperInvariant(spotLetter.Value);
        }
        else
        {
            // NONE / PARKING / GATE / DOCK — no letter to match against;
            // number+suffix agreement (already checked) is sufficient.
            return true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Private helpers.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the concourse letter from a parking-spot name string.
    /// Strips a single leading 'G' (gate-merger NormConcourse convention,
    /// e.g. "GA" → try 'A'), then returns the first character if it is a
    /// letter; otherwise returns <see langword="null"/>.
    /// </summary>
    private static char? ExtractConcourseLetter(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        string candidate = name;

        // Strip leading 'G' if the next character is also a letter
        // (e.g. "GA", "GB" → "A", "B").  A bare "G" gate stays as 'G'.
        if (candidate.Length >= 2
            && char.ToUpperInvariant(candidate[0]) == 'G'
            && char.IsLetter(candidate[1]))
        {
            candidate = candidate.Substring(1);
        }

        char first = char.ToUpperInvariant(candidate[0]);
        return char.IsLetter(first) ? first : (char?)null;
    }
}
