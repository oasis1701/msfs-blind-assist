namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The four parts of the 737 FMA, in PFD order.</summary>
public enum FmaColumn { AutoThrottle, Roll, Pitch, Status }

/// <summary>
/// One FMA column's words, verbatim from PMDG's own UTF-32 table: the word for a value v is
/// <c>Words[v - FirstValue]</c> when <c>FirstValue &lt;= v &lt; FirstValue + Words.Count</c>.
/// <see cref="ValueOneWord"/> is "CWS P" for pitch and "CWS R" for roll (value 1, which the tables do
/// not hold), else null. Equality compares the words by value.
/// </summary>
public sealed record FmaColumnWords(int FirstValue, IReadOnlyList<string> Words, string? ValueOneWord)
{
    public bool Equals(FmaColumnWords? other) =>
        other is not null
        && FirstValue == other.FirstValue
        && ValueOneWord == other.ValueOneWord
        && Words.SequenceEqual(other.Words, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(FirstValue);
        h.Add(ValueOneWord, StringComparer.Ordinal);
        foreach (var w in Words) h.Add(w, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>
/// Where one installed 737NG3.wasm keeps the FMA, all from that file's layout file, never
/// hardcoded. Addresses are wasm linear-memory addresses; field
/// offsets are from PMDG's autopilot object (<see cref="Fd1"/> and <see cref="Fd2"/> are read as bytes, the
/// rest as i32). Equality compares <see cref="Anchor"/> by content and ignores <see cref="SourceFile"/>,
/// so the same build installed twice gives one layout.
/// </summary>
public sealed record Pmdg737FmaLayout(
    string SourceFile,
    uint AnchorAddress,
    byte[] Anchor,
    uint InstanceGlobal,
    uint ObjectSize,
    uint DisplayGlobal,
    int FmaAp,
    int FmaAt,
    int Pitch,
    int PitchArmed,
    int Roll,
    int RollArmed,
    int Fd1,
    int Fd2,
    int McpAlt,
    int McpHdg,
    FmaColumnWords AtWords,
    FmaColumnWords RollWords,
    FmaColumnWords PitchWords,
    FmaColumnWords StatusWords)
{
    public bool Equals(Pmdg737FmaLayout? other) =>
        other is not null
        && AnchorAddress == other.AnchorAddress
        && Anchor.AsSpan().SequenceEqual(other.Anchor)
        && InstanceGlobal == other.InstanceGlobal
        && ObjectSize == other.ObjectSize
        && DisplayGlobal == other.DisplayGlobal
        && FmaAp == other.FmaAp
        && FmaAt == other.FmaAt
        && Pitch == other.Pitch
        && PitchArmed == other.PitchArmed
        && Roll == other.Roll
        && RollArmed == other.RollArmed
        && Fd1 == other.Fd1
        && Fd2 == other.Fd2
        && McpAlt == other.McpAlt
        && McpHdg == other.McpHdg
        && AtWords.Equals(other.AtWords)
        && RollWords.Equals(other.RollWords)
        && PitchWords.Equals(other.PitchWords)
        && StatusWords.Equals(other.StatusWords);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnchorAddress);
        h.AddBytes(Anchor);
        h.Add(InstanceGlobal);
        h.Add(ObjectSize);
        h.Add(DisplayGlobal);
        h.Add(FmaAp);
        h.Add(FmaAt);
        h.Add(Pitch);
        h.Add(PitchArmed);
        h.Add(Roll);
        h.Add(RollArmed);
        h.Add(Fd1);
        h.Add(Fd2);
        h.Add(McpAlt);
        h.Add(McpHdg);
        h.Add(AtWords);
        h.Add(RollWords);
        h.Add(PitchWords);
        h.Add(StatusWords);
        return h.ToHashCode();
    }
}

/// <summary>The FMA's result in a layout file: a layout, or the detailed reason (for the log) there is none.
/// The spoken reason for a missing layout is always <see cref="FmaReasons.FileNotRecognised"/>.</summary>
public sealed record FmaDerivation(Pmdg737FmaLayout? Layout, string? Failure);

/// <summary>The one list of spoken reasons why FMA announcements are unavailable or stopped.</summary>
public static class FmaReasons
{
    public const string FileNotRecognised = "PMDG file not recognised";
    public const string FilesNotFound = "PMDG 737 files not found";
    public const string MemoryNotFound = "aircraft memory not found";
    public const string NoAccess = "no access to the simulator";
    public const string Mismatch = "values do not match the SDK";

    /// <summary>The primary flight display's check is against the simulator's airspeed and altitude, not the SDK.</summary>
    public const string SimulatorMismatch = "values do not match the simulator";
    public const string UnexpectedValues = "unexpected values";
    public const string InternalError = "internal error, see the log";
}
