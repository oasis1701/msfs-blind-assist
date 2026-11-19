namespace MSFSBlindAssist.Services.PMDG;

/// <summary>The six FMA values read from the 737: raw numbers, as PMDG stores them.</summary>
public readonly record struct FmaState(int At, int Roll, int RollArmed, int Pitch, int PitchArmed, int Status);

/// <summary>What a raw FMA value means for one column.</summary>
public enum FmaWordKind { Blank, Word, NoWord, Corrupt }

/// <summary>
/// Composes what is spoken for the 737 FMA. The words are PMDG's own printed text, verbatim, from the
/// layout derived from the installed file; they are never rewritten for speech. Pure and culture
/// independent (no numbers are formatted).
/// </summary>
public static class Pmdg737FmaSpeech
{
    public const string Starting = "FMA reader starting";
    public const string WaitingForSdk = "FMA reader waiting for PMDG SDK data";
    public const string Resumed = "FMA announcements resumed";

    public static string Unavailable(string reason) => $"FMA announcements unavailable: {reason}";
    public static string Stopped(string reason) => $"FMA announcements stopped: {reason}";

    /// <summary>What every FMA change the monitor speaks starts with ("FMA: Roll: VOR/LOC"), so the pilot
    /// can tell it from the MCP lights and other call-outs (owner, 2026-10-23). The whole-FMA readout
    /// (<see cref="DescribeAll"/>), the status lines and "resumed, ..." already say FMA and never get it.</summary>
    public const string ChangePrefix = "FMA: ";

    /// <summary>A <see cref="DescribeChange"/> sentence as the monitor speaks it on its own.</summary>
    public static string ChangeAnnouncement(string change) => ChangePrefix + change;

    /// <summary>0 is blank; a value in the column's table is its word; 1 is the column's value-one word when it has one;
    /// any other positive value up to 8 beyond the table is a value with no word; anything else is corrupt.</summary>
    public static (FmaWordKind Kind, string? Text) Lookup(FmaColumnWords words, int value)
    {
        if (value == 0) return (FmaWordKind.Blank, null);
        if (value < 0) return (FmaWordKind.Corrupt, null);
        int end = words.FirstValue + words.Words.Count;
        if (value >= words.FirstValue && value < end)
            return (FmaWordKind.Word, words.Words[value - words.FirstValue]);
        if (value == 1 && words.ValueOneWord is not null)
            return (FmaWordKind.Word, words.ValueOneWord);
        if (value < end + 8) return (FmaWordKind.NoWord, null);
        return (FmaWordKind.Corrupt, null);
    }

    /// <summary>The changed parts in PFD order, or null when nothing speakable changed. A new value with no
    /// word is "unrecognised", never dropped (a TO/GA go-around from LOC sets roll to a value with no word,
    /// and silence would leave the pilot believing LOC); a corrupt one is never spoken.</summary>
    public static string? DescribeChange(FmaState before, FmaState after, Pmdg737FmaLayout layout)
    {
        var parts = new List<string>(6);
        AddChange(parts, "A/T", layout.AtWords, before.At, after.At);
        AddChange(parts, "Roll", layout.RollWords, before.Roll, after.Roll);
        if (!ArmedBecameActive(before.RollArmed, after.RollArmed, before.Roll, after.Roll))
            AddChange(parts, "Roll armed", layout.RollWords, before.RollArmed, after.RollArmed);
        AddChange(parts, "Pitch", layout.PitchWords, before.Pitch, after.Pitch);
        if (!ArmedBecameActive(before.PitchArmed, after.PitchArmed, before.Pitch, after.Pitch))
            AddChange(parts, "Pitch armed", layout.PitchWords, before.PitchArmed, after.PitchArmed);
        AddChange(parts, "AFDS", layout.StatusWords, before.Status, after.Status);
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The four columns in PFD order ("A/T: MCP SPD", "Roll: HDG SEL, armed VOR/LOC", …), for the PFD
    /// window's rows.</summary>
    public static IReadOnlyList<string> Columns(FmaState s, Pmdg737FmaLayout layout) => new[]
    {
        Column("A/T", layout.AtWords, s.At, null, 0),
        Column(RollLabel, layout.RollWords, s.Roll, layout.RollWords, s.RollArmed),
        Column("Pitch", layout.PitchWords, s.Pitch, layout.PitchWords, s.PitchArmed),
        Column("AFDS", layout.StatusWords, s.Status, null, 0),
    };

    /// <summary>The roll column's label in <see cref="Columns"/>.</summary>
    public const string RollLabel = "Roll";

    /// <summary>A column of <see cref="Columns"/> with its armed part (if any) replaced by <paramref name="armed"/>: the
    /// PFD window's roll row takes the armed text PMDG draws.</summary>
    public static string WithArmed(string column, string armed)
    {
        int at = column.IndexOf(ArmedPart, StringComparison.Ordinal);
        return (at < 0 ? column : column[..at]) + ArmedPart + armed;
    }

    /// <summary>What joins a column's armed word to its active one.</summary>
    private const string ArmedPart = ", armed ";

    /// <summary>The whole FMA, one sentence per column.</summary>
    public static string DescribeAll(FmaState s, Pmdg737FmaLayout layout) => string.Join(". ", Columns(s, layout)) + ".";

    private static bool ArmedBecameActive(int armedBefore, int armedAfter, int activeBefore, int activeAfter) =>
        armedBefore != armedAfter && armedAfter == 0 && armedBefore != 0
        && activeAfter != activeBefore && activeAfter == armedBefore;

    private static void AddChange(List<string> parts, string label, FmaColumnWords words, int before, int after)
    {
        if (before == after) return;
        var (kind, text) = Lookup(words, after);
        switch (kind)
        {
            case FmaWordKind.Blank: parts.Add($"{label}: none"); break;
            case FmaWordKind.Word: parts.Add($"{label}: {text}"); break;
            case FmaWordKind.NoWord: parts.Add($"{label}: {Unrecognised}"); break;
        }
    }

    private static string Column(string label, FmaColumnWords words, int value, FmaColumnWords? armedWords, int armedValue)
    {
        var s = $"{label}: {Spoken(words, value)}";
        if (armedWords is not null)
        {
            var (kind, text) = Lookup(armedWords, armedValue);
            if (kind != FmaWordKind.Blank) s += ArmedPart + (kind == FmaWordKind.Word ? text : Unrecognised);
        }
        return s;
    }

    private static string Spoken(FmaColumnWords words, int value)
    {
        var (kind, text) = Lookup(words, value);
        return kind switch
        {
            FmaWordKind.Blank => "none",
            FmaWordKind.Word => text!,
            _ => Unrecognised,
        };
    }

    /// <summary>How a value with no word is spoken, in a change and in the readout.</summary>
    private const string Unrecognised = "unrecognised";
}
