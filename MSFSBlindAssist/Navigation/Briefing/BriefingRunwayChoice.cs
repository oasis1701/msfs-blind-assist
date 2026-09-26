// MSFSBlindAssist/Navigation/Briefing/BriefingRunwayChoice.cs
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The runway one briefing leg is planned to, and the note saying where it came from (null: the flight plan's
/// runway, with SayIntentions not consulted).</summary>
public readonly record struct BriefingRunway(string Runway, string? Note);

/// <summary>
/// Which runway a taxi leg is briefed to. When SayIntentions is flying this flight and has assigned a runway
/// (<c>current_flight.flight_plan_departing_runway</c> / <c>flight_plan_arriving_runway</c>, SAPI's "assigned departure /
/// arrival runway"), that runway wins — it is the one its controllers will clear the pilot to — and the leg says where
/// it came from. Live KMEM→KATL (2026-09-26): SayIntentions assigned 36L while SimBrief planned 18R, a runway
/// SayIntentions was not using, and the briefing routed the taxi-out to 18R. Otherwise the flight plan's runway
/// (SimBrief's <c>plan_rwy</c>, or the pilot's own EFB pick) stands, as before.
/// </summary>
public static class BriefingRunwayChoice
{
    public const string AgreesNote = "SayIntentions has assigned this runway too";

    public static BriefingRunway Choose(string? planRunway, string? siRunway, bool siIsThisFlight)
    {
        string plan = planRunway?.Trim() ?? "";
        string si = siRunway?.Trim() ?? "";
        if (!siIsThisFlight || si.Length == 0) return new BriefingRunway(plan, null);
        if (plan.Length == 0)
            return new BriefingRunway(si, $"runway {si} is the runway SayIntentions assigned; the flight plan names no runway");
        return RunwayIdsMatch(plan, si)
            ? new BriefingRunway(plan, AgreesNote)
            : new BriefingRunway(si, $"runway {si} is the runway SayIntentions assigned; the flight plan names {plan}");
    }

    /// <summary>The same runway END: "9" and "09" are one runway. CleanRunway pads the number; NormalizeDesignator is the
    /// fallback for a compass-point designator ("N", "NE") it cannot parse.</summary>
    private static bool RunwayIdsMatch(string a, string b) =>
        string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    private static string Canon(string d) =>
        SayIntentionsClearanceParser.CleanRunway(d) ?? RouteRunwayCrossings.NormalizeDesignator(d);
}
