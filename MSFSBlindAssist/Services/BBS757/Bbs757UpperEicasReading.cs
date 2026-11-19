namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// One read of the 757 upper EICAS, as BlueBird draws it (<see cref="Bbs757UpperEicasReader.Decode"/>):
/// <list type="bullet">
/// <item><see cref="Messages"/>: the main list in BlueBird's vector order (a cancelled entry kept, with
/// <see cref="Bbs757EicasMessage.Cancelled"/>), then the communication list top to bottom as the screen shows it.</item>
/// <item><see cref="Page"/> as stored; <see cref="PageShown"/> whether "PAGE n" is drawn (no "of n": BlueBird draws none),
/// and <see cref="PageLine"/> that line in the layout's word while it is ("PAGE 2"); <see cref="Recall"/> whether the
/// RECALL word is drawn, and <see cref="RecallLine"/> that word while it is; <see cref="RecallTime"/> as stored, so a
/// RECALL press shows as its change; <see cref="Cancelled"/> the main-list entries whose cancelled mark is set.</item>
/// <item><see cref="Values"/>: the gauge rows in screen order (EPR, N1, EGT, TAT, then TAI, REV, EPR reference, N1 max and
/// EGT max only while drawn); <see cref="ThrustReference"/> as drawn, null when nothing is drawn;
/// <see cref="Envelope"/> the in-flight start block's lines, empty when it is not drawn.</item>
/// <item><see cref="Powered"/>: the screen is lit; everything else is carried whatever the power.</item>
/// </list>
/// Every number is formatted with the invariant culture. Equality is content equality (lists by sequence, doubles by
/// <see cref="double.Equals(double)"/>): the monitor compares readings with the default comparer.
/// </summary>
public sealed record Bbs757UpperEicasReading(
    IReadOnlyList<Bbs757EicasMessage> Messages,
    int Page,
    bool PageShown,
    string? PageLine,
    bool Recall,
    string? RecallLine,
    double RecallTime,
    int Cancelled,
    IReadOnlyList<Bbs757EicasValue> Values,
    string? ThrustReference,
    IReadOnlyList<string> Envelope,
    bool Powered,
    bool Corrupt) : IBbs757Reading
{
    public bool Equals(Bbs757UpperEicasReading? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Messages.SequenceEqual(other.Messages)
                && Page == other.Page
                && PageShown == other.PageShown
                && string.Equals(PageLine, other.PageLine, StringComparison.Ordinal)
                && Recall == other.Recall
                && string.Equals(RecallLine, other.RecallLine, StringComparison.Ordinal)
                && RecallTime.Equals(other.RecallTime)
                && Cancelled == other.Cancelled
                && Values.SequenceEqual(other.Values)
                && string.Equals(ThrustReference, other.ThrustReference, StringComparison.Ordinal)
                && Envelope.SequenceEqual(other.Envelope, StringComparer.Ordinal)
                && Powered == other.Powered
                && Corrupt == other.Corrupt));

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var m in Messages) h.Add(m);
        h.Add(Page);
        h.Add(PageShown);
        h.Add(PageLine, StringComparer.Ordinal);
        h.Add(Recall);
        h.Add(RecallLine, StringComparer.Ordinal);
        h.Add(RecallTime);
        h.Add(Cancelled);
        foreach (var v in Values) h.Add(v);
        h.Add(ThrustReference, StringComparer.Ordinal);
        foreach (string line in Envelope) h.Add(line, StringComparer.Ordinal);
        h.Add(Powered);
        h.Add(Corrupt);
        return h.ToHashCode();
    }
}

/// <summary>One upper EICAS message: its id, BlueBird's text exactly (an advisory's or communication message's leading
/// space kept), its <see cref="Bbs757UpperEicasLayout.Levels"/> word, whether CANC marked it and whether the screen
/// shows it now.</summary>
public readonly record struct Bbs757EicasMessage(int Id, string Text, string Level, bool Cancelled, bool Shown);

/// <summary>One gauge row: the left and right engine's values as drawn (null where BlueBird blanks or draws nothing;
/// <see cref="Right"/> null for a one-value row such as TAT) and each one's color when it is not the normal one
/// (<see cref="Amber"/>, <see cref="Red"/>, or <see cref="Green"/> for a fully deployed reverser).</summary>
public readonly record struct Bbs757EicasValue(string Label, string? Left, string? Right, string? LeftColor, string? RightColor)
{
    public const string EprLabel = "EPR";
    public const string N1Label = "N1";
    public const string EgtLabel = "EGT";
    public const string TatLabel = "TAT";
    public const string TaiLabel = "TAI";
    public const string RevLabel = "REV";
    public const string EprReferenceLabel = "EPR reference";
    public const string N1MaxLabel = "N1 max";
    public const string EgtMaxLabel = "EGT max";

    public const string Amber = "amber";
    public const string Red = "red";
    public const string Green = "green";
}
