namespace MSFSBlindAssist.FirstOfficer.A300;

/// <summary>
/// One line the First Officer performs, the SAME in its flow and its action group (the two can never drift): what it
/// writes, and the field that says it is done. A test or other sequence has no field: it is done when it has run.
/// </summary>
public sealed record A300FoLine(string Id, string Label, string? Field, Func<double, bool>? Done,
    IReadOnlyList<(string Key, int Target)> Writes, string? SequenceKey)
{
    private static bool Is(double v, double p) => Math.Abs(v - p) < 0.5;

    /// <summary>The line ticks itself on its field and then stays ticked (<see cref="Starter"/>); its flow step
    /// writes but never ticks it, since a write is not yet a running engine.</summary>
    public bool LatchesOnField { get; init; }

    /// <summary>An engine's starter: it writes the starter ON and reads done on the engine RUNNING (N2 at
    /// <see cref="A300FoStateEvaluator.EngineRunningN2"/>), never on the button, which the start valve's closing may
    /// put back off; the line latches, as the fleet's start selectors and the MD-11's START switches do ([FO-8]).</summary>
    public static A300FoLine Starter(string id, string label, string key, string n2Field)
        => new(id, label, n2Field, v => v >= A300FoStateEvaluator.EngineRunningN2, new[] { (key, 1) }, null)
        {
            LatchesOnField = true,
        };

    /// <summary>One row to one position.</summary>
    public static A300FoLine Row(string id, string label, string key, int target)
        => new(id, label, key, v => Is(v, target), new[] { (key, target) }, null);

    /// <summary>Several rows, read back as one <see cref="A300FoKeys.AllAt"/> field.</summary>
    public static A300FoLine Rows(string id, string label, string allAtField)
        => new(id, label, allAtField, v => v >= 0.5, A300FoKeys.AllAt[allAtField], null);

    /// <summary>A button read back on what it changes (<see cref="A300FoKeys.PressConfirmations"/>).</summary>
    public static A300FoLine Press(string id, string label, string key)
    {
        var confirm = A300FoKeys.PressConfirmations[key];
        return new(id, label, confirm.Field, confirm.Done, new[] { (key, 1) }, null);
    }

    /// <summary>A system test or other executor sequence.</summary>
    public static A300FoLine Sequence(string id, string label, string sequenceKey)
        => new(id, label, null, null, Array.Empty<(string, int)>(), sequenceKey);
}
