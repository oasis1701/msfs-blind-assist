namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// One BlueBird 757 display field's words, verbatim from the module's UTF-32 tables: the raw field value maps
/// to the word the display draws. 0 is always blank (never in the table); a non-zero value with no entry is
/// "unrecognised" (spoken, never dropped, as the PMDG reader does). The recipe stores raw field values, folding
/// the draw's <c>switch(value-1)</c> or <c>value == n</c> comparison in, so the reader is a plain lookup.
/// Equality compares the entries by content.
/// </summary>
public sealed record Bbs757WordTable(IReadOnlyDictionary<int, string> Words)
{
    public bool Equals(Bbs757WordTable? other) =>
        other is not null
        && Words.Count == other.Words.Count
        && Words.All(kv => other.Words.TryGetValue(kv.Key, out var w) && w == kv.Value);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var kv in Words.OrderBy(kv => kv.Key))
        {
            h.Add(kv.Key);
            h.Add(kv.Value, StringComparer.Ordinal);
        }
        return h.ToHashCode();
    }

    /// <summary>The word for a raw field value: null for 0 (blank), the word when the table has it, else
    /// <see cref="Unrecognised"/> for any other non-zero value.</summary>
    public string? Lookup(int value) =>
        value == 0 ? null : Words.TryGetValue(value, out var w) ? w : Unrecognised;

    public const string Unrecognised = "unrecognised";
}
