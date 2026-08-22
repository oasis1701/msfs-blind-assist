using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Marks which stands in a gate list must SPEAK their GSX terminal name — exactly the
/// ones whose identity (concourse letter, number, suffix) another stand in the same list
/// shares, so the terminal is the only thing telling the two apart. Sets
/// <see cref="ParkingSpot.TerminalNameDisambiguates"/> on those and clears it on the rest.
///
/// <para>
/// Run at the END of <c>GateDataSource</c>'s Remote API path, after the concourse-letter
/// filler: identity is only final then (KJFK's five "Gate 2" become "2", "B2", … once the
/// letters are borrowed, and only the ones that STILL collide need the terminal). Grouped
/// on the identity a pilot hears, not on <c>uiGateName</c>: two stands the filler lettered
/// apart are no longer ambiguous even though GSX named them identically.
/// </para>
///
/// <para>
/// Why a flag rather than blanking <see cref="ParkingSpot.TerminalName"/>: the terminal is
/// DATA the concourse-letter filler and future readouts use; only its place in the spoken
/// label is conditional. See <see cref="ParkingSpot.TerminalNameDisambiguates"/> for the
/// EHAM section-header case that made this necessary. Pure; pinned by
/// GateDataSourceRoutingTests.
/// </para>
/// </summary>
public static class GsxTerminalDisambiguator
{
    public static void Mark(IReadOnlyList<ParkingSpot> spots)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in spots)
            counts[IdentityKey(s)] = counts.TryGetValue(IdentityKey(s), out int n) ? n + 1 : 1;

        foreach (var s in spots)
            s.TerminalNameDisambiguates = counts[IdentityKey(s)] > 1;
    }

    private static string IdentityKey(ParkingSpot s) =>
        (s.Name ?? string.Empty).Trim().ToUpperInvariant() + "\u001f"
        + s.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u001f"
        + (s.Suffix ?? string.Empty).Trim().ToUpperInvariant();
}
