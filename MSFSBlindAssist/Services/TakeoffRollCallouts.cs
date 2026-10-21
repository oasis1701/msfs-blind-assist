using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.DA40;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The two take-off-roll speed calls an aircraft gets, decided in ONE place so every path
/// that configures <see cref="TakeoffAssistManager"/> applies the same answer.
///
/// ⚠️ THE DA40's PROFILE USED TO BE APPLIED AND NEVER TAKEN BACK. Loading the DA40 replaced
/// the airliner pair with one "Rotate" call on the app-wide manager, and nothing restored it,
/// so every aircraft flown after it for the rest of the session lost its 80 and 100 knot
/// calls; and saving Settings rebuilds the manager with the defaults, which silently took the
/// DA40's own call away. Both paths now ask this, for whatever aircraft is current.
/// </summary>
public static class TakeoffRollCallouts
{
    public readonly record struct Profile(double? LowKts, string LowPhrase, double? HighKts, string HighPhrase);

    /// <summary>The airliner pair, which every aircraft without a profile keeps.</summary>
    public static readonly Profile Default = new(80.0, "80 knots", 100.0, "100 knots");

    /// <summary>
    /// The DA40 rotates at 67 KIAS (XLS 59), so 80 and 100 land after it is flying — two
    /// announcements during the busiest ten seconds of the flight, neither marking anything.
    /// One call at Vr is what a light single wants.
    /// </summary>
    public static Profile For(IAircraftDefinition? aircraft) => aircraft switch
    {
        CowsDA40Definition da40 => new Profile(DA40Speeds.For(da40.Variant).Vr, "Rotate", null, string.Empty),
        _ => Default,
    };

    public static void Apply(TakeoffAssistManager? manager, IAircraftDefinition? aircraft)
    {
        if (manager == null) return;
        var p = For(aircraft);
        manager.ConfigureSpeedCallouts(p.LowKts, p.LowPhrase, p.HighKts, p.HighPhrase);
    }
}
