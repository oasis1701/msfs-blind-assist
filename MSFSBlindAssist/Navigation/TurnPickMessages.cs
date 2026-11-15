using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>Every sentence the Turn pick adds, composed in one place so the wording stays exact.</summary>
internal static class TurnPickMessages
{
    public static string SideWord(TurnSide side) => side == TurnSide.Left ? "left" : "right";

    public static string OffChartNote(TurnSide side, string taxiway, string distance)
        => $"Note: this route includes an off-chart {SideWord(side)} turn onto taxiway {taxiway}, {distance} with no taxi line.";

    public static string EnteringLeg(TurnSide side, string taxiway, string distance)
        => $"Off-chart leg. No taxi line for the next {distance}. Follow the tone, then turn {SideWord(side)} onto taxiway {taxiway}.";

    public static string Refused(TurnPickRefusal refusal, string destinationName)
    {
        string side = SideWord(refusal.Side);
        return refusal.Reason switch
        {
            TurnPickOutcome.NoTurnFound =>
                $"Note: no {side} turn onto taxiway {refusal.TaxiwayName} was found here. The route keeps the charted turn.",
            TurnPickOutcome.DoesNotLead =>
                $"Note: turning {side} onto taxiway {refusal.TaxiwayName} does not lead to {destinationName}. The route keeps the charted turn.",
            TurnPickOutcome.LongDetour =>
                $"Note: turning {side} onto taxiway {refusal.TaxiwayName} would be a long detour to {destinationName}. The route keeps the charted turn.",
            _ => "",
        };
    }

    public static string ChartedNote(TurnSide side, string taxiway)
        => $"Turning {SideWord(side)} onto taxiway {taxiway}, as picked.";

    public static string CouldNotBeKept(TurnSide side, string taxiway)
        => $"The {SideWord(side)} turn onto taxiway {taxiway} could not be kept.";

    public static string Missed(TurnSide side, string taxiway)
        => $"The {SideWord(side)} turn onto taxiway {taxiway} was missed.";

    /// The notes for one load, in row order, joined with a space; null when there are none: each
    /// refusal, each off-chart leg, and ChartedNote for each applied pick with ChangedRoute and not
    /// IsOffChart. A pick with ChangedRoute false says nothing. The same text goes into the summary and
    /// the start-of-guidance utterance: a Turn value left over from an earlier route can turn this one,
    /// and the summary alone is routinely cut off by the first callout.
    public static string? ComposeLoadNote(IReadOnlyList<AppliedTurnPick> applied,
        IReadOnlyList<TurnPickRefusal> refusals, string destinationName, Func<double, string> formatDistance)
    {
        var notes = new List<(int Row, string Text)>();
        foreach (var r in refusals)
        {
            string text = Refused(r, destinationName);
            if (text.Length > 0) notes.Add((r.RowIndex, text));
        }
        foreach (var p in applied)
        {
            if (!p.ChangedRoute) continue;
            if (p.IsOffChart)
                notes.Add((p.RowIndex, OffChartNote(p.Side, p.TaxiwayName, formatDistance(p.OffChartLengthMeters))));
            else
                notes.Add((p.RowIndex, ChartedNote(p.Side, p.TaxiwayName)));
        }
        if (notes.Count == 0) return null;
        return string.Join(" ", notes.OrderBy(n => n.Row).Select(n => n.Text));
    }
}
