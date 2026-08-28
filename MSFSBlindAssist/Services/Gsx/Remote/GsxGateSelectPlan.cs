using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// The order in which <c>gate.select</c>'s <c>gate</c> argument is attempted for a stand.
///
/// <para>Live-probed against a running GSX (KATL, PMDG 737, 2026-08-27). Of everything
/// GSX publishes per parking, NOTHING textual resolves: <c>uiGateName</c> verbatim, the
/// trimmed form, and <c>uiName</c> all return <c>not_found</c>. Only a stand NUMBER sent
/// as a JSON int, or a <c>bglName</c>, resolve — and <c>bglName</c> is absent from
/// <c>handlerData.airport.parkings</c>, reaching a client only inside an <c>ambiguous</c>
/// reply. Hence: number first, then the candidate's own <c>bglName</c>, then the verbatim
/// identifier as a last resort so nothing is worse than before.</para>
///
/// <para>The "verbatim identifier" rule is unbroken. Its purpose is that the app must
/// never rebuild a label out of its OWN parsed fields (<c>Name</c>/<c>Number</c>/
/// <c>Suffix</c>/<c>Describe()</c>) — that is how the wrong stand gets selected. A number
/// GSX published and a <c>bglName</c> GSX handed back are GSX's strings, not ours.</para>
/// </summary>
public static class GsxGateSelectPlan
{
    /// <summary>
    /// The first <c>gate</c> value to send: the stand's number (boxed <see cref="int"/>)
    /// when it has one, else the verbatim identifier, else null when nothing can be sent
    /// at all.
    /// </summary>
    public static object? FirstAttempt(ParkingSpot? spot)
    {
        // A spot with NO GsxIdentifier has no attempt AT ALL -- not even its number, and
        // this guard must not be "simplified" into a plain Number > 0 first test.
        //
        // Only GsxRemoteParkingReader populates GsxIdentifier, so a spot without one came
        // from the navdata/.ini fallback, and CLAUDE.md holds that such a list "cannot be
        // auto-selected -- gate.select degrades to BadArgs, i.e. to manual selection, which
        // is the pre-existing baseline and the intended degradation".
        //
        // Its number is the LEAST trustworthy number in the app to send: navdata's stand
        // numbering is the scenery author's BGL parking name, which disagrees with GSX on 46
        // of 222 KJFK stands. And such a spot carries no GsxUiName either, so if the number
        // did happen to resolve uniquely, GSX would prepare a stand nothing could check --
        // GsxGateSelectResult.ResolvedGateContradictsRequest has no fully-qualified name to
        // compare against, and GsxGateCandidateMatcher cannot resolve an ambiguity either.
        // Silent wrong-stand selection is exactly the failure this whole path exists to avoid.
        if (spot is null || string.IsNullOrWhiteSpace(spot.GsxIdentifier)) return null;

        return spot.Number > 0 ? spot.Number : spot.GsxIdentifier;
    }

    /// <summary>
    /// The last-resort <c>gate</c> value — today's behaviour, the verbatim identifier —
    /// or null when <see cref="FirstAttempt"/> already WAS that value and there is nothing
    /// further to try.
    /// </summary>
    public static string? FallbackAttempt(ParkingSpot? spot)
    {
        if (spot is null || spot.Number <= 0) return null;
        return string.IsNullOrWhiteSpace(spot.GsxIdentifier) ? null : spot.GsxIdentifier;
    }
}
